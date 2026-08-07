"""Unit test suite for temporary time-bounded auditor access token generator.

Requirements: PRD-SYS-001
"""

import pytest

import packages  # noqa: F401
from packages.security.auditor_token import AuditorAccessTokenService


def test_generate_and_validate_auditor_token() -> None:
    """Validate generating and decoding temporary auditor access token.

    Requirements: PRD-SYS-001
    """
    service = AuditorAccessTokenService()

    result = service.generate_auditor_token(
        auditor_email="fda_inspector@fda.gov",
        study_id="study_auditor_01",
        duration_hours=12,
    )

    assert "access_token" in result
    assert result["auditor_email"] == "fda_inspector@fda.gov"
    assert result["study_id"] == "study_auditor_01"

    token = result["access_token"]
    payload = service.validate_auditor_token(token)

    assert payload["sub"] == "fda_inspector@fda.gov"
    assert payload["study_id"] == "study_auditor_01"
    assert payload["is_read_only"] is True
    assert "auditor:read" in payload["scopes"]


def test_expired_auditor_token_raises_error() -> None:
    """Validate expired auditor access token raises ValueError.

    Requirements: PRD-SYS-001
    """
    service = AuditorAccessTokenService()

    # Generate token with negative duration (-1 hours) to simulate expiration
    result = service.generate_auditor_token(
        auditor_email="ema_inspector@ema.europa.eu",
        study_id="study_auditor_02",
        duration_hours=-1,
    )

    token = result["access_token"]

    with pytest.raises(ValueError) as exc:
        service.validate_auditor_token(token)

    assert "expired" in str(exc.value).lower()
