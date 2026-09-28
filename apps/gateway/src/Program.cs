using System;
using System.Collections.Generic;

using Cadence.Telemetry;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddCadenceWebDefaults();

string GetServiceUrl(string envVar, int defaultPort)
{
    return builder.Configuration[envVar] is { Length: > 0 } url
        ? url
        : new UriBuilder(Uri.UriSchemeHttp, "localhost", defaultPort).ToString();
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

var routes = new[]
{
    new RouteConfig { RouteId = "designer-route", ClusterId = "designer-cluster", Match = new RouteMatch { Path = "/api/v1/studies/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-subjects-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/subjects/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-obs-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/observations/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-forms-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/forms/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-queries-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/queries/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-comp-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/compliance/{**catch-all}" } },
    new RouteConfig { RouteId = "execution-generic-route", ClusterId = "execution-cluster", Match = new RouteMatch { Path = "/api/v1/execution/{**catch-all}" } },
    new RouteConfig { RouteId = "etmf-route", ClusterId = "etmf-cluster", Match = new RouteMatch { Path = "/api/v1/etmf/{**catch-all}" } },
    new RouteConfig { RouteId = "etmf-docs-route", ClusterId = "etmf-cluster", Match = new RouteMatch { Path = "/api/v1/documents/{**catch-all}" } },
    new RouteConfig { RouteId = "interop-route", ClusterId = "interop-cluster", Match = new RouteMatch { Path = "/api/v1/interop/{**catch-all}" } },
    new RouteConfig { RouteId = "quality-route", ClusterId = "quality-cluster", Match = new RouteMatch { Path = "/api/v1/quality/{**catch-all}" } },
    new RouteConfig { RouteId = "notifications-route", ClusterId = "notifications-cluster", Match = new RouteMatch { Path = "/api/v1/notifications/{**catch-all}" } },
    new RouteConfig { RouteId = "ctms-route", ClusterId = "ctms-cluster", Match = new RouteMatch { Path = "/api/v1/ctms/{**catch-all}" } },
    new RouteConfig { RouteId = "ctms-sites-route", ClusterId = "ctms-cluster", Match = new RouteMatch { Path = "/api/v1/sites/{**catch-all}" } },
    new RouteConfig { RouteId = "safety-route", ClusterId = "safety-cluster", Match = new RouteMatch { Path = "/api/v1/safety/{**catch-all}" } },
    new RouteConfig { RouteId = "tickets-route", ClusterId = "tickets-cluster", Match = new RouteMatch { Path = "/api/v1/tickets/{**catch-all}" } },
    new RouteConfig { RouteId = "eisf-route", ClusterId = "eisf-cluster", Match = new RouteMatch { Path = "/api/v1/eisf/{**catch-all}" } },
    new RouteConfig { RouteId = "econsent-route", ClusterId = "econsent-cluster", Match = new RouteMatch { Path = "/api/v1/econsent/{**catch-all}" } },
    new RouteConfig { RouteId = "org-route", ClusterId = "org-cluster", Match = new RouteMatch { Path = "/api/v1/org/{**catch-all}" } },
    new RouteConfig { RouteId = "fileshare-route", ClusterId = "fileshare-cluster", Match = new RouteMatch { Path = "/api/v1/fileshare/{**catch-all}" } },
    new RouteConfig { RouteId = "fileshare-files-route", ClusterId = "fileshare-cluster", Match = new RouteMatch { Path = "/api/v1/files/{**catch-all}" } },
    new RouteConfig { RouteId = "imaging-route", ClusterId = "imaging-cluster", Match = new RouteMatch { Path = "/api/v1/imaging/{**catch-all}" } },
    new RouteConfig { RouteId = "knowledge-route", ClusterId = "knowledge-cluster", Match = new RouteMatch { Path = "/api/v1/knowledge/{**catch-all}" } },
    new RouteConfig { RouteId = "payments-route", ClusterId = "payments-cluster", Match = new RouteMatch { Path = "/api/v1/payments/{**catch-all}" } },
    new RouteConfig { RouteId = "ai-gateway-route", ClusterId = "ai-gateway-cluster", Match = new RouteMatch { Path = "/api/v1/ai/{**catch-all}" } }
};

var clusters = new[]
{
    new ClusterConfig { ClusterId = "designer-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = designerUrl } } } },
    new ClusterConfig { ClusterId = "execution-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = executionUrl } } } },
    new ClusterConfig { ClusterId = "etmf-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = etmfUrl } } } },
    new ClusterConfig { ClusterId = "interop-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = interopUrl } } } },
    new ClusterConfig { ClusterId = "quality-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = qualityUrl } } } },
    new ClusterConfig { ClusterId = "notifications-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = notificationsUrl } } } },
    new ClusterConfig { ClusterId = "ctms-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = ctmsUrl } } } },
    new ClusterConfig { ClusterId = "safety-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = safetyUrl } } } },
    new ClusterConfig { ClusterId = "tickets-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = ticketsUrl } } } },
    new ClusterConfig { ClusterId = "eisf-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = eisfUrl } } } },
    new ClusterConfig { ClusterId = "econsent-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = econsentUrl } } } },
    new ClusterConfig { ClusterId = "org-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = orgUrl } } } },
    new ClusterConfig { ClusterId = "fileshare-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = fileshareUrl } } } },
    new ClusterConfig { ClusterId = "imaging-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = imagingUrl } } } },
    new ClusterConfig { ClusterId = "knowledge-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = knowledgeUrl } } } },
    new ClusterConfig { ClusterId = "payments-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = paymentsUrl } } } },
    new ClusterConfig { ClusterId = "ai-gateway-cluster", Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase) { { "destination", new DestinationConfig { Address = aiGatewayUrl } } } }
};

builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters);

var app = builder.Build();
app.UseCadenceWebDefaults();

app.MapGet("/healthz", () => TypedResults.Ok(new { status = "healthy", service = "Cadence.Apps.Gateway", version = "10.0" }))
    .WithName("HealthCheck")
    .WithSummary("Gateway liveness and readiness probe.");

var api = app.MapGroup("/api/v1");
api.MapGet("/status", () => TypedResults.Ok(new { mode = "Active", gateway = "Cadence.Gateway.NET10" }))
    .WithName("GatewayStatus")
    .WithSummary("Returns gateway dispatch status.");

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
