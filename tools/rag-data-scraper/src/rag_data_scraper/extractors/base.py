from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import List, Dict, Any, Optional

class BlockType(str, Enum):
    HEADING = "heading"
    PARAGRAPH = "paragraph"
    TABLE = "table"
    LIST = "list"
    CAPTION = "caption"

@dataclass
class ContentBlock:
    block_type: BlockType
    text: str
    heading_level: Optional[int] = None
    page_number: Optional[int] = None
    table_data: Optional[List[List[str]]] = None
    metadata: Dict[str, Any] = field(default_factory=dict)

@dataclass
class ExtractedDocument:
    source_uri: str
    title: str
    mime_type: str
    raw_sha256: str
    blocks: List[ContentBlock]
    ocr_used: bool = False
    ocr_confidence: float = 1.0
    truncated: bool = False
    document_metadata: Dict[str, Any] = field(default_factory=dict)

class BaseExtractor(ABC):
    @abstractmethod
    def extract(self, file_path: Path | str) -> ExtractedDocument:
        """Extract content blocks and metadata from a raw document file."""
        pass
