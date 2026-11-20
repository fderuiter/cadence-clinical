using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.RateLimiting;

using Cadence.Apps.Gateway;
using Cadence.Apps.Gateway.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Telemetry;
using Cadence.Topology;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

// Callers authenticate with a Keycloak-issued bearer token. OIDC_AUTHORITY (the issuer browsers see) is
// required and the host refuses to start without it. OIDC_METADATA_ADDRESS optionally points discovery at
// an internal URL (for example the Keycloak container). OIDC_AUDIENCE is the API audience only the web
// client's tokens carry; outside Development it is required, so a token issued to another client in the
// realm (such as the password-grant e-signature client) is refused (#5037). It is validated whenever set.
string oidcAuthority = builder.Configuration["OIDC_AUTHORITY"] is { Length: > 0 } authority
    ? authority
    : throw new InvalidOperationException("OIDC_AUTHORITY must be set to the Keycloak realm URL (for example http://keycloak:8080/realms/cadence).");
string? oidcAudience = builder.Configuration["OIDC_AUDIENCE"];
if (string.IsNullOrWhiteSpace(oidcAudience) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "OIDC_AUDIENCE must be set outside Development to the API audience the web client's tokens carry (for example cadence-api).");
}

string? oidcMetadataAddress = builder.Configuration["OIDC_METADATA_ADDRESS"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = oidcAuthority;
        if (!string.IsNullOrWhiteSpace(oidcMetadataAddress))
        {
            options.MetadataAddress = oidcMetadataAddress;
        }

        options.RequireHttpsMetadata = builder.Configuration.GetValue("OIDC_REQUIRE_HTTPS_METADATA", true);
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidIssuer = oidcAuthority;
        options.TokenValidationParameters.NameClaimType = CadenceClaimTypes.Subject;
        options.TokenValidationParameters.RoleClaimType = GatewayIdentityForwarding.RoleClaimType;
        options.TokenValidationParameters.ValidateAudience = !string.IsNullOrWhiteSpace(oidcAudience);
        options.TokenValidationParameters.ValidAudience = oidcAudience;
        options.Events = new JwtBearerEvents { OnTokenValidated = GatewayIdentityForwarding.AddKeycloakRealmRoles };
    });

builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());

// Downstream services only trust identity headers signed with this secret.
builder.Services.AddOptions<GatewaySigningOptions>()
    .Configure(options => options.Secret = builder.Configuration["GATEWAY_SECRET"])
    .Validate(
        options => options.Secret is { Length: >= GatewayAuthenticationOptions.MinimumSecretLength },
        $"GATEWAY_SECRET must be set to a secret of at least {GatewayAuthenticationOptions.MinimumSecretLength} characters.")
    .ValidateOnStart();

// E-signature step-up: signers re-enter their password, which is checked with Keycloak through a client
// reserved for that purpose (direct access grants only), before a single-use X-Sig-Token is issued.
builder.Services.AddOptions<SignatureCredentialOptions>()
    .Configure(options =>
    {
        options.TokenEndpoint = SignatureCredentialOptions.ResolveTokenEndpoint(
            builder.Configuration["OIDC_TOKEN_ENDPOINT"], oidcMetadataAddress, oidcAuthority);
        options.ClientId = builder.Configuration["OIDC_SIGNATURE_CLIENT_ID"] is { Length: > 0 } clientId
            ? clientId
            : SignatureCredentialOptions.DefaultClientId;
        options.ClientSecret = builder.Configuration["OIDC_SIGNATURE_CLIENT_SECRET"];
    });
builder.Services.AddHttpClient<ICredentialVerifier, KeycloakCredentialVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
// A tighter per-signer limit on password re-checks, on top of the gateway-wide limit (AddGatewayRateLimiting).
builder.Services.Configure<RateLimiterOptions>(options =>
{
    options.AddPolicy(SignatureTokenEndpoints.RateLimitPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value ?? string.Empty,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = SignatureTokenEndpoints.AttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// Each service's address. Only in Development does a missing address fall back to the local port; anywhere
// else the gateway refuses to start rather than forwarding to localhost, and an address that is not an
// absolute http(s) URL fails startup in every environment.
string GetServiceUrl(CadenceService service)
{
    if (builder.Configuration[service.UrlVariable] is not { Length: > 0 } url)
    {
        return builder.Environment.IsDevelopment()
            ? service.LocalUrl.ToString()
            : throw new InvalidOperationException($"{service.UrlVariable} must be set to the service's base URL outside Development.");
    }

    return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        ? url
        : throw new InvalidOperationException($"{service.UrlVariable} must be an absolute http or https URL (was '{url}').");
}

// The routes and clusters come from the service topology manifest (#5315). Every route must lead to paths its service
// maps; tests/Cadence.UnitTests/Gateway/GatewayRouteCoverageTests.cs checks both directions.
var routes = ServiceTopology.Services
    .SelectMany(service => service.RoutePrefixes.Select(prefix => new RouteConfig
    {
        RouteId = $"{service.Name}-{prefix.Replace('/', '-')}-route",
        ClusterId = service.ClusterId,
        Match = new RouteMatch { Path = $"/api/v1/{prefix}/{{**catch-all}}" },
    }))
    .ToArray();

var clusters = ServiceTopology.Services
    .Select(service => GatewayClusters.Create(service.ClusterId, GetServiceUrl(service), builder.Configuration))
    .ToArray();

builder.Services.AddGatewayRateLimiting(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters)
    .AddTransforms(GatewayIdentityForwarding.AddSignedIdentityTransform);

var app = builder.Build();
app.UseCadenceWebDefaults();
app.UseAuthentication();
// After authentication, so each signed-in caller has their own limit; before authorization, so a flood of
// bad tokens is limited per client address too.
app.UseRateLimiter();
app.UseAuthorization();
app.UseChangeReasonValidation();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Gateway", version = "10.0" }))
    .WithName("HealthCheck")
    .WithSummary("Gateway liveness and readiness probe.")
    .AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", gateway = "Cadence.Gateway.NET10" }))
    .WithName("GatewayStatus")
    .WithSummary("Returns gateway dispatch status.")
    .AllowAnonymous();

app.MapSignatureTokenEndpoints();
app.MapReverseProxy();

await app.RunAsync();

namespace Cadence.Apps.Gateway
{
    public partial class Program
    {
        protected Program()
        {
        }
    }
}
