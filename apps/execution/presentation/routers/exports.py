"""FastAPI router for CDISC dataset exports.

Requirements: PRD-SYS-009
"""

import os
from datetime import date, datetime
from typing import Any

import httpx
from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    BiostatExport,
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
    MigrationRule,
    SubjectConsent,
)
from packages.security import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    require_roles,
)

router = APIRouter(prefix="/api/v1/execution", tags=["CDISC Exports"])

CDISC_SIDECAR_URL = os.getenv("CDISC_SIDECAR_URL", "http://localhost:8000")


async def call_sidecar_sdtm(domain: str, payload: dict) -> dict:
    async with httpx.AsyncClient(timeout=60.0) as client:
        response = await client.post(
            f"{CDISC_SIDECAR_URL}/api/v1/cdisc/sdtm/{domain}", json=payload
        )
        response.raise_for_status()
        return response.json()


async def call_sidecar_adam(dataset: str, payload: dict) -> dict:
    async with httpx.AsyncClient(timeout=60.0) as client:
        response = await client.post(
            f"{CDISC_SIDECAR_URL}/api/v1/cdisc/adam/{dataset}", json=payload
        )
        response.raise_for_status()
        return response.json()


async def call_sidecar_bundle(payload: dict) -> dict:
    async with httpx.AsyncClient(timeout=60.0) as client:
        response = await client.post(
            f"{CDISC_SIDECAR_URL}/api/v1/cdisc/bundle", json=payload
        )
        response.raise_for_status()
        return response.json()


def model_to_dict(obj: Any) -> dict[str, Any]:
    if obj is None:
        return {}

    data = {}
    if hasattr(obj, "__table__"):
        for col in obj.__table__.columns:
            val = getattr(obj, col.name)
            if isinstance(val, (datetime, date)):
                data[col.name] = val.isoformat()
            else:
                data[col.name] = val
    else:
        for k, v in obj.__dict__.items():
            if k.startswith("_"):
                continue
            if isinstance(v, (datetime, date)):
                data[k] = v.isoformat()
            else:
                data[k] = v

    # Add extra dynamic properties accessed in mappings
    for prop in [
        "actarm",
        "ACTARM",
        "randomization_date",
        "randt",
        "end_of_study_date",
        "eosdt",
        "death_date",
        "dthdtc",
        "rfstdtc",
        "RFSTDTC",
        "rfendtc",
        "RFENDTC",
    ]:
        if hasattr(obj, prop) and prop not in data:
            val = getattr(obj, prop)
            if isinstance(val, (datetime, date)):
                data[prop] = val.isoformat()
            else:
                data[prop] = val

    return data


async def fetch_cdisc_input_data(session: Any, study_id: str) -> dict[str, Any]:
    # 1. Subjects
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subj = await session.execute(stmt_subj)
    subjects = list(res_subj.scalars().all())

    # 2. Observations
    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.is_deleted.is_(False),
    )
    res_obs = await session.execute(stmt_obs)
    observations = list(res_obs.scalars().all())

    # 3. Visits
    stmt_visit = select(ClinicalVisit).where(
        ClinicalVisit.study_id == study_id,
        ClinicalVisit.is_deleted.is_(False),
    )
    res_visit = await session.execute(stmt_visit)
    visits = list(res_visit.scalars().all())

    # 4. Migration rules
    stmt_rules = select(MigrationRule).where(
        MigrationRule.study_id == study_id,
        MigrationRule.is_deleted.is_(False),
    )
    res_rules = await session.execute(stmt_rules)
    migration_rules = list(res_rules.scalars().all())

    # 5. Target version (consent)
    stmt_target_version = (
        select(SubjectConsent.version_tag)
        .where(SubjectConsent.study_id == study_id)
        .order_by(SubjectConsent.version_index.desc())
        .limit(1)
    )
    res_target = await session.execute(stmt_target_version)
    target_version = res_target.scalar() or "1.0"

    return {
        "subjects": subjects,
        "observations": observations,
        "visits": visits,
        "migration_rules": migration_rules,
        "target_version": target_version,
    }


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

    # 1. Fetch raw data from DB
    async with db_manager.get_session_maker()() as session:
        db_data = await fetch_cdisc_input_data(session, study_id)
        await session.commit()
        await session.close()

    # 2. Serialize raw database records into clean, plain JSON payloads
    payload = {
        "study_id": study_id,
        "subjects": [model_to_dict(s) for s in db_data["subjects"]],
        "observations": [model_to_dict(o) for o in db_data["observations"]],
        "visits": [model_to_dict(v) for v in db_data["visits"]],
        "migration_rules": [model_to_dict(r) for r in db_data["migration_rules"]],
        "target_version": db_data["target_version"],
    }

    # 3. Dispatch a synchronous HTTP request to the sidecar service
    try:
        dataset_json_dict = await call_sidecar_sdtm(dom_upper, payload)

        # Log SUCCESS
        export_log = BiostatExport(
            study_id=study_id,
            export_type="SDTM",
            dataset_name=dom_upper,
            status="SUCCESS",
        )
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

        return dataset_json_dict

    except httpx.HTTPStatusError as e:
        scrubbed_msg = e.response.text
        try:
            err_detail = e.response.json().get("detail", scrubbed_msg)
            if isinstance(err_detail, list):
                err_detail = str(err_detail)
            scrubbed_msg = err_detail
        except Exception:
            pass

        export_log = BiostatExport(
            study_id=study_id,
            export_type="SDTM",
            dataset_name=dom_upper,
            status="FAILED",
            error_message=scrubbed_msg,
        )
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

        raise HTTPException(
            status_code=e.response.status_code,
            detail=scrubbed_msg,
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
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

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

    # 1. Fetch raw data from DB
    async with db_manager.get_session_maker()() as session:
        db_data = await fetch_cdisc_input_data(session, study_id)
        await session.commit()
        await session.close()

    # 2. Serialize raw database records into clean, plain JSON payloads
    payload = {
        "study_id": study_id,
        "subjects": [model_to_dict(s) for s in db_data["subjects"]],
        "observations": [model_to_dict(o) for o in db_data["observations"]],
        "visits": [model_to_dict(v) for v in db_data["visits"]],
        "migration_rules": [model_to_dict(r) for r in db_data["migration_rules"]],
        "target_version": db_data["target_version"],
    }

    # 3. Dispatch a synchronous HTTP request to the sidecar service
    try:
        dataset_json_dict = await call_sidecar_adam(ds_upper, payload)

        # Log SUCCESS
        export_log = BiostatExport(
            study_id=study_id,
            export_type="ADaM",
            dataset_name=ds_upper,
            status="SUCCESS",
        )
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

        return dataset_json_dict

    except httpx.HTTPStatusError as e:
        scrubbed_msg = e.response.text
        try:
            err_detail = e.response.json().get("detail", scrubbed_msg)
            if isinstance(err_detail, list):
                err_detail = str(err_detail)
            scrubbed_msg = err_detail
        except Exception:
            pass

        export_log = BiostatExport(
            study_id=study_id,
            export_type="ADaM",
            dataset_name=ds_upper,
            status="FAILED",
            error_message=scrubbed_msg,
        )
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

        raise HTTPException(
            status_code=e.response.status_code,
            detail=scrubbed_msg,
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
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

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
    # 1. Fetch raw data from DB
    async with db_manager.get_session_maker()() as session:
        db_data = await fetch_cdisc_input_data(session, study_id)
        await session.commit()
        await session.close()

    # 2. Serialize raw database records into clean, plain JSON payloads
    payload = {
        "study_id": study_id,
        "subjects": [model_to_dict(s) for s in db_data["subjects"]],
        "observations": [model_to_dict(o) for o in db_data["observations"]],
        "visits": [model_to_dict(v) for v in db_data["visits"]],
        "migration_rules": [model_to_dict(r) for r in db_data["migration_rules"]],
        "target_version": db_data["target_version"],
    }

    # 3. Dispatch a synchronous HTTP request to the sidecar service
    try:
        dataset_json_dict = await call_sidecar_bundle(payload)

        # Log SUCCESS
        export_log = BiostatExport(
            study_id=study_id,
            export_type="BUNDLE",
            dataset_name=None,
            status="SUCCESS",
        )
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

        return dataset_json_dict

    except httpx.HTTPStatusError as e:
        scrubbed_msg = e.response.text
        try:
            err_detail = e.response.json().get("detail", scrubbed_msg)
            if isinstance(err_detail, list):
                err_detail = str(err_detail)
            scrubbed_msg = err_detail
        except Exception:
            pass

        export_log = BiostatExport(
            study_id=study_id,
            export_type="BUNDLE",
            dataset_name=None,
            status="FAILED",
            error_message=scrubbed_msg,
        )
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

        raise HTTPException(
            status_code=e.response.status_code,
            detail=scrubbed_msg,
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
        async with db_manager.get_session_maker()() as log_session:
            log_session.add(export_log)
            await log_session.commit()

        raise HTTPException(
            status_code=500, detail=f"Export execution failed: {scrubbed_msg}"
        )
