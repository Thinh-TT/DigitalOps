from __future__ import annotations

import json
from pathlib import Path
from types import SimpleNamespace

import pytest
from docx import Document

from rag_data_scraper.adapters.base import BaseAdapter, CrawlResult
from rag_data_scraper.adapters.generic import GenericWebAdapter
from rag_data_scraper.crawler.engine import CrawlEngine
from rag_data_scraper.crawler.policy import CrawlPolicy
from rag_data_scraper.extractors.legacy_doc_extractor import LegacyDocExtractor
from rag_data_scraper.http_fetcher import FetchResponse, UrlPolicy


class _MattranFixtureFetcher:
    def __init__(self) -> None:
        self.policy = UrlPolicy(
            {"m.mattran.org.vn"},
            allow_related_asset_hosts=True,
        )

    async def get(self, url: str, *, headers=None) -> FetchResponse:
        html = """
        <html><head><title>Van ban huong dan</title></head><body><table>
          <tr><td><b>Loại văn bản</b>: Công văn<br>
                  <b>Ngày ban hành</b>: 01/08/2026</td></tr>
          <tr><td><b>Trích yếu</b>
            <p><a href="http://static.mattran.org.vn/files/law.doc">
              Công văn thử nghiệm
            </a></p>
            <a href="http://127.0.0.1/private.doc">blocked</a>
          </td></tr>
        </table></body></html>
        """.encode("utf-8")
        return FetchResponse(
            requested_url=url,
            final_url=url,
            status_code=200,
            headers={"Content-Type": "text/html; charset=utf-8"},
            content=html,
        )

    async def aclose(self) -> None:
        return None


@pytest.mark.asyncio
async def test_mattran_listing_emits_records_and_upgrades_allowed_http_assets(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        UrlPolicy,
        "_reject_non_public_addresses",
        staticmethod(lambda hostname, port: None),
    )
    fetcher = _MattranFixtureFetcher()
    assert "static.mattran.org.vn" in fetcher.policy.allowed_hosts
    assert "cms.mattran.org.vn" in fetcher.policy.allowed_hosts
    adapter = GenericWebAdapter(
        allowed_hosts={"m.mattran.org.vn"},
        fetcher=fetcher,
    )

    result = await adapter.fetch_and_parse(
        "https://m.mattran.org.vn/van-ban-huong-dan.html"
    )

    assert result.discovery_only is True
    assert len(result.embedded_documents) == 1
    record = result.embedded_documents[0]
    assert record.metadata["document_type"] == "Công văn"
    assert record.metadata["issued_date"] == "01/08/2026"
    assert record.discovered_links == [
        "https://static.mattran.org.vn/files/law.doc"
    ]
    assert result.discovered_links == []


class _EmbeddedListingAdapter(BaseAdapter):
    source_id = "test"
    source_namespace = "example.org"
    authority_namespace = None
    default_identity_strategy = "content_only"

    async def fetch_and_parse(self, url: str) -> CrawlResult:
        if url.endswith("/list"):
            documents = []
            for index in range(3):
                attachment = f"https://example.org/files/{index}.pdf"
                documents.append(
                    CrawlResult(
                        url=f"{url}#record-{index}",
                        canonical_key=f"record:{index}",
                        title=f"Record {index}",
                        html_or_bytes=(
                            "<html><body><h1>Record "
                            f"{index}</h1><p>Primary content {index}</p>"
                            "</body></html>"
                        ).encode(),
                        mime_type="text/html",
                        document_identity_strategy="content_only",
                        source_namespace=self.source_namespace,
                        authority_namespace=None,
                        metadata={"crawl_role": "primary_record"},
                        discovered_links=[attachment],
                        final_url=f"{url}#record-{index}",
                    )
                )
            return CrawlResult(
                url=url,
                canonical_key="listing",
                title="Listing",
                html_or_bytes=b"<html><body>Listing page</body></html>",
                mime_type="text/html",
                document_identity_strategy="content_only",
                source_namespace=self.source_namespace,
                authority_namespace=None,
                metadata={},
                discovered_links=[],
                embedded_documents=documents,
                discovery_only=True,
            )
        suffix = url.rsplit("/", 1)[-1]
        return CrawlResult(
            url=url,
            canonical_key=f"attachment:{suffix}",
            title=suffix,
            html_or_bytes=(
                f"<html><body><p>Attachment content {suffix}</p></body></html>"
            ).encode(),
            mime_type="text/html",
            document_identity_strategy="content_only",
            source_namespace=self.source_namespace,
            authority_namespace=None,
            metadata={},
            discovered_links=[],
        )


@pytest.mark.asyncio
async def test_engine_limits_primary_records_without_counting_attachments(
    tmp_path: Path,
) -> None:
    engine = CrawlEngine(
        _EmbeddedListingAdapter(),
        state_db_path=tmp_path / "state.db",
        staging_dir=tmp_path / "staging",
        raw_dir=tmp_path / "raw",
        max_total_resources=10,
        max_concurrent=2,
    )

    output = await engine.run_job(
        "JOB_EMBEDDED_LIMIT",
        ["https://example.org/list"],
        max_depth=1,
        max_resources=2,
    )
    manifest = json.loads((output / "manifest.json").read_text("utf-8"))

    assert manifest["total_observations"] == 4
    assert engine.last_run_metrics["primary_documents_created"] == 2
    assert engine.last_run_metrics["records_discovered"] == 3
    assert engine.last_run_metrics["attachments_discovered"] == 2
    assert engine.last_run_metrics["attachments_fetched"] == 2


@pytest.mark.asyncio
async def test_engine_disables_all_record_attachments_even_without_suffix(
    tmp_path: Path,
) -> None:
    class ExtensionlessAttachmentAdapter(_EmbeddedListingAdapter):
        async def fetch_and_parse(self, url: str) -> CrawlResult:
            result = await super().fetch_and_parse(url)
            if result.discovery_only:
                for index, document in enumerate(result.embedded_documents):
                    document.discovered_links = [
                        f"https://example.org/download?id={index}"
                    ]
            return result

    engine = CrawlEngine(
        ExtensionlessAttachmentAdapter(),
        state_db_path=tmp_path / "state.db",
        staging_dir=tmp_path / "staging",
        raw_dir=tmp_path / "raw",
        max_total_resources=10,
        crawl_policy=CrawlPolicy(include_attachments=False),
    )

    output = await engine.run_job(
        "JOB_NO_ATTACHMENTS",
        ["https://example.org/list"],
        max_depth=1,
        max_resources=2,
    )
    manifest = json.loads((output / "manifest.json").read_text("utf-8"))

    assert manifest["total_observations"] == 2
    assert engine.last_run_metrics["attachments_discovered"] == 2
    assert engine.last_run_metrics["attachments_fetched"] == 0


def test_legacy_doc_extractor_uses_bounded_headless_conversion(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    executable = tmp_path / "soffice.exe"
    executable.write_bytes(b"fixture")
    source = tmp_path / "legacy.doc"
    source.write_bytes(b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1fixture")

    def fake_run(command, **kwargs):
        output_dir = Path(command[command.index("--outdir") + 1])
        document = Document()
        document.add_paragraph("Nội dung DOC cũ đã chuyển đổi.")
        document.save(output_dir / "input.docx")
        assert kwargs["shell"] is False
        assert kwargs["timeout"] == 5
        return SimpleNamespace(returncode=0)

    monkeypatch.setattr(
        "rag_data_scraper.extractors.legacy_doc_extractor.subprocess.run",
        fake_run,
    )
    extracted = LegacyDocExtractor(
        soffice_cmd=str(executable),
        timeout_seconds=5,
        max_output_bytes=2 * 1024 * 1024,
    ).extract(source)

    assert [block.text for block in extracted.blocks] == [
        "Nội dung DOC cũ đã chuyển đổi."
    ]
    assert extracted.document_metadata["legacy_doc_converted"] is True
