using System;
using System.Collections.Generic;

using Cadence.Apps.Gateway;
using Cadence.Security.Authentication;
using Cadence.Telemetry;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

// Callers authenticate with a Keycloak-issued bearer token. OIDC_AUTHORITY (the issuer browsers see) is
// required and the host refuses to start without it. OIDC_METADATA_ADDRESS optionally points discovery at
// an internal URL (for example the Keycloak container), and OIDC_AUDIENCE is validated whenever it is set.
string oidcAuthority = builder.Configuration["OIDC_AUTHORITY"] is { Length: > 0 } authority
    ? authority
    : throw new InvalidOperationException("OIDC_AUTHORITY must be set to the Keycloak realm URL (for example http://keycloak:8080/realms/cadence).");
string? oidcAudience = builder.Configuration["OIDC_AUDIENCE"];
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

// Each service's address. Only in Development does a missing address fall back to the local port; anywhere
// else the gateway refuses to start rather than forwarding to localhost, and an address that is not an
// absolute http(s) URL fails startup in every environment.
string GetServiceUrl(string envVar, int defaultPort)
{
    if (builder.Configuration[envVar] is not { Length: > 0 } url)
    {
        return builder.Environment.IsDevelopment()
            ? new UriBuilder(Uri.UriSchemeHttp, "localhost", defaultPort).ToString()
            : throw new InvalidOperationException($"{envVar} must be set to the service's base URL outside Development.");
    }

    return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        ? url
        : throw new InvalidOperationException($"{envVar} must be an absolute http or https URL (was '{url}').");
}

string designerUrl = GetServiceUrl("DESIGNER_SERVICE_URL", 8001);
string executionUrl = GetServiceUrl("EXECUTION_SERVICE_URL", 8002);
string etmfUrl = GetServiceUrl("ETMF_SERVICE_URL", 8003);
string interopUrl = GetServiceUrl("INTEROP_SERVICE_URL", 8004);
string qualityUrl = GetServiceUrl("QUALITY_SERVICE_URL", 8005);
string notificationsUrl = GetServiceUrl("NOTIFICATIONS_SERVICE_URL", 8006);
string ctmsUrl = GetServiceUrl("CTMS_SERVICE_URL", 8007);
string safetyUrl = GetServiceUrl("SAFETY_SERVICE_URL", 8008);
string ticketsUrl = GetServiceUrl("TICKETS_SERVICE_URL", 8009);
string eisfUrl = GetServiceUrl("EISF_SERVICE_URL", 8010);
string econsentUrl = GetServiceUrl("ECONSENT_SERVICE_URL", 8011);
string orgUrl = GetServiceUrl("ORG_SERVICE_URL", 8012);
string fileshareUrl = GetServiceUrl("FILESHARE_SERVICE_URL", 8013);
string imagingUrl = GetServiceUrl("IMAGING_SERVICE_URL", 8014);
string knowledgeUrl = GetServiceUrl("KNOWLEDGE_SERVICE_URL", 8015);
string paymentsUrl = GetServiceUrl("PAYMENTS_SERVICE_URL", 8016);
string aiGatewayUrl = GetServiceUrl("AI_GATEWAY_SERVICE_URL", 8017);

// Every route must lead to paths its service maps; tests/Gateway/GatewayRouteCoverageTests.cs checks both directions.
var routes = new[]
{
    new RouteConfig { RouteId = "designer-route", ClusterId = "designer-cluster", Match = new RouteMatch { Path = "/api/v1/studies/{**catch-all}" } },
    new RouteConfig { RouteId = "designer-protocols-route", ClusterId = "designer-cluster", Match = new RouteMatch { Path = "/api/v1/protocols/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-obs-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/observations/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-forms-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/forms/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-queries-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/queries/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-generic-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/execution/{**catch-all}" } },
    new RouteConfig { RouteId = "etmf-route", ClusterId = "etmf-cluster", Match = new RouteMatch { Path = "/api/v1/etmf/{**catch-all}" } },
    new RouteConfig { RouteId = "interop-route", ClusterId = "interop-cluster", Match = new RouteMatch { Path = "/api/v1/interop/{**catch-all}" } },
    new RouteConfig { RouteId = "quality-route", ClusterId = "quality-cluster", Match = new RouteMatch { Path = "/api/v1/quality/{**catch-all}" } },
    new RouteConfig { RouteId = "notifications-route", ClusterId = "notifications-cluster", Match = new RouteMatch { Path = "/api/v1/notifications/{**catch-all}" } },
    new RouteConfig { RouteId = "ctms-route", ClusterId = "ctms-cluster", Match = new RouteMatch { Path = "/api/v1/ctms/{**catch-all}" } },
    new RouteConfig { RouteId = "safety-route", ClusterId = "safety-cluster", Match = new RouteMatch { Path = "/api/v1/safety/{**catch-all}" } },
    new RouteConfig { RouteId = "tickets-route", ClusterId = "tickets-cluster", Match = new RouteMatch { Path = "/api/v1/tickets/{**catch-all}" } },
    new RouteConfig { RouteId = "eisf-route", ClusterId = "eisf-cluster", Match = new RouteMatch { Path = "/api/v1/eisf/{**catch-all}" } },
    new RouteConfig { RouteId = "econsent-route", ClusterId = "econsent-cluster", Match = new RouteMatch { Path = "/api/v1/econsent/{**catch-all}" } },
    new RouteConfig { RouteId = "org-route", ClusterId = "org-cluster", Match = new RouteMatch { Path = "/api/v1/org/{**catch-all}" } },
    new RouteConfig { RouteId = "fileshare-route", ClusterId = "fileshare-cluster", Match = new RouteMatch { Path = "/api/v1/fileshare/{**catch-all}" } },
    new RouteConfig { RouteId = "imaging-route", ClusterId = "imaging-cluster", Match = new RouteMatch { Path = "/api/v1/imaging/{**catch-all}" } },
    new RouteConfig { RouteId = "knowledge-route", ClusterId = "knowledge-cluster", Match = new RouteMatch { Path = "/api/v1/knowledge/{**catch-all}" } },
    new RouteConfig { RouteId = "payments-route", ClusterId = "payments-cluster", Match = new RouteMatch { Path = "/api/v1/payments/{**catch-all}" } },
    new RouteConfig { RouteId = "ai-gateway-route", ClusterId = "ai-gateway-cluster", Match = new RouteMatch { Path = "/api/v1/ai/{**catch-all}" } }
};

var clusters = new[]
{
    GatewayClusters.Create("designer-cluster", designerUrl, builder.Configuration),
    GatewayClusters.Create("execution-cluster", executionUrl, builder.Configuration),
    GatewayClusters.Create("etmf-cluster", etmfUrl, builder.Configuration),
    GatewayClusters.Create("interop-cluster", interopUrl, builder.Configuration),
    GatewayClusters.Create("quality-cluster", qualityUrl, builder.Configuration),
    GatewayClusters.Create("notifications-cluster", notificationsUrl, builder.Configuration),
    GatewayClusters.Create("ctms-cluster", ctmsUrl, builder.Configuration),
    GatewayClusters.Create("safety-cluster", safetyUrl, builder.Configuration),
    GatewayClusters.Create("tickets-cluster", ticketsUrl, builder.Configuration),
    GatewayClusters.Create("eisf-cluster", eisfUrl, builder.Configuration),
    GatewayClusters.Create("econsent-cluster", econsentUrl, builder.Configuration),
    GatewayClusters.Create("org-cluster", orgUrl, builder.Configuration),
    GatewayClusters.Create("fileshare-cluster", fileshareUrl, builder.Configuration),
    GatewayClusters.Create("imaging-cluster", imagingUrl, builder.Configuration),
    GatewayClusters.Create("knowledge-cluster", knowledgeUrl, builder.Configuration),
    GatewayClusters.Create("payments-cluster", paymentsUrl, builder.Configuration),
    GatewayClusters.Create("ai-gateway-cluster", aiGatewayUrl, builder.Configuration),
};

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

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Gateway", version = "10.0" }))
    .WithName("HealthCheck")
    .WithSummary("Gateway liveness and readiness probe.")
    .AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", gateway = "Cadence.Gateway.NET10" }))
    .WithName("GatewayStatus")
    .WithSummary("Returns gateway dispatch status.")
    .AllowAnonymous();

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
