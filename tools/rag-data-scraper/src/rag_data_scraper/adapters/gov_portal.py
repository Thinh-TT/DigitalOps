from __future__ import annotations

import re
from typing import Mapping, Optional, Any

from .http_source import HttpSourceAdapter
from ..parsers.legal_metadata import LegalMetadataParser


class GovPortalAdapter(HttpSourceAdapter):
    def __init__(
        self,
        source_id: str = "vanban_chinhphu",
        source_namespace: str = "vanban.chinhphu.vn",
        authority_namespace: Optional[str] = "gov.vn",
        user_agent: str = "DigitalOps-RAG-Crawler/1.0",
        timeout_seconds: float = 30.0,
        max_response_bytes: int = 25 * 1024 * 1024,
        max_attempts: int = 3,
        backoff_base_seconds: float = 0.5,
        max_backoff_seconds: float = 8.0,
        per_host_delay_seconds: float = 0.2,
        per_host_max_concurrent: int = 2,
        fetcher=None,
    ) -> None:
        super().__init__(
            source_id=source_id,
            source_namespace=source_namespace,
            authority_namespace=authority_namespace,
            identity_strategy="authoritative",
            allowed_hosts={source_namespace, f"www.{source_namespace}"},
            key_prefix="gov",
            user_agent=user_agent,
            timeout_seconds=timeout_seconds,
            max_response_bytes=max_response_bytes,
            max_attempts=max_attempts,
            backoff_base_seconds=backoff_base_seconds,
            max_backoff_seconds=max_backoff_seconds,
            per_host_delay_seconds=per_host_delay_seconds,
            per_host_max_concurrent=per_host_max_concurrent,
            fetcher=fetcher,
        )

    def _metadata_for_html(self, soup, final_url: str) -> dict[str, Any]:
        metadata = LegalMetadataParser.parse(soup.get_text(" ", strip=True))
        metadata["source_url"] = final_url
        return metadata

    def _html_canonical_key(
        self,
        canonical_url: str,
        metadata: Mapping[str, Any],
    ) -> str:
        document_number = metadata.get("document_number")
        if not document_number:
            return self._url_key("web", canonical_url)
        normalized = re.sub(r"\s+", "", str(document_number)).upper()
        document_type = str(metadata.get("document_type") or "doc").lower()
        return f"gov:{document_type}:{normalized}"
