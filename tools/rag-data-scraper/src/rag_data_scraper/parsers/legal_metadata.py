import re
from typing import Dict, Any, Optional

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
