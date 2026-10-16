using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
        if (context.Resource is HttpContext httpContext)
        {
            var principal = httpContext.Items["Principal"] as Principal;
            if (principal != null && PermissionMatrix.HasPermission(principal, requirement.Permission))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }
        else if (context.User.Identity is { IsAuthenticated: true })
        {
            // Fallback from ClaimsPrincipal if HttpContext is not direct resource
            var roles = context.User.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value).ToList();
            var principal = new Principal
            {
                UserId = context.User.Identity.Name ?? "",
                Roles = RoleNormalization.GetNormalizedRoles(roles),
                RawRoles = roles
            };
            if (PermissionMatrix.HasPermission(principal, requirement.Permission))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        context.Fail();
        return Task.CompletedTask;
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
        Principal? principal = null;
        if (context.Resource is HttpContext httpContext)
        {
            principal = httpContext.Items["Principal"] as Principal;
        }

        if (principal == null && context.User.Identity is { IsAuthenticated: true })
        {
            var roles = context.User.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value).ToList();
            principal = new Principal
            {
                UserId = context.User.Identity.Name ?? "",
                Roles = RoleNormalization.GetNormalizedRoles(roles),
                RawRoles = roles
            };
        }

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
        context.Fail(new AuthorizationFailureReason(this, "The endpoint declares no authorization, so it is denied by default (ADR-2516)."));
        return Task.CompletedTask;
    }
}

public class StudyScopeRequirement : IAuthorizationRequirement { }

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
        if (context.Resource is HttpContext httpContext
            && httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal principal
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
/// the signed principal (ADR-2516). The site id comes from the route value the endpoint names with
/// <see cref="SiteScopeRouteParameter"/>; a route without it is a denial.
/// </summary>
public sealed class SiteScopeAuthorizationHandler : AuthorizationHandler<SiteScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SiteScopeRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext
            && httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal principal
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

public class ChangeJustificationRequirement : IAuthorizationRequirement { }

public class ChangeJustificationAuthorizationHandler : AuthorizationHandler<ChangeJustificationRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ChangeJustificationRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext)
        {
            string method = httpContext.Request.Method;
            bool isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

            if (!isWrite)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            var principal = httpContext.Items["Principal"] as Principal;
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

public class AuditorReadOnlyRequirement : IAuthorizationRequirement { }

public class AuditorReadOnlyAuthorizationHandler : AuthorizationHandler<AuditorReadOnlyRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuditorReadOnlyRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext)
        {
            var principal = httpContext.Items["Principal"] as Principal;
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
