using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

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
        else if (context.User.Identity?.IsAuthenticated ?? false)
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

        if (principal == null && (context.User.Identity?.IsAuthenticated ?? false))
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

public class StudyScopeRequirement : IAuthorizationRequirement { }

public class StudyScopeAuthorizationHandler : AuthorizationHandler<StudyScopeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StudyScopeRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext)
        {
            var principal = httpContext.Items["Principal"] as Principal;
            if (principal == null)
            {
                context.Fail();
                return;
            }

            string? studyId = httpContext.GetRouteValue("study_id")?.ToString()
                ?? httpContext.GetRouteValue("studyId")?.ToString()
                ?? httpContext.Request.Query["study_id"].FirstOrDefault()
                ?? httpContext.Request.Query["studyId"].FirstOrDefault()
                ?? httpContext.Request.Headers["X-Study-Id"].FirstOrDefault()
                ?? httpContext.Request.Headers["x-study-id"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(studyId) && httpContext.Request.HasJsonContentType())
            {
                try
                {
                    httpContext.Request.EnableBuffering();
                    using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
                    string body = await reader.ReadToEndAsync();
                    httpContext.Request.Body.Position = 0;

                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            (doc.RootElement.TryGetProperty("study_id", out var p) || doc.RootElement.TryGetProperty("studyId", out p)))
                        {
                            studyId = p.GetString();
                        }
                    }
                }
                catch
                {
                    // Ignore JSON parse error during scope resolution
                }
            }

            if (string.IsNullOrWhiteSpace(studyId) || StudyAccessEvaluator.CanAccessStudy(principal, studyId))
            {
                context.Succeed(requirement);
                return;
            }
        }

        context.Fail();
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
