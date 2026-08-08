from apps.etmf.infrastructure.models import (
    Base,
    DocumentExpirationAlertState,
    DocumentQCTransition,
    DocumentStatus,
    ExpectedDocument,
    TMFAuditLedgerSeal,
    TMFAuditLog,
    TMFDocument,
    TMFDocumentType,
)


def is_site_level_artifact(
    artifact_type: str, artifact_code: str | None = None
) -> bool:
    site_artifacts = {
        "fda form 1572",
        "financial disclosure",
        "investigator cv",
        "delegation of authority log",
        "site signature page",
        "site feasibility survey",
        "informed consent form",
    }
    site_codes_prefix = {
        "05.02",
        "04.01",
        "05.01",
    }

    art_lower = artifact_type.strip().lower()
    if art_lower in site_artifacts:
        return True
    if artifact_code:
        for prefix in site_codes_prefix:
            if artifact_code.startswith(prefix):
                return True
    return False


__all__ = [
    "Base",
    "DocumentExpirationAlertState",
    "DocumentQCTransition",
    "DocumentStatus",
    "ExpectedDocument",
    "TMFAuditLedgerSeal",
    "TMFAuditLog",
    "TMFDocument",
    "TMFDocumentType",
    "is_site_level_artifact",
]
