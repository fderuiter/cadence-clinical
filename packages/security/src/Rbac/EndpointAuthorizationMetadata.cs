using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Security.Rbac;

/// <summary>The named authorization policies <c>AddCadenceSecurity()</c> registers (ADR-2516).</summary>
public static class CadencePolicies
{
    /// <summary>Any signed-in caller; used only through <c>RequireSignedInCaller(reason)</c>.</summary>
    public const string SignedInCaller = "SignedInCaller";

    /// <summary>The signed caller carries an active verified tenant (<c>X-Tenant-Id</c>); used through <c>RequireTenantScope</c>.</summary>
    public const string TenantScope = "RequireTenantScope";

    /// <summary>The signed caller is assigned to the study the route names; used through <c>RequireStudyScope</c>.</summary>
    public const string StudyScope = "RequireStudyScope";

    /// <summary>The signed caller may reach the site the route names; used through <c>RequireSiteScope</c>.</summary>
    public const string SiteScope = "RequireSiteScope";

    /// <summary>The signed caller carries a verified tenant and at least one assigned study; handler enforces per-object/body study scope.</summary>
    public const string CallerStudyScope = "RequireCallerStudyScope";

    /// <summary>The signed caller carries a verified tenant, at least one assigned study, and (when site-scoped) at least one assigned site.</summary>
    public const string CallerStudyAndSiteScope = "RequireCallerStudyAndSiteScope";
}

/// <summary>Kinds of explicit endpoint scope declarations required on standalone clinical service routes.</summary>
public enum EndpointScopeKind
{
    Tenant,
    RouteStudy,
    RouteSite,
    CallerStudy,
    CallerStudyAndSite,
}

/// <summary>Explicit scope declaration attached to every standalone clinical route.</summary>
public sealed record EndpointScopeMetadata(EndpointScopeKind Kind, string? RouteParameter = null);

/// <summary>Verifies that a signed tenant code resolves to an active tenant in the backing store.</summary>
public interface ITenantAccessVerifier
{
    ValueTask<bool> IsActiveTenantAsync(string tenantCode, CancellationToken cancellationToken = default);
}

/// <summary>
/// Endpoint metadata recording why a route admits any signed-in caller (<c>RequireSignedInCaller</c>): its handler
/// decides access per object, or it reads or computes nothing a signed-in caller may not see.
/// </summary>
public sealed record SignedInCallerReason(string Reason);

/// <summary>Endpoint metadata naming the route value that holds the study id for <c>RequireStudyScope</c>.</summary>
public sealed record StudyScopeRouteParameter(string Name);

/// <summary>Endpoint metadata naming the route value that holds the site id for <c>RequireSiteScope</c>.</summary>
public sealed record SiteScopeRouteParameter(string Name);

/// <summary>Explicit request header metadata projected into OpenAPI parameter contracts.</summary>
public sealed record EndpointHeaderMetadata(string Name, bool Required, string Description, int? MaxLength = null);

