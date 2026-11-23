using System.IO;

using Cadence.ToolingCore.Services;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class CliDocsParityTests
{
    private static readonly string[] SampleCommands = ["doctor", "detect-duplication", "db", "reset"];

    [Fact]
    public void FindUndocumentedCommands_WhenEveryCommandIsDocumented_ReturnsEmpty()
    {
        const string docs = """
            ### `cadence doctor`
            | `detect-duplication` | Finds duplicated code |
            ### `cadence db`
            #### `cadence db reset`
            """;

        CliDocsGeneratorService.FindUndocumentedCommands(SampleCommands, docs).Should().BeEmpty();
    }

    [Fact]
    public void FindUndocumentedCommands_WhenCommandsAreMissing_ReturnsTheirNames()
    {
        const string docs = """
            ### `cadence doctor`
            The doctor command resets nothing and detects duplication nowhere.
            """;

        CliDocsGeneratorService.FindUndocumentedCommands(SampleCommands, docs)
            .Should().BeEquivalentTo("detect-duplication", "db", "reset");
    }

    [Fact]
    public void GetCommandNames_EveryRegisteredCommand_IsDocumentedInCliDocs()
    {
        var commandNames = Cadence.Cli.Program.GetCommandNames();
        commandNames.Should().Contain(["doctor", "check", "fix", "db", "reset", "gxp", "migrations"]);

        string docs = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "docs", "cli.md"));
        CliDocsGeneratorService.FindUndocumentedCommands(commandNames, docs).Should().BeEmpty();
    }
}
