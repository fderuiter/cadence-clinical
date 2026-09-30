using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Security.Authentication;

/// <summary>
/// Requires a fresh e-signature step-up token (<c>X-Sig-Token</c>) on a signature-gated endpoint. The token
/// must have been issued to the authenticated caller for this exact action and semantic action, and is
/// consumed on use. Anything else is answered with 401 <c>REAUTHENTICATION_REQUIRED</c> before the handler
/// runs, so no write happens.
/// @req:PRD-SYS-053
/// </summary>
public sealed class ElectronicSignatureEndpointFilter(string semanticAction, Func<HttpContext, string> resolveAction) : IEndpointFilter
{
    /// <summary>The <see cref="HttpContext.Items"/> key holding the verified, consumed token.</summary>
    public const string VerifiedTokenItemKey = "Cadence.VerifiedSigToken";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        string? userId = httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value;
        string? secret = httpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>()
            .Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(secret))
        {
            return ReauthenticationRequired();
        }

        try
        {
            var token = httpContext.RequestServices.GetRequiredService<SigTokenVerifier>().VerifyAndConsumeSigToken(
                httpContext.Request.Headers[GatewayHeaders.SigToken].ToString(),
                userId,
                new SigTokenBinding(resolveAction(httpContext), semanticAction),
                GatewayIdentity.EncodeSecret(secret));
            httpContext.Items[VerifiedTokenItemKey] = token;
        }
        catch (UnauthorizedAccessException)
        {
            return ReauthenticationRequired();
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>The contract's 401 body (§3.1), as ProblemDetails.</summary>
    public static IResult ReauthenticationRequired() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Re-authentication required",
            detail: SigTokenVerifier.ReauthenticationRequired,
            extensions: new Dictionary<string, object?>
            {
                ["error"] = SigTokenVerifier.ReauthenticationRequired,
                ["message"] = "21 CFR Part 11 mandate: Re-authentication is required.",
            });
}

public static class ElectronicSignatureEndpointExtensions
{
    /// <summary>
    /// Gates the endpoint on an e-signature step-up token bound to <paramref name="semanticAction"/> and to the
    /// concrete action <paramref name="resolveAction"/> computes from the request (normally the canonical path
    /// of the target resource, so a token for record A cannot sign record B).
    /// </summary>
    public static RouteHandlerBuilder RequireElectronicSignature(
        this RouteHandlerBuilder builder,
        string semanticAction,
        Func<HttpContext, string> resolveAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticAction);
        ArgumentNullException.ThrowIfNull(resolveAction);

        return builder
            .AddEndpointFilter(new ElectronicSignatureEndpointFilter(semanticAction, resolveAction))
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
