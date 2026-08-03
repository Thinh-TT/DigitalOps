"""Build the data model and standalone HTML used by the RAG preview workspace."""

from __future__ import annotations

import json
import math
import re
from collections import Counter
from pathlib import Path
from typing import Any


def build_preview_html(
    job_id: str,
    manifest: dict[str, Any],
    observations: list[dict[str, Any]],
    chunk_sets: list[dict[str, Any]],
    chunks: list[dict[str, Any]],
    errors: list[dict[str, Any]],
) -> str:
    payload = build_preview_payload(
        job_id, manifest, observations, chunk_sets, chunks, errors
    )
    serialized = json.dumps(
        payload, ensure_ascii=False, separators=(",", ":")
    )
    # This JSON lives in a script text node. Neutralize HTML delimiters so a
    # staging value cannot close the node and become executable markup.
    serialized = (
        serialized.replace("&", "\\u0026")
        .replace("<", "\\u003c")
        .replace(">", "\\u003e")
        .replace("\u2028", "\\u2028")
        .replace("\u2029", "\\u2029")
    )
    template_path = (
        Path(__file__).resolve().parents[1]
        / "web"
        / "static"
        / "rag_preview.html"
    )
    template = template_path.read_text(encoding="utf-8")
    placeholder = "__RAG_PREVIEW_DATA__"
    if template.count(placeholder) != 1:
        raise RuntimeError("RAG preview template has an invalid data placeholder.")
    return template.replace(placeholder, serialized)


def build_preview_payload(
    job_id: str,
    manifest: dict[str, Any],
    observations: list[dict[str, Any]],
    chunk_sets: list[dict[str, Any]],
    chunks: list[dict[str, Any]],
    errors: list[dict[str, Any]],
) -> dict[str, Any]:
    documents = [dict(item) for item in observations]
    preview_sets = [dict(item) for item in chunk_sets]
    preview_chunks = [dict(item) for item in chunks]
    crawler_errors = [dict(item) for item in errors]
    issues: list[dict[str, Any]] = []

    document_by_id = {
        str(item.get("observation_id")): item
        for item in documents
        if item.get("observation_id") is not None
    }
    set_by_id = {
        str(item.get("chunk_set_id")): item
        for item in preview_sets
        if item.get("chunk_set_id") is not None
    }
    sets_by_document: dict[str, list[dict[str, Any]]] = {}
    chunks_by_set: dict[str, list[dict[str, Any]]] = {}

    def issue(
        severity: str,
        code: str,
        target_type: str,
        target_id: Any,
        message: str,
        recommendation: str,
        *,
        occurrence_count: int = 1,
        sample_urls: list[str] | None = None,
        member_ids: list[str] | None = None,
    ) -> None:
        issues.append(
            {
                "issue_id": f"ISSUE-{len(issues) + 1:04d}",
                "severity": severity,
                "code": code,
                "target_type": target_type,
                "target_id": str(target_id or "unknown"),
                "message": message,
                "recommendation": recommendation,
                "occurrence_count": occurrence_count,
                "sample_urls": sample_urls or [],
                "member_ids": member_ids or [],
            }
        )

    for chunk_set in preview_sets:
        set_id = str(chunk_set.get("chunk_set_id") or "")
        document_id = str(chunk_set.get("observation_id") or "")
        document = document_by_id.get(document_id)
        chunk_set["_preview"] = {
            "document_id": document_id,
            "document_title": (
                document.get("title") if document else "Document not found"
            ),
        }
        sets_by_document.setdefault(document_id, []).append(chunk_set)
        chunks_by_set.setdefault(set_id, [])
        if not document:
            issue(
                "error",
                "UNKNOWN_DOCUMENT",
                "chunk_set",
                set_id,
                "Chunk set references an observation that does not exist.",
                "Restore the observation or remove the orphan chunk set.",
            )

    for chunk in preview_chunks:
        set_id = str(chunk.get("chunk_set_id") or "")
        chunk_set = set_by_id.get(set_id)
        document_id = (
            str(chunk_set.get("observation_id") or "") if chunk_set else ""
        )
        document = document_by_id.get(document_id)
        chunks_by_set.setdefault(set_id, []).append(chunk)
        chunk["_preview"] = {
            "document_id": document_id,
            "document_title": (
                document.get("title") if document else "Document not found"
            ),
            "target_tokens": (
                chunk_set.get("target_tokens") if chunk_set else None
            ),
            "soft_max_tokens": (
                chunk_set.get("soft_max_tokens") if chunk_set else None
            ),
            "max_tokens": (
                chunk_set.get("max_tokens") if chunk_set else None
            ),
        }
        chunk_id = chunk.get("chunk_id") or (
            f"{set_id}#{chunk.get('chunk_index', '?')}"
        )
        if not chunk_set:
            issue(
                "error",
                "UNKNOWN_CHUNK_SET",
                "chunk",
                chunk_id,
                "Chunk references a chunk set that does not exist.",
                "Restore the chunk set or remove the orphan chunk.",
            )
        text = chunk.get("text")
        if not isinstance(text, str) or not text.strip():
            issue(
                "error",
                "EMPTY_CHUNK",
                "chunk",
                chunk_id,
                "Chunk has no text content.",
                "Remove the empty chunk or rerun extraction and chunking.",
            )
        token_count = _as_int(chunk.get("token_count"))
        target_count = _as_int(
            chunk_set.get("target_tokens") if chunk_set else None
        )
        soft_max_count = _as_int(
            chunk_set.get("soft_max_tokens") if chunk_set else None
        )
        hard_max_count = _as_int(
            chunk_set.get("max_tokens") if chunk_set else None
        )
        # Old staging packages do not declare soft/hard ceilings. Preserve
        # their original target-based warning behavior while new packages use
        # the explicit adaptive limits.
        warning_count = soft_max_count or target_count
        hard_count = hard_max_count or 512
        if (
            token_count is not None
            and token_count > hard_count
        ):
            issue(
                "error",
                "TOKEN_HARD_LIMIT_EXCEEDED",
                "chunk",
                chunk_id,
                f"Chunk has {token_count} tokens and exceeds hard limit {hard_count}.",
                "Rerun chunking before embedding this package.",
            )
        elif (
            token_count is not None
            and warning_count is not None
            and token_count > warning_count
        ):
            issue(
                "warning",
                "TOKEN_BUDGET_EXCEEDED",
                "chunk",
                chunk_id,
                f"Chunk has {token_count} tokens and exceeds soft ceiling {warning_count}.",
                "Review semantic boundaries or reduce the soft chunk limit.",
            )
        start = _as_int(chunk.get("character_start"))
        end = _as_int(chunk.get("character_end"))
        if start is None or end is None or start < 0 or end <= start:
            issue(
                "error",
                "INVALID_OFFSETS",
                "chunk",
                chunk_id,
                "Character offsets are invalid.",
                "Rerun chunking and verify start is less than end.",
            )
        elif isinstance(text, str) and len(text) != end - start:
            issue(
                "warning",
                "OFFSET_LENGTH_MISMATCH",
                "chunk",
                chunk_id,
                "Text length does not match the character offsets.",
                "Compare normalized text and rerun chunking.",
            )
        acl = chunk.get("chunk_acl")
        acl = acl if isinstance(acl, dict) else {}
        allowed = _string_list(acl.get("allowed_roles"))
        denied = _string_list(acl.get("denied_roles"))
        if not allowed:
            issue(
                "warning",
                "MISSING_ALLOWED_ROLES",
                "chunk",
                chunk_id,
                "Chunk does not declare allowed_roles.",
                "Assign at least one allowed role before indexing.",
            )
        overlap = sorted(
            {value.lower() for value in allowed}
            & {value.lower() for value in denied}
        )
        if overlap:
            issue(
                "error",
                "ACL_ROLE_CONFLICT",
                "chunk",
                chunk_id,
                f"Roles appear in both allow and deny: {', '.join(overlap)}.",
                "Remove the ACL conflict before RAG ingestion.",
            )
        if not str(acl.get("security_classification") or "").strip():
            issue(
                "warning",
                "MISSING_CLASSIFICATION",
                "chunk",
                chunk_id,
                "Chunk has no security_classification.",
                "Assign a security classification before indexing.",
            )

    canonical_counts = Counter(
        str(item.get("canonical_document_key"))
        for item in documents
        if item.get("canonical_document_key")
    )
    for document in documents:
        document_id = str(document.get("observation_id") or "unknown")
        if not str(document.get("title") or "").strip():
            issue(
                "warning",
                "MISSING_TITLE",
                "document",
                document_id,
                "Document has no title.",
                "Add a title from source metadata or a controlled fallback.",
            )
        if not str(document.get("source_document_url") or "").strip():
            issue(
                "warning",
                "MISSING_SOURCE_URL",
                "document",
                document_id,
                "Document has no source URL.",
                "Add source_document_url to preserve source traceability.",
            )
        quality = document.get("extraction_quality")
        quality = quality if isinstance(quality, dict) else {}
        quality_status = str(quality.get("status") or "unknown")
        if quality_status in {"failed", "truncated"}:
            issue(
                "error" if quality_status == "failed" else "warning",
                "EXTRACTION_QUALITY",
                "document",
                document_id,
                f"Extraction quality is {quality_status}.",
                "Inspect the artifact and rerun extraction.",
            )
        elif quality_status not in {"clean", "ocr_fallback"}:
            issue(
                "warning",
                "UNKNOWN_EXTRACTION_QUALITY",
                "document",
                document_id,
                f"Extraction quality is unknown: {quality_status}.",
                "Normalize extraction quality to the supported schema.",
            )
        if quality_status == "ocr_fallback" or quality.get("ocr_used") is True:
            issue(
                "info",
                "OCR_USED",
                "document",
                document_id,
                "OCR was used; review the text before ingestion.",
                "Compare identifiers, dates, and sample text with the source.",
            )
        canonical_key = str(document.get("canonical_document_key") or "")
        if canonical_key and canonical_counts[canonical_key] > 1:
            issue(
                "warning",
                "DUPLICATE_DOCUMENT_KEY",
                "document",
                document_id,
                "Canonical document key is duplicated in this job.",
                "Review identity strategy and merge duplicate observations.",
            )
        owned_sets = sets_by_document.get(document_id, [])
        if not owned_sets:
            issue(
                "warning",
                "MISSING_CHUNK_SET",
                "document",
                document_id,
                "Document has no chunk set.",
                "Run chunking before exporting to a vector pipeline.",
            )
        elif len(owned_sets) > 1:
            issue(
                "warning",
                "MULTIPLE_CHUNK_SETS",
                "document",
                document_id,
                f"Document has {len(owned_sets)} chunk sets.",
                "Choose one official chunking version before ingestion.",
            )

    for chunk_set in preview_sets:
        set_id = str(chunk_set.get("chunk_set_id") or "unknown")
        actual_count = len(chunks_by_set.get(set_id, []))
        declared_count = _as_int(chunk_set.get("total_chunks"))
        if declared_count is not None and declared_count != actual_count:
            issue(
                "warning",
                "CHUNK_SET_COUNT_MISMATCH",
                "chunk_set",
                set_id,
                (
                    f"Chunk set declares {declared_count} chunks "
                    f"but {actual_count} were found."
                ),
                "Regenerate the chunk set metadata after chunking.",
            )
        chunk_set["_preview"]["actual_chunks"] = actual_count

    hash_groups: dict[str, list[dict[str, Any]]] = {}
    for chunk in preview_chunks:
        content_hash = str(chunk.get("content_sha256") or "")
        if content_hash:
            hash_groups.setdefault(content_hash, []).append(chunk)
    for same_hash in hash_groups.values():
        if len(same_hash) < 2:
            continue
        member_ids = [
            str(chunk.get("chunk_id") or "unknown") for chunk in same_hash
        ]
        document_ids = {
            str(chunk.get("_preview", {}).get("document_id") or "unknown")
            for chunk in same_hash
        }
        for chunk in same_hash:
            chunk["_preview"]["duplicate_count"] = len(same_hash)
        content_hash = str(same_hash[0].get("content_sha256") or "unknown")
        issue(
            "info",
            "DUPLICATE_CHUNK_CONTENT",
            "chunk_group",
            content_hash,
            (
                f"The same chunk content appears {len(same_hash)} times "
                f"across {len(document_ids)} document(s)."
            ),
            (
                "If this is shared navigation or footer text, remove it during "
                "HTML extraction; otherwise keep the valid repetition."
            ),
            occurrence_count=len(same_hash),
            member_ids=member_ids,
        )

    actual_counts = {
        "total_observations": len(documents),
        "total_chunk_sets": len(preview_sets),
        "total_chunks": len(preview_chunks),
        "total_errors": len(crawler_errors),
    }
    for field, actual_count in actual_counts.items():
        declared_count = _as_int(manifest.get(field))
        if declared_count is not None and declared_count != actual_count:
            issue(
                "warning",
                "MANIFEST_COUNT_MISMATCH",
                "manifest",
                field,
                (
                    f"Manifest declares {field}={declared_count}, "
                    f"but the actual count is {actual_count}."
                ),
                "Regenerate the manifest from the current staging package.",
            )

    crawler_error_groups: dict[
        tuple[str, str, str], list[dict[str, Any]]
    ] = {}
    for crawler_error in crawler_errors:
        stage = str(crawler_error.get("stage") or "unknown")
        error_type = str(crawler_error.get("error_type") or "CrawlerError")
        message = str(crawler_error.get("message") or "Crawler error has no message.")
        if error_type in {"RedirectPolicyError", "UnsafeUrlError"}:
            if "outside the crawl scope" in message:
                category = "redirect_out_of_scope"
            elif "Only approved HTTP(S)" in message:
                category = "insecure_scheme"
            elif "Redirect response did not include Location" in message:
                category = "redirect_missing_location"
            elif "Maximum redirect count exceeded" in message:
                category = "redirect_limit"
            else:
                category = "url_policy"
        elif error_type == "HttpStatusError":
            match = re.search(r"HTTP\s+(\d{3})", message)
            category = f"http_{match.group(1)}" if match else "http_status"
        elif error_type == "ResponseTooLargeError":
            category = "response_too_large"
        elif error_type == "OCRUnavailableError":
            category = "ocr_unavailable"
        elif error_type == "PDFTextExtractionError":
            category = "pdf_no_text"
        else:
            category = message
        crawler_error_groups.setdefault(
            (stage, error_type, category), []
        ).append(crawler_error)

    for (stage, error_type, category), grouped_errors in crawler_error_groups.items():
        sample_urls = [
            str(value.get("url"))
            for value in grouped_errors[:20]
            if value.get("url")
        ]
        if error_type in {"RedirectPolicyError", "UnsafeUrlError"}:
            if category == "redirect_missing_location":
                recommendation = (
                    "The server returned a redirect status without Location; "
                    "verify the response and conditional-cache behavior."
                )
            elif category == "redirect_limit":
                recommendation = (
                    "Inspect the redirect loop before changing the bounded "
                    "redirect limit."
                )
            else:
                recommendation = (
                    "Inspect the redirect target and explicitly allow only the "
                    "required public HTTPS host alias."
                )
        elif error_type == "ResponseTooLargeError":
            recommendation = (
                "Increase crawler.max_response_bytes only for trusted sources, "
                "and keep the configured hard limit bounded."
            )
        elif error_type == "OCRUnavailableError":
            recommendation = (
                "Install/configure Tesseract and the requested language model, "
                "then retry this PDF."
            )
        elif error_type == "PDFTextExtractionError":
            recommendation = (
                "Inspect the scanned PDF and OCR page limits; retry after fixing "
                "the OCR runtime or source file."
            )
        else:
            recommendation = "Inspect the URL/status and retry only failed resources."
        count = len(grouped_errors)
        issue(
            "error",
            "CRAWLER_ERROR",
            "crawler_error_group",
            f"{stage}:{error_type}:{category}",
            (
                f"{count} crawler failure(s) grouped as "
                f"{error_type}/{category}."
            ),
            recommendation,
            occurrence_count=count,
            sample_urls=sample_urls,
            member_ids=[
                str(
                    value.get("error_id")
                    or value.get("url")
                    or "unknown"
                )
                for value in grouped_errors
            ],
        )

    issue_counts = Counter(
        (item["target_type"], item["target_id"]) for item in issues
    )
    chunks_per_document: Counter[str] = Counter()
    for chunk in preview_chunks:
        document_id = str(chunk["_preview"].get("document_id") or "")
        chunks_per_document[document_id] += 1
        chunk_id = str(
            chunk.get("chunk_id")
            or f"{chunk.get('chunk_set_id')}#{chunk.get('chunk_index', '?')}"
        )
        chunk["_preview"]["issue_count"] = issue_counts[("chunk", chunk_id)]
    for document in documents:
        document_id = str(document.get("observation_id") or "unknown")
        document["_preview"] = {
            "chunk_count": chunks_per_document[document_id],
            "chunk_set_count": len(sets_by_document.get(document_id, [])),
            "issue_count": issue_counts[("document", document_id)],
        }

    severity_counts = Counter(item["severity"] for item in issues)
    token_values = [
        value
        for value in (
            _as_int(item.get("token_count")) for item in preview_chunks
        )
        if value is not None
    ]
    quality_counts = Counter(
        (
            str(item["extraction_quality"].get("status") or "unknown")
            if isinstance(item.get("extraction_quality"), dict)
            else "unknown"
        )
        for item in documents
    )
    source_counts = Counter(
        str(item.get("source_namespace") or "unknown") for item in documents
    )
    mime_counts = Counter(
        str(item.get("mime_type") or "unknown") for item in documents
    )

    return {
        "job_id": job_id,
        "manifest": dict(manifest),
        "documents": documents,
        "chunk_sets": preview_sets,
        "chunks": preview_chunks,
        "crawler_errors": crawler_errors,
        "issues": issues,
        "summary": {
            "documents": len(documents),
            "chunk_sets": len(preview_sets),
            "chunks": len(preview_chunks),
            "crawler_errors": len(crawler_errors),
            "actionable_issues": (
                severity_counts["error"] + severity_counts["warning"]
            ),
            "severity_counts": {
                "error": severity_counts["error"],
                "warning": severity_counts["warning"],
                "info": severity_counts["info"],
            },
            "token_stats": _token_stats(token_values),
            "quality_counts": dict(sorted(quality_counts.items())),
            "source_counts": dict(sorted(source_counts.items())),
            "mime_counts": dict(sorted(mime_counts.items())),
        },
    }


def _as_int(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _string_list(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    return [str(item) for item in value if str(item).strip()]


def _token_stats(values: list[int]) -> dict[str, float | int]:
    if not values:
        return {
            "min": 0,
            "median": 0,
            "p95": 0,
            "max": 0,
            "average": 0,
        }
    ordered = sorted(values)
    count = len(ordered)
    midpoint = count // 2
    median: float | int
    if count % 2:
        median = ordered[midpoint]
    else:
        median = round(
            (ordered[midpoint - 1] + ordered[midpoint]) / 2,
            1,
        )
    return {
        "min": ordered[0],
        "median": median,
        "p95": ordered[max(0, math.ceil(count * 0.95) - 1)],
        "max": ordered[-1],
        "average": round(sum(ordered) / count, 1),
    }


__all__ = ["build_preview_html", "build_preview_payload"]
