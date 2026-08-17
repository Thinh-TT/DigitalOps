import json
import webbrowser
from pathlib import Path
from typing import Any, Dict, List

from ..paths import resolve_job_dir
from .preview_workspace import build_preview_html, build_preview_payload


class PreviewGenerator:
    """Generates an interactive, standalone HTML Preview for staging dataset jobs."""

    def __init__(self, staging_dir: Path | str):
        self.staging_dir = Path(staging_dir)

    def generate(self, job_id: str, auto_open: bool = False) -> Path:
        job_path = resolve_job_dir(self.staging_dir, job_id)
        if not job_path.exists():
            raise FileNotFoundError(
                f"Staging job directory '{job_path}' does not exist."
            )

        manifest_file = job_path / "manifest.json"
        obs_file = job_path / "document-observations.jsonl"
        cs_file = job_path / "chunk-sets.jsonl"
        ck_file = job_path / "chunks.jsonl"
        err_file = job_path / "crawler-errors.jsonl"

        # Load Manifest
        manifest: Dict[str, Any] = {}
        if manifest_file.exists():
            with open(manifest_file, "r", encoding="utf-8") as f:
                manifest = json.load(f)

        # Load Observations
        observations: List[Dict[str, Any]] = []
        if obs_file.exists():
            with open(obs_file, "r", encoding="utf-8") as f:
                for line in f:
                    if line.strip():
                        observations.append(json.loads(line.strip()))

        # Load Chunk Sets
        chunk_sets: List[Dict[str, Any]] = []
        if cs_file.exists():
            with open(cs_file, "r", encoding="utf-8") as f:
                for line in f:
                    if line.strip():
                        chunk_sets.append(json.loads(line.strip()))

        # Load Chunks
        chunks: List[Dict[str, Any]] = []
        if ck_file.exists():
            with open(ck_file, "r", encoding="utf-8") as f:
                for line in f:
                    if line.strip():
                        chunks.append(json.loads(line.strip()))

        # Load Errors
        errors: List[Dict[str, Any]] = []
        if err_file.exists():
            with open(err_file, "r", encoding="utf-8") as f:
                for line in f:
                    if line.strip():
                        errors.append(json.loads(line.strip()))

        html_content = self._render_html(
            job_id, manifest, observations, chunk_sets, chunks, errors
        )
        out_html_path = job_path / "preview.html"

        with open(out_html_path, "w", encoding="utf-8") as f:
            f.write(html_content)

        if auto_open:
            webbrowser.open(out_html_path.as_uri())

        return out_html_path

    def _build_preview_payload(
        self,
        job_id: str,
        manifest: Dict[str, Any],
        observations: List[Dict[str, Any]],
        chunk_sets: List[Dict[str, Any]],
        chunks: List[Dict[str, Any]],
        errors: List[Dict[str, Any]],
    ) -> Dict[str, Any]:
        return build_preview_payload(
            job_id,
            manifest,
            observations,
            chunk_sets,
            chunks,
            errors,
        )

    def _render_html(
        self,
        job_id: str,
        manifest: Dict[str, Any],
        observations: List[Dict[str, Any]],
        chunk_sets: List[Dict[str, Any]],
        chunks: List[Dict[str, Any]],
        errors: List[Dict[str, Any]],
    ) -> str:
        return build_preview_html(
            job_id,
            manifest,
            observations,
            chunk_sets,
            chunks,
            errors,
        )
