import asyncio
import hashlib
import io
import json
import re
import sqlite3
from pathlib import Path
from types import SimpleNamespace
from uuid import uuid4

import httpx
from PIL import Image
import pytest

from rag_data_scraper.adapters.base import (
    BaseAdapter,
    CrawlResult,
    NotModifiedResult,
)
from rag_data_scraper.adapters.generic import GenericWebAdapter
from rag_data_scraper.adapters.http_source import UnsupportedContentTypeError
from rag_data_scraper.chunkers.structure_chunker import StructureChunker
from rag_data_scraper.cleaners.text_cleaner import TextCleaner
from rag_data_scraper.config import ChunkerSettings, Settings
from rag_data_scraper.crawler.engine import CrawlEngine
from rag_data_scraper.crawler.policy import CrawlPolicy
from rag_data_scraper.db.state_store import CrawlerStateStore
from rag_data_scraper.exporters.preview_generator import PreviewGenerator
from rag_data_scraper.extractors import pdf_extractor as pdf_extractor_module
from rag_data_scraper.extractors.base import (
    BlockType,
    ContentBlock,
    ExtractedDocument,
)
from rag_data_scraper.extractors.pdf_extractor import (
    OCRUnavailableError,
    PDFExtractor,
)
from rag_data_scraper.http_fetcher import (
    FetchResponse,
    RedirectPolicyError,
    ResponseTooLargeError,
    SafeHttpFetcher,
    UnsafeUrlError,
    UrlPolicy,
)
from rag_data_scraper.paths import resolve_job_dir, validate_job_id


def test_job_id_and_resolved_path_reject_traversal(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        validate_job_id("../../sentinel")
    with pytest.raises(ValueError):
        resolve_job_dir(tmp_path, r"..\..\sentinel")
    assert resolve_job_dir(tmp_path, "JOB_2026-08.03").parent == tmp_path.resolve()


def test_adaptive_chunk_limits_load_from_settings_and_validate_order() -> None:
    settings = Settings.load_from_yaml(
        Path(__file__).parent.parent / "config" / "settings.yaml"
    )
    assert (
        settings.chunker.target_tokens,
        settings.chunker.soft_max_tokens,
        settings.chunker.max_tokens,
        settings.chunker.overlap_tokens,
    ) == (448, 480, 512, 64)
    assert settings.crawler.max_response_bytes == 32 * 1024 * 1024
    assert settings.ocr.max_pages == 50
    assert settings.ocr.max_image_pixels == 3_000_000

    with pytest.raises(ValueError):
        ChunkerSettings(
            target_tokens=448,
            soft_max_tokens=440,
            max_tokens=512,
            overlap_tokens=64,
        )


def _png_fixture() -> bytes:
    buffer = io.BytesIO()
    Image.new("RGB", (20, 20), "white").save(buffer, format="PNG")
    return buffer.getvalue()


def test_pdf_ocr_decodes_image_bytes_once_and_marks_page_limit(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    image_file = SimpleNamespace(data=_png_fixture())

    class FakePage:
        images = [image_file]

        @staticmethod
        def extract_text() -> str:
            return ""

    fake_reader = SimpleNamespace(
        metadata=None,
        pages=[FakePage(), FakePage()],
    )
    monkeypatch.setattr(
        pdf_extractor_module,
        "PdfReader",
        lambda path: fake_reader,
    )
    calls = 0

    def fake_image_to_data(*args, **kwargs):
        nonlocal calls
        calls += 1
        assert args[0].size == (20, 20)
        assert kwargs["lang"] == "vie+eng"
        return {
            "text": ["Ủy", "ban"],
            "block_num": [1, 1],
            "par_num": [1, 1],
            "line_num": [1, 1],
            "conf": ["95", "85"],
        }

    fake_pytesseract = SimpleNamespace(
        image_to_data=fake_image_to_data,
        Output=SimpleNamespace(DICT="dict"),
    )
    monkeypatch.setattr(
        pdf_extractor_module,
        "pytesseract",
        fake_pytesseract,
    )
    source = tmp_path / "scan.pdf"
    source.write_bytes(b"%PDF fixture")
    extractor = PDFExtractor(max_ocr_pages=1)
    monkeypatch.setattr(
        extractor,
        "_configure_ocr",
        lambda: ("vie+eng", ""),
    )

    document = extractor.extract(source)

    assert calls == 1
    assert [block.text for block in document.blocks] == ["Ủy ban"]
    assert document.ocr_used is True
    assert document.ocr_confidence == pytest.approx(0.9)
    assert document.truncated is True
    assert document.document_metadata["ocr_pages_processed"] == 1
    assert document.document_metadata["ocr_pages_omitted"] == 1


def test_pdf_without_text_reports_missing_ocr_runtime(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    fake_page = SimpleNamespace(
        extract_text=lambda: "",
        images=[],
    )
    monkeypatch.setattr(
        pdf_extractor_module,
        "PdfReader",
        lambda path: SimpleNamespace(metadata=None, pages=[fake_page]),
    )
    source = tmp_path / "scan.pdf"
    source.write_bytes(b"%PDF fixture")
    extractor = PDFExtractor()
    monkeypatch.setattr(extractor, "_configure_ocr", lambda: None)

    with pytest.raises(OCRUnavailableError, match="no text layer"):
        extractor.extract(source)


def test_url_policy_rejects_private_and_out_of_scope_hosts() -> None:
    policy = UrlPolicy({"1.1.1.1"})
    assert policy.normalize_and_validate("https://1.1.1.1/a b") == (
        "https://1.1.1.1/a%20b"
    )
    with pytest.raises(UnsafeUrlError):
        policy.normalize_and_validate("https://127.0.0.1/admin")
    with pytest.raises(UnsafeUrlError):
        policy.normalize_and_validate("https://8.8.8.8/")
    with pytest.raises(UnsafeUrlError):
        policy.normalize_and_validate("file:///etc/passwd")


@pytest.mark.asyncio
async def test_fetcher_revalidates_redirects_and_bounds_body() -> None:
    async def redirect_handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            302,
            headers={"Location": "https://127.0.0.1/private"},
            request=request,
        )

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(redirect_handler)
    ) as client:
        fetcher = SafeHttpFetcher(UrlPolicy({"1.1.1.1"}), client=client)
        with pytest.raises(UnsafeUrlError):
            await fetcher.get("https://1.1.1.1/start")

    async def large_handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, content=b"12345", request=request)

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(large_handler)
    ) as client:
        fetcher = SafeHttpFetcher(
            UrlPolicy({"1.1.1.1"}),
            client=client,
            max_response_bytes=4,
        )
        with pytest.raises(ResponseTooLargeError):
            await fetcher.get("https://1.1.1.1/document")


@pytest.mark.asyncio
async def test_fetcher_retries_transient_status_and_reports_attempts() -> None:
    requests = 0

    async def handler(request: httpx.Request) -> httpx.Response:
        nonlocal requests
        requests += 1
        if requests == 1:
            return httpx.Response(
                429,
                headers={"Retry-After": "0"},
                request=request,
            )
        return httpx.Response(200, content=b"ok", request=request)

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(handler)
    ) as client:
        fetcher = SafeHttpFetcher(
            UrlPolicy({"1.1.1.1"}),
            client=client,
            max_attempts=2,
            backoff_base_seconds=0,
            max_backoff_seconds=0,
            per_host_delay_seconds=0,
        )
        response = await fetcher.get("https://1.1.1.1/transient")

    assert response.status_code == 200
    assert response.content == b"ok"
    assert response.attempt_count == 2
    assert requests == 2


@pytest.mark.asyncio
async def test_fetcher_upgrades_authorized_asset_redirect_to_https(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        UrlPolicy,
        "_reject_non_public_addresses",
        staticmethod(lambda hostname, port: None),
    )
    requested_urls: list[str] = []

    async def handler(request: httpx.Request) -> httpx.Response:
        requested_urls.append(str(request.url))
        if request.url.host == "m.example.org":
            return httpx.Response(
                301,
                headers={
                    "Location": "http://static.example.org/files/law.pdf"
                },
                request=request,
            )
        return httpx.Response(
            200,
            headers={"Content-Type": "application/pdf"},
            content=b"%PDF-1.7 fixture",
            request=request,
        )

    policy = UrlPolicy(
        {"m.example.org"},
        allow_related_asset_hosts=True,
    )
    assert "static.example.org" in policy.allowed_hosts
    async with httpx.AsyncClient(
        transport=httpx.MockTransport(handler)
    ) as client:
        response = await SafeHttpFetcher(
            policy,
            client=client,
            per_host_delay_seconds=0,
        ).get("https://m.example.org/files/law.pdf")

    assert requested_urls == [
        "https://m.example.org/files/law.pdf",
        "https://static.example.org/files/law.pdf",
    ]
    assert response.final_url == "https://static.example.org/files/law.pdf"
    assert response.redirect_chain == (
        "https://static.example.org/files/law.pdf",
    )


@pytest.mark.asyncio
async def test_fetcher_treats_304_after_redirect_as_not_modified(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        UrlPolicy,
        "_reject_non_public_addresses",
        staticmethod(lambda hostname, port: None),
    )
    requested_urls: list[str] = []

    async def handler(request: httpx.Request) -> httpx.Response:
        requested_urls.append(str(request.url))
        if request.url.host == "m.example.org":
            return httpx.Response(
                301,
                headers={
                    "Location": "http://static.example.org/files/law.pdf"
                },
                request=request,
            )
        assert request.headers["If-None-Match"] == '"version-1"'
        return httpx.Response(304, headers={"ETag": '"version-1"'}, request=request)

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(handler)
    ) as client:
        response = await SafeHttpFetcher(
            UrlPolicy(
                {"m.example.org"},
                allow_related_asset_hosts=True,
            ),
            client=client,
            per_host_delay_seconds=0,
        ).get(
            "https://m.example.org/files/law.pdf",
            headers={"If-None-Match": '"version-1"'},
        )

    assert requested_urls == [
        "https://m.example.org/files/law.pdf",
        "https://static.example.org/files/law.pdf",
    ]
    assert response.status_code == 304
    assert response.final_url == "https://static.example.org/files/law.pdf"
    assert response.content == b""
    assert response.redirect_chain == (
        "https://static.example.org/files/law.pdf",
    )


@pytest.mark.asyncio
async def test_fetcher_reports_blocked_redirect_target(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        UrlPolicy,
        "_reject_non_public_addresses",
        staticmethod(lambda hostname, port: None),
    )

    async def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            302,
            headers={"Location": "https://outside.example.net/private"},
            request=request,
        )

    async with httpx.AsyncClient(
        transport=httpx.MockTransport(handler)
    ) as client:
        fetcher = SafeHttpFetcher(
            UrlPolicy({"example.org"}),
            client=client,
            per_host_delay_seconds=0,
        )
        with pytest.raises(RedirectPolicyError) as raised:
            await fetcher.get("https://example.org/start")

    assert "example.org/start -> https://outside.example.net/private" in str(
        raised.value
    )
    assert "outside the crawl scope" in str(raised.value)


def test_crawl_policy_canonicalizes_deduplicates_and_filters_attachments() -> None:
    policy = CrawlPolicy(include_attachments=False)
    candidates = policy.candidates(
        [
            "HTTPS://Example.Gov.VN:443/van-ban?id=2&utm_source=test&a=1#top",
            "https://example.gov.vn/van-ban?a=1&id=2",
            "https://example.gov.vn/files/law.pdf",
            "https://example.gov.vn/static/site.js",
        ]
    )

    assert candidates == ["https://example.gov.vn/van-ban?a=1&id=2"]
    assert policy.priority(candidates[0]) == 50
    assert CrawlPolicy().priority("https://example.gov.vn/law.pdf") == 100
    assert not CrawlPolicy().should_visit("https://example.gov.vn/legacy.doc")
    assert CrawlPolicy().is_pagination("https://example.gov.vn/list?page=3")
    assert CrawlPolicy().canonicalize(
        "https://example.gov.vn/list?page=1"
    ) == "https://example.gov.vn/list"
    assert CrawlPolicy().priority(
        "https://example.gov.vn/van-ban?page=3"
    ) == 20
    assert CrawlPolicy().next_depth(
        "https://example.gov.vn/list?page=3",
        parent_depth=1,
        max_depth=1,
    ) == 1


class _FixtureFetcher:
    def __init__(self) -> None:
        self.policy = UrlPolicy({"1.1.1.1"})
        self.calls = 0
        self.closed = 0

    async def get(self, url: str, *, headers=None) -> FetchResponse:
        self.calls += 1
        html = b"""
            <html><head><title>Fixture</title>
            <link rel="canonical" href="/document?id=2&utm_source=test&a=1">
            </head><body>
            <a href="/attachment.pdf?utm_campaign=test">PDF</a>
            <a href="/attachment.pdf">Duplicate PDF</a>
            <div class="pagination"><a href="/list?page=2">2</a></div>
            <footer><a href="footer__logo">Broken footer link</a></footer>
            </body></html>
        """
        return FetchResponse(
            requested_url=url,
            final_url=url,
            status_code=200,
            headers={"Content-Type": "text/html; charset=utf-8"},
            content=html,
        )

    async def aclose(self) -> None:
        self.closed += 1


@pytest.mark.asyncio
async def test_http_adapter_reuses_fetcher_and_validates_document_signatures() -> None:
    fetcher = _FixtureFetcher()
    adapter = GenericWebAdapter(
        allowed_hosts={"1.1.1.1"},
        fetcher=fetcher,
    )

    first = await adapter.fetch_and_parse("https://1.1.1.1/start")
    second = await adapter.fetch_and_parse("https://1.1.1.1/start-2")
    await adapter.aclose()

    assert first is not None and second is not None
    assert first.url == "https://1.1.1.1/document?a=1&id=2"
    assert first.discovered_links == [
        "https://1.1.1.1/attachment.pdf",
        "https://1.1.1.1/list?page=2",
    ]
    assert fetcher.calls == 2
    assert fetcher.closed == 1
    with pytest.raises(UnsupportedContentTypeError):
        adapter._classify(
            "application/pdf",
            "https://1.1.1.1/not-really.pdf",
            b"<html>error</html>",
        )
    with pytest.raises(UnsupportedContentTypeError):
        adapter._classify(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "https://1.1.1.1/not-really.docx",
            b"PK-not-a-zip",
        )


def test_contiguous_chunks_match_normalized_text_offsets() -> None:
    extracted = ExtractedDocument(
        source_uri="memory://test",
        title="Test",
        mime_type="text/html",
        raw_sha256="0" * 64,
        blocks=[
            ContentBlock(BlockType.HEADING, "  Muc  1  ", heading_level=1),
            ContentBlock(
                BlockType.PARAGRAPH,
                "mot  hai ba bon nam sau bay tam chin muoi",
            ),
        ],
    )
    normalized_doc, normalized_text, normalized_hash = (
        TextCleaner.clean_document(extracted)
    )
    chunker = StructureChunker(
        target_tokens=5,
        overlap_tokens=1,
        max_tokens=6,
        tokenizer_name="test:word-count",
        token_counter=lambda text: len(text.split()),
    )

    chunk_set, chunks = chunker.chunk(
        normalized_doc,
        uuid4(),
        "JOB_TEST",
        normalized_text=normalized_text,
    )

    assert chunk_set.total_chunks == len(chunks)
    assert normalized_hash == __import__("hashlib").sha256(
        normalized_text.encode("utf-8")
    ).hexdigest()
    for chunk in chunks:
        assert chunk.token_count <= 6
        assert (
            normalized_text[chunk.character_start : chunk.character_end]
            == chunk.text
        )


def _adaptive_chunk_fixture(*blocks: ContentBlock):
    document = ExtractedDocument(
        source_uri="memory://adaptive",
        title="Adaptive chunking",
        mime_type="text/plain",
        raw_sha256="0" * 64,
        blocks=list(blocks),
    )
    return TextCleaner.clean_document(document)[:2]


def test_adaptive_chunker_keeps_one_sentence_within_soft_ceiling() -> None:
    document, normalized_text = _adaptive_chunk_fixture(
        ContentBlock(
            BlockType.PARAGRAPH,
            "mot hai ba bon nam sau bay tam chin.",
        )
    )
    chunker = StructureChunker(
        target_tokens=8,
        soft_max_tokens=10,
        overlap_tokens=2,
        max_tokens=12,
        tokenizer_name="test:word-count",
        token_counter=lambda text: len(text.split()),
    )

    chunk_set, chunks = chunker.chunk(
        document,
        uuid4(),
        "JOB_ADAPTIVE_SOFT",
        normalized_text=normalized_text,
    )

    assert [chunk.token_count for chunk in chunks] == [9]
    assert chunk_set.soft_max_tokens == 10
    assert chunk_set.max_tokens == 12
    assert chunk_set.chunker_version == "3.0.0"


def test_adaptive_chunker_splits_oversized_block_at_sentence_boundaries() -> None:
    first_sentence = "mot hai ba bon nam sau."
    second_sentence = "bay tam chin muoi muoi-mot muoi-hai."
    document, normalized_text = _adaptive_chunk_fixture(
        ContentBlock(
            BlockType.PARAGRAPH,
            f"{first_sentence} {second_sentence}",
        )
    )
    chunker = StructureChunker(
        target_tokens=8,
        soft_max_tokens=10,
        overlap_tokens=2,
        max_tokens=12,
        tokenizer_name="test:word-count",
        token_counter=lambda text: len(text.split()),
    )

    _, chunks = chunker.chunk(
        document,
        uuid4(),
        "JOB_ADAPTIVE_SENTENCES",
        normalized_text=normalized_text,
    )

    assert [chunk.text for chunk in chunks] == [
        first_sentence,
        second_sentence,
    ]
    assert all(chunk.token_count <= 10 for chunk in chunks)


def test_adaptive_chunker_splits_one_oversized_sentence_near_target() -> None:
    sentence = " ".join(f"tu-{index}" for index in range(19))
    document, normalized_text = _adaptive_chunk_fixture(
        ContentBlock(BlockType.PARAGRAPH, sentence)
    )
    chunker = StructureChunker(
        target_tokens=8,
        soft_max_tokens=10,
        overlap_tokens=2,
        max_tokens=12,
        tokenizer_name="test:word-count",
        token_counter=lambda text: len(text.split()),
    )

    _, chunks = chunker.chunk(
        document,
        uuid4(),
        "JOB_ADAPTIVE_WORDS",
        normalized_text=normalized_text,
    )

    assert [chunk.token_count for chunk in chunks] == [8, 8, 3]
    assert all(chunk.token_count <= 8 for chunk in chunks)
    assert all(
        normalized_text[chunk.character_start : chunk.character_end]
        == chunk.text
        for chunk in chunks
    )


def test_adaptive_overlap_never_pushes_chunk_above_soft_ceiling() -> None:
    document, normalized_text = _adaptive_chunk_fixture(
        ContentBlock(BlockType.HEADING, "Tieu de", heading_level=1),
        ContentBlock(
            BlockType.PARAGRAPH,
            "mot hai ba bon nam sau bay tam chin.",
        ),
    )
    chunker = StructureChunker(
        target_tokens=8,
        soft_max_tokens=10,
        overlap_tokens=2,
        max_tokens=12,
        tokenizer_name="test:word-count",
        token_counter=lambda text: len(text.split()),
    )

    _, chunks = chunker.chunk(
        document,
        uuid4(),
        "JOB_ADAPTIVE_OVERLAP",
        normalized_text=normalized_text,
    )

    assert [chunk.token_count for chunk in chunks] == [2, 9]
    assert all(chunk.token_count <= 10 for chunk in chunks)


def test_preview_escapes_untrusted_staging_values(tmp_path: Path) -> None:
    generator = PreviewGenerator(tmp_path)
    html = generator._render_html(
        "JOB_SAFE",
        {},
        [
            {
                "title": "</script><script>alert(1)</script>",
                "canonical_document_key": "<img src=x onerror=alert(2)>",
                "source_namespace": "example",
                "document_identity_strategy": "content_only",
                "mime_type": "text/html",
                "extraction_quality": {"status": "clean"},
            }
        ],
        [],
        [],
        [{"error_type": "<svg/onload=alert(3)>", "message": "<b>bad</b>"}],
    )
    assert "</script><script>alert(1)</script>" not in html
    assert "<img src=x onerror=alert(2)>" not in html
    assert "<svg/onload=alert(3)>" not in html
    assert "\\u003c/script\\u003e\\u003cscript\\u003ealert(1)" in html

    match = re.search(
        r'<script id="preview-data" type="application/json">(.*?)</script>',
        html,
        re.DOTALL,
    )
    assert match is not None
    payload = json.loads(match.group(1))
    assert payload["documents"][0]["title"] == (
        "</script><script>alert(1)</script>"
    )


class _ConcurrentAdapter(BaseAdapter):
    def __init__(self) -> None:
        self.active = 0
        self.max_active = 0

    source_id = "test"
    source_namespace = "example.gov.vn"
    authority_namespace = "gov.vn"
    default_identity_strategy = "authoritative"

    async def fetch_and_parse(self, url: str) -> CrawlResult:
        self.active += 1
        self.max_active = max(self.max_active, self.active)
        await asyncio.sleep(0.02)
        self.active -= 1
        return CrawlResult(
            url=url,
            canonical_key=f"test:{url.rsplit('/', 1)[-1]}",
            title=url,
            html_or_bytes=f"<html><body><h1>{url}</h1><p>noi dung</p></body></html>".encode(),
            mime_type="text/html",
            document_identity_strategy=self.default_identity_strategy,
            source_namespace=self.source_namespace,
            authority_namespace=self.authority_namespace,
            metadata={},
            discovered_links=[],
        )


@pytest.mark.asyncio
async def test_engine_enforces_concurrency_limit_and_persists_state(
    tmp_path: Path,
) -> None:
    adapter = _ConcurrentAdapter()
    db_path = tmp_path / "state" / "crawler.db"
    engine = CrawlEngine(
        adapter,
        state_db_path=db_path,
        staging_dir=tmp_path / "staging",
        raw_dir=tmp_path / "raw",
        max_concurrent=2,
        max_total_resources=10,
    )
    output = await engine.run_job(
        "JOB_LIMIT",
        [f"https://example.gov.vn/{index}" for index in range(4)],
        max_depth=0,
        max_resources=2,
    )

    manifest = json.loads((output / "manifest.json").read_text("utf-8"))
    assert manifest["total_observations"] == 2
    assert adapter.max_active == 2
    with sqlite3.connect(db_path) as connection:
        row = connection.execute(
            "SELECT status, resources_crawled FROM CrawlJobs WHERE job_id = ?",
            ("JOB_LIMIT",),
        ).fetchone()
    assert row == ("completed", 2)


@pytest.mark.asyncio
async def test_engine_marks_bounded_ocr_output_as_truncated(
    tmp_path: Path,
) -> None:
    class PdfAdapter(BaseAdapter):
        source_id = "pdf-test"
        source_namespace = "example.gov.vn"
        authority_namespace = "gov.vn"
        default_identity_strategy = "authoritative"

        async def fetch_and_parse(self, url: str) -> CrawlResult:
            return CrawlResult(
                url=url,
                canonical_key="pdf:scan",
                title="Scanned PDF",
                html_or_bytes=b"%PDF fixture",
                mime_type="application/pdf",
                document_identity_strategy=self.default_identity_strategy,
                source_namespace=self.source_namespace,
                authority_namespace=self.authority_namespace,
                metadata={"kind": "scan"},
                discovered_links=[],
            )

    engine = CrawlEngine(
        PdfAdapter(),
        state_db_path=tmp_path / "state" / "crawler.db",
        staging_dir=tmp_path / "staging",
        raw_dir=tmp_path / "raw",
        max_concurrent=1,
        max_total_resources=1,
    )
    engine.pdf_extractor = SimpleNamespace(
        extract=lambda path: ExtractedDocument(
            source_uri=str(path),
            title="Scanned PDF",
            mime_type="application/pdf",
            raw_sha256="0" * 64,
            blocks=[
                ContentBlock(
                    block_type=BlockType.PARAGRAPH,
                    text="Nội dung OCR có giới hạn.",
                    page_number=1,
                )
            ],
            ocr_used=True,
            ocr_confidence=0.9,
            truncated=True,
            document_metadata={
                "pdf_page_count": 628,
                "ocr_pages_processed": 50,
                "ocr_pages_omitted": 578,
            },
        )
    )

    output = await engine.run_job(
        "JOB_OCR_TRUNCATED",
        ["https://example.gov.vn/book.pdf"],
        max_depth=0,
        max_resources=1,
    )
    observation = json.loads(
        (output / "document-observations.jsonl").read_text(encoding="utf-8")
    )

    assert observation["extraction_quality"]["status"] == "truncated"
    assert observation["extraction_quality"]["ocr_used"] is True
    assert observation["document_metadata"]["kind"] == "scan"
    assert observation["document_metadata"]["ocr_pages_omitted"] == 578


@pytest.mark.asyncio
async def test_engine_resumes_from_checkpoints_without_refetching(
    tmp_path: Path,
) -> None:
    first_adapter = _ConcurrentAdapter()
    common = {
        "state_db_path": tmp_path / "state" / "crawler.db",
        "staging_dir": tmp_path / "staging",
        "raw_dir": tmp_path / "raw",
        "max_concurrent": 1,
        "max_total_resources": 2,
    }
    first_output = await CrawlEngine(first_adapter, **common).run_job(
        "JOB_RESUME",
        ["https://example.gov.vn/a", "https://example.gov.vn/b"],
        max_depth=0,
        max_resources=1,
    )
    first_manifest = json.loads(
        (first_output / "manifest.json").read_text(encoding="utf-8")
    )
    assert first_manifest["total_observations"] == 1

    second_adapter = _ConcurrentAdapter()
    resumed_output = await CrawlEngine(second_adapter, **common).run_job(
        "JOB_RESUME",
        ["https://example.gov.vn/a", "https://example.gov.vn/b"],
        max_depth=0,
        max_resources=2,
    )
    resumed_manifest = json.loads(
        (resumed_output / "manifest.json").read_text(encoding="utf-8")
    )

    assert resumed_manifest["total_observations"] == 2
    assert second_adapter.max_active == 1
    with sqlite3.connect(common["state_db_path"]) as connection:
        statuses = connection.execute(
            "SELECT status, COUNT(*) FROM CrawlFrontier "
            "WHERE job_id = ? GROUP BY status",
            ("JOB_RESUME",),
        ).fetchall()
    assert statuses == [("done", 2)]


def test_state_resume_only_requeues_interrupted_frontier_items(
    tmp_path: Path,
) -> None:
    store = CrawlerStateStore(tmp_path / "state" / "crawler.db")
    job_id = "JOB_TERMINAL_FAILURE"
    failed_url = "https://example.gov.vn/files/legacy.doc"
    interrupted_url = "https://example.gov.vn/interrupted"
    store.start_job(
        job_id=job_id,
        source_id="test",
        source_namespace="example.gov.vn",
        authority_namespace="gov.vn",
        identity_strategy="authoritative",
        base_url="https://example.gov.vn/",
    )
    store.prepare_frontier(job_id, [(failed_url, 0), (interrupted_url, 0)])
    claimed = store.claim_frontier(job_id, 2)
    assert {item.url for item in claimed} == {failed_url, interrupted_url}
    store.mark_frontier(job_id, failed_url, "failed", "terminal failure")

    store.prepare_frontier(job_id, [(failed_url, 0), (interrupted_url, 0)])

    assert store.frontier_counts(job_id) == {"failed": 1, "pending": 1}
    assert store.pending_frontier_urls(job_id) == [interrupted_url]


@pytest.mark.asyncio
async def test_engine_skips_stale_pending_url_under_current_policy(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "state" / "crawler.db"
    store = CrawlerStateStore(db_path)
    job_id = "JOB_POLICY_RESUME"
    legacy_url = "https://example.gov.vn/files/legacy.doc"
    valid_url = "https://example.gov.vn/article"
    store.start_job(
        job_id=job_id,
        source_id="test",
        source_namespace="example.gov.vn",
        authority_namespace="gov.vn",
        identity_strategy="authoritative",
        base_url=valid_url,
    )
    store.prepare_frontier(job_id, [(legacy_url, 100)])

    adapter = _ConcurrentAdapter()
    output = await CrawlEngine(
        adapter,
        state_db_path=db_path,
        staging_dir=tmp_path / "staging",
        raw_dir=tmp_path / "raw",
        max_concurrent=1,
        max_total_resources=2,
    ).run_job(job_id, [valid_url], max_depth=0, max_resources=2)

    manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["total_observations"] == 1
    assert adapter.max_active == 1
    with sqlite3.connect(db_path) as connection:
        statuses = dict(
            connection.execute(
                "SELECT url, status FROM CrawlFrontier WHERE job_id = ?",
                (job_id,),
            ).fetchall()
        )
    assert statuses == {legacy_url: "skipped", valid_url: "done"}


def test_delete_job_removes_scoped_state_but_keeps_shared_cache(
    tmp_path: Path,
) -> None:
    db_path = tmp_path / "state" / "crawler.db"
    store = CrawlerStateStore(db_path)
    job_id = "JOB_DELETE_STATE"
    url = "https://example.gov.vn/document"
    raw_root = tmp_path / "raw"
    raw_job_dir = raw_root / job_id
    raw_job_dir.mkdir(parents=True)
    raw_path = raw_job_dir / "document.html"
    raw_bytes = b"cached document"
    raw_path.write_bytes(raw_bytes)
    store.start_job(
        job_id=job_id,
        source_id="test",
        source_namespace="example.gov.vn",
        authority_namespace="gov.vn",
        identity_strategy="authoritative",
        base_url=url,
    )
    store.prepare_frontier(job_id, [(url, 0)])
    store.record_fetch(
        job_id=job_id,
        source_id="test",
        url=url,
        fetch_status="fetched",
        http_status=200,
        content_hash=hashlib.sha256(raw_bytes).hexdigest(),
        etag='"version-1"',
        raw_artifact_uri=str(raw_path.resolve()),
    )
    assert store.conditional_headers(
        url,
        "test",
        raw_base_dir=raw_root,
    ) == {"If-None-Match": '"version-1"'}

    assert store.delete_job(job_id, raw_job_dir=raw_job_dir) is True
    assert store.delete_job(job_id, raw_job_dir=raw_job_dir) is False

    with sqlite3.connect(db_path) as connection:
        job_count = connection.execute(
            "SELECT COUNT(*) FROM CrawlJobs WHERE job_id = ?", (job_id,)
        ).fetchone()[0]
        frontier_count = connection.execute(
            "SELECT COUNT(*) FROM CrawlFrontier WHERE job_id = ?", (job_id,)
        ).fetchone()[0]
        history_count = connection.execute(
            "SELECT COUNT(*) FROM ResourceFetchHistory WHERE job_id = ?", (job_id,)
        ).fetchone()[0]
        resource_count = connection.execute(
            "SELECT COUNT(*) FROM CrawledResources WHERE url = ?", (url,)
        ).fetchone()[0]
        cache_state = connection.execute(
            """
            SELECT etag, last_modified, content_hash, raw_artifact_uri,
                   fetch_status
            FROM CrawledResources WHERE url = ?
            """,
            (url,),
        ).fetchone()
    assert (job_count, frontier_count, history_count, resource_count) == (0, 0, 0, 1)
    assert cache_state == (None, None, None, None, "pending")

    # Reusing the same job ID starts clean and does not emit stale validators.
    store.start_job(
        job_id=job_id,
        source_id="test",
        source_namespace="example.gov.vn",
        authority_namespace="gov.vn",
        identity_strategy="authoritative",
        base_url=url,
    )
    store.prepare_frontier(job_id, [(url, 0)])
    assert store.conditional_headers(
        url,
        "test",
        raw_base_dir=raw_root,
    ) == {}


def test_conditional_headers_invalidate_tampered_or_out_of_root_artifact(
    tmp_path: Path,
) -> None:
    store = CrawlerStateStore(tmp_path / "state" / "crawler.db")
    job_id = "JOB_CACHE_INTEGRITY"
    url = "https://example.gov.vn/document"
    raw_root = tmp_path / "raw"
    raw_job_dir = raw_root / job_id
    raw_job_dir.mkdir(parents=True)
    raw_path = raw_job_dir / "document.html"
    raw_path.write_bytes(b"original")
    store.start_job(
        job_id=job_id,
        source_id="test",
        source_namespace="example.gov.vn",
        authority_namespace="gov.vn",
        identity_strategy="authoritative",
        base_url=url,
    )
    store.record_fetch(
        job_id=job_id,
        source_id="test",
        url=url,
        fetch_status="fetched",
        http_status=200,
        content_hash=hashlib.sha256(b"original").hexdigest(),
        etag='"version-1"',
        raw_artifact_uri=str(raw_path.resolve()),
    )

    raw_path.write_bytes(b"tampered")
    assert store.conditional_headers(
        url,
        "test",
        raw_base_dir=raw_root,
    ) == {}
    assert store.cached_resource(url, "test") is None

    outside_path = tmp_path / "outside.html"
    outside_path.write_bytes(b"outside")
    store.record_fetch(
        job_id=job_id,
        source_id="test",
        url=url,
        fetch_status="fetched",
        http_status=200,
        content_hash=hashlib.sha256(b"outside").hexdigest(),
        etag='"version-2"',
        raw_artifact_uri=str(outside_path.resolve()),
    )
    assert store.conditional_headers(
        url,
        "test",
        raw_base_dir=raw_root,
    ) == {}
    assert store.cached_resource(url, "test") is None


class _ConditionalCacheAdapter(BaseAdapter):
    source_id = "cache-test"
    source_namespace = "example.gov.vn"
    authority_namespace = "gov.vn"
    default_identity_strategy = "authoritative"

    def __init__(self) -> None:
        self.conditional_headers: dict[str, str] = {}

    async def fetch_and_parse(self, url: str) -> CrawlResult:
        return CrawlResult(
            url=url,
            canonical_key="cache:document",
            title="Cached document",
            html_or_bytes=b"<html><body><p>cached content</p></body></html>",
            mime_type="text/html",
            document_identity_strategy=self.default_identity_strategy,
            source_namespace=self.source_namespace,
            authority_namespace=self.authority_namespace,
            metadata={"kind": "fixture"},
            discovered_links=[],
            response_headers={"ETag": '"version-1"'},
        )

    async def fetch_and_parse_conditional(
        self,
        url: str,
        request_headers,
    ) -> CrawlResult | NotModifiedResult:
        self.conditional_headers = dict(request_headers)
        if request_headers:
            return NotModifiedResult(
                requested_url=url,
                final_url=url,
                response_headers={},
            )
        return await self.fetch_and_parse(url)


@pytest.mark.asyncio
async def test_engine_reuses_verified_raw_cache_after_not_modified(
    tmp_path: Path,
) -> None:
    common = {
        "state_db_path": tmp_path / "state" / "crawler.db",
        "staging_dir": tmp_path / "staging",
        "raw_dir": tmp_path / "raw",
        "max_concurrent": 1,
        "max_total_resources": 1,
    }
    url = "https://example.gov.vn/cache"
    first = _ConditionalCacheAdapter()
    await CrawlEngine(first, **common).run_job(
        "JOB_CACHE_1", [url], max_depth=0, max_resources=1
    )

    second = _ConditionalCacheAdapter()
    output = await CrawlEngine(second, **common).run_job(
        "JOB_CACHE_2", [url], max_depth=0, max_resources=1
    )
    manifest = json.loads(
        (output / "manifest.json").read_text(encoding="utf-8")
    )

    assert second.conditional_headers == {"If-None-Match": '"version-1"'}
    assert manifest["total_observations"] == 1
    observation = json.loads(
        (output / "document-observations.jsonl").read_text(encoding="utf-8")
    )
    assert observation["document_metadata"]["cache_reused"] is True


class _PaginationAdapter(BaseAdapter):
    source_id = "pagination-test"
    source_namespace = "example.gov.vn"
    authority_namespace = "gov.vn"
    default_identity_strategy = "authoritative"

    def __init__(self) -> None:
        self.fetched: list[str] = []

    async def fetch_and_parse(self, url: str) -> CrawlResult:
        self.fetched.append(url)
        links = {
            "https://example.gov.vn/list": [
                "https://example.gov.vn/list?page=2",
                "https://example.gov.vn/article-1.html",
            ],
            "https://example.gov.vn/list?page=2": [
                "https://example.gov.vn/list?page=3",
                "https://example.gov.vn/article-2.html",
            ],
            "https://example.gov.vn/list?page=3": [
                "https://example.gov.vn/article-3.html",
            ],
        }.get(url, [])
        return CrawlResult(
            url=url,
            canonical_key=f"pagination:{url}",
            title=url,
            html_or_bytes=(
                f"<html><body><p>Unique content for {url}</p></body></html>"
            ).encode(),
            mime_type="text/html",
            document_identity_strategy=self.default_identity_strategy,
            source_namespace=self.source_namespace,
            authority_namespace=self.authority_namespace,
            metadata={},
            discovered_links=links,
        )


@pytest.mark.asyncio
async def test_engine_follows_pagination_chain_without_consuming_content_depth(
    tmp_path: Path,
) -> None:
    adapter = _PaginationAdapter()
    db_path = tmp_path / "state" / "crawler.db"
    output = await CrawlEngine(
        adapter,
        state_db_path=db_path,
        staging_dir=tmp_path / "staging",
        raw_dir=tmp_path / "raw",
        max_concurrent=1,
        max_total_resources=10,
        max_pagination_pages=5,
    ).run_job(
        "JOB_PAGINATION",
        ["https://example.gov.vn/list"],
        max_depth=1,
        max_resources=10,
    )

    assert set(adapter.fetched) == {
        "https://example.gov.vn/list",
        "https://example.gov.vn/list?page=2",
        "https://example.gov.vn/list?page=3",
        "https://example.gov.vn/article-1.html",
        "https://example.gov.vn/article-2.html",
        "https://example.gov.vn/article-3.html",
    }
    manifest = json.loads((output / "manifest.json").read_text("utf-8"))
    assert manifest["total_observations"] == 6
    with sqlite3.connect(db_path) as connection:
        depths = dict(
            connection.execute(
                "SELECT url, depth FROM CrawlFrontier "
                "WHERE job_id = ?",
                ("JOB_PAGINATION",),
            ).fetchall()
        )
    assert depths["https://example.gov.vn/list?page=3"] == 0
    assert depths["https://example.gov.vn/article-3.html"] == 1
