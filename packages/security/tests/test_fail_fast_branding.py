import pytest

from packages.security.fail_fast import validate_branding


def test_validate_branding_dev_bypass(monkeypatch):
    """Verify that in development or test environments, default or legacy values do not raise an error."""
    monkeypatch.setenv("APP_ENV", "development")
    monkeypatch.setenv("BRAND_NAME", "Cadence Clinical")
    monkeypatch.setenv("BRAND_DOMAIN", "cadence-clinical.com")

    # Should not raise any error
    validate_branding("test_service")


def test_validate_branding_prod_failures(monkeypatch):
    """Verify that in production, invalid brand name or domain raises a RuntimeError."""
    monkeypatch.setenv("APP_ENV", "production")

    # 1. Missing BRAND_NAME
    monkeypatch.setenv("BRAND_NAME", "")
    monkeypatch.setenv("BRAND_DOMAIN", "validdomain.com")
    with pytest.raises(RuntimeError, match="BRAND_NAME"):
        validate_branding("test_service")

    # 2. Default BRAND_NAME
    monkeypatch.setenv("BRAND_NAME", "Cadence Clinical")
    with pytest.raises(RuntimeError, match="BRAND_NAME"):
        validate_branding("test_service")

    # 3. Valid BRAND_NAME, but missing BRAND_DOMAIN
    monkeypatch.setenv("BRAND_NAME", "Custom Brand")
    monkeypatch.setenv("BRAND_DOMAIN", "")
    with pytest.raises(RuntimeError, match="BRAND_DOMAIN"):
        validate_branding("test_service")

    # 4. Default BRAND_DOMAIN
    monkeypatch.setenv("BRAND_DOMAIN", "cadenceclinical.com")
    with pytest.raises(RuntimeError, match="BRAND_DOMAIN"):
        validate_branding("test_service")

    # 5. Legacy hyphenated BRAND_DOMAIN
    monkeypatch.setenv("BRAND_DOMAIN", "cadence-clinical.com")
    with pytest.raises(RuntimeError, match="BRAND_DOMAIN"):
        validate_branding("test_service")


def test_validate_branding_gateway_failures(monkeypatch):
    """Verify that in production, gateway validation checks keycloak variables too."""
    monkeypatch.setenv("APP_ENV", "staging")
    monkeypatch.setenv("BRAND_NAME", "Custom Brand")
    monkeypatch.setenv("BRAND_DOMAIN", "validdomain.com")

    # For non-gateway, valid branding passes
    validate_branding("test_service", is_gateway=False)

    # For gateway, if keycloak parameters are default/missing, it must fail
    monkeypatch.setenv("KEYCLOAK_REALM", "cadence")
    monkeypatch.setenv("KEYCLOAK_CLIENT_ID", "valid-client")
    with pytest.raises(RuntimeError, match="KEYCLOAK_REALM"):
        validate_branding("gateway", is_gateway=True)

    monkeypatch.setenv("KEYCLOAK_REALM", "valid-realm")
    monkeypatch.setenv("KEYCLOAK_CLIENT_ID", "cadence-clinical")
    with pytest.raises(RuntimeError, match="KEYCLOAK_CLIENT_ID"):
        validate_branding("gateway", is_gateway=True)


def test_validate_branding_success(monkeypatch):
    """Verify that valid production configurations successfully pass the validation checks."""
    monkeypatch.setenv("APP_ENV", "production")
    monkeypatch.setenv("BRAND_NAME", "Custom Brand")
    monkeypatch.setenv("BRAND_DOMAIN", "validdomain.com")
    monkeypatch.setenv("KEYCLOAK_REALM", "valid-realm")
    monkeypatch.setenv("KEYCLOAK_CLIENT_ID", "valid-client")

    # Microservice check
    validate_branding("test_service", is_gateway=False)

    # Gateway check
    validate_branding("gateway", is_gateway=True)
