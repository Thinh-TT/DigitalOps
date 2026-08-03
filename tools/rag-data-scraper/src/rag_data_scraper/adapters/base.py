from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Any, Dict, List, Mapping, Optional

@dataclass
class CrawlResult:
    url: str
    canonical_key: str
    title: str
    html_or_bytes: bytes
    mime_type: str
    document_identity_strategy: str
    source_namespace: str
    authority_namespace: Optional[str]
    metadata: Dict[str, Any]
    discovered_links: List[str]
    requested_url: Optional[str] = None
    final_url: Optional[str] = None
    http_status: int = 200
    response_headers: Mapping[str, str] | None = None
    attempt_count: int = 1
    elapsed_ms: int = 0


@dataclass
class NotModifiedResult:
    requested_url: str
    final_url: str
    response_headers: Mapping[str, str]
    attempt_count: int = 1
    elapsed_ms: int = 0

class BaseAdapter(ABC):
    @property
    @abstractmethod
    def source_id(self) -> str:
        pass

    @property
    @abstractmethod
    def source_namespace(self) -> str:
        pass

    @property
    @abstractmethod
    def authority_namespace(self) -> Optional[str]:
        pass

    @property
    @abstractmethod
    def default_identity_strategy(self) -> str:
        pass

    @abstractmethod
    async def fetch_and_parse(self, url: str) -> Optional[CrawlResult]:
        """Fetch content from URL and return CrawlResult with extracted links and metadata."""
        pass

    async def fetch_and_parse_conditional(
        self,
        url: str,
        request_headers: Mapping[str, str],
    ) -> Optional[CrawlResult] | NotModifiedResult:
        """Backward-compatible conditional seam for production adapters."""
        return await self.fetch_and_parse(url)

    async def aclose(self) -> None:
        """Release adapter-owned resources after a crawl job."""
        return None
