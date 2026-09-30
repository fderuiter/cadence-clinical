using System;
using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Cadence.Security.Rbac;

/// <summary>
/// Builds the <c>Permission:&lt;resource:action&gt;</c> policies that
/// <see cref="DependencyInjection.CadenceSecurityServiceCollectionExtensions.RequirePermission{TBuilder}"/> names: an
/// authenticated gateway caller whose roles hold that permission in <see cref="PermissionMatrix"/>. Every other policy
/// name, and the default and fallback policies, come from the registered <see cref="AuthorizationOptions"/>
/// (ADR-2510).
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    /// <summary>The prefix of a permission policy name.</summary>
    public const string PolicyPrefix = "Permission:";

    /// <summary>The policy name for <paramref name="permission"/> (<c>resource:action</c>).</summary>
    public static string PolicyName(string permission) => PolicyPrefix + permission;

    /// <inheritdoc />
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        var registered = await base.GetPolicyAsync(policyName).ConfigureAwait(false);
        if (registered is not null || !policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return registered;
        }

        string permission = policyName[PolicyPrefix.Length..];
        int colon = permission.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == permission.Length - 1)
        {
            // A malformed name fails at startup or on first use rather than silently allowing everyone.
            throw new InvalidOperationException($"Permission policy '{policyName}' must name a permission as 'resource:action'.");
        }

        return new AuthorizationPolicyBuilder(GatewayAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}
