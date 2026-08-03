from datetime import datetime
from typing import Literal, Optional
from pydantic import BaseModel, Field


class ManifestFiles(BaseModel):
    observations_file: str = "document-observations.jsonl"
    chunk_sets_file: str = "chunk-sets.jsonl"
    chunks_file: str = "chunks.jsonl"
    errors_file: str = "crawler-errors.jsonl"


class StagingManifest(BaseModel):
    schema_version: str = "1.0"
    corpus_type: Literal["general", "legal_reference"] = "general"
    source_registry_version: Optional[str] = None
    source_registry_entry_ids: list[str] = Field(default_factory=list)
    job_id: str
    started_at: datetime
    completed_at: datetime
    total_observations: int = Field(ge=0)
    total_chunk_sets: int = Field(ge=0)
    total_chunks: int = Field(ge=0)
    total_errors: int = Field(ge=0)
    files: ManifestFiles = Field(default_factory=ManifestFiles)
