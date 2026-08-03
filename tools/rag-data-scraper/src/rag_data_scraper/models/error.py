from datetime import datetime, timezone
from enum import Enum
from typing import Optional
from uuid import UUID, uuid4
from pydantic import BaseModel, Field


class ErrorStage(str, Enum):
    FETCH = "fetch"
    EXTRACT = "extract"
    CLEAN = "clean"
    METADATA = "metadata"
    CHUNK = "chunk"


class CrawlerError(BaseModel):
    error_id: UUID = Field(default_factory=uuid4)
    job_id: str
    source_id: str
    url: str
    stage: ErrorStage
    error_type: str
    message: str
    stack_trace: Optional[str] = None
    timestamp: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))
