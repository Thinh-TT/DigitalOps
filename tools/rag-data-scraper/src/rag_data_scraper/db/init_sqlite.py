import sqlite3
from pathlib import Path
import logging

logger = logging.getLogger(__name__)

SQLITE_SCHEMA = """
-- Crawler SQLite Local State Schema
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Sources (
    source_id TEXT PRIMARY KEY,
    source_name TEXT NOT NULL,
    source_namespace TEXT NOT NULL,
    authority_namespace TEXT,
    identity_strategy TEXT NOT NULL CHECK(identity_strategy IN ('authoritative', 'canonical_metadata', 'source_scoped', 'content_only')),
    base_url TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS CrawledResources (
    resource_id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL,
    url TEXT NOT NULL UNIQUE,
    canonical_document_key TEXT,
    etag TEXT,
    last_modified TEXT,
    content_hash TEXT,
    final_url TEXT,
    mime_type TEXT,
    title TEXT,
    raw_artifact_uri TEXT,
    metadata_json TEXT,
    discovered_links_json TEXT,
    document_identity_strategy TEXT,
    resource_source_namespace TEXT,
    resource_authority_namespace TEXT,
    last_fetched_at TEXT,
    http_status INTEGER,
    fetch_status TEXT CHECK(fetch_status IN ('pending', 'fetched', 'failed', 'skipped')),
    error_count INTEGER DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (source_id) REFERENCES Sources(source_id)
);

CREATE TABLE IF NOT EXISTS ResourceFetchHistory (
    history_id INTEGER PRIMARY KEY AUTOINCREMENT,
    resource_id TEXT NOT NULL,
    job_id TEXT NOT NULL,
    fetched_at TEXT NOT NULL DEFAULT (datetime('now')),
    http_status INTEGER NOT NULL,
    content_hash TEXT,
    bytes_downloaded INTEGER,
    execution_time_ms INTEGER,
    error_message TEXT,
    FOREIGN KEY (resource_id) REFERENCES CrawledResources(resource_id)
);

CREATE TABLE IF NOT EXISTS CrawlJobs (
    job_id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('running', 'completed', 'failed', 'cancelled')),
    started_at TEXT NOT NULL DEFAULT (datetime('now')),
    completed_at TEXT,
    resources_discovered INTEGER DEFAULT 0,
    resources_crawled INTEGER DEFAULT 0,
    resources_failed INTEGER DEFAULT 0,
    staging_directory TEXT,
    FOREIGN KEY (source_id) REFERENCES Sources(source_id)
);

CREATE TABLE IF NOT EXISTS CrawlFrontier (
    job_id TEXT NOT NULL,
    url TEXT NOT NULL,
    depth INTEGER NOT NULL CHECK(depth >= 0),
    priority INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL CHECK(status IN ('pending', 'running', 'done', 'failed', 'skipped')),
    discovered_from TEXT,
    attempts INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (job_id, url),
    FOREIGN KEY (job_id) REFERENCES CrawlJobs(job_id)
);

CREATE INDEX IF NOT EXISTS idx_resources_source ON CrawledResources(source_id);
CREATE INDEX IF NOT EXISTS idx_resources_url ON CrawledResources(url);
CREATE INDEX IF NOT EXISTS idx_resources_hash ON CrawledResources(content_hash);
CREATE INDEX IF NOT EXISTS idx_history_resource ON ResourceFetchHistory(resource_id);
CREATE INDEX IF NOT EXISTS idx_history_job ON ResourceFetchHistory(job_id);
CREATE INDEX IF NOT EXISTS idx_frontier_claim ON CrawlFrontier(job_id, status, priority DESC, depth, url);
"""


_RESOURCE_COLUMNS = {
    "final_url": "TEXT",
    "mime_type": "TEXT",
    "title": "TEXT",
    "raw_artifact_uri": "TEXT",
    "metadata_json": "TEXT",
    "discovered_links_json": "TEXT",
    "document_identity_strategy": "TEXT",
    "resource_source_namespace": "TEXT",
    "resource_authority_namespace": "TEXT",
}


def _upgrade_existing_schema(connection: sqlite3.Connection) -> None:
    existing = {
        row[1]
        for row in connection.execute("PRAGMA table_info(CrawledResources)")
    }
    for name, data_type in _RESOURCE_COLUMNS.items():
        if name not in existing:
            connection.execute(
                f'ALTER TABLE CrawledResources ADD COLUMN "{name}" {data_type}'
            )

def init_sqlite_db(db_path: Path | str) -> None:
    db_path = Path(db_path)
    db_path.parent.mkdir(parents=True, exist_ok=True)
    
    with sqlite3.connect(db_path) as conn:
        conn.executescript(SQLITE_SCHEMA)
        _upgrade_existing_schema(conn)
        conn.commit()
    logger.info(f"SQLite crawler state DB initialized at {db_path}")

if __name__ == "__main__":
    init_sqlite_db("storage/state/crawler.db")
