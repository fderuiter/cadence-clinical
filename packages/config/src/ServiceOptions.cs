namespace Cadence.Config;

/// <summary>
/// Configuration options for the Notifications service.
/// </summary>
public class NotificationsOptions : CommonOptions
{
    /// <summary>Database connection string for Notifications service.</summary>
    public string NotificationsDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";

    /// <summary>SMTP host address.</summary>
    public string SmtpHost { get; set; } = "127.0.0.1";

    /// <summary>SMTP port number.</summary>
    public int SmtpPort { get; set; } = 1025;

    /// <summary>SMTP authentication username.</summary>
    public string? SmtpUsername { get; set; }

    /// <summary>SMTP authentication password.</summary>
    public string? SmtpPassword { get; set; }

    /// <summary>Flag enabling TLS for SMTP connection.</summary>
    public bool SmtpUseTls { get; set; }

    /// <summary>Flag enabling SSL for SMTP connection.</summary>
    public bool SmtpUseSsl { get; set; }
}

/// <summary>
/// Configuration options for the eConsent service.
/// </summary>
public class EConsentOptions : CommonOptions
{
    /// <summary>Database connection string for eConsent service.</summary>
    public string EconsentDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";

    /// <summary>Optional localized cache TTL override.</summary>
    public int? EconsentCacheTtl { get; set; }

    /// <summary>Default cache TTL in seconds.</summary>
    public int CacheTtl { get; set; } = 300;

    /// <summary>Redis channel for cache invalidation events.</summary>
    public string RedisChannel { get; set; } = "econsent_cache_invalidation";

    /// <summary>Redis channel for completed consent events.</summary>
    public string RedisChannelConsent { get; set; } = "econsent_consent_completed";

    /// <summary>eConsent archival retry attempt cap.</summary>
    public int EconsentArchivalAttemptCap { get; set; } = 5;

    /// <summary>eConsent archival polling interval in seconds.</summary>
    public double EconsentArchivalPollIntervalSeconds { get; set; } = 5.0;

    /// <summary>Effective cache TTL value in seconds.</summary>
    public int EffectiveCacheTtl => EconsentCacheTtl ?? CacheTtl;
}

/// <summary>
/// Configuration options for the CTMS service.
/// </summary>
public class CtmsOptions : CommonOptions
{
    /// <summary>Database connection string for CTMS service.</summary>
    public string CtmsDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the Fileshare service.
/// </summary>
public class FileshareOptions : CommonOptions
{
    /// <summary>Database connection string for Fileshare service.</summary>
    public string FileshareDatabaseUrl { get; set; } = "sqlite+aiosqlite:///./fileshare.db";

    /// <summary>ClamAV daemon host address.</summary>
    public string ClamavHost { get; set; } = "127.0.0.1";

    /// <summary>ClamAV daemon port number.</summary>
    public int ClamavPort { get; set; } = 3310;
}

/// <summary>
/// Configuration options for the Interop service.
/// </summary>
public class InteropOptions : CommonOptions
{
    /// <summary>Database connection string for Interop service.</summary>
    public string InteropDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the eISF service.
/// </summary>
public class EisfOptions : CommonOptions
{
    /// <summary>Database connection string for eISF service.</summary>
    public string EisfDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the eTMF service.
/// </summary>
public class EtmfOptions : CommonOptions
{
    /// <summary>Database connection string for eTMF service.</summary>
    public string EtmfDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the Org service.
/// </summary>
public class OrgOptions : CommonOptions
{
    /// <summary>Database connection string for Org service.</summary>
    public string OrgDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the Quality service.
/// </summary>
public class QualityOptions : CommonOptions
{
    /// <summary>Database connection string for Quality service.</summary>
    public string QualityDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the Safety service.
/// </summary>
public class SafetyOptions : CommonOptions
{
    /// <summary>Database connection string for Safety service.</summary>
    public string SafetyDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";

    /// <summary>Safety transmission API endpoint URL.</summary>
    public string? SafetyDbTransmissionEndpoint { get; set; }

    /// <summary>Safety cases ingestion API endpoint URL.</summary>
    public string? SafetyDbIngestionEndpoint { get; set; }
}

/// <summary>
/// Configuration options for the Tickets service.
/// </summary>
public class TicketsOptions : CommonOptions
{
    /// <summary>Database connection string for Tickets service.</summary>
    public string TicketsDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";

    /// <summary>Support ticket escalation interval in seconds.</summary>
    public double TicketsEscalationIntervalSeconds { get; set; } = 3600.0;

    /// <summary>Support ticket escalation polling interval in seconds.</summary>
    public double TicketsEscalationPollIntervalSeconds { get; set; } = 1.0;
}

/// <summary>
/// Configuration options for the Payments service.
/// </summary>
public class PaymentsOptions : CommonOptions
{
    /// <summary>Database connection string for Payments service.</summary>
    public string PaymentsDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the Imaging service.
/// </summary>
public class ImagingOptions : CommonOptions
{
    /// <summary>Database connection string for Imaging service.</summary>
    public string ImagingDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";

    /// <summary>S3/MinIO bucket name for DICOM objects.</summary>
    public string ImagingStorageBucket { get; set; } = "imaging-dicom";

    /// <summary>MinIO endpoint URL override.</summary>
    public string? MinioEndpointUrl { get; set; }

    /// <summary>Generic storage endpoint URL override.</summary>
    public string? StorageEndpointUrl { get; set; }

    /// <summary>AWS endpoint URL override.</summary>
    public string? AwsEndpointUrl { get; set; }
}

/// <summary>
/// Configuration options for the AI Gateway service.
/// </summary>
public class AiGatewayOptions : CommonOptions
{
    /// <summary>Database connection string for AI Gateway service.</summary>
    public string AiGatewayDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";

    /// <summary>Tier 1 fast local/edge LLM model identifier.</summary>
    public string AiTier1Model { get; set; } = "ollama/llama3";

    /// <summary>Tier 2 balanced cloud LLM model identifier.</summary>
    public string AiTier2Model { get; set; } = "gpt-4o-mini";

    /// <summary>Tier 3 high-reasoning complex LLM model identifier.</summary>
    public string AiTier3Model { get; set; } = "gpt-4o";

    /// <summary>Default text embedding model identifier.</summary>
    public string AiTier1EmbedModel { get; set; } = "text-embedding-3-small";
}

/// <summary>
/// Configuration options for Storage adapters.
/// </summary>
public class StorageOptions : CommonOptions
{
    /// <summary>MinIO endpoint URL.</summary>
    public string? MinioEndpointUrl { get; set; }

    /// <summary>MinIO bucket name.</summary>
    public string? MinioBucket { get; set; }

    /// <summary>MinIO root username.</summary>
    public string? MinioRootUser { get; set; }

    /// <summary>MinIO root password.</summary>
    public string? MinioRootPassword { get; set; }

    /// <summary>Generic storage bucket name.</summary>
    public string? StorageBucket { get; set; }

    /// <summary>S3 bucket name.</summary>
    public string? S3BucketName { get; set; }

    /// <summary>Storage access key.</summary>
    public string? StorageAccessKey { get; set; }

    /// <summary>AWS access key ID.</summary>
    public string? AwsAccessKeyId { get; set; }

    /// <summary>Storage secret key.</summary>
    public string? StorageSecretKey { get; set; }

    /// <summary>AWS secret access key.</summary>
    public string? AwsSecretAccessKey { get; set; }

    /// <summary>Storage region.</summary>
    public string? StorageRegion { get; set; }

    /// <summary>AWS region.</summary>
    public string? AwsRegion { get; set; }

    /// <summary>Storage endpoint URL.</summary>
    public string? StorageEndpointUrl { get; set; }

    /// <summary>AWS endpoint URL.</summary>
    public string? AwsEndpointUrl { get; set; }

    /// <summary>Storage Server-Side Encryption algorithm.</summary>
    public string StorageSseAlgorithm { get; set; } = "AES256";

    /// <summary>Google Application Credentials path.</summary>
    public string? GoogleApplicationCredentials { get; set; }

    /// <summary>Google Drive service account JSON file path.</summary>
    public string? GoogleDriveServiceAccountPath { get; set; }

    /// <summary>Google Drive target folder ID.</summary>
    public string? GoogleDriveFolderId { get; set; }
}

/// <summary>
/// Configuration options for the Knowledge Hub service.
/// </summary>
public class KnowledgeOptions : CommonOptions
{
    /// <summary>Database connection string for Knowledge service.</summary>
    public string KnowledgeDatabaseUrl { get; set; } = "sqlite+aiosqlite:///./knowledge.db";

    /// <summary>Knowledge service REST endpoint URL.</summary>
    public string KnowledgeServiceUrl { get; set; } = "http://127.0.0.1:8006";
}

/// <summary>
/// Configuration options for the EDC Execution service.
/// </summary>
public class ExecutionOptions : CommonOptions
{
    /// <summary>Database connection string for EDC Execution service.</summary>
    public string ExecutionDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";

    /// <summary>EDC Execution REST endpoint URL.</summary>
    public string? ExecutionServiceUrl { get; set; }

    /// <summary>AI anomaly detection worker polling interval in seconds.</summary>
    public double AnomalyWorkerPollIntervalSeconds { get; set; } = 30.0;

    /// <summary>Medical coding term matcher cache TTL in seconds.</summary>
    public int CodingCacheTtl { get; set; } = 86400;

    /// <summary>Lab normal range cache TTL in seconds.</summary>
    public int LabRangeCacheTtl { get; set; } = 3600;

    /// <summary>Data query escalation email digest interval in seconds.</summary>
    public double QueryEscalationDigestSeconds { get; set; } = 86400.0;

    /// <summary>Data query escalation check loop interval in seconds.</summary>
    public double QueryEscalationIntervalSeconds { get; set; } = 86400.0;

    /// <summary>Flag to toggle execution background polling workers.</summary>
    public bool RunBackgroundWorkers { get; set; } = true;

    /// <summary>Audit trail Merkle block sealer interval in seconds.</summary>
    public double SealerIntervalSeconds { get; set; } = 60.0;

    /// <summary>RTSM randomization master secret key.</summary>
    public string? RtsmMasterSecret { get; set; }

    /// <summary>Biostat statistical export pseudonymization salt.</summary>
    public string? BiostatExportSalt { get; set; }

    /// <summary>Transactional outbox retry attempt cap.</summary>
    public int OutboxAttemptCap { get; set; } = 5;

    /// <summary>Transactional outbox batch processing size.</summary>
    public int OutboxBatchSize { get; set; } = 20;

    /// <summary>Transactional outbox max concurrency worker limit.</summary>
    public int OutboxMaxConcurrency { get; set; } = 10;

    /// <summary>Transactional outbox polling loop interval in seconds.</summary>
    public double OutboxPollIntervalSeconds { get; set; } = 5.0;
}

/// <summary>
/// Configuration options for the Study Designer service.
/// </summary>
public class DesignerOptions : CommonOptions
{
    /// <summary>Database connection string for Study Designer service.</summary>
    public string DesignerDatabaseUrl { get; set; } = "sqlite+aiosqlite:///:memory:";
}

/// <summary>
/// Configuration options for the API Gateway service.
/// </summary>
public class GatewayOptions : CommonOptions
{
    /// <summary>Gateway internal service authentication secret.</summary>
    public string? GatewayInternalSecret { get; set; }
}

/// <summary>
/// Configuration options for Security and Identity modules.
/// </summary>
public class SecurityOptions : CommonOptions
{
    /// <summary>OIDC JWT token verification algorithm.</summary>
    public string JwtAlgorithm { get; set; } = "RS256";

    /// <summary>JWT secret key for HMAC fallback validation.</summary>
    public string? JwtSecretKey { get; set; }
}

/// <summary>
/// Configuration options for De-identification package.
/// </summary>
public class DeidOptions : CommonOptions
{
    /// <summary>De-identification hashing salt.</summary>
    public string? DeidSalt { get; set; }
}

/// <summary>
/// Configuration options for Telemetry and Logging.
/// </summary>
public class TelemetryOptions : CommonOptions
{
    /// <summary>Structured application log verbosity level.</summary>
    public string LogLevel { get; set; } = "INFO";
}

/// <summary>
/// Configuration options for Compliance and GxP Signing.
/// </summary>
public class ComplianceOptions : CommonOptions
{
    /// <summary>GxP digital signer identifier.</summary>
    public string? GxpSignerId { get; set; }

    /// <summary>GitHub actor environment variable value.</summary>
    public string? GithubActor { get; set; }

    /// <summary>Current OS user username.</summary>
    public string? User { get; set; }

    /// <summary>GxP signing private key string.</summary>
    public string? GxpSigningPrivateKey { get; set; }

    /// <summary>GxP signing certificate string.</summary>
    public string? GxpSigningCert { get; set; }
}
