using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Modules.Intelligence.Grounding;
using Cadence.Modules.Intelligence.Tools;

using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// A whole run with a deterministic, scripted model (no real model in CI): it calls tools through the
/// Microsoft.Extensions.AI functions a provider would see, returns structured output that includes a fabricated value,
/// and only the grounded part becomes a Proposal.
/// </summary>
public sealed class ScriptedAgentRunTests
{
    [Fact]
    public async Task AScriptedRun_ProposesOnlyWhatItRead()
    {
        var world = new IntelligenceWorld();
        var plans = new FakePlanStore();
        var gateway = new FakeProposalGateway();
        var scope = world.Services(s =>
        {
            s.AddSingleton<IPlanArtifactStore>(plans);
            s.AddSingleton<IAgentProposalGateway>(gateway);
            s.AddIntelligenceProposalWriter();
        }).CreateScope().ServiceProvider;
        var catalog = scope.GetRequiredService<AgentToolCatalog>();
        var session = catalog.OpenSession(
            IntelligenceWorld.Principal(), ToolAllowList.Create([KernelToolNames.GetNode, KernelToolNames.TraceLineage], catalog));
        var functions = session.AsAIFunctions().ToDictionary(f => f.Name);

        // The scripted model: read the potassium, trace where it came from, then answer.
        var read = (JsonElement)(await functions[KernelToolNames.GetNode].InvokeAsync(
            new AIFunctionArguments { ["node"] = IntelligenceWorld.Potassium.ToString() }, TestContext.Current.CancellationToken))!;
        await functions[KernelToolNames.TraceLineage].InvokeAsync(
            new AIFunctionArguments { ["node"] = IntelligenceWorld.Potassium.ToString(), ["depth"] = 1 }, TestContext.Current.CancellationToken);
        var shownValue = read.GetProperty("data").GetProperty("observation").GetProperty("value").GetRawText();
        var modelOutput = $$"""
            {
              "intent": "Raise a query: potassium for 017-004 is above the reference range.",
              "claims": [
                { "text": "Potassium is 6.2 mmol/L.", "citations": ["{{IntelligenceWorld.Potassium}}"], "values": [{ "node": "{{IntelligenceWorld.Potassium}}", "value": {{shownValue}} }] },
                { "text": "It was collected under the chemistry panel.", "citations": ["{{IntelligenceWorld.CollectionSpec}}"] },
                { "text": "Sodium is 151 mmol/L.", "citations": ["{{IntelligenceWorld.Sodium}}"], "values": [{ "node": "{{IntelligenceWorld.Sodium}}", "value": { "type": "quantity", "value": 151, "unit": "mmol/L" } }] }
              ],
              "affected": ["{{IntelligenceWorld.Potassium}}"],
              "requestedCapabilities": ["query.raise"]
            }
            """;
        var output = JsonSerializer.Deserialize(modelOutput, IntelligenceToolsJsonContext.Default.AgentRunOutput)!;

        var result = await scope.GetRequiredService<AgentProposalWriter>().WriteAsync(
            session, new AgentRunProvenance("fake-model-1", "data-reviewer.query-draft", 1, "sha256:" + new string('b', 64)),
            new StudyId(IntelligenceWorld.Study.Id), output, cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(ProposalWriteStatus.Proposed, result.Problem);
        result.Grounded.Claims.Select(c => c.Text).Should().Equal("Potassium is 6.2 mmol/L.", "It was collected under the chemistry panel.");
        result.Grounded.Dropped.Should().ContainSingle().Which.Reason.Should().Be(DropReason.CitationNotRead, "the run never read sodium");
        gateway.Sent.Single().Command.ModelId.Should().Be("fake-model-1");
        session.Calls.Select(c => c.Tool).Should().Equal(KernelToolNames.GetNode, KernelToolNames.TraceLineage);
    }
}
