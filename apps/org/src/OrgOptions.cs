namespace Cadence.Modules.Org;

/// <summary>
/// Configuration for the Organization Administration module (<c>Org</c> configuration section).
/// </summary>
public sealed class OrgOptions
{
    /// <summary>The configuration section name (<c>Org</c>).</summary>
    public const string SectionName = "Org";

    /// <summary>Uses the deterministic in-memory OIDC user provisioner (permitted in Development and Testing only).</summary>
    public bool InMemoryProvisioner { get; set; }

    /// <summary>Keycloak Admin REST API settings used when provisioning OIDC accounts and sending welcome links.</summary>
    public KeycloakAdminOptions Keycloak { get; set; } = new();
}

/// <summary>
/// Connection and OAuth2 client settings for the Keycloak Admin REST API (ADR-2819).
/// </summary>
public sealed class KeycloakAdminOptions
{
    /// <summary>Base URL of the Keycloak server (for example <c>http://keycloak:8080</c>).</summary>
    public string? AdminBaseUrl { get; set; }

    /// <summary>Target Keycloak realm in which clinical tenant users live.</summary>
    public string Realm { get; set; } = "cadence";

    /// <summary>Keycloak realm against which the admin client or service user authenticates.</summary>
    public string TokenRealm { get; set; } = "cadence";

    /// <summary>OAuth2 client identifier used to acquire admin API tokens.</summary>
    public string ClientId { get; set; } = "admin-cli";

    /// <summary>OAuth2 client secret when using <c>client_credentials</c> grant.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Admin username when authenticating via resource-owner password grant in local environments.</summary>
    public string? AdminUsername { get; set; }

    /// <summary>Admin password when authenticating via resource-owner password grant in local environments.</summary>
    public string? AdminPassword { get; set; }

    /// <summary>Optional OIDC client id passed to Keycloak's <c>execute-actions-email</c> welcome link.</summary>
    public string? RedirectClientId { get; set; } = "cadence-web";

    /// <summary>Optional post-password-setup redirect URI passed to Keycloak's <c>execute-actions-email</c> welcome link.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>Lifespan in seconds of the password-setup welcome link (defaults to 24 hours).</summary>
    public int ActionLifespanSeconds { get; set; } = 86400; // deid-ignore: seconds in 24 hours, not a postal code

    /// <summary>Whether newly created OIDC users receive Keycloak's <c>UPDATE_PASSWORD</c> welcome email.</summary>
    public bool SendWelcomeEmail { get; set; } = true;
}
