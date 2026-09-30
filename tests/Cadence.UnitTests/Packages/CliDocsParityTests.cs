using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class CliDocsParityTests
{
    private const string ProgramSource = """
        AddCommand(root, "doctor", "Validates system dependencies", DoctorCommand.Configure);
        AddCommand(root, "detect-duplication", "Finds duplicated code", () => DuplicationDetectorService.DetectDuplication());
        var db = AddBranch(root, "db", "Database management");
        AddCommand(db, "reset", "Resets the datastores", DbCommands.Reset);

        private static void AddCommand(Command parent, string name, string description, Func<int> execute) =>
            AddCommand(parent, name, description, command => command.SetAction(_ => execute()));
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
