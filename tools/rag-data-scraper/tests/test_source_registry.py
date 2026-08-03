import json

import pytest
from pydantic import ValidationError

from rag_data_scraper.source_registry import SourceRegistry


def test_registry_resolves_only_when_all_hosts_match_same_entry(tmp_path):
    path = tmp_path / "source-registry.json"
    path.write_text(
        json.dumps(
            {
                "schema_version": "1.0",
                "registry_version": "test-1",
                "sources": [
                    {
                        "entry_id": "official",
                        "adapter": "generic_web",
                        "source_id": "official_source",
                        "source_namespace": "example.gov.vn",
                        "authority_namespace": "gov.vn",
                        "corpus_type": "legal_reference",
                        "source_trust_tier": "official",
                        "publish_policy": "authoritative",
                        "allowed_hosts": ["EXAMPLE.GOV.VN.", "www.example.gov.vn"],
                        "default_issuer": "Cơ quan nhà nước",
                        "language": "vi",
                    }
                ],
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )

    registry = SourceRegistry.load(path)
    profile = registry.resolve(
        "generic_web",
        [
            "https://example.gov.vn/page/1",
            "https://www.example.gov.vn/document/1",
        ],
    )

    assert profile is not None
    assert profile.entry_id == "official"
    assert profile.registry_version == "test-1"
    assert profile.allowed_hosts == ["example.gov.vn", "www.example.gov.vn"]
    assert registry.resolve("generic_web", ["https://evil.example/page"]) is None


def test_registry_rejects_duplicate_entry_ids():
    entry = {
        "entry_id": "duplicate",
        "adapter": "generic_web",
        "source_id": "source",
        "source_namespace": "example.gov.vn",
        "corpus_type": "legal_reference",
        "source_trust_tier": "official",
        "publish_policy": "authoritative",
        "allowed_hosts": ["example.gov.vn"],
    }

    with pytest.raises(ValidationError, match="entry_id values must be unique"):
        SourceRegistry.model_validate(
            {
                "schema_version": "1.0",
                "registry_version": "test-1",
                "sources": [entry, entry],
            }
        )
