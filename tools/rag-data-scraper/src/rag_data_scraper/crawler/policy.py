from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import PurePosixPath
import re
from typing import Iterable
from urllib.parse import parse_qsl, quote, unquote, urlencode, urlsplit, urlunsplit


_TRACKING_PARAMETERS = frozenset(
    {"fbclid", "gclid", "mc_cid", "mc_eid", "ref", "ref_src", "source"}
)
_SKIPPED_SUFFIXES = frozenset(
    {
        ".7z", ".avi", ".bmp", ".css", ".exe", ".gif", ".ico", ".jpeg",
        ".jpg", ".js", ".mov", ".mp3", ".mp4", ".mpeg", ".png", ".rar",
        ".tar", ".webm", ".webp", ".woff", ".woff2", ".zip", ".doc",
    }
)
_ATTACHMENT_SUFFIXES = frozenset({".docx", ".pdf"})
_PAGINATION_KEYS = frozenset({"page", "p", "paged", "pageindex", "trang"})
_PAGINATION_PATH = re.compile(
    r"(?:^|/)(?:page|paged|trang|trang-chu)(?:/|-)?\d+(?:/|$)",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class CrawlPolicy:
    """Normalize and prioritize crawl candidates behind one small interface."""

    include_attachments: bool = True
    include_path_prefixes: tuple[str, ...] = ()
    exclude_path_prefixes: tuple[str, ...] = ()
    tracking_parameters: frozenset[str] = field(
        default_factory=lambda: _TRACKING_PARAMETERS
    )

    def canonicalize(self, raw_url: str) -> str:
        parsed = urlsplit(raw_url.strip())
        scheme = parsed.scheme.lower()
        hostname = (parsed.hostname or "").lower().rstrip(".")
        if not scheme or not hostname:
            return raw_url.strip()
        port = parsed.port
        default_port = (scheme == "https" and port == 443) or (
            scheme == "http" and port == 80
        )
        host = hostname if port is None or default_port else f"{hostname}:{port}"
        path = quote(unquote(parsed.path or "/"), safe="/:~%+@")
        query_pairs = []
        for key, value in parse_qsl(parsed.query, keep_blank_values=True):
            lowered = key.lower()
            if lowered.startswith("utm_") or lowered in self.tracking_parameters:
                continue
            # Pagination links often expose both the canonical listing URL and
            # an explicit ``?page=1`` alias. Treat them as the same resource so
            # the first page does not consume crawl budget twice.
            if lowered in _PAGINATION_KEYS and value.strip() == "1":
                continue
            query_pairs.append((key, value))
        query_pairs.sort(key=lambda item: (item[0].lower(), item[0], item[1]))
        query = urlencode(query_pairs, doseq=True, safe="/:~%+@")
        return urlunsplit((scheme, host, path, query, ""))

    def should_visit(self, raw_url: str) -> bool:
        parsed = urlsplit(raw_url)
        if parsed.scheme.lower() != "https" or not parsed.hostname:
            return False
        lowered_path = (parsed.path or "/").lower()
        suffix = PurePosixPath(lowered_path).suffix
        if suffix in _SKIPPED_SUFFIXES:
            return False
        if not self.include_attachments and suffix in _ATTACHMENT_SUFFIXES:
            return False
        if self.include_path_prefixes and not any(
            lowered_path.startswith(prefix.lower())
            for prefix in self.include_path_prefixes
        ):
            return False
        return not any(
            lowered_path.startswith(prefix.lower())
            for prefix in self.exclude_path_prefixes
        )

    def is_pagination(self, raw_url: str) -> bool:
        parsed = urlsplit(raw_url)
        path = parsed.path.lower()
        if any(
            key.lower() in _PAGINATION_KEYS
            for key, _ in parse_qsl(parsed.query)
        ):
            return True
        return bool(_PAGINATION_PATH.search(path))

    def next_depth(
        self,
        raw_url: str,
        *,
        parent_depth: int,
        max_depth: int,
    ) -> int | None:
        """Pagination stays at content depth; normal links consume one level."""
        if self.is_pagination(raw_url):
            return parent_depth
        if parent_depth >= max_depth:
            return None
        return parent_depth + 1

    def priority(self, raw_url: str) -> int:
        parsed = urlsplit(raw_url)
        path = parsed.path.lower()
        if PurePosixPath(path).suffix in _ATTACHMENT_SUFFIXES:
            return 100
        if self.is_pagination(raw_url):
            return 20
        if any(
            marker in path
            for marker in ("/van-ban", "/document", "/detail", "/chi-tiet")
        ):
            return 50
        if PurePosixPath(path).suffix in {".html", ".htm"}:
            return 40
        return 0

    def candidates(self, urls: Iterable[str]) -> list[str]:
        result: list[str] = []
        seen: set[str] = set()
        for raw_url in urls:
            normalized = self.canonicalize(raw_url)
            if normalized in seen or not self.should_visit(normalized):
                continue
            seen.add(normalized)
            result.append(normalized)
        return result
