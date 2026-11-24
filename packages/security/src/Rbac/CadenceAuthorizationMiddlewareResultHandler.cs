using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Kernel.Explanations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Rbac;

/// <summary>
/// Default <see cref="IAuthorizationMiddlewareResultHandler"/> for Cadence hosts (ADR-2482, PRD-SYS-053): passes
/// succeeded and 401 challenge results to ASP.NET Core's default handler, and answers HTTP 403 policy denials with
/// RFC 9457 <c>application/problem+json</c> carrying a structured <c>explanation</c> payload.
/// </summary>
public sealed class CadenceAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Succeeded || authorizeResult.Challenged || !authorizeResult.Forbidden)
        {
            await _defaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        (Explanation explanation, string errorCode, string detail) =
            RbacExplanationBuilder.ForPolicyDenial(context, policy, authorizeResult);

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = errorCode,
            ["explanation"] = ProblemExplanation.From(explanation),
        };

        var result = Results.Problem(
            title: "Forbidden",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            instance: context.Request.Path.HasValue ? context.Request.Path.Value : null,
            type: "https://tools.ietf.org/html/rfc9110#section-15.5.4", // deid-ignore
            extensions: extensions);

        await result.ExecuteAsync(context).ConfigureAwait(false);
    }
}
