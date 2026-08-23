"""Data Integrity Sentinels Router (Duplicate Patient, Temporal & pHash Engines).

Exposes REST APIs for automated cross-site forensic data integrity sentinels,
duplicate patient record matching, temporal/behavioral anomaly scanning,
perceptual image hashing, and 21 CFR Part 11 investigation dispatch.
"""

from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import (
    DataIntegrityFindingRecord,
    DataIntegritySentinelRun,
)
from apps.quality.application.services.data_integrity_sentinel_service import (
    DataIntegritySentinelService,
)
from apps.quality.presentation.dtos import (
    DataIntegrityComprehensiveScanRequest,
    DataIntegrityFindingResponse,
    DataIntegrityInvestigationDispatchRequest,
    DataIntegritySentinelRunResponse,
    DataIntegritySimulationRequest,
    DataIntegritySimulationResponse,
    DuplicatePatientScanRequest,
    ImageHashScanRequest,
    TemporalAnomalyScanRequest,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter()


def get_data_integrity_service() -> DataIntegritySentinelService:
    import apps.quality.main as main_module

    return main_module.get_data_integrity_service()


def get_user_context(principal: Principal):
    import apps.quality.main as main_module

    return main_module.get_user_context(principal)


def authorize_quality_write(principal: Principal) -> list[str]:
    if not has_permission(principal, "quality_event:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Read-only roles are restricted to read-only access.",
        )
    return principal.roles


def map_finding_to_response(
    rec: DataIntegrityFindingRecord,
) -> DataIntegrityFindingResponse:
    return DataIntegrityFindingResponse(
        id=rec.id,
        run_id=rec.run_id,
        study_id=rec.study_id,
        sentinel_type=rec.sentinel_type,
        severity=rec.severity,
        anomaly_flag=rec.anomaly_flag,
        primary_subject_id=rec.primary_subject_id,
        primary_site_id=rec.primary_site_id,
        matched_subject_id=rec.matched_subject_id,
        matched_site_id=rec.matched_site_id,
        confidence_score=rec.confidence_score,
        similarity_score=rec.similarity_score,
        hamming_distance=rec.hamming_distance,
        transformation_type=rec.transformation_type,
        evidence_summary=rec.evidence_summary,
        forensic_metadata=rec.forensic_metadata
        if isinstance(rec.forensic_metadata, dict)
        else {},
        investigation_deviation_id=rec.investigation_deviation_id,
        kri_evaluation_id=rec.kri_evaluation_id,
        status=rec.status,
        created_at=rec.created_at.isoformat()
        if hasattr(rec.created_at, "isoformat")
        else str(rec.created_at),
        created_by=rec.created_by,
        version_index=rec.version_index,
        reason_for_change=rec.reason_for_change,
    )


def map_run_to_response(
    run: DataIntegritySentinelRun,
) -> DataIntegritySentinelRunResponse:
    findings = [map_finding_to_response(f) for f in (run.findings or [])]
    return DataIntegritySentinelRunResponse(
        id=run.id,
        study_id=run.study_id,
        run_name=run.run_name,
        scan_timestamp=run.scan_timestamp.isoformat()
        if hasattr(run.scan_timestamp, "isoformat")
        else str(run.scan_timestamp),
        sentinel_types=run.sentinel_types
        if isinstance(run.sentinel_types, list)
        else [],
        total_subjects_scanned=run.total_subjects_scanned,
        total_images_scanned=run.total_images_scanned,
        total_events_scanned=run.total_events_scanned,
        anomalies_detected_count=run.anomalies_detected_count,
        risk_tier=run.risk_tier,
        status=run.status,
        summary_findings=run.summary_findings
        if isinstance(run.summary_findings, dict)
        else {},
        findings=findings,
        created_at=run.created_at.isoformat()
        if hasattr(run.created_at, "isoformat")
        else str(run.created_at),
        created_by=run.created_by,
        version_index=run.version_index,
        reason_for_change=run.reason_for_change,
    )


@router.post(
    "/api/v1/quality/sentinels/duplicate-patients/scan",
    response_model=DataIntegritySentinelRunResponse,
    status_code=201,
)
@transactional
async def execute_duplicate_patient_scan(
    request: Request,
    payload: DuplicatePatientScanRequest,
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Executes cross-site similarity matching across baseline clinical feature vectors."""
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)

    subjects = payload.raw_subjects or []
    if not subjects and payload.subjects:
        subjects = [s.model_dump() for s in payload.subjects]

    if not subjects:
        sim_data = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
            study_id=payload.study_id
        )
        subjects = sim_data["subjects"]

    run = await service.execute_duplicate_patient_scan(
        study_id=payload.study_id,
        subjects=subjects,
        similarity_threshold=payload.similarity_threshold,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason
        or "Automated Duplicate Patient Record Sentinel execution",
        auto_dispatch_investigation=payload.auto_dispatch_investigation,
    )
    return map_run_to_response(run)


@router.post(
    "/api/v1/quality/sentinels/temporal-anomalies/scan",
    response_model=DataIntegritySentinelRunResponse,
    status_code=201,
)
@transactional
async def execute_temporal_anomaly_scan(
    request: Request,
    payload: TemporalAnomalyScanRequest,
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Detects off-hours entry clusters, rapid burst submissions, and identical procedure intervals."""
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)

    events = payload.raw_events or []
    if not events and payload.audit_events:
        events = [e.model_dump() for e in payload.audit_events]

    if not events:
        sim_data = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
            study_id=payload.study_id
        )
        events = sim_data["audit_events"]

    run = await service.execute_temporal_anomaly_scan(
        study_id=payload.study_id,
        audit_events=events,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason
        or "Automated Temporal & Behavioral Anomaly Scan execution",
        auto_dispatch_investigation=payload.auto_dispatch_investigation,
    )
    return map_run_to_response(run)


@router.post(
    "/api/v1/quality/sentinels/image-phash/scan",
    response_model=DataIntegritySentinelRunResponse,
    status_code=201,
)
@transactional
async def execute_image_phash_scan(
    request: Request,
    payload: ImageHashScanRequest,
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Computes DCT-based perceptual image hashes to detect reused and transformed radiological scans."""
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)

    images = payload.raw_images or []
    if not images and payload.image_attachments:
        images = [img.model_dump() for img in payload.image_attachments]

    if not images:
        sim_data = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
            study_id=payload.study_id
        )
        images = sim_data["image_attachments"]

    run = await service.execute_image_phash_scan(
        study_id=payload.study_id,
        image_attachments=images,
        hamming_threshold=payload.hamming_threshold,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason
        or "Automated Perceptual Image Hashing Sentinel execution",
        auto_dispatch_investigation=payload.auto_dispatch_investigation,
    )
    return map_run_to_response(run)


@router.post(
    "/api/v1/quality/sentinels/data-integrity/comprehensive-scan",
    response_model=DataIntegritySentinelRunResponse,
    status_code=201,
)
@transactional
async def execute_comprehensive_data_integrity_scan(
    request: Request,
    payload: DataIntegrityComprehensiveScanRequest,
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Executes a unified comprehensive data integrity scan across duplicate records, timing, and images."""
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)

    subjects = [s.model_dump() for s in payload.subjects] if payload.subjects else None
    events = (
        [e.model_dump() for e in payload.audit_events] if payload.audit_events else None
    )
    images = (
        [img.model_dump() for img in payload.image_attachments]
        if payload.image_attachments
        else None
    )

    if not subjects and not events and not images:
        sim_data = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
            study_id=payload.study_id
        )
        subjects = sim_data["subjects"]
        events = sim_data["audit_events"]
        images = sim_data["image_attachments"]

    run = await service.execute_comprehensive_scan(
        study_id=payload.study_id,
        subjects=subjects,
        audit_events=events,
        image_attachments=images,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason
        or "Automated Comprehensive Data Integrity Sentinel execution",
        auto_dispatch_investigation=payload.auto_dispatch_investigation,
    )
    return map_run_to_response(run)


@router.get(
    "/api/v1/quality/sentinels/runs",
    response_model=list[DataIntegritySentinelRunResponse],
)
@transactional
async def list_data_integrity_runs(
    request: Request,
    study_id: str | None = Query(None, description="Optional study ID filter"),
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Lists historical data integrity sentinel runs."""
    runs = await service.list_runs(study_id)
    return [map_run_to_response(r) for r in runs]


@router.get(
    "/api/v1/quality/sentinels/runs/{run_id}",
    response_model=DataIntegritySentinelRunResponse,
)
@transactional
async def get_data_integrity_run(
    request: Request,
    run_id: str,
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Retrieves full details and findings for a specific data integrity sentinel run."""
    run = await service.get_run_by_id(run_id)
    return map_run_to_response(run)


@router.get(
    "/api/v1/quality/sentinels/runs/{run_id}/findings",
    response_model=list[DataIntegrityFindingResponse],
)
@transactional
async def get_data_integrity_run_findings(
    request: Request,
    run_id: str,
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Retrieves all anomaly findings detected within a sentinel run."""
    findings = await service.get_findings_for_run(run_id)
    return [map_finding_to_response(f) for f in findings]


@router.post(
    "/api/v1/quality/sentinels/findings/{finding_id}/dispatch-investigation",
    response_model=DataIntegrityFindingResponse,
)
@transactional
async def dispatch_manual_investigation(
    request: Request,
    finding_id: str,
    payload: DataIntegrityInvestigationDispatchRequest,
    service: DataIntegritySentinelService = Depends(get_data_integrity_service),
    principal: Principal = Depends(get_principal),
):
    """Dispatches a formal 21 CFR Part 11 investigation deviation for a flagged finding."""
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)

    finding = await service.dispatch_investigation_for_finding(
        finding_id=finding_id,
        user_id=user_id,
        user_role=user_role,
        change_reason=payload.rationale or change_reason,
    )
    return map_finding_to_response(finding)


@router.post(
    "/api/v1/quality/sentinels/simulate",
    response_model=DataIntegritySimulationResponse,
)
async def simulate_sentinel_dataset(
    request: Request,
    payload: DataIntegritySimulationRequest,
    principal: Principal = Depends(get_principal),
):
    """Generates synthetic multi-site clinical datasets with injected fraud anomalies."""
    sim = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
        study_id=payload.study_id,
        num_subjects_per_site=payload.num_subjects_per_site,
        include_duplicate_patient=payload.include_duplicate_patient,
        include_temporal_burst=payload.include_temporal_burst,
        include_identical_intervals=payload.include_identical_intervals,
        include_image_duplication=payload.include_image_duplication,
    )
    scenarios = []
    if payload.include_duplicate_patient:
        scenarios.append(
            "DUPLICATE-PATIENT: Cloned cross-site ECG and laboratory profile (SITE-101 vs SITE-104)"
        )
    if payload.include_temporal_burst:
        scenarios.append(
            "TEMPORAL-BURST: Sunday midnight rapid submission cluster (< 4s interval)"
        )
    if payload.include_identical_intervals:
        scenarios.append(
            "IDENTICAL-INTERVALS: Exact 870.0s inter-procedure interval across 5 subjects"
        )
    if payload.include_image_duplication:
        scenarios.append(
            "IMAGE-PHASH: 90-degree rotated duplicate CT slice uploaded across subjects"
        )

    return DataIntegritySimulationResponse(
        study_id=payload.study_id,
        subjects_count=len(sim["subjects"]),
        audit_events_count=len(sim["audit_events"]),
        image_attachments_count=len(sim["image_attachments"]),
        simulated_scenarios=scenarios,
        sample_subjects=sim["subjects"][:3],
        sample_audit_events=sim["audit_events"][:3],
        sample_image_attachments=sim["image_attachments"][:2],
    )
