using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.UnitTests.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
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

    private static readonly IReadOnlyDictionary<string, Func<Task<IReadOnlyList<string>>>> ServicesByCluster =
        ServiceHosts.All.ToDictionary(
            kv => $"{kv.Key}-cluster",
            kv => new Func<Task<IReadOnlyList<string>>>(() => MappedPathsAsync(kv.Value)),
            StringComparer.Ordinal);

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-191")]
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

    private static async Task<IReadOnlyList<string>> MappedPathsAsync(
        Func<Action<IServiceCollection>?, BootedHost> boot)
    {
        await using var host = boot(null);
        return host.Endpoints
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
