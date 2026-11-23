using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Topology;
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
    private static readonly MethodInfo BootMethod =
        typeof(ServiceHosts).GetMethod(nameof(Boot), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Each host by name, derived from <see cref="ServiceTopology.Services"/>, with how to start it.</summary>
    public static IReadOnlyDictionary<string, Func<Action<IServiceCollection>?, BootedHost>> All { get; } =
        ServiceTopology.Services.ToDictionary(
            service => service.Name,
            CreateBootDelegate,
            StringComparer.Ordinal);

    /// <summary>The names of <see cref="All"/>, for <c>[MemberData]</c>.</summary>
    public static TheoryData<string> Names => new(All.Keys);

    private static Func<Action<IServiceCollection>?, BootedHost> CreateBootDelegate(CadenceService service)
    {
        if (string.Equals(service.Name, "execution", StringComparison.Ordinal))
        {
            return BootExecution;
        }

        Type programType = Assembly.Load(service.AssemblyName).GetType(service.ProgramTypeName, throwOnError: true)!;
        return BootMethod
            .MakeGenericMethod(programType)
            .CreateDelegate<Func<Action<IServiceCollection>?, BootedHost>>();
    }

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
