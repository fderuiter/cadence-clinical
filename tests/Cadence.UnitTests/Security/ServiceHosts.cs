using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.UnitTests.Execution;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// Every service host the gateway routes to, booted in process with test settings and no database work at startup, so a
/// test can read each one's endpoint table and send it signed requests.
/// </summary>
internal static class ServiceHosts
{
    /// <summary>Each host by name, with how to start it.</summary>
    public static IReadOnlyDictionary<string, Func<Action<IServiceCollection>?, BootedHost>> All { get; } =
        new Dictionary<string, Func<Action<IServiceCollection>?, BootedHost>>(StringComparer.Ordinal)
        {
            ["designer"] = Boot<Cadence.Apps.Designer.Program>,
            ["execution"] = BootExecution,
            ["etmf"] = Boot<Cadence.Apps.Etmf.Program>,
            ["interop"] = Boot<Cadence.Apps.Interop.Program>,
            ["quality"] = Boot<Cadence.Apps.Quality.Program>,
            ["notifications"] = Boot<Cadence.Apps.Notifications.Program>,
            ["ctms"] = Boot<Cadence.Apps.Ctms.Program>,
            ["safety"] = Boot<Cadence.Apps.Safety.Program>,
            ["tickets"] = Boot<Cadence.Apps.Tickets.Program>,
            ["eisf"] = Boot<Cadence.Apps.Eisf.Program>,
            ["econsent"] = Boot<Cadence.Apps.Econsent.Program>,
            ["org"] = Boot<Cadence.Apps.Org.Program>,
            ["fileshare"] = Boot<Cadence.Apps.Fileshare.Program>,
            ["imaging"] = Boot<Cadence.Apps.Imaging.Program>,
            ["knowledge"] = Boot<Cadence.Apps.Knowledge.Program>,
            ["payments"] = Boot<Cadence.Apps.Payments.Program>,
            ["kernel"] = Boot<Cadence.Kernel.Host.Program>,
        };

    /// <summary>The names of <see cref="All"/>, for <c>[MemberData]</c>.</summary>
    public static TheoryData<string> Names => new(All.Keys);

    private static BootedHost Boot<TProgram>(Action<IServiceCollection>? configure)
        where TProgram : class
    {
        var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestStorage()
                .UseTestDemographicsKey().UseTestRandomizationKey()
                .UseTestFieldProtectionKeys()
                .UseTestSceSigningKey()
                .UseTestImagingDeidentificationKeys();
            host.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType != typeof(SigTokenUseStoreStartupCheck))
                    .ToList())
                {
                    services.Remove(descriptor);
                }

                configure?.Invoke(services);
            });
        });
        return new BootedHost(factory, factory.Services, identity => factory.CreateSignedClient(identity), factory.CreateClient);
    }

    // Execution needs its in-memory database stand-in to start without PostgreSQL.
    private static BootedHost BootExecution(Action<IServiceCollection>? configure)
    {
        var execution = new ExecutionApiFactory();
        var factory = configure is null ? execution : execution.WithWebHostBuilder(host => host.ConfigureTestServices(configure));
        return new BootedHost(execution, factory.Services, identity => factory.CreateSignedClient(identity), factory.CreateClient);
    }
}

/// <summary>
/// Per-test-class cache of booted service hosts keyed by service name and configuration profile, disposed once xUnit
/// finishes the test class.
/// </summary>
public sealed class ServiceHostCache : IAsyncDisposable
{
    private readonly Dictionary<(string Service, string Profile), BootedHost> _hosts = new();

    internal BootedHost GetDefault(string service) => Get(service, "default", configure: null);

    internal BootedHost Get(string service, string profile, Action<IServiceCollection>? configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);

        var key = (service, profile);
        if (!_hosts.TryGetValue(key, out var booted))
        {
            booted = ServiceHosts.All[service](configure);
            _hosts[key] = booted;
        }

        return booted;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts.Values)
        {
            await host.DisposeAsync();
        }

        _hosts.Clear();
    }
}

/// <summary>One booted host: its services, and clients that sign as a given identity or not at all.</summary>
internal sealed class BootedHost(
    IAsyncDisposable owner,
    IServiceProvider services,
    Func<GatewayIdentity, HttpClient> signedClient,
    Func<HttpClient> unsignedClient) : IAsyncDisposable
{
    public IServiceProvider Services { get; } = services;

    /// <summary>Every route endpoint the host maps.</summary>
    public IReadOnlyList<RouteEndpoint> Endpoints =>
        Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();

    public HttpClient CreateSignedClient(GatewayIdentity identity) => signedClient(identity);

    public HttpClient CreateUnsignedClient() => unsignedClient();

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
