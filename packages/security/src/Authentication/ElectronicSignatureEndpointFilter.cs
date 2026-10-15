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
/// @req:SRS-SYS-005
/// </summary>
public sealed class ElectronicSignatureEndpointFilter(Func<EndpointFilterInvocationContext, SigTokenBinding?> resolveBinding) : IEndpointFilter
{
    /// <summary>A filter binding the token to <paramref name="semanticAction"/> on the action the request names.</summary>
    public ElectronicSignatureEndpointFilter(string semanticAction, Func<HttpContext, string> resolveAction)
        : this(context => new SigTokenBinding(resolveAction(context.HttpContext), semanticAction))
    {
    }

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
        // A binding the request cannot produce (for example a batch body that does not parse) signs nothing.
        var binding = resolveBinding(context);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(secret) || binding is null)
        {
            return ReauthenticationRequired();
        }

        try
        {
            var token = await httpContext.RequestServices.GetRequiredService<SigTokenVerifier>().VerifyAndConsumeAsync(
                httpContext.Request.Headers[GatewayHeaders.SigToken].ToString(),
                userId,
                binding,
                GatewayIdentity.EncodeSecret(secret),
                httpContext.RequestAborted).ConfigureAwait(false);
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

    /// <summary>
    /// Gates the endpoint on an e-signature step-up token bound to the <see cref="SigTokenBinding"/>
    /// <paramref name="resolveBinding"/> computes from the bound arguments, for endpoints whose binding depends on the
    /// request body (a batch id over the signed targets). A null binding is refused.
    /// </summary>
    public static RouteHandlerBuilder RequireElectronicSignature(
        this RouteHandlerBuilder builder,
        Func<EndpointFilterInvocationContext, SigTokenBinding?> resolveBinding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resolveBinding);

        return builder
            .AddEndpointFilter(new ElectronicSignatureEndpointFilter(resolveBinding))
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    /// <summary>The token the filter verified and consumed for this request, or null when no filter ran.</summary>
    public static System.IdentityModel.Tokens.Jwt.JwtSecurityToken? VerifiedSigToken(this HttpContext httpContext) =>
        httpContext?.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;
}
