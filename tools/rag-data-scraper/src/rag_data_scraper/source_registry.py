from __future__ import annotations

from pathlib import Path
import re
from typing import Literal, Optional
from urllib.parse import urlsplit

from pydantic import BaseModel, ConfigDict, Field, model_validator


CorpusType = Literal["general", "legal_reference"]
SourceTrustTier = Literal["official", "verified_copy", "aggregator", "unverified"]
PublishPolicy = Literal[
    "authoritative",
    "verified_copy",
    "cross_check_only",
    "blocked",
]
MAX_REGISTRY_BYTES = 1024 * 1024
HOST_PATTERN = re.compile(
    r"^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)*"
    r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$"
)


class SourceRegistryEntry(BaseModel):
    model_config = ConfigDict(extra="forbid")

    entry_id: str = Field(min_length=1, max_length=128)
    adapter: Literal["gov_portal", "legal_aggregator", "generic_web"]
    source_id: str = Field(min_length=1, max_length=128)
    source_namespace: str = Field(min_length=1, max_length=128)
    authority_namespace: Optional[str] = Field(default=None, max_length=128)
    corpus_type: CorpusType
    source_trust_tier: SourceTrustTier
    publish_policy: PublishPolicy
    allowed_hosts: list[str] = Field(min_length=1, max_length=32)
    default_issuer: Optional[str] = Field(default=None, max_length=512)
    language: str = Field(default="vi", min_length=2, max_length=16)

    @model_validator(mode="after")
    def normalize_and_validate_hosts(self):
        normalized = sorted(
            {
                host.strip().lower().rstrip(".")
                for host in self.allowed_hosts
                if host.strip()
            }
        )
        if not normalized:
            raise ValueError("source registry entry requires allowed hosts")
        if any(HOST_PATTERN.fullmatch(host) is None for host in normalized):
            raise ValueError("source registry contains an invalid allowed host")
        self.allowed_hosts = normalized
        return self


class ResolvedSourceProfile(SourceRegistryEntry):
    registry_version: str


class SourceRegistry(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: str = "1.0"
    registry_version: str = Field(min_length=1, max_length=64)
    sources: list[SourceRegistryEntry] = Field(min_length=1, max_length=256)

    @model_validator(mode="after")
    def validate_registry(self):
        if self.schema_version != "1.0":
            raise ValueError(
                f"unsupported source registry schema_version '{self.schema_version}'"
            )
        entry_ids = [entry.entry_id for entry in self.sources]
        if len(entry_ids) != len(set(entry_ids)):
            raise ValueError("source registry entry_id values must be unique")
        return self

    @classmethod
    def load(cls, path: Path | str) -> "SourceRegistry":
        registry_path = Path(path)
        if not registry_path.is_file():
            raise FileNotFoundError(
                f"source registry does not exist: {registry_path}"
            )
        size = registry_path.stat().st_size
        if size <= 0 or size > MAX_REGISTRY_BYTES:
            raise ValueError("source registry size is outside the allowed range")
        return cls.model_validate_json(registry_path.read_bytes())

    def resolve(
        self,
        adapter: str,
        urls: list[str],
    ) -> Optional[ResolvedSourceProfile]:
        hosts = {
            (urlsplit(url).hostname or "").lower().rstrip(".")
            for url in urls
        }
        hosts.discard("")
        if not hosts:
            return None
        matches = [
            entry
            for entry in self.sources
            if entry.adapter == adapter
            and hosts.issubset(set(entry.allowed_hosts))
        ]
        if len(matches) > 1:
            raise ValueError(
                f"source registry is ambiguous for adapter '{adapter}' and hosts {sorted(hosts)}"
            )
        if not matches:
            return None
        return ResolvedSourceProfile(
            **matches[0].model_dump(),
            registry_version=self.registry_version,
        )
