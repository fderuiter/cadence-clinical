using System.Linq;
using System.Threading.Tasks;

using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Authentication;

/// <summary>
/// Rejects any write (POST, PUT, PATCH, DELETE) from an authenticated auditor persona with 403. Runs after
/// authentication, so the roles it inspects are the signed ones, never raw request headers.
/// @req:PRD-SYS-053
/// </summary>
public sealed class AuditorWriteGuardMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        string method = context.Request.Method;
        bool isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        if (isWrite && context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal principal
            && principal.Roles.Any(RoleNormalization.AUDITOR_ROLES.Contains))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: Auditor personas are restricted to read-only access.", context.RequestAborted);
            return;
        }

        await next(context);
    }
}
