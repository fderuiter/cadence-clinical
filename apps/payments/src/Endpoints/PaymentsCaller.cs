using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// The gateway-authenticated caller of a Payments route: the user id from the signed <c>sub</c> claim and the roles
/// from the signed role claims (#5361). Payments records who acted from this, never from a request body.
/// </summary>
/// <param name="UserId">The caller's user id.</param>
/// <param name="Roles">The caller's roles as the gateway signed them, normalized to canonical role names.</param>
internal sealed record PaymentsCaller(string UserId, IReadOnlyList<string> Roles)
{
    /// <summary>
    /// The identity recorded for rows the service writes on its own, such as the demonstration stipend rules seeded on
    /// first read.
    /// </summary>
    public const string SeedSystemUser = "svc:payments";

    /// <summary>
    /// The identity recorded for the expense auto-approval policy, which approves and pays a low-risk claim with no
    /// person reviewing it.
    /// </summary>
    public const string AutoApprovalSystemUser = "svc:system_auto_approval";

    // PaymentAuditLog.UserRoles holds at most 255 characters.
    private const int MaxRolesLength = 255;

    /// <summary>The caller's canonical roles as one value for the audit record, comma-separated.</summary>
    public string RolesText
    {
        get
        {
            string joined = string.Join(",", Roles);
            return joined.Length <= MaxRolesLength ? joined : joined[..MaxRolesLength];
        }
    }

    /// <summary>True when the caller's roles hold <paramref name="permission"/> (<c>resource:action</c>) in the matrix.</summary>
    public bool Holds(string permission) =>
        PermissionMatrix.HasPermission(
            new Principal { UserId = UserId, Roles = RoleNormalization.GetNormalizedRoles(Roles), RawRoles = Roles },
            permission);

    /// <summary>
    /// Reads the caller off the request's verified claims, or <see langword="null"/> when the request carries no
    /// authenticated subject.
    /// </summary>
    public static PaymentsCaller? From(ClaimsPrincipal user)
    {
        string? userId = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        return new PaymentsCaller(userId, roles);
    }
}
