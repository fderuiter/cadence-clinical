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
public sealed class ElectronicSignatureEndpointFilter(
    Func<EndpointFilterInvocationContext, SigTokenBinding?> resolveBinding,
    Func<EndpointFilterInvocationContext, bool>? requiresSignature = null) : IEndpointFilter
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

        if (requiresSignature is not null && !requiresSignature(context))
        {
            return await next(context).ConfigureAwait(false);
        }

        var httpContext = context.HttpContext;
        string? userId = httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value;
        byte[] secretBytes = httpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>()
            .Get(GatewayAuthenticationHandler.SchemeName)
            .GetGatewaySecretBytes();
        // A binding the request cannot produce (for example a batch body that does not parse) signs nothing.
        var binding = resolveBinding(context);
        if (string.IsNullOrWhiteSpace(userId) || secretBytes.Length == 0 || binding is null)
        {
            return ReauthenticationRequired();
        }

        try
        {
            var token = await httpContext.RequestServices.GetRequiredService<SigTokenVerifier>().VerifyAndConsumeAsync(
                httpContext.Request.Headers[GatewayHeaders.SigToken].ToString(),
                userId,
                binding,
                secretBytes,
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

/// <summary>Endpoint metadata marking a route gated by <c>RequireElectronicSignature</c>.</summary>
public sealed class ElectronicSignatureRequired
{
    /// <summary>The shared instance.</summary>
    public static ElectronicSignatureRequired Instance { get; } = new();

    private ElectronicSignatureRequired()
    {
    }
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
            .WithMetadata(ElectronicSignatureRequired.Instance)
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
            .WithMetadata(ElectronicSignatureRequired.Instance)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Gates the endpoint on an e-signature step-up token whenever <paramref name="requiresSignature"/> returns
    /// <see langword="true"/> for the bound request (for example, irreversible <c>UNBLINDED</c> and <c>WITHDRAWN</c>
    /// participant transitions on a shared state-transition route), while allowing routine invocations through.
    /// </summary>
    public static RouteHandlerBuilder RequireElectronicSignatureWhen(
        this RouteHandlerBuilder builder,
        Func<EndpointFilterInvocationContext, bool> requiresSignature,
        Func<EndpointFilterInvocationContext, SigTokenBinding?> resolveBinding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(requiresSignature);
        ArgumentNullException.ThrowIfNull(resolveBinding);

        return builder
            .AddEndpointFilter(new ElectronicSignatureEndpointFilter(resolveBinding, requiresSignature))
            .WithMetadata(ElectronicSignatureRequired.Instance)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Gates the endpoint on an e-signature step-up token whenever <paramref name="requiresSignature"/> returns
    /// <see langword="true"/>, binding the token to the semantic action and concrete resource path computed from
    /// <see cref="HttpContext"/>.
    /// </summary>
    public static RouteHandlerBuilder RequireElectronicSignatureWhen(
        this RouteHandlerBuilder builder,
        Func<EndpointFilterInvocationContext, bool> requiresSignature,
        Func<HttpContext, string> resolveSemanticAction,
        Func<HttpContext, string> resolveAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(requiresSignature);
        ArgumentNullException.ThrowIfNull(resolveSemanticAction);
        ArgumentNullException.ThrowIfNull(resolveAction);

        return builder.RequireElectronicSignatureWhen(
            requiresSignature,
            context => new SigTokenBinding(
                resolveAction(context.HttpContext),
                resolveSemanticAction(context.HttpContext)));
    }

    /// <summary>The token the filter verified and consumed for this request, or null when no filter ran.</summary>
    public static System.IdentityModel.Tokens.Jwt.JwtSecurityToken? VerifiedSigToken(this HttpContext httpContext) =>
        httpContext?.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;
}
