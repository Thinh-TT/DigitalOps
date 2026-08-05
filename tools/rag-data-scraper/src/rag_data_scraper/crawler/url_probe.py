from __future__ import annotations

from collections import deque
from dataclasses import asdict, dataclass
import time
from typing import Any, Iterable

from ..adapters.base import BaseAdapter
from ..adapters.http_source import UnsupportedContentTypeError
from ..http_fetcher import (
    FetchRequestError,
    HttpStatusError,
    ResponseTooLargeError,
    UnsafeUrlError,
)
from .policy import CrawlPolicy


@dataclass(frozen=True)
class ProbeIssue:
    code: str
    url: str
    message: str


@dataclass(frozen=True)
class UrlProbeSummary:
    status: str
    count_mode: str
    seed_count: int
    pages_scanned: int
    listing_pages_scanned: int
    listing_pages_detected: int
    pagination_pages_detected: int
    pagination_pages_followed: int
    max_pagination_pages: int
    documents_detected: int
    attachments_detected: int
    pagination_limit_reached: bool
    duration_ms: int
    sample_titles: tuple[str, ...]
    issues: tuple[ProbeIssue, ...]

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


class UrlProbeService:
    """Bounded, read-only discovery pass for crawl planning."""

    def __init__(
        self,
        adapter: BaseAdapter,
        *,
        max_pagination_pages: int,
        crawl_policy: CrawlPolicy | None = None,
    ) -> None:
        if max_pagination_pages < 1:
            raise ValueError("max_pagination_pages must be positive")
        self.adapter = adapter
        self.max_pagination_pages = max_pagination_pages
        self.crawl_policy = crawl_policy or CrawlPolicy(
            include_attachments=False
        )

    @staticmethod
    def _issue_for(url: str, exc: Exception) -> ProbeIssue:
        if isinstance(exc, HttpStatusError):
            return ProbeIssue(
                code="HTTP_STATUS",
                url=url,
                message=f"The page returned HTTP {exc.status_code}.",
            )
        if isinstance(exc, UnsafeUrlError):
            return ProbeIssue(
                code="URL_POLICY",
                url=url,
                message="The URL was rejected by the crawler safety policy.",
            )
        if isinstance(exc, ResponseTooLargeError):
            return ProbeIssue(
                code="RESPONSE_TOO_LARGE",
                url=url,
                message="The page exceeds the configured response size limit.",
            )
        if isinstance(exc, UnsupportedContentTypeError):
            return ProbeIssue(
                code="UNSUPPORTED_CONTENT",
                url=url,
                message="The URL is not a supported HTML listing page.",
            )
        if isinstance(exc, FetchRequestError):
            return ProbeIssue(
                code="NETWORK_ERROR",
                url=url,
                message="The page could not be reached after bounded retries.",
            )
        return ProbeIssue(
            code="FETCH_FAILED",
            url=url,
            message="The page could not be inspected.",
        )

    async def run(self, seed_urls: Iterable[str]) -> UrlProbeSummary:
        started = time.perf_counter()
        submitted = [
            self.crawl_policy.canonicalize(url)
            for url in seed_urls
            if url.strip()
        ]
        if any(self.crawl_policy.is_attachment(url) for url in submitted):
            raise ValueError("URL probe accepts HTML pages, not attachment URLs")
        seeds = self.crawl_policy.candidates(submitted)
        if not seeds:
            raise ValueError("at least one eligible HTTPS seed URL is required")

        queue = deque((url, False) for url in seeds)
        queued = set(seeds)
        visited: set[str] = set()
        pagination_detected: set[str] = set()
        pagination_queued: set[str] = set()
        listing_pages_detected: set[str] = set()
        listing_pages_scanned: set[str] = set()
        exact_document_keys: set[str] = set()
        estimated_document_urls: set[str] = set()
        attachment_urls: set[str] = set()
        successful_pages: set[str] = set()
        sample_titles: list[str] = []
        issues: list[ProbeIssue] = []

        while queue:
            url, is_pagination = queue.popleft()
            queued.discard(url)
            if url in visited:
                continue
            visited.add(url)

            try:
                result = await self.adapter.fetch_and_parse(url)
            except Exception as exc:
                issues.append(self._issue_for(url, exc))
                continue
            if result is None:
                issues.append(
                    ProbeIssue(
                        code="EMPTY_RESPONSE",
                        url=url,
                        message="The adapter returned no page content.",
                    )
                )
                continue
            if result.mime_type.lower() != "text/html":
                issues.append(
                    ProbeIssue(
                        code="NOT_HTML",
                        url=url,
                        message="The URL is not an HTML page.",
                    )
                )
                continue

            page_url = self.crawl_policy.canonicalize(
                result.final_url or result.url or url
            )
            successful_pages.add(page_url)
            discovered_pagination: list[str] = []
            discovered_content: list[str] = []
            for link in result.discovered_links:
                normalized = self.crawl_policy.canonicalize(link)
                if self.crawl_policy.is_attachment(normalized):
                    attachment_urls.add(normalized)
                    continue
                if not self.crawl_policy.should_visit(normalized):
                    continue
                if self.crawl_policy.is_pagination(normalized):
                    pagination_detected.add(normalized)
                    listing_pages_detected.add(normalized)
                    discovered_pagination.append(normalized)
                else:
                    discovered_content.append(normalized)

            is_listing = bool(
                result.discovery_only
                or is_pagination
                or discovered_pagination
            )
            if is_listing:
                listing_pages_detected.add(page_url)
                listing_pages_scanned.add(page_url)

            if result.embedded_documents:
                for document in result.embedded_documents:
                    if document.canonical_key in exact_document_keys:
                        continue
                    exact_document_keys.add(document.canonical_key)
                    if document.title and len(sample_titles) < 5:
                        sample_titles.append(document.title)
                    for link in document.discovered_links:
                        normalized = self.crawl_policy.canonicalize(link)
                        attachment_urls.add(normalized)
            elif is_listing:
                estimated_document_urls.update(discovered_content)
            else:
                estimated_document_urls.add(page_url)

            for pagination_url in discovered_pagination:
                if pagination_url in visited or pagination_url in queued:
                    continue
                if len(pagination_queued) >= self.max_pagination_pages:
                    continue
                pagination_queued.add(pagination_url)
                queued.add(pagination_url)
                queue.append((pagination_url, True))

        has_exact = bool(exact_document_keys)
        has_estimated = bool(estimated_document_urls)
        if has_exact and has_estimated:
            count_mode = "MIXED"
        elif has_exact:
            count_mode = "EXACT_LISTING_RECORDS"
        else:
            count_mode = "ESTIMATED_LINKS"

        pagination_limit_reached = (
            len(pagination_detected) > len(pagination_queued)
        )
        status = "PARTIAL" if issues or pagination_limit_reached else "COMPLETE"
        return UrlProbeSummary(
            status=status,
            count_mode=count_mode,
            seed_count=len(seeds),
            pages_scanned=len(successful_pages),
            listing_pages_scanned=len(listing_pages_scanned),
            listing_pages_detected=len(listing_pages_detected),
            pagination_pages_detected=len(pagination_detected),
            pagination_pages_followed=len(pagination_queued),
            max_pagination_pages=self.max_pagination_pages,
            documents_detected=(
                len(exact_document_keys) + len(estimated_document_urls)
            ),
            attachments_detected=len(attachment_urls),
            pagination_limit_reached=pagination_limit_reached,
            duration_ms=int((time.perf_counter() - started) * 1000),
            sample_titles=tuple(sample_titles),
            issues=tuple(issues),
        )
