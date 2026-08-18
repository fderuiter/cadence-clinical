"""FastAPI router for CDISC and Regulatory Biostatistical Dataset Exports.

Requirements: PRD-SYS-001, PRD-SYS-004, PRD-CRF-008, Trace-1, Trace-7, Trace-12
"""

import os
from typing import Any, cast

from fastapi import APIRouter, Depends, HTTPException, Query, Response
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
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
    serialize_bundle_to_csv_zip,
    serialize_to_csv,
    serialize_to_dataset_json,
    serialize_to_odm_xml,
    validate_dataset_json,
    write_xpt,
)
from apps.execution.biostat.deid import (
    deidentify_export_data,
    scrub_error_message,
)
from apps.execution.database.models import (
    BiostatExport,
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
    SubjectConsent,
)
from apps.execution.presentation.routers.exports_schemas import ExportBundleRequest
from packages.security import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    ROLE_SPONSOR_ADMIN,
    require_roles,
)

router = APIRouter(prefix="/api/v1/execution", tags=["CDISC Exports"])


async def run_sdtm_extraction(
    session: Any,
    study_id: str,
    domain: str,
    site_ids: list[str] | None = None,
    cohorts: list[str] | None = None,
) -> tuple[list[dict], list[Any]]:
    """Helper to retrieve and transform raw observations to SDTM records."""
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    if site_ids:
        stmt_subj = stmt_subj.where(ClinicalSubject.site_id.in_(site_ids))
    res_subj = await session.execute(stmt_subj)
    subjects = res_subj.scalars().all()

    subj_ids = [s.subject_id for s in subjects]
    if not subj_ids:
        return [], []

    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.subject_id.in_(subj_ids),
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
    records: list[Any] = []
    supp_records: list[Any] = []
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

    # Optional cohort filter on ARM
    if cohorts and records:
        cohort_set = set(c.upper() for c in cohorts)
        records = [r for r in records if str(r.get("ARM", "")).upper() in cohort_set]

    for r in records:
        if "DOMAIN" not in r:
            r["DOMAIN"] = dom_upper
    return records, supp_records


async def run_adam_derivation(
    session: Any,
    study_id: str,
    dataset: str,
    site_ids: list[str] | None = None,
    cohorts: list[str] | None = None,
) -> list[dict]:
    """Helper to retrieve and derive ADaM analysis records."""
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    if site_ids:
        stmt_subj = stmt_subj.where(ClinicalSubject.site_id.in_(site_ids))
    res_subj = await session.execute(stmt_subj)
    subjects = res_subj.scalars().all()

    subj_ids = [s.subject_id for s in subjects]
    if not subj_ids:
        return []

    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.subject_id.in_(subj_ids),
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
    records: list[dict] = []
    if ds_upper == "ADSL":
        records = derive_adsl(subjects, observations)
    elif ds_upper == "ADAE":
        adsl_recs = derive_adsl(subjects, observations)
        ae_recs, _ = extract_ae(subjects, observations)
        records = derive_adae(adsl_recs, ae_recs)
        for r in records:
            if "AEDECOD" not in r or r["AEDECOD"] is None:
                r["AEDECOD"] = r.get("AETERM", "")
    elif ds_upper == "ADVS":
        adsl_recs = derive_adsl(subjects, observations)
        vs_recs, _ = extract_vs(subjects, observations)
        records = derive_advs(adsl_recs, vs_recs)
    else:
        raise ValueError(f"Unsupported ADaM dataset: {dataset}")

    if cohorts and records:
        cohort_set = set(c.upper() for c in cohorts)
        records = [r for r in records if str(r.get("ARM", "")).upper() in cohort_set]

    return records


@router.get("/biostat/sdtm/{domain}")
async def export_sdtm_domain(
    domain: str,
    study_id: str = Query(..., description="The unique study identifier"),
    format: str = Query(
        "json", description="Target export format: json, xpt, odm, csv"
    ),
    version: str = Query(
        "v5", description="SAS XPT version if format is xpt (v5 or v8)"
    ),
    privacy_profile: str = Query(
        "SAFE_HARBOR",
        description="Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED",
    ),
    salt: str | None = Query(None, description="HMAC pseudonymization salt"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA,
            ROLE_DATA_MANAGER,
            ROLE_SPONSOR_ADMIN,
            "sponsor_statistician",
            "statistician",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> Any:
    """Exports SDTM domain data (DM, AE, VS, LB, MH, CM) in Dataset-JSON, SAS XPT, ODM-XML, or CSV format."""
    dom_upper = domain.strip().upper()
    valid_domains = {"DM", "AE", "VS", "LB", "MH", "CM"}
    if dom_upper not in valid_domains:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported SDTM domain: '{domain}'. Must be one of {sorted(list(valid_domains))}",
        )

    fmt_clean = format.strip().lower()
    actual_salt: str = (
        salt or os.getenv("BIOSTAT_EXPORT_SALT") or "secure-clinical-salt-98765"
    )  # pragma: allowlist secret

    try:
        records, supp_records = await run_sdtm_extraction(session, study_id, dom_upper)
        export_data = {dom_upper: records}
        if supp_records:
            export_data[f"SUPP{dom_upper}"] = supp_records

        # Apply deterministic de-identification
        if privacy_profile.upper() != "UNRESTRICTED":
            export_data = cast(
                dict[str, list[dict[str, Any]]],
                deidentify_export_data(export_data, actual_salt),
            )
            records = export_data.get(dom_upper, [])

        if fmt_clean in ("xpt", "sas_xpt"):
            xpt_bytes = write_xpt(
                dataset_name=dom_upper,
                records=records,
                version=version,
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM_XPT",
                dataset_name=dom_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=xpt_bytes,
                media_type="application/x-sas-xport",
                headers={
                    "Content-Disposition": f"attachment; filename={dom_upper.lower()}.xpt"
                },
            )

        if fmt_clean in ("odm", "xml", "odm_xml"):
            xml_str = serialize_to_odm_xml(
                study_id=study_id,
                data=export_data,
                audit_user="system_exporter",
                change_reason="Regulatory Submission Export",
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM_ODM",
                dataset_name=dom_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=xml_str,
                media_type="application/xml",
                headers={
                    "Content-Disposition": f"attachment; filename={dom_upper.lower()}_odm.xml"
                },
            )

        if fmt_clean in ("csv", "text_csv"):
            csv_str = serialize_to_csv(
                records=records,
                privacy_profile=privacy_profile,
                salt=actual_salt,
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM_CSV",
                dataset_name=dom_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=csv_str,
                media_type="text/csv",
                headers={
                    "Content-Disposition": f"attachment; filename={dom_upper.lower()}.csv"
                },
            )

        # Default: Dataset-JSON format
        dataset_json = serialize_to_dataset_json(data=export_data, study_id=study_id)
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

    except HTTPException:
        raise
    except DatasetJSONValidationError as e:
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
            status_code=500,
            detail=f"Export execution failed: {scrubbed_msg}",
        )


@router.get("/biostat/adam/{dataset}")
async def export_adam_dataset(
    dataset: str,
    study_id: str = Query(..., description="The unique study identifier"),
    format: str = Query(
        "json", description="Target export format: json, xpt, odm, csv"
    ),
    version: str = Query(
        "v5", description="SAS XPT version if format is xpt (v5 or v8)"
    ),
    privacy_profile: str = Query(
        "SAFE_HARBOR",
        description="Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED",
    ),
    salt: str | None = Query(None, description="HMAC pseudonymization salt"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA,
            ROLE_DATA_MANAGER,
            ROLE_SPONSOR_ADMIN,
            "sponsor_statistician",
            "statistician",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> Any:
    """Exports ADaM dataset data (ADSL, ADAE, ADVS) in Dataset-JSON, SAS XPT, ODM-XML, or CSV format."""
    ds_upper = dataset.strip().upper()
    valid_datasets = {"ADSL", "ADAE", "ADVS"}
    if ds_upper not in valid_datasets:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported ADaM dataset: '{dataset}'. Must be one of {sorted(list(valid_datasets))}",
        )

    fmt_clean = format.strip().lower()
    actual_salt: str = (
        salt or os.getenv("BIOSTAT_EXPORT_SALT") or "secure-clinical-salt-98765"
    )  # pragma: allowlist secret

    try:
        records = await run_adam_derivation(session, study_id, ds_upper)

        # Apply deterministic de-identification
        if privacy_profile.upper() != "UNRESTRICTED":
            records = cast(
                list[dict[str, Any]], deidentify_export_data(records, actual_salt)
            )

        if fmt_clean in ("xpt", "sas_xpt"):
            xpt_bytes = write_xpt(
                dataset_name=ds_upper,
                records=records,
                version=version,
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM_XPT",
                dataset_name=ds_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=xpt_bytes,
                media_type="application/x-sas-xport",
                headers={
                    "Content-Disposition": f"attachment; filename={ds_upper.lower()}.xpt"
                },
            )

        if fmt_clean in ("odm", "xml", "odm_xml"):
            xml_str = serialize_to_odm_xml(
                study_id=study_id,
                data={ds_upper: records},
                audit_user="system_exporter",
                change_reason="Regulatory Submission Export",
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM_ODM",
                dataset_name=ds_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=xml_str,
                media_type="application/xml",
                headers={
                    "Content-Disposition": f"attachment; filename={ds_upper.lower()}_odm.xml"
                },
            )

        if fmt_clean in ("csv", "text_csv"):
            csv_str = serialize_to_csv(
                records=records,
                privacy_profile=privacy_profile,
                salt=actual_salt,
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM_CSV",
                dataset_name=ds_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=csv_str,
                media_type="text/csv",
                headers={
                    "Content-Disposition": f"attachment; filename={ds_upper.lower()}.csv"
                },
            )

        # Default: Dataset-JSON format
        dataset_json = serialize_to_dataset_json(
            data={ds_upper: records}, study_id=study_id
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

    except HTTPException:
        raise
    except DatasetJSONValidationError as e:
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
            status_code=500,
            detail=f"Export execution failed: {scrubbed_msg}",
        )


@router.get("/biostat/bundle")
async def export_biostat_bundle(
    study_id: str = Query(..., description="The unique study identifier"),
    format: str = Query(
        "json", description="Target export format: json, zip, csv_zip, odm"
    ),
    privacy_profile: str = Query(
        "SAFE_HARBOR",
        description="Privacy policy: SAFE_HARBOR, LIMITED_DATA_SET, GDPR_PSEUDONYMIZED, UNRESTRICTED",
    ),
    salt: str | None = Query(None, description="HMAC pseudonymization salt"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA,
            ROLE_DATA_MANAGER,
            ROLE_SPONSOR_ADMIN,
            "sponsor_statistician",
            "statistician",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> Any:
    """Exports all SDTM domains and ADaM datasets bundled in Dataset-JSON, CSV ZIP, or ODM-XML."""
    fmt_clean = format.strip().lower()
    actual_salt: str = (
        salt or os.getenv("BIOSTAT_EXPORT_SALT") or "secure-clinical-salt-98765"
    )  # pragma: allowlist secret

    try:
        bundle_data = {}
        for dom in ["DM", "AE", "VS", "LB", "MH", "CM"]:
            records, supp_records = await run_sdtm_extraction(session, study_id, dom)
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

        # Apply deterministic de-identification
        if privacy_profile.upper() != "UNRESTRICTED":
            bundle_data = cast(
                dict[str, list[dict[str, Any]]],
                deidentify_export_data(bundle_data, actual_salt),
            )

        if fmt_clean in ("zip", "csv_zip"):
            zip_bytes = serialize_bundle_to_csv_zip(
                bundle_data=bundle_data,
                privacy_profile=privacy_profile,
                salt=actual_salt,
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE_CSV_ZIP",
                dataset_name=None,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=zip_bytes,
                media_type="application/zip",
                headers={
                    "Content-Disposition": f"attachment; filename={study_id.lower()}_datasets.zip"
                },
            )

        if fmt_clean in ("odm", "xml", "odm_xml"):
            xml_str = serialize_to_odm_xml(
                study_id=study_id,
                data=bundle_data,
                audit_user="system_exporter",
                change_reason="Regulatory Submission Export",
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE_ODM",
                dataset_name=None,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=xml_str,
                media_type="application/xml",
                headers={
                    "Content-Disposition": f"attachment; filename={study_id.lower()}_odm.xml"
                },
            )

        # Default: Dataset-JSON format
        dataset_json = serialize_to_dataset_json(data=bundle_data, study_id=study_id)
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

    except HTTPException:
        raise
    except DatasetJSONValidationError as e:
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
            status_code=500,
            detail=f"Export execution failed: {scrubbed_msg}",
        )


@router.post("/exports/wizard")
async def execute_export_wizard(
    request_data: ExportBundleRequest,
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA,
            ROLE_DATA_MANAGER,
            ROLE_SPONSOR_ADMIN,
            "sponsor_statistician",
            "statistician",
        )
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> Any:
    """Executes a parameterized export job from the Regulatory Export Wizard."""
    study_id = request_data.study_id
    fmt = request_data.format.strip().lower()
    domains = [d.upper() for d in (request_data.domains or [])]
    datasets = [d.upper() for d in (request_data.datasets or [])]
    if not domains and not datasets:
        # Default to all standard domains
        domains = ["DM", "AE", "VS", "LB", "MH", "CM"]
        datasets = ["ADSL", "ADAE", "ADVS"]

    actual_salt: str = (
        request_data.salt
        or os.getenv("BIOSTAT_EXPORT_SALT")
        or "secure-clinical-salt-98765"
    )  # pragma: allowlist secret

    try:
        bundle_data: dict[str, list[dict]] = {}
        for dom in domains:
            records, supp_records = await run_sdtm_extraction(
                session,
                study_id,
                dom,
                site_ids=request_data.site_ids,
                cohorts=request_data.cohorts,
            )
            if records:
                bundle_data[dom] = records
            if supp_records:
                bundle_data[f"SUPP{dom}"] = supp_records

        for ds in datasets:
            records = await run_adam_derivation(
                session,
                study_id,
                ds,
                site_ids=request_data.site_ids,
                cohorts=request_data.cohorts,
            )
            if records:
                bundle_data[ds] = records

        if not bundle_data:
            raise HTTPException(
                status_code=404,
                detail="No clinical records matched the export criteria.",
            )

        # Apply de-identification
        if request_data.privacy_profile.upper() != "UNRESTRICTED":
            bundle_data = cast(
                dict[str, list[dict[str, Any]]],
                deidentify_export_data(bundle_data, actual_salt),
            )

        if fmt in ("xpt_v5", "xpt_v8", "xpt", "sas_xpt"):
            # If single dataset, return binary XPT; if multiple, package in zip
            ver = "v8" if fmt == "xpt_v8" or request_data.xpt_version == "v8" else "v5"
            if len(bundle_data) == 1:
                ds_name, ds_recs = next(iter(bundle_data.items()))
                xpt_content = write_xpt(ds_name, ds_recs, version=ver)
                export_log = BiostatExport(
                    study_id=study_id,
                    export_type=f"WIZARD_{fmt.upper()}",
                    dataset_name=ds_name,
                    status="SUCCESS",
                )
                session.add(export_log)
                await session.commit()
                return Response(
                    content=xpt_content,
                    media_type="application/x-sas-xport",
                    headers={
                        "Content-Disposition": f"attachment; filename={ds_name.lower()}.xpt"
                    },
                )
            import io
            import zipfile

            zip_buf = io.BytesIO()
            with zipfile.ZipFile(zip_buf, "w", zipfile.ZIP_DEFLATED) as zf:
                for ds_name, ds_recs in bundle_data.items():
                    xpt_bytes = write_xpt(ds_name, ds_recs, version=ver)
                    zf.writestr(f"{ds_name.lower()}.xpt", xpt_bytes)
            export_log = BiostatExport(
                study_id=study_id,
                export_type=f"WIZARD_{fmt.upper()}_ZIP",
                dataset_name=None,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=zip_buf.getvalue(),
                media_type="application/zip",
                headers={
                    "Content-Disposition": f"attachment; filename={study_id.lower()}_xpt_bundle.zip"
                },
            )

        if fmt in ("odm_xml", "odm", "xml"):
            xml_str = serialize_to_odm_xml(
                study_id=study_id,
                data=bundle_data,
                metadata_version_oid=request_data.metadata_version_oid,
                audit_user="wizard_exporter",
                change_reason="Export Wizard Regulatory Extract",
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="WIZARD_ODM_XML",
                dataset_name=None,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=xml_str,
                media_type="application/xml",
                headers={
                    "Content-Disposition": f"attachment; filename={study_id.lower()}_odm.xml"
                },
            )

        if fmt in ("csv", "csv_zip"):
            zip_bytes = serialize_bundle_to_csv_zip(
                bundle_data=bundle_data,
                privacy_profile=request_data.privacy_profile,
                salt=actual_salt,
                include_audit_fields=request_data.include_audit_trail,
            )
            export_log = BiostatExport(
                study_id=study_id,
                export_type="WIZARD_CSV_ZIP",
                dataset_name=None,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()
            return Response(
                content=zip_bytes,
                media_type="application/zip",
                headers={
                    "Content-Disposition": f"attachment; filename={study_id.lower()}_csv_bundle.zip"
                },
            )

        # Default Dataset-JSON
        dataset_json = serialize_to_dataset_json(
            data=bundle_data,
            study_id=study_id,
            metadata_version_id=request_data.metadata_version_oid,
        )
        validate_dataset_json(dataset_json)

        export_log = BiostatExport(
            study_id=study_id,
            export_type="WIZARD_DATASET_JSON",
            dataset_name=None,
            status="SUCCESS",
        )
        session.add(export_log)
        await session.commit()

        return dataset_json.model_dump()

    except HTTPException:
        raise
    except DatasetJSONValidationError as e:
        scrubbed_msg = scrub_error_message(str(e))
        export_log = BiostatExport(
            study_id=study_id,
            export_type=f"WIZARD_{fmt.upper()}",
            dataset_name=None,
            status="FAILED",
            error_message=scrubbed_msg,
        )
        session.add(export_log)
        await session.commit()
        raise HTTPException(
            status_code=422,
            detail=f"Export validation failed: {scrubbed_msg}",
        )
    except Exception as e:
        scrubbed_msg = scrub_error_message(str(e))
        export_log = BiostatExport(
            study_id=study_id,
            export_type=f"WIZARD_{fmt.upper()}",
            dataset_name=None,
            status="FAILED",
            error_message=scrubbed_msg,
        )
        session.add(export_log)
        await session.commit()
        raise HTTPException(
            status_code=500,
            detail=f"Export execution failed: {scrubbed_msg}",
        )
