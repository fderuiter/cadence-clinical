using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;

using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// The gateway-authenticated caller of a Payments route: the user id from the signed <c>sub</c> claim, the roles from
/// the signed role claims (#5361), and the studies and sites the gateway signed for them (ADR-2516, #5401). Payments
/// records who acted from this, never from a request body, and reaches only rows in the caller's studies and sites.
/// </summary>
/// <param name="UserId">The caller's user id.</param>
/// <param name="Roles">The caller's roles as the gateway signed them, normalized to canonical role names.</param>
/// <param name="Studies">The signed <c>study_ids</c>; <c>*</c> is every study, and none reaches no study.</param>
/// <param name="Sites">The signed <c>site_id</c> assignments.</param>
internal sealed record PaymentsCaller(string UserId, IReadOnlyList<string> Roles, IReadOnlyList<string> Studies, IReadOnlyList<string> Sites)
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

    private Principal AsPrincipal => new()
    {
        UserId = UserId,
        Roles = RoleNormalization.GetNormalizedRoles(Roles),
        RawRoles = Roles,
        AssignedStudies = Studies,
        AssignedSites = Sites,
    };

    /// <summary>True when the caller's roles hold <paramref name="permission"/> (<c>resource:action</c>) in the matrix.</summary>
    public bool Holds(string permission) => PermissionMatrix.HasPermission(AsPrincipal, permission);

    /// <summary>
    /// True when the caller may reach a row in <paramref name="studyId"/> at <paramref name="siteId"/>: they are assigned
    /// to the study, and, when they hold a site-scoped role or have site assignments, to the site. Fails closed.
    /// </summary>
    public bool Reaches(string? studyId, string? siteId)
    {
        var principal = AsPrincipal;
        return StudyAccessEvaluator.CanAccessStudy(principal, studyId) && SiteAccessEvaluator.CanAccessSite(principal, siteId);
    }

    /// <summary>True when the caller is assigned to <paramref name="studyId"/> (or to every study).</summary>
    public bool ReachesStudy(string? studyId) => StudyAccessEvaluator.CanAccessStudy(AsPrincipal, studyId);

    /// <summary>The studies a query is narrowed to, or <see langword="null"/> when the caller is assigned to every study.</summary>
    private IReadOnlyCollection<string>? StudyFilter =>
        Studies.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal))
            ? null
            : Studies.Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The sites a query is narrowed to, or <see langword="null"/> when the caller is not site-bound: as
    /// <see cref="SiteAccessEvaluator"/>, a site-scoped role or any site assignment binds the caller to their sites.
    /// </summary>
    private IReadOnlyCollection<string>? SiteFilter =>
        AsPrincipal.Roles.Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains) || Sites.Count > 0
            ? Sites.Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal)
            : null;

    /// <summary>True when the caller reaches every study and every site, so nothing needs narrowing.</summary>
    public bool IsUnrestricted => StudyFilter is null && SiteFilter is null;

    /// <summary>
    /// Narrows <paramref name="query"/> to the rows whose study (<paramref name="study"/>) and site
    /// (<paramref name="site"/>) the caller reaches. The filter runs in the database.
    /// </summary>
    public IQueryable<T> InScope<T>(IQueryable<T> query, Expression<Func<T, string>> study, Expression<Func<T, string>> site)
    {
        if (StudyFilter is { } studies)
        {
            query = query.Where(Contains(studies, study));
        }

        if (SiteFilter is { } sites)
        {
            query = query.Where(Contains(sites, site));
        }

        return query;
    }

    /// <summary>Narrows <paramref name="query"/> to rows in the caller's studies, for records that carry no site.</summary>
    public IQueryable<T> InStudyScope<T>(IQueryable<T> query, Expression<Func<T, string>> study) =>
        StudyFilter is { } studies ? query.Where(Contains(studies, study)) : query;

    private static Expression<Func<T, bool>> Contains<T>(IReadOnlyCollection<string> values, Expression<Func<T, string>> selector)
    {
        var list = values.ToList();
        // Read the list through a closure so EF Core sends it as a query parameter rather than inlining it.
        Expression<Func<List<string>>> captured = () => list;
        var call = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)], captured.Body, selector.Body);
        return Expression.Lambda<Func<T, bool>>(call, selector.Parameters);
    }

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

        var roles = Values(user, ClaimTypes.Role);
        return new PaymentsCaller(userId, roles, Values(user, CadenceClaimTypes.StudyIds), Values(user, CadenceClaimTypes.SiteId));
    }

    private static List<string> Values(ClaimsPrincipal user, string type) =>
        user.FindAll(type).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
}
