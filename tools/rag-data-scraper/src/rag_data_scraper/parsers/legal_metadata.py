import re
from datetime import date, datetime
from typing import Dict, Any, Optional

from ..models.observation import LegalDocumentMetadata
from ..source_registry import ResolvedSourceProfile

DOCUMENT_TYPES = [
    "Luật", "Bộ luật", "Nghị định", "Thông tư", "Quyết định", "Nghị quyết", 
    "Chỉ thị", "Thông tư liên tịch", "Công văn", "Chỉ đạo"
]

class LegalMetadataParser:
    """Parses Vietnamese legal document metadata using regex patterns."""

    NUMBER_PATTERN = re.compile(r"Số:\s*([0-9]+/[^\s,;\n]+)", re.IGNORECASE)
    DATE_PATTERN = re.compile(r"ngay\s+([0-9]{1,2})\s+thang\s+([0-9]{1,2})\s+nam\s+([0-9]{4})", re.IGNORECASE)
    
    @classmethod
    def parse(cls, text_sample: str) -> Dict[str, Any]:
        metadata: Dict[str, Any] = {}
        
        # 1. Document Number
        match_num = cls.NUMBER_PATTERN.search(text_sample)
        if match_num:
            metadata["document_number"] = match_num.group(1).strip()
            
        # 2. Document Type
        for doc_type in DOCUMENT_TYPES:
            if re.search(r"\b" + re.escape(doc_type) + r"\b", text_sample, re.IGNORECASE):
                metadata["document_type"] = doc_type
                break

        # 3. Issuance Date
        normalized_date_text = text_sample.replace("ngày", "ngay").replace("tháng", "thang").replace("năm", "nam")
        match_date = cls.DATE_PATTERN.search(normalized_date_text)
        if match_date:
            day, month, year = match_date.groups()
            metadata["issuance_date"] = f"{int(year):04d}-{int(month):02d}-{int(day):02d}"

        return metadata

    @staticmethod
    def _date(value: Any) -> Optional[date]:
        if isinstance(value, date):
            return value
        if not isinstance(value, str) or not value.strip():
            return None
        normalized = value.strip()
        for pattern in ("%Y-%m-%d", "%d/%m/%Y", "%d-%m-%Y"):
            try:
                return datetime.strptime(normalized, pattern).date()
            except ValueError:
                continue
        return None

    @classmethod
    def normalize(
        cls,
        metadata: Dict[str, Any],
        profile: Optional[ResolvedSourceProfile],
    ) -> Optional[LegalDocumentMetadata]:
        if profile is None or profile.corpus_type != "legal_reference":
            return None
        legal_status = str(
            metadata.get("legal_status") or "status_unknown"
        ).strip().lower()
        if legal_status not in {
            "current",
            "expired",
            "repealed",
            "superseded",
            "status_unknown",
        }:
            legal_status = "status_unknown"

        def string_list(key: str) -> list[str]:
            value = metadata.get(key, [])
            if isinstance(value, str):
                value = [value]
            if not isinstance(value, list):
                return []
            return [str(item).strip() for item in value if str(item).strip()]

        return LegalDocumentMetadata(
            document_number=(
                str(metadata["document_number"]).strip()
                if metadata.get("document_number")
                else None
            ),
            document_type=(
                str(metadata["document_type"]).strip()
                if metadata.get("document_type")
                else None
            ),
            issuer=(
                str(
                    metadata.get("issuer")
                    or metadata.get("issuing_authority")
                    or profile.default_issuer
                ).strip()
                if (
                    metadata.get("issuer")
                    or metadata.get("issuing_authority")
                    or profile.default_issuer
                )
                else None
            ),
            issued_date=cls._date(
                metadata.get("issued_date")
                or metadata.get("issuance_date")
            ),
            legal_status=legal_status,
            effective_from=cls._date(metadata.get("effective_from")),
            effective_to=cls._date(metadata.get("effective_to")),
            amends=string_list("amends"),
            replaces=string_list("replaces"),
            replaced_by=string_list("replaced_by"),
        )
