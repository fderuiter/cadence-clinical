using Cadence.Apps.Execution.Endpoints;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Cec;

public sealed class CecEndpointsTests
{
    [Fact(DisplayName = "ChairAdjudicateAction_FormatsCanonicalEndpointPath")]
    [GxPRequirement("PRD-CEC-002")]
    public void ChairAdjudicateAction_FormatsCanonicalEndpointPath()
    {
        string path = CecEndpoints.ChairAdjudicateAction("DOSSIER-MACE-001");
        path.Should().Be("/api/v1/execution/cec/dossiers/DOSSIER-MACE-001/chair-adjudicate");
    }

    [Fact(DisplayName = "ChairAdjudicateSemanticAction_IsExecutionCecChairAdjudicate")]
    [GxPRequirement("PRD-CEC-002")]
    public void ChairAdjudicateSemanticAction_IsExecutionCecChairAdjudicate()
    {
        CecEndpoints.ChairAdjudicateSemanticAction.Should().Be("execution.cec.chair_adjudicate");
    }
}
