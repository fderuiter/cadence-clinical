using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Topology;

/// <summary>
/// One service the gateway routes to: what it is called, where it listens, how the gateway finds it and which API
/// prefixes it serves.
/// </summary>
/// <param name="Name">The service's name: its AppHost resource, docker compose service and gateway cluster prefix.</param>
/// <param name="Directory">Its directory under <c>apps/</c>.</param>
/// <param name="Port">The host port docker compose publishes it on and the AppHost runs it on.</param>
/// <param name="UrlVariable">The variable that tells the gateway the service's base URL.</param>
/// <param name="RoutePrefixes">
/// The <c>/api/v1/&lt;prefix&gt;</c> paths the gateway forwards to it. A prefix of two segments, such as
/// <c>execution/forms</c>, takes precedence over another service's one-segment prefix it lies under: a module moved
/// into the kernel host keeps its routes (ADR-2601 §4.4).
/// </param>
/// <param name="Database">Whether it needs the PostgreSQL connection and the audit-log key.</param>
/// <param name="Core">Whether the AppHost's <c>core</c> profile runs it.</param>
public sealed record CadenceService(
    string Name,
    string Directory,
    int Port,
    string UrlVariable,
    IReadOnlyList<string> RoutePrefixes,
    bool Database,
    bool Core)
{
    /// <summary>The gateway's cluster for this service.</summary>
    public string ClusterId => $"{Name}-cluster";

    /// <summary>The address the gateway uses in Development when <see cref="UrlVariable"/> is not set.</summary>
    public Uri LocalUrl => new UriBuilder(Uri.UriSchemeHttp, "localhost", Port).Uri;
}

/// <summary>
/// The service topology manifest (#5315): the one list of services, ports, address variables and gateway routes. The
/// gateway builds its routes and clusters from it, the AppHost runs and wires the services from it, and the developer
/// CLI checks its ports from it. docker compose and the docs that describe the topology can't read C#, so
/// <c>ServiceTopologyTests</c> fails when they disagree with it.
/// </summary>
public static class ServiceTopology
{
    /// <summary>The gateway's port: the one address the web apps call.</summary>
    public const int GatewayPort = 8000;

    /// <summary>Every service behind the gateway, in port order.</summary>
    public static IReadOnlyList<CadenceService> Services { get; } =
    [
        new("designer", "designer", 8001, "DESIGNER_SERVICE_URL", ["studies", "protocols"], Database: false, Core: false),
        new("execution", "execution", 8002, "EXECUTION_SERVICE_URL", ["execution"], Database: true, Core: true),
        new("etmf", "etmf", 8003, "ETMF_SERVICE_URL", ["etmf"], Database: true, Core: true),
        new("interop", "interop", 8004, "INTEROP_SERVICE_URL", ["interop"], Database: true, Core: false),
        new("quality", "quality", 8005, "QUALITY_SERVICE_URL", ["quality"], Database: true, Core: false),
        new("notifications", "notifications", 8006, "NOTIFICATIONS_SERVICE_URL", ["notifications"], Database: true, Core: false),
        new("ctms", "ctms", 8007, "CTMS_SERVICE_URL", ["ctms"], Database: true, Core: true),
        new("safety", "safety", 8008, "SAFETY_SERVICE_URL", ["safety"], Database: true, Core: false),
        new("tickets", "tickets", 8009, "TICKETS_SERVICE_URL", ["tickets"], Database: true, Core: false),
        new("eisf", "eisf", 8010, "EISF_SERVICE_URL", ["eisf"], Database: true, Core: false),
        new("econsent", "econsent", 8011, "ECONSENT_SERVICE_URL", ["econsent"], Database: true, Core: false),
        new("org", "org", 8012, "ORG_SERVICE_URL", ["org"], Database: true, Core: false),
        new("fileshare", "fileshare", 8013, "FILESHARE_SERVICE_URL", ["fileshare"], Database: true, Core: false),
        new("imaging", "imaging", 8014, "IMAGING_SERVICE_URL", ["imaging"], Database: true, Core: false),
        new("knowledge", "knowledge", 8015, "KNOWLEDGE_SERVICE_URL", ["knowledge", "training"], Database: true, Core: true),
        new("payments", "payments", 8016, "PAYMENTS_SERVICE_URL", ["payments"], Database: true, Core: false),
        new("kernel", "kernel-api", 8018, "KERNEL_SERVICE_URL", ["kernel", "ai", "execution/forms"], Database: true, Core: true),
    ];

    /// <summary>The service called <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">No service has that name.</exception>
    public static CadenceService Get(string name) =>
        Services.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal))
        ?? throw new ArgumentException($"No service is called '{name}'.", nameof(name));
}
