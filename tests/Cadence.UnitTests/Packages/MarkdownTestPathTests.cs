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

    [Fact]
    public void ValidateWorkspaceLinks_MissingSamePageHeadingAnchor_IsReported()
    {
        var doc = Write("docs/guide.md", "# Guide\n\n## Overview\n\nJump to [Missing](#does-not-exist).\n");

        var issue = MarkdownParser.ValidateWorkspaceLinks(_root, [doc]).Should().ContainSingle().Subject;

        issue.TargetLink.Should().Be("#does-not-exist");
        issue.LineNumber.Should().Be(5);
        issue.FilePath.Should().Be("docs/guide.md");
    }

    [Fact]
    public void ValidateWorkspaceLinks_MissingCrossPageHeadingAnchor_IsReported()
    {
        Write("docs/target.md", "# Target Document\n\n## Known Section\n");
        var source = Write("docs/source.md", "# Source\n\nRead [Target](target.md#unknown-section).\n");

        var issue = MarkdownParser.ValidateWorkspaceLinks(_root, [source]).Should().ContainSingle().Subject;

        issue.TargetLink.Should().Be("target.md#unknown-section");
        issue.LineNumber.Should().Be(3);
        issue.FilePath.Should().Be("docs/source.md");
    }

    [Fact]
    public void ValidateWorkspaceLinks_AcceptsBothPortalAndGitHubHeadingSlugsAndSkipsCodeBlocks()
    {
        var schemaDoc = Write(
            "docs/schema/kernel.md",
            "# Kernel Schema\n\n" +
            "## 15. Roadmap\n\n" +
            "## kernel.activity_entries\n\n" +
            "## How the RTM decides a requirement's state\n\n" +
            "## `packages/ddf`\n");

        var readerDoc = Write(
            "docs/reader.md",
            "# Reader\n\n" +
            "- [GitHub numbered](schema/kernel.md#15-roadmap)\n" +
            "- [VitePress numbered](schema/kernel.md#_15-roadmap)\n" +
            "- [GitHub table](schema/kernel.md#kernelactivity_entries)\n" +
            "- [VitePress table](schema/kernel.md#kernel-activity-entries)\n" +
            "- [Punctuated](schema/kernel.md#how-the-rtm-decides-a-requirements-state)\n" +
            "- [Code heading](schema/kernel.md#packagesddf)\n" +
            "- [`Code label`](#reader)\n" +
            "- Inline code `[ignored](schema/kernel.md#missing-inline)` is skipped.\n\n" +
            "```markdown\n" +
            "[ignored in fence](schema/kernel.md#missing-fence)\n" +
            "```\n");

        MarkdownParser.ValidateWorkspaceLinks(_root, [schemaDoc, readerDoc]).Should().BeEmpty();
    }

    [Fact]
    public void ValidateWorkspaceLinks_WrappedMultiLineLinks_ReportsStartingLineAndSkipsCode()
    {
        Write("docs/target.md", "# Target\n\n## Known Section\n");
        var source = Write(
            "docs/wrapped.md",
            "# Wrapped Links\n\n" +
            "Valid [multi-line\nanchor link](target.md#known-section) and inline `[code](missing-inline.md)`.\n\n" +
            "First broken [wrapped\nfile link](missing-file.md) in prose.\n\n" +
            "```markdown\n[ignored\nfence link](missing-fence.md)\n```\n\n" +
            "Second broken [`wrapped`\nanchor](target.md#missing-section).\n");

        var issues = MarkdownParser.ValidateWorkspaceLinks(_root, [source]);

        issues.Should().HaveCount(2);
        issues[0].TargetLink.Should().Be("missing-file.md");
        issues[0].LineNumber.Should().Be(6);
        issues[1].TargetLink.Should().Be("target.md#missing-section");
        issues[1].LineNumber.Should().Be(14);
    }

    private string Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
