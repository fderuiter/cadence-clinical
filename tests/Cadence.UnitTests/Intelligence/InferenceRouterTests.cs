using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Inference;
using Cadence.Modules.Intelligence.Inference.Routing;
using Cadence.Modules.Intelligence.Infrastructure.Models;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// The inference router (ADR-2636 §4.2): local-only and residency-bound tenants, supplier approval, and failover that
/// never leaves the allowed routes. No test calls a model: providers are recording stubs.
/// </summary>
public sealed class InferenceRouterTests
{
    private static readonly TenantId EuTenant = TenantId.New();
    private static readonly TenantId OpenTenant = TenantId.New();

    private readonly StubProvider _bedrockEu = new("bedrock", InferenceLocality.External, SupplierIds.AwsBedrock, "EU");
    private readonly StubProvider _vertexUs = new("vertex", InferenceLocality.External, SupplierIds.GoogleVertex, "US");
    private readonly StubProvider _foundryGlobal = new("foundry", InferenceLocality.External, SupplierIds.MicrosoftFoundry);
    private readonly StubProvider _local = new("openai-compatible", InferenceLocality.InDeployment, SupplierIds.OperatorHosted, "EU", "US");
    private readonly StubResidencies _residencies = new();
    private readonly StubPolicies _policies = new();

    public InferenceRouterTests()
    {
        _residencies.Tenants[EuTenant] = new TenantResidency("EU");
        _residencies.Tenants[OpenTenant] = new TenantResidency(null);
    }

    /// <summary>
    /// A local-only tenant is routed only to the in-deployment provider; the external provider listed first is never
    /// called, and when the local provider fails the run ends unavailable instead of failing over to it.
    /// </summary>
    [Fact]
    public async Task LocalOnlyTenant_NeverReachesAnExternalProvider_EvenWhenTheLocalOneFails()
    {
        _policies.Tenants[EuTenant] = new TenantInferencePolicy(TenantInferenceMode.LocalOnly);
        var router = Build();

        var routing = await router.ResolveAsync(Request(EuTenant, "bedrock", "openai-compatible"), TestContext.Current.CancellationToken);

        routing.Routes.Select(r => r.ProviderId).Should().Equal("openai-compatible");
        routing.Decisions[0].Refusal.Should().Be(InferenceRefusalReason.LocalOnly);
        var response = await routing.Client!.GetResponseAsync("Summarise the visit.", cancellationToken: TestContext.Current.CancellationToken);
        response.Text.Should().Be("openai-compatible:claude-opus-5-5");
        ((InferenceRoute)response.AdditionalProperties![InferenceRoute.ResponsePropertyKey]!).Locality.Should().Be(InferenceLocality.InDeployment);

        _local.Failure = new InvalidOperationException("local model down");
        var failed = () => routing.Client!.GetResponseAsync("Again.", cancellationToken: TestContext.Current.CancellationToken);
        await failed.Should().ThrowAsync<InferenceUnavailableException>();
        _bedrockEu.Calls.Should().BeEmpty();
    }

    /// <summary>The deployment's LocalOnly mode overrides a tenant's Open policy.</summary>
    [Fact]
    public async Task SovereignDeployment_MakesEveryTenantLocalOnly()
    {
        _policies.Tenants[OpenTenant] = new TenantInferencePolicy(TenantInferenceMode.Open);
        var router = Build(config: new() { ["Intelligence:Inference:Mode"] = "LocalOnly" });

        var routing = await router.ResolveAsync(Request(OpenTenant, "foundry", "openai-compatible"), TestContext.Current.CancellationToken);

        routing.Routes.Should().ContainSingle().Which.Mode.Should().Be(TenantInferenceMode.LocalOnly);
        routing.Decisions[0].Refusal.Should().Be(InferenceRefusalReason.LocalOnly);
    }

    /// <summary>
    /// A residency-bound EU tenant reaches only providers that declare EU; failover from the EU provider goes to the
    /// next allowed route, never to the US or the residency-less provider.
    /// </summary>
    [Fact]
    public async Task ResidencyBoundTenant_StaysInItsRegion_IncludingOnFailover()
    {
        var router = Build();

        var routing = await router.ResolveAsync(Request(EuTenant, "vertex", "foundry", "bedrock", "openai-compatible"), TestContext.Current.CancellationToken);

        routing.Decisions.Select(d => d.Refusal).Should().Equal(
            InferenceRefusalReason.Residency, InferenceRefusalReason.Residency, null, null);
        _bedrockEu.Failure = new System.Net.Http.HttpRequestException("503 from the provider");
        var response = await routing.Client!.GetResponseAsync("Draft the query.", cancellationToken: TestContext.Current.CancellationToken);
        response.Text.Should().Be("openai-compatible:claude-opus-5-5");
        _vertexUs.Calls.Should().BeEmpty();
        _foundryGlobal.Calls.Should().BeEmpty();
        _bedrockEu.Calls.Should().HaveCount(1);
    }

    /// <summary>A tenant without a residency may use any approved provider; an Open tenant ignores residency.</summary>
    [Fact]
    public async Task UnrestrictedAndOpenTenants_MayUseAnyApprovedProvider()
    {
        _policies.Tenants[EuTenant] = new TenantInferencePolicy(TenantInferenceMode.Open);
        var router = Build();

        var open = await router.ResolveAsync(Request(EuTenant, "vertex"), TestContext.Current.CancellationToken);
        var unrestricted = await router.ResolveAsync(Request(OpenTenant, "foundry"), TestContext.Current.CancellationToken);

        open.IsRouted.Should().BeTrue();
        unrestricted.Routes.Should().ContainSingle().Which.TenantResidency.Should().BeNull();
    }

    /// <summary>
    /// A supplier record that is not approved is refused outside Development and allowed in it; a suspended one is
    /// refused everywhere; a tenant's supplier list narrows further.
    /// </summary>
    [Fact]
    public async Task SupplierStatus_GatesTheRoute()
    {
        var suppliers = new StubSuppliers(
            (SupplierIds.AwsBedrock, SupplierStatus.Draft),
            (SupplierIds.OperatorHosted, SupplierStatus.Suspended),
            (SupplierIds.MicrosoftFoundry, SupplierStatus.Approved));
        var production = Build(suppliers: suppliers);
        var development = Build(suppliers: suppliers, environment: Environments.Development);
        var request = Request(EuTenant, "bedrock", "openai-compatible");

        (await production.ResolveAsync(request, TestContext.Current.CancellationToken)).Decisions.Select(d => d.Refusal)
            .Should().Equal(InferenceRefusalReason.SupplierNotApproved, InferenceRefusalReason.SupplierSuspended);
        (await development.ResolveAsync(request, TestContext.Current.CancellationToken)).Decisions.Select(d => d.Refusal)
            .Should().Equal(null, InferenceRefusalReason.SupplierSuspended);

        _policies.Tenants[OpenTenant] = new TenantInferencePolicy(TenantInferenceMode.ResidencyBound, new HashSet<string> { SupplierIds.AwsBedrock });
        (await production.ResolveAsync(Request(OpenTenant, "foundry"), TestContext.Current.CancellationToken)).Refusal
            .Should().Be(InferenceRefusalReason.SupplierNotAllowed);
    }

    /// <summary>An unknown tenant, an unconfigured provider and an empty route list are refused, not defaulted.</summary>
    [Fact]
    public async Task UnknownTenantsProvidersAndEmptyRoutes_AreRefused()
    {
        var router = Build();
        var ct = TestContext.Current.CancellationToken;

        (await router.ResolveAsync(Request(TenantId.New(), "bedrock"), ct)).Refusal.Should().Be(InferenceRefusalReason.TenantUnknown);
        (await router.ResolveAsync(Request(EuTenant, "anthropic"), ct)).Refusal.Should().Be(InferenceRefusalReason.ProviderNotConfigured);
        (await router.ResolveAsync(new InferenceRequest(EuTenant, []), ct)).Refusal.Should().Be(InferenceRefusalReason.NoQualifiedRoute);
    }

    /// <summary>
    /// The module's own providers are routed too: the Claude API is an external supplier that needs an approved record
    /// and declares no residency, and the in-process fake sends nothing anywhere, so it needs neither. The caller's
    /// model id reaches a provider whose client does not fix it.
    /// </summary>
    [Fact]
    public async Task BuiltInProviders_AreRoutedByTheSameRules()
    {
        var anthropic = new FakeChatClient();
        var fake = new FakeChatClient((_, options) => $"model {options?.ModelId}");
        var router = Build(builtIn:
        [
            new ChatClientModelProvider("anthropic", anthropic, new Dictionary<string, string>(), new Dictionary<string, ModelPrice>()),
            new ChatClientModelProvider(FakeChatClient.ProviderName, fake, new Dictionary<string, string>(), new Dictionary<string, ModelPrice>()),
        ]);
        var ct = TestContext.Current.CancellationToken;

        (await router.ResolveAsync(Request(OpenTenant, "anthropic"), ct)).Refusal.Should().Be(InferenceRefusalReason.SupplierNotApproved);
        (await router.ResolveAsync(Request(EuTenant, "anthropic"), ct)).Refusal.Should().Be(InferenceRefusalReason.Residency);
        var routing = await router.ResolveAsync(Request(EuTenant, FakeChatClient.ProviderName), ct);
        routing.IsRouted.Should().BeTrue();
        (await routing.Client!.GetResponseAsync("Hello.", cancellationToken: ct)).Text.Should().Contain("model claude-opus-5-5");
        anthropic.Calls.Should().Be(0);
    }

    /// <summary>
    /// The run client factory turns a refused route into the registered refusal the runtime returns, and wraps an
    /// allowed one so the call runs in the run's sandbox and records the route for the ledger.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public async Task RunClientFactory_RefusesOrRecordsTheRoute()
    {
        _policies.Tenants[EuTenant] = new TenantInferencePolicy(TenantInferenceMode.LocalOnly);
        var services = Services();
        var clients = services.GetRequiredService<IRunChatClientFactory>();
        var registry = services.GetRequiredService<IModelProviderRegistry>();
        var task = new TaskConfiguration("data_review.query_draft", 1, "bedrock", "claude-opus-5-5", "Review.", 100, AutonomyLevel.Draft);
        var ct = TestContext.Current.CancellationToken;

        var refused = await clients.CreateAsync(new AgentRunCall(EuTenant, Guid.NewGuid(), task, registry.Find("bedrock")!, "claude-opus-5-5"), ct);
        refused.Error.Should().Be(IntelligenceErrors.InferenceRefused);

        var routed = await clients.CreateAsync(
            new AgentRunCall(EuTenant, Guid.NewGuid(), task with { Provider = "openai-compatible" }, registry.Find("openai-compatible")!, "claude-opus-5-5"), ct);
        routed.Value!.Inference.Should().Be(new AgentRunInference(SupplierIds.OperatorHosted, "InDeployment", "LocalOnly", "EU"));
        await routed.Value.Client.GetResponseAsync("Summarise.", cancellationToken: ct);
        _local.Calls.Should().ContainSingle();
    }

    /// <summary>A tenant's residency is read once and reused, then read again after the cache window.</summary>
    [Fact]
    public async Task Residency_IsCachedForAWindow()
    {
        var clock = new FakeClock();
        var router = Build(clock: clock);
        var ct = TestContext.Current.CancellationToken;

        await router.ResolveAsync(Request(EuTenant, "bedrock"), ct);
        await router.ResolveAsync(Request(EuTenant, "bedrock"), ct);
        _residencies.Reads.Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(6));
        await router.ResolveAsync(Request(EuTenant, "bedrock"), ct);
        _residencies.Reads.Should().Be(2);
    }

    private static InferenceRequest Request(TenantId tenant, params string[] providers) =>
        new(tenant, [.. providers.Select(p => new ModelRoute(p, "claude-opus-5-5"))]);

    private IInferenceRouter Build(
        Dictionary<string, string?>? config = null,
        ISupplierCatalog? suppliers = null,
        string environment = "Production",
        IClock? clock = null,
        IModelProvider[]? builtIn = null) =>
        Services(config, suppliers, environment, clock, builtIn).GetRequiredService<IInferenceRouter>();

    private ServiceProvider Services(
        Dictionary<string, string?>? config = null,
        ISupplierCatalog? suppliers = null,
        string environment = "Production",
        IClock? clock = null,
        IModelProvider[]? builtIn = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock ?? new FakeClock());
        services.AddSingleton<ITenantResidencyReader>(_residencies);
        services.AddSingleton<ITenantInferencePolicies>(_policies);
        services.AddSingleton(suppliers ?? new StubSuppliers(
            (SupplierIds.AwsBedrock, SupplierStatus.Approved),
            (SupplierIds.GoogleVertex, SupplierStatus.Approved),
            (SupplierIds.MicrosoftFoundry, SupplierStatus.Approved),
            (SupplierIds.OperatorHosted, SupplierStatus.Approved)));
        foreach (var provider in new[] { _bedrockEu, _vertexUs, _foundryGlobal, _local })
        {
            services.AddInferenceProvider(provider.Id, _ => provider);
        }

        foreach (var provider in builtIn ?? [])
        {
            services.AddSingleton(provider);
        }

        services.AddLogging();
        services.AddIntelligenceInference(
            new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build(), new HostingEnvironment { EnvironmentName = environment });
        return services.BuildServiceProvider();
    }
}
