"""FastAPI router for eTMF inspection readiness analytics, EMS export, and cryptographic verification."""

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from fastapi.responses import Response

from apps.etmf.adapters.database import transactional
from apps.etmf.application.use_cases import (
    ElectronicSignatureUseCase,
    ExportTmfEmsUseCase,
    InspectionReadinessUseCase,
    VerifyAuditLedgerChainUseCase,
)
from apps.etmf.domain.ports import ETMFRepositoryPort
from apps.etmf.presentation.dtos import (
    AuditChainVerificationResponse,
    InspectionReadinessResponse,
    MilestoneReadinessDetail,
    SignatureVerificationResponse,
    ZoneReadinessDetail,
)
from apps.etmf.presentation.routers.etmf import get_etmf_repository, write_audit_log
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter(prefix="/api/v1/etmf", tags=["Inspection & EMS"])


@router.get(
    "/studies/{study_id}/inspection-readiness",
    response_model=InspectionReadinessResponse,
)
@transactional
async def get_study_inspection_readiness(
    request: Request,
    study_id: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> InspectionReadinessResponse:
    """Calculates and returns a comprehensive inspection readiness assessment for a study.

    Evaluates Expected Document List (EDL) completion, zone matrix distribution,
    QC review bottlenecks, Part 11 electronic signature compliance, and expiration risks.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to inspect eTMF readiness.",
        )

    use_case = InspectionReadinessUseCase(repo)
    report = await use_case.evaluate_readiness(study_id)

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="INSPECTION_READINESS_VIEW",
        document_id=None,
        details=f"Viewed inspection readiness evaluation for study '{study_id}' (Score: {report.overall_readiness_score}/100, Rating: {report.readiness_rating}).",
    )

    return InspectionReadinessResponse(
        study_id=report.study_id,
        generated_at=report.generated_at,
        overall_readiness_score=report.overall_readiness_score,
        readiness_rating=report.readiness_rating,
        total_documents=report.total_documents,
        total_expected=report.total_expected,
        approved_documents_count=report.approved_documents_count,
        pending_qc_count=report.pending_qc_count,
        unsigned_documents_count=report.unsigned_documents_count,
        expired_documents_count=report.expired_documents_count,
        expiring_soon_count=report.expiring_soon_count,
        milestones=[
            MilestoneReadinessDetail(
                milestone=m.milestone,
                is_complete=m.is_complete,
                expected_count=m.expected_count,
                present_count=m.present_count,
                approved_count=m.approved_count,
                missing_artifacts=m.missing_artifacts,
                completeness_percentage=m.completeness_percentage,
            )
            for m in report.milestones
        ],
        zones=[
            ZoneReadinessDetail(
                zone_code=z.zone_code,
                zone_name=z.zone_name,
                expected_count=z.expected_count,
                present_count=z.present_count,
                approved_count=z.approved_count,
                pending_qc_count=z.pending_qc_count,
                rejected_count=z.rejected_count,
                missing_count=z.missing_count,
                completeness_percentage=z.completeness_percentage,
            )
            for z in report.zones
        ],
        action_items=report.action_items,
    )


@router.get("/studies/{study_id}/ems-export")
@transactional
async def export_study_tmf_ems(
    request: Request,
    study_id: str,
    study_title: str | None = Query(None, description="Optional study title"),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> Response:
    """Exports the eTMF in standard DIA TMF Exchange Mechanism Standard (EMS) format.

    Generates a compliant ZIP package containing `tmf-ems.xml`, `tmf-ems.json`,
    `checksums.sha256`, and all document version assets organized in standard DIA hierarchy.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read") and not has_permission(
        principal, "archive:export"
    ):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to export TMF EMS package.",
        )

    use_case = ExportTmfEmsUseCase(repo)
    zip_bytes = await use_case.export_package(
        study_id=study_id,
        study_title=study_title,
        requester_id=user_id,
        requester_role=user_roles,
        principal=principal,
    )

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="TMF_EMS_EXPORT",
        document_id=None,
        details=f"Exported DIA TMF Exchange Mechanism Standard (EMS) package for study '{study_id}'.",
    )

    return Response(
        content=zip_bytes,
        media_type="application/zip",
        headers={
            "Content-Disposition": f"attachment; filename=study_{study_id}_tmf_ems.zip"
        },
    )


@router.post("/audit-logs/verify-chain", response_model=AuditChainVerificationResponse)
@transactional
async def verify_audit_ledger_chain(
    request: Request,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> AuditChainVerificationResponse:
    """Cryptographically inspects and verifies the full Merkle block ledger chain for tampering detection."""
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_audit_logs:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    use_case = VerifyAuditLedgerChainUseCase(repo)
    report = await use_case.verify_chain()

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="AUDIT_CHAIN_VERIFY",
        document_id=None,
        details=f"Executed cryptographic audit ledger verification: {report['details']}",
    )

    return AuditChainVerificationResponse(
        is_valid=report["is_valid"],
        total_sealed_blocks=report["total_sealed_blocks"],
        total_sealed_records=report["total_sealed_records"],
        latest_block_hash=report["latest_block_hash"],
        genesis_block_hash=report["genesis_block_hash"],
        unsealed_records_count=report["unsealed_records_count"],
        tamper_detected=report["tamper_detected"],
        details=report["details"],
    )


@router.post(
    "/documents/{document_id}/verify-signature",
    response_model=SignatureVerificationResponse,
)
@transactional
async def verify_document_signature_endpoint(
    request: Request,
    document_id: str,
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
    principal: Principal = Depends(get_principal),
) -> SignatureVerificationResponse:
    """Verifies the electronic signature and SHA-256 cryptographic digest integrity on a document."""
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_document:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient permissions to verify signatures.",
        )

    use_case = ElectronicSignatureUseCase(repo)
    result = await use_case.verify_signature(document_id)

    await write_audit_log(
        repo=repo,
        user_id=user_id,
        user_role=user_roles,
        action="SIGNATURE_VERIFY",
        document_id=document_id,
        details=f"Verified electronic signature on document ID '{document_id}' (Valid: {result['is_valid']}).",
    )

    return SignatureVerificationResponse(
        document_id=result["document_id"],
        version_index=result["version_index"],
        is_valid=result["is_valid"],
        signer=result["signer"],
        signing_timestamp=result["signing_timestamp"],
        signing_reason=result["signing_reason"],
        certificate_fingerprint=result["certificate_fingerprint"],
        content_hash_matched=result["content_hash_matched"],
        details=result["details"],
    )
