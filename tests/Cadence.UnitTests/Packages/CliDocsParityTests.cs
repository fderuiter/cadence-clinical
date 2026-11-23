using System;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;
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

    [Fact]
    public void GateDocsCheck_OnCurrentRepository_ReturnsTrue()
    {
        GateDocs.Check(FindRepoRoot()).Should().BeTrue();
    }

    [Fact]
    public void GateDocsCheck_WhenSynthesizedRegionIsMutatedOrMissingMarkers_FailsClosed()
    {
        string realRoot = FindRepoRoot();
        string tempRoot = Path.Combine(Path.GetTempPath(), $"gate-docs-parity-{Guid.NewGuid():N}");
        try
        {
            foreach (string relPath in GateDocs.AllRegions.Select(r => r.RelativePath).Append(GateDocs.RelativePath).Distinct(StringComparer.Ordinal))
            {
                string dest = Path.Combine(tempRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(Path.Combine(realRoot, relPath.Replace('/', Path.DirectorySeparatorChar)), dest, overwrite: true);
            }

            // Copy/symlink inputs read by CodeMetadataCatalog during region rendering.
            CopyTree(Path.Combine(realRoot, "docs", "schema"), Path.Combine(tempRoot, "docs", "schema"));
            Directory.CreateSymbolicLink(Path.Combine(tempRoot, "apps"), Path.Combine(realRoot, "apps"));
            Directory.CreateSymbolicLink(Path.Combine(tempRoot, "packages"), Path.Combine(realRoot, "packages"));

            GateDocs.Check(tempRoot).Should().BeTrue();

            string cliDoc = Path.Combine(tempRoot, "docs", "cli.md");
            string originalCli = File.ReadAllText(cliDoc);
            File.WriteAllText(cliDoc, originalCli.Replace("8018", "8019", StringComparison.Ordinal));
            GateDocs.Check(tempRoot).Should().BeFalse();

            File.WriteAllText(cliDoc, originalCli.Replace("<!-- check-ports:end -->", "", StringComparison.Ordinal));
            GateDocs.Check(tempRoot).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RenderCommandsDoc_EmitsUnpaddedCommandInventoryWithoutCounts_AndProducesSingleLineDiffOnAddition()
    {
        var commands = CliDocsGeneratorService.ExtractRegisteredCommands(ProgramSource);
        commands.Select(c => c.CommandPath).Should().Equal(
            "cadence doctor",
            "cadence detect-duplication",
            "cadence db",
            "cadence db reset");

        string baseline = CliDocsGeneratorService.RenderCommandsDoc(ProgramSource);
        baseline.Should().StartWith("# CLI Commands Inventory\n")
            .And.Contain("| Command | Kind | Description |\n| --- | --- | --- |\n")
            .And.Contain("| `cadence db reset` | Command | Resets the datastores |\n")
            .And.NotContain("commands)");

        string extendedSource = ProgramSource + "\nAddCommand(root, \"verify-pipe\", \"Checks a|b pipes\", DoctorCommand.Configure);";
        string updated = CliDocsGeneratorService.RenderCommandsDoc(extendedSource);

        var baselineLines = baseline.Split('\n');
        var updatedLines = updated.Split('\n');
        updatedLines.Except(baselineLines).Should().Equal("| `cadence verify-pipe` | Command | Checks a\\|b pipes |");
        baselineLines.Except(updatedLines).Should().BeEmpty();
    }

    [Fact]
    public void GenerateCliDocs_WritesAndValidatesStandaloneCliCommandsReference()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), $"cli-docs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRepo, "packages", "cli", "src"));
        Directory.CreateDirectory(Path.Combine(tempRepo, "docs"));
        try
        {
            string expectedPorts = PortCheckerService.FormatPortRanges(PortCheckerService.DefaultPorts);
            File.WriteAllText(Path.Combine(tempRepo, "packages", "cli", "src", "Program.cs"), ProgramSource);
            File.WriteAllText(
                Path.Combine(tempRepo, "docs", "cli.md"),
                $"# Cadence Unified Developer CLI Reference\n\n`cadence doctor` `cadence check` `cadence fix` `cadence gxp` ({expectedPorts})\n");

            CliDocsGeneratorService.GenerateCliDocs(check: false, repoRoot: tempRepo).Should().Be(0);
            File.Exists(Path.Combine(tempRepo, "docs", "cli-commands.md")).Should().BeTrue();
            CliDocsGeneratorService.GenerateCliDocs(check: true, repoRoot: tempRepo).Should().Be(0);

            File.AppendAllText(Path.Combine(tempRepo, "docs", "cli-commands.md"), "| `cadence stale` | Command | Stale |\n");
            CliDocsGeneratorService.GenerateCliDocs(check: true, repoRoot: tempRepo).Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempRepo, recursive: true);
        }
    }

    private static void CopyTree(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Cadence.sln not found.");
    }
}
