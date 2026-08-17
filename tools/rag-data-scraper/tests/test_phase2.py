import asyncio
from pathlib import Path
import pytest
from unittest.mock import AsyncMock, patch

from rag_data_scraper.adapters.gov_portal import GovPortalAdapter
from rag_data_scraper.adapters.legal_aggregator import LegalAggregatorAdapter
from rag_data_scraper.adapters.base import CrawlResult
from rag_data_scraper.crawler.engine import CrawlEngine

def test_gov_portal_adapter_metadata():
    adapter = GovPortalAdapter()
    assert adapter.source_id == "vanban_chinhphu"
    assert adapter.default_identity_strategy == "authoritative"
    assert adapter.authority_namespace == "gov.vn"

def test_legal_aggregator_adapter_metadata():
    adapter = LegalAggregatorAdapter()
    assert adapter.source_id == "thuvienphapluat"
    assert adapter.default_identity_strategy == "canonical_metadata"
    assert adapter.authority_namespace == "gov.vn"

@pytest.mark.asyncio
async def test_crawl_engine_mock_run(tmp_path):
    mock_adapter = AsyncMock()
    mock_adapter.source_id = "test_source"
    mock_adapter.source_namespace = "test.org"
    mock_adapter.authority_namespace = "gov.vn"
    mock_adapter.default_identity_strategy = "authoritative"
    
    mock_result = CrawlResult(
        url="https://test.org/doc1",
        canonical_key="gov:doc:123",
        title="Nghị định 123/2024/NĐ-CP",
        html_or_bytes=b"<html><body><h1>Title</h1><p>So: 123/2024/ND-CP. Content test.</p></body></html>",
        mime_type="text/html",
        document_identity_strategy="authoritative",
        source_namespace="test.org",
        authority_namespace="gov.vn",
        metadata={"document_number": "123/2024/NĐ-CP"},
        discovered_links=[]
    )
    mock_adapter.fetch_and_parse.return_value = mock_result

    engine = CrawlEngine(
        adapter=mock_adapter,
        state_db_path=tmp_path / "state" / "crawler.db",
        staging_dir=tmp_path / "staging",
        raw_dir=tmp_path / "raw"
    )

    output_dir = await engine.run_job("test-job-p2", ["https://test.org/doc1"])

    assert output_dir.exists()
    assert (output_dir / "manifest.json").exists()
    assert (output_dir / "document-observations.jsonl").exists()
    assert (output_dir / "chunks.jsonl").exists()
