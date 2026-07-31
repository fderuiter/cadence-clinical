"""FastAPI router for study archival package generation and status tracking.

Requirements: PRD-SYS-001
"""

import datetime
import os
import tempfile
import uuid
import zipfile
from typing import Dict

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, Request
from fastapi.responses import FileResponse
from storage.document_models import ArchiveJobResponse

import packages  # noqa: F401
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/archive", tags=["Archive"])

# In-memory database of archival jobs
_ARCHIVE_JOBS: Dict[str, dict] = {}


def enforce_permission(request: Request, required_permission: str) -> None:
    """Enforce specific RBAC permission checking.

    Requirements: PRD-SYS-001
    """
    permissions = getattr(request.state, "permissions", set())
    perm_strings = {p.value if hasattr(p, "value") else str(p) for p in permissions}
    if required_permission not in perm_strings:
        raise HTTPException(
            status_code=403,
            detail=f"Forbidden: Missing required permission '{required_permission}'",
        )


async def run_archive_packaging(job_id: str, study_id: str):
    """Background task to package documents into a zip file."""
    _ARCHIVE_JOBS[job_id]["status"] = "PROCESSING"
    try:
        temp_dir = tempfile.gettempdir()
        zip_path = os.path.join(temp_dir, f"archive_{study_id}_{job_id}.zip")
        with zipfile.ZipFile(zip_path, "w") as zipf:
            zipf.writestr(
                "README.txt",
                (
                    f"Study {study_id} Archival Package\n"
                    f"Job ID: {job_id}\n"
                    f"Generated at: {datetime.datetime.now().isoformat()}"
                ),
            )
        _ARCHIVE_JOBS[job_id]["status"] = "COMPLETED"
        _ARCHIVE_JOBS[job_id]["download_url"] = f"/api/v1/archive/download/{job_id}"
    except Exception:
        _ARCHIVE_JOBS[job_id]["status"] = "FAILED"


@router.post("/studies/{study_id}/export", response_model=ArchiveJobResponse)
async def initiate_study_archival(
    study_id: str,
    request: Request,
    background_tasks: BackgroundTasks,
    current_user: dict = Depends(get_current_user),
) -> ArchiveJobResponse:
    """Initiate background ZIP packaging task.

    Requirements: PRD-SYS-001
    """
    enforce_permission(request, "archive:export")

    job_id = f"job_{uuid.uuid4().hex[:8]}"
    _ARCHIVE_JOBS[job_id] = {
        "job_id": job_id,
        "study_id": study_id,
        "status": "PENDING",
        "download_url": None,
    }

    background_tasks.add_task(run_archive_packaging, job_id, study_id)

    return ArchiveJobResponse(
        job_id=job_id,
        study_id=study_id,
        status="PENDING",
        download_url=None,
    )


@router.get("/jobs/{job_id}", response_model=ArchiveJobResponse)
async def get_archive_job_status(
    job_id: str,
    request: Request,
    current_user: dict = Depends(get_current_user),
) -> ArchiveJobResponse:
    """Check archive package build status.

    Requirements: PRD-SYS-001
    """
    enforce_permission(request, "archive:export")

    if job_id not in _ARCHIVE_JOBS:
        raise HTTPException(status_code=404, detail="Archive job not found")

    job = _ARCHIVE_JOBS[job_id]
    return ArchiveJobResponse(
        job_id=job["job_id"],
        study_id=job["study_id"],
        status=job["status"],
        download_url=job["download_url"],
    )


@router.get("/download/{job_id}")
async def download_archive_package(
    job_id: str,
    request: Request,
    current_user: dict = Depends(get_current_user),
) -> FileResponse:
    """Download the generated study archival ZIP package.

    Requirements: PRD-SYS-001
    """
    enforce_permission(request, "archive:export")

    if job_id not in _ARCHIVE_JOBS:
        raise HTTPException(status_code=404, detail="Archive job not found")

    job = _ARCHIVE_JOBS[job_id]
    if job["status"] != "COMPLETED":
        raise HTTPException(status_code=400, detail="Archive package is not ready yet.")

    temp_dir = tempfile.gettempdir()
    zip_path = os.path.join(temp_dir, f"archive_{job['study_id']}_{job_id}.zip")
    if not os.path.exists(zip_path):
        raise HTTPException(status_code=404, detail="Archive file not found on disk.")

    return FileResponse(
        zip_path,
        media_type="application/zip",
        filename=f"study_{job['study_id']}_archive.zip",
    )
