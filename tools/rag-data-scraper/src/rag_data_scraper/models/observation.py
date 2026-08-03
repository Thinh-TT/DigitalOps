from datetime import datetime, timezone
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
    document_metadata: Dict[str, Any] = Field(default_factory=dict)
    crawled_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
