from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Any, Dict, List, Mapping, Optional

from ..source_registry import ResolvedSourceProfile

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
    embedded_documents: List["CrawlResult"] = field(default_factory=list)
    discovery_only: bool = False


@dataclass
class NotModifiedResult:
    requested_url: str
    final_url: str
    response_headers: Mapping[str, str]
    attempt_count: int = 1
    elapsed_ms: int = 0

class BaseAdapter(ABC):
    @property
    def source_profile(self) -> Optional[ResolvedSourceProfile]:
        return getattr(self, "_source_profile", None)

    def attach_source_profile(
        self,
        profile: Optional[ResolvedSourceProfile],
    ) -> None:
        self._source_profile = profile

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

    def rehydrate_cached_result(self, result: CrawlResult) -> CrawlResult:
        """Rebuild derived parse output that is not stored in the HTTP cache."""
        return result
