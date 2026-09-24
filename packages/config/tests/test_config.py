"""Unit tests for centralized Pydantic BaseSettings configuration package."""

import pytest
from pydantic import ValidationError

from packages.config import (
    CommonSettings,
    NotificationsSettings,
    clear_settings_caches,
    get_common_settings,
    get_notifications_settings,
)


def test_default_settings_load():
    """Verify default settings instantiation in development mode."""
    settings = CommonSettings()
    assert settings.app_env == "development"
    assert settings.brand_name == "Cadence Clinical"
    assert settings.gateway_secret == "internal-gateway-secret-12345"


def test_env_var_override(monkeypatch):
    """Verify environment variables override default settings values."""
    monkeypatch.setenv("APP_ENV", "dev")
    monkeypatch.setenv("BRAND_NAME", "Custom Trial")
    monkeypatch.setenv("GATEWAY_SECRET", "custom-secret-key-12345")
    clear_settings_caches()

    settings = get_common_settings()
    assert settings.brand_name == "Custom Trial"
    assert settings.gateway_secret == "custom-secret-key-12345"


def test_invalid_numeric_parameter_raises_validation_error(monkeypatch):
    """Verify invalid numeric environment parameters trigger Pydantic ValidationError on startup."""
    monkeypatch.setenv("SMTP_PORT", "not_a_number")
    clear_settings_caches()

    with pytest.raises(ValidationError) as exc_info:
        NotificationsSettings()
    assert "smtp_port" in str(exc_info.value).lower()


def test_invalid_boolean_parameter_raises_validation_error(monkeypatch):
    """Verify invalid boolean environment parameters trigger Pydantic ValidationError on startup."""
    monkeypatch.setenv("SMTP_USE_TLS", "not_a_bool")
    clear_settings_caches()

    with pytest.raises(ValidationError) as exc_info:
        NotificationsSettings()
    assert "smtp_use_tls" in str(exc_info.value).lower()


def test_production_mode_fails_fast_on_insecure_secrets(monkeypatch):
    """Verify startup fails immediately in non-development mode when critical secrets use insecure fallbacks."""
    monkeypatch.setenv("APP_ENV", "production")
    monkeypatch.setenv("BRAND_NAME", "ACME Pharma")
    monkeypatch.setenv("BRAND_DOMAIN", "acmepharma.com")
    monkeypatch.setenv(
        "GATEWAY_SECRET", "internal-gateway-secret-12345"
    )  # pragma: allowlist secret

    with pytest.raises(ValueError) as exc_info:
        CommonSettings()
    assert "FATAL STARTUP ERROR" in str(exc_info.value)
    assert "GATEWAY_SECRET" in str(exc_info.value)


def test_production_mode_fails_fast_on_default_branding(monkeypatch):
    """Verify startup fails in non-development mode when legacy/default branding is retained."""
    monkeypatch.setenv("APP_ENV", "production")
    monkeypatch.setenv("GATEWAY_SECRET", "secure-custom-production-secret-999")
    monkeypatch.setenv("SIGNING_SECRET", "secure-custom-signing-secret-999")
    monkeypatch.setenv("AUDIT_LOG_SECRET_KEY", "secure-custom-audit-secret-999")
    monkeypatch.setenv("PSEUDONYMIZATION_SALT", "secure-custom-pseudo-salt-999")
    monkeypatch.setenv("SAFETY_SALT", "secure-custom-safety-salt-999")
    monkeypatch.setenv(
        "INBOUND_EMAIL_HMAC_SECRET", "secure-custom-email-hmac-secret-999"
    )
    monkeypatch.setenv("BRAND_NAME", "Cadence Clinical")

    with pytest.raises(ValueError) as exc_info:
        CommonSettings()
    assert "STARTUP ERROR" in str(exc_info.value)
    assert "BRAND_NAME" in str(exc_info.value)


def test_cache_clear_resets_cached_settings(monkeypatch):
    """Verify clear_settings_caches flushes lru_cache."""
    clear_settings_caches()
    s1 = get_notifications_settings()
    monkeypatch.setenv("SMTP_HOST", "192.168.1.50")
    s2 = get_notifications_settings()
    assert s1 is s2
    assert s2.smtp_host == "127.0.0.1"

    clear_settings_caches()
    s3 = get_notifications_settings()
    assert s3 is not s1
    assert s3.smtp_host == "192.168.1.50"


def test_all_settings_classes_ignore_extra_env_vars(monkeypatch):
    """Verify extra="ignore" allows unexpected environment variables without throwing errors."""
    monkeypatch.setenv("UNEXPECTED_RUNTIME_ENV_VAR", "some_value_123")
    from packages.config import ALL_SETTINGS_CLASSES

    for cls in ALL_SETTINGS_CLASSES:
        inst = cls()
        assert not hasattr(inst, "UNEXPECTED_RUNTIME_ENV_VAR")


def test_all_settings_getters_and_classes():
    """Verify all 20+ microservice settings classes instantiate properly with default settings."""
    from packages.config import (
        ALL_SETTINGS_CLASSES,
        get_ai_gateway_settings,
        get_common_settings,
        get_compliance_settings,
        get_ctms_settings,
        get_deid_settings,
        get_designer_settings,
        get_econsent_settings,
        get_eisf_settings,
        get_etmf_settings,
        get_execution_settings,
        get_fileshare_settings,
        get_gateway_settings,
        get_imaging_settings,
        get_interop_settings,
        get_knowledge_settings,
        get_notifications_settings,
        get_org_settings,
        get_payments_settings,
        get_quality_settings,
        get_safety_settings,
        get_security_settings,
        get_storage_settings,
        get_telemetry_settings,
        get_tickets_settings,
    )

    assert len(ALL_SETTINGS_CLASSES) >= 20

    assert get_common_settings().app_env == "development"
    assert get_knowledge_settings().knowledge_service_url == "http://127.0.0.1:8006"
    assert get_execution_settings().outbox_attempt_cap == 5
    assert get_designer_settings().designer_database_url is not None
    assert get_gateway_settings().gateway_internal_secret is not None
    assert get_security_settings().jwt_algorithm == "RS256"
    assert get_deid_settings().deid_salt is not None
    assert get_telemetry_settings().log_level == "INFO"
    assert get_storage_settings().storage_sse_algorithm == "AES256"
    assert get_compliance_settings() is not None
    assert get_ctms_settings() is not None
    assert get_econsent_settings() is not None
    assert get_eisf_settings() is not None
    assert get_etmf_settings() is not None
    assert get_fileshare_settings() is not None
    assert get_imaging_settings() is not None
    assert get_interop_settings() is not None
    assert get_notifications_settings() is not None
    assert get_org_settings() is not None
    assert get_payments_settings() is not None
    assert get_quality_settings() is not None
    assert get_safety_settings() is not None
    assert get_tickets_settings() is not None
    assert get_ai_gateway_settings().ai_tier_1_model == "ollama/llama3"

