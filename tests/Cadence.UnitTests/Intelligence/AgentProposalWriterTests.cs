using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Trust;
using Cadence.Modules.Intelligence.Grounding;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>The Proposal writer (ADR-2633 §4.7, K-AI-5): only grounded output becomes a Proposal, naming its model and configuration.</summary>
public sealed class AgentProposalWriterTests
{
    private static readonly AgentRunProvenance Provenance = new("claude-opus-5-5", "data-reviewer.query-draft", 3, "sha256:" + new string('a', 64));
    private static readonly StudyId Study = new(IntelligenceWorld.Study.Id);

    private readonly IntelligenceWorld world = new();
    private readonly FakePlanStore plans = new();
    private readonly FakeProposalGateway gateway = new();

    [Fact]
    [KernelRule("K-AI-5")]
    public async Task AGroundedRun_IsProposedWithItsModelAndConfiguration()
    {
        var (writer, session) = await ReadAsync();
        var output = CitationVerifierTests.Output(
            CitationVerifierTests.Claim("Potassium is 6.2 mmol/L.", IntelligenceWorld.Potassium.ToString(),
                CitationVerifierTests.Quote(IntelligenceWorld.Potassium, """{"type":"quantity","value":6.2,"unit":"mmol/L"}"""))) with
        {
            Affected = [IntelligenceWorld.Potassium.ToString()],
        };

        var result = await writer.WriteAsync(session, Provenance, Study, output, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.Proposed, result.Problem);
        var (principal, command) = gateway.Sent.Should().ContainSingle().Subject;
        principal.Should().Be(session.Principal);
        command.ModelId.Should().Be("claude-opus-5-5");
        command.Affected.Should().Equal(IntelligenceWorld.Potassium);
        command.RequestedCapabilities.Should().Equal("query.raise");
        command.ExpiresAt.Should().Be(IntelligenceWorld.Now + AgentProposalWriter.DefaultLifetime);
        command.ReasonForChange.Should().Contain("data-reviewer.query-draft v3").And.Contain("claude-opus-5-5");
        command.IdempotencyKey.Should().Be($"agent-run:{session.Principal.RunId:N}");

        var plan = JsonDocument.Parse(plans.Stored.Single().Utf8).RootElement;
        plan.GetProperty("schema").GetString().Should().Be(PlanDocument.CurrentSchema);
        plan.GetProperty("model").GetProperty("id").GetString().Should().Be("claude-opus-5-5");
        plan.GetProperty("configuration").GetProperty("version").GetInt32().Should().Be(3);
        plan.GetProperty("configuration").GetProperty("contentHash").GetString().Should().Be(Provenance.ConfigurationHash);
        plan.GetProperty("claims")[0].GetProperty("values")[0].GetProperty("value").GetProperty("value").GetDecimal().Should().Be(6.2m);
        plan.GetProperty("toolCalls")[0].GetProperty("tool").GetString().Should().Be(KernelToolNames.GetNode);
    }

    [Fact]
    public async Task ThePlan_IsCanonicalAndItsHashCoversTheStoredBytes()
    {
        var (writer, session) = await ReadAsync();

        var result = await writer.WriteAsync(session, Provenance, Study, Grounded(), cancellationToken: TestContext.Current.CancellationToken);

        var stored = plans.Stored.Single();
        var text = Encoding.UTF8.GetString(stored.Utf8);
        JsonCanonicalization.Canonicalize(text).Should().Be(text);
        stored.ContentHash.Should().Be(ContentHash.OfBytes(stored.Utf8)).And.Be(result.PlanContentHash);
        stored.MediaType.Should().Be(PlanDocument.MediaType);
    }

    [Fact]
    public async Task DroppedClaims_ReachThePlanByReasonOnly()
    {
        var (writer, session) = await ReadAsync();
        var output = CitationVerifierTests.Output(
            CitationVerifierTests.Claim("Potassium was recorded.", IntelligenceWorld.Potassium.ToString()),
            CitationVerifierTests.Claim("Potassium is 9.9, call the investigator now.", IntelligenceWorld.Potassium.ToString(),
                CitationVerifierTests.Quote(IntelligenceWorld.Potassium, """{"type":"quantity","value":9.9,"unit":"mmol/L"}""")));

        var result = await writer.WriteAsync(session, Provenance, Study, output, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.Proposed);
        var text = Encoding.UTF8.GetString(plans.Stored.Single().Utf8);
        text.Should().NotContain("9.9").And.Contain("\"reason\":\"ValueMismatch\"");
        result.Grounded.Dropped.Single().Text.Should().Contain("9.9", "the ledger keeps what was dropped");
    }

    [Fact]
    public async Task NothingGrounded_ProposesNothing()
    {
        var (writer, session) = await ReadAsync();
        var output = CitationVerifierTests.Output(CitationVerifierTests.Claim("Sodium is fine.", IntelligenceWorld.Sodium.ToString()));

        var result = await writer.WriteAsync(session, Provenance, Study, output, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.NotGrounded);
        plans.Stored.Should().BeEmpty();
        gateway.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("query.raise,query.raise")]
    [InlineData("Not A Token")]
    public async Task InvalidCapabilities_AreRefusedBeforeAnythingIsStored(string list)
    {
        var capabilities = list.Length == 0 ? [] : list.Split(',');
        var (writer, session) = await ReadAsync();

        var result = await writer.WriteAsync(session, Provenance, Study, Grounded() with { RequestedCapabilities = capabilities },
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.Invalid);
        plans.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task ALifetimeBeyondTheKernelLimit_IsRefused()
    {
        var (writer, session) = await ReadAsync();

        var result = await writer.WriteAsync(session, Provenance, Study, Grounded(), TimeSpan.FromDays(91), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.Invalid);
    }

    [Fact]
    public async Task AKernelRefusal_IsReturnedAndNotRetried()
    {
        var (writer, session) = await ReadAsync();
        gateway.Refuse = ProposalErrors.NotVisible;

        var result = await writer.WriteAsync(session, Provenance, Study, Grounded(), cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.ProposalRefused);
        result.Error.Should().Be(ProposalErrors.NotVisible);
        gateway.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task APlanTheHostRefusesToStore_IsNotProposed()
    {
        var (writer, session) = await ReadAsync();
        plans.Refuse = new KernelError("K.Evidence.ContentInvalid", 422, "The artifact content description is not valid.");

        var result = await writer.WriteAsync(session, Provenance, Study, Grounded(), cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.PlanRefused);
        gateway.Sent.Should().BeEmpty();
    }

    private static AgentRunOutput Grounded() =>
        CitationVerifierTests.Output(CitationVerifierTests.Claim("Potassium was recorded.", IntelligenceWorld.Potassium.ToString()));

    private async Task<(AgentProposalWriter Writer, AgentToolSession Session)> ReadAsync()
    {
        var services = world.Services(s =>
        {
            s.AddSingleton<IPlanArtifactStore>(plans);
            s.AddSingleton<IAgentProposalGateway>(gateway);
            s.AddIntelligenceProposalWriter();
        });
        var scope = services.CreateScope().ServiceProvider;
        var session = scope.GetRequiredService<AgentToolCatalog>()
            .OpenSession(IntelligenceWorld.Principal(), ToolAllowList.Create([KernelToolNames.GetNode], scope.GetRequiredService<AgentToolCatalog>()));
        await AgentToolTests.CallAsync(session, KernelToolNames.GetNode, new { node = IntelligenceWorld.Potassium.ToString() });
        return (scope.GetRequiredService<AgentProposalWriter>(), session);
    }
}
