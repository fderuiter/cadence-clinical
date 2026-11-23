using System;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;
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

        string docs = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "cli.md"));
        CliDocsGeneratorService.FindUndocumentedCommands(commandNames, docs).Should().BeEmpty();
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
            foreach (string relPath in GateDocs.AllRegions.Select(r => r.RelativePath).Distinct(StringComparer.Ordinal))
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

    private static void CopyTree(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string FindRepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);
}
