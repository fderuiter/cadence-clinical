import os
import sys

INSECURE_FALLBACKS = {
    "internal-gateway-secret-12345",
    "secure-clinical-salt-99",
    "internal-safety-salt-12345",
    "secure-clinical-salt-101",
    "default_secret",
    "gxp-audit-secret-key-cadence-2026",
    "dev-default-secret-inbound-email-hmac",
}

def assert_secure_secrets(service_name: str, required_secrets: dict[str, str | None]) -> None:
    """
    Validate required environment secrets on process startup.
    Immediately crashes the service with an informative error message if running in
    staging or production environments, and any critical secret is missing or uses
    an insecure fallback value.
    """
    app_env = os.getenv("APP_ENV", "").strip().lower()
    
    # Non-development environments (e.g. production or staging)
    if app_env and app_env not in ("development", "dev", "test"):
        invalid_secrets = []
        for name, value in required_secrets.items():
            if not value:
                invalid_secrets.append((name, "Missing override (empty or None)"))
                continue
            
            # Check for insecure fallbacks
            normalized_value = value.strip()
            is_insecure = (
                normalized_value in INSECURE_FALLBACKS
                or "internal-gateway-secret" in normalized_value
                or normalized_value.startswith("internal-g")
            )
            if is_insecure:
                invalid_secrets.append((name, "Uses insecure fallback value"))
        
        if invalid_secrets:
            # Format detailed error message identifying the specific environment variables
            details = "; ".join(f"{name} ({reason})" for name, reason in invalid_secrets)
            error_msg = (
                f"FATAL STARTUP ERROR: [{service_name}] Environment configuration validation failed "
                f"for non-development environment '{app_env}'. Critical secrets must have secure overrides. "
                f"Issues detected: {details}."
            )
            # Write to stderr
            print(error_msg, file=sys.stderr)
            raise RuntimeError(error_msg)
