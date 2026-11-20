using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Cadence.Apps.Gateway;

/// <summary>
/// The secret the gateway signs forwarded identity with. Bound from <c>GATEWAY_SECRET</c>; no default.
/// </summary>
public sealed class GatewaySigningOptions
{
    private string? _secret;
    private byte[] _secretBytes = [];

    public string? Secret
    {
        get => _secret;
        set
        {
            _secret = value;
            _secretBytes = string.IsNullOrEmpty(value) ? [] : GatewayIdentity.EncodeSecret(value);
        }
    }

    /// <summary>Cached UTF-8 bytes of <see cref="Secret"/>, or empty when unset.</summary>
    public ReadOnlyMemory<byte> SecretBytes => _secretBytes;

    internal byte[] GetSecretBytes() => _secretBytes;
}

/// <summary>
/// Turns the caller's validated OIDC token into the signed identity headers downstream services trust, and
/// makes sure nothing the browser sent can masquerade as that identity.
/// @req:PRD-SYS-053
/// </summary>
public static class GatewayIdentityForwarding
{
    public const string RoleClaimType = "role";

    private static readonly string[] ChangeReasonHeaders = [GatewayHeaders.ChangeReason, "X-Reason-For-Change"];

    private static readonly string[] StrippedRequestHeaders =
    [
        .. GatewayHeaders.GatewayAsserted,
        GatewayHeaders.ChangeReason,
        GatewayHeaders.SigToken,
        "Authorization",
    ];

    /// <summary>
    /// Keycloak puts realm roles in a nested <c>realm_access.roles</c> JSON claim; flatten them into plain
    /// <see cref="RoleClaimType"/> claims so authorization and forwarding see them.
    /// </summary>
    public static Task AddKeycloakRealmRoles(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return Task.CompletedTask;
        }

        foreach (var realmAccess in identity.FindAll("realm_access").ToList())
        {
            try
            {
                using var document = JsonDocument.Parse(realmAccess.Value);
                if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        if (role.GetString() is { Length: > 0 } name && !identity.HasClaim(RoleClaimType, name))
                        {
                            identity.AddClaim(new Claim(RoleClaimType, name));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // A malformed realm_access claim grants no roles.
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the identity to forward from the authenticated user and the request's own change-reason and
    /// e-signature step-up headers. Returns <c>null</c> when the token carries no subject.
    /// </summary>
    public static GatewayIdentity? CreateIdentity(HttpContext httpContext)
    {
        var user = httpContext.User;
        string? subject = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var roles = user.FindAll(RoleClaimType).Select(c => c.Value)
            .Where(r => !string.IsNullOrWhiteSpace(r) && !r.Contains(','))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var sites = user.FindAll(CadenceClaimTypes.SiteId).Select(c => c.Value)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Contains(','))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // The studies the caller is assigned to (or "*"), as the sign-in token lists them; none means study-scoped routes
        // are denied (ADR-2516).
        var studies = user.FindAll(CadenceClaimTypes.StudyIds).Select(c => c.Value)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Contains(','))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // The tenant comes from the sign-in token alone; a token without one forwards no tenant (#5038).
        string? tenant = user.FindFirst(CadenceClaimTypes.TenantId)?.Value is { } t && !string.IsNullOrWhiteSpace(t) ? t : null;
        bool unblinded = string.Equals(user.FindFirst(CadenceClaimTypes.UnblindedAccess)?.Value, "true", StringComparison.OrdinalIgnoreCase);

        string? changeReason = null;
        foreach (string header in ChangeReasonHeaders)
        {
            if (ChangeReasonHeader.TryDecode(httpContext.Request.Headers[header].ToString(), out string? reason)
                && !string.IsNullOrWhiteSpace(reason))
            {
                changeReason = reason;
                break;
            }
        }

        return new GatewayIdentity(
            UserId: subject,
            Roles: roles,
            SiteIds: sites,
            SponsorId: user.FindFirst(CadenceClaimTypes.SponsorId)?.Value,
            UnblindedAccess: unblinded,
            TenantId: tenant,
            ChangeReason: changeReason,
            SigToken: httpContext.Request.Headers[GatewayHeaders.SigToken].ToString(),
            StudyIds: studies);
    }

    /// <summary>
    /// Answers <c>400</c> when a request's reason for change is in the RFC 8187 form but malformed, so a bad
    /// encoding is reported as such instead of being forwarded without its reason (ADR-2495).
    /// </summary>
    public static IApplicationBuilder UseChangeReasonValidation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            foreach (string header in ChangeReasonHeaders)
            {
                if (!ChangeReasonHeader.TryDecode(context.Request.Headers[header].ToString(), out _))
                {
                    await TypedResults.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Malformed reason for change",
                        detail: $"{header} must be printable ASCII or RFC 8187 \"UTF-8''\" followed by percent-encoded UTF-8.")
                        .ExecuteAsync(context).ConfigureAwait(false);
                    return;
                }
            }

            await next(context).ConfigureAwait(false);
        });

    /// <summary>
    /// Registers the proxy transform: strip every client-supplied identity header and the bearer token, then
    /// attach freshly signed identity headers for the authenticated caller.
    /// </summary>
    public static void AddSignedIdentityTransform(TransformBuilderContext builderContext)
    {
        builderContext.AddRequestTransform(transformContext =>
        {
            var proxyHeaders = transformContext.ProxyRequest.Headers;
            foreach (string header in StrippedRequestHeaders)
            {
                proxyHeaders.Remove(header);
            }

            var identity = CreateIdentity(transformContext.HttpContext);
            if (identity is null)
            {
                return ValueTask.CompletedTask;
            }

            var signing = transformContext.HttpContext.RequestServices.GetRequiredService<IOptions<GatewaySigningOptions>>().Value;
            var clock = transformContext.HttpContext.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
            IReadOnlyDictionary<string, string> signed = identity.ToSignedHeaders(
                clock.UtcNow, signing.GetSecretBytes());

            foreach (var (name, value) in signed)
            {
                proxyHeaders.TryAddWithoutValidation(name, value);
            }

            return ValueTask.CompletedTask;
        });
    }
}
