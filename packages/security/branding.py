import logging
import os
import sys

logger = logging.getLogger("security.branding")

# Standardized fallback corporate parameters
FALLBACK_BRAND_NAME = "Cadence Clinical"
FALLBACK_BRAND_DOMAIN = "ccrsoft.com"


def get_brand_name() -> str:
    """
    Get the configured BRAND_NAME, fallback to standard corporate name.
    """
    return os.getenv("BRAND_NAME") or FALLBACK_BRAND_NAME


def get_brand_domain() -> str:
    """
    Get the configured BRAND_DOMAIN, fallback to identical corporate domain.
    """
    return os.getenv("BRAND_DOMAIN") or FALLBACK_BRAND_DOMAIN


def validate_branding(
    service_name: str, check_auth_keys: bool = False
) -> tuple[str, str]:
    """
    Validate mandatory branding/domain and optionally authentication configurations.
    Centralized validation rules across the gateway and all microservices.

    If running in a non-development environment (production/staging) OR CI/CD automated pipeline
    execution, fails fast and halts the boot sequence on invalid defaults.
    Otherwise, degrades to logging a non-blocking warning.

    Returns a tuple of (brand_name, brand_domain).
    """
    app_env = os.getenv("APP_ENV", "").strip().lower()
    is_ci = (
        os.getenv("CI", "").strip().lower() in ("true", "1")
        or os.getenv("GITHUB_ACTIONS") is not None
    )
    is_prod_or_staging = app_env not in ("development", "dev", "test", "")

    # We enforce strict validation in non-development environments OR in any CI/CD pipeline run.
    strict_mode = is_prod_or_staging or is_ci

    brand_name = os.getenv("BRAND_NAME")
    brand_domain = os.getenv("BRAND_DOMAIN")

    invalid = []

    # Validation rules:
    # 1. BRAND_NAME must not be empty and must not be the outdated default "Cadence Clinical"
    if not brand_name or brand_name.strip() == "Cadence Clinical":
        invalid.append("BRAND_NAME")

    # 2. BRAND_DOMAIN must not be empty and must not be the legacy/outdated default "cadenceclinical.com"
    if not brand_domain or brand_domain.strip() == "cadenceclinical.com":
        invalid.append("BRAND_DOMAIN")

    if check_auth_keys:
        realm = os.getenv("KEYCLOAK_REALM")
        client_id = os.getenv("KEYCLOAK_CLIENT_ID")
        if not realm or realm.strip() == "cadence":
            invalid.append("KEYCLOAK_REALM")
        if not client_id or client_id.strip() == "cadence-clinical":
            invalid.append("KEYCLOAK_CLIENT_ID")

    # Resolve values with fallback
    resolved_name = brand_name or FALLBACK_BRAND_NAME
    resolved_domain = brand_domain or FALLBACK_BRAND_DOMAIN

    if invalid:
        mode_str = "CI/CD pipeline" if is_ci else f"environment '{app_env}'"
        error_msg = (
            f"STARTUP ERROR: Outdated default 'Cadence' branding or missing secure configurations "
            f"detected in {mode_str} for variables: {', '.join(invalid)}. Halting boot sequence."
        )
        if strict_mode:
            print(error_msg, file=sys.stderr)
            logger.error(error_msg)
            # Fail immediately and halt deployment/boot
            sys.exit(1)
        else:
            # Degrade to non-blocking warning logs
            warning_msg = (
                f"WARNING: Outdated default 'Cadence' branding or missing secure configurations "
                f"detected for variables: {', '.join(invalid)}. Local start proceeding with fallback domain."
            )
            print(warning_msg, file=sys.stderr)
            logger.warning(warning_msg)

    return resolved_name, resolved_domain
