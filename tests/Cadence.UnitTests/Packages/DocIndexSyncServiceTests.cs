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
    public void ScanMarkdownFiles_ExcludesAdrsIndexShardsBuildArtifactsAndStagedPages_WhileKeepingAuthoredLowercaseSubdirIndex()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);

        File.WriteAllText(Path.Combine(docsDir, "VALID1.md"), "# Valid Doc 1\nSome content\n");

        string adrDir = Path.Combine(docsDir, "adr");
        Directory.CreateDirectory(adrDir);
        File.WriteAllText(Path.Combine(adrDir, "2026-01-01-test.md"), "# Test ADR\nADR content\n");

        string kernelDir = Path.Combine(docsDir, "kernel");
        Directory.CreateDirectory(kernelDir);
        File.WriteAllText(Path.Combine(kernelDir, "INDEX.md"), "# Kernel Shard\n");
        File.WriteAllText(Path.Combine(kernelDir, "SPEC.md"), "# Spec\nContent\n");

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
        File.WriteAllText(Path.Combine(docsDir, "ROOT_INDEX.md"), "# Root Shard\n");
        File.WriteAllText(Path.Combine(docsDir, "empty.md"), "");
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
        File.WriteAllText(Path.Combine(migrationDir, "DOC_INDEX.md"), "# Migration Shard\n");
        File.WriteAllText(Path.Combine(migrationDir, "index.md"), "# Authored migration page\n");

        var files = DocIndexSyncService.ScanMarkdownFiles(docsDir);

        files.Should().HaveCount(3);
        files.Should().Contain(f => f.RelativePath == "VALID1.md");
        files.Should().Contain(f => f.RelativePath == "kernel/SPEC.md");
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
    public void SyncDocIndex_UpdatesOnlyOwningCategoryShardSortedAlphabetically_WhileKeepingStaticHubUnchanged()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        string kernelDir = Path.Combine(docsDir, "kernel");
        Directory.CreateDirectory(kernelDir);

        string hubContent = string.Join(
            '\n',
            "# Documentation Index",
            "",
            "## Category documentation shards",
            "",
            "| Document | Status | What it is |",
            "| --- | --- | --- |",
            "| [Kernel index](kernel/INDEX.md) | Normative | Kernel docs |",
            "| [Root index](ROOT_INDEX.md) | Current | Root docs |",
            "",
            "## Architecture decision records",
            "",
            "| Document | Status | What it is |",
            "| --- | --- | --- |",
            "| [ADR hub](adr/index.md) | Current | Static ADR hub |",
            "| [Legacy ADRs](adr/legacy-index.md) | Historical | Frozen legacy ADRs |",
            "| [Security ADRs](adr/security/index.md) | Current | Security shard |",
            "");

        File.WriteAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"), hubContent);
        File.WriteAllText(
            Path.Combine(docsDir, "ROOT_INDEX.md"),
            "# Root Index\n\n## Developing and running\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Doc A](doca.md) | Current | Description A |\n");
        File.WriteAllText(
            Path.Combine(kernelDir, "INDEX.md"),
            "# Kernel Index\n\n## Kernel and roadmap\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Zeta Spec](ZETA_SPEC.md) | Current | Zeta description |\n");

        File.WriteAllText(Path.Combine(docsDir, "doca.md"), "# Doc A\nDescription A\n");
        File.WriteAllText(Path.Combine(kernelDir, "ZETA_SPEC.md"), "# Zeta Spec\nZeta description\n");
        File.WriteAllText(Path.Combine(kernelDir, "ALPHA_SPEC.md"), "# Alpha Spec\nAlpha description\n");

        // Check fails because kernel/ALPHA_SPEC.md is missing from kernel/INDEX.md.
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);

        // Sync updates only kernel/INDEX.md, inserting ALPHA_SPEC.md before ZETA_SPEC.md, leaving hub and ROOT_INDEX.md untouched.
        string rootShardBefore = File.ReadAllText(Path.Combine(docsDir, "ROOT_INDEX.md"));
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md")).Should().Be(hubContent);
        File.ReadAllText(Path.Combine(docsDir, "ROOT_INDEX.md")).Should().Be(rootShardBefore);

        string kernelShard = File.ReadAllText(Path.Combine(kernelDir, "INDEX.md"));
        int alphaPos = kernelShard.IndexOf("ALPHA_SPEC.md", StringComparison.Ordinal);
        int zetaPos = kernelShard.IndexOf("ZETA_SPEC.md", StringComparison.Ordinal);
        alphaPos.Should().BeGreaterThan(0);
        zetaPos.Should().BeGreaterThan(alphaPos);
    }

    [Fact]
    public void SyncDocIndex_CheckMode_RejectsPerAdrRowsInHubAndOutOfOrderShardRows()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(Path.Combine(docsDir, "doca.md"), "# Doc A\nFirst\n");
        File.WriteAllText(Path.Combine(docsDir, "docb.md"), "# Doc B\nSecond\n");

        // 1. Out-of-order rows in ROOT_INDEX.md fail check mode.
        File.WriteAllText(
            Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"),
            "# Documentation Index\n\n## Category documentation shards\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Root](ROOT_INDEX.md) | Current | Root |\n");
        File.WriteAllText(
            Path.Combine(docsDir, "ROOT_INDEX.md"),
            "# Root Index\n\n## Developing and running\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Doc B](docb.md) | Current | Second |\n| [Doc A](doca.md) | Current | First |\n");

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        // 2. Per-ADR row in DOCUMENTATION_INDEX.md fails check mode.
        File.WriteAllText(
            Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"),
            "# Documentation Index\n\n## Category documentation shards\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Root](ROOT_INDEX.md) | Current | Root |\n\n## Architecture decision records\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [ADR-2508](adr/2026-10-16-pin-pnpm.md) | Current | Per-ADR row forbidden |\n");

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
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
            "# Documentation Index\n\n## Category documentation shards\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Root](ROOT_INDEX.md) | Current | Root |\n");
        File.WriteAllText(
            Path.Combine(docsDir, "ROOT_INDEX.md"),
            "# Root Index\n\n## Developing and running\n\n| Document | Status | What it is |\n| --- | --- | --- |\n" + malformedRows);

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
    public void SyncDocIndex_RefreshesBlogShardRowsOnMetadataChanges_RemovesRevertedDrafts_AndKeepsHubStatic()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        string blogDir = Path.Combine(docsDir, "blog");
        Directory.CreateDirectory(blogDir);

        string hubContent = "# Documentation Index\n\n## Category documentation shards\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Engineering blog index](blog/DOC_INDEX.md) | Current | Blog shard |\n";
        File.WriteAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"), hubContent);

        File.WriteAllText(
            Path.Combine(blogDir, "index.md"),
            "---\ntitle: \"Cadence Engineering Blog\"\nsummary: \"Overview of posts.\"\n---\n\n# Cadence Engineering Blog\n");
        File.WriteAllText(
            Path.Combine(blogDir, "DOC_INDEX.md"),
            "# Engineering Blog Index\n\n## Engineering blog\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Cadence Engineering Blog](index.md) | Current | Overview of posts. |\n");

        string postPath = Path.Combine(blogDir, "2026-11-19-milestone.md");
        File.WriteAllText(
            postPath,
            "---\ntitle: \"Initial Milestone Title\"\ndate: \"2026-11-19\"\nsummary: \"Initial summary text.\"\ndraft: false\n---\n\n# Initial Milestone Title\n");

        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);
        File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md")).Should().Be(hubContent);

        string initialShard = File.ReadAllText(Path.Combine(blogDir, "DOC_INDEX.md"));
        initialShard.Should().Contain("[Initial Milestone Title](2026-11-19-milestone.md)");
        initialShard.Should().Contain("2026-11-19 — Initial summary text.");
        initialShard.IndexOf("2026-11-19-milestone.md", StringComparison.Ordinal)
            .Should().BeLessThan(initialShard.IndexOf("(index.md)", StringComparison.Ordinal));

        // Update title, summary, and date on the existing post: check mode must detect stale metadata
        File.WriteAllText(
            postPath,
            "---\ntitle: \"Revised Milestone Title\"\ndate: \"2026-11-20T12:00:00Z\"\nsummary: \"Revised summary text.\"\ndraft: false\n---\n\n# Initial Milestone Title\n");

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        string refreshedShard = File.ReadAllText(Path.Combine(blogDir, "DOC_INDEX.md"));
        refreshedShard.Should().Contain("[Revised Milestone Title](2026-11-19-milestone.md)");
        refreshedShard.Should().Contain("2026-11-20 — Revised summary text.");
        refreshedShard.Should().NotContain("Initial Milestone Title");

        // Revert post back to draft: true — check mode must fail until the row is removed
        File.WriteAllText(
            postPath,
            "---\ntitle: \"Revised Milestone Title\"\ndate: \"2026-11-20\"\nsummary: \"Revised summary text.\"\ndraft: true\n---\n\n# Initial Milestone Title\n");

        DocIndexSyncService.ScanMarkdownFiles(docsDir).Should().NotContain(f => f.RelativePath == "blog/2026-11-19-milestone.md");
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        string afterRevertShard = File.ReadAllText(Path.Combine(blogDir, "DOC_INDEX.md"));
        afterRevertShard.Should().NotContain("2026-11-19-milestone.md");
        afterRevertShard.Should().Contain("(index.md)");
        File.ReadAllText(Path.Combine(docsDir, "DOCUMENTATION_INDEX.md")).Should().Be(hubContent);
    }

    [Fact]
    public void SyncDocIndex_WriteMode_RemovesIntraTableBlankLinesAndPreservesSeparateTables()
    {
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(Path.Combine(docsDir, "doca.md"), "# Doc A\nFirst doc\n");
        File.WriteAllText(Path.Combine(docsDir, "docb.md"), "# Doc B\nSecond doc\n");
        File.WriteAllText(Path.Combine(docsDir, "docc.md"), "# Doc C\nThird doc\n");

        File.WriteAllText(
            Path.Combine(docsDir, "DOCUMENTATION_INDEX.md"),
            "# Documentation Index\n\n## Category documentation shards\n\n| Document | Status | What it is |\n| --- | --- | --- |\n| [Root](ROOT_INDEX.md) | Current | Root |\n");

        string splitShard = string.Join(
            '\n',
            "# Root Index",
            "",
            "## Developing and running",
            "",
            "| Document | Status | What it is |",
            "| --- | --- | --- |",
            "| [Doc A](doca.md) | Current | First doc |",
            "",
            "| [Doc B](docb.md) | Current | Split by intra-table blank line |",
            "",
            "| Document | Status | What it is |",
            "| --- | --- | --- |",
            "| [Doc C](docc.md) | Current | Separate valid table after blank line |",
            "");

        string shardPath = Path.Combine(docsDir, "ROOT_INDEX.md");
        File.WriteAllText(shardPath, splitShard);

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempDir).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempDir).Should().Be(0);

        string repaired = File.ReadAllText(shardPath);
        repaired.Should().Contain("| [Doc A](doca.md) | Current | First doc |\n| [Doc B](docb.md) | Current | Split by intra-table blank line |");
        repaired.Should().Contain("| [Doc B](docb.md) | Current | Split by intra-table blank line |\n\n| Document | Status | What it is |");
    }

    [Fact]
    public void RepositoryShards_PassSyncDocIndexCheckCleanly()
    {
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: DocIndexSyncService.FindRepoRoot())
            .Should().Be(0);
    }
}
