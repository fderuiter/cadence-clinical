import os

import pytest

from packages.security.branding import (
    FALLBACK_BRAND_DOMAIN,
    FALLBACK_BRAND_NAME,
    get_brand_domain,
    get_brand_name,
    validate_branding,
)


def test_branding_fallbacks():
    """Test standard fallbacks when environment variables are unset."""
    # Ensure they are cleared
    orig_name = os.environ.get("BRAND_NAME")
    orig_domain = os.environ.get("BRAND_DOMAIN")
    try:
        if "BRAND_NAME" in os.environ:
            del os.environ["BRAND_NAME"]
        if "BRAND_DOMAIN" in os.environ:
            del os.environ["BRAND_DOMAIN"]

        assert get_brand_name() == FALLBACK_BRAND_NAME
        assert get_brand_domain() == FALLBACK_BRAND_DOMAIN
    finally:
        if orig_name is not None:
            os.environ["BRAND_NAME"] = orig_name
        if orig_domain is not None:
            os.environ["BRAND_DOMAIN"] = orig_domain


def test_validate_branding_local_degraded(monkeypatch):
    """Local development and test suites should not block boot/test on invalid defaults."""
    monkeypatch.setenv("APP_ENV", "development")
    monkeypatch.delenv("CI", raising=False)
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    monkeypatch.setenv("BRAND_NAME", "Cadence Clinical")
    monkeypatch.setenv("BRAND_DOMAIN", "cadenceclinical.com")

    # This should not raise/exit, just return fallback/current values
    name, domain = validate_branding("test-service")
    assert name == "Cadence Clinical"
    assert domain == "cadenceclinical.com"


def test_validate_branding_strict_prod(monkeypatch):
    """In production or staging environment, validate_branding must crash/exit on invalid defaults."""
    monkeypatch.setenv("APP_ENV", "production")
    monkeypatch.delenv("CI", raising=False)
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    monkeypatch.setenv("BRAND_NAME", "Cadence Clinical")
    monkeypatch.setenv("BRAND_DOMAIN", "cadenceclinical.com")

    with pytest.raises(SystemExit) as exc_info:
        validate_branding("test-service")
    assert exc_info.value.code == 1


def test_validate_branding_strict_ci(monkeypatch):
    """In CI/CD environments, validate_branding must fail immediately and halt deployment."""
    monkeypatch.setenv("APP_ENV", "test")
    monkeypatch.setenv("CI", "true")
    monkeypatch.setenv("BRAND_NAME", "Cadence Clinical")
    monkeypatch.setenv("BRAND_DOMAIN", "cadenceclinical.com")

    with pytest.raises(SystemExit) as exc_info:
        validate_branding("test-service")
    assert exc_info.value.code == 1


def test_validate_branding_valid(monkeypatch):
    """With valid custom configurations, validate_branding should succeed without exiting."""
    monkeypatch.setenv("APP_ENV", "production")
    monkeypatch.setenv("BRAND_NAME", "Acme Clinical")
    monkeypatch.setenv("BRAND_DOMAIN", "acmeclinical.com")

    name, domain = validate_branding("test-service")
    assert name == "Acme Clinical"
    assert domain == "acmeclinical.com"
