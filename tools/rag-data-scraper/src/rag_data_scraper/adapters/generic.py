from __future__ import annotations

from typing import Iterable, Optional
from urllib.parse import urlsplit

from .http_source import HttpSourceAdapter


class GenericWebAdapter(HttpSourceAdapter):
    def __init__(
        self,
        source_id: str = "generic_web",
        source_namespace: str = "custom.web",
        authority_namespace: Optional[str] = None,
        user_agent: str = "DigitalOps-RAG-Crawler/1.0",
        allowed_hosts: Optional[Iterable[str]] = None,
        timeout_seconds: float = 30.0,
        max_response_bytes: int = 25 * 1024 * 1024,
        max_attempts: int = 3,
        backoff_base_seconds: float = 0.5,
        max_backoff_seconds: float = 8.0,
        per_host_delay_seconds: float = 0.2,
        per_host_max_concurrent: int = 2,
        allow_related_asset_hosts: bool = True,
        fetcher=None,
    ) -> None:
        hosts = tuple(host for host in (allowed_hosts or ()) if host)
        if not hosts:
            raise ValueError("GenericWebAdapter requires at least one allowed host")
        super().__init__(
            source_id=source_id,
            source_namespace=source_namespace,
            authority_namespace=authority_namespace,
            identity_strategy="content_only",
            allowed_hosts=hosts,
            key_prefix="generic",
            user_agent=user_agent,
            timeout_seconds=timeout_seconds,
            max_response_bytes=max_response_bytes,
            max_attempts=max_attempts,
            backoff_base_seconds=backoff_base_seconds,
            max_backoff_seconds=max_backoff_seconds,
            per_host_delay_seconds=per_host_delay_seconds,
            per_host_max_concurrent=per_host_max_concurrent,
            allow_related_asset_hosts=allow_related_asset_hosts,
            fetcher=fetcher,
        )

    def _effective_source_namespace(self, final_url: str) -> str:
        return urlsplit(final_url).hostname or self.source_namespace

    def _metadata_for_html(self, soup, final_url: str) -> dict[str, str]:
        return {
            "source_url": final_url,
            "domain": urlsplit(final_url).hostname or self.source_namespace,
        }
