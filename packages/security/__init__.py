from packages.security.audit_logger import (
    AuditLoggerEngine,
    AuditLogPayload,
    AuditLogRecord,
    CentralAuditLogger,
    audit_logger_engine,
)
from packages.security.context import (
    audit_context,
    audit_context_decorator,
    current_change_reason,
    current_ip_address,
    current_signature_context,
    current_timestamp,
    current_user_id,
    service_audit_context,
)
from packages.security.crypto_verifier import (
    SignatureVerificationRequest,
    SignatureVerificationResult,
    verify_electronic_signature,
)
from packages.security.delegation import (
    DelegationChecker,
    StaffRole,
    normalize_and_validate_staff_role,
    require_delegation,
    validate_request_staff_roles,
    verify_delegation_scope,
)
from packages.security.fail_fast import assert_secure_secrets, validate_branding
from packages.security.gateway_client import (
    GatewayBaseClient,
    run_async,
)
from packages.security.middleware import (
    GatewayAuthMiddleware,
    require_gateway_permission,
)
from packages.security.permissions import (
    PermissionEnum,
    RoleEnum,
    get_permissions_for_role,
    get_permissions_for_roles,
    normalize_role_name,
)
from packages.security.rbac import (
    ROLE_ALIASES,
    Principal,
    StudyScopeChecker,
    can_access_site,
    can_access_study,
    get_normalized_roles,
    get_principal,
    has_permission,
    mask_payload,
    require_permission,
    require_roles,
    require_study_scope,
    verify_is_auditor,
    verify_not_auditor,
)
from packages.security.sig_token_verifier import (
    TokenConsumptionCache,
    token_consumption_cache,
    verify_and_consume_sig_token,
)
from packages.security.signing import (
    asymmetric_sign,
    asymmetric_verify,
    canonical_serialize,
    capture_certificate_identifiers,
    compute_sha256_hash,
    generate_canonical_signature,
    verify_canonical_signature,
)
from packages.security.trial_roles import (
    ClinicalStaffRole,
    TrialRole,
    check_trial_role,
    enforce_site_isolation,
)

# Resolve clinical constants and components dynamically to support decoupling and on-the-fly registration.
def __getattr__(name: str) -> any:
    import packages.security.rbac as rbac
    if hasattr(rbac, name):
        return getattr(rbac, name)
    import packages.security.permissions as permissions
    if hasattr(permissions, name):
        return getattr(permissions, name)
    import packages.security.trial_roles as trial_roles
    if hasattr(trial_roles, name):
        return getattr(trial_roles, name)
    raise AttributeError(f"module '{__name__}' has no attribute '{name}'")
