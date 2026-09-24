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
from pydantic import BaseModel
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
    LabReferenceRange,
    LocalLabCertification,
    StagedAdverseEventReview,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.domain.lab_transport_models import (
    LabReferenceRangeCreate,
    LabReferenceRangeResponse,
    LabReferenceRangeUpdate,
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
from apps.execution.services.lab_assessment_engine import LabAssessmentEngine
from apps.execution.services.lab_ingestion_service import (
    LabBatchIngestResult,
    LabIngestionService,
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
    prefix="/api/v1/execution", tags=["Laboratory Governance & Ingestion"]
)


# ============================================================================
# Batch Ingestion Endpoints
# ============================================================================


@router.post("/labs/ingest", response_model=LabBatchIngestResult, status_code=200)
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
    "/labs/batch-status",
    response_model=list[LabBatchIngestResult] | LabBatchIngestResult,
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


@router.get("/labs/batch-status/{batch_id}", response_model=LabBatchIngestResult)
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


def _is_expired(exp_date: datetime) -> bool:
    if exp_date.tzinfo is not None:
        return exp_date < datetime.now(UTC)
    return exp_date < datetime.now(UTC).replace(tzinfo=None)


@router.post(
    "/labs/certifications",
    response_model=LocalLabCertificationResponse,
    status_code=201,
)
async def create_local_lab_certification(
    payload: LocalLabCertificationCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LocalLabCertificationResponse:
    """Register or update a local site laboratory regulatory certification (CLIA, CAP, ISO 15189)."""
    stmt = select(LocalLabCertification).where(
        LocalLabCertification.study_id == payload.study_id,
        LocalLabCertification.site_id == payload.site_id,
        LocalLabCertification.lab_name == payload.lab_name,
        LocalLabCertification.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    existing = res.scalars().first()

    user_id = current_user_id.get() or "system_user"
    reason = (
        current_change_reason.get()
        or payload.reason_for_change
        or "Register local laboratory certification"
    )
    status_str = "EXPIRED" if _is_expired(payload.expiration_date) else "ACTIVE"

    with audit_context(user_id=user_id, change_reason=reason):
        if existing:
            existing.certification_number = payload.certification_number
            existing.accreditation_body = payload.accreditation_body
            existing.effective_date = payload.effective_date
            existing.expiration_date = payload.expiration_date
            existing.director_name = payload.director_name
            existing.is_accredited = payload.is_accredited
            existing.notes = payload.notes
            existing.status = status_str
            existing.version_index += 1
            session.add(existing)
            await session.commit()
            await session.refresh(existing)
            return LocalLabCertificationResponse.model_validate(existing)

        cert = LocalLabCertification(
            study_id=payload.study_id,
            site_id=payload.site_id,
            lab_name=payload.lab_name,
            lab_identifier=payload.lab_identifier,
            certification_number=payload.certification_number,
            accreditation_body=payload.accreditation_body,
            effective_date=payload.effective_date,
            expiration_date=payload.expiration_date,
            director_name=payload.director_name,
            is_accredited=payload.is_accredited,
            notes=payload.notes,
            status=status_str,
        )
        session.add(cert)
        await session.commit()
        await session.refresh(cert)
        return LocalLabCertificationResponse.model_validate(cert)


@router.get(
    "/labs/certifications",
    response_model=list[LocalLabCertificationResponse],
)
async def list_local_lab_certifications(
    study_id: str | None = Query(None, description="Filter by study ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    status: str | None = Query(None, description="Filter by certification status"),
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[LocalLabCertificationResponse]:
    """Retrieve laboratory accreditation certifications across sites."""
    stmt = select(LocalLabCertification).where(
        LocalLabCertification.is_deleted.is_(False)
    )
    if study_id:
        stmt = stmt.where(LocalLabCertification.study_id == study_id)
    if site_id:
        stmt = stmt.where(LocalLabCertification.site_id == site_id)
    if status:
        stmt = stmt.where(LocalLabCertification.status == status)

    res = await session.execute(stmt)
    rows = res.scalars().all()

    results = []
    for r in rows:
        dto = LocalLabCertificationResponse.model_validate(r)
        if _is_expired(r.expiration_date):
            dto.status = "EXPIRED"
        results.append(dto)

    return results


@router.put(
    "/labs/certifications/{cert_id}",
    response_model=LocalLabCertificationResponse,
)
async def update_local_lab_certification(
    cert_id: str,
    payload: LocalLabCertificationUpdate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LocalLabCertificationResponse:
    """Update certification credentials and renewal dates for a specific site lab."""
    stmt = select(LocalLabCertification).where(
        (LocalLabCertification.id == cert_id)
        | (LocalLabCertification.site_id == cert_id),
        LocalLabCertification.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    cert = res.scalars().first()
    if not cert:
        raise HTTPException(
            status_code=404,
            detail=f"Local laboratory certification for '{cert_id}' not found.",
        )

    user_id = current_user_id.get() or "system_user"
    reason = (
        current_change_reason.get()
        or payload.reason_for_change
        or "Update local laboratory certification"
    )

    with audit_context(user_id=user_id, change_reason=reason):
        if payload.lab_name is not None:
            cert.lab_name = payload.lab_name
        if payload.lab_identifier is not None:
            cert.lab_identifier = payload.lab_identifier
        if payload.certification_number is not None:
            cert.certification_number = payload.certification_number
        if payload.accreditation_body is not None:
            cert.accreditation_body = payload.accreditation_body
        if payload.effective_date is not None:
            cert.effective_date = payload.effective_date
        if payload.expiration_date is not None:
            cert.expiration_date = payload.expiration_date
            cert.status = (
                "EXPIRED" if _is_expired(payload.expiration_date) else "ACTIVE"
            )
        if payload.director_name is not None:
            cert.director_name = payload.director_name
        if payload.is_accredited is not None:
            cert.is_accredited = payload.is_accredited
        if payload.notes is not None:
            cert.notes = payload.notes

        cert.version_index += 1
        session.add(cert)
        await session.commit()
        await session.refresh(cert)

    return LocalLabCertificationResponse.model_validate(cert)


@router.post(
    "/labs/certifications/verify",
    response_model=LocalLabCertificationVerifyResponse,
)
@router.post(
    "/labs/certifications/{site_id}/verify",
    response_model=LocalLabCertificationVerifyResponse,
)
async def verify_local_lab_certification_gate(
    payload: LocalLabCertificationVerifyRequest,
    site_id: str | None = None,
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LocalLabCertificationVerifyResponse:
    """Pre-flight GxP verification check validating that an active lab certification is in place."""
    target_site_id = site_id or payload.site_id
    obs_date = payload.observation_date or datetime.now(UTC).date().isoformat()

    return await LabAssessmentEngine.verify_local_lab_certification(
        study_id=payload.study_id,
        site_id=target_site_id,
        obs_date=obs_date,
        session=session,
    )


# ============================================================================
# Investigator Toxicity Review Queue Endpoints
# ============================================================================


@router.get(
    "/labs/toxicity/review-queue",
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
    "/labs/toxicity/review/{review_id}/action",
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

    created_ae_id: str | None = None
    created_query_id: str | None = None
    now_ts = datetime.now(UTC).replace(tzinfo=None)

    with audit_context(user_id=user_id, change_reason=reason):
        sig_manifest = {
            "signer_id": user_id,
            "signed_at": now_ts.isoformat(),
            "reason": reason,
            "meaning": f"Investigator {action_u.capitalize()} Sign-off",
            "action": action_u,
        }
        review.electronic_signature_manifest = sig_manifest

        if action_u in ("CONFIRM", "CONFIRMED"):
            review.review_status = "CONFIRMED_AS_AE"
            review.reviewed_by = user_id
            review.reviewed_at = now_ts
            review.investigator_notes = payload.investigator_notes

            if review.form_submission_id:
                stmt_form = select(FormSubmission).where(
                    FormSubmission.id == review.form_submission_id
                )
                res_form = await session.execute(stmt_form)
                existing_form = res_form.scalars().first()
                if existing_form:
                    existing_form.status = "SUBMITTED"
                    current_payload = dict(existing_form.payload or {})
                    current_payload["staging_status"] = "CONFIRMED_AS_AE"
                    current_payload["confirmed_by"] = user_id
                    current_payload["confirmed_at"] = now_ts.isoformat()
                    current_payload["investigator_confirmation_notes"] = (
                        payload.investigator_notes
                    )
                    existing_form.payload = current_payload
                    session.add(existing_form)
                    created_ae_id = existing_form.id
            if not created_ae_id:
                ae_submission = FormSubmission(
                    study_id=review.study_id,
                    site_id=review.site_id,
                    subject_id=review.subject_id,
                    visit_id=review.visit_id or "UNSCHEDULED",
                    form_id="AE",
                    status="SUBMITTED",
                    is_active=True,
                    payload={
                        "AETERM": payload.ae_term or review.toxicity_term,
                        "AESEV": f"Grade {review.ctcae_grade}",
                        "AESTDAT": now_ts.date().isoformat(),
                        "AESER": "Y" if review.ctcae_grade in (3, 4, 5) else "N",
                        "AEREL": payload.causality_assessment or "POSSIBLE",
                        "AETOXGR": review.ctcae_grade,
                        "AE_SOURCE": "LABORATORY_TRIGGER",
                        "ORIGINATING_OBSERVATION_ID": review.observation_id,
                    },
                    version=1,
                )
                session.add(ae_submission)
                await session.flush()
                created_ae_id = ae_submission.id
                review.form_submission_id = created_ae_id

        elif action_u in ("DISMISS", "DISMISSED"):
            review.review_status = "DISMISSED"
            review.reviewed_by = user_id
            review.reviewed_at = now_ts
            review.investigator_notes = payload.investigator_notes

        elif action_u in ("ESCALATE", "RAISE_QUERY", "QUERY"):
            review.review_status = "QUERY_RAISED"
            review.reviewed_by = user_id
            review.reviewed_at = now_ts
            review.investigator_notes = payload.investigator_notes

            query = ClinicalQuery(
                study_id=review.study_id,
                site_id=review.site_id,
                subject_id=review.subject_id,
                observation_id=review.observation_id,
                field_name=review.test_code,
                query_text=(
                    f"Investigator escalation for Grade {review.ctcae_grade} {review.toxicity_term} "
                    f"({review.test_name}: {review.lab_value} {review.unit}). {payload.investigator_notes or ''}"
                ),
                status="OPEN",
                origin="LAB_TOXICITY_MONITOR",
                created_by=user_id,
            )
            session.add(query)
            await session.flush()
            created_query_id = query.id
            review.query_id = created_query_id

        else:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid action '{payload.action}'. Supported: CONFIRM, DISMISS, ESCALATE.",
            )

        review.version += 1
        session.add(review)
        await session.commit()

    return StagedAdverseEventActionResponse(
        id=review.id,
        action_taken=action_u,
        review_status=review.review_status,
        form_submission_id=created_ae_id,
        query_id=created_query_id,
        message=f"Action '{action_u}' executed successfully.",
        timestamp=now_ts,
    )


# ============================================================================
# Dynamic CTCAE Toxicity Evaluation & Catalog Normalization
# ============================================================================


@router.post(
    "/labs/toxicity/evaluate",
    response_model=LabToxicityEvaluateResponse,
)
async def evaluate_lab_toxicity_endpoint(
    payload: LabToxicityEvaluateRequest,
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabToxicityEvaluateResponse:
    """Evaluate CTCAE v5.0 Grade, Hy's Law criteria, and delta checks for a single lab observation."""
    result = await LabAssessmentEngine.evaluate_observation(
        session=session,
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        test_code=payload.test_code,
        value=payload.value,
        unit=payload.unit,
        obs_date=payload.obs_date,
        previous_value=payload.previous_value,
        previous_date=payload.previous_date,
        alt_ast_max_baseline=payload.alt_ast_max_baseline,
        tbil_max_baseline=payload.tbil_max_baseline,
        grading_scale=payload.grading_scale or "CTCAE_V5",
    )

    return LabToxicityEvaluateResponse(
        test_code=payload.test_code,
        value=payload.value,
        unit=payload.unit,
        ctcae_grade=result.ctcae_grade,
        toxicity_term=result.toxicity_term,
        severity_category=result.severity_category,
        is_hys_law_warning=result.is_hys_law_warning,
        is_delta_check_alert=result.is_delta_check_alert,
        delta_percentage=result.delta_percentage,
        staged_review_id=result.staged_review_id,
        matched_reference_range_id=result.matched_reference_range_id,
        matched_range_low=result.matched_range_low,
        matched_range_high=result.matched_range_high,
        evaluation_notes=result.evaluation_notes,
    )


@router.get(
    "/labs/loinc-catalog",
    response_model=LoincCatalogResponse,
)
@router.get(
    "/labs/loinc/catalog",
    response_model=LoincCatalogResponse,
    include_in_schema=False,
)
async def get_loinc_catalog_endpoint(
    roles: list[str] = Depends(get_normalized_roles),
) -> LoincCatalogResponse:
    """Return dictionary of standardized LOINC mappings and CDISC Biomedical Concept bindings."""
    catalog = LabAssessmentEngine.get_loinc_catalog()
    if isinstance(catalog, dict):
        items = [LoincMappingItem(test_code=k, **v) for k, v in catalog.items()]
    else:
        items = [LoincMappingItem(**item) for item in catalog]
    return LoincCatalogResponse(items=items, total_count=len(items))


@router.post(
    "/labs/convert-unit",
    response_model=LoincConvertResponse,
)
@router.post(
    "/labs/loinc/convert",
    response_model=LoincConvertResponse,
    include_in_schema=False,
)
async def convert_unit_endpoint(
    payload: LoincConvertRequest,
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LoincConvertResponse:
    """Convert laboratory measurement between conventional and SI units."""
    converted = await LabAssessmentEngine.convert_unit(
        session=session,
        test_code=payload.test_code,
        from_unit=payload.from_unit,
        to_unit=payload.to_unit,
        value=payload.value,
    )

    loinc_entry = LabAssessmentEngine.resolve_loinc_mapping(payload.test_code)
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


# ============================================================================
# Lab Reference Range Governance CRUD & Recalculation
# ============================================================================


def validate_lab_range_payload(data: dict[str, Any]) -> None:
    """Enforces all domain/business constraints on a reference range data dictionary."""
    required_strings = [
        "study_id",
        "test_code",
        "test_name",
        "source",
        "unit",
        "normalized_unit",
        "sex_applicability",
    ]
    for field in required_strings:
        val = data.get(field)
        if val is None:
            raise HTTPException(
                status_code=400,
                detail=f"Field '{field}' is required and cannot be blank.",
            )
        if not isinstance(val, str) or not val.strip():
            raise HTTPException(
                status_code=400,
                detail=f"Field '{field}' cannot be blank.",
            )

    if "site_id" in data and data["site_id"] is not None:
        val = data["site_id"]
        if isinstance(val, str) and not val.strip():
            raise HTTPException(
                status_code=400,
                detail="Field 'site_id' cannot be blank.",
            )

    src = data.get("source")
    source_upper = str(src.value if hasattr(src, "value") else src).strip().upper()
    if source_upper not in ("CENTRAL", "LOCAL"):
        raise HTTPException(
            status_code=400,
            detail="Field 'source' must be either 'CENTRAL' or 'LOCAL'.",
        )
    data["source"] = source_upper

    if source_upper == "CENTRAL" and "site_id" in data and data["site_id"] is not None:
        raise HTTPException(
            status_code=400,
            detail="CENTRAL reference ranges are global and must have site_id = None.",
        )

    sex_val = data.get("sex_applicability")
    sex_upper = (
        str(sex_val.value if hasattr(sex_val, "value") else sex_val).strip().upper()
    )
    if sex_upper not in ("M", "F", "ALL", "U"):
        raise HTTPException(
            status_code=400,
            detail="Field 'sex_applicability' must be one of 'M', 'F', 'ALL', or 'U'.",
        )
    data["sex_applicability"] = sex_upper

    age_low = data.get("age_low")
    age_high = data.get("age_high")
    if age_low is not None:
        try:
            age_low_val = float(age_low)
            data["age_low"] = age_low_val
        except ValueError, TypeError:
            raise HTTPException(
                status_code=400,
                detail="Field 'age_low' must be a numeric value.",
            )
        if age_low_val < 0:
            raise HTTPException(
                status_code=400,
                detail="Field 'age_low' cannot be negative.",
            )

    if age_high is not None:
        try:
            age_high_val = float(age_high)
            data["age_high"] = age_high_val
        except ValueError, TypeError:
            raise HTTPException(
                status_code=400,
                detail="Field 'age_high' must be a numeric value.",
            )
        if age_high_val < 0:
            raise HTTPException(
                status_code=400,
                detail="Field 'age_high' cannot be negative.",
            )

    if age_low is not None and age_high is not None:
        if float(age_low) > float(age_high):
            raise HTTPException(
                status_code=400,
                detail="Field 'age_low' must be less than or equal to 'age_high'.",
            )

    low_bound = data.get("low_bound")
    high_bound = data.get("high_bound")
    if low_bound is not None:
        try:
            low_bound_val = float(low_bound)
            data["low_bound"] = low_bound_val
        except ValueError, TypeError:
            raise HTTPException(
                status_code=400,
                detail="Field 'low_bound' must be a numeric value.",
            )
    if high_bound is not None:
        try:
            high_bound_val = float(high_bound)
            data["high_bound"] = high_bound_val
        except ValueError, TypeError:
            raise HTTPException(
                status_code=400,
                detail="Field 'high_bound' must be a numeric value.",
            )

    if low_bound is not None and high_bound is not None:
        if float(low_bound) > float(high_bound):
            raise HTTPException(
                status_code=400,
                detail="Field 'low_bound' must be less than or equal to 'high_bound'.",
            )

    critical_low = data.get("critical_low")
    critical_high = data.get("critical_high")
    if critical_low is not None:
        try:
            critical_low_val = float(critical_low)
            data["critical_low"] = critical_low_val
        except ValueError, TypeError:
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_low' must be a numeric value.",
            )
    if critical_high is not None:
        try:
            critical_high_val = float(critical_high)
            data["critical_high"] = critical_high_val
        except ValueError, TypeError:
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_high' must be a numeric value.",
            )

    if critical_low is not None and critical_high is not None:
        if float(critical_low) > float(critical_high):
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_low' must be less than or equal to 'critical_high'.",
            )

    if critical_low is not None and low_bound is not None:
        if float(critical_low) > float(low_bound):
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_low' must be less than or equal to 'low_bound'.",
            )

    if critical_high is not None and high_bound is not None:
        if float(critical_high) < float(high_bound):
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_high' must be greater than or equal to 'high_bound'.",
            )


@router.post(
    "/lab-ranges",
    response_model=LabReferenceRangeResponse,
    status_code=201,
)
async def create_lab_range(
    request: Request,
    payload: LabReferenceRangeCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabReferenceRangeResponse:
    """Create a new lab reference range."""
    validate_lab_range_payload(payload.model_dump())

    source_val = (
        payload.source.value
        if hasattr(payload.source, "value")
        else str(payload.source)
    )

    user_id = (
        request.headers.get("X-User-Id")
        or getattr(request.state, "user_id", None)
        or current_user_id.get()
        or "system_user"
    )
    reason = (
        request.headers.get("X-Change-Reason")
        or current_change_reason.get()
        or payload.reason_for_change
        or "Create laboratory reference range"
    )

    with audit_context(user_id=user_id, change_reason=reason):
        range_record = LabReferenceRange(
            study_id=payload.study_id,
            test_code=payload.test_code,
            test_name=payload.test_name,
            source=source_val,
            site_id=payload.site_id,
            unit=payload.unit,
            normalized_unit=payload.normalized_unit,
            sex_applicability=payload.sex_applicability,
            ethnicity_applicability=payload.ethnicity_applicability,
            age_low=payload.age_low,
            age_high=payload.age_high,
            low_bound=payload.low_bound,
            high_bound=payload.high_bound,
            critical_low=payload.critical_low,
            critical_high=payload.critical_high,
            version=1,
        )
        session.add(range_record)
        await session.commit()
        await session.refresh(range_record)

    from apps.execution.lab_range_cache import lab_range_cache

    lab_range_cache.invalidate(payload.study_id, payload.test_code)

    return LabReferenceRangeResponse(
        id=range_record.id,
        study_id=range_record.study_id,
        test_code=range_record.test_code,
        test_name=range_record.test_name,
        source=range_record.source,
        site_id=range_record.site_id,
        unit=range_record.unit,
        normalized_unit=range_record.normalized_unit,
        sex_applicability=range_record.sex_applicability,
        ethnicity_applicability=range_record.ethnicity_applicability,
        age_low=range_record.age_low,
        age_high=range_record.age_high,
        low_bound=range_record.low_bound,
        high_bound=range_record.high_bound,
        critical_low=range_record.critical_low,
        critical_high=range_record.critical_high,
        version=range_record.version,
        is_deleted=range_record.is_deleted,
    )


@router.get(
    "/lab-ranges",
    response_model=list[LabReferenceRangeResponse],
)
async def list_lab_ranges(
    study_id: str | None = Query(None, description="Filter by study ID"),
    test_code: str | None = Query(None, description="Filter by test code"),
    source: str | None = Query(None, description="Filter by source"),
    lab_source: str | None = Query(None, description="Filter by lab source"),
    include_deleted: bool = Query(False, description="Include deleted ranges"),
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[LabReferenceRangeResponse]:
    """List lab reference ranges matching filters."""
    stmt = select(LabReferenceRange)
    if not include_deleted:
        stmt = stmt.where(LabReferenceRange.is_deleted.is_(False))
    if study_id:
        stmt = stmt.where(LabReferenceRange.study_id == study_id)
    if test_code:
        stmt = stmt.where(LabReferenceRange.test_code == test_code)
    src_filter = source or lab_source
    if src_filter:
        stmt = stmt.where(LabReferenceRange.source == src_filter)

    res = await session.execute(stmt)
    ranges = res.scalars().all()

    return [
        LabReferenceRangeResponse(
            id=r.id,
            study_id=r.study_id,
            test_code=r.test_code,
            test_name=r.test_name,
            source=r.source,
            site_id=r.site_id,
            unit=r.unit,
            normalized_unit=r.normalized_unit,
            sex_applicability=r.sex_applicability,
            ethnicity_applicability=r.ethnicity_applicability,
            age_low=r.age_low,
            age_high=r.age_high,
            low_bound=r.low_bound,
            high_bound=r.high_bound,
            critical_low=r.critical_low,
            critical_high=r.critical_high,
            version=r.version,
            is_deleted=r.is_deleted,
        )
        for r in ranges
    ]


@router.get(
    "/lab-ranges/{range_id}",
    response_model=LabReferenceRangeResponse,
)
async def get_lab_range(
    range_id: str,
    roles: list[str] = Depends(get_normalized_roles),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabReferenceRangeResponse:
    """Retrieve a single lab reference range."""
    stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
    res = await session.execute(stmt)
    r = res.scalars().first()
    if not r:
        raise HTTPException(status_code=404, detail="LabReferenceRange not found")

    return LabReferenceRangeResponse(
        id=r.id,
        study_id=r.study_id,
        test_code=r.test_code,
        test_name=r.test_name,
        source=r.source,
        site_id=r.site_id,
        unit=r.unit,
        normalized_unit=r.normalized_unit,
        sex_applicability=r.sex_applicability,
        ethnicity_applicability=r.ethnicity_applicability,
        age_low=r.age_low,
        age_high=r.age_high,
        low_bound=r.low_bound,
        high_bound=r.high_bound,
        critical_low=r.critical_low,
        critical_high=r.critical_high,
        version=r.version,
        is_deleted=r.is_deleted,
    )


@router.put(
    "/lab-ranges/{range_id}",
    response_model=LabReferenceRangeResponse,
)
async def update_lab_range(
    range_id: str,
    payload: LabReferenceRangeUpdate,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabReferenceRangeResponse:
    """Update an existing lab reference range."""
    stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
    res = await session.execute(stmt)
    r = res.scalars().first()
    if not r:
        raise HTTPException(status_code=404, detail="LabReferenceRange not found")

    payload_dict = payload.model_dump(exclude_unset=True)
    merged_data = {
        "study_id": payload_dict.get("study_id", r.study_id),
        "test_code": payload_dict.get("test_code", r.test_code),
        "test_name": payload_dict.get("test_name", r.test_name),
        "source": payload_dict.get("source", r.source),
        "site_id": payload_dict.get("site_id", r.site_id),
        "unit": payload_dict.get("unit", r.unit),
        "normalized_unit": payload_dict.get("normalized_unit", r.normalized_unit),
        "sex_applicability": payload_dict.get("sex_applicability", r.sex_applicability),
        "ethnicity_applicability": payload_dict.get(
            "ethnicity_applicability", r.ethnicity_applicability
        ),
        "age_low": payload_dict.get("age_low", r.age_low),
        "age_high": payload_dict.get("age_high", r.age_high),
        "low_bound": payload_dict.get("low_bound", r.low_bound),
        "high_bound": payload_dict.get("high_bound", r.high_bound),
        "critical_low": payload_dict.get("critical_low", r.critical_low),
        "critical_high": payload_dict.get("critical_high", r.critical_high),
    }

    validate_lab_range_payload(merged_data)

    old_study_id = r.study_id
    old_test_code = r.test_code

    src = merged_data["source"]
    source_str = src.value if hasattr(src, "value") else str(src)

    user_id = (
        request.headers.get("X-User-Id")
        or getattr(request.state, "user_id", None)
        or current_user_id.get()
        or "system_user"
    )
    reason = (
        request.headers.get("X-Change-Reason")
        or current_change_reason.get()
        or payload.reason_for_change
        or "Update laboratory reference range"
    )

    with audit_context(user_id=user_id, change_reason=reason):
        r.study_id = merged_data["study_id"]
        r.test_code = merged_data["test_code"]
        r.test_name = merged_data["test_name"]
        r.source = source_str
        r.site_id = merged_data["site_id"]
        r.unit = merged_data["unit"]
        r.normalized_unit = merged_data["normalized_unit"]
        r.sex_applicability = merged_data["sex_applicability"]
        r.ethnicity_applicability = merged_data["ethnicity_applicability"]
        r.age_low = merged_data["age_low"]
        r.age_high = merged_data["age_high"]
        r.low_bound = merged_data["low_bound"]
        r.high_bound = merged_data["high_bound"]
        r.critical_low = merged_data["critical_low"]
        r.critical_high = merged_data["critical_high"]
        r.version += 1

        session.add(r)
        await session.commit()
        await session.refresh(r)

    from apps.execution.lab_range_cache import lab_range_cache

    lab_range_cache.invalidate(old_study_id, old_test_code)
    lab_range_cache.invalidate(r.study_id, r.test_code)

    return LabReferenceRangeResponse(
        id=r.id,
        study_id=r.study_id,
        test_code=r.test_code,
        test_name=r.test_name,
        source=r.source,
        site_id=r.site_id,
        unit=r.unit,
        normalized_unit=r.normalized_unit,
        sex_applicability=r.sex_applicability,
        ethnicity_applicability=r.ethnicity_applicability,
        age_low=r.age_low,
        age_high=r.age_high,
        low_bound=r.low_bound,
        high_bound=r.high_bound,
        critical_low=r.critical_low,
        critical_high=r.critical_high,
        version=r.version,
        is_deleted=r.is_deleted,
    )


@router.delete(
    "/lab-ranges/{range_id}",
    response_model=LabReferenceRangeResponse,
)
async def delete_lab_range(
    range_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabReferenceRangeResponse:
    """Soft-delete a lab reference range by setting is_deleted = True."""
    stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
    res = await session.execute(stmt)
    r = res.scalars().first()
    if not r:
        raise HTTPException(status_code=404, detail="LabReferenceRange not found")

    user_id = (
        request.headers.get("X-User-Id")
        or getattr(request.state, "user_id", None)
        or current_user_id.get()
        or "system_user"
    )
    reason = (
        request.headers.get("X-Change-Reason")
        or current_change_reason.get()
        or "Soft delete laboratory reference range"
    )

    with audit_context(user_id=user_id, change_reason=reason):
        r.is_deleted = True
        session.add(r)
        await session.commit()
        await session.refresh(r)

    from apps.execution.lab_range_cache import lab_range_cache

    lab_range_cache.invalidate(r.study_id, r.test_code)

    return LabReferenceRangeResponse(
        id=r.id,
        study_id=r.study_id,
        test_code=r.test_code,
        test_name=r.test_name,
        source=r.source,
        site_id=r.site_id,
        unit=r.unit,
        normalized_unit=r.normalized_unit,
        sex_applicability=r.sex_applicability,
        ethnicity_applicability=r.ethnicity_applicability,
        age_low=r.age_low,
        age_high=r.age_high,
        low_bound=r.low_bound,
        high_bound=r.high_bound,
        critical_low=r.critical_low,
        critical_high=r.critical_high,
        version=r.version,
        is_deleted=r.is_deleted,
    )


class LabRangeRecalculateRequest(BaseModel):
    """Pydantic schema for triggering lab range recalculations."""

    study_id: str
    test_code: str


class LabRangeRecalculateResponse(BaseModel):
    """Pydantic schema returning recalculation status."""

    status: str
    study_id: str
    test_code: str
    updated_count: int


@router.post(
    "/lab-ranges/recalculate",
    response_model=LabRangeRecalculateResponse,
)
async def trigger_lab_range_recalculation(
    payload: LabRangeRecalculateRequest,
    background_tasks: BackgroundTasks,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabRangeRecalculateResponse:
    """Trigger cohort-wide reference range evaluation and recalculation on-demand."""
    from apps.execution.lab_range_cache import lab_range_cache
    from apps.execution.lab_ranges import recalculate_range_flags

    lab_range_cache.invalidate(payload.study_id, payload.test_code)

    count = await recalculate_range_flags(
        session=session,
        study_id=payload.study_id,
        test_code=payload.test_code,
        background_tasks=background_tasks,
    )

    return LabRangeRecalculateResponse(
        status="success",
        study_id=payload.study_id,
        test_code=payload.test_code,
        updated_count=count,
    )
