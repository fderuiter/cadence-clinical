import json
import os
import uuid
from datetime import datetime
from typing import Any, Optional

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, Query, Response
from fastapi.responses import FileResponse
from pydantic import BaseModel, Field
from sqlalchemy import select

from apps.execution.biostat import (
    DatasetJSONValidationError,
    derive_adae,
    derive_adsl,
    derive_advs,
    extract_ae,
    extract_dm,
    extract_lb,
    extract_mh,
    extract_vs,
    serialize_to_dataset_json,
    validate_dataset_json,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    BiostatExport,
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
    DatasetExportJob,
    SubjectConsent,
)
from packages.security import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    require_roles,
)
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution", tags=["CDISC Exports"])


class ExportTriggerRequest(BaseModel):
    study_id: str = Field(..., description="The unique study identifier")
    dataset_name: str = Field(
        default="BUNDLE",
        description="The dataset name (e.g., BUNDLE, ADSL, ADAE, ADVS, DM, etc.)",
    )


class JobStatusResponse(BaseModel):
    id: str
    study_id: Optional[str]
    dataset_name: Optional[str]
    status: str
    progress: int
    download_url: Optional[str] = None
    error_message: Optional[str] = None


async def run_sdtm_extraction(
    session: Any, study_id: str, domain: str
) -> tuple[list[dict], list[Any]]:
    """Helper to retrieve and transform raw observations to SDTM records."""
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subj = await session.execute(stmt_subj)
    subjects = res_subj.scalars().all()

    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.is_deleted.is_(False),
    )
    res_obs = await session.execute(stmt_obs)
    observations = list(res_obs.scalars().all())

    # Dynamic non-destructive protocol reconciliation
    from apps.execution.migration_rules import reconcile_observations

    stmt_target_version = (
        select(SubjectConsent.version_tag)
        .where(SubjectConsent.study_id == study_id)
        .order_by(SubjectConsent.version_index.desc())
        .limit(1)
    )
    res_target = await session.execute(stmt_target_version)
    target_version = res_target.scalar() or "1.0"
    observations = await reconcile_observations(session, observations, target_version)

    dom_upper = domain.strip().upper()
    records = []
    supp_records = []
    if dom_upper == "DM":
        records = extract_dm(subjects, observations)
    elif dom_upper == "AE":
        records, supp_records = extract_ae(subjects, observations)
    elif dom_upper == "VS":
        records, supp_records = extract_vs(subjects, observations)
    elif dom_upper == "LB":
        records, supp_records = extract_lb(subjects, observations)
    elif dom_upper == "MH":
        records, supp_records = extract_mh(subjects, observations)
    elif dom_upper == "CM":
        from apps.execution.sdtm_mapper import map_cm

        stmt_visit = select(ClinicalVisit).where(
            ClinicalVisit.study_id == study_id,
            ClinicalVisit.is_deleted.is_(False),
        )
        res_visit = await session.execute(stmt_visit)
        visits = res_visit.scalars().all()
        cm_models = map_cm(subjects, visits, observations)
        records = [
            cm.model_dump() if hasattr(cm, "model_dump") else cm.dict()
            for cm in cm_models
        ]
    else:
        raise ValueError(f"Unsupported SDTM domain: {domain}")

    for r in records:
        if "DOMAIN" not in r:
            r["DOMAIN"] = dom_upper
    return records, supp_records


async def run_adam_derivation(session: Any, study_id: str, dataset: str) -> list[dict]:
    """Helper to retrieve and derive ADaM analysis records."""
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subj = await session.execute(stmt_subj)
    subjects = res_subj.scalars().all()

    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.is_deleted.is_(False),
    )
    res_obs = await session.execute(stmt_obs)
    observations = list(res_obs.scalars().all())

    # Dynamic non-destructive protocol reconciliation
    from apps.execution.migration_rules import reconcile_observations

    stmt_target_version = (
        select(SubjectConsent.version_tag)
        .where(SubjectConsent.study_id == study_id)
        .order_by(SubjectConsent.version_index.desc())
        .limit(1)
    )
    res_target = await session.execute(stmt_target_version)
    target_version = res_target.scalar() or "1.0"
    observations = await reconcile_observations(session, observations, target_version)

    ds_upper = dataset.strip().upper()
    if ds_upper == "ADSL":
        return derive_adsl(subjects, observations)
    if ds_upper == "ADAE":
        adsl_recs = derive_adsl(subjects, observations)
        ae_recs, _ = extract_ae(subjects, observations)
        records = derive_adae(adsl_recs, ae_recs)
        for r in records:
            if "AEDECOD" not in r or r["AEDECOD"] is None:
                r["AEDECOD"] = r.get("AETERM", "")
        return records
    if ds_upper == "ADVS":
        adsl_recs = derive_adsl(subjects, observations)
        vs_recs, _ = extract_vs(subjects, observations)
        return derive_advs(adsl_recs, vs_recs)
    raise ValueError(f"Unsupported ADaM dataset: {dataset}")


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


# 1. Asynchronous Export Endpoints
@router.post("/exports", status_code=202)
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


@router.get("/exports/{job_id}", response_model=JobStatusResponse)
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


@router.get("/exports/{job_id}/download")
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


# 2. Synchronous Export Endpoints (for backward compatibility / baseline compliance)
@router.get("/biostat/sdtm/{domain}")
async def export_sdtm_domain(
    domain: str,
    study_id: str = Query(..., description="The unique study identifier"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> dict:
    """Exports SDTM domain data (DM, AE, VS, LB, MH, CM) in CDISC Dataset-JSON format."""
    dom_upper = domain.strip().upper()
    valid_domains = {"DM", "AE", "VS", "LB", "MH", "CM"}
    if dom_upper not in valid_domains:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported SDTM domain: '{domain}'. Must be one of {sorted(list(valid_domains))}",
        )

    async with db_manager.get_session_maker()() as session:
        try:
            records, supp_records = await run_sdtm_extraction(
                session, study_id, dom_upper
            )
            export_data = {dom_upper: records}
            if supp_records:
                export_data[f"SUPP{dom_upper}"] = supp_records

            # Apply deterministic de-identification transform
            salt = os.getenv(
                "BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765"
            )  # nosec B105: mock fallback secret
            from apps.execution.biostat.deid import deidentify_export_data

            export_data = deidentify_export_data(export_data, salt)

            dataset_json = serialize_to_dataset_json(
                data=export_data, study_id=study_id
            )
            validate_dataset_json(dataset_json)

            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM",
                dataset_name=dom_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()

            return dataset_json.model_dump()
        except DatasetJSONValidationError as e:
            from apps.execution.biostat.deid import scrub_error_message

            scrubbed_msg = scrub_error_message(str(e))
            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM",
                dataset_name=dom_upper,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=422,
                detail=f"Dataset-JSON validation failed: {scrubbed_msg}",
            )
        except Exception as e:
            from apps.execution.biostat.deid import scrub_error_message

            scrubbed_msg = scrub_error_message(str(e))
            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM",
                dataset_name=dom_upper,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=500, detail=f"Export execution failed: {scrubbed_msg}"
            )


@router.get("/biostat/adam/{dataset}")
async def export_adam_dataset(
    dataset: str,
    study_id: str = Query(..., description="The unique study identifier"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> dict:
    """Exports ADaM dataset data (ADSL, ADAE, ADVS) in CDISC Dataset-JSON format."""
    ds_upper = dataset.strip().upper()
    valid_datasets = {"ADSL", "ADAE", "ADVS"}
    if ds_upper not in valid_datasets:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported ADaM dataset: '{dataset}'. Must be one of {sorted(list(valid_datasets))}",
        )

    async with db_manager.get_session_maker()() as session:
        try:
            records = await run_adam_derivation(session, study_id, ds_upper)

            # Apply deterministic de-identification transform
            salt = os.getenv(
                "BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765"
            )  # nosec B105: mock fallback secret
            from apps.execution.biostat.deid import deidentify_export_data

            deidentified_records = deidentify_export_data(records, salt)

            dataset_json = serialize_to_dataset_json(
                data={ds_upper: deidentified_records}, study_id=study_id
            )
            validate_dataset_json(dataset_json)

            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM",
                dataset_name=ds_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()

            return dataset_json.model_dump()
        except DatasetJSONValidationError as e:
            from apps.execution.biostat.deid import scrub_error_message

            scrubbed_msg = scrub_error_message(str(e))
            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM",
                dataset_name=ds_upper,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=422,
                detail=f"Dataset-JSON validation failed: {scrubbed_msg}",
            )
        except Exception as e:
            from apps.execution.biostat.deid import scrub_error_message

            scrubbed_msg = scrub_error_message(str(e))
            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM",
                dataset_name=ds_upper,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=500, detail=f"Export execution failed: {scrubbed_msg}"
            )


@router.get("/biostat/bundle")
async def export_biostat_bundle(
    study_id: str = Query(..., description="The unique study identifier"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> dict:
    """Exports all SDTM domains and ADaM datasets bundled in a single CDISC Dataset-JSON document."""
    async with db_manager.get_session_maker()() as session:
        try:
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

            # Apply deterministic de-identification transform
            salt = os.getenv(
                "BIOSTAT_EXPORT_SALT", "secure-clinical-salt-98765"
            )  # nosec B105: mock fallback secret
            from apps.execution.biostat.deid import deidentify_export_data

            bundle_data = deidentify_export_data(bundle_data, salt)

            dataset_json = serialize_to_dataset_json(
                data=bundle_data, study_id=study_id
            )
            validate_dataset_json(dataset_json)

            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE",
                dataset_name=None,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()

            return dataset_json.model_dump()
        except DatasetJSONValidationError as e:
            from apps.execution.biostat.deid import scrub_error_message

            scrubbed_msg = scrub_error_message(str(e))
            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE",
                dataset_name=None,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=422,
                detail=f"Dataset-JSON validation failed: {scrubbed_msg}",
            )
        except Exception as e:
            from apps.execution.biostat.deid import scrub_error_message

            scrubbed_msg = scrub_error_message(str(e))
            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE",
                dataset_name=None,
                status="FAILED",
                error_message=scrubbed_msg,
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=500, detail=f"Export execution failed: {scrubbed_msg}"
            )
