import json
from pathlib import Path
import subprocess
import pytest
from rag_data_scraper.adapters.gov_portal import GovPortalAdapter
from rag_data_scraper.crawler.engine import CrawlEngine
from rag_data_scraper.source_registry import ResolvedSourceProfile
from unittest.mock import AsyncMock

@pytest.mark.asyncio
async def test_end_to_end_crawl_and_dotnet_validation(tmp_path):
    mock_adapter = AsyncMock()
    mock_adapter.source_id = "gov_portal"
    mock_adapter.source_namespace = "vanban.chinhphu.vn"
    mock_adapter.authority_namespace = "gov.vn"
    mock_adapter.default_identity_strategy = "authoritative"
    mock_adapter.source_profile = ResolvedSourceProfile(
        entry_id="vanban-chinhphu-official",
        adapter="gov_portal",
        source_id="gov_portal",
        source_namespace="vanban.chinhphu.vn",
        authority_namespace="gov.vn",
        corpus_type="legal_reference",
        source_trust_tier="official",
        publish_policy="authoritative",
        allowed_hosts=["vanban.chinhphu.vn"],
        default_issuer="Chính phủ",
        language="vi",
        registry_version="test-registry-1",
    )
    
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
        metadata={
            "document_number": "100/2024/NĐ-CP",
            "document_type": "Nghị định",
            "issuer": "Chính phủ",
            "issued_date": "2024-05-01",
            "legal_status": "current",
            "effective_from": "2024-06-01",
        },
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
    assert manifest_data["schema_version"] == "1.0"
    assert manifest_data["corpus_type"] == "legal_reference"
    assert manifest_data["source_registry_version"] == "test-registry-1"

    observation = json.loads(
        (staging_dir / "document-observations.jsonl")
        .read_text(encoding="utf-8")
        .splitlines()[0]
    )
    assert observation["source_provenance"]["source_trust_tier"] == "official"
    assert observation["source_provenance"]["source_version"].startswith("sha256:")
    assert observation["legal_metadata"]["document_number"] == "100/2024/NĐ-CP"
    assert observation["legal_metadata"]["legal_status"] == "current"

    # Call the standalone ingestion CLI through its stable validate command.
    project_path = (
        Path(__file__).parent.parent.parent.parent
        / "tools"
        / "DigitalOps.RagIngestion"
        / "DigitalOps.RagIngestion.csproj"
    )
    if project_path.exists():
        cmd = [
            "dotnet", "run", "--project", str(project_path.resolve()), "--",
            "validate", "--staging-dir", str(staging_dir.resolve())
        ]
        result = subprocess.run(cmd, capture_output=True, text=True)
        assert result.returncode == 0, (
            f"DigitalOps.RagIngestion validation failed: {result.stderr}"
        )
        assert "[VALIDATE] Complete" in result.stdout

        registry_path = tmp_path / "source-registry.json"
        registry_path.write_text(
            json.dumps(
                {
                    "schema_version": "1.0",
                    "registry_version": "test-registry-1",
                    "sources": [
                        {
                            "entry_id": "vanban-chinhphu-official",
                            "adapter": "gov_portal",
                            "source_id": "gov_portal",
                            "source_namespace": "vanban.chinhphu.vn",
                            "authority_namespace": "gov.vn",
                            "corpus_type": "legal_reference",
                            "source_trust_tier": "official",
                            "publish_policy": "authoritative",
                            "allowed_hosts": ["vanban.chinhphu.vn"],
                            "default_issuer": "Chính phủ",
                            "language": "vi",
                        }
                    ],
                },
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        admit = subprocess.run(
            [
                "dotnet", "run", "--project", str(project_path.resolve()), "--",
                "admit", "--staging-dir", str(staging_dir.resolve()),
                "--source-registry", str(registry_path.resolve()),
                "--approved-by", "pytest-data-steward",
                "--approval-reference", "PY-E2E-001",
            ],
            capture_output=True,
            text=True,
        )
        assert admit.returncode == 0, (
            f"DigitalOps.RagIngestion admission failed: {admit.stderr}"
        )
        receipt = json.loads(
            (staging_dir / "admission.json").read_text(encoding="utf-8")
        )
        assert receipt["status"] == "approved"
        assert receipt["approval_reference"] == "PY-E2E-001"
        assert receipt["approved_observation_ids"] == [observation["observation_id"]]
