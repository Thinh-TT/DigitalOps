from __future__ import annotations

from collections import OrderedDict
import hashlib
from io import BytesIO
import logging
import os
from pathlib import Path
import shutil
from typing import Any, Optional

from PIL import Image
from pypdf import PdfReader

try:
    import pytesseract

    HAS_PYTESSERACT = True
except ImportError:
    pytesseract = None
    HAS_PYTESSERACT = False

from .base import BaseExtractor, BlockType, ContentBlock, ExtractedDocument


logger = logging.getLogger(__name__)


class OCRUnavailableError(RuntimeError):
    pass


class PDFTextExtractionError(ValueError):
    pass


class PDFExtractor(BaseExtractor):
    """Extract native PDF text and use bounded OCR for image-only pages."""

    def __init__(
        self,
        tesseract_cmd: str = "tesseract",
        lang: str = "vie+eng",
        min_confidence: float = 60.0,
        *,
        tessdata_dir: Path | str | None = None,
        max_ocr_pages: int = 50,
        max_image_pixels: int = 3_000_000,
        page_timeout_seconds: float = 30.0,
    ) -> None:
        if max_ocr_pages < 1:
            raise ValueError("max_ocr_pages must be positive")
        if max_image_pixels < 1:
            raise ValueError("max_image_pixels must be positive")
        if page_timeout_seconds <= 0:
            raise ValueError("page_timeout_seconds must be positive")
        self.tesseract_cmd = tesseract_cmd
        self.lang = lang
        self.min_confidence = min_confidence
        self.tessdata_dir = Path(tessdata_dir) if tessdata_dir else None
        self.max_ocr_pages = max_ocr_pages
        self.max_image_pixels = max_image_pixels
        self.page_timeout_seconds = page_timeout_seconds

    def _resolve_tesseract_command(self) -> Optional[str]:
        configured = Path(self.tesseract_cmd).expanduser()
        if configured.is_file():
            return str(configured.resolve())

        discovered = shutil.which(self.tesseract_cmd)
        if discovered:
            return discovered

        if os.name != "nt":
            return None
        candidates = []
        for variable in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA"):
            root = os.environ.get(variable)
            if not root:
                continue
            base = Path(root)
            if variable == "LOCALAPPDATA":
                base /= "Programs"
            candidates.append(base / "Tesseract-OCR" / "tesseract.exe")
        for candidate in candidates:
            if candidate.is_file():
                return str(candidate.resolve())
        return None

    def _tessdata_configs(
        self,
        command: str,
    ) -> list[tuple[str, Optional[Path]]]:
        candidates = []
        if self.tessdata_dir is not None:
            candidates.append(self.tessdata_dir)
        candidates.append(Path(command).parent / "tessdata")
        configs: list[tuple[str, Optional[Path]]] = [("", None)]
        for candidate in candidates:
            if not candidate.is_dir():
                continue
            resolved = str(candidate.resolve())
            if '"' in resolved:
                continue
            config = f'--tessdata-dir "{resolved}"'
            option = (config, candidate.resolve())
            if option not in configs:
                configs.append(option)
        return configs

    def _configure_ocr(self) -> tuple[str, str] | None:
        if not HAS_PYTESSERACT or pytesseract is None:
            return None
        command = self._resolve_tesseract_command()
        if command is None:
            return None
        pytesseract.pytesseract.tesseract_cmd = command

        requested = [value for value in self.lang.split("+") if value]
        best: tuple[
            tuple[int, int], str, list[str], Optional[Path]
        ] | None = None
        for config, tessdata_dir in self._tessdata_configs(command):
            try:
                available = list(pytesseract.get_languages(config=config))
            except (OSError, RuntimeError, pytesseract.TesseractError):
                continue
            available_set = set(available)
            selected = [value for value in requested if value in available_set]
            weighted = sum(
                len(requested) - index
                for index, value in enumerate(requested)
                if value in available_set
            )
            score = (len(selected), weighted)
            if best is None or score > best[0]:
                best = (score, config, available, tessdata_dir)

        if best is None:
            return None
        _, config, available, tessdata_dir = best
        available_set = set(available)
        selected = [value for value in requested if value in available_set]
        if not selected:
            selected = [
                value for value in ("vie", "eng") if value in available_set
            ]
        if not selected:
            selected = sorted(value for value in available_set if value != "osd")[:1]
        if not selected:
            return None
        language = "+".join(selected)
        if language != self.lang:
            logger.warning(
                "OCR language fallback: requested %s, using %s",
                self.lang,
                language,
            )
        # pytesseract keeps quotes in config arguments on Windows when it
        # invokes OCR, even though get_languages strips them. TESSDATA_PREFIX
        # is the supported cross-platform way to handle paths with spaces.
        if tessdata_dir is not None:
            os.environ["TESSDATA_PREFIX"] = str(tessdata_dir)
            config = ""
        return language, config

    @staticmethod
    def _text_from_ocr_data(data: dict[str, Any]) -> tuple[str, list[float]]:
        lines: OrderedDict[tuple[int, int, int], list[str]] = OrderedDict()
        confidences: list[float] = []
        texts = data.get("text", [])
        for index, raw_text in enumerate(texts):
            text = str(raw_text).strip()
            if not text:
                continue
            key = (
                int(data.get("block_num", [0] * len(texts))[index]),
                int(data.get("par_num", [0] * len(texts))[index]),
                int(data.get("line_num", [0] * len(texts))[index]),
            )
            lines.setdefault(key, []).append(text)
            try:
                confidence = float(data.get("conf", [-1] * len(texts))[index])
            except (TypeError, ValueError):
                confidence = -1
            if confidence >= 0:
                confidences.append(confidence)
        return "\n".join(" ".join(words) for words in lines.values()), confidences

    def _ocr_image(
        self,
        image_bytes: bytes,
        *,
        language: str,
        tessdata_config: str,
    ) -> tuple[str, list[float]]:
        if pytesseract is None:
            return "", []
        with Image.open(BytesIO(image_bytes)) as opened:
            image = opened.convert("RGB")
        pixels = image.width * image.height
        if pixels > self.max_image_pixels:
            scale = (self.max_image_pixels / pixels) ** 0.5
            image.thumbnail(
                (
                    max(1, int(image.width * scale)),
                    max(1, int(image.height * scale)),
                ),
                Image.Resampling.LANCZOS,
            )
        config = " ".join(
            value for value in (tessdata_config, "--oem 1") if value
        )
        data = pytesseract.image_to_data(
            image,
            lang=language,
            config=config,
            output_type=pytesseract.Output.DICT,
            timeout=self.page_timeout_seconds,
        )
        return self._text_from_ocr_data(data)

    def extract(self, file_path: Path | str) -> ExtractedDocument:
        path = Path(file_path)
        raw_bytes = path.read_bytes()
        raw_sha256 = hashlib.sha256(raw_bytes).hexdigest()

        reader = PdfReader(path)
        blocks: list[ContentBlock] = []
        ocr_used = False
        ocr_confidences: list[float] = []
        ocr_runtime: tuple[str, str] | None = None
        ocr_runtime_checked = False
        ocr_pages = 0
        ocr_pages_omitted = 0
        ocr_pages_failed = 0

        title = path.stem
        if reader.metadata and reader.metadata.title:
            title = str(reader.metadata.title).strip()

        for page_idx, page in enumerate(reader.pages):
            page_num = page_idx + 1
            page_text = (page.extract_text() or "").strip()
            if len(page_text) >= 50:
                blocks.append(
                    ContentBlock(
                        block_type=BlockType.PARAGRAPH,
                        text=page_text,
                        page_number=page_num,
                    )
                )
                continue

            if not ocr_runtime_checked:
                ocr_runtime = self._configure_ocr()
                ocr_runtime_checked = True
            if ocr_runtime is None:
                if page_text:
                    blocks.append(
                        ContentBlock(
                            block_type=BlockType.PARAGRAPH,
                            text=page_text,
                            page_number=page_num,
                        )
                    )
                continue
            if ocr_pages >= self.max_ocr_pages:
                ocr_pages_omitted += 1
                if page_text:
                    blocks.append(
                        ContentBlock(
                            block_type=BlockType.PARAGRAPH,
                            text=page_text,
                            page_number=page_num,
                        )
                    )
                continue

            language, tessdata_config = ocr_runtime
            ocr_pages += 1
            page_ocr_text: list[str] = []
            page_confidences: list[float] = []
            try:
                for image_file in page.images:
                    text, confidences = self._ocr_image(
                        image_file.data,
                        language=language,
                        tessdata_config=tessdata_config,
                    )
                    if text:
                        page_ocr_text.append(text)
                    page_confidences.extend(confidences)
            except Exception as exc:
                ocr_pages_failed += 1
                logger.warning(
                    "OCR failed on page %s of %s: %s",
                    page_num,
                    path,
                    exc,
                )

            combined_ocr = "\n".join(page_ocr_text).strip()
            selected_text = combined_ocr if len(combined_ocr) > len(page_text) else page_text
            if not selected_text:
                continue
            used_page_ocr = selected_text == combined_ocr and bool(combined_ocr)
            ocr_used = ocr_used or used_page_ocr
            ocr_confidences.extend(page_confidences)
            page_confidence = (
                sum(page_confidences) / len(page_confidences)
                if page_confidences
                else 0.0
            )
            blocks.append(
                ContentBlock(
                    block_type=BlockType.PARAGRAPH,
                    text=selected_text,
                    page_number=page_num,
                    metadata={
                        "ocr": used_page_ocr,
                        "ocr_language": language if used_page_ocr else None,
                        "ocr_confidence": page_confidence / 100.0,
                        "ocr_below_min_confidence": (
                            used_page_ocr and page_confidence < self.min_confidence
                        ),
                    },
                )
            )

        if not blocks:
            if ocr_runtime is None:
                raise OCRUnavailableError(
                    "PDF has no text layer and a usable Tesseract OCR runtime "
                    "or requested language model was not found"
                )
            raise PDFTextExtractionError(
                "PDF has no text layer and bounded OCR produced no text"
            )

        avg_confidence = (
            sum(ocr_confidences) / len(ocr_confidences) / 100.0
            if ocr_confidences
            else (0.0 if ocr_used else 1.0)
        )
        language = ocr_runtime[0] if ocr_runtime else None
        return ExtractedDocument(
            source_uri=str(path.resolve()),
            title=title,
            mime_type="application/pdf",
            raw_sha256=raw_sha256,
            blocks=blocks,
            ocr_used=ocr_used,
            ocr_confidence=avg_confidence,
            truncated=ocr_pages_omitted > 0,
            document_metadata={
                "pdf_page_count": len(reader.pages),
                "ocr_language_requested": self.lang,
                "ocr_language_used": language,
                "ocr_pages_processed": ocr_pages,
                "ocr_pages_omitted": ocr_pages_omitted,
                "ocr_pages_failed": ocr_pages_failed,
                "ocr_page_limit": self.max_ocr_pages,
            },
        )
