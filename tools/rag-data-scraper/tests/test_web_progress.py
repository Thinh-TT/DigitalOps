import json
import sqlite3
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from rag_data_scraper.db.state_store import CrawlerStateStore
from rag_data_scraper.web import app as web_app


def _create_progress_state(db_path: Path, job_id: str) -> CrawlerStateStore:
    store = CrawlerStateStore(db_path)
    store.start_job(
        job_id=job_id,
        source_id="test",
        source_namespace="example.gov.vn",
        authority_namespace="gov.vn",
        identity_strategy="authoritative",
        base_url="https://example.gov.vn/list",
    )
    store.prepare_frontier(
        job_id,
        [
            ("https://example.gov.vn/list?page=1", 100, "pagination"),
            ("https://example.gov.vn/list?page=2", 99, "pagination"),
            ("https://example.gov.vn/a.pdf", 50, "attachment"),
            ("https://example.gov.vn/b.pdf", 49, "attachment"),
            ("https://example.gov.vn/c.pdf", 48, "attachment"),
            ("https://example.gov.vn/d.pdf", 47, "attachment"),
            ("https://example.gov.vn/e.pdf", 46, "attachment"),
        ],
    )
    with sqlite3.connect(db_path) as connection:
        statuses = {
            "https://example.gov.vn/list?page=1": "done",
            "https://example.gov.vn/list?page=2": "pending",
            "https://example.gov.vn/a.pdf": "done",
            "https://example.gov.vn/b.pdf": "running",
            "https://example.gov.vn/c.pdf": "pending",
            "https://example.gov.vn/d.pdf": "failed",
            "https://example.gov.vn/e.pdf": "skipped",
        }
        connection.executemany(
            "UPDATE CrawlFrontier SET status = ? WHERE job_id = ? AND url = ?",
            ((status, job_id, url) for url, status in statuses.items()),
        )
    return store


def test_frontier_progress_groups_live_pipeline_counts(tmp_path: Path) -> None:
    store = _create_progress_state(
        tmp_path / "state" / "crawler.db",
        "JOB_PROGRESS",
    )

    assert store.frontier_progress("JOB_PROGRESS") == {
        "listing_pages_scanned": 1,
        "listing_pages_total": 2,
        "listing_pages_pending": 1,
        "attachments_discovered": 5,
        "attachments_fetched": 1,
        "attachments_pending": 1,
        "attachments_running": 1,
        "attachment_failures": 1,
        "attachments_skipped": 1,
        "http_resources_terminal": 4,
        "http_resources_total": 7,
    }


def test_jobs_api_exposes_live_frontier_progress(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    job_id = "JOB_PROGRESS"
    state_db = tmp_path / "state" / "crawler.db"
    _create_progress_state(state_db, job_id)
    with sqlite3.connect(state_db) as connection:
        connection.execute(
            "UPDATE CrawlFrontier SET status = 'done' "
            "WHERE job_id = ? AND url = ?",
            (job_id, "https://example.gov.vn/list?page=2"),
        )
    staging_job = tmp_path / "staging" / job_id
    staging_job.mkdir(parents=True)
    (staging_job / "manifest.json").write_text(
        json.dumps(
            {
                "total_observations": 39,
                "exported_at": "2026-08-03T20:00:00+00:00",
            }
        ),
        encoding="utf-8",
    )
    (staging_job / "preview.html").write_text("old", encoding="utf-8")
    monkeypatch.setattr(web_app, "STATE_DB", state_db)
    monkeypatch.setattr(web_app, "STAGING_DIR", tmp_path / "staging")
    web_app.JOB_STATUS_MAP.clear()
    web_app.JOB_STATUS_MAP[job_id] = {
        "job_id": job_id,
        "source_adapter": "generic_web",
        "status": "RUNNING",
        "crawled_count": 12,
        "limit_count": 100,
        "max_pagination_pages": 40,
        "download_attachments": True,
        "created_at": "2026-08-03 23:12:32",
        "crawl_metrics": {},
        "export_status": "PENDING",
    }

    try:
        with TestClient(web_app.app) as client:
            response = client.get("/api/jobs")
    finally:
        web_app.JOB_STATUS_MAP.clear()

    assert response.status_code == 200
    job = response.json()["jobs"][0]
    assert job["crawl_phase"] == "attachments"
    assert job["crawled_count"] == 12
    assert job["observations_count"] == 12
    assert job["has_preview"] is False
    assert job["created_at"] == "2026-08-03 23:12:32"
    assert job["max_pagination_pages"] == 40
    assert job["crawl_metrics"]["primary_documents_created"] == 12
    assert job["crawl_metrics"]["listing_pages_scanned"] == 2
    assert job["crawl_metrics"]["attachments_fetched"] == 1
    assert job["crawl_metrics"]["attachments_pending"] == 1
    assert job["crawl_metrics"]["attachments_running"] == 1


def test_dashboard_renders_phase_oriented_job_progress() -> None:
    dashboard = (
        Path(web_app.__file__).parent / "static" / "index.html"
    ).read_text(encoding="utf-8")

    assert 'id="jobsList"' in dashboard
    assert "function renderJob(job)" in dashboard
    assert "Tiến độ xử lý tệp" in dashboard
    assert "Cập nhật mỗi 4 giây" in dashboard
    assert "jobsTableBody" not in dashboard
