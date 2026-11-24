using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Rbac;

/// <summary>
/// The signed caller of a request, for handlers that check scope on data the route does not name: a study id in a body
/// or on a stored record, or a list to narrow to the caller's studies (ADR-2516). Every answer fails closed: a request
/// without a verified principal reaches no study.
/// </summary>
public static class CallerScope
{
    public const string DefaultMissingCallerMessage = "An admitted request has no signed caller.";

    /// <summary>
    /// The verified principal the gateway scheme resolved for <paramref name="context"/>, falling back to authenticated
    /// <see cref="ClaimsPrincipal"/> <see cref="CadenceClaimTypes.Subject"/> claims when invoked outside the gateway handler, or null.
    /// </summary>
    public static Principal? Principal(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal gatewayPrincipal)
        {
            return gatewayPrincipal;
        }

        if (context.User.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        string? subject = context.User.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var rawRoles = context.User.Claims
            .Where(c => c.Type == ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        var studies = context.User.FindAll(CadenceClaimTypes.StudyIds)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        var sites = context.User.FindAll(CadenceClaimTypes.SiteId)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToArray();

        string? tenantId = context.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;

        return new Principal
        {
            UserId = subject,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            Roles = RoleNormalization.GetNormalizedRoles(rawRoles),
            RawRoles = rawRoles,
            AssignedStudies = studies,
            AssignedSites = sites,
            SponsorId = context.User.FindFirst(CadenceClaimTypes.SponsorId)?.Value,
            UnblindedAccess = string.Equals(
                context.User.FindFirst(CadenceClaimTypes.UnblindedAccess)?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>The caller's user id from the signed identity, or null when the request carries none.</summary>
    public static string? UserId(HttpContext context) =>
        Principal(context)?.UserId is { Length: > 0 } id && !string.IsNullOrWhiteSpace(id) ? id : null;

    /// <summary>Null-safe verified caller user id for <paramref name="context"/>, or null when unauthenticated.</summary>
    public static string? VerifiedUserId(HttpContext? context) =>
        context is null ? null : UserId(context);

    /// <summary>Returns the verified <see cref="Rbac.Principal"/> for an admitted route or throws <see cref="InvalidOperationException"/>.</summary>
    public static Principal RequiredPrincipal(
        HttpContext context,
        string missingMessage = DefaultMissingCallerMessage) =>
        Principal(context) ?? throw new InvalidOperationException(missingMessage);

    /// <summary>Returns the verified caller user id for an admitted route or throws <see cref="InvalidOperationException"/>.</summary>
    public static string RequiredUserId(
        HttpContext context,
        string missingMessage = DefaultMissingCallerMessage) =>
        UserId(context) ?? throw new InvalidOperationException(missingMessage);

    /// <summary>The caller's verified tenant code from the signed identity, or null when the request carries none.</summary>
    public static string? TenantId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Principal(context)?.TenantId is { Length: > 0 } tenantId && !string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId.Trim();
        }

        string? claimTenant = context.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        return string.IsNullOrWhiteSpace(claimTenant) ? null : claimTenant.Trim();
    }

    /// <summary>The caller's normalized roles, comma-joined, for an audit record.</summary>
    public static string Roles(HttpContext context) =>
        string.Join(',', Principal(context)?.Roles ?? []);

    /// <summary>The caller's normalized roles, comma-joined and truncated to <paramref name="maxLength"/> characters.</summary>
    public static string Roles(HttpContext context, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        string joined = Roles(context);
        return joined.Length <= maxLength ? joined : joined[..maxLength];
    }

    /// <summary>
    /// Resolves the audit actor <c>(User, Role)</c> for <paramref name="context"/>, falling back to
    /// <see cref="System.Security.Principal.IIdentity.Name"/> or <paramref name="defaultUser"/> and <paramref name="defaultRole"/>.
    /// </summary>
    public static (string User, string Role) ResolveActor(
        HttpContext context,
        string defaultUser = "system",
        string defaultRole = "SYSTEM")
    {
        ArgumentNullException.ThrowIfNull(context);
        string user = UserId(context)
            ?? (context.User.Identity?.Name is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) ? name : defaultUser);
        string roles = Roles(context);
        return (user, string.IsNullOrWhiteSpace(roles) ? defaultRole : roles);
    }

    /// <summary>True when the caller is assigned to <paramref name="studyId"/> (see <see cref="StudyAccessEvaluator"/>).</summary>
    public static bool CanAccessStudy(HttpContext context, string? studyId) =>
        StudyAccessEvaluator.CanAccessStudy(Principal(context), studyId);

    /// <summary>True when the caller may reach <paramref name="siteId"/> (see <see cref="SiteAccessEvaluator"/>).</summary>
    public static bool CanAccessSite(HttpContext context, string? siteId) =>
        SiteAccessEvaluator.CanAccessSite(Principal(context), siteId);

    /// <summary>
    /// The studies a list should be narrowed to: null when the caller is assigned to every study (<c>*</c>), otherwise
    /// the assigned study ids, which is empty for a caller with no assignment.
    /// </summary>
    public static IReadOnlyCollection<string>? StudyFilter(HttpContext context)
    {
        var assigned = Principal(context)?.AssignedStudies ?? [];
        if (assigned.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
        {
            return null;
        }

        return assigned.Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
    }
}
