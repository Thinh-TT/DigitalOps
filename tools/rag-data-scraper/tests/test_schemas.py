import json
from pathlib import Path
import pytest
import jsonschema
from rag_data_scraper.models.observation import (
    DocumentObservation,
    DocumentIdentityStrategy,
    ExtractionQuality,
    QualityStatus,
)
from rag_data_scraper.models.chunk import ChunkSet, Chunk, ChunkACL
from rag_data_scraper.models.manifest import StagingManifest
from rag_data_scraper.models.error import CrawlerError, ErrorStage

SCHEMAS_DIR = Path(__file__).parent.parent / "schemas"

def load_schema(schema_name: str) -> dict:
    schema_path = SCHEMAS_DIR / schema_name
    with open(schema_path, "r", encoding="utf-8") as f:
        return json.load(f)

def test_document_observation_schema():
    schema = load_schema("document-observation.schema.json")
    obs = DocumentObservation(
        job_id="job-123",
        source_id="vbpl",
        source_namespace="tvpl",
        authority_namespace="gov.vn",
        document_identity_strategy=DocumentIdentityStrategy.CANONICAL_METADATA,
        canonical_document_key="doc-456",
        source_document_url="https://example.com/doc",
        title="Test Document",
        raw_artifact_uri="storage/raw/doc.pdf",
        raw_sha256="a" * 64,
        mime_type="application/pdf",
        normalized_text_uri="storage/staging/job-123/text.txt",
        normalized_text_sha256="b" * 64,
        char_count=100,
        word_count=20,
        extraction_quality=ExtractionQuality(
            status=QualityStatus.CLEAN,
            ocr_used=False,
            confidence_score=1.0,
        ),
    )
    obs_dict = json.loads(obs.model_dump_json())
    jsonschema.validate(instance=obs_dict, schema=schema)

def test_chunk_set_and_chunk_schema():
    chunk_set_schema = load_schema("chunk-set.schema.json")
    chunk_schema = load_schema("chunk.schema.json")

    cs = ChunkSet(
        observation_id="11111111-1111-1111-1111-111111111111",
        job_id="job-123",
        chunking_strategy="qwen_tokenizer",
        chunker_version="1.0.0",
        tokenizer_name="Qwen/Qwen2.5-0.5B",
        target_tokens=448,
        soft_max_tokens=480,
        max_tokens=512,
        overlap_tokens=64,
        total_chunks=1,
    )
    cs_dict = json.loads(cs.model_dump_json())
    jsonschema.validate(instance=cs_dict, schema=chunk_set_schema)

    ck = Chunk(
        chunk_set_id=cs.chunk_set_id,
        chunk_index=0,
        text="Sample chunk content.",
        token_count=10,
        character_start=0,
        character_end=21,
        content_sha256="c" * 64,
        heading_path="Điều 1",
        page_numbers=[1],
        chunk_acl=ChunkACL(
            allowed_roles=["public"],
            denied_roles=[],
            security_classification="internal",
        ),
    )
    ck_dict = json.loads(ck.model_dump_json())
    jsonschema.validate(instance=ck_dict, schema=chunk_schema)

def test_manifest_schema():
    manifest_schema = load_schema("manifest.schema.json")
    from datetime import datetime, timezone
    now = datetime.now(timezone.utc)
    m = StagingManifest(
        job_id="job-123",
        started_at=now,
        completed_at=now,
        total_observations=1,
        total_chunk_sets=1,
        total_chunks=5,
        total_errors=0,
    )
    m_dict = json.loads(m.model_dump_json())
    jsonschema.validate(instance=m_dict, schema=manifest_schema)

def test_crawler_error_schema():
    error_schema = load_schema("crawler-error.schema.json")
    err = CrawlerError(
        job_id="job-123",
        source_id="vbpl",
        url="https://example.com/failed",
        stage=ErrorStage.FETCH,
        error_type="HTTPError",
        message="404 Not Found",
    )
    err_dict = json.loads(err.model_dump_json())
    jsonschema.validate(instance=err_dict, schema=error_schema)
