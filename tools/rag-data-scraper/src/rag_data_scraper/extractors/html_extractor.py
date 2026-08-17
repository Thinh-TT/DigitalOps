import hashlib
from pathlib import Path
from typing import List
from bs4 import BeautifulSoup, Comment
from .base import BaseExtractor, ExtractedDocument, ContentBlock, BlockType

class HTMLExtractor(BaseExtractor):
    def extract(self, file_path: Path | str) -> ExtractedDocument:
        path = Path(file_path)
        with open(path, "rb") as f:
            raw_bytes = f.read()
        raw_sha256 = hashlib.sha256(raw_bytes).hexdigest()

        html_content = raw_bytes.decode("utf-8", errors="replace")
        soup = BeautifulSoup(html_content, "lxml")

        # ASP.NET pages may wrap the body in a form, so keep form itself while
        # removing structural boilerplate before block extraction.
        for element in soup(
            [
                "aside",
                "dialog",
                "footer",
                "header",
                "iframe",
                "nav",
                "noscript",
                "option",
                "script",
                "select",
                "style",
                "svg",
                "template",
            ]
        ):
            element.decompose()

        boilerplate_tokens = {
            "advertisement",
            "breadcrumb",
            "cookie",
            "footer",
            "header",
            "menu",
            "navbar",
            "social",
        }
        for element in list(soup.find_all(True)):
            if not getattr(element, "attrs", None):
                continue
            raw_tokens = [element.get("id", "")]
            classes = element.get("class", [])
            raw_tokens.extend(classes if isinstance(classes, list) else [classes])
            tokens = {
                token.lower()
                for value in raw_tokens
                for token in str(value).replace("_", "-").split("-")
                if token
            }
            if tokens & boilerplate_tokens:
                element.decompose()

        # Remove HTML comments using 'string' parameter (BS4 4.12+ compliant)
        for comment in soup.find_all(string=lambda s: isinstance(s, Comment)):
            comment.extract()

        title = path.stem
        if soup.title and soup.title.string:
            title = soup.title.string.strip()

        blocks: List[ContentBlock] = []

        # 2. Extract block elements (h1-h6, p, tr, li) with non-trivial text
        for elem in soup.find_all(["h1", "h2", "h3", "h4", "h5", "h6", "p", "tr", "li"]):
            text = elem.get_text(separator=" ", strip=True)
            if not text or len(text) < 5:
                continue

            if elem.name in ["h1", "h2", "h3", "h4", "h5", "h6"]:
                level = int(elem.name[1])
                blocks.append(ContentBlock(
                    block_type=BlockType.HEADING,
                    text=text,
                    heading_level=level
                ))
            elif elem.name == "tr":
                cells = [td.get_text(strip=True) for td in elem.find_all(["td", "th"]) if td.get_text(strip=True)]
                if cells:
                    row_text = " | ".join(cells)
                    blocks.append(ContentBlock(
                        block_type=BlockType.PARAGRAPH,
                        text=row_text
                    ))
            elif elem.name in ["p", "li"]:
                blocks.append(ContentBlock(
                    block_type=BlockType.PARAGRAPH,
                    text=text
                ))

        return ExtractedDocument(
            source_uri=str(path.resolve()),
            title=title,
            mime_type="text/html",
            raw_sha256=raw_sha256,
            blocks=blocks,
            ocr_used=False,
            ocr_confidence=1.0
        )
