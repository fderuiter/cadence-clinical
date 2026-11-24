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
        foreach (string stagedPage in new[]
                 {
                     "AGENTS.md",
                     "ARCHITECTURE.md",
                     "CONTEXT.md",
                     "CONTRIBUTING.md",
                     "LICENSE.md",
                     "README.md",
                     "SECURITY.md",
                     "SUPPORT.md",
                     "index.md"
                 })
        {
            File.WriteAllText(Path.Combine(docsDir, stagedPage), "# Staged VitePress page\n");
        }

        string migrationDir = Path.Combine(docsDir, "migration");
        Directory.CreateDirectory(migrationDir);
        File.WriteAllText(Path.Combine(migrationDir, "index.md"), "# Authored migration page\n");

        var files = DocIndexSyncService.ScanMarkdownFiles(docsDir);

        files.Should().HaveCount(3);
        files.Should().Contain(f => f.RelativePath == "VALID1.md");
        files.Should().Contain(f => f.RelativePath == "adr/2026-01-01-test.md");
        files.Should().Contain(f => f.RelativePath == "migration/index.md");
    }

    [Fact]
    public void DetermineCategory_CategorizesPathsCorrectly()
    {
        DocIndexSyncService.DetermineCategory("blog/index.md").Should().Be("## Engineering blog");
        DocIndexSyncService.DetermineCategory("blog/2026-11-19-keeping-cadence-in-one-repository.md").Should().Be("## Engineering blog");
        DocIndexSyncService.DetermineCategory("docs/blog/2026-11-19-keeping-cadence-in-one-repository.md").Should().Be("## Engineering blog");
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
    public void ExtractDocMetadata_ReadsYamlFrontmatterSummaryAndTitle_AndSkipsVueTags()
    {
        string filePath = Path.Combine(_tempDir, "2026-11-20-post.md");
        File.WriteAllText(
            filePath,
            "---\ntitle: \"Frontmatter Blog Title\"\nsummary: \"Concise [ADR-2603](../adr/index.md) summary from frontmatter.\"\ndraft: true\n---\n\n<BlogIndex />\n");

        var (title, desc) = DocIndexSyncService.ExtractDocMetadata(filePath, "blog/2026-11-20-post.md");

        title.Should().Be("Frontmatter Blog Title");
        desc.Should().Be("Concise ADR-2603 summary from frontmatter.");
    }

    [Fact]
    public void DetermineStatus_AssignsStatusCorrectly()
    {
        DocIndexSyncService.DetermineStatus("architecture/repository-migration/REPOSITORY_REGISTRY.md", "").Should().Be("Historical");
        DocIndexSyncService.DetermineStatus("architecture/repository-migration/MIGRATION_PLAN.md", "").Should().Be("Historical");
        DocIndexSyncService.DetermineStatus("strategy/PRODUCT_CATALOG.md", "").Should().Be("Current");
        DocIndexSyncService.DetermineStatus("adr/2026-10-28-multi-repository-ownership-and-kernel-package-boundaries.md", "").Should().Be("Current");
        DocIndexSyncService.DetermineStatus(
            "adr/2026-10-28-multi-repository-ownership-and-kernel-package-boundaries.md",
            "# ADR-2602: Multi-Repository Ownership\n\n- **Status:** Partially superseded by [ADR-2603](2026-11-19-keep-cadence-in-one-repository.md)\n")
            .Should().Be("Current");
        DocIndexSyncService.DetermineStatus(
            "adr/2026-08-29-multi-tenant-database-schema-provisioning-and-automated-migration-pipeline.md",
            "# ADR-2237: Multi-Tenant Database Schema Provisioning\n\n> **Superseded Notice:** Superseded by ADR-2250.\n")
            .Should().Be("Historical");
        DocIndexSyncService.DetermineStatus(
            "adr/2026-10-06-published-form-version-registry-and-schema-hash-validation.md",
            "# ADR-2501: Published Form Version Registry\n\n- **Status:** Superseded by ADR-2622\n")
            .Should().Be("Historical");
        DocIndexSyncService.DetermineStatus(
            "adr/2023-01-01-neo4j-graph-database.md",
            "# ADR-2262: Neo4j Graph Database for Clinical Metadata\n\nUse Neo4j and Cypher for clinical graph queries.\n")
            .Should().Be("Historical");
        DocIndexSyncService.DetermineStatus(
            "adr/2023-01-03-pydantic-v2-domain-models.md",
            "# ADR-2264: Pydantic v2 for Domain Models\n\nAll Python FastAPI services use Pydantic v2 and SQLAlchemy.\n")
            .Should().Be("Historical");
        DocIndexSyncService.DetermineStatus(
            "adr/2026-09-29-native-postgresql-protocolversion-snapshots-and-neo4j-retirement.md",
            "# ADR-2245: Native PostgreSQL ProtocolVersion Snapshots and Neo4j Retirement\n\n- **Status:** Accepted\n\nRetires Neo4j in favor of PostgreSQL.\n")
            .Should().Be("Current");
        DocIndexSyncService.DetermineStatus(
            "adr/2026-09-02-migration-to-dotnet-10-architecture-and-standards.md",
            "# ADR-2233: Migration to .NET 10 Architecture and Standards\n\n- **Status:** Accepted\n\nMigrates services from Python FastAPI to .NET 10.\n")
            .Should().Be("Current");
        DocIndexSyncService.DetermineStatus("migration/01-old.md", "").Should().Be("Historical");
    }

    [Fact]
    public void SyncDocIndex_ReconcilesSupersededAndPythonNeo4jAdrStatuses()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        string adrDir = Path.Combine(docsDir, "adr");
        Directory.CreateDirectory(adrDir);

        File.WriteAllText(
            Path.Combine(adrDir, "2023-01-01-neo4j-graph-database.md"),
            "# ADR-2262: Neo4j Graph Database\n\n- **Status:** Superseded by ADR-2245\n\nStore protocol metadata in Neo4j.\n");
        File.WriteAllText(
            Path.Combine(adrDir, "2026-07-22-python-service.md"),
            "# ADR-010: Python FastAPI Service\n\n- **Status:** Accepted\n\nBuild API endpoints with FastAPI and Pydantic.\n");
        File.WriteAllText(
            Path.Combine(adrDir, "2026-09-29-clinical-kernel-modular-monolith-architecture.md"),
            "# ADR-2244: Clinical Kernel Modular Monolith Architecture\n\n- **Status:** Accepted\n\nConsolidate clinical truth in the .NET 10 Clinical Kernel.\n");

        string indexContent = """
            # Documentation Index

            ## Architecture decision records

            | Document | Status | What it is |
            | --- | --- | --- |
            | [ADR-2262](adr/2023-01-01-neo4j-graph-database.md) | Current | Neo4j graph metadata |
            | [ADR-010](adr/2026-07-22-python-service.md) | Current | Python FastAPI service |
            | [ADR-2244](adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) | Current | Clinical Kernel modular monolith |
            """;
        File.WriteAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"), indexContent);

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        string synced = File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"));
        synced.Should().Contain("| [ADR-2262](adr/2023-01-01-neo4j-graph-database.md) | Historical |");
        synced.Should().Contain("| [ADR-010](adr/2026-07-22-python-service.md) | Historical |");
        synced.Should().Contain("| [ADR-2244](adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) | Current |");
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

    [Fact]
    public void ExtractDocMetadata_BalancesTruncatedBackticks_AndSkipsHtmlComments()
    {
        string filePath = Path.Combine(_tempDir, "long-code.md");
        File.WriteAllText(
            filePath,
            "<!-- Generated by `pnpm cadence generate-schema-doc`. Do not edit by hand. -->\n" +
            "# Technical Design\n\n" +
            "Slice: `execution-observations` · Bounded Context: `Cadence.Apps.Execution` · Target Assembly: `apps/execution/src/Cadence.Apps.Execution/Cadence.Apps.Execution.csproj`\n");

        var (_, desc) = DocIndexSyncService.ExtractDocMetadata(filePath, "migration/slices/02_technical_design.md");

        desc.Length.Should().BeLessThanOrEqualTo(120);
        desc.Should().EndWith("...`");
        DocIndexSyncService.FindUnclosedBacktickRun($"| [Technical Design](x.md) | Historical | {desc} |", out _)
            .Should().Be(-1);
    }

    [Theory]
    [InlineData("| [Doc A](doca.md) | Current | Ok |\n\n| [Doc B](docb.md) | Current | Split by blank line |\n")]
    [InlineData("| [Doc A](doca.md) | Current | <!-- Generated by tool -- |\n| [Doc B](docb.md) | Current | Ok |\n")]
    [InlineData("| [Doc A](doca.md) | Current | Truncated `apps/execution/src/Ca... |\n| [Doc B](docb.md) | Current | Ok |\n")]
    public void SyncDocIndex_CheckMode_FailsOnSplitTablesTruncatedCommentsOrOddBackticks(string malformedRows)
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(Path.Combine(docsDir, "doca.md"), "# Doc A\nOk\n");
        File.WriteAllText(Path.Combine(docsDir, "docb.md"), "# Doc B\nOk\n");
        File.WriteAllText(
            Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"),
            "# Documentation Index\n\n## Developing and running\n\n| Document | Status | What it is |\n| --- | --- | --- |\n" + malformedRows);

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
    }

    [Fact]
    public void ScanMarkdownFiles_ExcludesStagedReadmePage()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);

        File.WriteAllText(Path.Combine(docsDir, "README.md"), "# Staged Root README\n");
        File.WriteAllText(Path.Combine(docsDir, "index.md"), "# Guided Landing Page\n");
        File.WriteAllText(Path.Combine(docsDir, "LOCAL_DEV_ENVIRONMENT.md"), "# Local Dev\nDetails\n");

        var files = DocIndexSyncService.ScanMarkdownFiles(docsDir);

        files.Should().ContainSingle(f => f.RelativePath == "LOCAL_DEV_ENVIRONMENT.md");
    }

    [Fact]
    public void ExtractDocMetadata_SupportsYamlFoldedAndLiteralBlockScalars_AndFrontmatterTitlePrecedence()
    {
        string foldedFile = Path.Combine(_tempDir, "2026-11-20-folded.md");
        File.WriteAllText(
            foldedFile,
            """
            ---
            title: >-
              Folded Frontmatter
              Post Title
            summary: >
              First folded sentence
              across two lines.
            draft: false
            ---

            # Different Body Heading
            """);

        var (foldedTitle, foldedDesc) = DocIndexSyncService.ExtractDocMetadata(foldedFile, "blog/2026-11-20-folded.md");
        foldedTitle.Should().Be("Folded Frontmatter Post Title");
        foldedDesc.Should().Be("First folded sentence across two lines.");

        string literalFile = Path.Combine(_tempDir, "2026-11-20-literal.md");
        File.WriteAllText(
            literalFile,
            """
            ---
            title: |
              Literal Post Title
            summary: |-
              Line one of literal summary.
              Line two of literal summary.
            draft: false
            ---

            # Body Heading
            """);

        var (literalTitle, literalDesc) = DocIndexSyncService.ExtractDocMetadata(literalFile, "blog/2026-11-20-literal.md");
        literalTitle.Should().Be("Literal Post Title");
        literalDesc.Should().Be("Line one of literal summary. Line two of literal summary.");
    }

    [Fact]
    public void SyncDocIndex_RefreshesEngineeringBlogRowsOnMetadataChanges_AndRemovesRevertedDrafts()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        string blogDir = Path.Combine(docsDir, "blog");
        Directory.CreateDirectory(blogDir);

        File.WriteAllText(
            Path.Combine(blogDir, "index.md"),
            "---\ntitle: \"Cadence Engineering Blog\"\nsummary: \"Overview of posts.\"\n---\n\n# Cadence Engineering Blog\n");

        string postPath = Path.Combine(blogDir, "2026-11-19-milestone.md");
        File.WriteAllText(
            postPath,
            "---\ntitle: \"Initial Milestone Title\"\ndate: \"2026-11-19\"\nsummary: \"Initial summary text.\"\ndraft: false\n---\n\n# Initial Milestone Title\n");

        File.WriteAllText(
            Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"),
            "# Documentation Index\n\n## Engineering blog\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Cadence Engineering Blog](blog/index.md) | Current | Overview of posts. |\n");

        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        string initialIndex = File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"));
        initialIndex.Should().Contain("[Initial Milestone Title](blog/2026-11-19-milestone.md)");
        initialIndex.Should().Contain("2026-11-19 — Initial summary text.");

        // Update title, summary, and date on the existing post: check mode must detect stale metadata
        File.WriteAllText(
            postPath,
            "---\ntitle: \"Revised Milestone Title\"\ndate: \"2026-11-20T12:00:00Z\"\nsummary: \"Revised summary text.\"\ndraft: false\n---\n\n# Initial Milestone Title\n");

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        string refreshedIndex = File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"));
        refreshedIndex.Should().Contain("[Revised Milestone Title](blog/2026-11-19-milestone.md)");
        refreshedIndex.Should().Contain("2026-11-20 — Revised summary text.");
        refreshedIndex.Should().NotContain("Initial Milestone Title");

        // Revert post back to draft: true — check mode must fail until the row is removed
        File.WriteAllText(
            postPath,
            "---\ntitle: \"Revised Milestone Title\"\ndate: \"2026-11-20\"\nsummary: \"Revised summary text.\"\ndraft: true\n---\n\n# Initial Milestone Title\n");

        DocIndexSyncService.ScanMarkdownFiles(docsDir).Should().NotContain(f => f.RelativePath == "blog/2026-11-19-milestone.md");
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        string afterRevertIndex = File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"));
        afterRevertIndex.Should().NotContain("blog/2026-11-19-milestone.md");
        afterRevertIndex.Should().Contain("blog/index.md");
    }
}
