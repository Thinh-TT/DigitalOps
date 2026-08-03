from __future__ import annotations

import asyncio
from datetime import datetime, timezone
import hashlib
import json
import logging
from pathlib import Path
from typing import Callable, List, Optional, Sequence
from uuid import uuid4

from ..adapters.base import BaseAdapter, CrawlResult, NotModifiedResult
from ..chunkers.structure_chunker import StructureChunker
from ..cleaners.text_cleaner import TextCleaner
from ..db.state_store import CrawlerStateStore, FrontierItem
from ..exporters.staging_exporter import StagingExporter
from ..extractors.docx_extractor import DOCXExtractor
from ..extractors.html_extractor import HTMLExtractor
from ..extractors.pdf_extractor import PDFExtractor
from ..models.chunk import Chunk, ChunkSet
from ..models.error import CrawlerError, ErrorStage
from ..models.observation import (
    DocumentIdentityStrategy,
    DocumentObservation,
    ExtractionQuality,
    QualityStatus,
)
from ..paths import resolve_job_dir, validate_job_id
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
        self.state = CrawlerStateStore(state_db_path)

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
            raise ValueError(
                "legacy binary .doc is unsupported; convert the document to DOCX or PDF"
            )
        else:
            extractor = HTMLExtractor()
        return await asyncio.to_thread(extractor.extract, raw_path)

    @staticmethod
    def _header(result: CrawlResult, name: str) -> Optional[str]:
        for key, value in (result.response_headers or {}).items():
            if key.lower() == name.lower():
                return value
        return None

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
        return CrawlResult(
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

    @staticmethod
    def _checkpoint_path(checkpoint_dir: Path, frontier_url: str) -> Path:
        digest = hashlib.sha256(frontier_url.encode("utf-8")).hexdigest()
        return checkpoint_dir / f"{digest}.json"

    def _write_checkpoint(
        self,
        checkpoint_dir: Path,
        frontier_url: str,
        observation: DocumentObservation,
        chunk_set: ChunkSet,
        chunks: list[Chunk],
    ) -> None:
        checkpoint_dir.mkdir(parents=True, exist_ok=True)
        path = self._checkpoint_path(checkpoint_dir, frontier_url)
        temporary = path.with_suffix(".tmp")
        payload = {
            "frontier_url": frontier_url,
            "observation": observation.model_dump(mode="json"),
            "chunk_set": chunk_set.model_dump(mode="json"),
            "chunks": [chunk.model_dump(mode="json") for chunk in chunks],
        }
        temporary.write_text(
            json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
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
            frontier_urls.add(frontier_url)
        return observations, chunk_tuples, frontier_urls

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
        resource_limit = min(
            max_resources or self.max_total_resources,
            self.max_total_resources,
        )
        if resource_limit < 1:
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
            [(url, self.crawl_policy.priority(url)) for url in seeds],
        )
        # A resumed job may contain pending URLs discovered under an older
        # policy (for example legacy .doc attachments). Revalidate them before
        # claiming work so policy upgrades also apply to durable frontiers.
        for url in self.state.pending_frontier_urls(job_id):
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
        if progress_callback and observations:
            progress_callback(len(observations))

        try:
            while True:
                counts = self.state.frontier_counts(job_id)
                terminal_count = sum(
                    counts.get(status, 0)
                    for status in ("done", "failed", "skipped")
                )
                remaining = resource_limit - terminal_count
                if remaining <= 0:
                    break
                batch = self.state.claim_frontier(
                    job_id,
                    min(self.max_concurrent, remaining),
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
                        except Exception:
                            logger.warning(
                                "Conditional cache unusable for %s (%s); refetching",
                                item.url,
                                type(exc).__name__,
                            )
                            try:
                                fetched = await self.adapter.fetch_and_parse(item.url)
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
                        status_code = int(getattr(fetched, "status_code", 0) or 0)
                        elapsed_ms = int(getattr(fetched, "elapsed_ms", 0) or 0)
                        errors.append(
                            self._error(
                                job_id=job_id,
                                source_id=self.adapter.source_id,
                                url=item.url,
                                stage=ErrorStage.FETCH,
                                error_type=type(fetched).__name__ if fetched is not None else "FetchFailed",
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
                        self.state.mark_frontier(job_id, item.url, "failed", message)
                        continue

                    result = fetched
                    candidates = []
                    for link in self.crawl_policy.candidates(
                        result.discovered_links
                    ):
                        next_depth = self.crawl_policy.next_depth(
                            link,
                            parent_depth=item.depth,
                            max_depth=max_depth,
                        )
                        if next_depth is None:
                            continue
                        if self.crawl_policy.is_pagination(link):
                            if link not in pagination_urls:
                                if len(pagination_urls) >= self.max_pagination_pages:
                                    continue
                                pagination_urls.add(link)
                        candidates.append(
                            (
                                link,
                                next_depth,
                                self.crawl_policy.priority(link),
                                item.url,
                            )
                        )
                    self.state.enqueue_frontier(job_id, candidates)

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
                            url=item.url,
                            fetch_status="skipped",
                            http_status=result.http_status,
                            content_hash=raw_sha256,
                            canonical_document_key=result.canonical_key,
                            bytes_downloaded=len(result.html_or_bytes),
                            error_message=duplicate_reason,
                            final_url=result.final_url or result.url,
                            execution_time_ms=result.elapsed_ms,
                        )
                        self.state.mark_frontier(
                            job_id, item.url, "skipped", duplicate_reason
                        )
                        continue

                    mime = result.mime_type.lower()
                    if mime.startswith(("image/", "video/", "audio/")):
                        self.state.record_fetch(
                            job_id=job_id,
                            source_id=self.adapter.source_id,
                            url=item.url,
                            fetch_status="skipped",
                            http_status=result.http_status,
                            content_hash=raw_sha256,
                            canonical_document_key=result.canonical_key,
                            bytes_downloaded=len(result.html_or_bytes),
                            final_url=result.final_url or result.url,
                            execution_time_ms=result.elapsed_ms,
                        )
                        self.state.mark_frontier(job_id, item.url, "skipped")
                        continue

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
                        logger.exception("Extraction failed for %s", item.url)
                        errors.append(
                            self._error(
                                job_id=job_id,
                                source_id=self.adapter.source_id,
                                url=item.url,
                                stage=ErrorStage.EXTRACT,
                                error_type=type(exc).__name__,
                                message=str(exc),
                            )
                        )
                        self.state.record_fetch(
                            job_id=job_id,
                            source_id=self.adapter.source_id,
                            url=item.url,
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
                        self.state.mark_frontier(job_id, item.url, "failed", str(exc))
                        continue

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
                        logger.exception("Chunking failed for %s", item.url)
                        errors.append(
                            self._error(
                                job_id=job_id,
                                source_id=self.adapter.source_id,
                                url=item.url,
                                stage=ErrorStage.CHUNK,
                                error_type=type(exc).__name__,
                                message=str(exc),
                            )
                        )
                        self.state.record_fetch(
                            job_id=job_id,
                            source_id=self.adapter.source_id,
                            url=item.url,
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
                        self.state.mark_frontier(job_id, item.url, "failed", str(exc))
                        continue
                    self._write_checkpoint(
                        checkpoint_dir,
                        item.url,
                        observation,
                        chunk_set,
                        chunks,
                    )
                    observations.append(observation)
                    chunk_tuples.append((chunk_set, chunks))
                    canonical_keys.add(result.canonical_key)
                    content_hashes.add(raw_sha256)
                    self.state.record_fetch(
                        job_id=job_id,
                        source_id=self.adapter.source_id,
                        url=item.url,
                        fetch_status="fetched" if result.http_status == 200 else "skipped",
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
                    self.state.mark_frontier(job_id, item.url, "done")
                    if progress_callback:
                        progress_callback(len(observations))

            completed_at = datetime.now(timezone.utc)
            output_dir = StagingExporter(self.staging_dir).export(
                job_id=job_id,
                started_at=started_at,
                completed_at=completed_at,
                observations=observations,
                chunk_tuples=chunk_tuples,
                errors=errors,
            )
            counts = self.state.frontier_counts(job_id)
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
