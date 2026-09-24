"""Centralized Pydantic BaseSettings models for Cadence Clinical microservices and shared packages."""

from functools import lru_cache

from pydantic import Field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

INSECURE_FALLBACKS: set[str] = {
    "internal-gateway-secret-12345",
    "secure-clinical-salt-99",
    "internal-safety-salt-12345",
    "secure-clinical-salt-101",
    "default_secret",
    "gxp-audit-secret-key-cadence-2026",
    "dev-default-secret-inbound-email-hmac",
}


class CommonSettings(BaseSettings):
    """Shared configuration parameters common across microservices."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        case_sensitive=False,
    )

    # Runtime Environment & Branding
    app_env: str = Field(default="development", validation_alias="APP_ENV")
    brand_name: str = Field(default="Cadence Clinical", validation_alias="BRAND_NAME")
    brand_domain: str = Field(default="ccrsoft.com", validation_alias="BRAND_DOMAIN")

    # Shared Secrets & Keys
    gateway_secret: str = Field(
        default="internal-gateway-secret-12345", validation_alias="GATEWAY_SECRET"
    )
    signing_secret: str = Field(
        default="designer-amendment-secure-key-12345", validation_alias="SIGNING_SECRET"
    )
    audit_log_secret_key: str = Field(
        default="gxp-audit-secret-key-cadence-2026",
        validation_alias="AUDIT_LOG_SECRET_KEY",
    )
    pseudonymization_salt: str = Field(
        default="secure-clinical-salt-98765", validation_alias="PSEUDONYMIZATION_SALT"
    )
    safety_salt: str = Field(
        default="internal-safety-salt-12345", validation_alias="SAFETY_SALT"
    )
    inbound_email_hmac_secret: str = Field(
        default="dev-default-secret-inbound-email-hmac",
        validation_alias="INBOUND_EMAIL_HMAC_SECRET",
    )

    # Cross-Service Core URLs
    gateway_url: str = Field(
        default="http://localhost:8000", validation_alias="GATEWAY_URL"
    )
    ai_gateway_url: str = Field(
        default="http://localhost:8000", validation_alias="AI_GATEWAY_URL"
    )
    designer_url: str = Field(
        default="http://localhost:8001", validation_alias="DESIGNER_URL"
    )
    execution_url: str = Field(
        default="http://localhost:8002", validation_alias="EXECUTION_URL"
    )
    etmf_url: str = Field(default="http://localhost:8003", validation_alias="ETMF_URL")
    econsent_url: str = Field(
        default="http://localhost:8004", validation_alias="ECONSENT_URL"
    )
    ctms_url: str = Field(default="http://localhost:8005", validation_alias="CTMS_URL")
    notifications_url: str = Field(
        default="http://localhost:8006", validation_alias="NOTIFICATIONS_URL"
    )
    org_url: str = Field(default="http://localhost:8010", validation_alias="ORG_URL")
    fileshare_url: str = Field(
        default="http://localhost:8011", validation_alias="FILESHARE_URL"
    )

    # Auth & OIDC
    keycloak_url: str | None = Field(default=None, validation_alias="KEYCLOAK_URL")
    keycloak_realm: str = Field(default="cadence", validation_alias="KEYCLOAK_REALM")
    keycloak_client_id: str = Field(
        default="cadence-clinical", validation_alias="KEYCLOAK_CLIENT_ID"
    )

    # Datastores Defaults
    redis_host: str | None = Field(default=None, validation_alias="REDIS_HOST")
    redis_port: int = Field(default=6379, validation_alias="REDIS_PORT")
    redis_password: str | None = Field(default=None, validation_alias="REDIS_PASSWORD")
    redis_url: str | None = Field(default=None, validation_alias="REDIS_URL")
    neo4j_uri: str = Field(
        default="bolt://localhost:7687", validation_alias="NEO4J_URI"
    )
    neo4j_user: str = Field(default="neo4j", validation_alias="NEO4J_USER")
    neo4j_password: str = Field(
        default="cadence_password", validation_alias="NEO4J_PASSWORD"
    )

    @model_validator(mode="after")
    def validate_non_dev_guardrails(self) -> CommonSettings:
        """Enforce strict non-dev security guardrails on startup."""
        env_lower = (self.app_env or "").strip().lower()

        # Non-development environments (e.g. staging, production)
        if env_lower and env_lower not in ("development", "dev", "test"):
            invalid_secrets: list[str] = []
            secrets_to_check = {
                "GATEWAY_SECRET": self.gateway_secret,
                "SIGNING_SECRET": self.signing_secret,
                "AUDIT_LOG_SECRET_KEY": self.audit_log_secret_key,
                "PSEUDONYMIZATION_SALT": self.pseudonymization_salt,
                "SAFETY_SALT": self.safety_salt,
                "INBOUND_EMAIL_HMAC_SECRET": self.inbound_email_hmac_secret,
            }
            for name, val in secrets_to_check.items():
                if not val or not val.strip():
                    invalid_secrets.append(f"{name} (Missing override)")
                    continue
                norm = val.strip()
                if (
                    norm in INSECURE_FALLBACKS
                    or "internal-gateway-secret" in norm
                    or norm.startswith("internal-g")
                ):
                    invalid_secrets.append(f"{name} (Uses insecure fallback value)")

            if invalid_secrets:
                details = "; ".join(invalid_secrets)
                raise ValueError(
                    f"FATAL STARTUP ERROR: Environment configuration validation failed "
                    f"for non-development environment '{env_lower}'. Critical secrets must have secure overrides. "
                    f"Issues detected: {details}."
                )

            invalid_branding: list[str] = []
            if not self.brand_name or self.brand_name.strip() == "Cadence Clinical":
                invalid_branding.append("BRAND_NAME")
            if not self.brand_domain or self.brand_domain.strip() in (
                "cadenceclinical.com",
                "cadence-clinical.com",
            ):
                invalid_branding.append("BRAND_DOMAIN")

            if invalid_branding:
                raise ValueError(
                    f"STARTUP ERROR: Outdated default/legacy 'Cadence' branding or domain detected "
                    f"in environment '{env_lower}' for variables: {', '.join(invalid_branding)}."
                )

        return self


class NotificationsSettings(CommonSettings):
    """Localized configuration for the Notifications service."""

    notifications_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:",
        validation_alias="NOTIFICATIONS_DATABASE_URL",
    )
    smtp_host: str = Field(default="127.0.0.1", validation_alias="SMTP_HOST")
    smtp_port: int = Field(default=1025, validation_alias="SMTP_PORT")
    smtp_username: str | None = Field(default=None, validation_alias="SMTP_USERNAME")
    smtp_password: str | None = Field(default=None, validation_alias="SMTP_PASSWORD")
    smtp_use_tls: bool = Field(default=False, validation_alias="SMTP_USE_TLS")
    smtp_use_ssl: bool = Field(default=False, validation_alias="SMTP_USE_SSL")


class EConsentSettings(CommonSettings):
    """Localized configuration for the eConsent service."""

    econsent_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="ECONSENT_DATABASE_URL"
    )
    econsent_cache_ttl: int | None = Field(
        default=None, validation_alias="ECONSENT_CACHE_TTL"
    )
    cache_ttl: int = Field(default=300, validation_alias="CACHE_TTL")
    redis_channel: str = Field(
        default="econsent_cache_invalidation", validation_alias="REDIS_CHANNEL"
    )
    redis_channel_consent: str = Field(
        default="econsent_consent_completed", validation_alias="REDIS_CHANNEL_CONSENT"
    )
    econsent_archival_attempt_cap: int = Field(
        default=5, validation_alias="ECONSENT_ARCHIVAL_ATTEMPT_CAP"
    )
    econsent_archival_poll_interval_seconds: float = Field(
        default=5.0, validation_alias="ECONSENT_ARCHIVAL_POLL_INTERVAL_SECONDS"
    )

    @property
    def effective_cache_ttl(self) -> int:
        return (
            self.econsent_cache_ttl
            if self.econsent_cache_ttl is not None
            else self.cache_ttl
        )


class CTMSSettings(CommonSettings):
    """Localized configuration for the CTMS service."""

    ctms_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="CTMS_DATABASE_URL"
    )


class FileshareSettings(CommonSettings):
    """Localized configuration for the Fileshare service."""

    fileshare_database_url: str = Field(
        default="sqlite+aiosqlite:///./fileshare.db",
        validation_alias="FILESHARE_DATABASE_URL",
    )
    clamav_host: str = Field(default="127.0.0.1", validation_alias="CLAMAV_HOST")
    clamav_port: int = Field(default=3310, validation_alias="CLAMAV_PORT")


class InteropSettings(CommonSettings):
    """Localized configuration for the Interop service."""

    interop_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="INTEROP_DATABASE_URL"
    )


class EISFSettings(CommonSettings):
    """Localized configuration for the eISF service."""

    eisf_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="EISF_DATABASE_URL"
    )


class EtmfSettings(CommonSettings):
    """Localized configuration for the eTMF service."""

    etmf_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="ETMF_DATABASE_URL"
    )


class OrgSettings(CommonSettings):
    """Localized configuration for the Org service."""

    org_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="ORG_DATABASE_URL"
    )


class QualitySettings(CommonSettings):
    """Localized configuration for the Quality service."""

    quality_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="QUALITY_DATABASE_URL"
    )


class SafetySettings(CommonSettings):
    """Localized configuration for the Safety service."""

    safety_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="SAFETY_DATABASE_URL"
    )


class TicketsSettings(CommonSettings):
    """Localized configuration for the Tickets service."""

    tickets_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="TICKETS_DATABASE_URL"
    )
    tickets_escalation_interval_seconds: float = Field(
        default=3600.0, validation_alias="TICKETS_ESCALATION_INTERVAL_SECONDS"
    )
    tickets_escalation_poll_interval_seconds: float = Field(
        default=1.0, validation_alias="TICKETS_ESCALATION_POLL_INTERVAL_SECONDS"
    )


class PaymentsSettings(CommonSettings):
    """Localized configuration for the Payments service."""

    payments_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="PAYMENTS_DATABASE_URL"
    )


class ImagingSettings(CommonSettings):
    """Localized configuration for the Imaging service."""

    imaging_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:", validation_alias="IMAGING_DATABASE_URL"
    )


class AiGatewaySettings(CommonSettings):
    """Localized configuration for the AI Gateway service."""

    ai_gateway_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:",
        validation_alias="AI_GATEWAY_DATABASE_URL",
        description="AI Gateway relational database connection string",
    )
    ai_tier_1_model: str = Field(
        default="ollama/llama3",
        validation_alias="AI_TIER_1_MODEL",
        description="Tier 1 fast local/edge LLM model identifier",
    )
    ai_tier_2_model: str = Field(
        default="gpt-4o-mini",
        validation_alias="AI_TIER_2_MODEL",
        description="Tier 2 balanced cloud LLM model identifier",
    )
    ai_tier_3_model: str = Field(
        default="gpt-4o",
        validation_alias="AI_TIER_3_MODEL",
        description="Tier 3 high-reasoning complex LLM model identifier",
    )
    ai_tier_1_embed_model: str = Field(
        default="text-embedding-3-small",
        validation_alias="AI_TIER_1_EMBED_MODEL",
        description="Default text embedding model identifier",
    )


class StorageSettings(CommonSettings):
    """Localized configuration for Storage adapters."""

    minio_endpoint_url: str | None = Field(
        default=None, validation_alias="MINIO_ENDPOINT_URL"
    )
    minio_bucket: str | None = Field(default=None, validation_alias="MINIO_BUCKET")
    minio_root_user: str | None = Field(
        default=None, validation_alias="MINIO_ROOT_USER"
    )
    minio_root_password: str | None = Field(
        default=None, validation_alias="MINIO_ROOT_PASSWORD"
    )
    storage_bucket: str | None = Field(default=None, validation_alias="STORAGE_BUCKET")
    s3_bucket_name: str | None = Field(default=None, validation_alias="S3_BUCKET_NAME")
    storage_access_key: str | None = Field(
        default=None, validation_alias="STORAGE_ACCESS_KEY"
    )
    aws_access_key_id: str | None = Field(
        default=None, validation_alias="AWS_ACCESS_KEY_ID"
    )
    storage_secret_key: str | None = Field(
        default=None, validation_alias="STORAGE_SECRET_KEY"
    )
    aws_secret_access_key: str | None = Field(
        default=None, validation_alias="AWS_SECRET_ACCESS_KEY"
    )
    storage_region: str | None = Field(default=None, validation_alias="STORAGE_REGION")
    aws_region: str | None = Field(default=None, validation_alias="AWS_REGION")
    storage_endpoint_url: str | None = Field(
        default=None, validation_alias="STORAGE_ENDPOINT_URL"
    )
    aws_endpoint_url: str | None = Field(
        default=None, validation_alias="AWS_ENDPOINT_URL"
    )
    storage_sse_algorithm: str = Field(
        default="AES256", validation_alias="STORAGE_SSE_ALGORITHM"
    )
    google_application_credentials: str | None = Field(
        default=None, validation_alias="GOOGLE_APPLICATION_CREDENTIALS"
    )
    google_drive_service_account_path: str | None = Field(
        default=None, validation_alias="GOOGLE_DRIVE_SERVICE_ACCOUNT_PATH"
    )
    google_drive_folder_id: str | None = Field(
        default=None, validation_alias="GOOGLE_DRIVE_FOLDER_ID"
    )


class KnowledgeSettings(CommonSettings):
    """Localized configuration for the Knowledge & Support Hub service."""

    knowledge_database_url: str = Field(
        default="sqlite+aiosqlite:///./knowledge.db",
        validation_alias="KNOWLEDGE_DATABASE_URL",
        description="Knowledge service relational database URL",
    )
    knowledge_service_url: str = Field(
        default="http://127.0.0.1:8006",
        validation_alias="KNOWLEDGE_SERVICE_URL",
        description="Knowledge service REST endpoint URL",
    )


class ExecutionSettings(CommonSettings):
    """Localized configuration for the EDC Execution service."""

    execution_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:",
        validation_alias="EXECUTION_DATABASE_URL",
        description="EDC Execution database connection string",
    )
    execution_service_url: str | None = Field(
        default=None,
        validation_alias="EXECUTION_SERVICE_URL",
        description="EDC Execution REST endpoint URL",
    )
    anomaly_worker_poll_interval_seconds: float = Field(
        default=30.0,
        validation_alias="ANOMALY_WORKER_POLL_INTERVAL_SECONDS",
        description="AI anomaly detection worker polling interval in seconds",
    )
    coding_cache_ttl: int = Field(
        default=86400,
        validation_alias="CODING_CACHE_TTL",
        description="Medical coding term matcher cache TTL in seconds",
    )
    lab_range_cache_ttl: int = Field(
        default=3600,
        validation_alias="LAB_RANGE_CACHE_TTL",
        description="Lab normal range cache TTL in seconds",
    )
    query_escalation_digest_seconds: float = Field(
        default=86400.0,
        validation_alias="QUERY_ESCALATION_DIGEST_SECONDS",
        description="Data query escalation email digest interval in seconds",
    )
    query_escalation_interval_seconds: float = Field(
        default=86400.0,
        validation_alias="QUERY_ESCALATION_INTERVAL_SECONDS",
        description="Data query escalation check loop interval in seconds",
    )
    run_background_workers: bool = Field(
        default=True,
        validation_alias="RUN_BACKGROUND_WORKERS",
        description="Flag to toggle execution background polling workers",
    )
    sealer_interval_seconds: float = Field(
        default=60.0,
        validation_alias="SEALER_INTERVAL_SECONDS",
        description="Audit trail Merkle block sealer interval in seconds",
    )
    rtsm_master_secret: str = Field(
        default="dev-rtsm-master-secret-placeholder",
        validation_alias="RTSM_MASTER_SECRET",
        description="RTSM randomization master secret key",
    )
    biostat_export_salt: str = Field(
        default="dev-biostat-export-salt-placeholder",
        validation_alias="BIOSTAT_EXPORT_SALT",
        description="Biostat statistical export pseudonymization salt",
    )
    outbox_attempt_cap: int = Field(
        default=5,
        validation_alias="OUTBOX_ATTEMPT_CAP",
        description="Transactional outbox retry attempt cap",
    )
    outbox_batch_size: int = Field(
        default=20,
        validation_alias="OUTBOX_BATCH_SIZE",
        description="Transactional outbox batch processing size",
    )
    outbox_max_concurrency: int = Field(
        default=10,
        validation_alias="OUTBOX_MAX_CONCURRENCY",
        description="Transactional outbox max concurrency worker limit",
    )
    outbox_poll_interval_seconds: float = Field(
        default=5.0,
        validation_alias="OUTBOX_POLL_INTERVAL_SECONDS",
        description="Transactional outbox polling loop interval in seconds",
    )


class DesignerSettings(CommonSettings):
    """Localized configuration for the Study Designer service."""

    designer_database_url: str = Field(
        default="sqlite+aiosqlite:///:memory:",
        validation_alias="DESIGNER_DATABASE_URL",
        description="Study Designer database connection string",
    )


class GatewaySettings(CommonSettings):
    """Localized configuration for the API Gateway service."""

    gateway_internal_secret: str = Field(
        default="dev-gateway-internal-secret-placeholder",
        validation_alias="GATEWAY_INTERNAL_SECRET",
        description="Gateway internal service authentication secret",
    )


class SecuritySettings(CommonSettings):
    """Localized configuration for Security & Identity modules."""

    jwt_algorithm: str = Field(
        default="RS256",
        validation_alias="JWT_ALGORITHM",
        description="OIDC JWT token verification algorithm",
    )
    jwt_secret_key: str = Field(
        default="dev-jwt-secret-key-12345",
        validation_alias="JWT_SECRET_KEY",
        description="JWT secret key for HMAC fallback validation",
    )


class DeidSettings(CommonSettings):
    """Localized configuration for De-identification package."""

    deid_salt: str = Field(
        default="secure-clinical-salt-98765",
        validation_alias="DEID_SALT",
        description="De-identification hashing salt",
    )


class TelemetrySettings(CommonSettings):
    """Localized configuration for Telemetry & Logging."""

    log_level: str = Field(
        default="INFO",
        validation_alias="LOG_LEVEL",
        description="Structured application log verbosity level",
    )


class ComplianceSettings(CommonSettings):
    """Localized configuration for Compliance & GxP Signing."""

    gxp_signer_id: str | None = Field(default=None, validation_alias="GXP_SIGNER_ID")
    github_actor: str | None = Field(default=None, validation_alias="GITHUB_ACTOR")
    user: str | None = Field(default=None, validation_alias="USER")
    gxp_signing_private_key: str | None = Field(
        default=None, validation_alias="GXP_SIGNING_PRIVATE_KEY"
    )
    gxp_signing_cert: str | None = Field(
        default=None, validation_alias="GXP_SIGNING_CERT"
    )


# Cached Factory Instances
@lru_cache
def get_common_settings() -> CommonSettings:
    return CommonSettings()


@lru_cache
def get_notifications_settings() -> NotificationsSettings:
    return NotificationsSettings()


@lru_cache
def get_econsent_settings() -> EConsentSettings:
    return EConsentSettings()


@lru_cache
def get_ctms_settings() -> CTMSSettings:
    return CTMSSettings()


@lru_cache
def get_fileshare_settings() -> FileshareSettings:
    return FileshareSettings()


@lru_cache
def get_interop_settings() -> InteropSettings:
    return InteropSettings()


@lru_cache
def get_eisf_settings() -> EISFSettings:
    return EISFSettings()


@lru_cache
def get_etmf_settings() -> EtmfSettings:
    return EtmfSettings()


@lru_cache
def get_org_settings() -> OrgSettings:
    return OrgSettings()


@lru_cache
def get_quality_settings() -> QualitySettings:
    return QualitySettings()


@lru_cache
def get_safety_settings() -> SafetySettings:
    return SafetySettings()


@lru_cache
def get_tickets_settings() -> TicketsSettings:
    return TicketsSettings()


@lru_cache
def get_payments_settings() -> PaymentsSettings:
    return PaymentsSettings()


@lru_cache
def get_imaging_settings() -> ImagingSettings:
    return ImagingSettings()


@lru_cache
def get_ai_gateway_settings() -> AiGatewaySettings:
    return AiGatewaySettings()


@lru_cache
def get_knowledge_settings() -> KnowledgeSettings:
    return KnowledgeSettings()


@lru_cache
def get_execution_settings() -> ExecutionSettings:
    return ExecutionSettings()


@lru_cache
def get_designer_settings() -> DesignerSettings:
    return DesignerSettings()


@lru_cache
def get_gateway_settings() -> GatewaySettings:
    return GatewaySettings()


@lru_cache
def get_security_settings() -> SecuritySettings:
    return SecuritySettings()


@lru_cache
def get_deid_settings() -> DeidSettings:
    return DeidSettings()


@lru_cache
def get_telemetry_settings() -> TelemetrySettings:
    return TelemetrySettings()


@lru_cache
def get_storage_settings() -> StorageSettings:
    return StorageSettings()


@lru_cache
def get_compliance_settings() -> ComplianceSettings:
    return ComplianceSettings()


ALL_SETTINGS_CLASSES: list[type[BaseSettings]] = [
    CommonSettings,
    NotificationsSettings,
    EConsentSettings,
    CTMSSettings,
    FileshareSettings,
    InteropSettings,
    EISFSettings,
    EtmfSettings,
    OrgSettings,
    QualitySettings,
    SafetySettings,
    TicketsSettings,
    PaymentsSettings,
    ImagingSettings,
    AiGatewaySettings,
    KnowledgeSettings,
    ExecutionSettings,
    DesignerSettings,
    GatewaySettings,
    SecuritySettings,
    DeidSettings,
    TelemetrySettings,
    StorageSettings,
    ComplianceSettings,
]


def clear_settings_caches() -> None:
    """Clear all settings caches for test isolation."""
    get_common_settings.cache_clear()
    get_notifications_settings.cache_clear()
    get_econsent_settings.cache_clear()
    get_ctms_settings.cache_clear()
    get_fileshare_settings.cache_clear()
    get_interop_settings.cache_clear()
    get_eisf_settings.cache_clear()
    get_etmf_settings.cache_clear()
    get_org_settings.cache_clear()
    get_quality_settings.cache_clear()
    get_safety_settings.cache_clear()
    get_tickets_settings.cache_clear()
    get_payments_settings.cache_clear()
    get_imaging_settings.cache_clear()
    get_ai_gateway_settings.cache_clear()
    get_knowledge_settings.cache_clear()
    get_execution_settings.cache_clear()
    get_designer_settings.cache_clear()
    get_gateway_settings.cache_clear()
    get_security_settings.cache_clear()
    get_deid_settings.cache_clear()
    get_telemetry_settings.cache_clear()
    get_storage_settings.cache_clear()
    get_compliance_settings.cache_clear()
