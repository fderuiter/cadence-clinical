using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class CliDocsParityTests
{
    private const string ProgramSource = """
        config.AddCommand<DoctorCommand>("doctor")
            .WithDescription("Validates system dependencies");
        config.AddCommand<DetectDuplicationCommand>("detect-duplication");
        config.AddBranch("db", db =>
        {
            db.AddCommand<DbResetCommand>("reset");
        });
        """;

    [Fact]
    public void FindUndocumentedCommands_WhenEveryCommandIsDocumented_ReturnsEmpty()
    {
        const string docs = """
            ### `cadence doctor`
            | `detect-duplication` | Finds duplicated code |
            ### `cadence db`
            #### `cadence db reset`
            """;

        CliDocsGeneratorService.FindUndocumentedCommands(ProgramSource, docs).Should().BeEmpty();
    }

    [Fact]
    public void FindUndocumentedCommands_WhenCommandsAreMissing_ReturnsTheirNames()
    {
        const string docs = """
            ### `cadence doctor`
            The doctor command resets nothing and detects duplication nowhere.
            """;

        CliDocsGeneratorService.FindUndocumentedCommands(ProgramSource, docs)
            .Should().BeEquivalentTo("detect-duplication", "db", "reset");
    }
}
