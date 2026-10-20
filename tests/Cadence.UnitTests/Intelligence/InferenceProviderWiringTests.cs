using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Inference;
using Cadence.Modules.Intelligence.Inference.Providers;
using Cadence.Modules.Intelligence.Inference.Routing;
using Cadence.Modules.Intelligence.Sandbox;
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
/// The real providers wired through <see cref="InferenceServiceCollectionExtensions.AddIntelligenceInference"/> with
/// recording HTTP handlers in place of the network (ADR-2636): what leaves the process, to which host, and what the
/// sandbox refuses. No request reaches a real endpoint.
/// </summary>
public sealed class InferenceProviderWiringTests : IDisposable
{
    private const string LocalEndpoint = "http://vllm.ai.svc.cluster.local:8000/v1/";
    private static readonly TenantId Tenant = TenantId.New();

    private readonly RecordingHandler _local = new(OpenAiCompletion);
    private readonly RecordingHandler _bedrock = new("{}");
    private readonly RecordingHandler _other = new("{}");
    private readonly StubPolicies _policies = new();

    public void Dispose()
    {
        _local.Dispose();
        _bedrock.Dispose();
        _other.Dispose();
    }

    /// <summary>
    /// A local-only tenant's run goes to the in-deployment endpoint, scrubbed, and Bedrock (configured for the
    /// tenant's region and listed first) receives nothing.
    /// </summary>
    [Fact]
    public async Task LocalOnlyTenant_CallsTheInDeploymentEndpoint_AndNothingElse()
    {
        _policies.Tenants[Tenant] = new TenantInferencePolicy(TenantInferenceMode.LocalOnly);
        var router = Services().GetRequiredService<IInferenceRouter>();

        var routing = await router.ResolveAsync(
            new InferenceRequest(Tenant, [new ModelRoute("bedrock", "claude-opus-5-5"), new ModelRoute("openai-compatible", "claude-opus-5-5")]),
            TestContext.Current.CancellationToken);
        var response = await routing.Client!.GetResponseAsync("Reach me at jane.roe@example.org", cancellationToken: TestContext.Current.CancellationToken);

        response.Text.Should().Be("local answer");
        _bedrock.Requests.Should().BeEmpty();
        var sent = _local.Requests.Should().ContainSingle().Subject;
        sent.Uri.Host.Should().Be("vllm.ai.svc.cluster.local");
        sent.Body.Should().Contain("[EMAIL]").And.NotContain("jane.roe@example.org").And.Contain("\"model\":\"llama-3.3-70b\"");
    }

    /// <summary>An in-deployment endpoint on a public host stops the host at start.</summary>
    [Fact]
    public void PublicEndpointLabelledLocal_FailsAtStart()
    {
        var services = Services(new() { ["Intelligence:Inference:Providers:OpenAiCompatible:Endpoint"] = "https://models.example.com/v1" });

        var build = () => services.GetServices<IInferenceProvider>().ToList();

        build.Should().Throw<InvalidOperationException>().WithMessage("*inside the deployment*models.example.com*");
    }

    /// <summary>A global Vertex endpoint cannot claim a residency.</summary>
    [Fact]
    public void GlobalVertexEndpoint_CannotDeclareAResidency()
    {
        var services = Services(new()
        {
            ["Intelligence:Inference:Providers:Vertex:ProjectId"] = "trial-project",
            ["Intelligence:Inference:Providers:Vertex:Region"] = "global",
            ["Intelligence:Inference:Providers:Vertex:Residency:0"] = "EU",
        });

        var build = () => services.GetServices<IInferenceProvider>().ToList();

        build.Should().Throw<InvalidOperationException>().WithMessage("*global endpoint*");
    }

    /// <summary>Each cloud provider pins the host its SDK calls, so it cannot be pointed elsewhere at run time.</summary>
    [Fact]
    public async Task ProviderClients_ReachOnlyTheirEndpointHosts()
    {
        var services = Services(new()
        {
            ["Intelligence:Inference:Providers:Vertex:ProjectId"] = "trial-project",
            ["Intelligence:Inference:Providers:Vertex:Region"] = "europe-west4",
            ["Intelligence:Inference:Providers:Foundry:Resource"] = "cadence-eu",
            ["Intelligence:Inference:Providers:AnthropicAws:Region"] = "eu-west-1",
            ["Intelligence:Inference:Providers:AnthropicAws:WorkspaceId"] = "wrkspc_test",
        });
        var hosts = services.GetServices<IInferenceProvider>().ToDictionary(p => p.Id, p => p.EndpointHosts.Single());

        hosts.Should().Equal(new Dictionary<string, string>
        {
            ["anthropic-aws"] = "aws-external-anthropic.eu-west-1.api.aws",
            ["bedrock"] = "bedrock-mantle.eu-central-1.api.aws",
            ["vertex"] = "europe-west4-aiplatform.googleapis.com",
            ["foundry"] = "cadence-eu.services.ai.azure.com",
            ["openai-compatible"] = "vllm.ai.svc.cluster.local",
        });
        var bedrock = services.GetRequiredService<IHttpClientFactory>().CreateClient(InferenceHttpClients.NameFor("bedrock"));
        var stray = () => bedrock.GetAsync(new Uri("https://collector.example.com/"), TestContext.Current.CancellationToken);
        await stray.Should().ThrowAsync<InferenceEgressRefusedException>();
        _bedrock.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Inside an agent sandbox any other client's request is refused and counted; the same request outside a sandbox
    /// goes through, and the model provider's own client still works inside it.
    /// </summary>
    [Fact]
    public async Task Sandbox_RefusesEveryClientButTheModelPort()
    {
        var services = Services();
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var ct = TestContext.Current.CancellationToken;

        using (var sandbox = AgentSandbox.Enter(Tenant, Guid.NewGuid()))
        {
            var refused = () => factory.CreateClient("other").GetAsync(new Uri("https://registry.example.com/"), ct);
            await refused.Should().ThrowAsync<InferenceEgressRefusedException>();
            sandbox.EgressRefusals.Should().Be(1);
            _other.Requests.Should().BeEmpty();

            var model = await factory.CreateClient(InferenceHttpClients.NameFor("openai-compatible")).GetAsync(new Uri(LocalEndpoint + "models"), ct);
            model.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        AgentSandbox.Current.Should().BeNull();
        (await factory.CreateClient("other").GetAsync(new Uri("https://registry.example.com/"), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A sandboxed run stops at its model-call limit, with the reason.</summary>
    [Fact]
    public async Task Sandbox_StopsARunAtItsModelCallLimit()
    {
        _policies.Tenants[Tenant] = new TenantInferencePolicy(TenantInferenceMode.LocalOnly);
        var routing = await Services().GetRequiredService<IInferenceRouter>().ResolveAsync(
            new InferenceRequest(Tenant, [new ModelRoute("openai-compatible", "claude-opus-5-5")]), TestContext.Current.CancellationToken);

        using var sandbox = AgentSandbox.Enter(Tenant, Guid.NewGuid(), new SandboxLimits(TimeSpan.FromMinutes(1), 1, 1_000));
        await routing.Client!.GetResponseAsync("First step.", cancellationToken: TestContext.Current.CancellationToken);
        var second = () => routing.Client!.GetResponseAsync("Second step.", cancellationToken: TestContext.Current.CancellationToken);

        (await second.Should().ThrowAsync<SandboxLimitExceededException>()).Which.Reason.Should().Be(SandboxStopReason.ModelCalls);
        _local.Requests.Should().ContainSingle();
        sandbox.Tokens.Should().Be(12);
    }

    private ServiceProvider Services(Dictionary<string, string?>? extra = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["Intelligence:Inference:Providers:Bedrock:Region"] = "eu-central-1",
            ["Intelligence:Inference:Providers:Bedrock:Residency:0"] = "EU",
            ["Intelligence:Inference:Providers:OpenAiCompatible:Endpoint"] = LocalEndpoint,
            ["Intelligence:Inference:Providers:OpenAiCompatible:Residency:0"] = "EU",
            ["Intelligence:Inference:Providers:OpenAiCompatible:Models:claude-opus-5-5"] = "llama-3.3-70b",
            ["Intelligence:Inference:Suppliers:operator-hosted:Status"] = "Approved",
            ["Intelligence:Inference:Suppliers:operator-hosted:AssessedBy"] = "QA, Sponsor GmbH",
            ["Intelligence:Inference:Suppliers:operator-hosted:AssessedOn"] = "2026-10-01",
            ["Intelligence:Inference:Suppliers:operator-hosted:NextReviewOn"] = "2027-10-01",
        };
        foreach (var (key, value) in extra ?? [])
        {
            config[key] = value;
        }

        var residencies = new StubResidencies();
        residencies.Tenants[Tenant] = new TenantResidency("EU");
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSingleton<ITenantResidencyReader>(residencies);
        services.AddSingleton<ITenantInferencePolicies>(_policies);
        services.AddIntelligenceInference(
            new ConfigurationBuilder().AddInMemoryCollection(config).Build(), new HostingEnvironment { EnvironmentName = Environments.Production });
        services.AddHttpClient(InferenceHttpClients.NameFor("openai-compatible")).ConfigurePrimaryHttpMessageHandler(() => _local);
        services.AddHttpClient(InferenceHttpClients.NameFor("bedrock")).ConfigurePrimaryHttpMessageHandler(() => _bedrock);
        services.AddHttpClient("other").ConfigurePrimaryHttpMessageHandler(() => _other);
        return services.BuildServiceProvider();
    }

    private const string OpenAiCompletion = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1760000000,"model":"llama-3.3-70b",
         "choices":[{"index":0,"message":{"role":"assistant","content":"local answer"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":10,"completion_tokens":2,"total_tokens":12}}
        """;

    private sealed record RecordedRequest(Uri Uri, string Body);

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
