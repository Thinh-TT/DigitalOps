import asyncio
import json
import logging
import shutil
from pathlib import Path
from typing import List, Dict, Any, Literal
from urllib.parse import urlsplit
from datetime import datetime, timezone

from fastapi import FastAPI, BackgroundTasks, HTTPException
from fastapi.responses import HTMLResponse, FileResponse
from pydantic import BaseModel, Field, field_validator, model_validator

from starlette.background import BackgroundTask
from ..adapters.gov_portal import GovPortalAdapter
from ..adapters.legal_aggregator import LegalAggregatorAdapter
from ..adapters.generic import GenericWebAdapter
from ..crawler.engine import CrawlEngine
from ..crawler.policy import CrawlPolicy
from ..crawler.url_probe import UrlProbeService
from ..chunkers.structure_chunker import StructureChunker
from ..db.state_store import CrawlerStateStore

from ..exporters.rag_exporter import (
    ExportDependencyUnavailableError,
    ExportTooLargeError,
    InvalidStagingPackageError,
    RagExportFormat,
    RagExportService,
)
from ..paths import resolve_job_dir, validate_job_id
from ..config import Settings
from ..source_registry import SourceRegistry
logger = logging.getLogger(__name__)

app = FastAPI(
    title="DigitalOps RAG Data Scraper Web API",
    description="REST API & Dashboard for Multi-source RAG Data Scraper",
    version="1.0.0"
)

STATIC_DIR = Path(__file__).parent / "static"

@app.middleware("http")
async def add_security_headers(request, call_next):
    response = await call_next(request)
    response.headers["Content-Security-Policy"] = (
        "default-src 'self'; img-src 'self' data:; "
        "style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; "
        "connect-src 'self'; object-src 'none'; base-uri 'none'; "
        "frame-ancestors 'self'; form-action 'self'"
    )
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["X-Frame-Options"] = "SAMEORIGIN"
    response.headers["Referrer-Policy"] = "no-referrer"
    response.headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()"
    return response

STAGING_DIR = Path("storage/staging")
STATE_DB = Path("storage/state/crawler.db")
SETTINGS_FILE = Path("config/settings.yaml")
JOB_METADATA_FILE = "job-metadata.json"

def get_job_dir(job_id: str) -> Path:
    try:
        return resolve_job_dir(STAGING_DIR, job_id)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


# In-memory status map for tracking active jobs
JOB_STATUS_MAP: Dict[str, Dict[str, Any]] = {}
URL_PROBE_SEMAPHORE = asyncio.Semaphore(2)


def _live_crawl_metrics(
    job_id: str,
    status: str,
    base_metrics: Dict[str, Any],
    primary_documents: int,
) -> Dict[str, Any]:
    """Merge persisted metrics with a current durable-frontier snapshot."""
    metrics = dict(base_metrics)
    metrics["primary_documents_created"] = primary_documents
    if status != "RUNNING" or not STATE_DB.is_file():
        return metrics

    try:
        metrics.update(CrawlerStateStore(STATE_DB).frontier_progress(job_id))
    except Exception:
        logger.warning(
            "Unable to read live crawler progress for job %s",
            job_id,
            exc_info=True,
        )
    return metrics


def _crawl_phase(
    status: str,
    export_status: Any,
    metrics: Dict[str, Any],
) -> str:
    if status == "FAILED":
        return "failed"
    if status == "COMPLETED":
        return "exporting" if export_status == "BUILDING" else "completed"
    if status != "RUNNING":
        return "pending"

    if (
        int(metrics.get("listing_pages_pending", 0)) > 0
        or int(metrics.get("primary_documents_created", 0)) == 0
    ):
        return "discovery"
    attachments_active = int(metrics.get("attachments_pending", 0)) + int(
        metrics.get("attachments_running", 0)
    )
    if attachments_active > 0:
        return "attachments"
    return "finalizing"


def get_export_service(job_id: str) -> RagExportService:
    job_dir = get_job_dir(job_id)
    safe_job_id = job_dir.name
    if JOB_STATUS_MAP.get(safe_job_id, {}).get("status") == "RUNNING":
        raise HTTPException(
            status_code=409,
            detail="The staging package is still being generated.",
        )
    required = ("manifest.json", "chunks.jsonl")
    if not job_dir.is_dir():
        raise HTTPException(status_code=404, detail="Staging job not found.")
    if not all((job_dir / name).is_file() for name in required):
        raise HTTPException(
            status_code=409,
            detail="The staging package is not ready for export.",
        )
    return RagExportService(job_dir)


class CreateJobRequest(BaseModel):
    job_id: str = Field(..., min_length=1, max_length=64, pattern=r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")
    source: Literal["gov_portal", "legal_aggregator", "generic_web"]
    urls: List[str] = Field(..., min_length=1, max_length=100)
    limit: int = Field(
        default=50,
        ge=1,
        le=10000,
        description="Maximum number of primary documents emitted",
    )
    max_pagination_pages: int = Field(
        default=25,
        ge=1,
        le=500,
        description="Maximum listing/pagination URLs followed",
    )
    download_attachments: bool = Field(
        default=True,
        description="Whether to extract PDF/DOCX/legacy DOC attachments",
    )
    export_format: RagExportFormat = Field(
        default=RagExportFormat.CHUNKS_JSONL,
        description="Artifact to build automatically after the crawl completes",
    )

    @field_validator("urls")
    @classmethod
    def validate_urls(cls, urls: List[str]) -> List[str]:
        normalized = list(dict.fromkeys(url.strip() for url in urls))
        for url in normalized:
            parsed = urlsplit(url)
            if parsed.scheme.lower() != "https":
                raise ValueError("seed URLs must use HTTPS")
            if not parsed.hostname or parsed.username or parsed.password:
                raise ValueError(
                    "seed URLs require a host and cannot contain credentials")
        return normalized

    @model_validator(mode="after")
    def validate_source_scope(self):
        approved_hosts = {
            "gov_portal": {
                "vanban.chinhphu.vn",
                "www.vanban.chinhphu.vn",
            },
            "legal_aggregator": {
                "thuvienphapluat.vn",
                "www.thuvienphapluat.vn",
            },
        }
        allowed = approved_hosts.get(self.source)
        if allowed is not None:
            invalid = {
                urlsplit(url).hostname
                for url in self.urls
                if urlsplit(url).hostname not in allowed
            }
            if invalid:
                raise ValueError(
                    f"seed host is outside {self.source} scope")
        return self


class UrlProbeRequest(BaseModel):
    source: Literal["gov_portal", "legal_aggregator", "generic_web"]
    urls: List[str] = Field(..., min_length=1, max_length=10)
    max_pagination_pages: int = Field(
        default=25,
        ge=1,
        le=100,
        description=(
            "Maximum pagination URLs inspected in addition to seed URLs"
        ),
    )

    @field_validator("urls")
    @classmethod
    def validate_urls(cls, urls: List[str]) -> List[str]:
        normalized = list(dict.fromkeys(url.strip() for url in urls))
        for url in normalized:
            if len(url) > 2048 or any(ord(character) < 32 for character in url):
                raise ValueError(
                    "probe URLs must be at most 2048 characters and contain no control characters"
                )
            parsed = urlsplit(url)
            if parsed.scheme.lower() != "https":
                raise ValueError("probe URLs must use HTTPS")
            if not parsed.hostname or parsed.username or parsed.password:
                raise ValueError(
                    "probe URLs require a host and cannot contain credentials"
                )
        return normalized

    @model_validator(mode="after")
    def validate_source_scope(self):
        approved_hosts = {
            "gov_portal": {
                "vanban.chinhphu.vn",
                "www.vanban.chinhphu.vn",
            },
            "legal_aggregator": {
                "thuvienphapluat.vn",
                "www.thuvienphapluat.vn",
            },
        }
        allowed = approved_hosts.get(self.source)
        if allowed is not None and any(
            urlsplit(url).hostname not in allowed for url in self.urls
        ):
            raise ValueError(f"probe host is outside {self.source} scope")
        return self


class UrlProbeIssueResponse(BaseModel):
    code: str
    url: str
    message: str


class UrlProbeResponse(BaseModel):
    status: Literal["COMPLETE", "PARTIAL"]
    count_mode: Literal[
        "EXACT_LISTING_RECORDS",
        "ESTIMATED_LINKS",
        "MIXED",
    ]
    seed_count: int
    pages_scanned: int
    listing_pages_scanned: int
    listing_pages_detected: int
    pagination_pages_detected: int
    pagination_pages_followed: int
    max_pagination_pages: int
    documents_detected: int
    attachments_detected: int
    pagination_limit_reached: bool
    duration_ms: int
    sample_titles: List[str]
    issues: List[UrlProbeIssueResponse]


def get_adapter(source: str, urls: List[str], settings: Settings | None = None):
    settings = settings or Settings.load_from_yaml(SETTINGS_FILE)
    registry = SourceRegistry.load(settings.governance.source_registry_path)
    profile = registry.resolve(source, urls)
    crawler = settings.crawler
    common = {
        "user_agent": crawler.user_agent,
        "timeout_seconds": crawler.request_timeout_seconds,
        "max_response_bytes": crawler.max_response_bytes,
        "max_attempts": crawler.retry_attempts,
        "backoff_base_seconds": crawler.retry_backoff_base_seconds,
        "max_backoff_seconds": crawler.retry_max_backoff_seconds,
        "per_host_delay_seconds": crawler.per_host_delay_seconds,
        "per_host_max_concurrent": crawler.per_host_max_concurrent,
    }
    s = source.lower()
    if s in ["gov_portal", "vanban_chinhphu"]:
        if profile is None:
            raise ValueError("seed URL is not registered for gov_portal")
        adapter = GovPortalAdapter(
            source_id=profile.source_id,
            source_namespace=profile.source_namespace,
            authority_namespace=profile.authority_namespace,
            **common,
        )
    elif s in ["legal_aggregator", "thuvienphapluat"]:
        if profile is None:
            raise ValueError(
                "seed URL is not registered for legal_aggregator"
            )
        adapter = LegalAggregatorAdapter(
            source_id=profile.source_id,
            source_namespace=profile.source_namespace,
            authority_namespace=profile.authority_namespace,
            **common,
        )
    else:
        allowed_hosts = profile.allowed_hosts if profile is not None else {
            parsed.hostname
            for raw_url in urls
            if (parsed := urlsplit(raw_url)).hostname
        }
        adapter = GenericWebAdapter(
            source_id=profile.source_id if profile else "generic_web",
            source_namespace=(
                profile.source_namespace if profile else "custom.web"
            ),
            authority_namespace=(profile.authority_namespace if profile else None),
            allowed_hosts=allowed_hosts,
            **common,
        )
    adapter.attach_source_profile(profile)
    return adapter

def _write_job_metadata(job_dir: Path, payload: Dict[str, Any]) -> None:
    path = job_dir / JOB_METADATA_FILE
    temporary = path.with_suffix(".tmp")
    temporary.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2),
        encoding="utf-8",
        newline="\n",
    )
    temporary.replace(path)


async def execute_crawl_job(
    job_id: str,
    source: str,
    urls: List[str],
    limit: int,
    export_format: RagExportFormat = RagExportFormat.CHUNKS_JSONL,
    download_attachments: bool = True,
    max_pagination_pages: int = 25,
):
    logger.info(f"Starting background crawl job {job_id} for source {source}")
    JOB_STATUS_MAP[job_id] = {
        "job_id": job_id,
        "source_adapter": source,
        "status": "RUNNING",
        "crawled_count": 0,
        "limit_count": limit,
        "max_pagination_pages": max_pagination_pages,
        "download_attachments": download_attachments,
        "created_at": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "preferred_export_format": export_format.value,
        "preferred_export_ready": False,
        "export_status": "PENDING",
        "crawl_metrics": {},
    }

    try:
        settings = Settings.load_from_yaml(SETTINGS_FILE)
        JOB_STATUS_MAP[job_id]["http_fetch_limit"] = (
            settings.crawler.max_total_resources
        )
        adapter = get_adapter(source, urls, settings)
        engine = CrawlEngine(
            adapter=adapter,
            state_db_path=STATE_DB,
            staging_dir=STAGING_DIR,
            raw_dir=settings.storage.raw_base_dir,
            max_concurrent=settings.crawler.max_concurrent_requests,
            max_total_resources=settings.crawler.max_total_resources,
            max_pagination_pages=max_pagination_pages,
            chunker=StructureChunker(
                target_tokens=settings.chunker.target_tokens,
                soft_max_tokens=settings.chunker.soft_max_tokens,
                overlap_tokens=settings.chunker.overlap_tokens,
                max_tokens=settings.chunker.max_tokens,
                tokenizer_name=settings.chunker.tokenizer_model,
            ),
            crawl_policy=CrawlPolicy(
                include_attachments=download_attachments,
            ),
            ocr_tesseract_cmd=settings.ocr.tesseract_cmd,
            ocr_lang=settings.ocr.lang,
            ocr_min_confidence=settings.ocr.min_confidence,
            ocr_tessdata_dir=settings.ocr.tessdata_dir,
            max_ocr_pages=settings.ocr.max_pages,
            max_ocr_image_pixels=settings.ocr.max_image_pixels,
            ocr_page_timeout_seconds=settings.ocr.page_timeout_seconds,
            legacy_doc_soffice_cmd=settings.legacy_doc.soffice_cmd,
            legacy_doc_timeout_seconds=settings.legacy_doc.timeout_seconds,
            legacy_doc_max_output_bytes=settings.legacy_doc.max_output_bytes,
        )
        
        output_dir = await engine.run_job(
            job_id=job_id,
            seed_urls=urls,
            max_depth=settings.crawler.max_depth,
            max_resources=limit,
            progress_callback=lambda count: JOB_STATUS_MAP[job_id].update(
                crawled_count=count
            ),
        )

        JOB_STATUS_MAP[job_id]["crawl_metrics"] = dict(
            engine.last_run_metrics
        )
        JOB_STATUS_MAP[job_id]["crawled_count"] = int(
            engine.last_run_metrics.get("primary_documents_created", 0)
        )
        JOB_STATUS_MAP[job_id]["status"] = "COMPLETED"
        JOB_STATUS_MAP[job_id]["export_status"] = "BUILDING"
        try:
            await asyncio.to_thread(
                RagExportService(output_dir).build_persistent,
                export_format,
            )
            JOB_STATUS_MAP[job_id]["preferred_export_ready"] = True
            JOB_STATUS_MAP[job_id]["export_status"] = "READY"
        except Exception:
            # The crawl package remains valid even when an optional rich-format
            # writer is unavailable. Users can retry the export from the table.
            JOB_STATUS_MAP[job_id]["export_status"] = "FAILED"
            logger.error(
                "Automatic export failed for job %s (%s)",
                job_id,
                export_format.value,
                exc_info=True,
            )
        try:
            await asyncio.to_thread(
                _write_job_metadata,
                output_dir,
                {
                    "job_id": job_id,
                    "source_adapter": source,
                    "download_attachments": download_attachments,
                    "max_pagination_pages": max_pagination_pages,
                    "document_limit": limit,
                    "crawl_metrics": engine.last_run_metrics,
                    "preferred_export_format": export_format.value,
                    "preferred_export_ready": JOB_STATUS_MAP[job_id][
                        "preferred_export_ready"
                    ],
                    "export_status": JOB_STATUS_MAP[job_id]["export_status"],
                    "completed_at": datetime.now(timezone.utc).isoformat(),
                },
            )
        except OSError:
            logger.warning("Could not persist metadata for job %s", job_id, exc_info=True)
        logger.info(f"Background crawl job {job_id} COMPLETED successfully.")
    except Exception as e:
        logger.error(f"Background crawl job {job_id} FAILED: {e}", exc_info=True)
        JOB_STATUS_MAP[job_id]["status"] = "FAILED"
        JOB_STATUS_MAP[job_id]["error"] = str(e)

@app.get("/", response_class=HTMLResponse)
async def serve_dashboard():
    index_file = STATIC_DIR / "index.html"
    if not index_file.exists():
        raise HTTPException(status_code=404, detail="Dashboard UI index.html not found")
    return HTMLResponse(content=index_file.read_text(encoding="utf-8"))


@app.post("/api/url-probes", response_model=UrlProbeResponse)
async def create_url_probe(req: UrlProbeRequest):
    """Inspect listing/pagination capacity without creating crawl state."""
    adapter = None
    try:
        settings = Settings.load_from_yaml(SETTINGS_FILE)
        adapter = get_adapter(req.source, req.urls, settings)
        service = UrlProbeService(
            adapter,
            max_pagination_pages=req.max_pagination_pages,
        )
        async with URL_PROBE_SEMAPHORE:
            async with asyncio.timeout(120):
                summary = await service.run(req.urls)
        return UrlProbeResponse.model_validate(summary.to_dict())
    except ValueError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    except TimeoutError as exc:
        raise HTTPException(
            status_code=504,
            detail="URL inspection exceeded the 120 second limit.",
        ) from exc
    except Exception as exc:
        logger.error("URL inspection failed", exc_info=True)
        raise HTTPException(
            status_code=502,
            detail="The URL could not be inspected safely.",
        ) from exc
    finally:
        if adapter is not None:
            try:
                await adapter.aclose()
            except Exception:
                logger.warning(
                    "Unable to close URL inspection adapter",
                    exc_info=True,
                )


@app.get("/api/jobs")
async def list_jobs():
    jobs_list = []
    
    # 1. Read from staging directory for historical completed jobs
    if STAGING_DIR.exists():
        for job_folder in STAGING_DIR.iterdir():
            if job_folder.is_dir():
                job_id = job_folder.name
                try:
                    validate_job_id(job_id)
                except ValueError:
                    continue
                manifest_file = job_folder / "manifest.json"
                preview_file = job_folder / "preview.html"
                metadata_file = job_folder / JOB_METADATA_FILE
                
                crawled_count = 0
                created_at = "Unknown"
                if manifest_file.exists():
                    try:
                        manifest_data = json.loads(manifest_file.read_text(encoding="utf-8"))
                        crawled_count = manifest_data.get("total_observations", 0)
                        created_at = manifest_data.get("exported_at", "")[:19].replace("T", " ")
                    except Exception:
                        pass

                persisted_metadata: Dict[str, Any] = {}
                if metadata_file.is_file():
                    try:
                        value = json.loads(metadata_file.read_text(encoding="utf-8"))
                        if isinstance(value, dict):
                            persisted_metadata = value
                    except (OSError, json.JSONDecodeError):
                        logger.warning(
                            "Ignoring invalid metadata for job %s",
                            job_id,
                            exc_info=True,
                        )
                persisted_metrics = persisted_metadata.get(
                    "crawl_metrics",
                    {},
                )
                if not isinstance(persisted_metrics, dict):
                    persisted_metrics = {}
                
                # Check memory status first, fallback to completed if preview exists
                mem_status = JOB_STATUS_MAP.get(job_id, {})
                status = mem_status.get("status", "COMPLETED" if preview_file.exists() else "UNKNOWN")
                source = mem_status.get(
                    "source_adapter",
                    persisted_metadata.get("source_adapter", "custom"),
                )
                preferred_format = mem_status.get(
                    "preferred_export_format",
                    persisted_metadata.get("preferred_export_format"),
                )
                preferred_ready = bool(
                    mem_status.get(
                        "preferred_export_ready",
                        persisted_metadata.get("preferred_export_ready", False),
                    )
                )
                can_export = (
                    status != "RUNNING"
                    and manifest_file.is_file()
                    and (job_folder / "chunks.jsonl").is_file()
                )
                crawl_metrics = mem_status.get(
                    "crawl_metrics",
                    persisted_metrics,
                )
                if not isinstance(crawl_metrics, dict):
                    crawl_metrics = {}
                primary_documents = int(
                    mem_status.get(
                        "crawled_count",
                        crawl_metrics.get(
                            "primary_documents_created",
                            crawled_count,
                        ),
                    )
                )
                crawl_metrics = _live_crawl_metrics(
                    job_id,
                    status,
                    crawl_metrics,
                    primary_documents,
                )
                export_status = mem_status.get(
                    "export_status",
                    persisted_metadata.get("export_status"),
                )
                displayed_created_at = mem_status.get(
                    "created_at",
                    created_at,
                )

                jobs_list.append({
                    "job_id": job_id,
                    "source_adapter": source,
                    "status": status,
                    "crawled_count": primary_documents,
                    "observations_count": int(
                        crawl_metrics.get(
                            "observations_created",
                            (
                                primary_documents
                                if status == "RUNNING"
                                else crawled_count
                            ),
                        )
                    ),
                    "limit_count": mem_status.get(
                        "limit_count",
                        persisted_metadata.get(
                            "document_limit",
                            primary_documents,
                        ),
                    ),
                    "crawl_metrics": crawl_metrics,
                    "crawl_phase": _crawl_phase(
                        status,
                        export_status,
                        crawl_metrics,
                    ),
                    "max_pagination_pages": mem_status.get(
                        "max_pagination_pages",
                        persisted_metadata.get("max_pagination_pages"),
                    ),
                    "download_attachments": mem_status.get(
                        "download_attachments",
                        persisted_metadata.get("download_attachments", True),
                    ),
                    "created_at": displayed_created_at,
                    "has_preview": (
                        status != "RUNNING" and preview_file.exists()
                    ),
                    "preferred_export_format": preferred_format,
                    "preferred_export_ready": preferred_ready,
                    "export_status": export_status,
                    "error": mem_status.get("error"),
                    "export_formats": [
                        item["format_id"]
                        for item in RagExportService.descriptors()
                    ] if can_export else [],
                })
    
    # Also append active running jobs in memory not yet in staging directory
    for jid, jinfo in JOB_STATUS_MAP.items():
        if not any(j["job_id"] == jid for j in jobs_list):
            status = jinfo.get("status", "RUNNING")
            primary_documents = int(jinfo.get("crawled_count", 0))
            base_metrics = jinfo.get("crawl_metrics", {})
            if not isinstance(base_metrics, dict):
                base_metrics = {}
            crawl_metrics = _live_crawl_metrics(
                jid,
                status,
                base_metrics,
                primary_documents,
            )
            export_status = jinfo.get("export_status")
            jobs_list.append({
                "job_id": jid,
                "source_adapter": jinfo.get("source_adapter", "custom"),
                "status": status,
                "crawled_count": primary_documents,
                "limit_count": jinfo.get("limit_count", 50),
                "observations_count": int(
                    crawl_metrics.get(
                        "observations_created",
                        primary_documents,
                    )
                ),
                "crawl_metrics": crawl_metrics,
                "crawl_phase": _crawl_phase(
                    status,
                    export_status,
                    crawl_metrics,
                ),
                "max_pagination_pages": jinfo.get("max_pagination_pages"),
                "download_attachments": jinfo.get(
                    "download_attachments",
                    True,
                ),
                "created_at": jinfo.get("created_at", ""),
                "has_preview": False,
                "preferred_export_format": jinfo.get("preferred_export_format"),
                "preferred_export_ready": False,
                "export_status": export_status,
                "error": jinfo.get("error"),
                "export_formats": [],
            })

    # Sort most recent first
    jobs_list.sort(key=lambda x: x.get("created_at", ""), reverse=True)
    return {"jobs": jobs_list}

@app.post("/api/jobs")
async def create_job(req: CreateJobRequest, background_tasks: BackgroundTasks):
    job_id = validate_job_id(req.job_id)
    if not job_id:
        raise HTTPException(status_code=400, detail="job_id cannot be empty")

    if JOB_STATUS_MAP.get(job_id, {}).get("status") == "RUNNING":
        raise HTTPException(status_code=400, detail=f"Job {job_id} is already running.")

    # Schedule async background task
    if get_job_dir(job_id).exists():
        raise HTTPException(
            status_code=409,
            detail=f"Staging job {job_id} already exists; choose a new job_id.")
    background_tasks.add_task(
        execute_crawl_job,
        job_id=job_id,
        source=req.source,
        urls=req.urls,
        limit=req.limit,
        export_format=req.export_format,
        download_attachments=req.download_attachments,
        max_pagination_pages=req.max_pagination_pages,
    )

    return {
        "status": "ACCEPTED",
        "message": f"Crawl job '{job_id}' started in background.",
        "job_id": job_id,
        "export_format": req.export_format.value,
    }

@app.get("/api/jobs/{job_id}/preview")
async def get_job_preview(job_id: str):
    preview_path = get_job_dir(job_id) / "preview.html"
    if not preview_path.exists():
        raise HTTPException(status_code=404, detail=f"Preview file for job {job_id} not found.")
    return FileResponse(preview_path, media_type="text/html")


@app.get("/api/jobs/{job_id}/exports")
async def list_job_exports(job_id: str):
    service = get_export_service(job_id)
    return {
        "job_id": service.job_directory.name,
        "formats": service.descriptors(),
    }


@app.get("/api/jobs/{job_id}/exports/{export_format}")
async def download_job_export(
    job_id: str,
    export_format: RagExportFormat,
):
    service = get_export_service(job_id)
    try:
        artifact = await asyncio.to_thread(service.persisted, export_format)
        if artifact is None:
            artifact = await asyncio.to_thread(service.build, export_format)
    except ExportTooLargeError as exc:
        raise HTTPException(
            status_code=413,
            detail="Export exceeds the configured size limit.",
        ) from exc
    except ExportDependencyUnavailableError as exc:
        logger.error(
            "Export writer is unavailable for job %s and format %s",
            job_id,
            export_format.value,
            exc_info=True,
        )
        raise HTTPException(
            status_code=503,
            detail="The requested export writer is unavailable.",
        ) from exc
    except (InvalidStagingPackageError, OSError):
        logger.warning(
            "Rejected invalid staging export for job %s",
            job_id,
            exc_info=True,
        )
        raise HTTPException(
            status_code=409,
            detail=(
                "The staging package is incomplete or failed integrity "
                "validation."
            ),
        ) from None
    return FileResponse(
        artifact.path,
        media_type=artifact.media_type,
        filename=artifact.download_name,
        headers={"Cache-Control": "no-store"},
        background=(
            BackgroundTask(artifact.cleanup)
            if artifact.temporary
            else None
        ),
    )


@app.delete("/api/jobs/{job_id}")
async def delete_job(job_id: str):
    job_dir = get_job_dir(job_id)
    settings = Settings.load_from_yaml(SETTINGS_FILE)
    raw_job_dir = resolve_job_dir(settings.storage.raw_base_dir, job_id)
    had_memory = job_id in JOB_STATUS_MAP
    if JOB_STATUS_MAP.get(job_id, {}).get("status") == "RUNNING":
        raise HTTPException(
            status_code=409,
            detail=f"Job {job_id} is running and cannot be deleted.")
    deleted_files = False
    
    if job_dir.exists() and job_dir.is_dir():
        try:
            shutil.rmtree(job_dir)
            deleted_files = True
            logger.info(f"Deleted staging directory for job {job_id}: {job_dir}")
        except Exception as e:
            logger.error(f"Failed to delete directory {job_dir}: {e}")
            raise HTTPException(status_code=500, detail=f"Failed to delete job files: {e}")

    if raw_job_dir.exists() and raw_job_dir.is_dir():
        try:
            shutil.rmtree(raw_job_dir)
            deleted_files = True
            logger.info(f"Deleted raw directory for job {job_id}: {raw_job_dir}")
        except Exception as e:
            logger.error(f"Failed to delete directory {raw_job_dir}: {e}")
            raise HTTPException(status_code=500, detail=f"Failed to delete job files: {e}")

    try:
        deleted_state = CrawlerStateStore(STATE_DB).delete_job(
            job_id,
            raw_job_dir=raw_job_dir,
        )
    except Exception as e:
        logger.error(f"Failed to delete crawler state for job {job_id}: {e}")
        raise HTTPException(status_code=500, detail="Failed to delete crawler state.") from e

    if job_id in JOB_STATUS_MAP:
        del JOB_STATUS_MAP[job_id]

    if not deleted_files and not deleted_state and not had_memory:
        raise HTTPException(
            status_code=404,
            detail=f"Job {job_id} not found in staging, raw storage, state, or memory.",
        )

    return {
        "status": "SUCCESS",
        "message": f"Job {job_id} and all job-scoped files/state were successfully deleted.",
        "job_id": job_id
    }
