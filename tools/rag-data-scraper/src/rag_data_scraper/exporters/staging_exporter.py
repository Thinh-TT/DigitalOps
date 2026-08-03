from datetime import datetime, timezone
import json
import logging
from pathlib import Path
import shutil
from typing import List, Tuple
from ..models.observation import DocumentObservation
from ..models.chunk import ChunkSet, Chunk
from ..models.manifest import StagingManifest
from ..paths import resolve_job_dir
from ..models.error import CrawlerError
from .preview_generator import PreviewGenerator

logger = logging.getLogger(__name__)

class StagingExporter:
    def __init__(self, staging_base_dir: Path | str):
        self.staging_base_dir = Path(staging_base_dir)

    def export(
        self,
        job_id: str,
        started_at: datetime,
        completed_at: datetime,
        observations: List[DocumentObservation],
        chunk_tuples: List[Tuple[ChunkSet, List[Chunk]]],
        errors: List[CrawlerError]
    ) -> Path:
        job_dir = resolve_job_dir(self.staging_base_dir, job_id)
        job_dir.mkdir(parents=True, exist_ok=True)

        obs_path = job_dir / "document-observations.jsonl"
        cs_path = job_dir / "chunk-sets.jsonl"
        ck_path = job_dir / "chunks.jsonl"
        err_path = job_dir / "crawler-errors.jsonl"
        manifest_path = job_dir / "manifest.json"

        # 1. Copy source artifacts into the package and write portable paths.
        portable_observations: List[DocumentObservation] = []
        for observation in observations:
            observation_dir = (
                job_dir / "artifacts" / str(observation.observation_id)
            )
            observation_dir.mkdir(parents=True, exist_ok=True)
            raw_source = Path(observation.raw_artifact_uri).resolve(strict=True)
            normalized_source = Path(
                observation.normalized_text_uri
            ).resolve(strict=True)
            raw_suffix = raw_source.suffix.lower() or ".bin"
            raw_destination = observation_dir / f"raw{raw_suffix}"
            normalized_destination = observation_dir / "normalized.txt"
            shutil.copyfile(raw_source, raw_destination)
            shutil.copyfile(normalized_source, normalized_destination)
            portable_observations.append(
                observation.model_copy(
                    update={
                        "raw_artifact_uri": raw_destination.relative_to(
                            job_dir
                        ).as_posix(),
                        "normalized_text_uri": normalized_destination.relative_to(
                            job_dir
                        ).as_posix(),
                    }
                )
            )

        with open(obs_path, "w", encoding="utf-8") as f:
            for obs in portable_observations:
                f.write(obs.model_dump_json() + "\n")

        # 2. Write ChunkSets & Chunks
        total_chunks = 0
        total_chunk_sets = len(chunk_tuples)

        with open(cs_path, "w", encoding="utf-8") as f_cs, open(ck_path, "w", encoding="utf-8") as f_ck:
            for cs, chunks in chunk_tuples:
                f_cs.write(cs.model_dump_json() + "\n")
                for ck in chunks:
                    f_ck.write(ck.model_dump_json() + "\n")
                    total_chunks += 1

        # 3. Write Errors
        with open(err_path, "w", encoding="utf-8") as f:
            for err in errors:
                f.write(err.model_dump_json() + "\n")

        # 4. Write Manifest
        manifest = StagingManifest(
            job_id=job_id,
            started_at=started_at,
            completed_at=completed_at,
            total_observations=len(portable_observations),
            total_chunk_sets=total_chunk_sets,
            total_chunks=total_chunks,
            total_errors=len(errors)
        )
        with open(manifest_path, "w", encoding="utf-8") as f:
            f.write(manifest.model_dump_json(indent=2))

        # 5. Generate Preview HTML automatically
        try:
            preview_gen = PreviewGenerator(self.staging_base_dir)
            preview_gen.generate(job_id=job_id, auto_open=False)
        except Exception:
            logger.exception("Unable to generate staging preview for job %s", job_id)

        return job_dir
