"""FastAPI Router for SMART on FHIR eSource Ingestion Pipeline and CRC Reconciliation Drawer.

Requirements: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, ADR-2199, FDA Electronic Source Data Guidance
"""

from datetime import datetime

from fastapi import APIRouter, Depends, HTTPException, Request, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.application import (
    EsourceIngestionService,
    EsourceReconciliationService,
    ReconciliationError,
    SmartAuthError,
    SmartAuthService,
)
from apps.interop.auth import require_staff_role
from apps.interop.domain.esource_models import EhrEndpointConfig
from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.esource_repository import (
    EsourceReconciliationRepository,
)
from apps.interop.infrastructure.models import (
    EhrEndpointRecord,
    EhrReconciliationCandidateRecord,
    InteropAuditLog,
)
from apps.interop.presentation.esource_dtos import (
    EhrCandidateItemDTO,
    EhrCandidateListResponse,
    EhrEndpointCreateRequest,
    EhrEndpointResponse,
    EhrIngestBundleRequest,
    EhrIngestBundleResponse,
    EhrPollRequest,
    EhrPollResponse,
    EhrProvenanceResponse,
    EhrReconcileAcceptRequest,
    EhrReconcileActionResponse,
    EhrReconcileQueryRequest,
    EhrReconcileRejectRequest,
    SmartAssertionVerifyRequest,
    SmartAssertionVerifyResponse,
    SmartClientAssertionRequest,
    SmartClientAssertionResponse,
)
from packages.database import DatabaseSessionDependency

router = APIRouter(prefix="/api/v1/interop/esource", tags=["eSource & SMART on FHIR"])
get_db_session = DatabaseSessionDependency(db_manager)

smart_auth_service = SmartAuthService()
ingestion_service = EsourceIngestionService(smart_auth=smart_auth_service)
reconciliation_service = EsourceReconciliationService()


def _to_candidate_dto(rec: EhrReconciliationCandidateRecord) -> EhrCandidateItemDTO:
    """Convert an EhrReconciliationCandidateRecord ORM entity to EhrCandidateItemDTO."""
    return EhrCandidateItemDTO(
        id=rec.id,
        study_id=rec.study_id,
        site_id=rec.site_id,
        subject_id=rec.subject_id,
        subject_pseudonym=rec.subject_pseudonym,
        ehr_system_id=rec.ehr_system_id,
        fhir_resource_type=rec.fhir_resource_type,
        fhir_resource_id=rec.fhir_resource_id,
        source_timestamp=rec.source_timestamp.isoformat()
        if isinstance(rec.source_timestamp, datetime)
        else str(rec.source_timestamp),
        target_domain=rec.target_domain,
        target_variable=rec.target_variable,
        cdash_testcd=rec.cdash_testcd,
        cdash_test=rec.cdash_test,
        candidate_value=rec.candidate_value,
        candidate_unit=rec.candidate_unit,
        current_ecrf_value=rec.current_ecrf_value,
        current_ecrf_updated_at=rec.current_ecrf_updated_at.isoformat()
        if isinstance(rec.current_ecrf_updated_at, datetime)
        else None,
        discrepancy_type=rec.discrepancy_type,
        confidence_score=rec.confidence_score,
        mapping_provenance=rec.mapping_provenance,
        raw_fhir_resource=rec.raw_fhir_resource,
        status=rec.status,
        resolution_action=rec.resolution_action,
        resolution_reason=rec.resolution_reason,
        resolved_by=rec.resolved_by,
        resolved_at=rec.resolved_at.isoformat()
        if isinstance(rec.resolved_at, datetime)
        else None,
        electronic_signature=rec.electronic_signature,
        clinical_query_id=rec.clinical_query_id,
        created_at=rec.created_at.isoformat()
        if isinstance(rec.created_at, datetime)
        else None,
        version_index=rec.version_index,
    )


# ==============================================================================
# 1. Hospital EHR Endpoint Management
# ==============================================================================


@router.post(
    "/endpoints",
    response_model=EhrEndpointResponse,
    status_code=status.HTTP_201_CREATED,
)
async def register_ehr_endpoint(
    request: Request,
    payload: EhrEndpointCreateRequest,
    session: AsyncSession = Depends(get_db_session),
) -> EhrEndpointResponse:
    """Register a new hospital EHR FHIR endpoint with SMART Backend Services credentials."""
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Register EHR Endpoint")

    rec = EhrEndpointRecord(
        name=payload.name,
        system_type=str(payload.system_type),
        fhir_base_url=payload.fhir_base_url,
        token_url=payload.token_url,
        client_id=payload.client_id,
        public_key_jwks_url=payload.public_key_jwks_url,
        scopes=payload.scopes,
        polling_interval_sec=payload.polling_interval_sec,
        is_active=payload.is_active,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(rec)
    await session.flush()

    audit = InteropAuditLog(
        user_id=user_id,
        user_role=user_roles,
        action="EHR_ENDPOINT_REGISTERED",
        details=f"Registered EHR endpoint '{payload.name}' ({payload.system_type}) with base URL '{payload.fhir_base_url}'.",
        change_reason=change_reason,
    )
    session.add(audit)
    await session.flush()

    return EhrEndpointResponse(
        endpoint_id=rec.id,
        name=rec.name,
        system_type=rec.system_type,
        fhir_base_url=rec.fhir_base_url,
        token_url=rec.token_url,
        client_id=rec.client_id,
        public_key_jwks_url=rec.public_key_jwks_url,
        scopes=rec.scopes,
        polling_interval_sec=rec.polling_interval_sec,
        is_active=rec.is_active,
        last_polled_at=rec.last_polled_at,
        created_at=rec.created_at,
        version_index=rec.version_index,
    )


@router.get("/endpoints", response_model=list[EhrEndpointResponse])
async def list_ehr_endpoints(
    request: Request,
    active_only: bool = True,
    session: AsyncSession = Depends(get_db_session),
) -> list[EhrEndpointResponse]:
    """List configured hospital EHR endpoints."""
    require_staff_role(request)

    query = select(EhrEndpointRecord)
    if active_only:
        query = query.where(EhrEndpointRecord.is_active.is_(True))

    result = await session.execute(query)
    endpoints = result.scalars().all()

    return [
        EhrEndpointResponse(
            endpoint_id=rec.id,
            name=rec.name,
            system_type=rec.system_type,
            fhir_base_url=rec.fhir_base_url,
            token_url=rec.token_url,
            client_id=rec.client_id,
            public_key_jwks_url=rec.public_key_jwks_url,
            scopes=rec.scopes,
            polling_interval_sec=rec.polling_interval_sec,
            is_active=rec.is_active,
            last_polled_at=rec.last_polled_at,
            created_at=rec.created_at,
            version_index=rec.version_index,
        )
        for rec in endpoints
    ]


# ==============================================================================
# 2. SMART Backend Services Authentication Helpers
# ==============================================================================


@router.post("/smart/assertion", response_model=SmartClientAssertionResponse)
async def generate_client_assertion(
    request: Request,
    payload: SmartClientAssertionRequest,
) -> SmartClientAssertionResponse:
    """Generate an asymmetric signed client assertion JWT for SMART on FHIR Backend Services OAuth2."""
    require_staff_role(request)
    try:
        jwt_str = smart_auth_service.create_client_assertion(
            client_id=payload.client_id,
            token_endpoint=payload.token_endpoint,
            private_key_pem=payload.private_key_pem,
            algorithm=payload.algorithm,
            key_id=payload.key_id,
            expiration_seconds=payload.expiration_seconds,
        )
        return SmartClientAssertionResponse(
            client_assertion_jwt=jwt_str,
            token_endpoint=payload.token_endpoint,
            algorithm=payload.algorithm,
            expires_in_seconds=payload.expiration_seconds,
        )
    except SmartAuthError as exc:
        raise HTTPException(status_code=exc.status_code, detail=exc.message) from exc


@router.post("/smart/verify", response_model=SmartAssertionVerifyResponse)
async def verify_client_assertion(
    request: Request,
    payload: SmartAssertionVerifyRequest,
) -> SmartAssertionVerifyResponse:
    """Verify an asymmetric client assertion JWT per SMART Backend Services rules."""
    require_staff_role(request)
    try:
        claims = smart_auth_service.verify_client_assertion(
            assertion_jwt=payload.assertion_jwt,
            public_key_pem=payload.public_key_pem,
            expected_audience=payload.expected_audience,
            expected_client_id=payload.expected_client_id,
        )
        return SmartAssertionVerifyResponse(is_valid=True, claims=claims)
    except SmartAuthError as exc:
        return SmartAssertionVerifyResponse(is_valid=False, error_message=exc.message)


# ==============================================================================
# 3. Live EHR Polling and FHIR Ingestion Pipeline
# ==============================================================================


@router.post("/poll", response_model=EhrPollResponse)
async def poll_hospital_ehr(
    request: Request,
    payload: EhrPollRequest,
    session: AsyncSession = Depends(get_db_session),
) -> EhrPollResponse:
    """Trigger automated SMART on FHIR polling against a registered hospital EHR endpoint."""
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Automated EHR Poll")

    # Fetch endpoint
    stmt = select(EhrEndpointRecord).where(EhrEndpointRecord.id == payload.endpoint_id)
    result = await session.execute(stmt)
    rec = result.scalars().first()
    if not rec:
        raise HTTPException(
            status_code=404, detail=f"EHR Endpoint '{payload.endpoint_id}' not found"
        )

    endpoint_config = EhrEndpointConfig(
        endpoint_id=rec.id,
        name=rec.name,
        system_type=rec.system_type,
        fhir_base_url=rec.fhir_base_url,
        token_url=rec.token_url,
        client_id=rec.client_id,
        public_key_jwks_url=rec.public_key_jwks_url,
        scopes=rec.scopes,
        polling_interval_sec=rec.polling_interval_sec,
        is_active=rec.is_active,
    )

    try:
        candidates = await ingestion_service.poll_hospital_ehr(
            endpoint_config=endpoint_config,
            private_key_pem=payload.private_key_pem,
            study_id=payload.study_id,
            site_id=payload.site_id,
            subject_id=payload.subject_id,
            current_ecrf_context=payload.current_ecrf_context,
        )
    except SmartAuthError as exc:
        raise HTTPException(
            status_code=exc.status_code,
            detail=f"EHR Authentication failed: {exc.message}",
        ) from exc

    repo = EsourceReconciliationRepository(session)
    saved_records = await reconciliation_service.save_candidates(
        repository=repo,
        candidates=candidates,
        user_id=user_id,
        user_role=user_roles,
        change_reason=change_reason,
    )

    rec.last_polled_at = datetime.now()
    await session.flush()

    return EhrPollResponse(
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        total_ingested=len(saved_records),
        candidates=[_to_candidate_dto(r) for r in saved_records],
    )


@router.post("/ingest", response_model=EhrIngestBundleResponse)
async def ingest_fhir_bundle(
    request: Request,
    payload: EhrIngestBundleRequest,
    session: AsyncSession = Depends(get_db_session),
) -> EhrIngestBundleResponse:
    """Ingest a standard FHIR Bundle, normalize to CDASH domain models, and populate candidate reconciliation queue."""
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(
        request.state, "change_reason", "Direct FHIR Bundle Ingestion"
    )

    candidates = ingestion_service.normalize_fhir_bundle(
        bundle=payload.bundle,
        study_id=payload.study_id,
        site_id=payload.site_id,
        ehr_system_id=payload.ehr_system_id,
        current_ecrf_context=payload.current_ecrf_context,
    )

    repo = EsourceReconciliationRepository(session)
    saved_records = await reconciliation_service.save_candidates(
        repository=repo,
        candidates=candidates,
        user_id=user_id,
        user_role=user_roles,
        change_reason=change_reason,
    )

    return EhrIngestBundleResponse(
        study_id=payload.study_id,
        total_ingested=len(saved_records),
        candidates=[_to_candidate_dto(r) for r in saved_records],
    )


# ==============================================================================
# 4. CRC Reconciliation Drawer & Review Workflow (Non-Destructive Actions)
# ==============================================================================


@router.get("/candidates", response_model=EhrCandidateListResponse)
async def list_candidates(
    request: Request,
    study_id: str | None = None,
    subject_id: str | None = None,
    site_id: str | None = None,
    domain: str | None = None,
    status: str | None = None,
    skip: int = 0,
    limit: int = 100,
    session: AsyncSession = Depends(get_db_session),
) -> EhrCandidateListResponse:
    """Retrieve candidate reconciliation items with filtering for the split-screen drawer."""
    require_staff_role(request)
    repo = EsourceReconciliationRepository(session)
    records, total = await reconciliation_service.list_candidates(
        repository=repo,
        study_id=study_id,
        subject_id=subject_id,
        site_id=site_id,
        domain=domain,
        status=status,
        skip=skip,
        limit=limit,
    )
    return EhrCandidateListResponse(
        total_count=total,
        candidates=[_to_candidate_dto(r) for r in records],
    )


@router.get("/candidates/{candidate_id}", response_model=EhrCandidateItemDTO)
async def get_candidate(
    candidate_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> EhrCandidateItemDTO:
    """Get details of a specific candidate reconciliation item."""
    require_staff_role(request)
    repo = EsourceReconciliationRepository(session)
    rec = await reconciliation_service.get_candidate_by_id(
        repository=repo, candidate_id=candidate_id
    )
    if not rec:
        raise HTTPException(
            status_code=404, detail=f"Candidate '{candidate_id}' not found"
        )
    return _to_candidate_dto(rec)


@router.post("/reconcile/accept", response_model=EhrReconcileActionResponse)
async def accept_candidate(
    request: Request,
    payload: EhrReconcileAcceptRequest,
    session: AsyncSession = Depends(get_db_session),
) -> EhrReconcileActionResponse:
    """1-Click 'Accept & Commit' candidate EHR data into eCRF with Part 11 audit attribution."""
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = payload.change_reason or getattr(
        request.state, "change_reason", "CRC Accept & Commit"
    )

    repo = EsourceReconciliationRepository(session)
    try:
        updated_rec = await reconciliation_service.accept_and_commit(
            repository=repo,
            candidate_id=payload.candidate_id,
            user_id=user_id,
            user_role=user_roles,
            signature_token=payload.signature_token,
            commit_reason=payload.commit_reason,
            change_reason=change_reason,
        )
        return EhrReconcileActionResponse(
            success=True,
            candidate=_to_candidate_dto(updated_rec),
            message=f"Candidate '{updated_rec.target_variable}' committed successfully.",
        )
    except ReconciliationError as exc:
        raise HTTPException(status_code=exc.status_code, detail=exc.message) from exc


@router.post("/reconcile/reject", response_model=EhrReconcileActionResponse)
async def reject_candidate(
    request: Request,
    payload: EhrReconcileRejectRequest,
    session: AsyncSession = Depends(get_db_session),
) -> EhrReconcileActionResponse:
    """1-Click 'Reject with Reason' to discard candidate EHR data with mandatory justification."""
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = payload.change_reason or getattr(
        request.state, "change_reason", "CRC Reject Candidate"
    )

    repo = EsourceReconciliationRepository(session)
    try:
        updated_rec = await reconciliation_service.reject_candidate(
            repository=repo,
            candidate_id=payload.candidate_id,
            user_id=user_id,
            user_role=user_roles,
            rejection_reason=payload.rejection_reason,
            change_reason=change_reason,
        )
        return EhrReconcileActionResponse(
            success=True,
            candidate=_to_candidate_dto(updated_rec),
            message=f"Candidate '{updated_rec.target_variable}' rejected with reason.",
        )
    except ReconciliationError as exc:
        raise HTTPException(status_code=exc.status_code, detail=exc.message) from exc


@router.post("/reconcile/query", response_model=EhrReconcileActionResponse)
async def raise_clinical_query(
    request: Request,
    payload: EhrReconcileQueryRequest,
    session: AsyncSession = Depends(get_db_session),
) -> EhrReconcileActionResponse:
    """1-Click 'Raise Clinical Query' to open a formal GxP discrepancy query for an EHR-to-eCRF mismatch."""
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = payload.change_reason or getattr(
        request.state, "change_reason", "CRC Raise Discrepancy Query"
    )

    repo = EsourceReconciliationRepository(session)
    try:
        updated_rec, query = await reconciliation_service.raise_clinical_query(
            repository=repo,
            candidate_id=payload.candidate_id,
            user_id=user_id,
            user_role=user_roles,
            query_explanation=payload.query_explanation,
            discrepancy_type=payload.discrepancy_type,
            change_reason=change_reason,
        )
        return EhrReconcileActionResponse(
            success=True,
            candidate=_to_candidate_dto(updated_rec),
            clinical_query_id=query.id,
            message=f"Clinical Query '{query.id}' successfully raised for '{updated_rec.target_variable}'.",
        )
    except ReconciliationError as exc:
        raise HTTPException(status_code=exc.status_code, detail=exc.message) from exc


# ==============================================================================
# 5. Provenance & Audit Trail Inspector
# ==============================================================================


@router.get("/provenance/{candidate_id}", response_model=EhrProvenanceResponse)
async def get_provenance_audit(
    candidate_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> EhrProvenanceResponse:
    """Retrieve complete immutable FHIR source provenance and Part 11 audit ledger for a record."""
    require_staff_role(request)
    repo = EsourceReconciliationRepository(session)
    try:
        prov = await reconciliation_service.get_provenance_audit(
            repository=repo, candidate_id=candidate_id
        )
        return EhrProvenanceResponse(
            candidate_id=prov["candidate_id"],
            study_id=prov["study_id"],
            site_id=prov["site_id"],
            subject_id=prov["subject_id"],
            subject_pseudonym=prov["subject_pseudonym"],
            ehr_system_id=prov["ehr_system_id"],
            fhir_resource_type=prov["fhir_resource_type"],
            fhir_resource_id=prov["fhir_resource_id"],
            source_timestamp=prov["source_timestamp"],
            target_domain=prov["target_domain"],
            target_variable=prov["target_variable"],
            cdash_testcd=prov["cdash_testcd"],
            cdash_test=prov["cdash_test"],
            candidate_value=prov["candidate_value"],
            candidate_unit=prov["candidate_unit"],
            current_ecrf_value=prov["current_ecrf_value"],
            discrepancy_type=prov["discrepancy_type"],
            confidence_score=prov["confidence_score"],
            mapping_provenance=prov["mapping_provenance"],
            raw_fhir_resource=prov["raw_fhir_resource"],
            status=prov["status"],
            resolution_action=prov["resolution_action"],
            resolution_reason=prov["resolution_reason"],
            resolved_by=prov["resolved_by"],
            resolved_at=prov["resolved_at"],
            electronic_signature=prov["electronic_signature"],
            clinical_query_id=prov["clinical_query_id"],
            audit_trail=prov["audit_trail"],
        )
    except ReconciliationError as exc:
        raise HTTPException(status_code=exc.status_code, detail=exc.message) from exc
