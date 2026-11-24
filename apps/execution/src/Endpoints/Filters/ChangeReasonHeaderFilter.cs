using System;
using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Endpoints.Filters;

/// <summary>
/// Marks a stateless validation, conversion, or dry-run endpoint that performs no persistent mutation and therefore
/// opts out of a route group's default <see cref="ChangeReasonHeaderFilter"/>.
/// </summary>
public sealed class AllowWithoutChangeReasonMetadata
{
    public static AllowWithoutChangeReasonMetadata Instance { get; } = new();

    private AllowWithoutChangeReasonMetadata()
    {
    }
}

public static class ChangeReasonEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Explicitly opts a non-mutating helper route out of a route group's <see cref="ChangeReasonHeaderFilter"/>.
    /// </summary>
    public static TBuilder AllowWithoutChangeReason<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(AllowWithoutChangeReasonMetadata.Instance);
    }
}

/// <summary>
/// Enforces the mandatory <c>X-Change-Reason</c> request header (21 CFR Part 11 change-justification
/// string) ahead of mutating Execution routes. Safe read verbs (<c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c>) and
/// stateless helper routes marked with <see cref="AllowWithoutChangeReasonMetadata"/> pass through; every other
/// route short-circuits with a <c>400</c> problem response when the header is absent, whitespace-only, or a
/// malformed RFC 8187 value (ADR-2495).
/// </summary>
public sealed class ChangeReasonHeaderFilter : IEndpointFilter
{
    private const string HeaderName = "X-Change-Reason";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        string method = httpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return next(context);
        }

        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<AllowWithoutChangeReasonMetadata>() is not null)
        {
            return next(context);
        }

        var headerValue = httpContext.Request.Headers[HeaderName].ToString();
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

        return next(context);
    }
}
