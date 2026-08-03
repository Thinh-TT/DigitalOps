from __future__ import annotations

import hashlib
import os
from pathlib import Path
import shutil
import subprocess
from tempfile import TemporaryDirectory
from zipfile import BadZipFile, ZipFile

from .base import BaseExtractor, ExtractedDocument
from .docx_extractor import DOCXExtractor


class LegacyDocExtractor(BaseExtractor):
    """Convert a bounded legacy DOC/RTF file with headless LibreOffice."""

    def __init__(
        self,
        *,
        soffice_cmd: str | None = None,
        timeout_seconds: float = 60.0,
        max_output_bytes: int = 64 * 1024 * 1024,
    ) -> None:
        if timeout_seconds <= 0 or max_output_bytes <= 0:
            raise ValueError("legacy DOC conversion limits must be positive")
        self.soffice_cmd = soffice_cmd
        self.timeout_seconds = timeout_seconds
        self.max_output_bytes = max_output_bytes

    def _resolve_soffice(self) -> Path:
        configured = self.soffice_cmd or os.environ.get(
            "RAG_SCRAPER_LIBREOFFICE"
        )
        candidates: list[str | Path] = []
        if configured:
            candidates.append(configured)
        for command in ("soffice", "libreoffice"):
            resolved = shutil.which(command)
            if resolved:
                candidates.append(resolved)
        candidates.extend(
            [
                Path("C:/Program Files/LibreOffice/program/soffice.exe"),
                Path("C:/Program Files (x86)/LibreOffice/program/soffice.exe"),
                Path("/usr/bin/soffice"),
                Path("/usr/bin/libreoffice"),
                Path("/Applications/LibreOffice.app/Contents/MacOS/soffice"),
            ]
        )
        for candidate in candidates:
            path = Path(candidate).expanduser()
            if path.is_file():
                return path.resolve()
        raise RuntimeError(
            "LibreOffice was not found; install it or set "
            "RAG_SCRAPER_LIBREOFFICE to the soffice executable"
        )

    def _validate_docx(self, path: Path) -> None:
        if not path.is_file() or path.stat().st_size > self.max_output_bytes:
            raise ValueError("converted DOCX is missing or exceeds the output limit")
        try:
            with ZipFile(path) as archive:
                members = archive.infolist()
                names = {member.filename for member in members}
                if not {"[Content_Types].xml", "word/document.xml"}.issubset(
                    names
                ):
                    raise ValueError("converted DOCX is missing required members")
                if (
                    len(members) > 10_000
                    or sum(member.file_size for member in members)
                    > self.max_output_bytes * 8
                ):
                    raise ValueError("converted DOCX expands beyond safety limits")
        except BadZipFile as exc:
            raise ValueError("LibreOffice produced an invalid DOCX package") from exc

    def extract(self, file_path: Path | str) -> ExtractedDocument:
        source = Path(file_path).resolve()
        raw_bytes = source.read_bytes()
        raw_sha256 = hashlib.sha256(raw_bytes).hexdigest()
        soffice = self._resolve_soffice()

        with TemporaryDirectory(prefix="rag-doc-convert-") as temporary:
            workspace = Path(temporary)
            input_path = workspace / "input.doc"
            output_dir = workspace / "output"
            profile_dir = workspace / "profile"
            output_dir.mkdir()
            profile_dir.mkdir()
            input_path.write_bytes(raw_bytes)
            command = [
                str(soffice),
                "--headless",
                "--safe-mode",
                "--nologo",
                "--nodefault",
                "--nofirststartwizard",
                "--norestore",
                f"-env:UserInstallation={profile_dir.as_uri()}",
                "--convert-to",
                "docx",
                "--outdir",
                str(output_dir),
                str(input_path),
            ]
            try:
                completed = subprocess.run(
                    command,
                    cwd=workspace,
                    capture_output=True,
                    text=True,
                    timeout=self.timeout_seconds,
                    check=False,
                    shell=False,
                    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                )
            except subprocess.TimeoutExpired as exc:
                raise TimeoutError(
                    f"legacy DOC conversion exceeded {self.timeout_seconds:g} seconds"
                ) from exc
            output_path = output_dir / "input.docx"
            if completed.returncode != 0 or not output_path.is_file():
                raise RuntimeError(
                    "LibreOffice could not convert the legacy DOC document"
                )
            self._validate_docx(output_path)
            converted = DOCXExtractor().extract(output_path)

        return ExtractedDocument(
            source_uri=str(source),
            title=source.stem,
            mime_type="application/msword",
            raw_sha256=raw_sha256,
            blocks=converted.blocks,
            ocr_used=False,
            ocr_confidence=1.0,
            document_metadata={
                "legacy_doc_converted": True,
                "legacy_doc_converter": "libreoffice-headless",
            },
        )
