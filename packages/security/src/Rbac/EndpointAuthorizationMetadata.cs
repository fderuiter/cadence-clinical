using System;

namespace Cadence.Security.Rbac;

/// <summary>The named authorization policies <c>AddCadenceSecurity()</c> registers (ADR-2516).</summary>
public static class CadencePolicies
{
    /// <summary>Any signed-in caller; used only through <c>RequireSignedInCaller(reason)</c>.</summary>
    public const string SignedInCaller = "SignedInCaller";

    /// <summary>The signed caller is assigned to the study the route names; used through <c>RequireStudyScope</c>.</summary>
    public const string StudyScope = "RequireStudyScope";

    /// <summary>The signed caller may reach the site the route names; used through <c>RequireSiteScope</c>.</summary>
    public const string SiteScope = "RequireSiteScope";
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
