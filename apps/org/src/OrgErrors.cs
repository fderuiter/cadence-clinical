using Cadence.Kernel.Commands;

namespace Cadence.Modules.Org;

/// <summary>
/// Checked-in registry of Organization Administration module error codes (<c>M.Org.&lt;Area&gt;.&lt;Name&gt;</c>, K-ERR-1).
/// </summary>
public static class OrgErrors
{
    /// <summary>The invitation or delegation email address is missing or malformed.</summary>
    public static readonly KernelError InvitationEmailInvalid =
        new("M.Org.Invitation.EmailInvalid", 422, "A valid colleague email address is required.");

    /// <summary>The invitation requires a colleague name when onboarding a new identity.</summary>
    public static readonly KernelError InvitationNameRequired =
        new("M.Org.Invitation.NameRequired", 422, "Colleague full name is required to provision a new seat.");

    /// <summary>The requested role, duty, capability, or target scope is missing or unknown.</summary>
    public static readonly KernelError InvitationTargetInvalid =
        new("M.Org.Invitation.TargetInvalid", 422, "Specify a valid role, clinical duty, or capability and an existing target scope.");

    /// <summary>Unblinding capabilities cannot be bundled into role invitations or site delegations (K-AUTH-4).</summary>
    public static readonly KernelError UnblindingForbidden =
        new("M.Org.Authority.UnblindingForbidden", 422, "Unblinding capabilities cannot be issued through standard team invitations or delegations (K-AUTH-4).");

    /// <summary>The caller holds no active delegable grant covering the requested delegation.</summary>
    public static readonly KernelError NoDelegableGrant =
        new("M.Org.Authority.NoDelegableGrant", 403, "You do not hold an active delegable grant (WithGrantOption) covering this request.");

    /// <summary>The email address is already bound to a different tenant in the OIDC provider.</summary>
    public static readonly KernelError CrossTenantIdentityConflict =
        new("M.Org.Identity.CrossTenantConflict", 409, "That email address is already registered to a different tenant workspace.");

    /// <summary>The OIDC identity provider rejected or failed user provisioning.</summary>
    public static readonly KernelError OidcProvisioningFailed =
        new("M.Org.Identity.ProvisioningFailed", 502, "Could not provision the colleague account in the identity provider.");

    /// <summary>The host does not have IdentityVault configured to seal personal identifiers.</summary>
    public static readonly KernelError IdentityVaultUnavailable =
        new("M.Org.Identity.VaultUnavailable", 503, "IdentityVault is not configured on this host; personal identifiers cannot be sealed.");
}
