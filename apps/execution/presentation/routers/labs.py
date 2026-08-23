"""FastAPI router for central and local laboratory batch ingestion, reference range governance,
toxicity grading, investigator AE review queue, and LOINC unit conversions.

Requirements:
- ADR-2199 / PRD-SYS-123 (Lab Normal Range Governance & Interoperability)
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-PVG-001 (Pharmacovigilance & AE Staging Automation)
- PRD-MDR-001 (Metadata Repository & Catalog Normalization)
- PRD-QRY-001 (Automated Discrepancy Query Escalation)
- Trace-1 (Audit Trail & 21 CFR Part 11)
- Trace-15 (Laboratory Data Flow & Reference Ranges)
"""

from datetime import UTC, datetime
from typing import Any

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    HTTPException,
    Query,
    Request,
)
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.context import (
    audit_context,
    current_change_reason,
    current_user_id,
)
from apps.execution.database.models import (
    ClinicalQuery,
    FormSubmission,
    LocalLabCertification,
    StagedAdverseEventReview,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.domain.lab_transport_models import (
    LabToxicityEvaluateRequest,
    LabToxicityEvaluateResponse,
    LocalLabCertificationCreate,
    LocalLabCertificationResponse,
    LocalLabCertificationUpdate,
    LocalLabCertificationVerifyRequest,
    LocalLabCertificationVerifyResponse,
    LoincCatalogResponse,
    LoincConvertRequest,
    LoincConvertResponse,
    LoincMappingItem,
    StagedAdverseEventActionRequest,
    StagedAdverseEventActionResponse,
    StagedAdverseEventReviewResponse,
)
from apps.execution.lab_ranges import (
    convert_lab_unit,
    get_loinc_catalog,
    resolve_loinc_mapping,
)
from apps.execution.services.lab_ingestion_service import (
    LabBatchIngestResult,
    LabIngestionService,
)
from apps.execution.services.lab_toxicity_engine import (
    LabToxicityEngine,
)
from packages.security import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    ROLE_INVESTIGATOR,
    get_normalized_roles,
    require_roles,
    verify_not_auditor,
)

router = APIRouter(
    prefix="/api/v1/execution/labs", tags=["Laboratory Governance & Ingestion"]
)


# ============================================================================
# Batch Ingestion Endpoints
# ============================================================================


@router.post("/ingest", response_model=LabBatchIngestResult, status_code=200)
async def ingest_lab_batch_endpoint(
    request: Request,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabBatchIngestResult:
    """Ingest central or local laboratory batch data (CSV, HL7 v2.x, FHIR JSON, CDISC LAB, or Local Manual)."""
    content_type = request.headers.get("content-type", "").lower()

    study_id: str | None = None
    site_id: str | None = None
    lab_source: str = "CENTRAL"
    format_type: str = "csv"
    grading_scale: str = "CTCAE_V5"
    reason_for_change: str = "Batch laboratory data ingestion"
    raw_payload: Any = None

    if (
        "multipart/form-data" in content_type
        or "application/x-www-form-urlencoded" in content_type
    ):
        form = await request.form()
        format_type = str(form.get("format", "csv"))
        study_id = str(form.get("study_id")) if form.get("study_id") else None
        site_id = str(form.get("site_id")) if form.get("site_id") else None
        lab_source = str(form.get("lab_source", "CENTRAL"))
        grading_scale = str(form.get("grading_scale", "CTCAE_V5"))
        reason_for_change = str(
            form.get("reason_for_change", "Batch laboratory data ingestion")
        )

        upload_file = form.get("file")
        if upload_file is not None and hasattr(upload_file, "read"):
            file_bytes = await upload_file.read()
            raw_payload = file_bytes.decode("utf-8-sig", errors="replace")
        elif "payload" in form:
            raw_payload = str(form.get("payload"))
    else:
        try:
            body_json = await request.json()
        except Exception:
            body_json = {}

        if isinstance(body_json, dict):
            format_type = str(body_json.get("format", "csv"))
            study_id = body_json.get("study_id")
            site_id = body_json.get("site_id")
            lab_source = body_json.get("lab_source", "CENTRAL")
            grading_scale = body_json.get("grading_scale", "CTCAE_V5")
            reason_for_change = body_json.get(
                "reason_for_change", "Batch laboratory data ingestion"
            )
            raw_payload = body_json.get("payload")
            if raw_payload is None and "resource" in body_json:
                raw_payload = body_json["resource"]
            elif raw_payload is None and "resources" in body_json:
                raw_payload = body_json["resources"]
            elif raw_payload is None and "records" in body_json:
                raw_payload = body_json["records"]
            elif raw_payload is None and body_json.get("resourceType"):
                raw_payload = body_json
                format_type = "fhir"
        elif isinstance(body_json, list):
            raw_payload = body_json
            format_type = "fhir"

    if raw_payload is None or (
        isinstance(raw_payload, str) and not raw_payload.strip()
    ):
        raise HTTPException(
            status_code=400,
            detail="Batch ingestion requires non-empty 'payload', 'file', or 'resource'.",
        )

    user_id = current_user_id.get() or "system_lab_ingestion"
    gxp_reason = current_change_reason.get() or reason_for_change

    return await LabIngestionService.ingest_batch(
        session=session,
        payload=raw_payload,
        format=format_type,
        study_id=study_id,
        site_id=site_id,
        lab_source=lab_source,
        grading_scale=grading_scale,
        user_id=user_id,
        change_reason=gxp_reason,
        background_tasks=background_tasks,
    )


@router.get(
    "/batch-status", response_model=list[LabBatchIngestResult] | LabBatchIngestResult
)
async def get_batch_status_endpoint(
    batch_id: str | None = Query(None, description="Optional batch ID to query"),
    study_id: str | None = Query(None, description="Optional study ID filter"),
    roles: list[str] = Depends(verify_not_auditor),
) -> list[LabBatchIngestResult] | LabBatchIngestResult:
    """Retrieve status and metrics for previous lab batch ingestion jobs."""
    if batch_id:
        batch = LabIngestionService.get_batch_status(batch_id)
        if not batch:
            raise HTTPException(
                status_code=404,
                detail=f"Laboratory ingestion batch '{batch_id}' not found.",
            )
        return batch

    return LabIngestionService.list_batch_statuses(study_id=study_id)


@router.get("/batch-status/{batch_id}", response_model=LabBatchIngestResult)
async def get_batch_status_by_id_endpoint(
    batch_id: str,
    roles: list[str] = Depends(verify_not_auditor),
) -> LabBatchIngestResult:
    """Query execution status and statistics of a specific laboratory batch by ID."""
    batch = LabIngestionService.get_batch_status(batch_id)
    if not batch:
        raise HTTPException(
            status_code=404,
            detail=f"Laboratory ingestion batch '{batch_id}' not found.",
        )
    return batch


# ============================================================================
# Local Laboratory Certification Endpoints
# ============================================================================


@router.post(
    "/certifications",
    response_model=LocalLabCertificationResponse,
    status_code=201,
)
async def create_local_lab_certification(
    payload: LocalLabCertificationCreate,
    roles: list[str] = Depends(
        require_roles(ROLE_CRA, ROLE_DATA_MANAGER, ROLE_INVESTIGATOR)
    ),
    _justification: None = Depends(verify_change_justification),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LocalLabCertificationResponse:
    """Register a local laboratory certification and accreditation."""
    user_id = current_user_id.get() or "system_user"
    reason = current_change_reason.get() or payload.reason_for_change

    with audit_context(user_id, reason):
        cert = LocalLabCertification(
            study_id=payload.study_id,
            site_id=payload.site_id,
            lab_name=payload.lab_name,
            lab_identifier=payload.lab_identifier,
            certification_number=payload.certification_number,
            accreditation_body=payload.accreditation_body,
            effective_date=payload.effective_date.replace(tzinfo=None),
            expiration_date=payload.expiration_date.replace(tzinfo=None),
            status="ACTIVE",
            director_name=payload.director_name,
            is_accredited=payload.is_accredited,
            notes=payload.notes,
            created_by=user_id,
            reason_for_change=reason,
        )
        session.add(cert)
        await session.commit()
        await session.refresh(cert)

    return LocalLabCertificationResponse(
        id=cert.id,
        study_id=cert.study_id,
        site_id=cert.site_id,
        lab_name=cert.lab_name,
        lab_identifier=cert.lab_identifier,
        certification_number=cert.certification_number,
        accreditation_body=cert.accreditation_body,
        effective_date=cert.effective_date,
        expiration_date=cert.expiration_date,
        status=cert.status,
        director_name=cert.director_name,
        is_accredited=cert.is_accredited,
        notes=cert.notes,
        version=cert.version,
        is_deleted=cert.is_deleted,
    )


@router.get(
    "/certifications",
    response_model=list[LocalLabCertificationResponse],
)
async def list_local_lab_certifications(
    study_id: str | None = Query(None, description="Filter by study ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    status: str | None = Query(None, description="Filter by certification status"),
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[LocalLabCertificationResponse]:
    """List local laboratory certifications with optional filters."""
    stmt = select(LocalLabCertification).where(
        LocalLabCertification.is_deleted.is_(False)
    )
    if study_id:
        stmt = stmt.where(LocalLabCertification.study_id == study_id)
    if site_id:
        stmt = stmt.where(LocalLabCertification.site_id == site_id)
    if status:
        stmt = stmt.where(LocalLabCertification.status == status.upper())

    stmt = stmt.order_by(LocalLabCertification.expiration_date.asc())
    res = await session.execute(stmt)
    rows = res.scalars().all()

    return [
        LocalLabCertificationResponse(
            id=r.id,
            study_id=r.study_id,
            site_id=r.site_id,
            lab_name=r.lab_name,
            lab_identifier=r.lab_identifier,
            certification_number=r.certification_number,
            accreditation_body=r.accreditation_body,
            effective_date=r.effective_date,
            expiration_date=r.expiration_date,
            status=r.status,
            director_name=r.director_name,
            is_accredited=r.is_accredited,
            notes=r.notes,
            version=r.version,
            is_deleted=r.is_deleted,
        )
        for r in rows
    ]


@router.put(
    "/certifications/{cert_id}",
    response_model=LocalLabCertificationResponse,
)
async def update_local_lab_certification(
    cert_id: str,
    payload: LocalLabCertificationUpdate,
    roles: list[str] = Depends(
        require_roles(ROLE_CRA, ROLE_DATA_MANAGER, ROLE_INVESTIGATOR)
    ),
    _justification: None = Depends(verify_change_justification),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LocalLabCertificationResponse:
    """Update an existing local laboratory certification."""
    stmt = select(LocalLabCertification).where(LocalLabCertification.id == cert_id)
    res = await session.execute(stmt)
    cert = res.scalars().first()
    if not cert:
        raise HTTPException(status_code=404, detail="LocalLabCertification not found")

    user_id = current_user_id.get() or "system_user"
    reason = current_change_reason.get() or payload.reason_for_change

    with audit_context(user_id, reason):
        if payload.lab_name is not None:
            cert.lab_name = payload.lab_name
        if payload.lab_identifier is not None:
            cert.lab_identifier = payload.lab_identifier
        if payload.certification_number is not None:
            cert.certification_number = payload.certification_number
        if payload.accreditation_body is not None:
            cert.accreditation_body = payload.accreditation_body
        if payload.effective_date is not None:
            cert.effective_date = payload.effective_date.replace(tzinfo=None)
        if payload.expiration_date is not None:
            cert.expiration_date = payload.expiration_date.replace(tzinfo=None)
        if payload.status is not None:
            cert.status = payload.status
        if payload.director_name is not None:
            cert.director_name = payload.director_name
        if payload.is_accredited is not None:
            cert.is_accredited = payload.is_accredited
        if payload.notes is not None:
            cert.notes = payload.notes
        cert.version += 1
        await session.commit()
        await session.refresh(cert)

    return LocalLabCertificationResponse(
        id=cert.id,
        study_id=cert.study_id,
        site_id=cert.site_id,
        lab_name=cert.lab_name,
        lab_identifier=cert.lab_identifier,
        certification_number=cert.certification_number,
        accreditation_body=cert.accreditation_body,
        effective_date=cert.effective_date,
        expiration_date=cert.expiration_date,
        status=cert.status,
        director_name=cert.director_name,
        is_accredited=cert.is_accredited,
        notes=cert.notes,
        version=cert.version,
        is_deleted=cert.is_deleted,
    )


@router.post(
    "/certifications/verify",
    response_model=LocalLabCertificationVerifyResponse,
)
async def verify_local_lab_certification_endpoint(
    payload: LocalLabCertificationVerifyRequest,
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LocalLabCertificationVerifyResponse:
    """Verify if a site has active, unexpired local lab certification for an observation date."""
    obs_date = payload.observation_date or datetime.now(UTC).replace(tzinfo=None)
    stmt = select(LocalLabCertification).where(
        LocalLabCertification.study_id == payload.study_id,
        LocalLabCertification.site_id == payload.site_id,
        LocalLabCertification.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    certs = res.scalars().all()

    if not certs:
        return LocalLabCertificationVerifyResponse(
            is_valid=False,
            status="MISSING",
            site_id=payload.site_id,
            study_id=payload.study_id,
            uncalibrated_safeguard_triggered=True,
            message=f"No local lab certification registered for site {payload.site_id}.",
        )

    active_cert = None
    for c in certs:
        if c.status == "ACTIVE" and c.is_accredited:
            if c.effective_date <= obs_date <= c.expiration_date:
                active_cert = c
                break

    if active_cert:
        days_left = (active_cert.expiration_date - obs_date).days
        return LocalLabCertificationVerifyResponse(
            is_valid=True,
            status="ACTIVE",
            site_id=payload.site_id,
            study_id=payload.study_id,
            certification_number=active_cert.certification_number,
            accreditation_body=active_cert.accreditation_body,
            expiration_date=active_cert.expiration_date,
            days_until_expiration=days_left,
            uncalibrated_safeguard_triggered=False,
            message=f"Certification is valid ({active_cert.accreditation_body} #{active_cert.certification_number}). {days_left} days until expiration.",
        )

    # Check if expired
    most_recent = sorted(certs, key=lambda x: x.expiration_date, reverse=True)[0]
    days_expired = (obs_date - most_recent.expiration_date).days
    return LocalLabCertificationVerifyResponse(
        is_valid=False,
        status="EXPIRED" if days_expired > 0 else most_recent.status,
        site_id=payload.site_id,
        study_id=payload.study_id,
        certification_number=most_recent.certification_number,
        accreditation_body=most_recent.accreditation_body,
        expiration_date=most_recent.expiration_date,
        days_until_expiration=-days_expired if days_expired > 0 else 0,
        uncalibrated_safeguard_triggered=True,
        message=f"Local lab certification expired on {most_recent.expiration_date.strftime('%Y-%m-%d')}. Uncalibrated range safeguard active.",
    )


# ============================================================================
# Investigator Toxicity Review Queue Endpoints
# ============================================================================


@router.get(
    "/toxicity/review-queue",
    response_model=list[StagedAdverseEventReviewResponse],
)
async def list_staged_ae_review_queue(
    study_id: str | None = Query(None, description="Filter by study ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    review_status: str | None = Query(
        "STAGED_FOR_INVESTIGATOR_REVIEW",
        description="Filter by status (default: STAGED_FOR_INVESTIGATOR_REVIEW, ALL for any)",
    ),
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[StagedAdverseEventReviewResponse]:
    """Retrieve laboratory Grade 3/4 toxicity triggers staged for Investigator review."""
    stmt = select(StagedAdverseEventReview).where(
        StagedAdverseEventReview.is_deleted.is_(False)
    )
    if study_id:
        stmt = stmt.where(StagedAdverseEventReview.study_id == study_id)
    if site_id:
        stmt = stmt.where(StagedAdverseEventReview.site_id == site_id)
    if review_status and review_status.upper() != "ALL":
        stmt = stmt.where(
            StagedAdverseEventReview.review_status == review_status.upper()
        )

    stmt = stmt.order_by(StagedAdverseEventReview.created_at.desc())
    res = await session.execute(stmt)
    rows = res.scalars().all()

    return [
        StagedAdverseEventReviewResponse(
            id=r.id,
            study_id=r.study_id,
            site_id=r.site_id,
            subject_id=r.subject_id,
            visit_id=r.visit_id,
            observation_id=r.observation_id,
            form_submission_id=r.form_submission_id,
            query_id=r.query_id,
            test_code=r.test_code,
            test_name=r.test_name,
            lab_value=r.lab_value,
            unit=r.unit,
            ctcae_grade=r.ctcae_grade,
            grading_scale=r.grading_scale,
            toxicity_term=r.toxicity_term,
            clinical_description=r.clinical_description,
            is_hys_law_warning=r.is_hys_law_warning,
            is_delta_check_alert=r.is_delta_check_alert,
            delta_percentage=r.delta_percentage,
            review_status=r.review_status,
            reviewed_by=r.reviewed_by,
            reviewed_at=r.reviewed_at,
            investigator_notes=r.investigator_notes,
            version=r.version,
            is_deleted=r.is_deleted,
            created_at=r.created_at,
        )
        for r in rows
    ]


@router.post(
    "/toxicity/review/{review_id}/action",
    response_model=StagedAdverseEventActionResponse,
)
async def action_staged_ae_review(
    review_id: str,
    payload: StagedAdverseEventActionRequest,
    roles: list[str] = Depends(
        require_roles(ROLE_INVESTIGATOR, ROLE_CRA, ROLE_DATA_MANAGER)
    ),
    _justification: None = Depends(verify_change_justification),
    session: AsyncSession = Depends(get_execution_db_session),
) -> StagedAdverseEventActionResponse:
    """Perform Investigator workflow action on a staged Grade 3/4 AE (Confirm, Dismiss, Escalate)."""
    stmt = select(StagedAdverseEventReview).where(
        StagedAdverseEventReview.id == review_id
    )
    res = await session.execute(stmt)
    review = res.scalars().first()
    if not review:
        raise HTTPException(
            status_code=404, detail="StagedAdverseEventReview not found"
        )

    action_u = payload.action.strip().upper()
    user_id = current_user_id.get() or "investigator_user"
    reason = current_change_reason.get() or payload.reason_for_change
    now = datetime.now(UTC).replace(tzinfo=None)

    with audit_context(user_id, reason):
        # Update linked FormSubmission
        if review.form_submission_id:
            stmt_form = select(FormSubmission).where(
                FormSubmission.id == review.form_submission_id
            )
            res_form = await session.execute(stmt_form)
            form_sub = res_form.scalars().first()
            if form_sub:
                form_payload = dict(form_sub.payload or {})
                if action_u == "CONFIRM":
                    form_sub.status = "SUBMITTED"
                    form_payload["staging_status"] = "CONFIRMED_AS_AE"
                    form_payload["investigator_confirmation_notes"] = (
                        payload.investigator_notes
                    )
                    form_payload["confirmed_at"] = now.isoformat()
                    form_payload["confirmed_by"] = user_id
                elif action_u == "DISMISS":
                    form_sub.status = "DISMISSED"
                    form_payload["staging_status"] = (
                        "DISMISSED_NOT_CLINICALLY_SIGNIFICANT"
                    )
                    form_payload["dismissal_justification"] = payload.investigator_notes
                elif action_u == "ESCALATE_SAE":
                    form_sub.status = "SUBMITTED"
                    form_payload["staging_status"] = "ESCALATED_SAE"
                    form_payload["aeser"] = "Y"
                    form_payload["serious_adverse_event"] = True
                    form_payload["escalated_by"] = user_id
                form_sub.payload = form_payload
                form_sub.version += 1

        # Update linked ClinicalQuery
        if review.query_id:
            stmt_query = select(ClinicalQuery).where(
                ClinicalQuery.id == review.query_id
            )
            res_query = await session.execute(stmt_query)
            query = res_query.scalars().first()
            if query:
                if action_u == "CONFIRM":
                    query.status = "ANSWERED"
                    query.resolution_notes = f"Confirmed as AE by Investigator ({user_id}): {payload.investigator_notes or 'AE verified.'}"
                elif action_u == "DISMISS":
                    query.status = "RESOLVED"
                    query.resolution_notes = f"Dismissed as not clinically significant by Investigator ({user_id}): {payload.investigator_notes or 'Deemed not clinically significant.'}"
                elif action_u == "ESCALATE_SAE":
                    query.priority = "CRITICAL"
                    query.status = "ANSWERED"
                    query.resolution_notes = f"Escalated to Serious Adverse Event (SAE) by Investigator ({user_id}): {payload.investigator_notes or 'SAE criteria fulfilled.'}"

        # Update Review Record with 21 CFR Part 11 electronic signature manifest
        if action_u == "CONFIRM":
            review.review_status = "CONFIRMED_AS_AE"
            msg = "Grade 3/4 laboratory abnormality confirmed as Adverse Event eCRF."
        elif action_u == "DISMISS":
            review.review_status = "DISMISSED_NOT_CLINICALLY_SIGNIFICANT"
            msg = "Grade 3/4 laboratory abnormality dismissed as not clinically significant."
        elif action_u == "ESCALATE_SAE":
            review.review_status = "ESCALATED_SAE"
            msg = "Laboratory abnormality escalated to Serious Adverse Event (SAE)."
        else:
            raise HTTPException(
                status_code=400, detail=f"Unsupported review action '{payload.action}'."
            )

        review.reviewed_by = user_id
        review.reviewed_at = now
        review.investigator_notes = payload.investigator_notes
        review.electronic_signature_manifest = {
            "signer_id": user_id,
            "action": action_u,
            "timestamp": now.isoformat(),
            "meaning": f"Investigator clinical determination ({action_u})",
            "reason_for_change": reason,
        }
        review.version += 1
        await session.commit()

    return StagedAdverseEventActionResponse(
        id=review.id,
        review_status=review.review_status,
        action_taken=action_u,
        form_submission_id=review.form_submission_id,
        query_id=review.query_id,
        message=msg,
        timestamp=now,
    )


@router.post(
    "/toxicity/evaluate",
    response_model=LabToxicityEvaluateResponse,
)
async def evaluate_toxicity_realtime(
    payload: LabToxicityEvaluateRequest,
    roles: list[str] = Depends(get_normalized_roles),
) -> LabToxicityEvaluateResponse:
    """Evaluate and simulate CTCAE v5.0 / DAIDS v2.1 toxicity grading in real time."""
    assessment = LabToxicityEngine.evaluate_observation(
        test_code=payload.test_code,
        test_name=payload.test_name or payload.test_code,
        value=payload.value,
        unit=payload.unit,
        ref_low=payload.ref_low,
        ref_high=payload.ref_high,
        baseline_value=payload.baseline_value,
        grading_scale=payload.grading_scale,
        alt_co_analyte=payload.alt_value,
        ast_co_analyte=payload.ast_value,
        bili_co_analyte=payload.bili_value,
        alkp_co_analyte=payload.alkp_value,
    )

    return LabToxicityEvaluateResponse(
        test_code=assessment.test_code,
        test_name=assessment.test_name,
        observed_value=assessment.observed_value,
        unit=assessment.unit,
        ctcae_grade=int(assessment.ctcae_grade),
        grading_scale=assessment.grading_scale,
        toxicity_description=assessment.toxicity_description,
        is_critical_ae_trigger=assessment.is_critical_ae_trigger,
        is_hys_law_warning=assessment.is_hys_law_warning,
        is_delta_check_alert=assessment.is_delta_check_alert,
        delta_percentage=assessment.delta_percentage,
    )


# ============================================================================
# LOINC Catalog & Standard Mathematical Unit Conversions
# ============================================================================


@router.get(
    "/loinc-catalog",
    response_model=LoincCatalogResponse,
)
async def get_loinc_catalog_endpoint(
    roles: list[str] = Depends(get_normalized_roles),
) -> LoincCatalogResponse:
    """Retrieve standardized LOINC catalog mappings."""
    items_raw = get_loinc_catalog()
    items = [LoincMappingItem(**item) for item in items_raw]
    return LoincCatalogResponse(total_count=len(items), items=items)


@router.post(
    "/convert-unit",
    response_model=LoincConvertResponse,
)
async def convert_unit_endpoint(
    payload: LoincConvertRequest,
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LoincConvertResponse:
    """Convert laboratory measurement between conventional and SI units."""
    converted = await convert_lab_unit(
        session=session,
        test_code=payload.test_code,
        from_unit=payload.from_unit,
        to_unit=payload.to_unit,
        value=payload.value,
    )

    loinc_entry = resolve_loinc_mapping(payload.test_code)
    loinc_code = loinc_entry.get("loinc_code") if loinc_entry else None

    factor = (converted / payload.value) if payload.value != 0 else 1.0

    return LoincConvertResponse(
        test_code=payload.test_code,
        from_unit=payload.from_unit,
        to_unit=payload.to_unit,
        original_value=payload.value,
        converted_value=converted,
        factor=factor,
        offset=0.0,
        loinc_code=loinc_code,
    )
