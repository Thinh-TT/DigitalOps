from dataclasses import replace
import hashlib
import re
from typing import Tuple

from ..extractors.base import ExtractedDocument

class TextCleaner:
    @staticmethod
    def normalize_fragment(raw_text: str) -> str:
        if not raw_text:
            return ""
        text = raw_text.replace("\r\n", "\n").replace("\r", "\n")
        text = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "", text)
        text = re.sub(r"[ \t]+", " ", text)
        text = re.sub(r"\n{3,}", "\n\n", text)
        return text.strip()

    @classmethod
    def clean_document(cls, document: ExtractedDocument) -> Tuple[ExtractedDocument, str, str]:
        """Normalize blocks once so files, hashes, offsets and chunks share one text."""
        blocks = []
        for block in document.blocks:
            text = cls.normalize_fragment(block.text)
            if text:
                blocks.append(replace(block, text=text))
        cleaned_document = replace(document, blocks=blocks)
        canonical_text = "\n\n".join(block.text for block in blocks)
        sha256 = hashlib.sha256(canonical_text.encode("utf-8")).hexdigest()
        return cleaned_document, canonical_text, sha256

    @staticmethod
    def clean(raw_text: str) -> Tuple[str, str]:
        """
        Cleans and normalizes raw text.
        Returns: (cleaned_text, text_sha256)
        """
        if not raw_text:
            return "", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"

        cleaned = TextCleaner.normalize_fragment(raw_text)
        text_sha256 = hashlib.sha256(cleaned.encode("utf-8")).hexdigest()
        
        return cleaned, text_sha256
