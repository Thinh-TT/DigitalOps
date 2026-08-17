import re
from pathlib import Path


JOB_ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")


def validate_job_id(job_id: str) -> str:
    normalized = job_id.strip()
    if not JOB_ID_PATTERN.fullmatch(normalized):
        raise ValueError(
            "job_id must be 1-64 characters and contain only letters, digits, '.', '_' or '-'"
        )
    if normalized in {".", ".."}:
        raise ValueError("job_id cannot be '.' or '..'")
    return normalized


def resolve_job_dir(base_dir: Path | str, job_id: str) -> Path:
    """Resolve a job directory and guarantee it is a direct child of base_dir."""
    safe_job_id = validate_job_id(job_id)
    base = Path(base_dir).resolve()
    candidate = (base / safe_job_id).resolve()
    if candidate.parent != base:
        raise ValueError("job_id escapes the configured staging directory")
    return candidate
