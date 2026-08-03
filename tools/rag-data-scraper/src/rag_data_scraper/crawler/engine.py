from __future__ import annotations

import asyncio
from datetime import datetime, timezone
import hashlib
import json
import logging
from pathlib import Path
from typing import Callable, List, Optional, Sequence
from urllib.parse import urlsplit
from uuid import uuid4

from ..adapters.base import BaseAdapter, CrawlResult, NotModifiedResult
from ..chunkers.structure_chunker import StructureChunker
from ..cleaners.text_cleaner import TextCleaner
from ..db.state_store import CrawlerStateStore, FrontierItem
from ..exporters.staging_exporter import StagingExporter
from ..extractors.docx_extractor import DOCXExtractor
from ..extractors.html_extractor import HTMLExtractor
from ..extractors.legacy_doc_extractor import LegacyDocExtractor
from ..extractors.pdf_extractor import PDFExtractor
from ..models.chunk import Chunk, ChunkSet
from ..models.error import CrawlerError, ErrorStage
from ..models.observation import (
    DocumentIdentityStrategy,
    DocumentObservation,
    ExtractionQuality,
    QualityStatus,
    SourceProvenance,
)
from ..parsers.legal_metadata import LegalMetadataParser
from ..paths import resolve_job_dir, validate_job_id
from ..source_registry import ResolvedSourceProfile
from .policy import CrawlPolicy


logger = logging.getLogger(__name__)
ProgressCallback = Callable[[int], None]


class CrawlEngine:
    """Bounded crawler with durable frontier, checkpoints, and exact offsets."""

    def __init__(
        self,
        adapter: BaseAdapter,
        state_db_path: Path | str = "storage/state/crawler.db",
        staging_dir: Path | str = "storage/staging",
        raw_dir: Path | str = "storage/raw",
        max_concurrent: int = 5,
        max_total_resources: int = 500,
        max_pagination_pages: int = 25,
        chunker: Optional[StructureChunker] = None,
        crawl_policy: Optional[CrawlPolicy] = None,
        ocr_tesseract_cmd: str = "tesseract",
        ocr_lang: str = "vie+eng",
        ocr_min_confidence: float = 60.0,
        ocr_tessdata_dir: Path | str | None = None,
        max_ocr_pages: int = 50,
        max_ocr_image_pixels: int = 3_000_000,
        ocr_page_timeout_seconds: float = 30.0,
        legacy_doc_soffice_cmd: str | None = None,
        legacy_doc_timeout_seconds: float = 60.0,
        legacy_doc_max_output_bytes: int = 64 * 1024 * 1024,
    ) -> None:
        if max_concurrent < 1:
            raise ValueError("max_concurrent must be positive")
        if max_total_resources < 1:
            raise ValueError("max_total_resources must be positive")
        if max_pagination_pages < 1:
            raise ValueError("max_pagination_pages must be positive")
        self.adapter = adapter
        self.staging_dir = Path(staging_dir)
        self.raw_dir = Path(raw_dir)
        self.max_concurrent = max_concurrent
        self.max_total_resources = max_total_resources
        self.max_pagination_pages = max_pagination_pages
        self.chunker = chunker or StructureChunker()
        self.crawl_policy = crawl_policy or CrawlPolicy()
        self.pdf_extractor = PDFExtractor(
            tesseract_cmd=ocr_tesseract_cmd,
            lang=ocr_lang,
            min_confidence=ocr_min_confidence,
            tessdata_dir=ocr_tessdata_dir,
            max_ocr_pages=max_ocr_pages,
            max_image_pixels=max_ocr_image_pixels,
            page_timeout_seconds=ocr_page_timeout_seconds,
        )
        self.legacy_doc_extractor = LegacyDocExtractor(
            soffice_cmd=legacy_doc_soffice_cmd,
            timeout_seconds=legacy_doc_timeout_seconds,
            max_output_bytes=legacy_doc_max_output_bytes,
        )
        self.state = CrawlerStateStore(state_db_path)
        self.last_run_metrics: dict[str, int | bool] = {}

    @staticmethod
    def _error(
        *,
        job_id: str,
        source_id: str,
        url: str,
        stage: ErrorStage,
        error_type: str,
        message: str,
    ) -> CrawlerError:
        return CrawlerError(
            job_id=job_id,
            source_id=source_id,
            url=url,
            stage=stage,
            error_type=error_type,
            message=message,
        )

    async def _fetch_batch(
        self,
        items: Sequence[FrontierItem],
    ) -> List[Optional[CrawlResult] | NotModifiedResult | BaseException]:
        conditional_fetch = getattr(
            type(self.adapter),
            "fetch_and_parse_conditional",
            None,
        )

        async def fetch(item: FrontierItem):
            # Some integrations use a lightweight mock/duck-typed adapter that only
            # implements the original fetch method. Do not mistake AsyncMock's
            # dynamically-created attributes for an implemented conditional seam.
            if callable(conditional_fetch):
                return await self.adapter.fetch_and_parse_conditional(
                    item.url,
                    self.state.conditional_headers(
                        item.url,
                        self.adapter.source_id,
                        raw_base_dir=self.raw_dir,
                    ),
                )
            return await self.adapter.fetch_and_parse(item.url)

        return list(
            await asyncio.gather(
                *(fetch(item) for item in items),
                return_exceptions=True,
            )
        )

    @staticmethod
    def _file_extension(result: CrawlResult) -> str:
        mime = result.mime_type.lower()
        if mime == "application/pdf":
            return ".pdf"
        if "wordprocessingml" in mime:
            return ".docx"
        if mime == "application/msword":
            return ".doc"
        return ".html"

    async def _extract(self, result: CrawlResult, raw_path: Path):
        mime = result.mime_type.lower()
        if mime == "application/pdf":
            extractor = self.pdf_extractor
        elif "wordprocessingml" in mime:
            extractor = DOCXExtractor()
        elif mime == "application/msword":
            extractor = self.legacy_doc_extractor
        else:
            extractor = HTMLExtractor()
        return await asyncio.to_thread(extractor.extract, raw_path)

    @staticmethod
    def _header(result: CrawlResult, name: str) -> Optional[str]:
        for key, value in (result.response_headers or {}).items():
            if key.lower() == name.lower():
                return value
        return None

    def _source_provenance(
        self,
        result: CrawlResult,
        normalized_text_sha256: str,
    ) -> SourceProvenance:
        profile = self._resolved_source_profile()
        domain = (
            urlsplit(result.final_url or result.url).hostname
            or result.source_namespace
        ).lower().rstrip(".")
        source_version = str(
            result.metadata.get("source_version")
            or f"sha256:{normalized_text_sha256}"
        )
        if profile is None:
            return SourceProvenance(
                source_domain=domain,
                source_version=source_version,
            )
        return SourceProvenance(
            registry_entry_id=profile.entry_id,
            registry_version=profile.registry_version,
            corpus_type=profile.corpus_type,
            source_trust_tier=profile.source_trust_tier,
            source_domain=domain,
            source_version=source_version,
            publish_policy=profile.publish_policy,
            language=profile.language,
        )

    def _resolved_source_profile(self) -> Optional[ResolvedSourceProfile]:
        profile = getattr(self.adapter, "source_profile", None)
        return profile if isinstance(profile, ResolvedSourceProfile) else None

    def _cached_result(
        self,
        item: FrontierItem,
        not_modified: NotModifiedResult,
    ) -> CrawlResult:
        cached = self.state.cached_resource(item.url, self.adapter.source_id)
        if cached is None:
            raise ValueError("304 response has no reusable cached resource")
        raw_path = Path(cached.raw_artifact_uri).resolve()
        try:
            raw_path.relative_to(self.raw_dir.resolve())
        except ValueError as exc:
            raise ValueError("cached raw artifact escapes the raw directory") from exc
        content = raw_path.read_bytes()
        if hashlib.sha256(content).hexdigest() != cached.content_hash:
            raise ValueError("cached raw artifact failed its SHA-256 check")
        metadata = dict(cached.metadata)
        metadata["cache_reused"] = True
        result = CrawlResult(
            url=cached.final_url,
            canonical_key=cached.canonical_document_key,
            title=cached.title,
            html_or_bytes=content,
            mime_type=cached.mime_type,
            document_identity_strategy=cached.document_identity_strategy,
            source_namespace=cached.source_namespace,
            authority_namespace=cached.authority_namespace,
            metadata=metadata,
            discovered_links=cached.discovered_links,
            requested_url=not_modified.requested_url,
            final_url=cached.final_url,
            http_status=304,
            response_headers=not_modified.response_headers,
            attempt_count=not_modified.attempt_count,
            elapsed_ms=not_modified.elapsed_ms,
        )
        return self.adapter.rehydrate_cached_result(result)

    @staticmethod
    def _checkpoint_path(checkpoint_dir: Path, checkpoint_key: str) -> Path:
        digest = hashlib.sha256(checkpoint_key.encode("utf-8")).hexdigest()
        return checkpoint_dir / f"{digest}.json"

    @staticmethod
    def _completion_path(checkpoint_dir: Path, frontier_url: str) -> Path:
        digest = hashlib.sha256(frontier_url.encode("utf-8")).hexdigest()
        return checkpoint_dir / f"{digest}.done"

    def _write_checkpoint(
        self,
        checkpoint_dir: Path,
        frontier_url: str,
        observation: DocumentObservation,
        chunk_set: ChunkSet,
        chunks: list[Chunk],
        *,
        checkpoint_key: Optional[str] = None,
        frontier_complete: bool = True,
    ) -> None:
        checkpoint_dir.mkdir(parents=True, exist_ok=True)
        path = self._checkpoint_path(
            checkpoint_dir,
            checkpoint_key or frontier_url,
        )
        temporary = path.with_suffix(".tmp")
        payload = {
            "frontier_url": frontier_url,
            "observation": observation.model_dump(mode="json"),
            "chunk_set": chunk_set.model_dump(mode="json"),
            "chunks": [chunk.model_dump(mode="json") for chunk in chunks],
            "frontier_complete": frontier_complete,
        }
        temporary.write_text(
            json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
            encoding="utf-8",
            newline="\n",
        )
        temporary.replace(path)

    def _write_frontier_completion(
        self,
        checkpoint_dir: Path,
        frontier_url: str,
        job_id: str,
    ) -> None:
        checkpoint_dir.mkdir(parents=True, exist_ok=True)
        path = self._completion_path(checkpoint_dir, frontier_url)
        temporary = path.with_suffix(".tmp")
        temporary.write_text(
            json.dumps(
                {"job_id": job_id, "frontier_url": frontier_url},
                ensure_ascii=False,
                separators=(",", ":"),
            ),
            encoding="utf-8",
            newline="\n",
        )
        temporary.replace(path)

    def _load_checkpoints(
        self,
        checkpoint_dir: Path,
        job_raw_dir: Path,
        job_id: str,
    ) -> tuple[
        list[DocumentObservation],
        list[tuple[ChunkSet, list[Chunk]]],
        set[str],
    ]:
        observations: list[DocumentObservation] = []
        chunk_tuples: list[tuple[ChunkSet, list[Chunk]]] = []
        frontier_urls: set[str] = set()
        if not checkpoint_dir.is_dir():
            return observations, chunk_tuples, frontier_urls
        scope = job_raw_dir.resolve()
        for path in sorted(checkpoint_dir.glob("*.json")):
            try:
                payload = json.loads(path.read_text(encoding="utf-8"))
                observation = DocumentObservation.model_validate(payload["observation"])
                chunk_set = ChunkSet.model_validate(payload["chunk_set"])
                chunks = [Chunk.model_validate(value) for value in payload["chunks"]]
                frontier_url = str(payload["frontier_url"])
                if observation.job_id != job_id or chunk_set.job_id != job_id:
                    raise ValueError("checkpoint belongs to another job")
                raw_path = Path(observation.raw_artifact_uri).resolve()
                normalized_path = Path(observation.normalized_text_uri).resolve()
                raw_path.relative_to(scope)
                normalized_path.relative_to(scope)
                raw_bytes = raw_path.read_bytes()
                normalized_bytes = normalized_path.read_bytes()
                if hashlib.sha256(raw_bytes).hexdigest() != observation.raw_sha256:
                    raise ValueError("checkpoint raw artifact hash mismatch")
                if (
                    hashlib.sha256(normalized_bytes).hexdigest()
                    != observation.normalized_text_sha256
                ):
                    raise ValueError("checkpoint normalized text hash mismatch")
                normalized_text = normalized_bytes.decode("utf-8")
                if chunk_set.observation_id != observation.observation_id:
                    raise ValueError("checkpoint chunk set relation is invalid")
                if chunk_set.total_chunks != len(chunks):
                    raise ValueError("checkpoint chunk count is invalid")
                for expected_index, chunk in enumerate(chunks):
                    if (
                        chunk.chunk_set_id != chunk_set.chunk_set_id
                        or chunk.chunk_index != expected_index
                        or normalized_text[
                            chunk.character_start : chunk.character_end
                        ] != chunk.text
                        or hashlib.sha256(chunk.text.encode("utf-8")).hexdigest()
                        != chunk.content_sha256
                    ):
                        raise ValueError("checkpoint chunk integrity is invalid")
            except (
                KeyError,
                OSError,
                TypeError,
                UnicodeError,
                ValueError,
                json.JSONDecodeError,
            ) as exc:
                logger.warning("Ignoring invalid checkpoint %s: %s", path, exc)
                continue
            observations.append(observation)
            chunk_tuples.append((chunk_set, chunks))
            if bool(payload.get("frontier_complete", True)):
                frontier_urls.add(frontier_url)
        for path in sorted(checkpoint_dir.glob("*.done")):
            try:
                payload = json.loads(path.read_text(encoding="utf-8"))
                if payload.get("job_id") != job_id:
                    raise ValueError("completion checkpoint belongs to another job")
                frontier_url = str(payload["frontier_url"])
            except (
                KeyError,
                OSError,
                TypeError,
                ValueError,
                json.JSONDecodeError,
            ) as exc:
                logger.warning(
                    "Ignoring invalid completion checkpoint %s: %s",
                    path,
                    exc,
                )
                continue
            frontier_urls.add(frontier_url)
        return observations, chunk_tuples, frontier_urls

    async def _materialize_document(
        self,
        *,
        result: CrawlResult,
        record_url: str,
        frontier_url: str,
        checkpoint_key: str,
        frontier_complete: bool,
        job_id: str,
        job_raw_dir: Path,
        checkpoint_dir: Path,
        observations: list[DocumentObservation],
        chunk_tuples: list[tuple[ChunkSet, list[Chunk]]],
        canonical_keys: set[str],
        content_hashes: set[str],
        errors: list[CrawlerError],
    ) -> tuple[str, Optional[DocumentObservation]]:
        raw_sha256 = hashlib.sha256(result.html_or_bytes).hexdigest()
        duplicate_reason = None
        if result.canonical_key in canonical_keys:
            duplicate_reason = "duplicate canonical document key"
        elif raw_sha256 in content_hashes:
            duplicate_reason = "duplicate raw content hash"
        if duplicate_reason:
            self.state.record_fetch(
                job_id=job_id,
                source_id=self.adapter.source_id,
                url=record_url,
                fetch_status="skipped",
                http_status=result.http_status,
                content_hash=raw_sha256,
                canonical_document_key=result.canonical_key,
                bytes_downloaded=len(result.html_or_bytes),
                error_message=duplicate_reason,
                final_url=result.final_url or result.url,
                execution_time_ms=result.elapsed_ms,
            )
            return "skipped", None

        mime = result.mime_type.lower()
        if mime.startswith(("image/", "video/", "audio/")):
            self.state.record_fetch(
                job_id=job_id,
                source_id=self.adapter.source_id,
                url=record_url,
                fetch_status="skipped",
                http_status=result.http_status,
                content_hash=raw_sha256,
                canonical_document_key=result.canonical_key,
                bytes_downloaded=len(result.html_or_bytes),
                final_url=result.final_url or result.url,
                execution_time_ms=result.elapsed_ms,
            )
            return "skipped", None

        raw_path = job_raw_dir / f"{uuid4().hex}{self._file_extension(result)}"
        await asyncio.to_thread(raw_path.write_bytes, result.html_or_bytes)

        try:
            extracted = await self._extract(result, raw_path)
            if not extracted.blocks:
                raise ValueError("extractor returned no text blocks")
            normalized_doc, normalized_text, text_sha256 = (
                TextCleaner.clean_document(extracted)
            )
            if not normalized_doc.blocks:
                raise ValueError("normalization removed every text block")
        except Exception as exc:
            logger.exception("Extraction failed for %s", record_url)
            errors.append(
                self._error(
                    job_id=job_id,
                    source_id=self.adapter.source_id,
                    url=record_url,
                    stage=ErrorStage.EXTRACT,
                    error_type=type(exc).__name__,
                    message=str(exc),
                )
            )
            self.state.record_fetch(
                job_id=job_id,
                source_id=self.adapter.source_id,
                url=record_url,
                fetch_status="failed",
                http_status=result.http_status,
                content_hash=raw_sha256,
                canonical_document_key=result.canonical_key,
                bytes_downloaded=len(result.html_or_bytes),
                etag=self._header(result, "ETag"),
                last_modified=self._header(result, "Last-Modified"),
                error_message=str(exc),
                final_url=result.final_url or result.url,
                mime_type=result.mime_type,
                title=result.title,
                raw_artifact_uri=str(raw_path.resolve()),
                metadata=result.metadata,
                discovered_links=result.discovered_links,
                document_identity_strategy=result.document_identity_strategy,
                resource_source_namespace=result.source_namespace,
                resource_authority_namespace=result.authority_namespace,
                execution_time_ms=result.elapsed_ms,
            )
            return "failed", None

        normalized_path = job_raw_dir / f"{uuid4().hex}_norm.txt"
        normalized_path.write_text(
            normalized_text,
            encoding="utf-8",
            newline="\n",
        )
        try:
            strategy = DocumentIdentityStrategy(
                result.document_identity_strategy
            )
        except ValueError:
            strategy = DocumentIdentityStrategy.CONTENT_ONLY

        observation = DocumentObservation(
            job_id=job_id,
            source_id=self.adapter.source_id,
            source_namespace=result.source_namespace,
            authority_namespace=result.authority_namespace,
            document_identity_strategy=strategy,
            canonical_document_key=result.canonical_key,
            source_document_url=result.final_url or result.url,
            title=result.title,
            raw_artifact_uri=str(raw_path.resolve()),
            raw_sha256=raw_sha256,
            mime_type=result.mime_type,
            normalized_text_uri=str(normalized_path.resolve()),
            normalized_text_sha256=text_sha256,
            char_count=len(normalized_text),
            word_count=len(normalized_text.split()),
            extraction_quality=ExtractionQuality(
                status=(
                    QualityStatus.TRUNCATED
                    if normalized_doc.truncated
                    else QualityStatus.OCR_FALLBACK
                    if extracted.ocr_used
                    else QualityStatus.CLEAN
                ),
                ocr_used=extracted.ocr_used,
                confidence_score=extracted.ocr_confidence,
            ),
            source_provenance=self._source_provenance(
                result,
                text_sha256,
            ),
            legal_metadata=LegalMetadataParser.normalize(
                {
                    **result.metadata,
                    **normalized_doc.document_metadata,
                },
                self._resolved_source_profile(),
            ),
            document_metadata={
                **result.metadata,
                **normalized_doc.document_metadata,
            },
        )
        try:
            chunk_set, chunks = self.chunker.chunk(
                normalized_doc,
                observation.observation_id,
                job_id,
                normalized_text=normalized_text,
            )
        except Exception as exc:
            logger.exception("Chunking failed for %s", record_url)
            errors.append(
                self._error(
                    job_id=job_id,
                    source_id=self.adapter.source_id,
                    url=record_url,
                    stage=ErrorStage.CHUNK,
                    error_type=type(exc).__name__,
                    message=str(exc),
                )
            )
            self.state.record_fetch(
                job_id=job_id,
                source_id=self.adapter.source_id,
                url=record_url,
                fetch_status="failed",
                http_status=result.http_status,
                content_hash=raw_sha256,
                canonical_document_key=result.canonical_key,
                bytes_downloaded=len(result.html_or_bytes),
                etag=self._header(result, "ETag"),
                last_modified=self._header(result, "Last-Modified"),
                error_message=str(exc),
                final_url=result.final_url or result.url,
                mime_type=result.mime_type,
                title=result.title,
                raw_artifact_uri=str(raw_path.resolve()),
                metadata=result.metadata,
                discovered_links=result.discovered_links,
                document_identity_strategy=result.document_identity_strategy,
                resource_source_namespace=result.source_namespace,
                resource_authority_namespace=result.authority_namespace,
                execution_time_ms=result.elapsed_ms,
            )
            return "failed", None

        self._write_checkpoint(
            checkpoint_dir,
            frontier_url,
            observation,
            chunk_set,
            chunks,
            checkpoint_key=checkpoint_key,
            frontier_complete=frontier_complete,
        )
        observations.append(observation)
        chunk_tuples.append((chunk_set, chunks))
        canonical_keys.add(result.canonical_key)
        content_hashes.add(raw_sha256)
        self.state.record_fetch(
            job_id=job_id,
            source_id=self.adapter.source_id,
            url=record_url,
            fetch_status=(
                "fetched" if result.http_status == 200 else "skipped"
            ),
            http_status=result.http_status,
            content_hash=raw_sha256,
            canonical_document_key=result.canonical_key,
            bytes_downloaded=(
                len(result.html_or_bytes) if result.http_status == 200 else 0
            ),
            etag=self._header(result, "ETag"),
            last_modified=self._header(result, "Last-Modified"),
            final_url=result.final_url or result.url,
            mime_type=result.mime_type,
            title=result.title,
            raw_artifact_uri=str(raw_path.resolve()),
            metadata=result.metadata,
            discovered_links=result.discovered_links,
            document_identity_strategy=result.document_identity_strategy,
            resource_source_namespace=result.source_namespace,
            resource_authority_namespace=result.authority_namespace,
            execution_time_ms=result.elapsed_ms,
        )
        return "done", observation

    async def _record_discovery_page(
        self,
        *,
        result: CrawlResult,
        frontier_url: str,
        job_id: str,
        job_raw_dir: Path,
    ) -> None:
        raw_path = job_raw_dir / f"{uuid4().hex}.html"
        await asyncio.to_thread(raw_path.write_bytes, result.html_or_bytes)
        raw_sha256 = hashlib.sha256(result.html_or_bytes).hexdigest()
        self.state.record_fetch(
            job_id=job_id,
            source_id=self.adapter.source_id,
            url=frontier_url,
            fetch_status=(
                "fetched" if result.http_status == 200 else "skipped"
            ),
            http_status=result.http_status,
            content_hash=raw_sha256,
            canonical_document_key=result.canonical_key,
            bytes_downloaded=(
                len(result.html_or_bytes) if result.http_status == 200 else 0
            ),
            etag=self._header(result, "ETag"),
            last_modified=self._header(result, "Last-Modified"),
            final_url=result.final_url or result.url,
            mime_type=result.mime_type,
            title=result.title,
            raw_artifact_uri=str(raw_path.resolve()),
            metadata={
                **result.metadata,
                "discovery_only": True,
                "embedded_document_count": len(result.embedded_documents),
            },
            discovered_links=result.discovered_links,
            document_identity_strategy=result.document_identity_strategy,
            resource_source_namespace=result.source_namespace,
            resource_authority_namespace=result.authority_namespace,
            execution_time_ms=result.elapsed_ms,
        )

    async def run_job(
        self,
        job_id: str,
        seed_urls: List[str],
        max_depth: int = 1,
        max_resources: Optional[int] = None,
        progress_callback: Optional[ProgressCallback] = None,
    ) -> Path:
        job_id = validate_job_id(job_id)
        if max_depth < 0:
            raise ValueError("max_depth cannot be negative")
        document_limit = max_resources or self.max_total_resources
        if document_limit < 1:
            raise ValueError("max_resources must be positive")

        seeds = self.crawl_policy.candidates(
            url.strip() for url in seed_urls if url.strip()
        )
        if not seeds:
            raise ValueError("at least one eligible seed URL is required")

        started_at = datetime.now(timezone.utc)
        job_raw_dir = resolve_job_dir(self.raw_dir, job_id)
        job_raw_dir.mkdir(parents=True, exist_ok=True)
        checkpoint_dir = job_raw_dir / "checkpoints"
        resolve_job_dir(self.staging_dir, job_id)

        self.state.start_job(
            job_id=job_id,
            source_id=self.adapter.source_id,
            source_namespace=self.adapter.source_namespace,
            authority_namespace=self.adapter.authority_namespace,
            identity_strategy=self.adapter.default_identity_strategy,
            base_url=seeds[0],
        )
        self.state.prepare_frontier(
            job_id,
            [
                (url, self.crawl_policy.priority(url), "document")
                for url in seeds
            ],
        )
        # A resumed job may contain pending URLs discovered under an older
        # policy (for example legacy .doc attachments). Revalidate them before
        # claiming work so policy upgrades also apply to durable frontiers.
        for url in self.state.pending_frontier_urls(job_id):
            if url not in seeds:
                inferred_kind = self.crawl_policy.resource_kind(url)
                if inferred_kind != "document":
                    self.state.set_frontier_resource_kind(
                        job_id,
                        url,
                        inferred_kind,
                    )
            if not self.crawl_policy.should_visit(url):
                self.state.mark_frontier(
                    job_id,
                    url,
                    "skipped",
                    "URL is no longer eligible under the current crawl policy",
                )
        pagination_urls = {
            url
            for url in self.state.frontier_urls(job_id)
            if self.crawl_policy.is_pagination(url)
        }

        observations, chunk_tuples, checkpoint_urls = self._load_checkpoints(
            checkpoint_dir,
            job_raw_dir,
            job_id,
        )
        for url in checkpoint_urls:
            self.state.mark_frontier(job_id, url, "done")
        errors: list[CrawlerError] = []
        canonical_keys = {
            observation.canonical_document_key for observation in observations
        }
        content_hashes = {observation.raw_sha256 for observation in observations}
        primary_document_count = sum(
            observation.document_metadata.get("crawl_role") != "attachment"
            for observation in observations
        )
        attachment_count = len(observations) - primary_document_count
        listing_pages_scanned = 0
        records_discovered = primary_document_count
        attachment_urls: set[str] = set()
        attachment_parent_keys: dict[str, set[str]] = {}
        attachment_failures = 0
        fetch_limit_reached = False
        if progress_callback and primary_document_count:
            progress_callback(primary_document_count)

        try:
            while True:
                counts = self.state.frontier_counts(job_id)
                terminal_count = sum(
                    counts.get(status, 0)
                    for status in ("done", "failed", "skipped")
                )
                remaining_fetches = self.max_total_resources - terminal_count
                if remaining_fetches <= 0:
                    fetch_limit_reached = True
                    break

                document_limit_reached = (
                    primary_document_count >= document_limit
                )
                document_remaining = max(
                    1,
                    document_limit - primary_document_count,
                )
                batch = self.state.claim_frontier(
                    job_id,
                    min(
                        self.max_concurrent,
                        remaining_fetches,
                        (
                            document_remaining
                            if not document_limit_reached
                            else self.max_concurrent
                        ),
                    ),
                    resource_kinds=(
                        ("pagination", "attachment")
                        if document_limit_reached
                        else None
                    ),
                )
                if not batch:
                    break
                fetched_results = await self._fetch_batch(batch)

                for item, fetched in zip(batch, fetched_results):
                    if isinstance(fetched, asyncio.CancelledError):
                        raise fetched
                    if isinstance(fetched, NotModifiedResult):
                        try:
                            fetched = self._cached_result(item, fetched)
                        except Exception as cache_error:
                            logger.warning(
                                "Conditional cache unusable for %s (%s); refetching",
                                item.url,
                                type(cache_error).__name__,
                            )
                            try:
                                fetched = await self.adapter.fetch_and_parse(
                                    item.url
                                )
                            except Exception as exc:
                                fetched = exc

                    if isinstance(fetched, Exception) or not isinstance(
                        fetched,
                        CrawlResult,
                    ):
                        if isinstance(fetched, Exception):
                            message = str(fetched)
                        elif fetched is None:
                            message = "adapter returned no crawl result"
                        else:
                            message = "adapter returned an invalid crawl result"
                        status_code = int(
                            getattr(fetched, "status_code", 0) or 0
                        )
                        elapsed_ms = int(
                            getattr(fetched, "elapsed_ms", 0) or 0
                        )
                        errors.append(
                            self._error(
                                job_id=job_id,
                                source_id=self.adapter.source_id,
                                url=item.url,
                                stage=ErrorStage.FETCH,
                                error_type=(
                                    type(fetched).__name__
                                    if fetched is not None
                                    else "FetchFailed"
                                ),
                                message=message,
                            )
                        )
                        self.state.record_fetch(
                            job_id=job_id,
                            source_id=self.adapter.source_id,
                            url=item.url,
                            fetch_status="failed",
                            http_status=status_code,
                            error_message=message,
                            execution_time_ms=elapsed_ms,
                        )
                        self.state.mark_frontier(
                            job_id,
                            item.url,
                            "failed",
                            message,
                        )
                        if item.resource_kind == "attachment":
                            attachment_failures += 1
                        continue

                    result = fetched
                    main_candidates = []
                    for link in self.crawl_policy.candidates(
                        result.discovered_links
                    ):
                        resource_kind = self.crawl_policy.resource_kind(link)
                        if (
                            primary_document_count >= document_limit
                            and resource_kind != "pagination"
                        ):
                            continue
                        next_depth = self.crawl_policy.next_depth(
                            link,
                            parent_depth=item.depth,
                            max_depth=max_depth,
                        )
                        if next_depth is None:
                            continue
                        if self.crawl_policy.is_pagination(link):
                            if link not in pagination_urls:
                                if (
                                    len(pagination_urls)
                                    >= self.max_pagination_pages
                                ):
                                    continue
                                pagination_urls.add(link)
                        main_candidates.append(
                            (
                                link,
                                next_depth,
                                self.crawl_policy.priority(link),
                                item.url,
                                resource_kind,
                            )
                        )
                    self.state.enqueue_frontier(job_id, main_candidates)

                    if result.discovery_only:
                        listing_pages_scanned += 1
                        records_discovered += len(result.embedded_documents)
                        remaining_documents = max(
                            0,
                            document_limit - primary_document_count,
                        )
                        accepted_documents = result.embedded_documents[
                            :remaining_documents
                        ]
                        embedded_candidates = []
                        for document in accepted_documents:
                            attachment_urls.update(document.discovered_links)
                            if not self.crawl_policy.include_attachments:
                                continue
                            for link in self.crawl_policy.candidates(
                                document.discovered_links
                            ):
                                next_depth = self.crawl_policy.next_depth(
                                    link,
                                    parent_depth=item.depth,
                                    max_depth=max_depth,
                                )
                                if next_depth is None:
                                    continue
                                attachment_parent_keys.setdefault(
                                    link,
                                    set(),
                                ).add(document.canonical_key)
                                embedded_candidates.append(
                                    (
                                        link,
                                        next_depth,
                                        self.crawl_policy.priority(link),
                                        item.url,
                                        "attachment",
                                    )
                                )
                        self.state.enqueue_frontier(
                            job_id,
                            embedded_candidates,
                        )

                        for document in accepted_documents:
                            _, observation = await self._materialize_document(
                                result=document,
                                record_url=(
                                    document.final_url or document.url
                                ),
                                frontier_url=item.url,
                                checkpoint_key=(
                                    document.final_url or document.url
                                ),
                                frontier_complete=False,
                                job_id=job_id,
                                job_raw_dir=job_raw_dir,
                                checkpoint_dir=checkpoint_dir,
                                observations=observations,
                                chunk_tuples=chunk_tuples,
                                canonical_keys=canonical_keys,
                                content_hashes=content_hashes,
                                errors=errors,
                            )
                            if observation is not None:
                                primary_document_count += 1
                                if progress_callback:
                                    progress_callback(
                                        primary_document_count
                                    )

                        await self._record_discovery_page(
                            result=result,
                            frontier_url=item.url,
                            job_id=job_id,
                            job_raw_dir=job_raw_dir,
                        )
                        self._write_frontier_completion(
                            checkpoint_dir,
                            item.url,
                            job_id,
                        )
                        self.state.mark_frontier(
                            job_id,
                            item.url,
                            "done",
                        )
                        continue

                    is_attachment = item.resource_kind == "attachment"
                    if (
                        not is_attachment
                        and primary_document_count >= document_limit
                    ):
                        if item.resource_kind == "pagination":
                            await self._record_discovery_page(
                                result=result,
                                frontier_url=item.url,
                                job_id=job_id,
                                job_raw_dir=job_raw_dir,
                            )
                            self._write_frontier_completion(
                                checkpoint_dir,
                                item.url,
                                job_id,
                            )
                            self.state.mark_frontier(
                                job_id,
                                item.url,
                                "skipped",
                                "document output limit reached",
                            )
                        else:
                            self.state.release_frontier(job_id, item.url)
                        continue

                    if is_attachment:
                        parent_keys = sorted(
                            attachment_parent_keys.get(item.url, set())
                        )
                        result.metadata = {
                            **result.metadata,
                            "crawl_role": "attachment",
                            "parent_canonical_keys": parent_keys,
                        }

                    status, observation = await self._materialize_document(
                        result=result,
                        record_url=item.url,
                        frontier_url=item.url,
                        checkpoint_key=item.url,
                        frontier_complete=True,
                        job_id=job_id,
                        job_raw_dir=job_raw_dir,
                        checkpoint_dir=checkpoint_dir,
                        observations=observations,
                        chunk_tuples=chunk_tuples,
                        canonical_keys=canonical_keys,
                        content_hashes=content_hashes,
                        errors=errors,
                    )
                    self.state.mark_frontier(
                        job_id,
                        item.url,
                        status,
                    )
                    if is_attachment and status == "failed":
                        attachment_failures += 1
                    if observation is not None:
                        if is_attachment:
                            attachment_count += 1
                        else:
                            primary_document_count += 1
                            if progress_callback:
                                progress_callback(
                                    primary_document_count
                                )

            completed_at = datetime.now(timezone.utc)
            counts = self.state.frontier_counts(job_id)
            records_discovered = max(
                records_discovered,
                primary_document_count,
            )
            self.last_run_metrics = {
                "document_limit": document_limit,
                "primary_documents_created": primary_document_count,
                "observations_created": len(observations),
                "listing_pages_scanned": listing_pages_scanned,
                "records_discovered": records_discovered,
                "attachments_discovered": len(attachment_urls),
                "attachments_fetched": attachment_count,
                "attachment_failures": attachment_failures,
                "attachments_unprocessed": max(
                    0,
                    len(attachment_urls)
                    - attachment_count
                    - attachment_failures,
                ),
                "pagination_urls_seen": len(pagination_urls),
                "http_resources_terminal": sum(
                    counts.get(status, 0)
                    for status in ("done", "failed", "skipped")
                ),
                "http_fetch_limit": self.max_total_resources,
                "fetch_limit_reached": fetch_limit_reached,
                "legacy_doc_converted": sum(
                    bool(
                        observation.document_metadata.get(
                            "legacy_doc_converted"
                        )
                    )
                    for observation in observations
                ),
            }
            output_dir = StagingExporter(self.staging_dir).export(
                job_id=job_id,
                started_at=started_at,
                completed_at=completed_at,
                observations=observations,
                chunk_tuples=chunk_tuples,
                errors=errors,
                corpus_type=(
                    self._resolved_source_profile().corpus_type
                    if self._resolved_source_profile() is not None
                    else "general"
                ),
                source_registry_version=(
                    self._resolved_source_profile().registry_version
                    if self._resolved_source_profile() is not None
                    else None
                ),
                source_registry_entry_ids=(
                    [self._resolved_source_profile().entry_id]
                    if self._resolved_source_profile() is not None
                    else []
                ),
            )
            self.state.finish_job(
                job_id=job_id,
                status="completed",
                discovered=sum(counts.values()),
                crawled=len(observations),
                failed=counts.get("failed", 0),
                staging_directory=str(output_dir.resolve()),
            )
            return output_dir
        except Exception:
            counts = self.state.frontier_counts(job_id)
            self.state.finish_job(
                job_id=job_id,
                status="failed",
                discovered=sum(counts.values()),
                crawled=len(observations),
                failed=counts.get("failed", 0) + 1,
                staging_directory=None,
            )
            raise
        finally:
            try:
                await self.adapter.aclose()
            except Exception:
                logger.warning("Adapter cleanup failed", exc_info=True)
