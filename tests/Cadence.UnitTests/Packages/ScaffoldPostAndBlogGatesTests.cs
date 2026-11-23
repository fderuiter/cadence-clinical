using System;
using System.IO;

using Cadence.Cli.Commands;
using Cadence.Testing.Time;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ScaffoldPostAndBlogGatesTests : IDisposable
{
    private readonly string _tempDir;

    public ScaffoldPostAndBlogGatesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ScaffoldPostTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Ignore cleanup races in temp directory
            }
        }
    }

    [Fact]
    public void ScaffoldPost_CreatesDatedMarkdownDraft_AndExcludesDraftFromDocumentationIndex()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(
            Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"),
            "# Documentation Index\n\n## Engineering blog\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Blog](blog/index.md) | Current | Overview |\n");

        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 14, 30, 0, TimeSpan.Zero));
        int exitCode = ScaffoldCommands.ScaffoldPost(
            title: "Ship Kernel \"Phase 3\" Milestones!",
            repoRoot: _tempDir,
            clock: clock,
            author: "fderuiter, @clinical-architect, Clinical Lead",
            tags: ["Kernel", "Architecture, GxP"],
            summary: "How Phase 3 unifies schedule and rule execution.",
            draft: true);

        exitCode.Should().Be(0);

        string expectedPostPath = Path.Combine(docsDir, "blog", "2026-11-20-ship-kernel-phase-3-milestones.md");
        File.Exists(expectedPostPath).Should().BeTrue();

        string postContent = File.ReadAllText(expectedPostPath);
        postContent.Should().Contain("title: \"Ship Kernel \\\"Phase 3\\\" Milestones!\"");
        postContent.Should().Contain("date: \"2026-11-20\"");
        postContent.Should().Contain("  - \"@fderuiter\"");
        postContent.Should().Contain("  - \"@clinical-architect\"");
        postContent.Should().Contain("tags: [\"kernel\", \"architecture\", \"gxp\"]");
        postContent.Should().Contain("draft: true");
        postContent.Should().Contain("# Ship Kernel \"Phase 3\" Milestones!");

        string updatedIndex = File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"));
        updatedIndex.Should().NotContain("blog/2026-11-20-ship-kernel-phase-3-milestones.md");
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);
    }

    [Fact]
    public void ScaffoldPost_SupportsPublishFlag_IndexesPublishedPost_AndRejectsDuplicatesOrEmptySlugs()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(
            Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"),
            "# Documentation Index\n\n## Engineering blog\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Blog](blog/index.md) | Current | Overview |\n");

        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

        ScaffoldCommands.ScaffoldPost("   !!!   ", repoRoot: _tempDir, clock: clock)
            .Should().Be(1, "titles with no alphanumeric characters cannot form a file slug");

        ScaffoldCommands.ScaffoldPost(
            "Immediate Release Note",
            repoRoot: _tempDir,
            clock: clock,
            summary: "Published immediately for subscribers.",
            draft: false)
            .Should().Be(0);

        string postPath = Path.Combine(_tempDir, "docs", "blog", "2026-11-20-immediate-release-note.md");
        File.ReadAllText(postPath).Should().Contain("draft: false");
        File.ReadAllText(postPath).Should().Contain("tags: [\"engineering\"]");

        string updatedIndex = File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"));
        updatedIndex.Should().Contain("blog/2026-11-20-immediate-release-note.md");
        updatedIndex.Should().Contain("2026-11-20 — Published immediately for subscribers.");
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        ScaffoldCommands.ScaffoldPost("Immediate Release Note", repoRoot: _tempDir, clock: clock, draft: false)
            .Should().Be(1, "scaffolding an existing dated blog post must not overwrite it");
    }

    [Fact]
    public void PathPatternValidator_AllowsVitePressThemeVueFiles_AndRejectsVueOutsideBoundaries()
    {
        string themeVue = Path.Combine(_tempDir, "docs", ".vitepress", "theme", "components", "BlogIndex.vue");
        Directory.CreateDirectory(Path.GetDirectoryName(themeVue)!);
        File.WriteAllText(themeVue, "<template><div /></template>\n");

        PathPatternValidatorService.ValidatePathPatterns(
            files: ["docs/.vitepress/theme/components/BlogIndex.vue"],
            repoRoot: _tempDir).Should().Be(0);

        string strayVue = Path.Combine(_tempDir, "docs", "blog", "StrayWidget.vue");
        Directory.CreateDirectory(Path.GetDirectoryName(strayVue)!);
        File.WriteAllText(strayVue, "<template><div /></template>\n");

        PathPatternValidatorService.ValidatePathPatterns(
            files: ["docs/blog/StrayWidget.vue"],
            repoRoot: _tempDir).Should().Be(1);
    }
}
