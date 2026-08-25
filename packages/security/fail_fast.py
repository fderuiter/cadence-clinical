import os

INSECURE_FALLBACKS = {
    "internal-gateway-secret-12345",
    "secure-clinical-salt-99",
    "internal-safety-salt-12345",
    "secure-clinical-salt-101",
    "default_secret",
    "gxp-audit-secret-key-cadence-2026",
    "dev-default-secret-inbound-email-hmac",
    "default-guest-secret",
    "cadence-dev-secret-change-in-prod",
    "internal-device-secret-key-12345",
    "internal-cgt-crypto-secret-key-12345",
    "default-master-secret-change-me-in-production",
    "secure-clinical-salt-98765",
    "mock_secret",
    "cadence_secret_pass",
    "cadence-imaging-deid-secret-12345",
    "cadence-clinical-gxp-secret-key-2026",
}


def assert_secure_secrets(
    service_name: str, required_secrets: dict[str, str | None]
) -> None:
    """Validate required environment secrets on process startup.

    Immediately crashes the service with an informative error message across all
    environments if any critical secret is missing or uses an insecure fallback value.

    Args:
        service_name: Name of the service invoking secret validation.
        required_secrets: Mapping of secret variable names to their active values.

    Raises:
        RuntimeError: If any critical secret is missing, empty, or matches an insecure fallback value.
    """
    invalid_secrets = []
    for name, value in required_secrets.items():
        if not value or not str(value).strip():
            invalid_secrets.append((name, "Missing override (empty or None)"))
            continue

        # Check for insecure fallbacks
        normalized_value = str(value).strip()
        is_insecure = (
            normalized_value in INSECURE_FALLBACKS
            or "internal-gateway-secret" in normalized_value
            or normalized_value.startswith("internal-g")
            or "secure-clinical-salt" in normalized_value
            or "internal-cgt-crypto-secret" in normalized_value
            or "internal-device-secret" in normalized_value
            or "cadence_secret" in normalized_value
            or normalized_value == "mock_secret"
        )
        if is_insecure:
            invalid_secrets.append((name, "Uses insecure fallback value"))

    if invalid_secrets:
        # Format detailed error message identifying the specific environment variables
        details = "; ".join(f"{name} ({reason})" for name, reason in invalid_secrets)
        error_msg = (
            f"FATAL STARTUP ERROR: [{service_name}] Environment configuration validation failed. "
            f"Critical secrets must have secure overrides. "
            f"Issues detected: {details}."
        )
        raise RuntimeError(error_msg)


def validate_branding(service_name: str, is_gateway: bool = False) -> None:
    """Validate branding, domain, and authentication configurations on startup.

    Halts the boot sequence by raising a RuntimeError if legacy or default domain
    or unconfigured values are detected in production or staging environments.

    Args:
        service_name: Name of the service invoking branding validation.
        is_gateway: True if the calling service is the central API gateway.

    Raises:
        RuntimeError: If branding or domain configuration uses default legacy values in non-dev environments.
    """
    app_env = os.getenv("APP_ENV", "").strip().lower()
    is_prod_or_staging = app_env not in ("development", "dev", "test", "")

    if is_prod_or_staging:
        invalid = []
        brand_name = os.getenv("BRAND_NAME")
        if not brand_name or brand_name.strip() == "Cadence Clinical":
            invalid.append("BRAND_NAME")

        brand_domain = os.getenv("BRAND_DOMAIN")
        if (
            not brand_domain
            or brand_domain.strip() == "cadenceclinical.com"
            or brand_domain.strip() == "cadence-clinical.com"
        ):
            invalid.append("BRAND_DOMAIN")

        if is_gateway:
            keycloak_realm = os.getenv("KEYCLOAK_REALM")
            if not keycloak_realm or keycloak_realm.strip() == "cadence":
                invalid.append("KEYCLOAK_REALM")

            keycloak_client_id = os.getenv("KEYCLOAK_CLIENT_ID")
            if (
                not keycloak_client_id
                or keycloak_client_id.strip() == "cadence-clinical"
            ):
                invalid.append("KEYCLOAK_CLIENT_ID")

        if invalid:
            error_msg = (
                f"STARTUP ERROR: [{service_name}] Outdated default/legacy 'Cadence' branding, domain or "
                f"missing secure configurations detected in environment '{app_env}' for variables: "
                f"{', '.join(invalid)}. Halting boot sequence."
            )
            raise RuntimeError(error_msg)
