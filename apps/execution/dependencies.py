from collections.abc import AsyncGenerator

from fastapi import Depends, HTTPException, Request
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.core import db_manager
from apps.execution.services.amendment_diff import StudyVersionDiffEngine
from apps.execution.services.doa_service import DOAService
from apps.execution.services.e2b_parser import E2BR3Parser
from apps.execution.services.eisf_service import EISFService
from apps.execution.services.offline_sync import OfflineSyncEngine
from apps.execution.services.pdf_redactor import PDFRedactorService
from apps.execution.services.sae_reconciler import SAEReconciler
from packages.security.auditor_token import AuditorAccessTokenService
from packages.security.ner_scrubber import PHINameEntityScrubber
from packages.security.signature_builder import CryptographicSignatureBuilder


def verify_change_justification(request: Request) -> None:
    """Enforce presence of change justification header (version 2)."""
    version = request.headers.get("X-Signature-Version")
    change_reason = request.headers.get("X-Change-Reason")
    if version not in ("2", "v2") or not change_reason:
        raise HTTPException(
            status_code=403,
            detail="API rejects any state modifications that do not contain a verified, gateway-signed change justification header.",
        )


async def get_db() -> AsyncGenerator[AsyncSession]:
    """Provide an async database session generator helper."""
    async with db_manager.get_session_maker()() as session:
        yield session


def get_offline_sync_engine(db: AsyncSession = Depends(get_db)) -> OfflineSyncEngine:
    """Resolve OfflineSyncEngine with the active db session."""
    return OfflineSyncEngine(session=db)


# Global-style singletons for stateless or standard services
_doa_service = DOAService()
_eisf_service = EISFService()
_scrubber = PHINameEntityScrubber()
_redactor = PDFRedactorService()


def get_doa_service() -> DOAService:
    """Resolve DOAService."""
    return _doa_service


def get_signature_builder() -> CryptographicSignatureBuilder:
    """Resolve CryptographicSignatureBuilder."""
    return CryptographicSignatureBuilder()


def get_study_version_diff_engine() -> StudyVersionDiffEngine:
    """Resolve StudyVersionDiffEngine."""
    return StudyVersionDiffEngine()


def get_auditor_access_token_service() -> AuditorAccessTokenService:
    """Resolve AuditorAccessTokenService."""
    return AuditorAccessTokenService()


def get_e2b_parser() -> E2BR3Parser:
    """Resolve E2BR3Parser."""
    return E2BR3Parser()


def get_sae_reconciler() -> SAEReconciler:
    """Resolve SAEReconciler."""
    return SAEReconciler()


def get_eisf_service() -> EISFService:
    """Resolve EISFService."""
    return _eisf_service


def get_scrubber() -> PHINameEntityScrubber:
    """Resolve PHINameEntityScrubber."""
    return _scrubber


def get_redactor() -> PDFRedactorService:
    """Resolve PDFRedactorService."""
    return _redactor
