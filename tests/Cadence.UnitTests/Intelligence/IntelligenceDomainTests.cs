using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Runs;
using Cadence.Modules.Intelligence.Evaluation.Sets;
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

    private static readonly TaskConfiguration Governed = Configuration with
    {
        Tools = ["dictionary.search"],
        RetrievalSources = ["meddra"],
        EvaluationSet = new EvaluationSetRef("coding-gold", 1, "sha256:" + new string('c', 64)),
        AcceptanceCriteria = new AcceptanceCriteria(MinimumPassRate: 0.9, MinimumReviewedPassRate: 0.95),
        ApproverRole = "medical_coder",
        Credibility = new CredibilityAssessment(
            "Which MedDRA term fits the verbatim?", "A coder reviews each suggestion before it is applied.", ModelInfluence.Medium, DecisionConsequence.Medium),
    };

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
            Configuration with { Tools = ["dictionary.search"] },
            Configuration with { RetrievalSources = ["meddra"] },
            Configuration with { OutputSchema = "{\"type\":\"object\"}" },
            Configuration with { EvaluationSet = new EvaluationSetRef("coding-gold", 1, "sha256:" + new string('b', 64)) },
            Configuration with { AcceptanceCriteria = new AcceptanceCriteria(0.9, 0.95) },
            Configuration with { ApproverRole = "medical_coder" },
            Governed,
            Governed with { Credibility = Governed.Credibility! with { DecisionConsequence = DecisionConsequence.High } },
            Governed with { AcceptanceCriteria = Governed.AcceptanceCriteria! with { MinimumPassRate = 0.95 } },
        ];

        variants.Select(v => v.ContentHash).Should().OnlyHaveUniqueItems().And.NotContain(Configuration.ContentHash);
        (Configuration with { }).ContentHash.Should().Be(Configuration.ContentHash).And.StartWith("sha256:").And.HaveLength(Cadence.Kernel.Trust.ContentHash.Length);
        Configuration.Ref.Should().Be(new ConfigurationRef(Configuration.Key, Configuration.Version, Configuration.ContentHash));
    }

    /// <summary>The model risk class follows the FDA influence-by-consequence matrix.</summary>
    [Theory]
    [InlineData(ModelInfluence.Low, DecisionConsequence.Low, ModelRiskClass.Low)]
    [InlineData(ModelInfluence.Medium, DecisionConsequence.Low, ModelRiskClass.Low)]
    [InlineData(ModelInfluence.Low, DecisionConsequence.Medium, ModelRiskClass.Low)]
    [InlineData(ModelInfluence.High, DecisionConsequence.Low, ModelRiskClass.Medium)]
    [InlineData(ModelInfluence.Medium, DecisionConsequence.Medium, ModelRiskClass.Medium)]
    [InlineData(ModelInfluence.Low, DecisionConsequence.High, ModelRiskClass.Medium)]
    [InlineData(ModelInfluence.High, DecisionConsequence.Medium, ModelRiskClass.High)]
    [InlineData(ModelInfluence.Medium, DecisionConsequence.High, ModelRiskClass.High)]
    [InlineData(ModelInfluence.High, DecisionConsequence.High, ModelRiskClass.High)]
    public void CredibilityAssessment_RiskClassFollowsTheMatrix(ModelInfluence influence, DecisionConsequence consequence, ModelRiskClass expected) =>
        new CredibilityAssessment("Which code fits?", "Coder reviews each suggestion.", influence, consequence).RiskClass.Should().Be(expected);

    /// <summary>A configuration is qualifiable only with an evaluation set, criteria, a credibility assessment and, when it proposes, an approver role.</summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public void QualificationGaps_NameWhatIsMissing()
    {
        Governed.QualificationGaps().Should().BeEmpty();
        Configuration.QualificationGaps().Should().HaveCount(4);
        (Governed with { ApproverRole = null, Level = AutonomyLevel.Inform }).QualificationGaps().Should().BeEmpty("an Inform task writes no proposals");
        (Governed with { Credibility = Governed.Credibility! with { ContextOfUse = " " } }).QualificationGaps()
            .Should().ContainSingle().Which.Should().Be("State the context of use.");
        (Governed with { Tools = ["dictionary.search", "dictionary.search"] }).Problems().Should().ContainSingle().Which.Should().Contain("listed twice");
        (Governed with { Tools = ["Bad Tool"] }).Problems().Should().ContainSingle().Which.Should().Contain("'Bad Tool'");
    }

    /// <summary>
    /// A qualification makes its configuration usable only while it is qualified and the configuration still has the
    /// qualified content; suspending it, or changing any field, takes it out of use; requalifying restores it.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public void TaskQualification_UsableOnlyWhileQualifiedAndUnchanged()
    {
        var at = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        var evidence = new EvaluationEvidence("eval-run-7", Governed.EvaluationSet!.ContentHash, Passed: true, "{\"accuracy\":0.94}");

        TaskQualification.WhyNotUsable(null, Governed).Should().Contain("is not qualified in this tenant");

        var qualification = TaskQualification.Qualify(Tenant, Governed, evidence, Guid.NewGuid(), "coding-lead", at);
        qualification.Id.Should().Be(TaskQualification.IdFor(Tenant, Governed.Key, Governed.Version));
        qualification.RiskClass.Should().Be(ModelRiskClass.Medium);
        TaskQualification.WhyNotUsable(qualification, Governed).Should().BeNull();

        var changed = Governed with { Instructions = "Suggest {{dictionary}} codes quickly." };
        TaskQualification.WhyNotUsable(qualification, changed).Should().Contain("changed after it was qualified")
            .And.Contain(Governed.ContentHash).And.Contain(changed.ContentHash);
        var requalifyChanged = () => qualification.Requalify(changed, evidence, Guid.NewGuid(), "coding-lead", at);
        requalifyChanged.Should().Throw<InvalidOperationException>();

        qualification.Suspend("Override rate rose to 30%.", at.AddDays(1));
        TaskQualification.WhyNotUsable(qualification, Governed).Should().Contain("is suspended: Override rate rose to 30%.");

        qualification.Requalify(Governed, evidence with { RunReference = "eval-run-8" }, Guid.NewGuid(), "coding-lead", at.AddDays(2));
        TaskQualification.WhyNotUsable(qualification, Governed).Should().BeNull();
        qualification.SuspensionReason.Should().BeNull();
        qualification.EvaluationRunReference.Should().Be("eval-run-8");
    }

    /// <summary>The attestation target id is stable per key and version, and differs between versions.</summary>
    [Fact]
    public void AttestationTargetId_IsStablePerVersion()
    {
        TaskConfiguration.AttestationTargetId("medical_coding.suggest", 1).Should().Be(TaskConfiguration.AttestationTargetId("medical_coding.suggest", 1));
        TaskConfiguration.AttestationTargetId("medical_coding.suggest", 1).Should().NotBe(TaskConfiguration.AttestationTargetId("medical_coding.suggest", 2));
    }

    /// <summary>Malformed configurations are reported, and the catalog refuses them and a version registered twice at startup.</summary>
    [Fact]
    public void Problems_ReportMalformedConfigurations()
    {
        Configuration.Problems().Should().BeEmpty();
        (Configuration with { Key = "Medical Coding" }).Problems().Should().ContainSingle();
        (Configuration with { Version = 0, MaxOutputTokens = 0, Instructions = " " }).Problems().Should().HaveCount(3);

        var duplicate = () => new RegisteredTaskConfigurations([Configuration, Configuration with { Instructions = "Other." }]);
        var catalog = new RegisteredTaskConfigurations([Configuration, Configuration with { Version = 2 }, Configuration with { Version = 3 }]);
        catalog.Versions(Configuration.Key).Select(c => c.Version).Should().Equal(3, 2, 1);
        catalog.Find(Configuration.Key, 2)!.Version.Should().Be(2);
        catalog.Find(Configuration.Key, 4).Should().BeNull();
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
