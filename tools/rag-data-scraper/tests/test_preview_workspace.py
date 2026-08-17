from pathlib import Path

from rag_data_scraper.exporters.preview_generator import PreviewGenerator


def _preview_fixture():
    documents = [
        {
            "observation_id": "doc-1",
            "title": "Document one",
            "canonical_document_key": "duplicate-key",
            "source_document_url": "",
            "source_namespace": "example.gov.vn",
            "mime_type": "text/html",
            "extraction_quality": {
                "status": "clean",
                "ocr_used": False,
                "confidence_score": 1.0,
            },
        },
        {
            "observation_id": "doc-2",
            "title": "",
            "canonical_document_key": "duplicate-key",
            "source_document_url": "https://example.gov.vn/two",
            "source_namespace": "example.gov.vn",
            "mime_type": "application/pdf",
            "extraction_quality": {
                "status": "ocr_fallback",
                "ocr_used": True,
                "confidence_score": 0.8,
            },
        },
    ]
    chunk_sets = [
        {
            "chunk_set_id": "set-1",
            "observation_id": "doc-1",
            "target_tokens": 4,
            "overlap_tokens": 1,
            "total_chunks": 2,
            "chunking_strategy": "structure_aware",
            "tokenizer_name": "test-tokenizer",
        }
    ]
    chunks = [
        {
            "chunk_id": "chunk-1",
            "chunk_set_id": "set-1",
            "chunk_index": 0,
            "text": "hello",
            "token_count": 6,
            "character_start": 0,
            "character_end": 5,
            "content_sha256": "a" * 64,
            "chunk_acl": {
                "allowed_roles": ["Staff"],
                "denied_roles": ["staff"],
                "security_classification": "",
            },
        }
    ]
    manifest = {
        "total_observations": 2,
        "total_chunk_sets": 1,
        "total_chunks": 9,
        "total_errors": 0,
    }
    errors = [
        {
            "error_id": "error-1",
            "stage": "fetch",
            "url": "https://example.gov.vn/fail",
            "message": "HTTP 500",
        }
    ]
    return manifest, documents, chunk_sets, chunks, errors


def test_preview_payload_surfaces_rag_health_and_token_metrics(
    tmp_path: Path,
) -> None:
    generator = PreviewGenerator(tmp_path)
    payload = generator._build_preview_payload(
        "JOB_HEALTH",
        *_preview_fixture(),
    )

    codes = {issue["code"] for issue in payload["issues"]}
    assert {
        "TOKEN_BUDGET_EXCEEDED",
        "ACL_ROLE_CONFLICT",
        "MISSING_CLASSIFICATION",
        "MISSING_SOURCE_URL",
        "DUPLICATE_DOCUMENT_KEY",
        "MISSING_CHUNK_SET",
        "CHUNK_SET_COUNT_MISMATCH",
        "MANIFEST_COUNT_MISMATCH",
        "CRAWLER_ERROR",
        "OCR_USED",
    }.issubset(codes)

    summary = payload["summary"]
    assert summary["documents"] == 2
    assert summary["chunks"] == 1
    assert summary["token_stats"] == {
        "min": 6,
        "median": 6,
        "p95": 6,
        "max": 6,
        "average": 6.0,
    }
    assert summary["actionable_issues"] == (
        summary["severity_counts"]["error"]
        + summary["severity_counts"]["warning"]
    )
    assert payload["documents"][0]["_preview"]["chunk_count"] == 1
    assert payload["documents"][1]["_preview"]["chunk_count"] == 0
    assert payload["chunks"][0]["_preview"]["document_title"] == "Document one"


def test_preview_template_is_paginated_accessible_and_self_contained(
    tmp_path: Path,
) -> None:
    generator = PreviewGenerator(tmp_path)
    html = generator._render_html(
        "JOB_UI",
        *_preview_fixture(),
    )

    assert 'role="tablist"' in html
    assert 'aria-selected="true"' in html
    assert 'role="dialog"' in html
    assert "const PAGE_SIZE = 50;" in html
    assert "document.createElement" in html
    assert "event.target.classList" not in html
    assert "https://cdn." not in html
    assert "__RAG_PREVIEW_DATA__" not in html


def test_preview_groups_repeated_duplicate_and_crawler_issues(
    tmp_path: Path,
) -> None:
    manifest, documents, chunk_sets, chunks, errors = _preview_fixture()
    duplicate = dict(chunks[0])
    duplicate["chunk_id"] = "chunk-2"
    duplicate["chunk_index"] = 1
    duplicate["character_start"] = 6
    duplicate["character_end"] = 11
    chunks.append(duplicate)
    chunk_sets[0]["total_chunks"] = 2
    errors.append(
        {
            "error_id": "error-2",
            "stage": "fetch",
            "url": "https://example.gov.vn/fail-2",
            "message": "HTTP 500",
        }
    )
    manifest["total_chunks"] = 2
    manifest["total_errors"] = 2

    payload = PreviewGenerator(tmp_path)._build_preview_payload(
        "JOB_GROUPED",
        manifest,
        documents,
        chunk_sets,
        chunks,
        errors,
    )
    duplicate_issues = [
        item
        for item in payload["issues"]
        if item["code"] == "DUPLICATE_CHUNK_CONTENT"
    ]
    crawler_issues = [
        item
        for item in payload["issues"]
        if item["code"] == "CRAWLER_ERROR"
    ]

    assert len(duplicate_issues) == 1
    assert duplicate_issues[0]["occurrence_count"] == 2
    assert duplicate_issues[0]["target_type"] == "chunk_group"
    assert len(crawler_issues) == 1
    assert crawler_issues[0]["occurrence_count"] == 2
    assert crawler_issues[0]["sample_urls"] == [
        "https://example.gov.vn/fail",
        "https://example.gov.vn/fail-2",
    ]


def test_preview_does_not_label_missing_redirect_location_as_url_policy(
    tmp_path: Path,
) -> None:
    manifest, documents, chunk_sets, chunks, errors = _preview_fixture()
    errors[:] = [
        {
            "error_id": "redirect-error",
            "stage": "fetch",
            "error_type": "UnsafeUrlError",
            "url": "https://example.gov.vn/document.pdf",
            "message": "Redirect response did not include Location",
        }
    ]
    manifest["total_errors"] = 1

    payload = PreviewGenerator(tmp_path)._build_preview_payload(
        "JOB_REDIRECT_CATEGORY",
        manifest,
        documents,
        chunk_sets,
        chunks,
        errors,
    )
    crawler_issue = next(
        item for item in payload["issues"] if item["code"] == "CRAWLER_ERROR"
    )

    assert crawler_issue["target_id"].endswith("redirect_missing_location")
    assert "conditional-cache behavior" in crawler_issue["recommendation"]


def test_preview_gives_bounded_response_size_recommendation(
    tmp_path: Path,
) -> None:
    manifest, documents, chunk_sets, chunks, errors = _preview_fixture()
    errors[:] = [
        {
            "error_id": "large-response",
            "stage": "fetch",
            "error_type": "ResponseTooLargeError",
            "url": "https://example.gov.vn/book.pdf",
            "message": "Response exceeds 33554432 bytes",
        }
    ]
    manifest["total_errors"] = 1

    payload = PreviewGenerator(tmp_path)._build_preview_payload(
        "JOB_RESPONSE_LIMIT",
        manifest,
        documents,
        chunk_sets,
        chunks,
        errors,
    )
    crawler_issue = next(
        item for item in payload["issues"] if item["code"] == "CRAWLER_ERROR"
    )

    assert crawler_issue["target_id"].endswith("response_too_large")
    assert "trusted sources" in crawler_issue["recommendation"]


def test_preview_uses_explicit_soft_and_hard_token_limits(tmp_path: Path) -> None:
    manifest, documents, chunk_sets, chunks, errors = _preview_fixture()
    chunk_sets[0]["soft_max_tokens"] = 8
    chunk_sets[0]["max_tokens"] = 10

    payload = PreviewGenerator(tmp_path)._build_preview_payload(
        "JOB_ADAPTIVE_LIMITS",
        manifest,
        documents,
        chunk_sets,
        chunks,
        errors,
    )
    token_codes = {
        item["code"] for item in payload["issues"] if item["target_type"] == "chunk"
    }
    assert "TOKEN_BUDGET_EXCEEDED" not in token_codes
    assert "TOKEN_HARD_LIMIT_EXCEEDED" not in token_codes
    assert payload["chunks"][0]["_preview"]["soft_max_tokens"] == 8
    assert payload["chunks"][0]["_preview"]["max_tokens"] == 10

    chunks[0]["token_count"] = 11
    payload = PreviewGenerator(tmp_path)._build_preview_payload(
        "JOB_ADAPTIVE_HARD_LIMIT",
        manifest,
        documents,
        chunk_sets,
        chunks,
        errors,
    )
    token_codes = {
        item["code"] for item in payload["issues"] if item["target_type"] == "chunk"
    }
    assert "TOKEN_HARD_LIMIT_EXCEEDED" in token_codes

