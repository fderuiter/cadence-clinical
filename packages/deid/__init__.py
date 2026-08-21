import os

# Set safe mock/placeholder environment variables for the CLI scanner to prevent startup crashes when imported
os.environ.setdefault(
    "AUDIT_LOG_SECRET_KEY", "test-gxp-audit-secret-key-placeholder-abc"
)
os.environ.setdefault(
    "INBOUND_EMAIL_HMAC_SECRET", "test-email-hmac-secret-placeholder-xyz"
)

from packages.deid.air_gap import (
    DeidAirGapVault,
)
from packages.deid.detector import (
    DeidDetector,
    redact_text,
    resolve_overlaps,
)
from packages.deid.manifest import (
    RedactionManifest,
    build_redaction_manifest,
    sign_manifest_asymmetric,
    sign_manifest_symmetric,
    verify_manifest_asymmetric,
    verify_manifest_symmetric,
)
from packages.deid.models import (
    PROFILE_CATEGORIES,
    ComplianceProfile,
    DetectionResult,
    DetectorCategory,
)
from packages.deid.transforms import (
    DEFAULT_DATE_SHIFT_DAYS,
    RedactionRecordItem,
    apply_deid_transforms,
    cap_age_numeric,
    cap_age_string,
    get_subject_date_shift,
    normalize_and_cap_age,
    pseudonymize_subject_id,
    pseudonymize_value,
    scrub_error_message,
    shift_date_by_subject,
    shift_date_string,
)

__all__ = [
    "ComplianceProfile",
    "DeidAirGapVault",
    "DetectionResult",
    "DetectorCategory",
    "PROFILE_CATEGORIES",
    "DeidDetector",
    "resolve_overlaps",
    "redact_text",
    "DEFAULT_DATE_SHIFT_DAYS",
    "RedactionRecordItem",
    "apply_deid_transforms",
    "cap_age_string",
    "pseudonymize_value",
    "scrub_error_message",
    "shift_date_string",
    "get_subject_date_shift",
    "shift_date_by_subject",
    "cap_age_numeric",
    "normalize_and_cap_age",
    "pseudonymize_subject_id",
    "RedactionManifest",
    "build_redaction_manifest",
    "sign_manifest_symmetric",
    "verify_manifest_symmetric",
    "sign_manifest_asymmetric",
    "verify_manifest_asymmetric",
]
