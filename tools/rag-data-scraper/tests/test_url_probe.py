from __future__ import annotations

from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from rag_data_scraper.adapters.base import BaseAdapter, CrawlResult
from rag_data_scraper.crawler.url_probe import UrlProbeService
from rag_data_scraper.web import app as web_app


def _document(
    key: str,
    title: str,
    attachment: str,
) -> CrawlResult:
    return CrawlResult(
        url=f"https://example.org/list#document-{key}",
        canonical_key=key,
        title=title,
        html_or_bytes=f"<p>{title}</p>".encode(),
        mime_type="text/html",
        document_identity_strategy="content_only",
        source_namespace="example.org",
        authority_namespace=None,
        metadata={},
        discovered_links=[attachment],
    )


def _listing(
    url: str,
    *,
    links: list[str] | None = None,
    documents: list[CrawlResult] | None = None,
    discovery_only: bool = True,
) -> CrawlResult:
    return CrawlResult(
        url=url,
        canonical_key=f"listing:{url}",
        title="Listing",
        html_or_bytes=b"<html><body>Listing</body></html>",
        mime_type="text/html",
        document_identity_strategy="content_only",
        source_namespace="example.org",
        authority_namespace=None,
        metadata={},
        discovered_links=links or [],
        final_url=url,
        embedded_documents=documents or [],
        discovery_only=discovery_only,
    )


class _ProbeAdapter(BaseAdapter):
    def __init__(self, pages: dict[str, CrawlResult]) -> None:
        self.pages = pages
        self.requested: list[str] = []
        self.closed = False

    @property
    def source_id(self) -> str:
        return "probe_fixture"

    @property
    def source_namespace(self) -> str:
        return "example.org"

    @property
    def authority_namespace(self) -> None:
        return None

    @property
    def default_identity_strategy(self) -> str:
        return "content_only"

    async def fetch_and_parse(self, url: str) -> CrawlResult | None:
        self.requested.append(url)
        return self.pages.get(url)

    async def aclose(self) -> None:
        self.closed = True


def _exact_pages() -> dict[str, CrawlResult]:
    seed = "https://example.org/list"
    page_two = "https://example.org/list?page=2"
    page_three = "https://example.org/list?page=3"
    document_one = _document(
        "document:1",
        "Văn bản 1",
        "https://example.org/files/one.pdf",
    )
    return {
        seed: _listing(
            seed,
            links=[page_two, page_three],
            documents=[
                document_one,
                _document(
                    "document:2",
                    "Văn bản 2",
                    "https://example.org/download?id=two",
                ),
            ],
        ),
        page_two: _listing(
            page_two,
            links=[page_three],
            documents=[
                document_one,
                _document(
                    "document:3",
                    "Văn bản 3",
                    "https://example.org/files/three.docx",
                ),
            ],
        ),
        page_three: _listing(
            page_three,
            documents=[
                _document(
                    "document:4",
                    "Văn bản 4",
                    "https://example.org/files/four.doc",
                )
            ],
        ),
    }


@pytest.mark.asyncio
async def test_url_probe_counts_records_pagination_and_attachments() -> None:
    adapter = _ProbeAdapter(_exact_pages())

    summary = await UrlProbeService(
        adapter,
        max_pagination_pages=2,
    ).run(["https://example.org/list"])

    assert summary.status == "COMPLETE"
    assert summary.count_mode == "EXACT_LISTING_RECORDS"
    assert summary.pages_scanned == 3
    assert summary.listing_pages_detected == 3
    assert summary.pagination_pages_detected == 2
    assert summary.pagination_pages_followed == 2
    assert summary.documents_detected == 4
    assert summary.attachments_detected == 4
    assert summary.pagination_limit_reached is False
    assert adapter.requested == [
        "https://example.org/list",
        "https://example.org/list?page=2",
        "https://example.org/list?page=3",
    ]


@pytest.mark.asyncio
async def test_url_probe_marks_result_partial_at_pagination_limit() -> None:
    adapter = _ProbeAdapter(_exact_pages())

    summary = await UrlProbeService(
        adapter,
        max_pagination_pages=1,
    ).run(["https://example.org/list"])

    assert summary.status == "PARTIAL"
    assert summary.pages_scanned == 2
    assert summary.pagination_pages_detected == 2
    assert summary.pagination_pages_followed == 1
    assert summary.documents_detected == 3
    assert summary.pagination_limit_reached is True


@pytest.mark.asyncio
async def test_url_probe_estimates_generic_content_links() -> None:
    seed = "https://example.org/news"
    page_two = "https://example.org/news?page=2"
    adapter = _ProbeAdapter(
        {
            seed: _listing(
                seed,
                links=[
                    page_two,
                    "https://example.org/news/first.html",
                    "https://example.org/news/second.html",
                ],
                discovery_only=False,
            ),
            page_two: _listing(
                page_two,
                links=["https://example.org/news/third.html"],
                discovery_only=False,
            ),
        }
    )

    summary = await UrlProbeService(
        adapter,
        max_pagination_pages=5,
    ).run([seed])

    assert summary.status == "COMPLETE"
    assert summary.count_mode == "ESTIMATED_LINKS"
    assert summary.documents_detected == 3
    assert summary.listing_pages_detected == 2


@pytest.mark.asyncio
async def test_url_probe_rejects_attachment_seed() -> None:
    adapter = _ProbeAdapter({})

    with pytest.raises(ValueError, match="HTML pages"):
        await UrlProbeService(
            adapter,
            max_pagination_pages=5,
        ).run(["https://example.org/file.pdf"])


def test_url_probe_api_returns_read_only_discovery_result(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    adapter = _ProbeAdapter(_exact_pages())
    monkeypatch.setattr(
        web_app.Settings,
        "load_from_yaml",
        lambda _path: object(),
    )
    monkeypatch.setattr(
        web_app,
        "get_adapter",
        lambda source, urls, settings: adapter,
    )

    with TestClient(web_app.app) as client:
        response = client.post(
            "/api/url-probes",
            json={
                "source": "generic_web",
                "urls": ["https://example.org/list"],
                "max_pagination_pages": 2,
            },
        )

    assert response.status_code == 200
    payload = response.json()
    assert payload["count_mode"] == "EXACT_LISTING_RECORDS"
    assert payload["documents_detected"] == 4
    assert payload["pagination_pages_detected"] == 2
    assert adapter.closed is True


def test_url_probe_api_rejects_non_https_before_adapter_creation(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    adapter_called = False

    def fail_if_called(*_args):
        nonlocal adapter_called
        adapter_called = True
        raise AssertionError("adapter should not be created")

    monkeypatch.setattr(web_app, "get_adapter", fail_if_called)
    with TestClient(web_app.app) as client:
        response = client.post(
            "/api/url-probes",
            json={
                "source": "generic_web",
                "urls": ["http://example.org/list"],
            },
        )

    assert response.status_code == 422
    assert adapter_called is False


def test_url_probe_api_rejects_oversized_url() -> None:
    oversized_url = "https://example.org/" + ("a" * 2048)

    with TestClient(web_app.app) as client:
        response = client.post(
            "/api/url-probes",
            json={
                "source": "generic_web",
                "urls": [oversized_url],
            },
        )

    assert response.status_code == 422


def test_dashboard_exposes_url_probe_controls() -> None:
    dashboard = (
        Path(web_app.__file__).parent / "static" / "index.html"
    ).read_text(encoding="utf-8")

    assert 'id="urlProbeBtn"' in dashboard
    assert 'id="urlProbeResult"' in dashboard
    assert "async function probeUrls()" in dashboard
    assert "/api/url-probes" in dashboard
    assert "Không tạo job" not in dashboard
    assert "không tạo job" in dashboard
