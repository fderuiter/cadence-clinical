"""FastAPI sub-router for CDISC ODM XML and verifiable document exports."""

from fastapi import APIRouter, Depends
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLConsentClauseRepository,
    SQLConsentSignatureRepository,
    SQLConsentTemplateRepository,
    SQLGranularOptionRepository,
    SQLSubjectConsentRepository,
)
from apps.econsent.application.use_cases import InspectionExportService
from apps.econsent.presentation.dtos import (
    CdiscOdmExportResponse,
    VerifiableCertificateResponse,
)
from packages.database import DatabaseSessionDependency

router = APIRouter(prefix="/api/v1/econsent/export", tags=["Export"])
get_db_session = DatabaseSessionDependency(db_manager)


@router.get(
    "/cdisc-odm/{study_id}/{subject_pseudonym}/{template_id}/{version_index}",
    response_model=CdiscOdmExportResponse,
)
async def export_subject_cdisc_odm(
    study_id: str,
    subject_pseudonym: str,
    template_id: str,
    version_index: int,
    session: AsyncSession = Depends(get_db_session),
) -> CdiscOdmExportResponse:
    """Exports clinical eConsent records in standard CDISC ODM v1.3.2/v2.0 XML."""
    svc = InspectionExportService(
        template_repo=SQLConsentTemplateRepository(session),
        clause_repo=SQLConsentClauseRepository(session),
        signature_repo=SQLConsentSignatureRepository(session),
        granular_repo=SQLGranularOptionRepository(session),
        audit_repo=SQLConsentAuditRepository(session),
        consent_repo=SQLSubjectConsentRepository(session),
    )
    xml_content = await svc.export_cdisc_odm(
        study_id=study_id,
        subject_pseudonym=subject_pseudonym,
        template_id=template_id,
        version_index=version_index,
    )
    return CdiscOdmExportResponse(
        study_id=study_id,
        subject_pseudonym=subject_pseudonym,
        template_id=template_id,
        version_index=version_index,
        odm_version="1.3.2",
        xml_content=xml_content,
    )


@router.get(
    "/certificate/{study_id}/{subject_pseudonym}/{template_id}/{version_index}",
    response_model=VerifiableCertificateResponse,
)
async def export_verifiable_certificate(
    study_id: str,
    subject_pseudonym: str,
    template_id: str,
    version_index: int,
    session: AsyncSession = Depends(get_db_session),
) -> VerifiableCertificateResponse:
    """Generates a standalone, tamper-evident HTML consent certificate."""
    svc = InspectionExportService(
        template_repo=SQLConsentTemplateRepository(session),
        clause_repo=SQLConsentClauseRepository(session),
        signature_repo=SQLConsentSignatureRepository(session),
        granular_repo=SQLGranularOptionRepository(session),
        audit_repo=SQLConsentAuditRepository(session),
        consent_repo=SQLSubjectConsentRepository(session),
    )
    html_content = await svc.export_verifiable_html_certificate(
        study_id=study_id,
        subject_pseudonym=subject_pseudonym,
        template_id=template_id,
        version_index=version_index,
    )
    import hashlib

    digest = hashlib.sha256(html_content.encode("utf-8")).hexdigest()

    return VerifiableCertificateResponse(
        study_id=study_id,
        subject_pseudonym=subject_pseudonym,
        template_id=template_id,
        version_index=version_index,
        html_content=html_content,
        digest_sha256=digest,
    )
