from __future__ import annotations

from dataclasses import dataclass
import html
import importlib
import json
from pathlib import Path
import re
import textwrap
from typing import Iterable, Iterator
from xml.sax.saxutils import escape as xml_escape
from zipfile import ZIP_DEFLATED, ZipFile

from ..models.chunk import Chunk
from ..models.observation import DocumentObservation
from .export_errors import (
    ExportDependencyUnavailableError,
    ExportTooLargeError,
)


FORMAT_VERSION = "1.0"
MAX_EXCEL_CELL_CHARS = 32_000
MAX_OFFICE_DOCUMENTS = 10_000
MAX_PPTX_SLIDES = 2_000
PPTX_TEXT_CHARS = 1_400
MAX_VISIBLE_SVG_LINES = 500
_INVALID_XML_CHARS = re.compile(
    "[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F\\uFFFE\\uFFFF]"
)


@dataclass(frozen=True)
class RichExportDocument:
    observation: DocumentObservation
    normalized_text: str
    chunks: tuple[Chunk, ...]


def _load_dependency(module_name: str, package_name: str):
    try:
        return importlib.import_module(module_name)
    except ImportError as exc:
        raise ExportDependencyUnavailableError(
            f"Install {package_name} to generate this export."
        ) from exc


def _xml_safe(value: object) -> str:
    return _INVALID_XML_CHARS.sub("\uFFFD", str(value))


def _spreadsheet_safe(value: object) -> object:
    if isinstance(value, str) and value.lstrip().startswith(
        ("=", "+", "-", "@", "\t", "\r")
    ):
        return "'" + value
    return value


def _segments(value: str, limit: int) -> Iterator[str]:
    if not value:
        yield ""
        return
    for start in range(0, len(value), limit):
        yield value[start : start + limit]


class RichFormatExporter:
    """Generate portable document exports from a validated staging package."""

    def __init__(self, max_export_bytes: int) -> None:
        self.max_export_bytes = max_export_bytes

    def _check_output(self, path: Path) -> None:
        if path.stat().st_size > self.max_export_bytes:
            raise ExportTooLargeError("Export exceeds its byte limit.")

    def _write_zip(
        self,
        path: Path,
        members: Iterable[tuple[str, bytes]],
    ) -> None:
        total_bytes = 0
        with ZipFile(
            path,
            "w",
            compression=ZIP_DEFLATED,
            compresslevel=6,
            allowZip64=True,
        ) as archive:
            for name, content in members:
                total_bytes += len(content)
                if total_bytes > self.max_export_bytes:
                    raise ExportTooLargeError(
                        "Export exceeds its byte limit."
                    )
                archive.writestr(name, content)
        self._check_output(path)

    @staticmethod
    def _document_metadata(document: RichExportDocument) -> dict[str, object]:
        observation = document.observation
        return {
            "format_version": FORMAT_VERSION,
            "observation_id": str(observation.observation_id),
            "job_id": observation.job_id,
            "canonical_document_key": observation.canonical_document_key,
            "title": observation.title,
            "source_id": observation.source_id,
            "source_namespace": observation.source_namespace,
            "source_url": observation.source_document_url,
            "mime_type": observation.mime_type,
            "normalized_text_sha256": observation.normalized_text_sha256,
            "crawled_at": observation.crawled_at.isoformat(),
            "document_metadata": observation.document_metadata,
        }

    def write_html(
        self,
        path: Path,
        job_id: str,
        documents: tuple[RichExportDocument, ...],
    ) -> None:
        parts = [
            "<!doctype html><html lang=\"vi\"><head>",
            "<meta charset=\"utf-8\">",
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">",
            f"<title>RAG export {html.escape(job_id)}</title>",
            "<style>body{font-family:system-ui,sans-serif;max-width:980px;margin:2rem auto;padding:0 1rem;color:#17202a}article{border-bottom:1px solid #d9e1e8;padding:1.5rem 0}dl{display:grid;grid-template-columns:max-content 1fr;gap:.35rem 1rem}dt{font-weight:700}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f6f8fa;padding:1rem;border-radius:.5rem}</style>",
            "</head><body>",
            f"<h1>RAG export: {html.escape(job_id)}</h1>",
            f"<p>{len(documents)} document(s), format version {FORMAT_VERSION}.</p>",
        ]
        for document in documents:
            metadata = self._document_metadata(document)
            parts.extend(
                [
                    f'<article id="document-{html.escape(str(document.observation.observation_id))}">',
                    f"<h2>{html.escape(document.observation.title)}</h2>",
                    "<dl>",
                ]
            )
            for key in (
                "observation_id",
                "canonical_document_key",
                "source_url",
                "mime_type",
                "normalized_text_sha256",
                "crawled_at",
            ):
                parts.append(
                    f"<dt>{html.escape(key)}</dt><dd>{html.escape(str(metadata[key]))}</dd>"
                )
            parts.extend(
                [
                    "</dl>",
                    f"<pre>{html.escape(document.normalized_text)}</pre>",
                    "</article>",
                ]
            )
        parts.append("</body></html>")
        path.write_text("".join(parts), encoding="utf-8", newline="\n")
        self._check_output(path)

    @staticmethod
    def _reportlab_fonts(pdfmetrics, ttfonts) -> tuple[str, str]:
        candidates = [
            (
                Path("C:/Windows/Fonts/arial.ttf"),
                Path("C:/Windows/Fonts/arialbd.ttf"),
            ),
            (
                Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
                Path("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
            ),
        ]
        for regular, bold in candidates:
            if regular.is_file() and bold.is_file():
                pdfmetrics.registerFont(ttfonts.TTFont("RagSans", regular))
                pdfmetrics.registerFont(ttfonts.TTFont("RagSansBold", bold))
                return "RagSans", "RagSansBold"
        reportlab = _load_dependency("reportlab", "reportlab")
        fonts_dir = Path(reportlab.__file__).parent / "fonts"
        pdfmetrics.registerFont(
            ttfonts.TTFont("RagSans", fonts_dir / "Vera.ttf")
        )
        pdfmetrics.registerFont(
            ttfonts.TTFont("RagSansBold", fonts_dir / "VeraBd.ttf")
        )
        return "RagSans", "RagSansBold"

    def write_pdf(
        self,
        path: Path,
        job_id: str,
        documents: tuple[RichExportDocument, ...],
    ) -> None:
        pagesizes = _load_dependency("reportlab.lib.pagesizes", "reportlab")
        styles_module = _load_dependency(
            "reportlab.lib.styles", "reportlab"
        )
        colors = _load_dependency("reportlab.lib.colors", "reportlab")
        enums = _load_dependency("reportlab.lib.enums", "reportlab")
        platypus = _load_dependency("reportlab.platypus", "reportlab")
        pdfmetrics = _load_dependency("reportlab.pdfbase.pdfmetrics", "reportlab")
        ttfonts = _load_dependency("reportlab.pdfbase.ttfonts", "reportlab")
        regular, bold = self._reportlab_fonts(pdfmetrics, ttfonts)
        styles = styles_module.getSampleStyleSheet()
        title_style = styles_module.ParagraphStyle(
            "RagTitle",
            parent=styles["Title"],
            fontName=bold,
            fontSize=18,
            leading=22,
            textColor=colors.HexColor("#17324D"),
        )
        heading_style = styles_module.ParagraphStyle(
            "RagHeading",
            parent=styles["Heading1"],
            fontName=bold,
            fontSize=14,
            leading=18,
        )
        body_style = styles_module.ParagraphStyle(
            "RagBody",
            parent=styles["BodyText"],
            fontName=regular,
            fontSize=10,
            leading=14,
            alignment=enums.TA_LEFT,
            spaceAfter=6,
        )
        story = [
            platypus.Paragraph(
                f"RAG export: {html.escape(job_id)}", title_style
            ),
            platypus.Paragraph(
                f"{len(documents)} document(s), format version {FORMAT_VERSION}.",
                body_style,
            ),
        ]
        for index, document in enumerate(documents):
            if index:
                story.append(platypus.PageBreak())
            story.append(
                platypus.Paragraph(
                    html.escape(document.observation.title), heading_style
                )
            )
            metadata = self._document_metadata(document)
            for key in (
                "observation_id",
                "canonical_document_key",
                "source_url",
                "normalized_text_sha256",
            ):
                story.append(
                    platypus.Paragraph(
                        f"<b>{html.escape(key)}:</b> {html.escape(str(metadata[key]))}",
                        body_style,
                    )
                )
            for paragraph in document.normalized_text.splitlines() or [""]:
                story.append(
                    platypus.Paragraph(
                        html.escape(paragraph) or "&#160;", body_style
                    )
                )

        def page_number(canvas, doc) -> None:
            canvas.saveState()
            canvas.setFont(regular, 8)
            canvas.drawRightString(
                pagesizes.A4[0] - 36,
                24,
                f"{job_id} - page {doc.page}",
            )
            canvas.restoreState()

        document = platypus.SimpleDocTemplate(
            str(path),
            pagesize=pagesizes.A4,
            rightMargin=42,
            leftMargin=42,
            topMargin=42,
            bottomMargin=38,
            title=f"RAG export {job_id}",
            author="DigitalOps RAG Data Scraper",
        )
        document.build(story, onFirstPage=page_number, onLaterPages=page_number)
        self._check_output(path)

    def write_docx(
        self,
        path: Path,
        job_id: str,
        documents: tuple[RichExportDocument, ...],
    ) -> None:
        if len(documents) > MAX_OFFICE_DOCUMENTS:
            raise ExportTooLargeError("DOCX document count exceeds its limit.")
        docx = _load_dependency("docx", "python-docx")
        shared = _load_dependency("docx.shared", "python-docx")
        enum_section = _load_dependency(
            "docx.enum.section", "python-docx"
        )
        word = docx.Document()
        section = word.sections[0]
        section.page_width = shared.Mm(210)
        section.page_height = shared.Mm(297)
        section.orientation = enum_section.WD_ORIENT.PORTRAIT
        styles = word.styles
        styles["Normal"].font.name = "Arial"
        styles["Normal"].font.size = shared.Pt(10)
        word.add_heading(f"RAG export: {job_id}", 0)
        word.add_paragraph(
            f"{len(documents)} document(s), format version {FORMAT_VERSION}."
        )
        for document in documents:
            word.add_page_break()
            word.add_heading(document.observation.title, 1)
            metadata = self._document_metadata(document)
            for key in (
                "observation_id",
                "canonical_document_key",
                "source_url",
                "mime_type",
                "normalized_text_sha256",
                "crawled_at",
            ):
                paragraph = word.add_paragraph()
                paragraph.add_run(f"{key}: ").bold = True
                paragraph.add_run(_xml_safe(metadata[key]))
            word.add_heading("Normalized text", 2)
            word.add_paragraph(_xml_safe(document.normalized_text))
        word.core_properties.title = f"RAG export {job_id}"
        word.core_properties.author = "DigitalOps RAG Data Scraper"
        word.save(path)
        self._check_output(path)
    def write_txt_zip(
        self,
        path: Path,
        job_id: str,
        documents: tuple[RichExportDocument, ...],
    ) -> None:
        manifest = {
            "format_version": FORMAT_VERSION,
            "job_id": job_id,
            "total_documents": len(documents),
            "documents": [
                {
                    **self._document_metadata(document),
                    "file": (
                        "documents/"
                        f"{document.observation.observation_id}.txt"
                    ),
                }
                for document in documents
            ],
        }

        def members() -> Iterator[tuple[str, bytes]]:
            yield (
                "export-manifest.json",
                json.dumps(
                    manifest, ensure_ascii=False, indent=2
                ).encode("utf-8"),
            )
            for document in documents:
                yield (
                    "documents/"
                    f"{document.observation.observation_id}.txt",
                    document.normalized_text.encode("utf-8"),
                )

        self._write_zip(path, members())

    @staticmethod
    def _chunk_sheet_rows(
        records: Iterable[dict[str, object]],
    ) -> Iterator[list[object]]:
        for record in records:
            metadata = record["metadata"]
            assert isinstance(metadata, dict)
            text = str(record["text"])
            for part_index, text_part in enumerate(
                _segments(text, MAX_EXCEL_CELL_CHARS)
            ):
                yield [
                    _spreadsheet_safe(record["id"]),
                    _spreadsheet_safe(text_part),
                    part_index,
                    metadata["job_id"],
                    metadata["observation_id"],
                    metadata["chunk_set_id"],
                    metadata["chunk_index"],
                    metadata["token_count"],
                    _spreadsheet_safe(metadata["canonical_document_key"]),
                    _spreadsheet_safe(metadata["title"]),
                    _spreadsheet_safe(metadata["source_url"]),
                    metadata["mime_type"],
                    _spreadsheet_safe(metadata["heading_path"] or ""),
                    json.dumps(metadata["page_numbers"]),
                    json.dumps(metadata["allowed_roles"]),
                    json.dumps(metadata["denied_roles"]),
                    metadata["security_classification"],
                    metadata["content_sha256"],
                ]

    def write_xlsx(
        self,
        path: Path,
        documents: tuple[RichExportDocument, ...],
        records: tuple[dict[str, object], ...],
    ) -> None:
        openpyxl = _load_dependency("openpyxl", "openpyxl")
        cell_module = _load_dependency("openpyxl.cell", "openpyxl")
        styles = _load_dependency("openpyxl.styles", "openpyxl")
        workbook = openpyxl.Workbook(write_only=True, iso_dates=True)
        chunks_sheet = workbook.create_sheet("Chunks")
        documents_sheet = workbook.create_sheet("Documents")
        headers = [
            "id", "text", "text_part_index", "job_id",
            "observation_id", "chunk_set_id", "chunk_index",
            "token_count", "canonical_document_key", "title",
            "source_url", "mime_type", "heading_path",
            "page_numbers_json", "allowed_roles_json",
            "denied_roles_json", "security_classification",
            "content_sha256",
        ]

        def header_cells(sheet, values: list[str]) -> list[object]:
            result = []
            for value in values:
                cell = cell_module.WriteOnlyCell(sheet, value=value)
                cell.font = styles.Font(bold=True, color="FFFFFF")
                cell.fill = styles.PatternFill(
                    "solid", fgColor="17324D"
                )
                result.append(cell)
            return result

        chunks_sheet.append(header_cells(chunks_sheet, headers))
        for row in self._chunk_sheet_rows(records):
            chunks_sheet.append(
                [
                    _xml_safe(value) if isinstance(value, str) else value
                    for value in row
                ]
            )
        document_headers = [
            "observation_id", "job_id", "canonical_document_key",
            "title", "source_url", "mime_type",
            "normalized_text_sha256", "crawled_at",
            "document_metadata_json",
        ]
        documents_sheet.append(
            header_cells(documents_sheet, document_headers)
        )
        for document in documents:
            metadata = self._document_metadata(document)
            documents_sheet.append(
                [
                    metadata["observation_id"],
                    metadata["job_id"],
                    _spreadsheet_safe(metadata["canonical_document_key"]),
                    _spreadsheet_safe(metadata["title"]),
                    _spreadsheet_safe(metadata["source_url"]),
                    metadata["mime_type"],
                    metadata["normalized_text_sha256"],
                    metadata["crawled_at"],
                    _spreadsheet_safe(
                        json.dumps(
                            metadata["document_metadata"],
                            ensure_ascii=False,
                            separators=(",", ":"),
                        )
                    ),
                ]
            )
        chunks_sheet.freeze_panes = "A2"
        documents_sheet.freeze_panes = "A2"
        workbook.save(path)
        self._check_output(path)

    def write_json(
        self,
        path: Path,
        job_id: str,
        records: tuple[dict[str, object], ...],
    ) -> None:
        with path.open("wb") as stream:
            total = 0

            def emit(value: bytes) -> None:
                nonlocal total
                total += len(value)
                if total > self.max_export_bytes:
                    raise ExportTooLargeError(
                        "Export exceeds its byte limit."
                    )
                stream.write(value)

            emit(
                (
                    "{"
                    f'"format_version":"{FORMAT_VERSION}",'
                    f'"job_id":{json.dumps(job_id)},'
                    f'"total_chunks":{len(records)},"chunks":['
                ).encode("utf-8")
            )
            for index, record in enumerate(records):
                if index:
                    emit(b",")
                emit(
                    json.dumps(
                        record,
                        ensure_ascii=False,
                        separators=(",", ":"),
                    ).encode("utf-8")
                )
            emit(b"]}\n")
        self._check_output(path)
    def write_pptx(
        self,
        path: Path,
        job_id: str,
        documents: tuple[RichExportDocument, ...],
    ) -> None:
        pptx = _load_dependency("pptx", "python-pptx")
        if len(documents) > MAX_OFFICE_DOCUMENTS:
            raise ExportTooLargeError(
                "PPTX document count exceeds its limit."
            )
        slide_count = 1 + sum(
            max(
                1,
                (
                    len(document.normalized_text)
                    + PPTX_TEXT_CHARS
                    - 1
                )
                // PPTX_TEXT_CHARS,
            )
            for document in documents
        )
        if slide_count > MAX_PPTX_SLIDES:
            raise ExportTooLargeError(
                "PPTX slide count exceeds its limit."
            )
        deck = pptx.Presentation()
        title_slide = deck.slides.add_slide(deck.slide_layouts[0])
        title_slide.shapes.title.text = f"RAG export: {job_id}"
        title_slide.placeholders[1].text = (
            f"{len(documents)} document(s) | "
            f"format version {FORMAT_VERSION}"
        )
        for document in documents:
            parts = list(
                _segments(document.normalized_text, PPTX_TEXT_CHARS)
            )
            for part_index, text_part in enumerate(parts, start=1):
                slide = deck.slides.add_slide(deck.slide_layouts[1])
                suffix = (
                    f" ({part_index}/{len(parts)})"
                    if len(parts) > 1
                    else ""
                )
                slide.shapes.title.text = (
                    document.observation.title + suffix
                )
                slide.placeholders[1].text = _xml_safe(text_part)
                slide.notes_slide.notes_text_frame.text = json.dumps(
                    self._document_metadata(document),
                    ensure_ascii=False,
                    separators=(",", ":"),
                )
        deck.core_properties.title = f"RAG export {job_id}"
        deck.core_properties.author = "DigitalOps RAG Data Scraper"
        deck.save(path)
        self._check_output(path)

    def write_xml(
        self,
        path: Path,
        job_id: str,
        documents: tuple[RichExportDocument, ...],
    ) -> None:
        with path.open("w", encoding="utf-8", newline="\n") as stream:
            stream.write(
                '<?xml version="1.0" encoding="UTF-8"?>\n'
            )
            stream.write(
                '<rag-export '
                f'format-version="{FORMAT_VERSION}" '
                f'job-id="{xml_escape(_xml_safe(job_id))}">\n'
            )
            for document in documents:
                metadata = self._document_metadata(document)
                observation_id = xml_escape(
                    _xml_safe(metadata["observation_id"])
                )
                stream.write(
                    f'  <document observation-id="{observation_id}">\n'
                )
                for key in (
                    "title",
                    "canonical_document_key",
                    "source_url",
                    "mime_type",
                    "normalized_text_sha256",
                    "crawled_at",
                ):
                    tag = key.replace("_", "-")
                    value = xml_escape(_xml_safe(metadata[key]))
                    stream.write(
                        f"    <{tag}>{value}</{tag}>\n"
                    )
                metadata_json = json.dumps(
                    metadata["document_metadata"],
                    ensure_ascii=False,
                    separators=(",", ":"),
                )
                stream.write(
                    "    <document-metadata-json>"
                    + xml_escape(_xml_safe(metadata_json))
                    + "</document-metadata-json>\n"
                )
                stream.write(
                    "    <normalized-text>"
                    + xml_escape(_xml_safe(document.normalized_text))
                    + "</normalized-text>\n"
                )
                stream.write("    <chunks>\n")
                for chunk in document.chunks:
                    stream.write(
                        "      <chunk "
                        f'id="{chunk.chunk_id}" '
                        f'index="{chunk.chunk_index}" '
                        f'content-sha256="{chunk.content_sha256}">'
                        + xml_escape(_xml_safe(chunk.text))
                        + "</chunk>\n"
                    )
                stream.write("    </chunks>\n  </document>\n")
                if stream.tell() > self.max_export_bytes:
                    raise ExportTooLargeError(
                        "Export exceeds its byte limit."
                    )
            stream.write("</rag-export>\n")
        self._check_output(path)

    def write_svg_zip(
        self,
        path: Path,
        job_id: str,
        documents: tuple[RichExportDocument, ...],
    ) -> None:
        manifest = {
            "format_version": FORMAT_VERSION,
            "job_id": job_id,
            "total_documents": len(documents),
            "note": (
                "Full normalized text is stored in each SVG metadata "
                "element."
            ),
        }

        def svg(document: RichExportDocument) -> bytes:
            wrapped_lines: list[str] = []
            for source_line in (
                document.normalized_text.splitlines() or [""]
            ):
                wrapped_lines.extend(
                    textwrap.wrap(
                        source_line,
                        width=100,
                        replace_whitespace=False,
                        drop_whitespace=False,
                    )
                    or [""]
                )
                if len(wrapped_lines) >= MAX_VISIBLE_SVG_LINES:
                    wrapped_lines = wrapped_lines[
                        :MAX_VISIBLE_SVG_LINES
                    ]
                    break
            height = 150 + 20 * len(wrapped_lines)
            metadata = {
                **self._document_metadata(document),
                "normalized_text": document.normalized_text,
            }
            metadata_json = json.dumps(
                metadata,
                ensure_ascii=False,
                separators=(",", ":"),
            )
            title = xml_escape(_xml_safe(document.observation.title))
            source = xml_escape(
                _xml_safe(document.observation.source_document_url)
            )
            parts = [
                '<?xml version="1.0" encoding="UTF-8"?>',
                '<svg xmlns="http://www.w3.org/2000/svg" '
                f'width="1200" height="{height}" '
                f'viewBox="0 0 1200 {height}">',
                "<style>"
                "text{font-family:Arial,sans-serif;fill:#17202a}"
                ".title{font-size:26px;font-weight:700}"
                ".meta{font-size:13px;fill:#51606f}"
                ".body{font-size:15px}"
                "</style>",
                "<metadata>"
                + xml_escape(_xml_safe(metadata_json))
                + "</metadata>",
                '<rect width="100%" height="100%" fill="#ffffff"/>',
                f'<text class="title" x="40" y="45">{title}</text>',
                '<text class="meta" x="40" y="75">'
                "observation_id: "
                f"{document.observation.observation_id}</text>",
                '<text class="meta" x="40" y="98">'
                f"source: {source}</text>",
                '<text class="body" x="40" y="135">',
            ]
            for index, line in enumerate(wrapped_lines):
                dy = 0 if index == 0 else 20
                parts.append(
                    f'<tspan x="40" dy="{dy}">'
                    + xml_escape(_xml_safe(line))
                    + "</tspan>"
                )
            if len(wrapped_lines) >= MAX_VISIBLE_SVG_LINES:
                parts.append(
                    '<tspan x="40" dy="20">'
                    "[Preview truncated; full text is in metadata]"
                    "</tspan>"
                )
            parts.extend(["</text>", "</svg>"])
            return "".join(parts).encode("utf-8")

        def members() -> Iterator[tuple[str, bytes]]:
            yield (
                "export-manifest.json",
                json.dumps(
                    manifest, ensure_ascii=False, indent=2
                ).encode("utf-8"),
            )
            for document in documents:
                yield (
                    f"documents/"
                    f"{document.observation.observation_id}.svg",
                    svg(document),
                )

        self._write_zip(path, members())
