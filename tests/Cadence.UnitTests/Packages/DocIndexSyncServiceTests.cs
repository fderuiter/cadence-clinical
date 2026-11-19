using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class DocIndexSyncServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DocIndexSyncServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DocIndexTests_" + Guid.NewGuid().ToString("N"));
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
            catch (Exception)
            {
                // Ignore cleanup errors in temporary test directory
            }
        }
    }

    [Fact]
    public void ScanMarkdownFiles_ExcludesBuildArtifactsAndSystemDirs()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);

        File.WriteAllText(Path.Combine(docsDir, "VALID1.md"), "# Valid Doc 1\nSome content\n");

        string adrDir = Path.Combine(docsDir, "adr");
        Directory.CreateDirectory(adrDir);
        File.WriteAllText(Path.Combine(adrDir, "2026-01-01-test.md"), "# Test ADR\nADR content\n");

        string nodeModulesDir = Path.Combine(docsDir, "node_modules");
        Directory.CreateDirectory(nodeModulesDir);
        File.WriteAllText(Path.Combine(nodeModulesDir, "ignored.md"), "# Ignored\n");

        string vitepressDir = Path.Combine(docsDir, ".vitepress");
        Directory.CreateDirectory(vitepressDir);
        File.WriteAllText(Path.Combine(vitepressDir, "ignored.md"), "# Ignored\n");

        string runsDir = Path.Combine(docsDir, "SDLC", "runs");
        Directory.CreateDirectory(runsDir);
        File.WriteAllText(Path.Combine(runsDir, "run_report.md"), "# Run Report\n");

        File.WriteAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"), "# Index\n");
        File.WriteAllText(Path.Combine(docsDir, "empty.md"), ""); // Empty file

        var files = DocIndexSyncService.ScanMarkdownFiles(docsDir);

        files.Should().HaveCount(2);
        files.Should().Contain(f => f.RelativePath == "VALID1.md");
        files.Should().Contain(f => f.RelativePath == "adr/2026-01-01-test.md");
    }

    [Fact]
    public void DetermineCategory_CategorizesPathsCorrectly()
    {
        DocIndexSyncService.DetermineCategory("kernel/SPEC.md").Should().Be("## Kernel and roadmap");
        DocIndexSyncService.DetermineCategory("architecture/repository-migration/MIGRATION_PLAN.md").Should().Be("## Former repository migration program");
        DocIndexSyncService.DetermineCategory("adr/001.md").Should().Be("## Architecture decision records");
        DocIndexSyncService.DetermineCategory("SDLC/01_PRD.md").Should().Be("## Requirements, validation and GxP");
        DocIndexSyncService.DetermineCategory("migration/PHASE0.md").Should().Be("## Python-to-.NET port (completed)");
        DocIndexSyncService.DetermineCategory("specs/01-spec.md").Should().Be("## Design proposals");
        DocIndexSyncService.DetermineCategory("CDISC/README.md").Should().Be("## Standards reference material");
        DocIndexSyncService.DetermineCategory("agents/domain.md").Should().Be("## Repository policies");
        DocIndexSyncService.DetermineCategory("strategy/MARKET_HYPOTHESES.md").Should().Be("## Product");
        DocIndexSyncService.DetermineCategory("FUNCTIONAL_PRODUCT_SPECIFICATION.md").Should().Be("## Product");
        DocIndexSyncService.DetermineCategory("api/designer.md").Should().Be("## Developing and running");
    }

    [Fact]
    public void DetermineStatus_AssignsStatusCorrectly()
    {
        DocIndexSyncService.DetermineStatus("architecture/repository-migration/REPOSITORY_REGISTRY.md", "").Should().Be("Historical");
        DocIndexSyncService.DetermineStatus("architecture/repository-migration/MIGRATION_PLAN.md", "").Should().Be("Historical");
        DocIndexSyncService.DetermineStatus("strategy/PRODUCT_CATALOG.md", "").Should().Be("Current");
        DocIndexSyncService.DetermineStatus("adr/2026-10-28-multi-repository-ownership-and-kernel-package-boundaries.md", "").Should().Be("Current");
        DocIndexSyncService.DetermineStatus("migration/01-old.md", "").Should().Be("Historical");
    }

    [Fact]
    public void ExtractDocMetadata_ExtractsTitleAndDescription()
    {
        string filePath = Path.Combine(_tempDir, "sample.md");
        File.WriteAllText(filePath, "# My Custom Title\n\n> [!WARNING]\n\nThis is the main description paragraph for the document.\n");

        var (title, desc) = DocIndexSyncService.ExtractDocMetadata(filePath, "sample.md");

        title.Should().Be("My Custom Title");
        desc.Should().StartWith("This is the main description paragraph");
    }

    /// <summary>
    /// A description taken from a line with an inline link keeps the link text only, so a truncated
    /// description cannot leave a half link that the docs build reports as broken.
    /// </summary>
    [Fact]
    public void ExtractDocMetadata_ReducesInlineLinksToTheirText()
    {
        string filePath = Path.Combine(_tempDir, "superseded.md");
        string target = "2026-09-02-" + new string('x', 120) + ".md";
        File.WriteAllText(filePath, $"# Old Decision\n\n- Status: Superseded by [ADR-2428]({target})\n");

        var (_, desc) = DocIndexSyncService.ExtractDocMetadata(filePath, "superseded.md");

        desc.Should().Be("- Status: Superseded by ADR-2428");
    }

    [Fact]
    public void SyncDocIndex_CheckMode_FailsWhenUnindexedFileExists()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);

        string indexContent = """
            # Documentation Index

            ## Developing and running

            | Document | Status | What it is |
            | --- | --- | --- |
            | [Doc A](doca.md) | Current | Description A |
            """;

        File.WriteAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"), indexContent);
        File.WriteAllText(Path.Combine(docsDir, "doca.md"), "# Doc A\nDescription A\n");
        File.WriteAllText(Path.Combine(docsDir, "docb.md"), "# Doc B\nDescription B\n");

        // First check: should fail because docb.md is unindexed
        int checkExitCode = DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir);
        checkExitCode.Should().Be(1);

        // Sync: should update DOCUMENTATION_INDEX.md
        int syncExitCode = DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir);
        syncExitCode.Should().Be(0);

        // Second check: should pass cleanly
        int checkExitCode2 = DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir);
        checkExitCode2.Should().Be(0);

        string updatedIndex = File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"));
        updatedIndex.Should().Contain("docb.md");
        updatedIndex.Should().Contain("Doc B");
    }
}
