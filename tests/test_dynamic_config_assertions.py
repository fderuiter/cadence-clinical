"""
Dynamic Runtime Configuration Validation Test Suite.

Verifies that the system enforces safety guards to prevent insecure systems,
weak/fallback secrets, or active development bypass flags from being loaded
under a production configuration profile.
"""

import os
from typing import Generator

import pytest

from packages.security.config_validation import (
    ConfigurationError,
    is_production_profile,
    validate_runtime_config,
)


@pytest.fixture(autouse=True)
def clean_env() -> Generator[None, None, None]:
    """
    Fixture to isolate and clean environment variables for each test.

    Saves the original environment, clears key configuration/bypass keys,
    and restores the original environment after the test is completed.
    """
    original_env = dict(os.environ)

    # List of environment variables we manipulate
    target_keys = [
        "ENV",
        "ENVIRONMENT",
        "APP_ENV",
        "PROD",
        "PRODUCTION",
        "GATEWAY_SECRET",
        "SIGNING_SECRET",
        "REDACTION_SIGNING_SECRET",
        "ALLOW_UNVERIFIED_JWT_FOR_TEST",
        "JWT_TEST_SECRET",
        "SKIP_JWKS_FETCH",
    ]
    for key in target_keys:
        if key in os.environ:
            del os.environ[key]

    yield

    # Restore original environment
    os.environ.clear()
    os.environ.update(original_env)


def test_is_production_profile_detection() -> None:
    """
    Test that various environment variables are correctly detected as production.
    """
    # Baseline: clean environment should not be production
    assert not is_production_profile()

    # Test 'ENV' variable
    os.environ["ENV"] = "production"
    assert is_production_profile()
    os.environ["ENV"] = "PROD"
    assert is_production_profile()
    os.environ["ENV"] = "development"
    assert not is_production_profile()
    del os.environ["ENV"]

    # Test 'ENVIRONMENT' variable
    os.environ["ENVIRONMENT"] = "production"
    assert is_production_profile()
    os.environ["ENVIRONMENT"] = "prod"
    assert is_production_profile()
    os.environ["ENVIRONMENT"] = "test"
    assert not is_production_profile()
    del os.environ["ENVIRONMENT"]

    # Test 'APP_ENV' variable
    os.environ["APP_ENV"] = "production"
    assert is_production_profile()
    os.environ["APP_ENV"] = "prod"
    assert is_production_profile()
    os.environ["APP_ENV"] = "local"
    assert not is_production_profile()
    del os.environ["APP_ENV"]

    # Test 'PROD' variable
    os.environ["PROD"] = "true"
    assert is_production_profile()
    os.environ["PROD"] = "1"
    assert is_production_profile()
    os.environ["PROD"] = "false"
    assert not is_production_profile()
    del os.environ["PROD"]

    # Test 'PRODUCTION' variable
    os.environ["PRODUCTION"] = "true"
    assert is_production_profile()
    os.environ["PRODUCTION"] = "1"
    assert is_production_profile()
    os.environ["PRODUCTION"] = "0"
    assert not is_production_profile()
    del os.environ["PRODUCTION"]


def test_non_production_profile_allows_fallback_secrets() -> None:
    """
    Verify that in non-production profiles, fallback secrets are allowed.

    This ensures that the local development environments do not break.
    """
    # @req:PRD-CFG-003
    # Clean env is non-production by default
    assert not is_production_profile()

    # Explicitly set bypass variable and default fallback secret
    os.environ["ALLOW_UNVERIFIED_JWT_FOR_TEST"] = "true"
    os.environ["GATEWAY_SECRET"] = "internal-gateway-secret-12345"

    # Should not throw any exception
    try:
        validate_runtime_config()
    except ConfigurationError as e:
        pytest.fail(
            f"validate_runtime_config raised ConfigurationError in non-production mode: {e}"
        )


def test_production_profile_requires_gateway_secret() -> None:
    """
    Verify that production profiles fail to initialize when GATEWAY_SECRET is missing.
    """
    # @req:PRD-CFG-001
    os.environ["ENV"] = "production"
    assert is_production_profile()

    with pytest.raises(ConfigurationError, match="GATEWAY_SECRET.*not set"):
        validate_runtime_config()


def test_production_profile_fails_on_default_gateway_secret() -> None:
    """
    Verify that production profiles throw an error when default gateway secret is set.
    """
    # @req:PRD-CFG-001
    os.environ["ENV"] = "production"
    os.environ["GATEWAY_SECRET"] = "internal-gateway-secret-12345"
    assert is_production_profile()

    with pytest.raises(
        ConfigurationError, match="Insecure fallback/default secret detected"
    ):
        validate_runtime_config()


def test_production_profile_fails_on_weak_or_predictable_secret() -> None:
    """
    Verify that production profiles throw an error when gateway secret is weak or short.
    """
    # @req:PRD-CFG-001
    os.environ["ENV"] = "production"
    assert is_production_profile()

    # Too short
    os.environ["GATEWAY_SECRET"] = "short-secret"
    with pytest.raises(ConfigurationError, match="too weak or too short"):
        validate_runtime_config()

    # Contains prohibited keyword like "secret"
    os.environ["GATEWAY_SECRET"] = "super-long-but-contains-secret-keyword"
    with pytest.raises(ConfigurationError, match="weak or predictable pattern"):
        validate_runtime_config()


def test_production_profile_succeeds_on_strong_secret() -> None:
    """
    Verify that production profiles initialize successfully when a strong secret is set.
    """
    # @req:PRD-CFG-001
    # @req:PRD-CFG-004
    os.environ["ENV"] = "production"
    # A strong, unique key with no prohibited weak keywords, longer than 16 characters
    os.environ["GATEWAY_SECRET"] = "uq78bnyWRE89unb3v78qw69bn0q87wy6e"
    assert is_production_profile()

    # Should run with no errors
    validate_runtime_config()


def test_production_profile_fails_on_active_developer_bypass_flags() -> None:
    """
    Verify that the system crashes on boot if any active developer bypass flags are present in production.
    """
    # @req:PRD-CFG-002
    # @req:PRD-CFG-004
    os.environ["ENV"] = "production"
    os.environ["GATEWAY_SECRET"] = "uq78bnyWRE89unb3v78qw69bn0q87wy6e"
    assert is_production_profile()

    # Try setting ALLOW_UNVERIFIED_JWT_FOR_TEST
    os.environ["ALLOW_UNVERIFIED_JWT_FOR_TEST"] = "true"
    with pytest.raises(
        ConfigurationError,
        match="Active developer bypass parameter 'ALLOW_UNVERIFIED_JWT_FOR_TEST'",
    ):
        validate_runtime_config()
    del os.environ["ALLOW_UNVERIFIED_JWT_FOR_TEST"]

    # Try setting JWT_TEST_SECRET
    os.environ["JWT_TEST_SECRET"] = "some-test-secret"
    with pytest.raises(
        ConfigurationError, match="Active developer bypass parameter 'JWT_TEST_SECRET'"
    ):
        validate_runtime_config()
    del os.environ["JWT_TEST_SECRET"]

    # Try setting SKIP_JWKS_FETCH
    os.environ["SKIP_JWKS_FETCH"] = "1"
    with pytest.raises(
        ConfigurationError, match="Active developer bypass parameter 'SKIP_JWKS_FETCH'"
    ):
        validate_runtime_config()
    del os.environ["SKIP_JWKS_FETCH"]


def test_production_profile_ignores_falsy_developer_bypass_flags() -> None:
    """
    Verify that production profiles allow developer bypass flags if they are explicitly falsy.
    """
    # @req:PRD-CFG-002
    os.environ["ENV"] = "production"
    os.environ["GATEWAY_SECRET"] = "uq78bnyWRE89unb3v78qw69bn0q87wy6e"
    assert is_production_profile()

    os.environ["ALLOW_UNVERIFIED_JWT_FOR_TEST"] = "false"
    os.environ["JWT_TEST_SECRET"] = "0"
    os.environ["SKIP_JWKS_FETCH"] = "False"

    # Should run successfully without throwing
    validate_runtime_config()
