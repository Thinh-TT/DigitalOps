from pathlib import Path
from typing import Any, Dict, Optional
import yaml
from pydantic import BaseModel, Field, model_validator


class StorageSettings(BaseModel):
    staging_base_dir: Path = Field(default=Path("storage/staging"))
    raw_base_dir: Path = Field(default=Path("storage/raw"))
    state_db_path: Path = Field(default=Path("storage/state/crawler.db"))


class ChunkerSettings(BaseModel):
    target_tokens: int = Field(default=448, gt=0, le=512)
    soft_max_tokens: int = Field(default=480, gt=0, le=512)
    overlap_tokens: int = Field(default=64, ge=0, lt=512)
    max_tokens: int = Field(default=512, gt=0, le=512)

    @model_validator(mode="after")
    def validate_limits(self):
        if not (
            self.overlap_tokens
            < self.target_tokens
            <= self.soft_max_tokens
            <= self.max_tokens
        ):
            raise ValueError(
                "chunk limits must satisfy overlap < target <= soft max <= max"
            )
        return self

    tokenizer_model: str = "heuristic:vietnamese-word-1.3x"


class OCRSettings(BaseModel):
    tesseract_cmd: str = "tesseract"
    lang: str = "vie+eng"
    min_confidence: float = 60.0
    tessdata_dir: Optional[Path] = Path("storage/ocr/tessdata")
    max_pages: int = Field(default=50, ge=1, le=500)
    max_image_pixels: int = Field(default=3_000_000, ge=1_000_000, le=50_000_000)
    page_timeout_seconds: float = Field(default=30.0, gt=0, le=300)


class CrawlerSettings(BaseModel):
    max_concurrent_requests: int = Field(default=5, ge=1, le=32)
    request_timeout_seconds: float = Field(default=30.0, gt=0, le=300)
    user_agent: str = "DigitalOps-RAG-Crawler/1.0 (+http://digitalops.internal)"
    max_response_bytes: int = Field(default=25 * 1024 * 1024, ge=1024, le=100 * 1024 * 1024)
    max_total_resources: int = Field(default=500, ge=1, le=10000)
    max_depth: int = Field(default=1, ge=0, le=5)
    max_pagination_pages: int = Field(default=25, ge=1, le=500)
    retry_attempts: int = Field(default=3, ge=1, le=8)
    retry_backoff_base_seconds: float = Field(default=0.5, ge=0, le=30)
    retry_max_backoff_seconds: float = Field(default=8.0, ge=0, le=120)
    per_host_delay_seconds: float = Field(default=0.2, ge=0, le=30)
    per_host_max_concurrent: int = Field(default=2, ge=1, le=16)

    @model_validator(mode="after")
    def validate_retry_limits(self):
        if self.retry_max_backoff_seconds < self.retry_backoff_base_seconds:
            raise ValueError("retry max backoff must be >= base backoff")
        return self


class Settings(BaseModel):
    version: str = "1.0"
    storage: StorageSettings = Field(default_factory=StorageSettings)
    chunker: ChunkerSettings = Field(default_factory=ChunkerSettings)
    ocr: OCRSettings = Field(default_factory=OCRSettings)
    crawler: CrawlerSettings = Field(default_factory=CrawlerSettings)

    @classmethod
    def load_from_yaml(cls, path: Path) -> "Settings":
        if not path.exists():
            return cls()
        with open(path, "r", encoding="utf-8") as f:
            data = yaml.safe_load(f) or {}
        return cls(**data)
