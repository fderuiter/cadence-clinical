using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Security.Rbac;

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
    }
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        Principal? principal = ResolvePrincipal(context);
        if (principal is not null
            && PermissionMatrix.HasPermission(
                FilterEffectivePrincipalForPermission(principal, requirement.Permission),
                requirement.Permission))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail();
        return Task.CompletedTask;
    }

    private static Principal FilterEffectivePrincipalForPermission(Principal principal, string permission)
    {
        if (ClinicalScopeGuard.IsVerifiedTrialTenant(principal.TenantId) || IsReadPermission(permission))
        {
            return principal;
        }

        bool holdsTrialCoordinator =
            principal.Roles.Any(IsTrialCoordinatorRole) || principal.RawRoles.Any(IsTrialCoordinatorRole);
        if (!holdsTrialCoordinator)
        {
            return principal;
        }

        var filteredRawRoles = principal.RawRoles
            .Where(role => !IsTrialCoordinatorRole(role))
            .ToArray();
        var filteredRoles = principal.Roles
            .Where(role => !IsTrialCoordinatorRole(role))
            .ToArray();

        return new Principal
        {
            UserId = principal.UserId,
            TenantId = principal.TenantId,
            Roles = filteredRoles,
            RawRoles = filteredRawRoles,
            AssignedStudies = principal.AssignedStudies,
            AssignedSites = principal.AssignedSites,
            SponsorId = principal.SponsorId,
            UnblindedAccess = principal.UnblindedAccess,
        };
    }

    private static bool IsTrialCoordinatorRole(string role) =>
        string.Equals(
            RoleNormalization.NormalizeRole(role),
            RoleNormalization.ROLE_TRIAL_COORDINATOR,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsReadPermission(string permission)
    {
        int separatorIndex = permission.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == permission.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> action = permission.AsSpan(separatorIndex + 1).Trim();
        return action.Equals("read", StringComparison.OrdinalIgnoreCase)
            || action.StartsWith("read_", StringComparison.OrdinalIgnoreCase);
    }

    internal static Principal? ResolvePrincipal(AuthorizationHandlerContext context)
    {
        if (context.Resource is HttpContext httpContext && ClinicalScopeGuard.ResolvePrincipal(httpContext) is { } resolved)
        {
            return resolved;
        }

        if (context.User.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        var roles = context.User.Claims
            .Where(c => c.Type == ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .ToList();
        var studies = context.User.FindAll(CadenceClaimTypes.StudyIds)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        var sites = context.User.FindAll(CadenceClaimTypes.SiteId)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        string? tenantId = context.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;

        return new Principal
        {
            UserId = context.User.FindFirst(CadenceClaimTypes.Subject)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.Identity.Name
                ?? string.Empty,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            Roles = RoleNormalization.GetNormalizedRoles(roles),
            RawRoles = roles,
            AssignedStudies = studies,
            AssignedSites = sites,
        };
    }
}

public class RoleRequirement : IAuthorizationRequirement
{
    public IReadOnlyList<string> RequiredRoles { get; }

    public RoleRequirement(params string[] requiredRoles)
    {
        RequiredRoles = requiredRoles?.Select(RoleNormalization.NormalizeRole).ToList() ?? [];
    }
}

public class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        Principal? principal = PermissionAuthorizationHandler.ResolvePrincipal(context);
        if (principal != null && requirement.RequiredRoles.Any(reqRole => principal.Roles.Contains(reqRole, StringComparer.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The fallback policy's requirement (ADR-2516): an endpoint that declares no authorization of its own is denied to
/// every signed caller. Health, status and development API documents opt out with <c>AllowAnonymous()</c>.
/// </summary>
public sealed class UndeclaredEndpointRequirement : IAuthorizationRequirement;

/// <summary>Never satisfies <see cref="UndeclaredEndpointRequirement"/>, so an undeclared endpoint answers 403.</summary>
public sealed class UndeclaredEndpointAuthorizationHandler : AuthorizationHandler<UndeclaredEndpointRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, UndeclaredEndpointRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        // A request that matched no route reaches no operation; after authentication it gets the routing 404.
        if (context.Resource is HttpContext { } httpContext && httpContext.GetEndpoint() is null)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail(new AuthorizationFailureReason(this, "The endpoint declares no authorization, so it is denied by default (ADR-2516)."));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Requires a verified, active <c>X-Tenant-Id</c> on the signed principal (ADR-2514, PRD-CRF-013).
/// Fails closed when the header/claim is missing or when <see cref="ITenantAccessVerifier"/> reports the tenant inactive.
/// </summary>
public sealed class TenantScopeRequirement : IAuthorizationRequirement;

public sealed class TenantScopeAuthorizationHandler : AuthorizationHandler<TenantScopeRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        HttpContext? httpContext = context.Resource as HttpContext;
        Principal? principal = PermissionAuthorizationHandler.ResolvePrincipal(context);
        string? tenantCode = principal?.TenantId ?? (httpContext is not null ? CallerScope.TenantId(httpContext) : null);
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            context.Fail(new AuthorizationFailureReason(this, ClinicalScopeGuard.TenantAccessDeniedDetail));
            return;
        }

        ITenantAccessVerifier? verifier = httpContext?.RequestServices?.GetService<ITenantAccessVerifier>();
        if (verifier is not null && !await verifier.IsActiveTenantAsync(tenantCode, httpContext!.RequestAborted).ConfigureAwait(false))
        {
            context.Fail(new AuthorizationFailureReason(this, ClinicalScopeGuard.TenantAccessDeniedDetail));
            return;
        }

        context.Succeed(requirement);
    }
}

/// <summary>
/// Requires the signed caller to hold at least one study assignment (<c>X-Study-Ids</c>); per-object/body study checks
/// run in the endpoint handler via <see cref="ClinicalScopeGuard"/>.
/// </summary>
public sealed class CallerStudyScopeRequirement : IAuthorizationRequirement;

public sealed class CallerStudyScopeAuthorizationHandler : AuthorizationHandler<CallerStudyScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CallerStudyScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        Principal? principal = PermissionAuthorizationHandler.ResolvePrincipal(context);
        if (ClinicalScopeGuard.HasAnyStudyAssignment(principal))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail(new AuthorizationFailureReason(this, ClinicalScopeGuard.StudyAccessDeniedDetail));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Requires the signed caller to hold at least one study assignment and, when site-scoped, at least one site assignment.
/// </summary>
public sealed class CallerStudyAndSiteScopeRequirement : IAuthorizationRequirement;

public sealed class CallerStudyAndSiteScopeAuthorizationHandler : AuthorizationHandler<CallerStudyAndSiteScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CallerStudyAndSiteScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        Principal? principal = PermissionAuthorizationHandler.ResolvePrincipal(context);
        if (!ClinicalScopeGuard.HasAnyStudyAssignment(principal))
        {
            context.Fail(new AuthorizationFailureReason(this, ClinicalScopeGuard.StudyAccessDeniedDetail));
            return Task.CompletedTask;
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(principal))
        {
            context.Fail(new AuthorizationFailureReason(this, ClinicalScopeGuard.SiteAccessDeniedDetail));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public class StudyScopeRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Admits a caller to a route that names a study only when the signed principal is assigned to that study (ADR-2516).
/// The study id comes from the route value the endpoint names with <see cref="StudyScopeRouteParameter"/>, never from
/// the query, the body or an unsigned header. It fails closed: no route value, no principal or no assignment is a
/// denial.
/// </summary>
public class StudyScopeAuthorizationHandler : AuthorizationHandler<StudyScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, StudyScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.Resource is HttpContext httpContext
            && ClinicalScopeGuard.ResolvePrincipal(httpContext) is { } principal
            && RouteValue<StudyScopeRouteParameter>(httpContext, p => p.Name) is { } studyId
            && StudyAccessEvaluator.CanAccessStudy(principal, studyId))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail(new AuthorizationFailureReason(this, "The caller is not assigned to the study this route names."));
        return Task.CompletedTask;
    }

    internal static string? RouteValue<TMetadata>(HttpContext httpContext, Func<TMetadata, string> name)
        where TMetadata : class
    {
        var parameter = httpContext.GetEndpoint()?.Metadata.GetMetadata<TMetadata>();
        if (parameter is null)
        {
            return null;
        }

        string? value = httpContext.GetRouteValue(name(parameter))?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

public sealed class SiteScopeRequirement : IAuthorizationRequirement;

/// <summary>
/// Admits a caller to a route that names a site only when <see cref="SiteAccessEvaluator.CanAccessSite"/> allows it for
/// the signed principal and the caller holds at least one study assignment (ADR-2516). The site id comes from the route
/// value the endpoint names with <see cref="SiteScopeRouteParameter"/>; a route without it is a denial.
/// </summary>
public sealed class SiteScopeAuthorizationHandler : AuthorizationHandler<SiteScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SiteScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.Resource is HttpContext httpContext
            && ClinicalScopeGuard.ResolvePrincipal(httpContext) is { } principal
            && ClinicalScopeGuard.HasAnyStudyAssignment(principal)
            && StudyScopeAuthorizationHandler.RouteValue<SiteScopeRouteParameter>(httpContext, p => p.Name) is { } siteId
            && SiteAccessEvaluator.CanAccessSite(principal, siteId))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail(new AuthorizationFailureReason(this, "The caller may not reach the site this route names."));
        return Task.CompletedTask;
    }
}

public class ChangeJustificationRequirement : IAuthorizationRequirement
{
}

public class ChangeJustificationAuthorizationHandler : AuthorizationHandler<ChangeJustificationRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ChangeJustificationRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.Resource is HttpContext httpContext)
        {
            string method = httpContext.Request.Method;
            bool isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

            if (!isWrite)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
            if (principal != null && !string.IsNullOrWhiteSpace(principal.ChangeReason))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        context.Fail();
        return Task.CompletedTask;
    }
}

public class AuditorReadOnlyRequirement : IAuthorizationRequirement
{
}

public class AuditorReadOnlyAuthorizationHandler : AuthorizationHandler<AuditorReadOnlyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuditorReadOnlyRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.Resource is HttpContext httpContext)
        {
            var principal = ClinicalScopeGuard.ResolvePrincipal(httpContext);
            if (principal != null)
            {
                bool isAuditor = principal.Roles.Any(r => RoleNormalization.AUDITOR_ROLES.Contains(r));
                string method = httpContext.Request.Method;
                bool isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

                if (isAuditor && isWrite)
                {
                    context.Fail();
                    return Task.CompletedTask;
                }
            }

            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
