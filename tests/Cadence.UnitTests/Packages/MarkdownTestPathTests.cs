using System;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Markdown;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>The markdown gate reports paths into .NET test project folders that no longer exist.</summary>
public sealed class MarkdownTestPathTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "markdown-test-paths-" + Guid.NewGuid().ToString("N"))).FullName;

    public MarkdownTestPathTests()
    {
        Write("tests/Cadence.UnitTests/Packages/ThingTests.cs", "namespace X;");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ExistingFilesAndFolders_AreAccepted()
    {
        var doc = Write("docs/adr/a.md",
            "See `tests/Cadence.UnitTests/Packages/ThingTests.cs` and tests/Cadence.UnitTests/Packages/.\n");

        MarkdownParser.ValidateTestProjectPaths(_root, [doc]).Should().BeEmpty();
    }

    [Fact]
    public void AMovedTestFile_IsReportedWithItsLine()
    {
        var doc = Write("docs/adr/a.md", "Intro.\n- `tests/Packages/ThingTests.cs`: the old path.\n");

        var issue = MarkdownParser.ValidateTestProjectPaths(_root, [doc]).Should().ContainSingle().Subject;

        issue.TargetLink.Should().Be("tests/Packages/ThingTests.cs");
        issue.LineNumber.Should().Be(2);
    }

    [Fact]
    public void LowerCaseFoldersPathsInsideAnotherTreeAndGlobs_AreNotChecked()
    {
        var doc = Write("docs/guide.md",
            "A key was committed under tests/fixtures/keys/. The web app keeps apps/web/tests/Views/X.spec.ts.\n" +
            "Globs such as `tests/Cadence.*` and `tests/Cadence.UnitTests/**` name no single path.\n");

        MarkdownParser.ValidateTestProjectPaths(_root, [doc]).Should().BeEmpty();
    }

    [Fact]
    public void MigrationWorkingPapersAndGeneratedReports_AreSkipped()
    {
        var papers = Write("docs/migration/slices/s/03_tickets.md", "`tests/Execution/PlannedTests.cs`\n");
        var rtm = Write("docs/SDLC/Requirements_Traceability_Matrix.md", "`tests/Execution/OldTests.cs`\n");

        MarkdownParser.ValidateTestProjectPaths(_root, new[] { papers, rtm }.ToList()).Should().BeEmpty();
    }

    [Fact]
    public void ValidateWorkspaceLinks_BrokenLinkInNonHistoricalDoc_IsReportedWithLineNumber()
    {
        var doc = Write("docs/guide.md", "Welcome\n- Read [Broken Link](nonexistent/path/file.md)\n");

        var issue = MarkdownParser.ValidateWorkspaceLinks(_root, [doc]).Should().ContainSingle().Subject;

        issue.TargetLink.Should().Be("nonexistent/path/file.md");
        issue.LineNumber.Should().Be(2);
        issue.FilePath.Should().Be("docs/guide.md");
    }

    [Fact]
    public void ValidateWorkspaceLinks_RefactoredDirectoryReference_IsReported()
    {
        var doc = Write("README.md", "# Guide\nSee [Compliance App](apps/compliance/src) for details.\n");

        var issue = MarkdownParser.ValidateWorkspaceLinks(_root, [doc]).Should().ContainSingle().Subject;

        issue.TargetLink.Should().Be("apps/compliance/src");
        issue.LineNumber.Should().Be(2);
    }

    [Fact]
    public void ValidateWorkspaceLinks_HistoricalDocExclusionRules_AllowsPreMigrationTargets()
    {
        var doc = Write("docs/adr/0001-legacy.md", "Historic ADR.\n- [Old Python Service](legacy/old_service.py)\n- [Old ADR](0000-arch.md)\n");

        MarkdownParser.ValidateWorkspaceLinks(_root, [doc]).Should().BeEmpty();
    }

    [Fact]
    public void ValidateMarkdown_HeadingFormatting_DetectsMissingSpace()
    {
        var doc = Write("docs/heading.md", "##HeadingWithoutSpace\nSome content.\n");

        int exitCode = Cadence.ToolingCore.Services.MarkdownValidatorService.ValidateMarkdown([doc], _root);

        exitCode.Should().Be(1);
    }

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
