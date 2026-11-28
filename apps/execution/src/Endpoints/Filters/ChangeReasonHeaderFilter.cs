using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Endpoints.Filters;

/// <summary>
/// Endpoint metadata marker indicating that <see cref="ChangeReasonHeaderFilter"/> is attached to a mutating route.
/// </summary>
public sealed class ChangeReasonRequired
{
    public static ChangeReasonRequired Instance { get; } = new();

    private ChangeReasonRequired()
    {
    }
}

/// <summary>
/// Route-builder extensions for enforcing 21 CFR Part 11 <c>X-Change-Reason</c> headers on mutating endpoints.
/// </summary>
public static class ChangeReasonEndpointExtensions
{
    public static RouteHandlerBuilder RequireChangeReasonHeader(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ChangeReasonHeaderFilter>()
            .WithMetadata(ChangeReasonRequired.Instance);
}

/// <summary>
/// Enforces the mandatory <c>X-Change-Reason</c> request header (21 CFR Part 11 change-justification string,
/// PRD-CRF-010) ahead of mutating Execution routes. Short-circuits with HTTP <c>400</c> before handler execution when
/// the header is absent, whitespace-only, shorter than <see cref="MinimumReasonLength"/> trimmed characters, or a
/// malformed RFC 8187 value (ADR-2495).
/// </summary>
public sealed class ChangeReasonHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-Change-Reason";
    public const int MinimumReasonLength = 10;

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var headerValue = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!ChangeReasonHeader.TryDecode(headerValue, out string? reason))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"{HeaderName} must be printable ASCII or RFC 8187 \"UTF-8''\" followed by percent-encoded UTF-8."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"{HeaderName} header is required."));
        }

        if (reason.Trim().Length < MinimumReasonLength)
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"{HeaderName} must be at least {MinimumReasonLength} characters."));
        }

        return next(context);
    }
}
