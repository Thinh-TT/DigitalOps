import argparse
import asyncio
import ipaddress
from pathlib import Path
import sys
import logging
import webbrowser

from .config import Settings
from .db.init_sqlite import init_sqlite_db
from .adapters.gov_portal import GovPortalAdapter
from .adapters.legal_aggregator import LegalAggregatorAdapter
from .crawler.engine import CrawlEngine
from .crawler.policy import CrawlPolicy
from .exporters.preview_generator import PreviewGenerator
from .exporters.rag_exporter import RagExportFormat, RagExportService
from .adapters.generic import GenericWebAdapter

from .chunkers.structure_chunker import StructureChunker
logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
logger = logging.getLogger("rag_data_scraper.cli")

def main():
    parser = argparse.ArgumentParser(description="Multi-source RAG Data Crawler & Extraction Pipeline")
    subparsers = parser.add_subparsers(dest="command", help="Sub-command to execute")

    # init-db
    init_db_parser = subparsers.add_parser("init-db", help="Initialize SQLite state DB")
    init_db_parser.add_argument("--db-path", default="storage/state/crawler.db", help="Path to crawler.db")

    # crawl
    crawl_parser = subparsers.add_parser("crawl", help="Crawl URLs and export staging dataset")
    crawl_parser.add_argument("--source", choices=["gov_portal", "legal_aggregator", "generic_web"], default="gov_portal", help="Adapter type")
    crawl_parser.add_argument("--urls", required=True, nargs="+", help="Seed URLs to crawl")
    crawl_parser.add_argument("--job-id", required=True, help="Job identifier")
    crawl_parser.add_argument("--config", default="config/settings.yaml", help="Settings file path")
    crawl_parser.add_argument("--depth", type=int, default=0, help="Max link recursion depth")
    crawl_parser.add_argument(
        "--export-format",
        choices=[value.value for value in RagExportFormat],
        default=RagExportFormat.CHUNKS_JSONL.value,
        help="Artifact built automatically after a successful crawl",
    )
    crawl_parser.add_argument(
        "--no-attachments",
        action="store_true",
        help="Skip PDF/DOCX links discovered while crawling",
    )
    crawl_parser.add_argument(
        "--pagination-limit",
        type=int,
        default=None,
        help="Maximum pagination URLs followed (default: config value)",
    )

    # preview
    preview_parser = subparsers.add_parser("preview", help="Generate the RAG Inspector HTML workspace for a staging job")
    preview_parser.add_argument("--job-id", required=True, help="Job identifier to preview")
    crawl_parser.add_argument("--limit", type=int, default=500, help="Maximum fetched resources")
    preview_parser.add_argument("--staging-dir", default="storage/staging", help="Staging base directory")
    preview_parser.add_argument("--open", action="store_true", help="Auto open HTML in browser")

    # web
    web_parser = subparsers.add_parser("web", help="Start Web Dashboard UI server")
    web_parser.add_argument("--host", default="127.0.0.1", help="Host address (default: 127.0.0.1)")
    web_parser.add_argument("--port", type=int, default=8000, help="Port number (default: 8000)")
    web_parser.add_argument("--open", action="store_true", help="Auto open Web Dashboard in browser")

    args = parser.parse_args()

    if args.command == "init-db":
        init_sqlite_db(args.db_path)
        print(f"Successfully initialized SQLite DB at {args.db_path}")

    elif args.command == "crawl":
        settings = Settings.load_from_yaml(Path(args.config))
        
        adapter_options = {
            "timeout_seconds": settings.crawler.request_timeout_seconds,
            "max_response_bytes": settings.crawler.max_response_bytes,
            "user_agent": settings.crawler.user_agent,
            "max_attempts": settings.crawler.retry_attempts,
            "backoff_base_seconds": settings.crawler.retry_backoff_base_seconds,
            "max_backoff_seconds": settings.crawler.retry_max_backoff_seconds,
            "per_host_delay_seconds": settings.crawler.per_host_delay_seconds,
            "per_host_max_concurrent": settings.crawler.per_host_max_concurrent,
        }
        if args.source == "gov_portal":
            adapter = GovPortalAdapter(**adapter_options)
        elif args.source == "legal_aggregator":
            adapter = LegalAggregatorAdapter(**adapter_options)
        else:
            from urllib.parse import urlsplit
            allowed_hosts = {urlsplit(url).hostname for url in args.urls if urlsplit(url).hostname}
            adapter = GenericWebAdapter(allowed_hosts=allowed_hosts, **adapter_options)

        engine = CrawlEngine(
            adapter=adapter,
            state_db_path=settings.storage.state_db_path,
            staging_dir=settings.storage.staging_base_dir,
            raw_dir=settings.storage.raw_base_dir,
            max_concurrent=settings.crawler.max_concurrent_requests,
            max_total_resources=settings.crawler.max_total_resources,
            max_pagination_pages=(
                args.pagination_limit
                if args.pagination_limit is not None
                else settings.crawler.max_pagination_pages
            ),
            chunker=StructureChunker(
                target_tokens=settings.chunker.target_tokens,
                soft_max_tokens=settings.chunker.soft_max_tokens,
                overlap_tokens=settings.chunker.overlap_tokens,
                max_tokens=settings.chunker.max_tokens,
                tokenizer_name=settings.chunker.tokenizer_model,
            ),
            crawl_policy=CrawlPolicy(
                include_attachments=not args.no_attachments,
            ),
            ocr_tesseract_cmd=settings.ocr.tesseract_cmd,
            ocr_lang=settings.ocr.lang,
            ocr_min_confidence=settings.ocr.min_confidence,
            ocr_tessdata_dir=settings.ocr.tessdata_dir,
            max_ocr_pages=settings.ocr.max_pages,
            max_ocr_image_pixels=settings.ocr.max_image_pixels,
            ocr_page_timeout_seconds=settings.ocr.page_timeout_seconds,
        )

        logger.info(f"Starting crawl job '{args.job_id}' using adapter '{args.source}'")
        output_dir = asyncio.run(engine.run_job(
            job_id=args.job_id,
            seed_urls=args.urls,
            max_depth=args.depth,
            max_resources=args.limit,
        ))
        preview_html = output_dir / "preview.html"
        export_artifact = RagExportService(output_dir).build_persistent(
            RagExportFormat(args.export_format)
        )
        print(f"SUCCESS: Crawl job completed.")
        print(f"  - Staging Package: {output_dir}")
        print(f"  - RAG Inspector HTML Preview: {preview_html}")
        print(f"  - Preferred Export: {export_artifact.path}")

    elif args.command == "preview":
        generator = PreviewGenerator(staging_dir=Path(args.staging_dir))
        html_path = generator.generate(job_id=args.job_id, auto_open=args.open)
        print(f"SUCCESS: Generated interactive HTML preview at: {html_path.resolve()}")

    elif args.command == "web":
        import uvicorn
        if args.host != "localhost":
            try:
                if not ipaddress.ip_address(args.host).is_loopback:
                    parser.error("the unauthenticated dashboard may only bind to a loopback host")
            except ValueError:
                parser.error("dashboard host must be localhost or a loopback IP address")
        url = f"http://{args.host}:{args.port}"
        print(f"===========================================================")
        print(f"DigitalOps RAG Scraper Web Dashboard is running at:")
        print(f"   {url}")
        print(f"===========================================================")

        if args.open:
            webbrowser.open(url)

        uvicorn.run("rag_data_scraper.web.app:app", host=args.host, port=args.port, reload=False)


    else:
        parser.print_help()
        sys.exit(1)

if __name__ == "__main__":
    main()
