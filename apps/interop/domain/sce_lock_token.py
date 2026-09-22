"""Database Lock Authorization Token engine for unblinded statistical analysis gating.

Gates unblinded treatment arm variables (ARMCD, TRT01P, RANDDT) behind formal cryptographic
database lock authorization tokens in compliance with 21 CFR Part 11 and study double-blind SOPs.

Requirements: PRD-CDR-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import hashlib
import hmac
import os
import uuid
from datetime import UTC, datetime, timedelta

from apps.interop.domain.sce_models import DatabaseLockAuthToken

DEFAULT_UNBLINDED_ROLES = [
    "unblinded_statistician",
    "idmc",
    "emergency_unblinder",
    "sponsor_admin",
    "admin",
]


def _get_signing_secret() -> bytes:
    """Retrieve cryptographic signing secret from settings."""
    from packages.config import get_interop_settings

    settings = get_interop_settings()
    secret = (
        settings.signing_secret
        or settings.gateway_secret
    )
    return secret.encode("utf-8")


def generate_lock_token_signature(
    token_id: str,
    study_id: str,
    lock_id: str,
    issued_to: str,
    expires_at: datetime,
) -> str:
    """Computes HMAC-SHA256 signature for a database lock authorization token."""
    exp_iso = (
        expires_at.replace(tzinfo=UTC).isoformat()
        if expires_at.tzinfo is None
        else expires_at.isoformat()
    )
    raw_payload = f"{token_id}:{study_id}:{lock_id}:{issued_to}:{exp_iso}"
    secret = _get_signing_secret()
    return hmac.new(secret, raw_payload.encode("utf-8"), hashlib.sha256).hexdigest()


def issue_database_lock_token(
    study_id: str,
    lock_id: str,
    milestone: str,
    issued_to: str,
    reason: str,
    expires_in_hours: int = 24,
    authorized_roles: list[str] | None = None,
) -> DatabaseLockAuthToken:
    """Issues a cryptographically signed authorization token for unblinded analysis.

    Args:
        study_id: Clinical study ID
        lock_id: Database lock reference ID
        milestone: Milestone tier (e.g. DATABASE_LOCK, INTERIM_ANALYSIS_1)
        issued_to: User ID of authorized statistician / IDMC lead
        reason: Regulatory justification for unblinded access
        expires_in_hours: Validity duration (default 24h)
        authorized_roles: Permitted roles (defaults to unblinded clinical roles)

    Returns:
        Signed DatabaseLockAuthToken
    """
    token_id = f"DBLOCK-AUTH-{uuid.uuid4().hex[:12].upper()}"
    now = datetime.now(UTC)
    expires_at = now + timedelta(hours=max(1, expires_in_hours))
    roles = authorized_roles or DEFAULT_UNBLINDED_ROLES

    sig_hash = generate_lock_token_signature(
        token_id=token_id,
        study_id=study_id,
        lock_id=lock_id,
        issued_to=issued_to,
        expires_at=expires_at,
    )

    return DatabaseLockAuthToken(
        token_id=token_id,
        study_id=study_id,
        lock_id=lock_id,
        milestone=milestone,
        issued_to=issued_to,
        authorized_roles=roles,
        issued_at=now,
        expires_at=expires_at,
        signature_hash=sig_hash,
        is_revoked=False,
        reason=reason,
    )


def verify_database_lock_token(
    token: DatabaseLockAuthToken,
    requesting_study_id: str,
    requesting_user_role: str,
) -> tuple[bool, str]:
    """Validates database lock authorization token integrity, study match, expiration, and role permissions.

    Args:
        token: DatabaseLockAuthToken instance
        requesting_study_id: Study ID of the execution request
        requesting_user_role: Role of the executing user

    Returns:
        Tuple of (is_valid: bool, rejection_reason: str)
    """
    if token.is_revoked:
        return (
            False,
            f"Authorization token '{token.token_id}' has been explicitly revoked.",
        )

    if token.study_id != requesting_study_id:
        return (
            False,
            f"Token study mismatch: token belongs to '{token.study_id}' but request is for '{requesting_study_id}'.",
        )

    now = datetime.now(UTC)
    token_exp = (
        token.expires_at.replace(tzinfo=UTC)
        if token.expires_at.tzinfo is None
        else token.expires_at
    )

    if now > token_exp:
        return (
            False,
            f"Authorization token '{token.token_id}' expired at {token_exp.isoformat()}.",
        )

    # Verify role authorization
    normalized_role = requesting_user_role.lower().strip()
    permitted_roles = [r.lower().strip() for r in token.authorized_roles]
    if normalized_role not in permitted_roles:
        return (
            False,
            f"Role '{requesting_user_role}' is not authorized by this lock token. Permitted: {token.authorized_roles}.",
        )

    # Verify cryptographic signature
    expected_sig = generate_lock_token_signature(
        token_id=token.token_id,
        study_id=token.study_id,
        lock_id=token.lock_id,
        issued_to=token.issued_to,
        expires_at=token_exp,
    )

    if not hmac.compare_digest(token.signature_hash, expected_sig):
        return (
            False,
            "Cryptographic signature verification failed on database lock token.",
        )

    return True, "Token successfully validated for unblinded analysis access."
