from __future__ import annotations

import hashlib
from html import escape
from typing import Callable, Optional
from urllib.parse import urlsplit

from bs4 import BeautifulSoup

from .base import CrawlResult


_GUIDANCE_HOSTS = frozenset(
    {
        "m.mattran.org.vn",
        "mattran.org.vn",
        "www.mattran.org.vn",
    }
)


def is_mattran_guidance_listing(url: str) -> bool:
    parsed = urlsplit(url)
    return (
        (parsed.hostname or "").lower().rstrip(".") in _GUIDANCE_HOSTS
        and parsed.path.rstrip("/").lower() == "/van-ban-huong-dan.html"
    )


def _label_values(row) -> dict[str, str]:
    values = list(row.stripped_strings)
    result: dict[str, str] = {}
    for index in range(0, len(values) - 1, 2):
        label = values[index].strip().casefold()
        value = values[index + 1].strip().lstrip(":").strip()
        result[label] = value
    return result


def parse_mattran_guidance_records(
    soup: BeautifulSoup,
    listing_url: str,
    *,
    normalize_link: Callable[[str, str], Optional[str]],
    source_id: str,
    source_namespace: str,
    authority_namespace: Optional[str],
    identity_strategy: str,
) -> list[CrawlResult]:
    """Turn each two-row guidance entry into one stable RAG document."""
    if not is_mattran_guidance_listing(listing_url):
        return []

    rows = soup.select("tr")
    records: list[CrawlResult] = []
    seen_keys: set[str] = set()
    for index in range(0, len(rows) - 1):
        header_row = rows[index]
        abstract_row = rows[index + 1]
        header_values = list(header_row.stripped_strings)
        abstract_values = list(abstract_row.stripped_strings)
        if (
            not header_values
            or header_values[0].strip().casefold() != "loại văn bản"
            or not abstract_values
            or abstract_values[0].strip().casefold() != "trích yếu"
        ):
            continue

        labels = _label_values(header_row)
        document_type = labels.get("loại văn bản", "")
        issued_date = labels.get("ngày ban hành", "")
        anchors = abstract_row.select("a[href]")
        abstract = ""
        attachment_urls: list[str] = []
        for anchor in anchors:
            anchor_text = anchor.get_text(" ", strip=True)
            if anchor_text and not abstract:
                abstract = anchor_text
            normalized = normalize_link(listing_url, str(anchor.get("href", "")))
            if normalized and normalized not in attachment_urls:
                attachment_urls.append(normalized)
        if not abstract:
            abstract = " ".join(abstract_values[1:]).strip()
        if not abstract:
            continue

        identity_material = "\n".join(
            [
                document_type.casefold(),
                issued_date,
                abstract.casefold(),
                *sorted(attachment_urls),
            ]
        )
        digest = hashlib.sha256(identity_material.encode("utf-8")).hexdigest()
        canonical_key = f"mattran_guidance:{digest[:32]}"
        if canonical_key in seen_keys:
            continue
        seen_keys.add(canonical_key)

        title = abstract
        virtual_url = f"{listing_url}#document-{digest[:16]}"
        attachment_markup = "".join(
            f"<li>{escape(url)}</li>" for url in attachment_urls
        )
        html = (
            "<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\">"
            f"<title>{escape(title)}</title></head><body><article>"
            f"<h1>{escape(title)}</h1>"
            f"<p>Loại văn bản: {escape(document_type or 'Không rõ')}</p>"
            f"<p>Ngày ban hành: {escape(issued_date or 'Không rõ')}</p>"
            f"<p>Trích yếu: {escape(abstract)}</p>"
            f"<ul>{attachment_markup}</ul>"
            "</article></body></html>"
        ).encode("utf-8")
        records.append(
            CrawlResult(
                url=virtual_url,
                canonical_key=canonical_key,
                title=title,
                html_or_bytes=html,
                mime_type="text/html",
                document_identity_strategy=identity_strategy,
                source_namespace=source_namespace,
                authority_namespace=authority_namespace,
                metadata={
                    "source_url": virtual_url,
                    "listing_url": listing_url,
                    "record_kind": "guidance_document",
                    "crawl_role": "primary_record",
                    "document_type": document_type,
                    "issued_date": issued_date,
                    "abstract": abstract,
                    "attachment_urls": attachment_urls,
                    "attachment_count": len(attachment_urls),
                },
                discovered_links=attachment_urls,
                requested_url=listing_url,
                final_url=virtual_url,
                http_status=200,
            )
        )
    return records
