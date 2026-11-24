using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel.Explanations;
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
    public const string ReadOnlyDetail = "Forbidden: Auditor personas are restricted to read-only access.";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        string method = context.Request.Method;
        bool isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        if (isWrite && context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal principal
            && principal.Roles.Any(RoleNormalization.AUDITOR_ROLES.Contains))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "https://tools.ietf.org/html/rfc9110#section-15.5.4", // deid-ignore
                ["title"] = "Forbidden",
                ["status"] = StatusCodes.Status403Forbidden,
                ["detail"] = ReadOnlyDetail,
                ["code"] = "FORBIDDEN_AUDITOR_READ_ONLY",
                ["explanation"] = ProblemExplanation.From(RbacExplanationBuilder.ForAuditorReadOnly(ReadOnlyDetail)),
            };
            if (context.Request.Path.HasValue)
            {
                payload["instance"] = context.Request.Path.Value;
            }

            await JsonSerializer.SerializeAsync(context.Response.Body, payload, cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }
}
