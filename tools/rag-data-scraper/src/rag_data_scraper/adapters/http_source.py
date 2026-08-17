from __future__ import annotations

import hashlib
import io
from dataclasses import replace
from pathlib import PurePosixPath
from typing import Any, Iterable, Mapping, Optional
from urllib.parse import urljoin, urlsplit, urlunsplit
from zipfile import BadZipFile, ZipFile

from bs4 import BeautifulSoup

from .base import BaseAdapter, CrawlResult, NotModifiedResult
from ..crawler.policy import CrawlPolicy
from ..http_fetcher import (
    HttpStatusError,
    SafeHttpFetcher,
    UnsafeUrlError,
    UrlPolicy,
)


class UnsupportedContentTypeError(ValueError):
    pass


class HttpSourceAdapter(BaseAdapter):
    """Deep module shared by web sources: fetch, classify, link, and provenance."""

    def __init__(
        self,
        *,
        source_id: str,
        source_namespace: str,
        authority_namespace: Optional[str],
        identity_strategy: str,
        allowed_hosts: Iterable[str],
        key_prefix: str,
        user_agent: str,
        timeout_seconds: float,
        max_response_bytes: int,
        max_attempts: int = 3,
        backoff_base_seconds: float = 0.5,
        max_backoff_seconds: float = 8.0,
        per_host_delay_seconds: float = 0.2,
        per_host_max_concurrent: int = 2,
        allow_related_asset_hosts: bool = False,
        fetcher: Optional[SafeHttpFetcher] = None,
    ) -> None:
        self._source_id = source_id
        self._source_namespace = source_namespace
        self._authority_namespace = authority_namespace
        self._identity_strategy = identity_strategy
        self._key_prefix = key_prefix
        self._max_response_bytes = max_response_bytes
        self._crawl_policy = CrawlPolicy()
        self._fetcher = fetcher or SafeHttpFetcher(
            UrlPolicy(
                allowed_hosts,
                allow_related_asset_hosts=allow_related_asset_hosts,
            ),
            timeout_seconds=timeout_seconds,
            max_response_bytes=max_response_bytes,
            max_attempts=max_attempts,
            backoff_base_seconds=backoff_base_seconds,
            max_backoff_seconds=max_backoff_seconds,
            per_host_delay_seconds=per_host_delay_seconds,
            per_host_max_concurrent=per_host_max_concurrent,
            user_agent=user_agent,
        )

    @property
    def source_id(self) -> str:
        return self._source_id

    @property
    def source_namespace(self) -> str:
        return self._source_namespace

    @property
    def authority_namespace(self) -> Optional[str]:
        return self._authority_namespace

    @property
    def default_identity_strategy(self) -> str:
        return self._identity_strategy

    def _effective_source_namespace(self, final_url: str) -> str:
        return self.source_namespace

    def _metadata_for_html(
        self,
        soup: BeautifulSoup,
        final_url: str,
    ) -> dict[str, Any]:
        return {"source_url": final_url}

    def _html_canonical_key(
        self,
        canonical_url: str,
        metadata: Mapping[str, Any],
    ) -> str:
        return self._url_key("web", canonical_url)

    def _embedded_documents_for_html(
        self,
        soup: BeautifulSoup,
        final_url: str,
    ) -> list[CrawlResult]:
        return []

    def _is_discovery_only_html(
        self,
        soup: BeautifulSoup,
        final_url: str,
    ) -> bool:
        return False

    @staticmethod
    def _should_follow_anchor(anchor) -> bool:
        href = anchor.get("href")
        if not isinstance(href, str) or not href.strip():
            return False
        if href.strip().lower().startswith(
            ("#", "data:", "javascript:", "mailto:", "tel:")
        ):
            return False
        ignored_tags = {"aside", "footer", "header", "nav"}
        ignored_tokens = {
            "advertisement",
            "breadcrumb",
            "cookie",
            "footer",
            "header",
            "menu",
            "navbar",
            "social",
        }
        for element in (anchor, *anchor.parents):
            if getattr(element, "name", None) in ignored_tags:
                return False
            attributes = getattr(element, "attrs", {}) or {}
            raw_tokens = [attributes.get("id", "")]
            classes = attributes.get("class", [])
            raw_tokens.extend(classes if isinstance(classes, list) else [classes])
            tokens = {
                token.lower()
                for value in raw_tokens
                for token in str(value).replace("_", "-").split("-")
                if token
            }
            if tokens & ignored_tokens:
                return False
            if getattr(element, "name", None) == "body":
                break
        return True

    def _url_key(self, kind: str, canonical_url: str) -> str:
        digest = hashlib.sha256(canonical_url.encode("utf-8")).hexdigest()[:24]
        return f"{self._key_prefix}_{kind}:{digest}"

    def _normalize_discovered_url(
        self,
        base_url: str,
        raw_href: str,
    ) -> Optional[str]:
        candidate = urljoin(base_url, raw_href)
        parsed = urlsplit(candidate)
        hostname = (parsed.hostname or "").lower().rstrip(".")
        if (
            parsed.scheme.lower() == "http"
            and hostname in self._fetcher.policy.allowed_hosts
        ):
            candidate = urlunsplit(
                ("https", parsed.netloc, parsed.path, parsed.query, "")
            )
        try:
            safe = self._fetcher.policy.normalize_and_validate(candidate)
        except (TypeError, UnsafeUrlError):
            return None
        return self._crawl_policy.canonicalize(safe)

    def _canonical_url(self, soup: BeautifulSoup, final_url: str) -> str:
        canonical = soup.find("link", rel=lambda value: value and "canonical" in value)
        href = canonical.get("href") if canonical else None
        candidate = urljoin(final_url, href) if isinstance(href, str) and href else final_url
        try:
            safe = self._fetcher.policy.normalize_and_validate(candidate)
        except (TypeError, UnsafeUrlError):
            safe = final_url
        return self._crawl_policy.canonicalize(safe)

    def _parse_html(
        self,
        content: bytes,
        final_url: str,
    ) -> tuple[
        str,
        str,
        dict[str, Any],
        list[str],
        list[CrawlResult],
        bool,
    ]:
        soup = BeautifulSoup(content, "lxml")
        title = (
            soup.title.string.strip()
            if soup.title and soup.title.string
            else PurePosixPath(urlsplit(final_url).path).name or final_url
        )
        canonical_url = self._canonical_url(soup, final_url)
        metadata = self._metadata_for_html(soup, canonical_url)
        metadata["source_url"] = canonical_url
        embedded_documents = self._embedded_documents_for_html(
            soup,
            canonical_url,
        )
        embedded_links = {
            link
            for document in embedded_documents
            for link in document.discovered_links
        }
        links: list[str] = []
        for anchor in soup.find_all("a", href=True):
            if not self._should_follow_anchor(anchor):
                continue
            discovered = self._normalize_discovered_url(
                final_url,
                str(anchor["href"]),
            )
            if discovered and discovered not in embedded_links:
                links.append(discovered)
        return (
            title,
            canonical_url,
            metadata,
            list(dict.fromkeys(links)),
            embedded_documents,
            self._is_discovery_only_html(soup, canonical_url),
        )

    def _classify(self, content_type: str, final_url: str, content: bytes) -> str:
        lowered_type = content_type.lower().split(";", 1)[0].strip()
        suffix = PurePosixPath(urlsplit(final_url).path.lower()).suffix
        if (
            b"%PDF-" in content[:1024]
            or lowered_type == "application/pdf"
            or suffix == ".pdf"
        ):
            if b"%PDF-" not in content[:1024]:
                raise UnsupportedContentTypeError(
                    "PDF response does not contain a valid PDF header"
                )
            return "application/pdf"
        if "wordprocessingml" in lowered_type or suffix == ".docx":
            if not content.startswith(b"PK"):
                raise UnsupportedContentTypeError("DOCX response is not a ZIP package")
            try:
                with ZipFile(io.BytesIO(content)) as archive:
                    members = archive.infolist()
                    names = {member.filename for member in members}
                    if not {"[Content_Types].xml", "word/document.xml"}.issubset(names):
                        raise UnsupportedContentTypeError(
                            "DOCX response is missing required package members"
                        )
                    if (
                        len(members) > 10_000
                        or sum(member.file_size for member in members)
                        > self._max_response_bytes * 8
                    ):
                        raise UnsupportedContentTypeError(
                            "DOCX expanded package exceeds safety limits"
                        )
            except BadZipFile as exc:
                raise UnsupportedContentTypeError(
                    "DOCX response is not a valid ZIP package"
                ) from exc
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        if lowered_type == "application/msword" or suffix == ".doc":
            if not (
                content.startswith(b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1")
                or content.lstrip().startswith(b"{\\rtf")
            ):
                raise UnsupportedContentTypeError(
                    "legacy DOC response is neither OLE Compound File nor RTF"
                )
            return "application/msword"
        if lowered_type.startswith(("image/", "audio/", "video/")):
            return lowered_type
        if suffix in {".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp"}:
            return "image/unsupported"
        html_prefix = content.lstrip()[:256].lower()
        if (
            lowered_type in {"text/html", "application/xhtml+xml", ""}
            or html_prefix.startswith((b"<!doctype html", b"<html", b"<?xml"))
        ):
            return "text/html"
        raise UnsupportedContentTypeError(
            f"Unsupported response content type: {lowered_type or 'unknown'}"
        )

    async def fetch_and_parse(self, url: str) -> Optional[CrawlResult]:
        result = await self.fetch_and_parse_conditional(url, {})
        if isinstance(result, NotModifiedResult):
            raise RuntimeError("Unexpected 304 without conditional request headers")
        return result

    async def fetch_and_parse_conditional(
        self,
        url: str,
        request_headers: Mapping[str, str],
    ) -> CrawlResult | NotModifiedResult:
        response = await self._fetcher.get(url, headers=request_headers)
        if response.status_code == 304:
            return NotModifiedResult(
                requested_url=response.requested_url,
                final_url=response.final_url,
                response_headers=response.headers,
                attempt_count=response.attempt_count,
                elapsed_ms=response.elapsed_ms,
            )
        if response.status_code != 200:
            raise HttpStatusError(
                response.status_code,
                response.final_url,
                attempt_count=response.attempt_count,
                elapsed_ms=response.elapsed_ms,
            )

        final_url = self._crawl_policy.canonicalize(response.final_url)
        mime_type = self._classify(
            response.headers.get("Content-Type", ""),
            final_url,
            response.content,
        )
        metadata: dict[str, Any] = {
            "source_url": final_url,
            "requested_url": response.requested_url,
            "redirect_chain": list(response.redirect_chain),
        }
        links: list[str] = []
        title = PurePosixPath(urlsplit(final_url).path).name or final_url

        if mime_type == "text/html":
            (
                title,
                canonical_url,
                html_metadata,
                links,
                embedded_documents,
                discovery_only,
            ) = self._parse_html(response.content, final_url)
            metadata.update(html_metadata)
            canonical_key = self._html_canonical_key(canonical_url, metadata)
            final_url = canonical_url
        else:
            embedded_documents = []
            discovery_only = False
            kind = "media" if mime_type.startswith(("image/", "audio/", "video/")) else (
                "pdf" if mime_type == "application/pdf" else
                "docx" if "wordprocessingml" in mime_type else "doc"
            )
            canonical_key = self._url_key(kind, final_url)

        return CrawlResult(
            url=final_url,
            canonical_key=canonical_key,
            title=title,
            html_or_bytes=response.content,
            mime_type=mime_type,
            document_identity_strategy=self.default_identity_strategy,
            source_namespace=self._effective_source_namespace(final_url),
            authority_namespace=self.authority_namespace,
            metadata=metadata,
            discovered_links=links,
            requested_url=response.requested_url,
            final_url=final_url,
            http_status=response.status_code,
            response_headers=response.headers,
            attempt_count=response.attempt_count,
            elapsed_ms=response.elapsed_ms,
            embedded_documents=embedded_documents,
            discovery_only=discovery_only,
        )

    def rehydrate_cached_result(self, result: CrawlResult) -> CrawlResult:
        if result.mime_type.lower() != "text/html":
            return result
        (
            title,
            canonical_url,
            html_metadata,
            links,
            embedded_documents,
            discovery_only,
        ) = self._parse_html(
            result.html_or_bytes,
            result.final_url or result.url,
        )
        metadata = {**result.metadata, **html_metadata}
        return replace(
            result,
            url=canonical_url,
            final_url=canonical_url,
            title=title,
            canonical_key=self._html_canonical_key(canonical_url, metadata),
            metadata=metadata,
            discovered_links=links,
            embedded_documents=embedded_documents,
            discovery_only=discovery_only,
        )

    async def aclose(self) -> None:
        await self._fetcher.aclose()
