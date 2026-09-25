namespace Cadence.Config;

/// <summary>
/// Shared configuration options common across Cadence Clinical microservices.
/// </summary>
public class CommonOptions
{
    /// <summary>Runtime environment name (e.g. "development", "staging", "production").</summary>
    public string AppEnv { get; set; } = "development";

    /// <summary>Brand name for application display.</summary>
    public string BrandName { get; set; } = "Cadence Clinical";

    /// <summary>Brand domain name.</summary>
    public string BrandDomain { get; set; } = "ccrsoft.com";

    /// <summary>Shared internal gateway secret.</summary>
    public string GatewaySecret { get; set; } = "internal-gateway-secret-12345";

    /// <summary>Shared signing secret.</summary>
    public string SigningSecret { get; set; } = "designer-amendment-secure-key-12345";

    /// <summary>GxP audit log secret key.</summary>
    public string AuditLogSecretKey { get; set; } = "gxp-audit-secret-key-cadence-2026";

    /// <summary>Pseudonymization hashing salt.</summary>
    public string PseudonymizationSalt { get; set; } = "secure-clinical-salt-98765";

    /// <summary>Safety salt key.</summary>
    public string SafetySalt { get; set; } = "internal-safety-salt-12345";

    /// <summary>Inbound email HMAC secret key.</summary>
    public string InboundEmailHmacSecret { get; set; } = "dev-default-secret-inbound-email-hmac";

    /// <summary>API Gateway base URL.</summary>
    public string GatewayUrl { get; set; } = "http://localhost:8000";

    /// <summary>AI Gateway service base URL.</summary>
    public string AiGatewayUrl { get; set; } = "http://localhost:8000";

    /// <summary>Study Designer service base URL.</summary>
    public string DesignerUrl { get; set; } = "http://localhost:8001";

    /// <summary>EDC Execution service base URL.</summary>
    public string ExecutionUrl { get; set; } = "http://localhost:8002";

    /// <summary>eTMF service base URL.</summary>
    public string EtmfUrl { get; set; } = "http://localhost:8003";

    /// <summary>eConsent service base URL.</summary>
    public string EconsentUrl { get; set; } = "http://localhost:8004";

    /// <summary>CTMS service base URL.</summary>
    public string CtmsUrl { get; set; } = "http://localhost:8005";

    /// <summary>Notifications service base URL.</summary>
    public string NotificationsUrl { get; set; } = "http://localhost:8006";

    /// <summary>Org service base URL.</summary>
    public string OrgUrl { get; set; } = "http://localhost:8010";

    /// <summary>Fileshare service base URL.</summary>
    public string FileshareUrl { get; set; } = "http://localhost:8011";

    /// <summary>Keycloak OIDC server URL.</summary>
    public string? KeycloakUrl { get; set; }

    /// <summary>Keycloak OIDC realm.</summary>
    public string KeycloakRealm { get; set; } = "cadence";

    /// <summary>Keycloak OIDC client ID.</summary>
    public string KeycloakClientId { get; set; } = "cadence-clinical";

    /// <summary>Redis server hostname.</summary>
    public string? RedisHost { get; set; }

    /// <summary>Redis server port.</summary>
    public int RedisPort { get; set; } = 6379;

    /// <summary>Redis password.</summary>
    public string? RedisPassword { get; set; }

    /// <summary>Redis connection URL.</summary>
    public string? RedisUrl { get; set; }

    /// <summary>Neo4j database connection URI.</summary>
    public string Neo4jUri { get; set; } = "bolt://localhost:7687";

    /// <summary>Neo4j database username.</summary>
    public string Neo4jUser { get; set; } = "neo4j";

    /// <summary>Neo4j database password.</summary>
    public string Neo4jPassword { get; set; } = "cadence_password";
}
