using System;
using System.Collections.Generic;
using System.Linq;

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
    /// <summary>The verified principal the gateway scheme resolved for <paramref name="context"/>, or null.</summary>
    public static Principal? Principal(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal;
    }

    /// <summary>The caller's user id from the signed identity, or null when the request carries none.</summary>
    public static string? UserId(HttpContext context) =>
        Principal(context)?.UserId is { Length: > 0 } id ? id : null;

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
