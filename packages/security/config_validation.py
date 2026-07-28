"""
Dynamic Runtime Configuration Validation and Safety Guardrails.

This module provides utilities to dynamically inspect and assert the safety
of runtime configurations before application boot. It ensures that when
a production profile or environment is active, the system does not boot
with weak/fallback symmetric keys or active developer bypass variables.
"""

import os
from typing import Dict, List


class ConfigurationError(ValueError):
    """Exception raised when a security configuration violation is detected."""

    pass


# Default fallback secrets that are prohibited under a production profile
PROHIBITED_FALLBACK_SECRETS: Dict[str, str] = {
    "GATEWAY_SECRET": "internal-gateway-secret-12345",
    "SIGNING_SECRET": "designer-amendment-secure-key-12345",
    "REDACTION_SIGNING_SECRET": "internal-gateway-secret-12345",
}

# Active development bypass environment variables that must not be loaded in production
DEVELOPMENT_BYPASS_VARIABLES: List[str] = [
    "ALLOW_UNVERIFIED_JWT_FOR_TEST",
    "JWT_TEST_SECRET",
    "SKIP_JWKS_FETCH",
]


def is_production_profile() -> bool:
    """
    Check if the current runtime environment is configured as a production environment.

    Scans standard environment flags to dynamically determine if a production
    profile or mode is active.

    Returns:
        bool: True if the environment is determined to be production; False otherwise.
    """
    # Scan environment designations
    for var in ["ENV", "ENVIRONMENT", "APP_ENV"]:
        val = os.getenv(var, "").lower()
        if val in ("production", "prod"):
            return True

    # Scan truthy flag settings indicating production
    for var in ["PROD", "PRODUCTION"]:
        val = os.getenv(var, "").lower()
        if val in ("true", "1"):
            return True

    return False


def validate_runtime_config() -> None:
    """
    Dynamically validate the active environment variables.

    If a production profile is active, this function asserts that:
    1. No development bypass flags are enabled (which would compromise authentication).
    2. No fallback/default secrets are used (which are known and insecure).
    3. Mandatory security variables like GATEWAY_SECRET are present and cryptographically strong.

    Raises:
        ConfigurationError: If any security validation check fails.
    """
    # Only enforce strict production hardening and checks if a production profile is active
    if not is_production_profile():
        return

    # --- Check 1: Active Development Bypass Parameters ---
    # Developer bypass variables permit unauthenticated requests or mock verifications.
    # We must crash immediately on boot if any of these are active under a production profile.
    for bypass_var in DEVELOPMENT_BYPASS_VARIABLES:
        val = os.getenv(bypass_var)
        if val:
            # An active developer bypass is considered enabled if it is present and
            # not explicitly set to a falsy value.
            if val.lower() not in ("false", "0"):
                raise ConfigurationError(
                    f"Security Guardrail Violation: Active developer bypass parameter '{bypass_var}' "
                    f"detected under a production configuration. Boot sequence aborted."
                )

    # --- Check 2: Default Gateway/Fallback Secrets ---
    # Default secrets compiled in the source code can be extracted easily and must not be used in prod.
    # We evaluate variables dynamically to support services parsing individual properties at runtime.
    for secret_var, default_val in PROHIBITED_FALLBACK_SECRETS.items():
        val = os.getenv(secret_var)
        if val is not None:
            # Check if the configured secret matches the insecure default fallback value
            if val == default_val:
                raise ConfigurationError(
                    f"Security Guardrail Violation: Insecure fallback/default secret detected for "
                    f"'{secret_var}' under a production configuration. Boot sequence aborted."
                )

            # Check if the secret is too short to be cryptographically secure (e.g. less than 16 chars)
            if len(val) < 16:
                raise ConfigurationError(
                    f"Security Guardrail Violation: The secret configured in '{secret_var}' is too weak "
                    f"or too short for a production profile. Must be at least 16 characters."
                )

            # Check if the secret contains weak, predictable words
            weak_words = [
                "secret",
                "default",
                "fallback",
                "insecure",
                "password",
                "test",
                "mock",
                "demo",
                "cadence",
            ]
            if any(word in val.lower() for word in weak_words):
                raise ConfigurationError(
                    f"Security Guardrail Violation: The secret configured in '{secret_var}' contains "
                    f"weak or predictable pattern. Pattern contains prohibited keywords."
                )
        else:
            # GATEWAY_SECRET is absolutely mandatory for all microservices to authorize inter-service calls.
            if secret_var == "GATEWAY_SECRET":
                raise ConfigurationError(
                    f"Security Guardrail Violation: Mandatory security parameter '{secret_var}' is not set, "
                    f"which would trigger unsafe fallback defaults. Boot sequence aborted."
                )
