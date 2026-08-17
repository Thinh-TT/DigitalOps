from datetime import datetime, timezone
from typing import Any, Dict, List, Optional
from uuid import UUID, uuid4
from pydantic import BaseModel, Field, model_validator


class ChunkSet(BaseModel):
    chunk_set_id: UUID = Field(default_factory=uuid4)
    observation_id: UUID
    job_id: str
    chunking_strategy: str
    chunker_version: str
    tokenizer_name: str
    target_tokens: int = Field(gt=0)
    soft_max_tokens: Optional[int] = Field(default=None, gt=0)
    max_tokens: Optional[int] = Field(default=None, gt=0)
    overlap_tokens: int = Field(ge=0)
    total_chunks: int = Field(ge=0)
    created_at: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))

    @model_validator(mode="after")
    def validate_token_limits(self):
        soft_max = self.soft_max_tokens or self.target_tokens
        hard_max = self.max_tokens or max(soft_max, self.target_tokens)
        if not self.overlap_tokens < self.target_tokens <= soft_max <= hard_max:
            raise ValueError(
                "chunk limits must satisfy overlap < target <= soft max <= max"
            )
        return self


class ChunkACL(BaseModel):
    allowed_roles: List[str] = Field(default_factory=list)
    denied_roles: List[str] = Field(default_factory=list)
    security_classification: str = Field(default="internal")


class Chunk(BaseModel):
    chunk_id: UUID = Field(default_factory=uuid4)
    chunk_set_id: UUID
    chunk_index: int = Field(ge=0)
    text: str
    token_count: int = Field(gt=0)
    character_start: int = Field(ge=0)
    character_end: int = Field(ge=0)
    content_sha256: str = Field(pattern=r"^[a-f0-9]{64}$")
    heading_path: Optional[str] = None
    page_numbers: List[int] = Field(default_factory=list)
    structure_metadata: Dict[str, Any] = Field(default_factory=dict)
    chunk_acl: ChunkACL = Field(default_factory=ChunkACL)
