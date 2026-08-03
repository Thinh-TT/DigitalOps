from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sqlite3
from typing import Any, Mapping, Optional, Sequence

from .init_sqlite import init_sqlite_db


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


@dataclass(frozen=True)
class FrontierItem:
    url: str
    depth: int
    priority: int
    attempts: int
    resource_kind: str = "document"


@dataclass(frozen=True)
class CachedResource:
    url: str
    final_url: str
    canonical_document_key: str
    content_hash: str
    mime_type: str
    title: str
    raw_artifact_uri: str
    metadata: dict[str, Any]
    discovered_links: list[str]
    document_identity_strategy: str
    source_namespace: str
    authority_namespace: Optional[str]


class CrawlerStateStore:
    """Transactional seam for jobs, conditional cache, and durable frontier."""

    def __init__(self, db_path: Path | str) -> None:
        self.db_path = Path(db_path)
        init_sqlite_db(self.db_path)

    def start_job(
        self,
        *,
        job_id: str,
        source_id: str,
        source_namespace: str,
        authority_namespace: Optional[str],
        identity_strategy: str,
        base_url: str,
    ) -> None:
        with sqlite3.connect(self.db_path) as connection:
            connection.execute(
                """
                INSERT INTO Sources (
                    source_id, source_name, source_namespace, authority_namespace,
                    identity_strategy, base_url
                ) VALUES (?, ?, ?, ?, ?, ?)
                ON CONFLICT(source_id) DO UPDATE SET
                    source_namespace = excluded.source_namespace,
                    authority_namespace = excluded.authority_namespace,
                    identity_strategy = excluded.identity_strategy,
                    base_url = excluded.base_url,
                    is_active = 1
                """,
                (
                    source_id,
                    source_id,
                    source_namespace,
                    authority_namespace,
                    identity_strategy,
                    base_url,
                ),
            )
            existing = connection.execute(
                "SELECT status FROM CrawlJobs WHERE job_id = ?",
                (job_id,),
            ).fetchone()
            if existing is None:
                connection.execute(
                    """
                    INSERT INTO CrawlJobs (job_id, source_id, status, started_at)
                    VALUES (?, ?, 'running', ?)
                    """,
                    (job_id, source_id, _utc_now()),
                )
            else:
                connection.execute(
                    """
                    UPDATE CrawlJobs
                    SET source_id = ?, status = 'running', completed_at = NULL,
                        staging_directory = NULL
                    WHERE job_id = ?
                    """,
                    (source_id, job_id),
                )

    def prepare_frontier(
        self,
        job_id: str,
        seeds: Sequence[tuple[str, int] | tuple[str, int, str]],
    ) -> None:
        now = _utc_now()
        with sqlite3.connect(self.db_path) as connection:
            connection.execute(
                """
                UPDATE CrawlFrontier
                SET status = 'pending', updated_at = ?
                WHERE job_id = ? AND status = 'running'
                """,
                (now, job_id),
            )
            normalized_seeds: list[tuple[str, int, str]] = []
            for seed in seeds:
                if len(seed) == 2:
                    url, priority = seed
                    kind = "document"
                else:
                    url, priority, kind = seed
                normalized_seeds.append((url, priority, kind))
            connection.executemany(
                """
                INSERT INTO CrawlFrontier (
                    job_id, url, depth, priority, resource_kind, status, updated_at
                ) VALUES (?, ?, 0, ?, ?, 'pending', ?)
                ON CONFLICT(job_id, url) DO UPDATE SET
                    priority = MAX(CrawlFrontier.priority, excluded.priority),
                    resource_kind = excluded.resource_kind,
                    updated_at = excluded.updated_at
                """,
                (
                    (job_id, url, priority, kind, now)
                    for url, priority, kind in normalized_seeds
                ),
            )

    def enqueue_frontier(
        self,
        job_id: str,
        candidates: Sequence[
            tuple[str, int, int, str] | tuple[str, int, int, str, str]
        ],
    ) -> None:
        if not candidates:
            return
        now = _utc_now()
        with sqlite3.connect(self.db_path) as connection:
            normalized_candidates: list[
                tuple[str, int, int, str, str]
            ] = []
            for candidate in candidates:
                if len(candidate) == 4:
                    url, depth, priority, parent = candidate
                    kind = "document"
                else:
                    url, depth, priority, parent, kind = candidate
                normalized_candidates.append(
                    (url, depth, priority, parent, kind)
                )
            connection.executemany(
                """
                INSERT INTO CrawlFrontier (
                    job_id, url, depth, priority, resource_kind, status,
                    discovered_from, updated_at
                ) VALUES (?, ?, ?, ?, ?, 'pending', ?, ?)
                ON CONFLICT(job_id, url) DO UPDATE SET
                    priority = MAX(CrawlFrontier.priority, excluded.priority),
                    depth = MIN(CrawlFrontier.depth, excluded.depth),
                    resource_kind = CASE
                        WHEN excluded.resource_kind = 'attachment'
                        THEN 'attachment'
                        ELSE CrawlFrontier.resource_kind
                    END,
                    updated_at = excluded.updated_at
                """,
                (
                    (job_id, url, depth, priority, kind, parent, now)
                    for url, depth, priority, parent, kind
                    in normalized_candidates
                ),
            )

    def claim_frontier(
        self,
        job_id: str,
        limit: int,
        *,
        resource_kinds: Optional[Sequence[str]] = None,
    ) -> list[FrontierItem]:
        if limit < 1:
            return []
        with sqlite3.connect(self.db_path) as connection:
            connection.execute("BEGIN IMMEDIATE")
            kind_filter = ""
            parameters: list[Any] = [job_id]
            if resource_kinds:
                placeholders = ",".join("?" for _ in resource_kinds)
                kind_filter = f" AND resource_kind IN ({placeholders})"
                parameters.extend(resource_kinds)
            parameters.append(limit)
            rows = connection.execute(
                f"""
                SELECT url, depth, priority, attempts, resource_kind
                FROM CrawlFrontier
                WHERE job_id = ? AND status = 'pending'{kind_filter}
                ORDER BY priority DESC, depth ASC, url ASC
                LIMIT ?
                """,
                parameters,
            ).fetchall()
            now = _utc_now()
            connection.executemany(
                """
                UPDATE CrawlFrontier
                SET status = 'running', attempts = attempts + 1,
                    updated_at = ?
                WHERE job_id = ? AND url = ? AND status = 'pending'
                """,
                ((now, job_id, row[0]) for row in rows),
            )
        return [
            FrontierItem(row[0], row[1], row[2], row[3] + 1, row[4])
            for row in rows
        ]

    def mark_frontier(
        self,
        job_id: str,
        url: str,
        status: str,
        error_message: Optional[str] = None,
    ) -> None:
        if status not in {"done", "failed", "skipped"}:
            raise ValueError("invalid terminal frontier status")
        with sqlite3.connect(self.db_path) as connection:
            connection.execute(
                """
                UPDATE CrawlFrontier
                SET status = ?, last_error = ?, updated_at = ?
                WHERE job_id = ? AND url = ?
                """,
                (status, error_message, _utc_now(), job_id, url),
            )

    def release_frontier(self, job_id: str, url: str) -> None:
        """Return claimed work to pending without losing resume state."""
        with sqlite3.connect(self.db_path) as connection:
            connection.execute(
                """
                UPDATE CrawlFrontier
                SET status = 'pending', updated_at = ?
                WHERE job_id = ? AND url = ? AND status = 'running'
                """,
                (_utc_now(), job_id, url),
            )

    def set_frontier_resource_kind(
        self,
        job_id: str,
        url: str,
        resource_kind: str,
    ) -> None:
        if resource_kind not in {"document", "pagination", "attachment"}:
            raise ValueError("invalid frontier resource kind")
        with sqlite3.connect(self.db_path) as connection:
            connection.execute(
                """
                UPDATE CrawlFrontier
                SET resource_kind = ?, updated_at = ?
                WHERE job_id = ? AND url = ? AND status = 'pending'
                """,
                (resource_kind, _utc_now(), job_id, url),
            )

    def frontier_counts(self, job_id: str) -> dict[str, int]:
        with sqlite3.connect(self.db_path) as connection:
            rows = connection.execute(
                """
                SELECT status, COUNT(*)
                FROM CrawlFrontier
                WHERE job_id = ?
                GROUP BY status
                """,
                (job_id,),
            ).fetchall()
        return {row[0]: row[1] for row in rows}

    def frontier_urls(self, job_id: str) -> list[str]:
        with sqlite3.connect(self.db_path) as connection:
            rows = connection.execute(
                "SELECT url FROM CrawlFrontier WHERE job_id = ? ORDER BY url",
                (job_id,),
            ).fetchall()
        return [row[0] for row in rows]

    def pending_frontier_urls(self, job_id: str) -> list[str]:
        with sqlite3.connect(self.db_path) as connection:
            rows = connection.execute(
                """
                SELECT url
                FROM CrawlFrontier
                WHERE job_id = ? AND status = 'pending'
                ORDER BY url
                """,
                (job_id,),
            ).fetchall()
        return [row[0] for row in rows]

    @staticmethod
    def _resolved_artifact_path(
        raw_artifact_uri: str,
        raw_base_dir: Path | str,
    ) -> Optional[Path]:
        try:
            raw_root = Path(raw_base_dir).resolve(strict=False)
            artifact = Path(raw_artifact_uri).resolve(strict=False)
        except (OSError, RuntimeError, ValueError):
            return None
        if artifact == raw_root or raw_root not in artifact.parents:
            return None
        return artifact

    @staticmethod
    def _artifact_sha256(path: Path) -> Optional[str]:
        try:
            if not path.is_file():
                return None
            digest = hashlib.sha256()
            with path.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            return digest.hexdigest()
        except OSError:
            return None

    @staticmethod
    def _clear_cached_artifact(
        connection: sqlite3.Connection,
        url: str,
        raw_artifact_uri: str,
    ) -> None:
        connection.execute(
            """
            UPDATE CrawledResources
            SET etag = NULL, last_modified = NULL, content_hash = NULL,
                raw_artifact_uri = NULL, http_status = NULL,
                fetch_status = 'pending'
            WHERE url = ? AND raw_artifact_uri = ?
            """,
            (url, raw_artifact_uri),
        )

    def delete_job(
        self,
        job_id: str,
        *,
        raw_job_dir: Path | str | None = None,
    ) -> bool:
        """Delete job state and invalidate cache backed by its raw directory."""
        with sqlite3.connect(self.db_path) as connection:
            before = connection.total_changes
            if raw_job_dir is not None:
                raw_job_root = Path(raw_job_dir).resolve(strict=False)
                cached_artifacts = connection.execute(
                    """
                    SELECT url, raw_artifact_uri
                    FROM CrawledResources
                    WHERE raw_artifact_uri IS NOT NULL
                    """
                ).fetchall()
                for url, raw_artifact_uri in cached_artifacts:
                    artifact = self._resolved_artifact_path(
                        raw_artifact_uri,
                        raw_job_root,
                    )
                    if artifact is not None:
                        self._clear_cached_artifact(
                            connection,
                            url,
                            raw_artifact_uri,
                        )
            connection.execute(
                "DELETE FROM ResourceFetchHistory WHERE job_id = ?",
                (job_id,),
            )
            connection.execute(
                "DELETE FROM CrawlFrontier WHERE job_id = ?",
                (job_id,),
            )
            connection.execute(
                "DELETE FROM CrawlJobs WHERE job_id = ?",
                (job_id,),
            )
            return connection.total_changes > before

    def conditional_headers(
        self,
        url: str,
        source_id: Optional[str] = None,
        *,
        raw_base_dir: Path | str | None = None,
    ) -> dict[str, str]:
        with sqlite3.connect(self.db_path) as connection:
            row = connection.execute(
                "SELECT etag, last_modified, source_id, raw_artifact_uri, "
                "content_hash "
                "FROM CrawledResources WHERE url = ?",
                (url,),
            ).fetchone()
            if row is None or (source_id is not None and row[2] != source_id):
                return {}
            raw_artifact_uri = row[3]
            content_hash = row[4]
            artifact = (
                self._resolved_artifact_path(raw_artifact_uri, raw_base_dir)
                if raw_artifact_uri and raw_base_dir is not None
                else None
            )
            if (
                artifact is None
                or not content_hash
                or self._artifact_sha256(artifact) != content_hash
            ):
                if raw_artifact_uri:
                    self._clear_cached_artifact(
                        connection,
                        url,
                        raw_artifact_uri,
                    )
                return {}
            headers: dict[str, str] = {}
            for name, value in (
                ("If-None-Match", row[0]),
                ("If-Modified-Since", row[1]),
            ):
                if value and "\r" not in value and "\n" not in value:
                    headers[name] = value
            return headers

    def cached_resource(
        self,
        url: str,
        source_id: Optional[str] = None,
    ) -> Optional[CachedResource]:
        with sqlite3.connect(self.db_path) as connection:
            row = connection.execute(
                """
                SELECT url, final_url, canonical_document_key, content_hash,
                       mime_type, title, raw_artifact_uri, metadata_json,
                       discovered_links_json, document_identity_strategy,
                       resource_source_namespace, resource_authority_namespace,
                       source_id
                FROM CrawledResources
                WHERE url = ? AND raw_artifact_uri IS NOT NULL
                """,
                (url,),
            ).fetchone()
        if (
            row is None
            or (source_id is not None and row[12] != source_id)
            or not all(row[index] for index in (1, 2, 3, 4, 5, 6, 9, 10))
        ):
            return None
        try:
            metadata = json.loads(row[7] or "{}")
            discovered_links = json.loads(row[8] or "[]")
        except json.JSONDecodeError:
            return None
        if not isinstance(metadata, dict) or not isinstance(discovered_links, list):
            return None
        return CachedResource(
            url=row[0],
            final_url=row[1],
            canonical_document_key=row[2],
            content_hash=row[3],
            mime_type=row[4],
            title=row[5],
            raw_artifact_uri=row[6],
            metadata=metadata,
            discovered_links=[str(value) for value in discovered_links],
            document_identity_strategy=row[9],
            source_namespace=row[10],
            authority_namespace=row[11],
        )

    def record_fetch(
        self,
        *,
        job_id: str,
        source_id: str,
        url: str,
        fetch_status: str,
        http_status: int,
        content_hash: Optional[str] = None,
        canonical_document_key: Optional[str] = None,
        bytes_downloaded: int = 0,
        error_message: Optional[str] = None,
        etag: Optional[str] = None,
        last_modified: Optional[str] = None,
        final_url: Optional[str] = None,
        mime_type: Optional[str] = None,
        title: Optional[str] = None,
        raw_artifact_uri: Optional[str] = None,
        metadata: Optional[Mapping[str, Any]] = None,
        discovered_links: Optional[Sequence[str]] = None,
        document_identity_strategy: Optional[str] = None,
        resource_source_namespace: Optional[str] = None,
        resource_authority_namespace: Optional[str] = None,
        execution_time_ms: Optional[int] = None,
    ) -> None:
        resource_id = hashlib.sha256(url.encode("utf-8")).hexdigest()
        now = _utc_now()
        with sqlite3.connect(self.db_path) as connection:
            connection.execute(
                """
                INSERT INTO CrawledResources (
                    resource_id, source_id, url, canonical_document_key,
                    etag, last_modified, content_hash, final_url, mime_type,
                    title, raw_artifact_uri, metadata_json,
                    discovered_links_json, document_identity_strategy,
                    resource_source_namespace, resource_authority_namespace,
                    last_fetched_at, http_status, fetch_status, error_count
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ON CONFLICT(url) DO UPDATE SET
                    source_id = excluded.source_id,
                    canonical_document_key = COALESCE(excluded.canonical_document_key, CrawledResources.canonical_document_key),
                    etag = COALESCE(excluded.etag, CrawledResources.etag),
                    last_modified = COALESCE(excluded.last_modified, CrawledResources.last_modified),
                    content_hash = COALESCE(excluded.content_hash, CrawledResources.content_hash),
                    final_url = COALESCE(excluded.final_url, CrawledResources.final_url),
                    mime_type = COALESCE(excluded.mime_type, CrawledResources.mime_type),
                    title = COALESCE(excluded.title, CrawledResources.title),
                    raw_artifact_uri = COALESCE(excluded.raw_artifact_uri, CrawledResources.raw_artifact_uri),
                    metadata_json = COALESCE(excluded.metadata_json, CrawledResources.metadata_json),
                    discovered_links_json = COALESCE(excluded.discovered_links_json, CrawledResources.discovered_links_json),
                    document_identity_strategy = COALESCE(excluded.document_identity_strategy, CrawledResources.document_identity_strategy),
                    resource_source_namespace = COALESCE(excluded.resource_source_namespace, CrawledResources.resource_source_namespace),
                    resource_authority_namespace = COALESCE(excluded.resource_authority_namespace, CrawledResources.resource_authority_namespace),
                    last_fetched_at = excluded.last_fetched_at,
                    http_status = excluded.http_status,
                    fetch_status = excluded.fetch_status,
                    error_count = CASE
                        WHEN excluded.fetch_status = 'failed' THEN CrawledResources.error_count + 1
                        ELSE CrawledResources.error_count
                    END
                """,
                (
                    resource_id, source_id, url, canonical_document_key,
                    etag, last_modified, content_hash, final_url, mime_type,
                    title, raw_artifact_uri,
                    json.dumps(metadata, ensure_ascii=False) if metadata is not None else None,
                    json.dumps(discovered_links, ensure_ascii=False) if discovered_links is not None else None,
                    document_identity_strategy, resource_source_namespace,
                    resource_authority_namespace, now, http_status,
                    fetch_status, 1 if fetch_status == "failed" else 0,
                ),
            )
            stored_resource_id = connection.execute(
                "SELECT resource_id FROM CrawledResources WHERE url = ?",
                (url,),
            ).fetchone()[0]
            connection.execute(
                """
                INSERT INTO ResourceFetchHistory (
                    resource_id, job_id, fetched_at, http_status, content_hash,
                    bytes_downloaded, execution_time_ms, error_message
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    stored_resource_id, job_id, now, http_status, content_hash,
                    bytes_downloaded, execution_time_ms, error_message,
                ),
            )

    def finish_job(
        self,
        *,
        job_id: str,
        status: str,
        discovered: int,
        crawled: int,
        failed: int,
        staging_directory: Optional[str],
    ) -> None:
        with sqlite3.connect(self.db_path) as connection:
            connection.execute(
                """
                UPDATE CrawlJobs
                SET status = ?, completed_at = ?, resources_discovered = ?,
                    resources_crawled = ?, resources_failed = ?,
                    staging_directory = ?
                WHERE job_id = ?
                """,
                (
                    status, _utc_now(), discovered, crawled, failed,
                    staging_directory, job_id,
                ),
            )
