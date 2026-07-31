"""Temporary time-bounded auditor access token generator for GxP regulatory inspection access.

Requirements: PRD-SYS-001
"""

import os
import time
from datetime import UTC, datetime, timedelta
from typing import Any

from jose import JWTError, jwt

import packages  # noqa: F401


class AuditorAccessTokenService:
    """Service generating and validating temporary read-only JWT access tokens for external auditors.

    Requirements: PRD-SYS-001
    """

    def __init__(self, secret_key: str | None = None) -> None:
        """Initialize service with gateway secret key."""
        self._secret = (
            secret_key or os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
        ).encode("utf-8")

    def generate_auditor_token(
        self,
        auditor_email: str,
        study_id: str,
        duration_hours: int = 24,
        scopes: list[str] | None = None,
    ) -> dict[str, Any]:
        """Generate signed time-bounded JWT access token for external regulatory inspection.

        Args:
            auditor_email: Inspector/auditor email address.
            study_id: Target protocol study ID.
            duration_hours: Token validity duration in hours (default 24h).
            scopes: Optional custom scopes. Defaults to read-only auditor scopes.

        Returns:
            Dictionary containing access_token, expires_at, and scopes.
        """
        now = datetime.now(UTC)
        expires_at = now + timedelta(hours=duration_hours)
        iat = time.time()
        exp = expires_at.timestamp()

        effective_scopes = scopes or [
            "auditor:read",
            "audit_trail:read",
            "casebook:view",
        ]

        payload = {
            "sub": auditor_email,
            "auditor_email": auditor_email,
            "study_id": study_id,
            "role": "auditor",
            "roles": ["auditor"],
            "scopes": effective_scopes,
            "iat": iat,
            "exp": exp,
            "is_read_only": True,
        }

        token = jwt.encode(payload, self._secret, algorithm="HS256")

        return {
            "access_token": token,
            "token_type": "bearer",
            "expires_at": expires_at.isoformat(),
            "study_id": study_id,
            "auditor_email": auditor_email,
            "scopes": effective_scopes,
        }

    def validate_auditor_token(self, token: str) -> dict[str, Any]:
        """Validate signature and expiration of auditor JWT token.

        Args:
            token: JWT token string.

        Returns:
            Decoded payload dictionary.

        Raises:
            ValueError: If token is invalid, tampered, or expired.
        """
        try:
            payload = jwt.decode(token, self._secret, algorithms=["HS256"])
            if payload.get("exp", 0) < time.time():
                raise ValueError("Auditor access token has expired.")
            return payload
        except JWTError as exc:
            raise ValueError(f"Invalid auditor token: {str(exc)}")
