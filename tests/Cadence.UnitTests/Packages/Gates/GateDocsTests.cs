using Cadence.Cli.Services;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages.Gates;

public sealed class GateDocsTests
{
    [Fact]
    public void GateTables_InTheRepository_MatchTheRegistry()
    {
        GateDocs.Check(RepositoryRoot.Find()).Should().BeTrue("run 'pnpm cadence generate-cli-docs'");
    }

    [Fact]
    public void ReplaceRegion_RewritesOnlyBetweenTheMarkers_AndRejectsAMissingRegion()
    {
        string document = $"before\n{GateDocs.BeginMarker}\nold\n{GateDocs.EndMarker}\nafter\n";

        GateDocs.ReplaceRegion(document, "new\n").Should()
            .Be($"before\n{GateDocs.BeginMarker}\n\nnew\n\n{GateDocs.EndMarker}\nafter\n");
        GateDocs.ReplaceRegion("no markers\n", "new").Should().BeNull();
    }

    [Fact]
    public void RenderTable_PadsColumnsAndEscapesPipes()
    {
        GateDocs.RenderTable(["A", "Long header"], [["`a|b`", "x"]]).Should().Be(
            "| A      | Long header |\n" +
            "| ------ | ----------- |\n" +
            "| `a\\|b` | x           |\n");
    }
}
