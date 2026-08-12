import json
import os
import uuid
from typing import Any

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, Response
from fastapi.responses import FileResponse
from pydantic import BaseModel, Field
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import BiostatExport, DatasetExportJob
from packages.security import ROLE_CRA, ROLE_DATA_MANAGER
from packages.security.middleware import get_current_user
from packages.security.rbac import require_roles

router = APIRouter(prefix="/api/v1/execution/exports", tags=["Exports"])


class ExportTriggerRequest(BaseModel):
    study_id: str = Field(..., description="The unique study identifier")
    dataset_name: str = Field(
        default="BUNDLE",
        description="The dataset name (e.g., BUNDLE, ADSL, ADAE, ADVS, DM, etc.)",
    )


class JobStatusResponse(BaseModel):
    id: str
    study_id: str | None
    dataset_name: str | None
    status: str
    progress: int
    download_url: str | None = None
    error_message: str | None = None


async def run_dataset_export_task(
    job_id: str, study_id: str, dataset_name: str
) -> None:
    session_maker = db_manager.get_session_maker()

    # 1. Update status to PROCESSING
    async with session_maker() as session:
        stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
        res = await session.execute(stmt)
        job = res.scalars().first()
        if not job:
            return

        job.status = "PROCESSING"
        job.progress = 10
        await session.commit()

    try:
        # Dynamically import the execution functions to avoid circular imports
        from apps.execution.biostat import (
            DatasetJSONValidationError,
            serialize_to_dataset_json,
            validate_dataset_json,
        )
        from apps.execution.biostat.deid import (
            deidentify_export_data,
            scrub_error_message,
        )
        from apps.execution.main import run_adam_derivation, run_sdtm_extraction

        # Determine the export type and dataset name
        ds_upper = dataset_name.strip().upper()
        export_type = "BUNDLE"
        export_ds = None

        if ds_upper == "BUNDLE":
            export_type = "BUNDLE"
            export_ds = None
        elif ds_upper in {"ADSL", "ADAE", "ADVS"}:
            export_type = "ADaM"
            export_ds = ds_upper
        else:
            export_type = "SDTM"
            export_ds = ds_upper

        # 2. Extract/derive the data
        async with session_maker() as session:
            if export_type == "BUNDLE":
                # Generate bundle
                bundle_data = {}
                for dom in ["DM", "AE", "VS", "LB", "MH", "CM"]:
                    records, supp_records = await run_sdtm_extraction(
                        session, study_id, dom
                    )
                    if records:
                        bundle_data[dom] = records
                    if supp_records:
                        bundle_data[f"SUPP{dom}"] = supp_records
                for ds in ["ADSL", "ADAE", "ADVS"]:
                    records = await run_adam_derivation(session, study_id, ds)
                    if records:
                        bundle_data[ds] = records

                if not bundle_data:
                    raise HTTPException(
                        status_code=404,
                        detail="No biostat records found for the given study.",
                    )

                export_data = bundle_data
            elif export_type == "ADaM":
                records = await run_adam_derivation(session, study_id, export_ds)
                export_data = {export_ds: records}
            else:
                # SDTM domain extraction
                records, supp_records = await run_sdtm_extraction(
                    session, study_id, export_ds
                )
                export_data = {}
                if records:
                    export_data[export_ds] = records
                if supp_records:
                    export_data[f"SUPP{export_ds}"] = supp_records

                if not export_data:
                    raise HTTPException(
                        status_code=404,
                        detail=f"No SDTM records found for domain '{export_ds}' in study.",
                    )

            # Update progress
            stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
            res = await session.execute(stmt)
            job = res.scalars().first()
            if job:
                job.progress = 50
                await session.commit()

            # Apply de-identification
            salt = os.getenv("BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765")  # nosec B105: mock fallback secret
            deidentified_data = deidentify_export_data(export_data, salt)

            # Update progress
            stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
            res = await session.execute(stmt)
            job = res.scalars().first()
            if job:
                job.progress = 70
                await session.commit()

            # Serialize and Validate
            dataset_json = serialize_to_dataset_json(
                data=deidentified_data, study_id=study_id
            )
            validate_dataset_json(dataset_json)

            # Update progress
            stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
            res = await session.execute(stmt)
            job = res.scalars().first()
            if job:
                job.progress = 90
                await session.commit()

            # Save the file to local exports directory
            exports_dir = "/app/exports"
            os.makedirs(exports_dir, exist_ok=True)
            file_path = os.path.join(exports_dir, f"{job_id}.json")

            with open(file_path, "w", encoding="utf-8") as f:
                json.dump(dataset_json.model_dump(), f, indent=2)

            # Record a successful BiostatExport log
            export_log = BiostatExport(
                study_id=study_id,
                export_type=export_type,
                dataset_name=export_ds,
                status="SUCCESS",
            )
            session.add(export_log)

            # Mark job as COMPLETED
            stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
            res = await session.execute(stmt)
            job = res.scalars().first()
            if job:
                job.status = "COMPLETED"
                job.progress = 100
                job.download_url = f"/api/v1/execution/exports/{job_id}/download"
                job.file_path = file_path
                await session.commit()

    except DatasetJSONValidationError as e:
        scrubbed_msg = scrub_error_message(str(e))
        async with session_maker() as session:
            # Record a failed BiostatExport log
            export_log = BiostatExport(
                study_id=study_id,
                export_type=export_type,
                dataset_name=export_ds,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)

            # Update job to FAILED
            stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
            res = await session.execute(stmt)
            job = res.scalars().first()
            if job:
                job.status = "FAILED"
                job.progress = 100
                job.error_message = f"Dataset-JSON validation failed: {scrubbed_msg}"
                await session.commit()
    except Exception as e:
        scrubbed_msg = scrub_error_message(str(e))
        async with session_maker() as session:
            # Record a failed BiostatExport log
            export_log = BiostatExport(
                study_id=study_id,
                export_type=export_type,
                dataset_name=export_ds,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)

            # Update job to FAILED
            stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
            res = await session.execute(stmt)
            job = res.scalars().first()
            if job:
                job.status = "FAILED"
                job.progress = 100
                job.error_message = f"Export execution failed: {scrubbed_msg}"
                await session.commit()


@router.post("", status_code=202)
async def trigger_dataset_export(
    payload: ExportTriggerRequest,
    background_tasks: BackgroundTasks,
    current_user: dict = Depends(get_current_user),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> dict[str, Any]:
    """Trigger an asynchronous CDISC Dataset-JSON export and validation task.

    Returns HTTP 202 status and unique job ID immediately.
    """
    job_id = str(uuid.uuid4())

    async with db_manager.get_session_maker()() as session:
        job = DatasetExportJob(
            id=job_id,
            status="PENDING",
            progress=0,
            study_id=payload.study_id,
            dataset_name=payload.dataset_name,
            initiated_by=current_user.get("sub", "anonymous"),
        )
        session.add(job)
        await session.commit()

    background_tasks.add_task(
        run_dataset_export_task,
        job_id=job_id,
        study_id=payload.study_id,
        dataset_name=payload.dataset_name,
    )

    return {
        "job_id": job_id,
        "status": "PENDING",
    }


@router.get("/{job_id}", response_model=JobStatusResponse)
async def get_export_job_status(
    job_id: str,
    current_user: dict = Depends(get_current_user),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> JobStatusResponse:
    """Retrieve the status and progress of an active/completed export job."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
        res = await session.execute(stmt)
        job = res.scalars().first()
        if not job:
            raise HTTPException(status_code=404, detail="Export job not found")

        return JobStatusResponse(
            id=job.id,
            study_id=job.study_id,
            dataset_name=job.dataset_name,
            status=job.status,
            progress=job.progress,
            download_url=job.download_url if job.status == "COMPLETED" else None,
            error_message=job.error_message,
        )


@router.get("/{job_id}/download")
async def download_exported_file(
    job_id: str,
    current_user: dict = Depends(get_current_user),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> Response:
    """Download the final exported CDISC Dataset-JSON file if the job completed successfully."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(DatasetExportJob).where(DatasetExportJob.id == job_id)
        res = await session.execute(stmt)
        job = res.scalars().first()
        if not job:
            raise HTTPException(status_code=404, detail="Export job not found")

        if job.status != "COMPLETED" or not job.file_path:
            raise HTTPException(
                status_code=400,
                detail=f"Export job is in status '{job.status}' and cannot be downloaded.",
            )

        if not os.path.exists(job.file_path):
            raise HTTPException(
                status_code=404,
                detail="The generated export file was not found on the server.",
            )

        return FileResponse(
            job.file_path,
            media_type="application/json",
            filename=f"{job.id}.json",
        )
