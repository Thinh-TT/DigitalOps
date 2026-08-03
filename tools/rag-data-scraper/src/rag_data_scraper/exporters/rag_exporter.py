from __future__ import annotations

import csv
from dataclasses import dataclass
from enum import Enum
import hashlib
import io
import json
from pathlib import Path
import tempfile
from typing import BinaryIO, Callable, Iterable, TypeVar
from zipfile import ZIP_DEFLATED, ZipFile

from pydantic import BaseModel, ValidationError

from ..models.chunk import Chunk, ChunkSet
from ..models.manifest import StagingManifest
from ..paths import validate_job_id
from ..models.observation import DocumentObservation
from .export_errors import (
    ExportDependencyUnavailableError,
    ExportTooLargeError,
    InvalidStagingPackageError,
)
from .rich_format_exporter import (
    RichExportDocument,
    RichFormatExporter,
)


DEFAULT_MAX_EXPORT_BYTES = 1024 * 1024 * 1024
MAX_JSON_LINE_BYTES = 32 * 1024 * 1024
FORMAT_VERSION = "1.0"


class RagExportFormat(str, Enum):
    STAGING_ZIP = "staging_zip"
    CHUNKS_JSONL = "chunks_jsonl"
    CHUNKS_CSV = "chunks_csv"
    DOCUMENTS_MARKDOWN_ZIP = "documents_markdown_zip"
    DOCUMENTS_HTML = "documents_html"
    DOCUMENTS_PDF = "documents_pdf"
    DOCUMENTS_DOCX = "documents_docx"
    DOCUMENTS_TXT_ZIP = "documents_txt_zip"
    CHUNKS_XLSX = "chunks_xlsx"
    CHUNKS_JSON = "chunks_json"
    DOCUMENTS_PPTX = "documents_pptx"
    DOCUMENTS_XML = "documents_xml"
    DOCUMENTS_SVG_ZIP = "documents_svg_zip"


@dataclass(frozen=True)
class ExportFormatDescriptor:
    format_id: RagExportFormat
    label: str
    description: str
    media_type: str
    suffix: str


EXPORT_FORMATS: dict[RagExportFormat, ExportFormatDescriptor] = {
    RagExportFormat.STAGING_ZIP: ExportFormatDescriptor(
        RagExportFormat.STAGING_ZIP,
        "Staging ZIP",
        "Self-contained lossless package for DxOs.Workers.",
        "application/zip",
        "-staging.zip",
    ),
    RagExportFormat.CHUNKS_JSONL: ExportFormatDescriptor(
        RagExportFormat.CHUNKS_JSONL,
        "Chunks JSONL",
        "One exact-text chunk per line with RAG metadata.",
        "application/x-ndjson",
        "-chunks.jsonl",
    ),
    RagExportFormat.CHUNKS_CSV: ExportFormatDescriptor(
        RagExportFormat.CHUNKS_CSV,
        "Chunks CSV",
        "Flat spreadsheet-safe data for review and ETL.",
        "text/csv; charset=utf-8",
        "-chunks.csv",
    ),
    RagExportFormat.DOCUMENTS_MARKDOWN_ZIP: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_MARKDOWN_ZIP,
        "Documents Markdown ZIP",
        "One Markdown document per observation with YAML front matter.",
        "application/zip",
        "-documents-markdown.zip",
    ),
    RagExportFormat.DOCUMENTS_HTML: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_HTML,
        "Documents HTML",
        "Readable standalone HTML with escaped normalized text.",
        "text/html; charset=utf-8",
        "-documents.html",
    ),
    RagExportFormat.DOCUMENTS_PDF: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_PDF,
        "Documents PDF",
        "Paginated PDF for review, sharing, and text extraction.",
        "application/pdf",
        "-documents.pdf",
    ),
    RagExportFormat.DOCUMENTS_DOCX: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_DOCX,
        "Documents DOCX",
        "Editable Word document with source metadata and full text.",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "-documents.docx",
    ),
    RagExportFormat.DOCUMENTS_TXT_ZIP: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_TXT_ZIP,
        "Documents TXT ZIP",
        "One exact UTF-8 text file per observation plus a manifest.",
        "application/zip",
        "-documents-txt.zip",
    ),
    RagExportFormat.CHUNKS_XLSX: ExportFormatDescriptor(
        RagExportFormat.CHUNKS_XLSX,
        "Chunks XLSX",
        "Spreadsheet-safe chunk and document sheets for review and ETL.",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "-chunks.xlsx",
    ),
    RagExportFormat.CHUNKS_JSON: ExportFormatDescriptor(
        RagExportFormat.CHUNKS_JSON,
        "Chunks JSON",
        "A JSON envelope containing exact-text chunks and RAG metadata.",
        "application/json",
        "-chunks.json",
    ),
    RagExportFormat.DOCUMENTS_PPTX: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_PPTX,
        "Documents PPTX",
        "Presentation slides containing document text and metadata.",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "-documents.pptx",
    ),
    RagExportFormat.DOCUMENTS_XML: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_XML,
        "Documents XML",
        "Structured XML containing documents, metadata, and chunks.",
        "application/xml",
        "-documents.xml",
    ),
    RagExportFormat.DOCUMENTS_SVG_ZIP: ExportFormatDescriptor(
        RagExportFormat.DOCUMENTS_SVG_ZIP,
        "Documents SVG ZIP",
        "One safe SVG preview per document with full text in metadata.",
        "application/zip",
        "-documents-svg.zip",
    ),
}




@dataclass(frozen=True)
class ExportArtifact:
    path: Path
    download_name: str
    media_type: str
    temporary: bool = True

    def cleanup(self) -> None:
        if self.temporary:
            self.path.unlink(missing_ok=True)


@dataclass(frozen=True)
class _LoadedPackage:
    manifest: StagingManifest
    observations: dict[str, DocumentObservation]
    chunk_sets: dict[str, ChunkSet]
    chunks: tuple[Chunk, ...]
    normalized_texts: dict[str, str]


ModelT = TypeVar("ModelT", bound=BaseModel)


class _BoundedWriter:
    def __init__(self, stream: BinaryIO, max_bytes: int) -> None:
        self.stream = stream
        self.max_bytes = max_bytes
        self.bytes_written = 0

    def write(self, value: bytes) -> None:
        if self.bytes_written + len(value) > self.max_bytes:
            raise ExportTooLargeError("Export exceeds its byte limit.")
        self.stream.write(value)
        self.bytes_written += len(value)


class RagExportService:
    """Validate a staging package and build bounded, portable RAG exports."""

    _CANONICAL_FILES = (
        "manifest.json",
        "document-observations.jsonl",
        "chunk-sets.jsonl",
        "chunks.jsonl",
        "crawler-errors.jsonl",
    )
    _KNOWN_ROLES = {
        "public",
        "administrator",
        "clerk",
        "drafter",
        "leader",
    }
    _KNOWN_CLASSIFICATIONS = {
        "public",
        "internal",
        "confidential",
        "restricted",
    }

    def __init__(
        self,
        job_directory: Path | str,
        max_export_bytes: int = DEFAULT_MAX_EXPORT_BYTES,
    ) -> None:
        self.job_directory = Path(job_directory).resolve()
        try:
            validate_job_id(self.job_directory.name)
        except ValueError as exc:
            raise InvalidStagingPackageError(
                "Staging directory has an invalid job identifier."
            ) from exc
        if max_export_bytes <= 0:
            raise ValueError("max_export_bytes must be positive")
        self.max_export_bytes = max_export_bytes
        self._tracked_files: set[Path] = set()
        self._tracked_bytes = 0

    @staticmethod
    def descriptors() -> list[dict[str, str]]:
        return [
            {
                "format_id": value.format_id.value,
                "label": value.label,
                "description": value.description,
                "media_type": value.media_type,
            }
            for value in EXPORT_FORMATS.values()
        ]

    def build(self, export_format: RagExportFormat) -> ExportArtifact:
        descriptor = EXPORT_FORMATS[export_format]
        package = self._load_and_validate()
        temp_path = self._new_temp_path(descriptor.suffix)
        rich = RichFormatExporter(self.max_export_bytes)
        documents_cache: tuple[RichExportDocument, ...] | None = None
        records_cache: tuple[dict[str, object], ...] | None = None

        def documents() -> tuple[RichExportDocument, ...]:
            nonlocal documents_cache
            if documents_cache is None:
                documents_cache = self._rich_documents(package)
            return documents_cache

        def records() -> tuple[dict[str, object], ...]:
            nonlocal records_cache
            if records_cache is None:
                records_cache = tuple(
                    self._record(observation, chunk_set, chunk)
                    for observation, chunk_set, chunk
                    in self._ordered(package)
                )
            return records_cache

        job_id = package.manifest.job_id
        try:
            writers: dict[RagExportFormat, Callable[[], None]] = {
                RagExportFormat.STAGING_ZIP: lambda: (
                    self._write_staging_zip(temp_path)
                ),
                RagExportFormat.CHUNKS_JSONL: lambda: (
                    self._write_chunks_jsonl(temp_path, package)
                ),
                RagExportFormat.CHUNKS_CSV: lambda: (
                    self._write_chunks_csv(temp_path, package)
                ),
                RagExportFormat.DOCUMENTS_MARKDOWN_ZIP: lambda: (
                    self._write_markdown_zip(temp_path, package)
                ),
                RagExportFormat.DOCUMENTS_HTML: lambda: (
                    rich.write_html(temp_path, job_id, documents())
                ),
                RagExportFormat.DOCUMENTS_PDF: lambda: (
                    rich.write_pdf(temp_path, job_id, documents())
                ),
                RagExportFormat.DOCUMENTS_DOCX: lambda: (
                    rich.write_docx(temp_path, job_id, documents())
                ),
                RagExportFormat.DOCUMENTS_TXT_ZIP: lambda: (
                    rich.write_txt_zip(temp_path, job_id, documents())
                ),
                RagExportFormat.CHUNKS_XLSX: lambda: (
                    rich.write_xlsx(temp_path, documents(), records())
                ),
                RagExportFormat.CHUNKS_JSON: lambda: (
                    rich.write_json(temp_path, job_id, records())
                ),
                RagExportFormat.DOCUMENTS_PPTX: lambda: (
                    rich.write_pptx(temp_path, job_id, documents())
                ),
                RagExportFormat.DOCUMENTS_XML: lambda: (
                    rich.write_xml(temp_path, job_id, documents())
                ),
                RagExportFormat.DOCUMENTS_SVG_ZIP: lambda: (
                    rich.write_svg_zip(temp_path, job_id, documents())
                ),
            }
            writers[export_format]()
            if temp_path.stat().st_size > self.max_export_bytes:
                raise ExportTooLargeError("Export exceeds its byte limit.")
            return ExportArtifact(
                temp_path,
                f"{job_id}{descriptor.suffix}",
                descriptor.media_type,
            )
        except Exception:
            temp_path.unlink(missing_ok=True)
            raise

    def build_persistent(
        self,
        export_format: RagExportFormat,
    ) -> ExportArtifact:
        """Build once into the job directory and protect it with SHA-256."""
        descriptor = EXPORT_FORMATS[export_format]
        artifact = self.build(export_format)
        exports_dir = self.job_directory / "exports"
        try:
            if exports_dir.is_symlink():
                raise InvalidStagingPackageError(
                    "Export directory cannot be a symbolic link."
                )
            exports_dir.mkdir(parents=False, exist_ok=True)
            destination = exports_dir / f"{self.job_directory.name}{descriptor.suffix}"
            checksum_path = destination.with_name(destination.name + ".sha256")
            if destination.is_symlink() or checksum_path.is_symlink():
                raise InvalidStagingPackageError(
                    "Persistent export cannot replace a symbolic link."
                )
            artifact.path.replace(destination)
            digest = self._sha256_file(destination)
            temporary_checksum = checksum_path.with_suffix(
                checksum_path.suffix + ".tmp"
            )
            temporary_checksum.write_text(
                f"{digest}  {destination.name}\n",
                encoding="ascii",
                newline="\n",
            )
            temporary_checksum.replace(checksum_path)
            return ExportArtifact(
                destination,
                destination.name,
                descriptor.media_type,
                temporary=False,
            )
        except Exception:
            artifact.cleanup()
            raise

    def persisted(
        self,
        export_format: RagExportFormat,
    ) -> ExportArtifact | None:
        """Return a verified persistent artifact, or None when it is absent/stale."""
        descriptor = EXPORT_FORMATS[export_format]
        exports_dir = self.job_directory / "exports"
        destination = exports_dir / f"{self.job_directory.name}{descriptor.suffix}"
        checksum_path = destination.with_name(destination.name + ".sha256")
        if (
            exports_dir.is_symlink()
            or destination.is_symlink()
            or checksum_path.is_symlink()
            or not destination.is_file()
            or not checksum_path.is_file()
        ):
            return None
        if destination.stat().st_size > self.max_export_bytes:
            return None
        try:
            checksum = checksum_path.read_text(encoding="ascii").split()[0]
        except (IndexError, OSError, UnicodeError):
            return None
        actual = self._sha256_file(destination)
        if checksum != actual:
            return None
        return ExportArtifact(
            destination,
            destination.name,
            descriptor.media_type,
            temporary=False,
        )

    @staticmethod
    def _sha256_file(path: Path) -> str:
        digest = hashlib.sha256()
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        return digest.hexdigest()

    @staticmethod
    def _new_temp_path(suffix: str) -> Path:
        handle = tempfile.NamedTemporaryFile(
            prefix="digitalops-rag-export-",
            suffix=suffix,
            delete=False,
        )
        handle.close()
        return Path(handle.name)

    def _track_file(self, path: Path) -> None:
        if path in self._tracked_files:
            return
        if path.is_symlink() or not path.is_file():
            raise InvalidStagingPackageError(
                f"Package member is not a regular file: {path.name}"
            )
        if self._tracked_bytes + path.stat().st_size > self.max_export_bytes:
            raise ExportTooLargeError("Package exceeds its byte limit.")
        self._tracked_files.add(path)
        self._tracked_bytes += path.stat().st_size

    def _safe_member(self, relative_value: str) -> Path:
        if not relative_value or "\x00" in relative_value:
            raise InvalidStagingPackageError("Package contains an empty path.")
        relative = Path(relative_value)
        if relative.is_absolute() or ".." in relative.parts:
            raise InvalidStagingPackageError(
                f"Package path escapes the job directory: {relative_value}"
            )
        current = self.job_directory
        for part in relative.parts:
            current /= part
            if current.is_symlink():
                raise InvalidStagingPackageError(
                    f"Package path contains a symlink: {relative_value}"
                )
        try:
            resolved = (self.job_directory / relative).resolve(strict=True)
            resolved.relative_to(self.job_directory)
        except (FileNotFoundError, ValueError) as exc:
            raise InvalidStagingPackageError(
                f"Package member is missing or unsafe: {relative_value}"
            ) from exc
        self._track_file(resolved)
        return resolved

    def _read_json_lines(
        self,
        file_name: str,
        model_type: type[ModelT],
    ) -> list[ModelT]:
        values: list[ModelT] = []
        try:
            with self._safe_member(file_name).open("rb") as stream:
                while True:
                    line = stream.readline(MAX_JSON_LINE_BYTES + 1)
                    if not line:
                        break
                    if len(line) > MAX_JSON_LINE_BYTES:
                        raise InvalidStagingPackageError(
                            f"{file_name} contains an oversized JSON line."
                        )
                    if line.strip():
                        values.append(model_type.model_validate_json(line))
        except (OSError, UnicodeError, ValidationError) as exc:
            raise InvalidStagingPackageError(
                f"{file_name} is not valid staging JSONL."
            ) from exc
        return values

    @staticmethod
    def _sha256(path: Path) -> str:
        digest = hashlib.sha256()
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        return digest.hexdigest()

    @staticmethod
    def _unique(
        values: Iterable[ModelT],
        attribute: str,
    ) -> dict[str, ModelT]:
        result: dict[str, ModelT] = {}
        for value in values:
            key = str(getattr(value, attribute))
            if key in result:
                raise InvalidStagingPackageError(
                    f"Duplicate {attribute}: {key}"
                )
            result[key] = value
        return result

    def _load_and_validate(self) -> _LoadedPackage:
        if not self.job_directory.is_dir():
            raise InvalidStagingPackageError("Staging job does not exist.")
        try:
            manifest = StagingManifest.model_validate_json(
                self._safe_member("manifest.json").read_bytes()
            )
        except (OSError, ValidationError) as exc:
            raise InvalidStagingPackageError(
                "manifest.json is not valid."
            ) from exc
        expected_files = {
            "observations_file": "document-observations.jsonl",
            "chunk_sets_file": "chunk-sets.jsonl",
            "chunks_file": "chunks.jsonl",
            "errors_file": "crawler-errors.jsonl",
        }
        if (
            manifest.job_id != self.job_directory.name
            or manifest.files.model_dump() != expected_files
        ):
            raise InvalidStagingPackageError(
                "Manifest does not match the staging contract."
            )
        observation_values = self._read_json_lines(
            expected_files["observations_file"], DocumentObservation
        )
        chunk_set_values = self._read_json_lines(
            expected_files["chunk_sets_file"], ChunkSet
        )
        chunk_values = self._read_json_lines(
            expected_files["chunks_file"], Chunk
        )
        error_count = self._count_lines(expected_files["errors_file"])
        if (
            len(observation_values) != manifest.total_observations
            or len(chunk_set_values) != manifest.total_chunk_sets
            or len(chunk_values) != manifest.total_chunks
            or error_count != manifest.total_errors
            or not observation_values
            or not chunk_set_values
            or not chunk_values
        ):
            raise InvalidStagingPackageError(
                "Manifest counts do not match staging files."
            )
        observations = self._unique(
            observation_values, "observation_id"
        )
        chunk_sets = self._unique(chunk_set_values, "chunk_set_id")
        self._unique(chunk_values, "chunk_id")
        if any(
            str(chunk.chunk_set_id) not in chunk_sets
            for chunk in chunk_values
        ):
            raise InvalidStagingPackageError(
                "Chunk references an unknown chunk set."
            )
        normalized_texts: dict[str, str] = {}
        set_counts: dict[str, int] = {}
        for observation_id, observation in observations.items():
            if observation.job_id != manifest.job_id:
                raise InvalidStagingPackageError(
                    "Observation belongs to another job."
                )
            raw_path = self._safe_member(observation.raw_artifact_uri)
            normalized_path = self._safe_member(
                observation.normalized_text_uri
            )
            if self._sha256(raw_path) != observation.raw_sha256:
                raise InvalidStagingPackageError("Raw hash mismatch.")
            normalized_bytes = normalized_path.read_bytes()
            if (
                hashlib.sha256(normalized_bytes).hexdigest()
                != observation.normalized_text_sha256
            ):
                raise InvalidStagingPackageError(
                    "Normalized hash mismatch."
                )
            try:
                text = normalized_bytes.decode("utf-8")
            except UnicodeDecodeError as exc:
                raise InvalidStagingPackageError(
                    "Normalized text is not UTF-8."
                ) from exc
            if len(text) != observation.char_count:
                raise InvalidStagingPackageError(
                    "Normalized character count mismatch."
                )
            normalized_texts[observation_id] = text
        for chunk_set_id, chunk_set in chunk_sets.items():
            observation_id = str(chunk_set.observation_id)
            if (
                observation_id not in observations
                or chunk_set.job_id != manifest.job_id
            ):
                raise InvalidStagingPackageError(
                    f"Invalid chunk set: {chunk_set_id}"
                )
            set_counts[observation_id] = set_counts.get(observation_id, 0) + 1
            set_chunks = sorted(
                [
                    chunk
                    for chunk in chunk_values
                    if str(chunk.chunk_set_id) == chunk_set_id
                ],
                key=lambda chunk: chunk.chunk_index,
            )
            if (
                len(set_chunks) != chunk_set.total_chunks
                or [item.chunk_index for item in set_chunks]
                != list(range(len(set_chunks)))
            ):
                raise InvalidStagingPackageError(
                    f"Invalid chunk sequence: {chunk_set_id}"
                )
            for chunk in set_chunks:
                self._validate_chunk(
                    chunk,
                    normalized_texts[observation_id],
                    chunk_set.max_tokens or 512,
                )
        if any(set_counts.get(key, 0) != 1 for key in observations):
            raise InvalidStagingPackageError(
                "Each observation must have exactly one chunk set."
            )
        return _LoadedPackage(
            manifest,
            observations,
            chunk_sets,
            tuple(chunk_values),
            normalized_texts,
        )

    def _count_lines(self, file_name: str) -> int:
        count = 0
        with self._safe_member(file_name).open("rb") as stream:
            while True:
                line = stream.readline(MAX_JSON_LINE_BYTES + 1)
                if not line:
                    return count
                if len(line) > MAX_JSON_LINE_BYTES:
                    raise InvalidStagingPackageError(
                        f"{file_name} contains an oversized JSON line."
                    )
                if line.strip():
                    count += 1

    def _validate_chunk(
        self,
        chunk: Chunk,
        normalized_text: str,
        max_tokens: int,
    ) -> None:
        if (
            chunk.token_count > max_tokens
            or chunk.character_end <= chunk.character_start
            or chunk.character_end > len(normalized_text)
            or normalized_text[
                chunk.character_start : chunk.character_end
            ] != chunk.text
            or hashlib.sha256(chunk.text.encode("utf-8")).hexdigest()
            != chunk.content_sha256
        ):
            raise InvalidStagingPackageError(
                f"Invalid chunk content: {chunk.chunk_id}"
            )
        allowed = [value.lower() for value in chunk.chunk_acl.allowed_roles]
        denied = [value.lower() for value in chunk.chunk_acl.denied_roles]
        if (
            not allowed
            or len(allowed) != len(set(allowed))
            or len(denied) != len(set(denied))
            or not set(allowed).issubset(self._KNOWN_ROLES)
            or not set(denied).issubset(self._KNOWN_ROLES)
            or set(allowed).intersection(denied)
            or chunk.chunk_acl.security_classification.lower()
            not in self._KNOWN_CLASSIFICATIONS
        ):
            raise InvalidStagingPackageError(
                f"Invalid chunk ACL: {chunk.chunk_id}"
            )

    @staticmethod
    def _rich_documents(
        package: _LoadedPackage,
    ) -> tuple[RichExportDocument, ...]:
        set_by_observation = {
            str(chunk_set.observation_id): chunk_set
            for chunk_set in package.chunk_sets.values()
        }
        chunks_by_set: dict[str, list[Chunk]] = {
            chunk_set_id: []
            for chunk_set_id in package.chunk_sets
        }
        for chunk in package.chunks:
            chunks_by_set[str(chunk.chunk_set_id)].append(chunk)
        result = []
        for observation_id, observation in sorted(
            package.observations.items(),
            key=lambda item: (
                item[1].canonical_document_key,
                item[0],
            ),
        ):
            chunk_set = set_by_observation[observation_id]
            chunks = tuple(
                sorted(
                    chunks_by_set[str(chunk_set.chunk_set_id)],
                    key=lambda chunk: (
                        chunk.chunk_index,
                        str(chunk.chunk_id),
                    ),
                )
            )
            result.append(
                RichExportDocument(
                    observation=observation,
                    normalized_text=package.normalized_texts[
                        observation_id
                    ],
                    chunks=chunks,
                )
            )
        return tuple(result)

    def _ordered(
        self, package: _LoadedPackage
    ) -> list[tuple[DocumentObservation, ChunkSet, Chunk]]:
        joined = []
        for chunk in package.chunks:
            chunk_set = package.chunk_sets[str(chunk.chunk_set_id)]
            observation = package.observations[
                str(chunk_set.observation_id)
            ]
            joined.append((observation, chunk_set, chunk))
        return sorted(
            joined,
            key=lambda item: (
                item[0].canonical_document_key,
                item[2].chunk_index,
                str(item[2].chunk_id),
            ),
        )

    @staticmethod
    def _record(
        observation: DocumentObservation,
        chunk_set: ChunkSet,
        chunk: Chunk,
    ) -> dict[str, object]:
        return {
            "id": str(chunk.chunk_id),
            "text": chunk.text,
            "metadata": {
                "format_version": FORMAT_VERSION,
                "job_id": observation.job_id,
                "observation_id": str(observation.observation_id),
                "chunk_set_id": str(chunk_set.chunk_set_id),
                "chunk_index": chunk.chunk_index,
                "token_count": chunk.token_count,
                "character_start": chunk.character_start,
                "character_end": chunk.character_end,
                "content_sha256": chunk.content_sha256,
                "canonical_document_key": (
                    observation.canonical_document_key
                ),
                "title": observation.title,
                "source_id": observation.source_id,
                "source_namespace": observation.source_namespace,
                "source_url": observation.source_document_url,
                "mime_type": observation.mime_type,
                "heading_path": chunk.heading_path,
                "page_numbers": chunk.page_numbers,
                "allowed_roles": chunk.chunk_acl.allowed_roles,
                "denied_roles": chunk.chunk_acl.denied_roles,
                "security_classification": (
                    chunk.chunk_acl.security_classification
                ),
                "normalized_text_sha256": (
                    observation.normalized_text_sha256
                ),
                "crawled_at": observation.crawled_at.isoformat(),
                "document_metadata": observation.document_metadata,
            },
        }

    def _write_chunks_jsonl(
        self, path: Path, package: _LoadedPackage
    ) -> None:
        with path.open("wb") as stream:
            writer = _BoundedWriter(stream, self.max_export_bytes)
            for observation, chunk_set, chunk in self._ordered(package):
                writer.write(
                    (
                        json.dumps(
                            self._record(observation, chunk_set, chunk),
                            ensure_ascii=False,
                            separators=(",", ":"),
                        )
                        + "\n"
                    ).encode("utf-8")
                )

    @staticmethod
    def _spreadsheet_safe(value: object) -> object:
        if isinstance(value, str) and value.lstrip().startswith(
            ("=", "+", "-", "@", "\t", "\r")
        ):
            return "'" + value
        return value

    def _write_chunks_csv(
        self, path: Path, package: _LoadedPackage
    ) -> None:
        fields = [
            "id", "text", "job_id", "observation_id", "chunk_set_id",
            "chunk_index", "token_count", "canonical_document_key",
            "title", "source_url", "mime_type", "heading_path",
            "page_numbers_json", "allowed_roles_json",
            "denied_roles_json", "security_classification",
            "content_sha256",
        ]
        with path.open("wb") as stream:
            output = _BoundedWriter(stream, self.max_export_bytes)
            output.write(b"\xef\xbb\xbf")
            buffer = io.StringIO()
            writer = csv.DictWriter(
                buffer, fieldnames=fields, lineterminator="\n"
            )

            def emit(row: dict[str, object] | None = None) -> None:
                buffer.seek(0)
                buffer.truncate(0)
                if row is None:
                    writer.writeheader()
                else:
                    writer.writerow(
                        {
                            key: self._spreadsheet_safe(value)
                            for key, value in row.items()
                        }
                    )
                output.write(buffer.getvalue().encode("utf-8"))

            emit()
            for observation, chunk_set, chunk in self._ordered(package):
                emit(
                    {
                        "id": str(chunk.chunk_id),
                        "text": chunk.text,
                        "job_id": observation.job_id,
                        "observation_id": str(observation.observation_id),
                        "chunk_set_id": str(chunk_set.chunk_set_id),
                        "chunk_index": chunk.chunk_index,
                        "token_count": chunk.token_count,
                        "canonical_document_key": (
                            observation.canonical_document_key
                        ),
                        "title": observation.title,
                        "source_url": observation.source_document_url,
                        "mime_type": observation.mime_type,
                        "heading_path": chunk.heading_path or "",
                        "page_numbers_json": json.dumps(chunk.page_numbers),
                        "allowed_roles_json": json.dumps(
                            chunk.chunk_acl.allowed_roles
                        ),
                        "denied_roles_json": json.dumps(
                            chunk.chunk_acl.denied_roles
                        ),
                        "security_classification": (
                            chunk.chunk_acl.security_classification
                        ),
                        "content_sha256": chunk.content_sha256,
                    }
                )

    def _staging_members(self) -> list[tuple[Path, str]]:
        members = [
            (self._safe_member(name), name)
            for name in self._CANONICAL_FILES
        ]
        artifacts = self.job_directory / "artifacts"
        if not artifacts.is_dir() or artifacts.is_symlink():
            raise InvalidStagingPackageError(
                "Package is missing its artifacts directory."
            )
        for candidate in sorted(artifacts.rglob("*")):
            if not candidate.is_dir():
                relative = candidate.relative_to(
                    self.job_directory
                ).as_posix()
                members.append((self._safe_member(relative), relative))
        return members

    def _write_staging_zip(self, path: Path) -> None:
        with ZipFile(
            path,
            "w",
            compression=ZIP_DEFLATED,
            compresslevel=6,
            allowZip64=True,
        ) as archive:
            for source, name in self._staging_members():
                archive.write(source, name)

    @staticmethod
    def _front_matter(observation: DocumentObservation) -> str:
        values: dict[str, object] = {
            "format_version": FORMAT_VERSION,
            "observation_id": str(observation.observation_id),
            "job_id": observation.job_id,
            "canonical_document_key": observation.canonical_document_key,
            "title": observation.title,
            "source_id": observation.source_id,
            "source_namespace": observation.source_namespace,
            "source_url": observation.source_document_url,
            "mime_type": observation.mime_type,
            "normalized_text_sha256": observation.normalized_text_sha256,
            "crawled_at": observation.crawled_at.isoformat(),
            "document_metadata": observation.document_metadata,
        }
        lines = ["---"]
        lines.extend(
            f"{key}: "
            + json.dumps(
                value,
                ensure_ascii=False,
                separators=(",", ":"),
            )
            for key, value in values.items()
        )
        return "\n".join([*lines, "---", ""])

    def _write_markdown_zip(
        self, path: Path, package: _LoadedPackage
    ) -> None:
        export_manifest = json.dumps(
            {
                "format_version": FORMAT_VERSION,
                "job_id": package.manifest.job_id,
                "total_documents": len(package.observations),
            },
            ensure_ascii=False,
            indent=2,
        ).encode("utf-8")
        uncompressed_bytes = len(export_manifest)
        with ZipFile(
            path,
            "w",
            compression=ZIP_DEFLATED,
            compresslevel=6,
            allowZip64=True,
        ) as archive:
            archive.writestr("export-manifest.json", export_manifest)
            for observation_id, observation in sorted(
                package.observations.items(),
                key=lambda item: (
                    item[1].canonical_document_key,
                    item[0],
                ),
            ):
                content = (
                    self._front_matter(observation)
                    + package.normalized_texts[observation_id]
                    + "\n"
                ).encode("utf-8")
                uncompressed_bytes += len(content)
                if uncompressed_bytes > self.max_export_bytes:
                    raise ExportTooLargeError(
                        "Markdown export exceeds its byte limit."
                    )
                archive.writestr(
                    f"documents/{observation_id}.md", content
                )

