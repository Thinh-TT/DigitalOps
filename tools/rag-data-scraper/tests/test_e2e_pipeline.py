import json
from pathlib import Path
import subprocess
import pytest
from rag_data_scraper.adapters.gov_portal import GovPortalAdapter
from rag_data_scraper.crawler.engine import CrawlEngine
from unittest.mock import AsyncMock

@pytest.mark.asyncio
async def test_end_to_end_crawl_and_dotnet_validation(tmp_path):
    mock_adapter = AsyncMock()
    mock_adapter.source_id = "gov_portal"
    mock_adapter.source_namespace = "vanban.chinhphu.vn"
    mock_adapter.authority_namespace = "gov.vn"
    mock_adapter.default_identity_strategy = "authoritative"
    
    from rag_data_scraper.adapters.base import CrawlResult
    mock_adapter.fetch_and_parse.return_value = CrawlResult(
        url="https://vanban.chinhphu.vn/doc-100",
        canonical_key="gov:nghidinh:100/2024/ND-CP",
        title="Nghị định 100/2024/NĐ-CP",
        html_or_bytes=b"<html><body><h1>Title</h1><p>So: 100/2024/ND-CP ngay 01 thang 05 nam 2024. Quan ly DigitalOps.</p></body></html>",
        mime_type="text/html",
        document_identity_strategy="authoritative",
        source_namespace="vanban.chinhphu.vn",
        authority_namespace="gov.vn",
        metadata={"document_number": "100/2024/NĐ-CP", "document_type": "Nghị định"},
        discovered_links=[]
    )

    staging_base = tmp_path / "staging"
    engine = CrawlEngine(
        adapter=mock_adapter,
        state_db_path=tmp_path / "state" / "crawler.db",
        staging_dir=staging_base,
        raw_dir=tmp_path / "raw"
    )

    job_id = "job-e2e-100"
    staging_dir = await engine.run_job(job_id, ["https://vanban.chinhphu.vn/doc-100"])

    assert staging_dir.exists()
    manifest_file = staging_dir / "manifest.json"
    assert manifest_file.exists()

    with open(manifest_file, "r", encoding="utf-8") as f:
        manifest_data = json.load(f)

    assert manifest_data["job_id"] == job_id
    assert manifest_data["total_observations"] == 1
    assert manifest_data["total_chunks"] >= 1

    # Run DxOs.Workers CLI with --validate-only flag
    project_path = Path(__file__).parent.parent.parent.parent / "DxOs.Workers" / "DxOs.Workers.csproj"
    if project_path.exists():
        cmd = [
            "dotnet", "run", "--project", str(project_path.resolve()), "--",
            "--staging-dir", str(staging_dir.resolve()),
            "--validate-only"
        ]
        result = subprocess.run(cmd, capture_output=True, text=True)
        assert result.returncode == 0, f"DxOs.Workers validation failed: {result.stderr}"
        assert "[VALIDATE-ONLY] Complete" in result.stdout
