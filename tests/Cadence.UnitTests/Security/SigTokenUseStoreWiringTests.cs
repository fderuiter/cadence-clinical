using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Database.Signatures;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// Every host that maps an e-signature route keeps token uses in a durable store, and a host that does not refuses
/// to start outside Development, so a token cannot be replayed after a restart or on a second instance (#5434,
/// ADR-2515).
/// </summary>
public sealed class SigTokenUseStoreWiringTests(ServiceHostCache hosts) : IClassFixture<ServiceHostCache>
{
    /// <summary>
    /// Each host's composition root registers the durable store when it maps a signed route. Execution's shared test
    /// host swaps in process memory for its in-memory database, so Execution is checked from its real composition root.
    /// </summary>
    [Theory]
    [MemberData(nameof(ServiceHosts.Names), MemberType = typeof(ServiceHosts))]
    [GxPRequirement("SRS-SYS-005")]
    public void EveryHostWithASignedRoute_RegistersADurableTokenUseStore(string service)
    {
        var host = hosts.GetDefault(service);
        bool mapsSignedRoute = host.Endpoints.Any(e => e.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null);
        if (service is "econsent" or "knowledge" or "execution" or "tickets")
        {
            mapsSignedRoute.Should().BeTrue("{0} maps e-signature routes", service);
        }

        if (!mapsSignedRoute || service == "execution")
        {
            return;
        }

        host.Services.GetRequiredService<ISigTokenUseStore>().Should().BeAssignableTo<IDurableSigTokenUseStore>();
    }

    /// <summary>Execution's real composition root registers the durable store for its own context.</summary>
    [Fact]
    public void ExecutionCompositionRoot_RegistersTheDurableTokenUseStore()
    {
        using var host = new WebApplicationFactory<Cadence.Apps.Execution.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestDemographicsKey().UseTestRandomizationKey()
            .UseTestPostgresConnectionString());

        host.Services.GetRequiredService<ISigTokenUseStore>()
            .Should().BeOfType<PostgresSigTokenUseStore<Cadence.Apps.Execution.Infrastructure.Persistence.ExecutionDbContext>>();
    }

    /// <summary>Outside Development, a signed route over the process-local store fails startup and names the route.</summary>
    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task StartupCheck_InProductionWithASignedRouteAndTheProcessLocalStore_RefusesToStart()
    {
        var check = Check(Environments.Production, new ReplayCache(), signedRoute: true);

        var act = () => check.StartAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("/sign");
    }

    /// <summary>A durable store, Development, or a host with no signed route starts.</summary>
    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Development", false, true)]
    [InlineData("Production", false, false)]
    public async Task StartupCheck_OtherwiseStarts(string environment, bool durable, bool signedRoute)
    {
        ISigTokenUseStore store = durable ? new DurableStore() : new ReplayCache();
        var check = Check(environment, store, signedRoute || durable);

        await check.StartAsync(TestContext.Current.CancellationToken);
    }

    private static SigTokenUseStoreStartupCheck Check(string environment, ISigTokenUseStore store, bool signedRoute)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/sign"), 0);
        if (signedRoute)
        {
            builder.Metadata.Add(ElectronicSignatureRequired.Instance);
        }

        var services = new ServiceCollection()
            .AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(builder.Build()))
            .BuildServiceProvider();
        return new SigTokenUseStoreStartupCheck(services, store, new Environment(environment));
    }

    private sealed class DurableStore : IDurableSigTokenUseStore
    {
        public Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, System.Threading.CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
