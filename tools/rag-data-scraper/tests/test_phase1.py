from datetime import datetime, timezone
from pathlib import Path
import pytest
from rag_data_scraper.parsers.legal_metadata import LegalMetadataParser
from rag_data_scraper.cleaners.text_cleaner import TextCleaner
from rag_data_scraper.extractors.html_extractor import HTMLExtractor
from rag_data_scraper.chunkers.structure_chunker import StructureChunker
from rag_data_scraper.exporters.staging_exporter import StagingExporter
from rag_data_scraper.models.observation import (
    DocumentObservation,
    DocumentIdentityStrategy,
    ExtractionQuality,
    QualityStatus,
)

def test_legal_metadata_parser():
    sample_text = """
    CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM
    Độc lập - Tự do - Hạnh phúc

    Số: 15/2023/NĐ-CP   Hà Nội, ngày 15 tháng 04 năm 2023

    NGHỊ ĐỊNH
    Về việc quản lý và vận hành hệ thống DigitalOps
    """
    metadata = LegalMetadataParser.parse(sample_text)
    assert metadata.get("document_number") == "15/2023/NĐ-CP"
    assert metadata.get("document_type") == "Nghị định"
    assert metadata.get("issuance_date") == "2023-04-15"

def test_text_cleaner():
    raw = "Hello   world!\n\n\nThis is   a test.\x00"
    cleaned, sha256_hash = TextCleaner.clean(raw)
    assert cleaned == "Hello world!\n\nThis is a test."
    assert len(sha256_hash) == 64

def test_html_extraction_chunking_and_export(tmp_path):
    html_file = tmp_path / "sample.html"
    html_content = """
    <!DOCTYPE html>
    <html>
    <head><title>Nghị định 15/2023/NĐ-CP</title></head>
    <body>
        <h1>Chương I: Quy định chung</h1>
        <p>Điều 1. Phạm vi điều chỉnh. Nghị định này quy định về vận hành hệ thống.</p>
        <h2>Chương II: Tổ chức thực hiện</h2>
        <p>Điều 2. Trách nhiệm của các đơn vị thuộc hệ thống DigitalOps.</p>
    </body>
    </html>
    """
    html_file.write_text(html_content, encoding="utf-8")

    # 1. Extract
    extractor = HTMLExtractor()
    extracted_doc = extractor.extract(html_file)
    assert extracted_doc.title == "Nghị định 15/2023/NĐ-CP"
    assert len(extracted_doc.blocks) >= 4

    # 2. Chunk
    chunker = StructureChunker(target_tokens=50, max_tokens=100)
    obs = DocumentObservation(
        job_id="job-test-01",
        source_id="test_src",
        source_namespace="local",
        document_identity_strategy=DocumentIdentityStrategy.CANONICAL_METADATA,
        canonical_document_key="doc-test-01",
        source_document_url=str(html_file),
        title=extracted_doc.title,
        raw_artifact_uri=str(html_file),
        raw_sha256=extracted_doc.raw_sha256,
        mime_type="text/html",
        normalized_text_uri=str(html_file),
        normalized_text_sha256=extracted_doc.raw_sha256,
        char_count=len(html_content),
        word_count=len(html_content.split()),
        extraction_quality=ExtractionQuality(status=QualityStatus.CLEAN, ocr_used=False, confidence_score=1.0)
    )

    chunk_set, chunks = chunker.chunk(extracted_doc, obs.observation_id, "job-test-01")
    assert chunk_set.total_chunks > 0
    assert len(chunks) == chunk_set.total_chunks

    # 3. Export
    exporter = StagingExporter(tmp_path / "staging")
    now = datetime.now(timezone.utc)
    job_dir = exporter.export("job-test-01", now, now, [obs], [(chunk_set, chunks)], [])

    assert (job_dir / "manifest.json").exists()
    assert (job_dir / "document-observations.jsonl").exists()
    assert (job_dir / "chunk-sets.jsonl").exists()
    assert (job_dir / "chunks.jsonl").exists()
    assert (job_dir / "crawler-errors.jsonl").exists()


def test_html_extractor_removes_structural_boilerplate(tmp_path):
    html_file = tmp_path / "boilerplate.html"
    html_file.write_text(
        """
        <html><head><title>Article</title></head><body>
          <header><p>Shared header</p></header>
          <nav><ul><li>Shared menu</li></ul></nav>
          <div class="menu"><p>Secondary menu</p></div>
          <article><h1>Article heading</h1><p>Useful article content.</p></article>
          <footer><p>Shared footer and contact details.</p></footer>
        </body></html>
        """,
        encoding="utf-8",
    )

    extracted = HTMLExtractor().extract(html_file)
    text = "\n".join(block.text for block in extracted.blocks)

    assert "Article heading" in text
    assert "Useful article content." in text
    assert "Shared header" not in text
    assert "Shared menu" not in text
    assert "Secondary menu" not in text
    assert "Shared footer" not in text
