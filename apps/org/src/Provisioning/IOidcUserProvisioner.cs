using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;

namespace Cadence.Modules.Org.Provisioning;

/// <summary>
/// Parameters for provisioning or reusing an OIDC colleague account for a tenant workspace (ADR-2819).
/// </summary>
/// <param name="TenantCode">The signed tenant code (<c>tenant_id</c> claim in Keycloak).</param>
/// <param name="Email">Normalized colleague email address.</param>
/// <param name="GivenName">Colleague given name.</param>
/// <param name="FamilyName">Colleague family name.</param>
/// <param name="RealmRoles">Keycloak realm roles to ensure on the account.</param>
/// <param name="StudyCodes">Study codes to merge into the user's <c>study_ids</c> attribute.</param>
/// <param name="SiteCode">Optional site code to assign on the user's <c>site_id</c> attribute.</param>
/// <param name="KnownTenantPersonId">Existing <see cref="PersonId"/> already matched via <c>IdentityVault</c> blind index in this tenant, when present.</param>
public sealed record OidcProvisioningRequest(
    string TenantCode,
    string Email,
    string GivenName,
    string FamilyName,
    IReadOnlyList<string> RealmRoles,
    IReadOnlyList<string> StudyCodes,
    string? SiteCode,
    PersonId? KnownTenantPersonId = null);

/// <summary>Whether the OIDC user account was newly created or matched an existing account in the same tenant.</summary>
public enum OidcProvisioningOutcome
{
    /// <summary>A new OIDC identity was created in the realm.</summary>
    Created = 0,

    /// <summary>An existing OIDC identity in the same tenant was reused without duplication.</summary>
    ReusedExisting = 1,
}

/// <summary>
/// Pseudonymous result of OIDC user provisioning. The <see cref="PersonId"/> equals the OIDC subject UUID so
/// <c>Actor.Person(sub)</c> matches <c>Grant.Grantee</c> on first sign-in.
/// </summary>
/// <param name="PersonId">The OIDC user UUID bound to the kernel <see cref="PersonId"/>.</param>
/// <param name="Outcome">Whether the account was newly created or reused.</param>
/// <param name="WelcomeEmailSent">Whether the password-setup welcome email action was dispatched.</param>
public sealed record OidcProvisioningResult(
    PersonId PersonId,
    OidcProvisioningOutcome Outcome,
    bool WelcomeEmailSent);

/// <summary>
/// Raised when OIDC user provisioning fails or encounters a cross-tenant identity collision. Carries a registered
/// <see cref="KernelError"/> and never embeds direct personal identifiers in its message.
/// </summary>
public sealed class OidcProvisioningException : Exception
{
    /// <summary>Creates a provisioning exception for <paramref name="error"/>.</summary>
    public OidcProvisioningException(KernelError error, Exception? innerException = null)
        : base(error.Message, innerException)
    {
        Error = error;
    }

    /// <summary>The registered module error describing the failure.</summary>
    public KernelError Error { get; }
}

/// <summary>
/// Port for creating or reusing OIDC user accounts and dispatching welcome password-setup links without logging PII.
/// </summary>
public interface IOidcUserProvisioner
{
    /// <summary>Creates a new OIDC user in the tenant or reuses an existing user in the same tenant.</summary>
    Task<OidcProvisioningResult> ProvisionOrReuseAsync(OidcProvisioningRequest request, CancellationToken cancellationToken = default);
}
