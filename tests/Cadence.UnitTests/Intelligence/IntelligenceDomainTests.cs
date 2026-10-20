using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Infrastructure.Models;
using Cadence.Modules.Intelligence.Infrastructure.Tasks;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// The Intelligence module's domain: task configuration identity (K-AI-5), prompt templates, the ledger entry and its
/// integrity hash, controls, the fake provider and the provider registry.
/// </summary>
public sealed class IntelligenceDomainTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0f6a3b1e-2222-4c2d-9e8f-000000000002"));

    private static readonly TaskConfiguration Configuration = new(
        "medical_coding.suggest", 1, "anthropic", "tier:balanced", "Suggest {{dictionary}} codes.", 1000, AutonomyLevel.ApproveToExecute);

    /// <summary>
    /// K-AI-5: the qualified unit is the whole configuration, so changing the prompt, the model, the provider, the
    /// limit or the level changes its content hash, not just changing the model id.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public void ContentHash_ChangesWhenAnyFieldChanges()
    {
        TaskConfiguration[] variants =
        [
            Configuration with { Instructions = "Suggest {{dictionary}} codes, with the path." },
            Configuration with { Model = "tier:deep" },
            Configuration with { Provider = "fake" },
            Configuration with { MaxOutputTokens = 1001 },
            Configuration with { Level = AutonomyLevel.Draft },
            Configuration with { Version = 2 },
            Configuration with { Key = "medical_coding.suggest_who" },
        ];

        variants.Select(v => v.ContentHash).Should().OnlyHaveUniqueItems().And.NotContain(Configuration.ContentHash);
        (Configuration with { }).ContentHash.Should().Be(Configuration.ContentHash).And.HaveLength(64);
    }

    /// <summary>Malformed configurations are reported, and the catalog refuses them and duplicate keys at startup.</summary>
    [Fact]
    public void Problems_ReportMalformedConfigurations()
    {
        Configuration.Problems().Should().BeEmpty();
        (Configuration with { Key = "Medical Coding" }).Problems().Should().ContainSingle();
        (Configuration with { Version = 0, MaxOutputTokens = 0, Instructions = " " }).Problems().Should().HaveCount(3);

        var duplicate = () => new RegisteredTaskConfigurations([Configuration, Configuration with { Version = 2 }]);
        var malformed = () => new RegisteredTaskConfigurations([Configuration with { Provider = "" }]);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*registered more than once*");
        malformed.Should().Throw<InvalidOperationException>().WithMessage("*Name the provider*");
    }

    /// <summary>Templates render strictly: every placeholder needs a value and every value a placeholder.</summary>
    [Fact]
    public void PromptTemplate_RendersStrictly()
    {
        PromptTemplate.Render("Code {{term}} with {{dictionary}}; {{term}} again.", new Dictionary<string, string>
        {
            ["term"] = "headache",
            ["dictionary"] = "MedDRA",
        }).Should().Be("Code headache with MedDRA; headache again.");

        var missing = () => PromptTemplate.Render("Code {{term}}.", new Dictionary<string, string>());
        var unused = () => PromptTemplate.Render("Code it.", new Dictionary<string, string> { ["term"] = "x" });
        missing.Should().Throw<ArgumentException>().WithMessage("*missing term; unused none*");
        unused.Should().Throw<ArgumentException>().WithMessage("*missing none; unused term*");
    }

    /// <summary>
    /// The ledger entry records the configuration version and hash, completes once, and its content hash detects a
    /// changed field; timestamps and cost are held at the precision PostgreSQL stores, so a re-read entry still verifies.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public void AgentRun_CompletesOnce_AndItsHashDetectsChanges()
    {
        var started = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(2)).AddTicks(7);
        var run = AgentRun.Start(Guid.CreateVersion7(), Tenant, Configuration, "claude-sonnet-5-5", "person-1", "[]", started);

        run.TaskVersion.Should().Be(1);
        run.ConfigurationHash.Should().Be(Configuration.ContentHash);
        run.StartedAt.Should().Be(new DateTimeOffset(2026, 10, 20, 7, 0, 0, TimeSpan.Zero), "stored in UTC at whole microseconds");
        run.ContentHash.Should().BeNull();

        run.Succeed("Suggested 10019211.", 120, 30, 0.0012345678m, started.AddSeconds(3));

        run.Status.Should().Be(AgentRunStatus.Succeeded);
        run.CostUsd.Should().Be(0.001235m);
        run.ContentHash.Should().Be(run.ComputeContentHash());
        var again = () => run.Fail("late", 0, 0, null, started.AddSeconds(4));
        again.Should().Throw<InvalidOperationException>();

        typeof(AgentRun).GetProperty(nameof(AgentRun.ResponseText))!.SetValue(run, "Suggested 10000000.");
        run.ComputeContentHash().Should().NotBe(run.ContentHash, "an altered answer no longer matches its hash");
    }

    /// <summary>A control's id is derived from the tenant and scope, so the handler finds it without a lookup.</summary>
    [Fact]
    public void AgentControl_IdIsStablePerTenantAndScope()
    {
        AgentControl.IdFor(Tenant, AgentControl.AllTasks).Should().Be(AgentControl.IdFor(Tenant, AgentControl.AllTasks));
        AgentControl.IdFor(Tenant, AgentControl.AllTasks).Should().NotBe(AgentControl.IdFor(Tenant, "medical_coding.suggest"));
        AgentControl.IdFor(new TenantId(Guid.NewGuid()), AgentControl.AllTasks).Should().NotBe(AgentControl.IdFor(Tenant, AgentControl.AllTasks));
        var negative = () => AgentControl.For(Tenant, AgentControl.AllTasks).Set(true, -1);
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>The fake provider answers the same conversation the same way and reports deterministic usage.</summary>
    [Fact]
    public async System.Threading.Tasks.Task FakeChatClient_IsDeterministic()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = new FakeChatClient();
        ChatMessage[] messages = [new(ChatRole.System, "Be brief."), new(ChatRole.User, "Hello")];

        var first = await client.GetResponseAsync(messages, new ChatOptions { ModelId = "fake-fast" }, ct);
        var second = await client.GetResponseAsync(messages, new ChatOptions { ModelId = "fake-fast" }, ct);
        var other = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Bye")], new ChatOptions { ModelId = "fake-fast" }, ct);

        first.Text.Should().StartWith("[fake fake-fast] ").And.Be(second.Text);
        other.Text.Should().NotBe(first.Text);
        first.Usage!.InputTokenCount.Should().Be(FakeChatClient.Tokens("Be brief.Hello"));
        client.GetService<ChatClientMetadata>()!.ProviderName.Should().Be(FakeChatClient.ProviderName);
    }

    /// <summary>Providers resolve tiers, price usage, and are registered once per name.</summary>
    [Fact]
    public void Providers_ResolveTiersAndPrices_AndRefuseDuplicates()
    {
        using var client = new FakeChatClient();
        var provider = new ChatClientModelProvider(
            "fake",
            client,
            new Dictionary<string, string> { ["fast"] = "fake-fast" },
            new Dictionary<string, ModelPrice> { ["fake-fast"] = new() { InputPerMillion = 1m, OutputPerMillion = 4m } });

        provider.ResolveModel("tier:fast").Should().Be("fake-fast");
        provider.ResolveModel("tier:deep").Should().BeNull();
        provider.ResolveModel("claude-opus-5-5").Should().Be("claude-opus-5-5");
        provider.Cost("fake-fast", 1_000_000, 500_000).Should().Be(3m);
        provider.Cost("claude-opus-5-5", 1, 1).Should().BeNull();

        var registry = new ModelProviderRegistry([provider]);
        registry.Find("fake").Should().BeSameAs(provider);
        registry.Find("anthropic").Should().BeNull();
        var duplicate = () => new ModelProviderRegistry([provider, provider]);
        duplicate.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// The fake provider is refused outside Development and Testing, where its answers could pass for a model's; without
    /// an API key no Anthropic provider is configured (no default secret).
    /// </summary>
    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Development", true, true)]
    [InlineData(IntelligenceModule.TestingEnvironment, true, true)]
    [InlineData("Production", false, true)]
    public void AddModule_RefusesTheFakeProviderOutsideDevelopmentAndTesting(string environment, bool fake, bool allowed)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Intelligence:FakeProvider"] = fake ? "true" : "false" })
            .Build();
        var services = new ServiceCollection();

        var add = () => services.AddCadenceIntelligenceModule(configuration, new Environment(environment), "Host=localhost;Database=cadence");

        if (allowed)
        {
            add.Should().NotThrow();
            var providers = services.Where(d => d.ServiceType == typeof(IModelProvider)).Select(d => ((IModelProvider)d.ImplementationInstance!).Name);
            providers.Should().BeEquivalentTo(fake ? [FakeChatClient.ProviderName] : Array.Empty<string>());
        }
        else
        {
            add.Should().Throw<InvalidOperationException>().WithMessage("*FakeProvider*");
        }
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Cadence.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
