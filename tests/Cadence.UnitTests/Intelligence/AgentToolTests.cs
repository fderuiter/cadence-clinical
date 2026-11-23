using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// The governed kernel read tools (ADR-2633): requester-bound visibility, allow-lists, the untrusted-data envelope and
/// the run's read set.
/// </summary>
public sealed class AgentToolTests
{
    private readonly IntelligenceWorld world = new();

    [Fact]
    public async Task GetNode_ReturnsTheObservationValue_AndRecordsIt()
    {
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Potassium.ToString() });

        var data = result.GetProperty("data");
        data.GetProperty("ref").GetString().Should().Be(IntelligenceWorld.Potassium.ToString());
        data.GetProperty("observation").GetProperty("value").GetProperty("value").GetDecimal().Should().Be(6.2m);
        session.ReadSet.HasRead(IntelligenceWorld.Potassium).Should().BeTrue();
        session.ReadSet.ValueOf(IntelligenceWorld.Potassium).Should().Be("""{"type":"quantity","unit":"mmol/L","value":6.2}""");
    }

    [Fact]
    [KernelRule("K-AI-6")]
    public async Task GetNode_HiddenFromTheRequester_ReadsAsNotFound_AndIsNotRecorded()
    {
        world.Visibility.Hide(IntelligenceWorld.Requester, IntelligenceWorld.Site);
        var session = IntelligenceWorld.Open(world.Services());

        var hidden = await CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Site.ToString() });
        var missing = await CallAsync(session, KernelToolNames.GetNode, new { node = "study_site:01920000-0000-7000-8000-00000000ffff" });

        hidden.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotFoundOrNotVisible);
        hidden.GetProperty("error").GetRawText().Should().Be(missing.GetProperty("error").GetRawText(), "a hidden object and a missing one give the same answer");
        session.ReadSet.HasRead(IntelligenceWorld.Site).Should().BeFalse();
    }

    [Fact]
    public async Task GetNode_HiddenOnlyFromTheAgent_IsAlsoHidden()
    {
        world.Visibility.Hide(IntelligenceWorld.Agent, IntelligenceWorld.Potassium);
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Potassium.ToString() });

        result.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotFoundOrNotVisible);
        session.ReadSet.ValueOf(IntelligenceWorld.Potassium).Should().BeNull();
    }

    [Fact]
    public async Task GetNode_MasksZoomStepsTheAgentMayNotSee()
    {
        world.Visibility.Hide(IntelligenceWorld.Requester, IntelligenceWorld.Participant, MaskReason.IdentityVault);
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Site.ToString() });

        var path = result.GetProperty("data").GetProperty("path").EnumerateArray().ToList();
        path[2].GetProperty("masked").GetString().Should().Be("identity_vault");
        path[2].GetProperty("label").ValueKind.Should().Be(JsonValueKind.Null);
        session.ReadSet.HasRead(IntelligenceWorld.Participant).Should().BeFalse();
    }

    [Fact]
    public async Task Search_LeavesOutHitsTheRequesterMayNotSee()
    {
        world.Visibility.Hide(IntelligenceWorld.Requester, IntelligenceWorld.HiddenSite);
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.SearchKernel, new { text = "site" });

        var hits = result.GetProperty("data").GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("ref").GetString()).ToList();
        hits.Should().Equal(IntelligenceWorld.Site.ToString());
        session.ReadSet.HasRead(IntelligenceWorld.HiddenSite).Should().BeFalse();
    }

    [Fact]
    public async Task ListChildren_MasksHiddenChildrenInPlace_AndRefusesAHiddenParent()
    {
        world.Visibility.Hide(IntelligenceWorld.Agent, IntelligenceWorld.HiddenSite);
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.ListChildren, new { node = IntelligenceWorld.Study.ToString() });

        var children = result.GetProperty("data").GetProperty("children").EnumerateArray().ToList();
        children.Should().HaveCount(2, "a masked child keeps its place");
        children[1].GetProperty("masked").GetString().Should().Be("not_permitted");
        children[1].GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
        children[1].GetProperty("childCount").ValueKind.Should().Be(JsonValueKind.Null);

        world.Visibility.Hide(IntelligenceWorld.Requester, IntelligenceWorld.Study);
        var refused = await CallAsync(session, KernelToolNames.ListChildren, new { node = IntelligenceWorld.Study.ToString() });
        refused.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotFoundOrNotVisible);
    }

    [Fact]
    public async Task TraceLineage_ReadsAsTheRequester_AndMasksWhatOnlyTheAgentMayNotSee()
    {
        world.Visibility.Hide(IntelligenceWorld.Agent, IntelligenceWorld.Site);
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.TraceLineage, new { node = IntelligenceWorld.Potassium.ToString(), depth = 2 });

        world.Kernel.Traces.Should().ContainSingle().Which.Reader.Should().Be(IntelligenceWorld.Requester);
        var data = result.GetProperty("data");
        var nodes = data.GetProperty("nodes").EnumerateArray().ToList();
        nodes.Should().HaveCount(4, "masked objects keep the trace's length");
        nodes.Should().NotContain(n => n.GetProperty("ref").GetString() == IntelligenceWorld.Site.ToString());
        var maskedSite = nodes.Single(n => n.GetProperty("kind").GetString() == NodeKinds.StudySite);
        maskedSite.GetProperty("masked").GetString().Should().Be("not_permitted");
        data.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("from").GetString())
            .Should().Contain(maskedSite.GetProperty("handle").GetString()).And.NotContain(IntelligenceWorld.Site.ToString());
        session.ReadSet.HasRead(IntelligenceWorld.CollectionSpec).Should().BeTrue();
        session.ReadSet.HasRead(IntelligenceWorld.Site).Should().BeFalse();
    }

    [Fact]
    public async Task TraceLineage_RefusesDepthBeyondTheAgentLimit()
    {
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.TraceLineage, new { node = IntelligenceWorld.Potassium.ToString(), depth = 11 });

        result.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.InvalidArguments);
        world.Kernel.Traces.Should().BeEmpty();
    }

    [Fact]
    public async Task Explain_CountsEvidenceThePrincipalMayNotSee()
    {
        world.Visibility.Hide(IntelligenceWorld.Requester, IntelligenceWorld.HiddenSite);
        var services = world.Services(s => s.AddScoped<IExplanationSource, FixedExplanations>());
        var session = IntelligenceWorld.Open(services);

        var result = await CallAsync(session, KernelToolNames.Explain, new { node = IntelligenceWorld.Finding.ToString() });

        var step = result.GetProperty("data").GetProperty("steps")[0];
        step.GetProperty("evidence").EnumerateArray().Select(e => e.GetString()).Should().Equal(IntelligenceWorld.Potassium.ToString());
        step.GetProperty("maskedEvidence").GetInt32().Should().Be(1);
        session.ReadSet.HasRead(IntelligenceWorld.Finding).Should().BeTrue();
    }

    [Fact]
    public async Task CanIAndSimulate_AreNotAvailableUntilTheHostProvidesThem()
    {
        var session = IntelligenceWorld.Open(world.Services());

        var canI = await CallAsync(session, KernelToolNames.CanI, new { capability = "query.raise", node = IntelligenceWorld.Potassium.ToString() });
        var simulate = await CallAsync(session, KernelToolNames.Simulate, new { candidate = "protocol_version:01920000-0000-7000-8000-000000000401" });

        canI.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotAvailable);
        simulate.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotAvailable);
    }

    [Fact]
    public async Task CanI_ReturnsTheHostsExplainedDecision()
    {
        var services = world.Services(s => s.AddScoped<IAgentDryRun, DenyingDryRun>());
        var session = IntelligenceWorld.Open(services, KernelToolNames.CanI);

        var result = await CallAsync(session, KernelToolNames.CanI, new { capability = "observation.correct", node = IntelligenceWorld.Potassium.ToString() });

        var data = result.GetProperty("data");
        data.GetProperty("permitted").GetBoolean().Should().BeFalse();
        data.GetProperty("explanation").GetProperty("conditions")[0].GetProperty("code").GetString().Should().Be("grant.active");
    }

    [Fact]
    public async Task Simulate_ForwardsVersionPins_AndMasksHiddenVersions()
    {
        var recording = new RecordingAgentSimulation();
        var services = world.Services(s => s.AddScoped<IAgentSimulation>(_ => recording));
        var session = IntelligenceWorld.Open(services, KernelToolNames.Simulate);
        var versionId = new ProtocolVersionId(Guid.Parse("01920000-0000-7000-8000-000000000401"));
        var pinnedCandidate = NodeRef.For(versionId, 4);
        var pinnedBase = NodeRef.For(versionId, 3);

        var storedResult = await CallAsync(session, KernelToolNames.Simulate, new { candidate = pinnedCandidate.ToString() });
        storedResult.GetProperty("data").GetProperty("candidate").GetString().Should().Be(pinnedCandidate.ToString());
        recording.Requests.Should().ContainSingle().Which.Candidate.Should().Match<SimulationCandidate>(
            c => c.VersionId == versionId && c.Version == 4);

        var unsavedResult = await CallAsync(
            session,
            KernelToolNames.Simulate,
            new { baseVersion = pinnedBase.ToString(), content = "{\"title\":\"CARDIO-2\"}" });
        unsavedResult.GetProperty("data").GetProperty("baseVersion").GetString().Should().Be(pinnedBase.ToString());
        recording.Requests[^1].Candidate.Should().Match<SimulationCandidate>(
            c => c.BaseVersionId == versionId && c.BaseVersion == 3);

        world.Visibility.Hide(IntelligenceWorld.Requester, NodeRef.For(versionId));
        var hidden = await CallAsync(session, KernelToolNames.Simulate, new { candidate = pinnedCandidate.ToString() });
        hidden.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotFoundOrNotVisible);
        recording.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task EveryResult_IsWrappedAsUntrustedData_EvenWhenItCarriesInstructions()
    {
        var session = IntelligenceWorld.Open(world.Services());

        var result = await CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Study.ToString() });

        result.GetProperty("cadence_tool_result").GetString().Should().Be(KernelToolNames.GetNode);
        result.GetProperty("trust").GetString().Should().Be(ToolDataPolicy.UntrustedData);
        result.GetProperty("notice").GetString().Should().Be(ToolDataPolicy.Notice);
        result.GetProperty("data").GetProperty("title").GetString().Should().StartWith("Ignore previous instructions",
            "the text is passed through as data inside the envelope, never promoted to an instruction");
        result.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("cadence_tool_result", "trust", "notice", "data");
    }

    [Fact]
    public async Task AToolOffTheAllowList_IsNotOffered_AndItsCallIsRefusedAndLogged()
    {
        var session = IntelligenceWorld.Open(world.Services(), KernelToolNames.GetNode);

        session.Tools.Select(t => t.Name).Should().Equal(KernelToolNames.GetNode);
        session.AsAIFunctions().Select(f => f.Name).Should().Equal(KernelToolNames.GetNode);
        var result = await CallAsync(session, KernelToolNames.SearchKernel, new { text = "site" });

        result.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotAllowed);
        session.Calls.Should().ContainSingle().Which.ErrorCode.Should().Be(ToolErrors.NotAllowed);
        session.ReadSet.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void AnAllowList_RefusesUnknownAndRepeatedNames()
    {
        var catalog = world.Services().CreateScope().ServiceProvider.GetRequiredService<AgentToolCatalog>();

        var unknown = () => ToolAllowList.Create(["get_node", "execute_command"], catalog);
        var repeated = () => ToolAllowList.Create(["get_node", "get_node"], catalog);

        unknown.Should().Throw<ArgumentException>().WithMessage("*execute_command*");
        repeated.Should().Throw<ArgumentException>().WithMessage("*get_node*");
        ToolAllowList.None.Names.Should().BeEmpty();
        catalog.OpenSession(IntelligenceWorld.Principal(), ToolAllowList.None).Tools.Should().BeEmpty("an empty list means no tools");
    }

    [Fact]
    public void ATaskConfiguration_NamesItsAllowList()
    {
        var catalog = world.Services().CreateScope().ServiceProvider.GetRequiredService<AgentToolCatalog>();
        var configuration = new TaskConfiguration("data_review.query_draft", 1, "fake", "tier:fast", "Review the data.", 512, AutonomyLevel.Draft)
        {
            Tools = [KernelToolNames.GetNode, KernelToolNames.TraceLineage],
        };

        var allowList = ToolAllowList.For(configuration, catalog);

        allowList.Names.Should().BeEquivalentTo(KernelToolNames.GetNode, KernelToolNames.TraceLineage);
        var unknown = () => ToolAllowList.For(configuration with { Tools = ["execute_command"] }, catalog);
        unknown.Should().Throw<ArgumentException>().WithMessage("*execute_command*");
    }

    [Fact]
    public void TheCatalog_HoldsOnlyReadTools()
    {
        var catalog = world.Services().CreateScope().ServiceProvider.GetRequiredService<AgentToolCatalog>();

        catalog.Names.Should().BeEquivalentTo(KernelToolNames.All);
    }

    [Fact]
    public async Task AIFunctions_RunThroughTheSession()
    {
        var session = IntelligenceWorld.Open(world.Services(), KernelToolNames.GetNode);
        var function = session.AsAIFunctions().Single();

        var result = await function.InvokeAsync(
            new AIFunctionArguments { ["node"] = IntelligenceWorld.Sodium.ToString() }, TestContext.Current.CancellationToken);

        var envelope = result.Should().BeOfType<JsonElement>().Subject;
        envelope.GetProperty("trust").GetString().Should().Be(ToolDataPolicy.UntrustedData);
        session.ReadSet.ValueOf(IntelligenceWorld.Sodium).Should().NotBeNull();
        function.JsonSchema.GetProperty("required")[0].GetString().Should().Be("node");
    }

    [Fact]
    public async Task InvalidArguments_AreRefusedWithoutReading()
    {
        var session = IntelligenceWorld.Open(world.Services());

        var badRef = await CallAsync(session, KernelToolNames.GetNode, new { node = "Study:NOT-A-UUID" });
        var badShape = await CallAsync(session, KernelToolNames.SearchKernel, new { text = 42 });

        badRef.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.InvalidArguments);
        badShape.GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.InvalidArguments);
    }

    [Fact]
    [KernelRule("K-AI-6")]
    public void APrincipal_IsAnAgentActingForAPerson()
    {
        var forService = () => new AgentPrincipal(IntelligenceWorld.Tenant, IntelligenceWorld.Agent, Actor.ServiceAccount("batch"), Guid.NewGuid());
        var notAgent = () => new AgentPrincipal(IntelligenceWorld.Tenant, IntelligenceWorld.Requester, IntelligenceWorld.Requester, Guid.NewGuid());

        forService.Should().Throw<ArgumentException>();
        notAgent.Should().Throw<ArgumentException>();
    }

    internal static async Task<JsonElement> CallAsync(AgentToolSession session, string tool, object arguments) =>
        await session.InvokeAsync(tool, JsonSerializer.SerializeToElement(arguments), TestContext.Current.CancellationToken);

    private sealed class DenyingDryRun : IAgentDryRun
    {
        public ValueTask<AgentDryRunAnswer?> CanIAsync(AgentPrincipal principal, string capability, NodeRef resource, System.Threading.CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentDryRunAnswer?>(new AgentDryRunAnswer(
                true,
                false,
                "No grant covers observation.correct here.",
                new Cadence.Kernel.Explanations.Explanation(
                    resource, "observation.correct is not permitted", "kernel.authorization", [],
                    [Cadence.Kernel.Explanations.EvaluatedCondition.Unsatisfied("grant.active", "Hold a grant", resource)], [])));
    }

    private sealed class RecordingAgentSimulation : IAgentSimulation
    {
        public System.Collections.Generic.List<SimulationRequest> Requests { get; } = [];

        public ValueTask<CommandResult<SimulationResult>?> SimulateAsync(
            AgentPrincipal principal, SimulationRequest request, System.Threading.CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var candidateRef = request.Candidate.VersionId is { } id
                ? NodeRef.For(id, request.Candidate.Version ?? 1)
                : null;
            var baseRef = request.Candidate.BaseVersionId is { } baseId
                ? NodeRef.For(baseId, request.Candidate.BaseVersion ?? 1)
                : null;
            return ValueTask.FromResult<CommandResult<SimulationResult>?>(CommandResult<SimulationResult>.Success(new SimulationResult(
                IntelligenceWorld.Study,
                new SimulationAsOf(IntelligenceWorld.Now, IntelligenceWorld.Now),
                new CandidateSummary(CandidateKind.ProtocolVersion, candidateRef, baseRef, "sha256:cand", "sha256:base"),
                [],
                "sha256:in",
                "sha256:out",
                [],
                [],
                [])));
        }
    }
}
