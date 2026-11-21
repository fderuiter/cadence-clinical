using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Tickets.Endpoints.Filters;

/// <summary>
/// Rejects mutating ticket requests whose <c>X-Change-Reason</c> header is missing, blank, or malformed (ADR-2495).
/// Runs as an endpoint filter after authorization so route-authorization probes can verify RBAC policies cleanly.
/// </summary>
public sealed class ChangeReasonHeaderFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string headerValue = context.HttpContext.Request.Headers[GatewayHeaders.ChangeReason].ToString();
        if (!ChangeReasonHeader.TryDecode(headerValue, out string? reason))
        {
            return Results.Problem(
                title: "Malformed X-Change-Reason header",
                detail: $"The '{GatewayHeaders.ChangeReason}' header is not valid RFC 8187 UTF-8 percent-encoding.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Results.Problem(
                title: "Missing X-Change-Reason header",
                detail: $"Write operations require a non-empty '{GatewayHeaders.ChangeReason}' header for the 21 CFR Part 11 audit trail.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context).ConfigureAwait(false);
    }
}
