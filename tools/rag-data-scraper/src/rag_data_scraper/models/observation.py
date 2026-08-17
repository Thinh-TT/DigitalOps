from datetime import date, datetime, timezone
from enum import Enum
from typing import Any, Dict, Optional
from uuid import UUID, uuid4
from pydantic import BaseModel, Field, HttpUrl


class DocumentIdentityStrategy(str, Enum):
    AUTHORITATIVE = "authoritative"
    CANONICAL_METADATA = "canonical_metadata"
    SOURCE_SCOPED = "source_scoped"
    CONTENT_ONLY = "content_only"


class QualityStatus(str, Enum):
    CLEAN = "clean"
    OCR_FALLBACK = "ocr_fallback"
    TRUNCATED = "truncated"
    FAILED = "failed"


class ExtractionQuality(BaseModel):
    status: QualityStatus
    ocr_used: bool
    confidence_score: float = Field(ge=0.0, le=1.0)


class SourceProvenance(BaseModel):
    registry_entry_id: Optional[str] = None
    registry_version: Optional[str] = None
    corpus_type: str = "general"
    source_trust_tier: str = "unverified"
    source_domain: str
    source_version: str
    publish_policy: str = "blocked"
    language: str = "vi"


class LegalDocumentMetadata(BaseModel):
    document_number: Optional[str] = None
    document_type: Optional[str] = None
    issuer: Optional[str] = None
    issued_date: Optional[date] = None
    legal_status: str = "status_unknown"
    effective_from: Optional[date] = None
    effective_to: Optional[date] = None
    amends: list[str] = Field(default_factory=list)
    replaces: list[str] = Field(default_factory=list)
    replaced_by: list[str] = Field(default_factory=list)


class DocumentObservation(BaseModel):
    observation_id: UUID = Field(default_factory=uuid4)
    job_id: str
    source_id: str
    source_namespace: str
    authority_namespace: Optional[str] = None
    document_identity_strategy: DocumentIdentityStrategy
    canonical_document_key: str
    source_document_url: str
    title: str
    raw_artifact_uri: str
    raw_sha256: str = Field(pattern=r"^[a-f0-9]{64}$")
    mime_type: str
    normalized_text_uri: str
    normalized_text_sha256: str = Field(pattern=r"^[a-f0-9]{64}$")
    char_count: int = Field(ge=0)
    word_count: int = Field(ge=0)
    extraction_quality: ExtractionQuality
    source_provenance: Optional[SourceProvenance] = None
    legal_metadata: Optional[LegalDocumentMetadata] = None
    document_metadata: Dict[str, Any] = Field(default_factory=dict)
    crawled_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
