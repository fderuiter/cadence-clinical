using System.CommandLine;
using System.IO;

using Cadence.Cli;
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
            # Cadence Unified Developer CLI Reference
            cadence doctor
            cadence check
            cadence fix
            cadence setup
            cadence gxp
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

    [Fact]
    public void ValidateCommandTree_WhenEverySubcommandOptionAndAliasIsDocumented_ReturnsNoErrors()
    {
        var root = new RootCommand("Test root");
        var doctor = new Command("doctor", "Validates environment");
        doctor.Options.Add(new Option<bool>("--auto-fix", "-f") { Description = "Auto fix issues" });
        root.Subcommands.Add(doctor);

        const string docs = """
            # Cadence Unified Developer CLI Reference
            cadence doctor
            cadence check
            cadence fix
            cadence setup
            cadence gxp
            ### cadence doctor
            | `-f\|--auto-fix` | Auto fix issues |
            """;

        CliDocsGeneratorService.ValidateCommandTree(root, docs).Should().BeEmpty();
    }

    [Fact]
    public void ValidateCommandTree_WhenOptionIsMissing_ReturnsError()
    {
        var root = new RootCommand("Test root");
        var doctor = new Command("doctor", "Validates environment");
        doctor.Options.Add(new Option<bool>("--json") { Description = "JSON output" });
        root.Subcommands.Add(doctor);

        const string docs = """
            # Cadence Unified Developer CLI Reference
            cadence doctor
            cadence check
            cadence fix
            cadence setup
            cadence gxp
            ### cadence doctor
            Validates environment.
            """;

        var errors = CliDocsGeneratorService.ValidateCommandTree(root, docs);
        errors.Should().Contain(e => e.Contains("--json") && e.Contains("cadence doctor"));
    }

    [Fact]
    public void ValidateCommandTree_WhenOptionAliasIsMissing_ReturnsError()
    {
        var root = new RootCommand("Test root");
        var doctor = new Command("doctor", "Validates environment");
        doctor.Options.Add(new Option<bool>("--auto-fix", "-f") { Description = "Auto fix issues" });
        root.Subcommands.Add(doctor);

        const string docs = """
            # Cadence Unified Developer CLI Reference
            cadence doctor
            cadence check
            cadence fix
            cadence setup
            cadence gxp
            ### cadence doctor
            | `--auto-fix` | Auto fix issues |
            """;

        var errors = CliDocsGeneratorService.ValidateCommandTree(root, docs);
        errors.Should().Contain(e => e.Contains("'-f'") && e.Contains("cadence doctor"));
    }

    [Fact]
    public void ValidateCommandTree_WhenArgumentIsMissing_ReturnsError()
    {
        var root = new RootCommand("Test root");
        var check = new Command("check", "Runs gates");
        check.Arguments.Add(new Argument<string>("gate") { Description = "Gate name" });
        root.Subcommands.Add(check);

        const string docs = """
            # Cadence Unified Developer CLI Reference
            cadence doctor
            cadence check
            cadence fix
            cadence setup
            cadence gxp
            ### cadence check
            Runs quality gates.
            """;

        var errors = CliDocsGeneratorService.ValidateCommandTree(root, docs);
        errors.Should().Contain(e => e.Contains("gate") && e.Contains("cadence check"));
    }

    [Fact]
    public void ValidateCommandTree_WhenDefaultValueIsMissing_ReturnsError()
    {
        var root = new RootCommand("Test root");
        var ci = new Command("ci", "Runs CI");
        ci.Options.Add(new Option<string>("--base")
        {
            Description = "Base ref",
            DefaultValueFactory = _ => "origin/main"
        });
        root.Subcommands.Add(ci);

        const string docs = """
            # Cadence Unified Developer CLI Reference
            cadence doctor
            cadence check
            cadence fix
            cadence setup
            cadence gxp
            ### cadence ci
            | `--base` | Base ref |
            """;

        var errors = CliDocsGeneratorService.ValidateCommandTree(root, docs);
        errors.Should().Contain(e => e.Contains("origin/main") && e.Contains("cadence ci"));
    }

    [Fact]
    public void ValidateCommandTree_WithLiveProgramRootCommand_PassesAgainstCliMd()
    {
        var root = Cadence.Cli.Program.BuildRootCommand();
        string repoRoot = Cadence.Cli.Services.QualityGateOrchestrator.FindRepoRoot();
        string cliMdPath = Path.Combine(repoRoot, "docs", "cli.md");
        string docsContent = File.ReadAllText(cliMdPath);

        var errors = CliDocsGeneratorService.ValidateCommandTree(root, docsContent);
        errors.Should().BeEmpty();
    }
}
