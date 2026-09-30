using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.UnitTests.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

using Yarp.ReverseProxy.Configuration;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// Boots the real gateway and every service it routes to, and checks the gateway's route table against the
/// endpoints each service actually maps: every gateway route must lead to at least one mapped endpoint (no
/// route to nowhere), and every <c>/api/v1</c> endpoint a service maps must be reachable through the gateway.
/// </summary>
public sealed class GatewayRouteCoverageTests
{
    private const string CatchAll = "/{**catch-all}";

    private static readonly Dictionary<string, Func<Task<IReadOnlyList<string>>>> ServicesByCluster = new(StringComparer.Ordinal)
    {
        ["designer-cluster"] = MappedPaths<Cadence.Apps.Designer.Program>,
        ["execution-cluster"] = ExecutionPaths,
        ["etmf-cluster"] = MappedPaths<Cadence.Apps.Etmf.Program>,
        ["interop-cluster"] = MappedPaths<Cadence.Apps.Interop.Program>,
        ["quality-cluster"] = MappedPaths<Cadence.Apps.Quality.Program>,
        ["notifications-cluster"] = MappedPaths<Cadence.Apps.Notifications.Program>,
        ["ctms-cluster"] = MappedPaths<Cadence.Apps.Ctms.Program>,
        ["safety-cluster"] = MappedPaths<Cadence.Apps.Safety.Program>,
        ["tickets-cluster"] = MappedPaths<Cadence.Apps.Tickets.Program>,
        ["eisf-cluster"] = MappedPaths<Cadence.Apps.Eisf.Program>,
        ["econsent-cluster"] = MappedPaths<Cadence.Apps.Econsent.Program>,
        ["org-cluster"] = MappedPaths<Cadence.Apps.Org.Program>,
        ["fileshare-cluster"] = MappedPaths<Cadence.Apps.Fileshare.Program>,
        ["imaging-cluster"] = MappedPaths<Cadence.Apps.Imaging.Program>,
        ["knowledge-cluster"] = MappedPaths<Cadence.Apps.Knowledge.Program>,
        ["payments-cluster"] = MappedPaths<Cadence.Apps.Payments.Program>,
        ["ai-gateway-cluster"] = MappedPaths<Cadence.Apps.AiGateway.Program>,
    };

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task EveryRoute_LeadsToMappedEndpoints_AndEveryServiceApiIsRouted()
    {
        var routes = await GatewayRoutesAsync();
        routes.Select(r => r.ClusterId).Distinct().Should().BeEquivalentTo(ServicesByCluster.Keys,
            "every gateway cluster needs its service registered in this test");

        var problems = new List<string>();
        foreach (var (clusterId, loadPaths) in ServicesByCluster)
        {
            var mapped = await loadPaths();
            var prefixes = routes.Where(r => r.ClusterId == clusterId).Select(r => r.Prefix).ToList();

            problems.AddRange(prefixes
                .Where(prefix => !mapped.Any(path => IsUnder(path, prefix)))
                .Select(prefix => $"gateway route {prefix} -> {clusterId}: the service maps nothing under it"));

            problems.AddRange(mapped
                .Where(path => path.StartsWith("/api/v1/", StringComparison.Ordinal))
                .Where(path => !prefixes.Any(prefix => IsUnder(path, prefix)))
                .Select(path => $"{clusterId} maps {path}, which no gateway route reaches"));
        }

        problems.Should().BeEmpty(string.Join("; ", problems));
    }

    private static bool IsUnder(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<(string ClusterId, string Prefix)>> GatewayRoutesAsync()
    {
        await using var gateway = new WebApplicationFactory<Cadence.Apps.Gateway.Program>().WithWebHostBuilder(host => host
            .UseSetting("OIDC_AUTHORITY", "https://keycloak.test/realms/cadence")
            .UseTestGatewaySecret());

        return gateway.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes
            .Select(route =>
            {
                string path = route.Match.Path ?? string.Empty;
                path.Should().EndWith(CatchAll, $"route {route.RouteId} is expected to forward a whole path prefix");
                return (route.ClusterId!, path[..^CatchAll.Length]);
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<string>> MappedPaths<TProgram>()
        where TProgram : class
    {
        await using var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(host => host
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseTestDemographicsKey()
            .UseTestFieldProtectionKeys());
        return PathsOf(factory.Services);
    }

    // Execution needs its in-memory database stand-in to start without PostgreSQL.
    private static async Task<IReadOnlyList<string>> ExecutionPaths()
    {
        await using var factory = new ExecutionApiFactory();
        return PathsOf(factory.Services);
    }

    private static List<string> PathsOf(IServiceProvider services) =>
        services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
