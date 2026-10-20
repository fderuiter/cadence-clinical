using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Cadence.Compliance;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Behavioural tests for <see cref="RequirementTagScanner"/>, which finds test methods and the requirement IDs
/// they declare. The sample sources are built line by line so this file's own tests never look tagged.
/// </summary>
public class RequirementTagScannerTests
{
    private static string Source(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void ScanSource_FindsFactsAndTheoriesWithNamespaceQualifiedNames()
    {
        var tests = RequirementTagScanner.ScanSource(Source(
            "namespace Sample.Tests;",
            "public class Widget",
            "{",
            "    [Fact]",
            "    public void Plain() { }",
            "",
            "    [Theory]",
            "    [InlineData(1)]",
            "    public async Task WithData(int x) { await Task.Yield(); }",
            "",
            "    public void NotATest() { }",
            "}"), "tests/Widget.cs");

        tests.Select(t => t.FullName).Should().Equal("Sample.Tests.Widget.Plain", "Sample.Tests.Widget.WithData");
        tests[0].File.Should().Be("tests/Widget.cs");
        tests[0].Line.Should().Be(5);
    }

    [Fact]
    public void ScanSource_NamesNestedClassesTheWayTrxDoes()
    {
        var tests = RequirementTagScanner.ScanSource(Source(
            "namespace Sample {",
            "    public class Outer",
            "    {",
            "        public class Inner",
            "        {",
            "            [Fact]",
            "            public void Nested() { }",
            "        }",
            "",
            "        [Fact]",
            "        public void Direct() { }",
            "    }",
            "}"), "tests/Outer.cs");

        tests.Select(t => t.FullName).Should().BeEquivalentTo("Sample.Outer+Inner.Nested", "Sample.Outer.Direct");
    }

    [Fact]
    public void ScanSource_CollectsIdsFromAttributesAndMethodDocComments()
    {
        var tests = RequirementTagScanner.ScanSource(Source(
            "namespace Sample;",
            "public class Tagged",
            "{",
            "    /// <summary>Checks things.",
            "    /// @req:PRD-AAA-001, PRD-AAA-002",
            "    /// </summary>",
            "    [Fact]",
            "    [GxPRequirement(\"PRD-BBB-001\", \"PRD-BBB-002\")]",
            "    [GxPRequirement(\"SRS-CCC-001\")]",
            "    public void Many()",
            "    {",
            // Split so the pre-commit requirement sanitizer, which rewrites staged tags, leaves the sample alone.
            "        /// @" + "req:Trace-7",
            "    }",
            "}"), "tests/Tagged.cs");

        tests.Should().ContainSingle().Which.RequirementIds.Should()
            .BeEquivalentTo("PRD-AAA-001", "PRD-AAA-002", "PRD-BBB-001", "PRD-BBB-002", "SRS-CCC-001", "Trace-7");
    }

    [Fact]
    public void ScanSource_IgnoresClassLevelTagsSoTheyNeverQualifyEveryTestInTheFile()
    {
        var tests = RequirementTagScanner.ScanSource(Source(
            "namespace Sample;",
            "/// <summary>Suite.",
            "/// @req:PRD-AAA-001",
            "/// </summary>",
            "[Trait(\"Requirement\", \"PRD-AAA-002\")]",
            "public class Suite",
            "{",
            "    [Fact]",
            "    public void Untagged() { }",
            "}"), "tests/Suite.cs");

        tests.Should().ContainSingle().Which.RequirementIds.Should().BeEmpty();
    }

    [Fact]
    public void ScanSource_IgnoresTestAttributesInsideCommentsAndStrings()
    {
        var tests = RequirementTagScanner.ScanSource(Source(
            "namespace Sample;",
            "public class Tricky",
            "{",
            "    /// Mark helpers with [Fact] only when they are tests.",
            "    public void Helper() { }",
            "",
            "    // [Fact]",
            "    public void CommentedOut() { }",
            "",
            "    private const string Text = \"[Theory]\";",
            "    public void AfterString() { }",
            "",
            "    [Fact] // pragma: allowlist secret",
            "    public void Real() { }",
            "}"), "tests/Tricky.cs");

        tests.Should().ContainSingle().Which.FullName.Should().Be("Sample.Tricky.Real");
    }

    [Fact]
    public void Scan_ReportsAnAtReqLineOnAClassAsAViolation()
    {
        var scan = RequirementTagScanner.Scan(Source(
            "namespace Sample;",
            "/// <summary>Suite.",
            "/// @" + "req:PRD-AAA-001",
            "/// </summary>",
            "public class Suite",
            "{",
            "    [Fact]",
            "    public void Untagged() { }",
            "}"), "tests/Suite.cs");

        scan.Tests.Should().ContainSingle().Which.RequirementIds.Should().BeEmpty();
        var violation = scan.Violations.Should().ContainSingle().Subject;
        violation.Line.Should().Be(5);
        violation.Message.Should().Contain("Suite").And.Contain("PRD-AAA-001").And.Contain("on the type");
    }

    [Fact]
    public void Scan_ReportsAGxPRequirementAttributeOnANestedRecordAsAViolation()
    {
        var scan = RequirementTagScanner.Scan(Source(
            "namespace Sample;",
            "public class Outer",
            "{",
            "    [GxPRequirement(\"PRD-AAA-001\")]",
            "    private sealed record Helper(int Value)",
            "    {",
            "    }",
            "}"), "tests/Outer.cs");

        scan.Violations.Should().ContainSingle().Which.Message.Should().Contain("Helper declares PRD-AAA-001 on the type");
    }

    [Fact]
    public void Scan_ReportsATraitRequirementAsAnUnsupportedFormAndDoesNotCountIt()
    {
        var scan = RequirementTagScanner.Scan(Source(
            "namespace Sample;",
            "public class Traited",
            "{",
            "    [Fact]",
            "    [Trait(\"Category\", \"GxP_OQ\")]",
            "    [Trait( \"Requirement\", \"PRD-AAA-001\")]",
            "    public void Old() { }",
            "}"), "tests/Traited.cs");

        scan.Tests.Should().ContainSingle().Which.RequirementIds.Should().BeEmpty("a Trait is not a requirement tag");
        var violation = scan.Violations.Should().ContainSingle().Subject;
        violation.Line.Should().Be(6);
        violation.Message.Should().Contain("not a supported requirement tag");
    }

    [Theory]
    [InlineData("tests/Cadence.UnitTests/Interop/StatisticalComputingEnvironmentTests.cs", true)]
    [InlineData("tests/Cadence.UnitTests/Packages/RtmGeneratorServiceTests.cs", true)]
    [InlineData("tests/Cadence.UnitTests/Hooks/AgentHookGuardTests.cs", true)]
    [InlineData("apps/cli/Packages/AgentHookGuardTests.cs", true)]
    [InlineData("tests/Cadence.UnitTests/Packages/AgentHookGuardTests.cs", false)]
    [InlineData("tests/Cadence.UnitTests/Packages/AgentHookCommandsTests.cs", false)]
    public void Scan_ReportsTheAgentHookRequirementOutsideAgentHookTests(string path, bool expectViolation)
    {
        var scan = RequirementTagScanner.Scan(Source(
            "namespace Sample;",
            "public class Hooks",
            "{",
            "    /// <summary>Blocks a destructive command.",
            "    /// @" + "req:PRD-SYS-053",
            "    /// </summary>",
            "    [Fact]",
            "    public void Blocks() { }",
            "}"), path);

        scan.Tests.Should().ContainSingle().Which.RequirementIds.Should().Equal("PRD-SYS-053");
        if (expectViolation)
        {
            scan.Violations.Should().ContainSingle().Which.Message.Should().Contain("Blocks declares PRD-SYS-053");
        }
        else
        {
            scan.Violations.Should().BeEmpty();
        }
    }

    [Fact]
    public void Scan_AcceptsMethodLevelTagsAndIgnoresSampleSourcesInStrings()
    {
        var scan = RequirementTagScanner.Scan(Source(
            "namespace Sample;",
            "/// <summary>A suite with no requirement tag of its own.</summary>",
            "public class Clean",
            "{",
            "    private const string Sample = \"[Trait(\\\"Requirement\\\", \\\"PRD-AAA-002\\\")]\";",
            "",
            "    /// <summary>Checks things.",
            "    /// @" + "req:PRD-AAA-001",
            "    /// </summary>",
            "    [Fact]",
            "    [GxPRequirement(\"PRD-AAA-001\")]",
            "    public void Tagged() { }",
            "}"), "tests/Clean.cs");

        scan.Violations.Should().BeEmpty();
        scan.Tests.Should().ContainSingle().Which.RequirementIds.Should().Equal("PRD-AAA-001");
    }

    [Fact]
    public void ScanSource_DoesNotLeakTagsFromOneTestToTheNext()
    {
        var tests = RequirementTagScanner.ScanSource(Source(
            "namespace Sample;",
            "public class Pair",
            "{",
            "    [Fact]",
            "    [GxPRequirement(\"PRD-AAA-001\")]",
            "    public void First()",
            "    {",
            "        var x = new[] { 1, 2 };",
            "    }",
            "",
            "    [Fact]",
            "    public void Second() { }",
            "}"), "tests/Pair.cs");

        tests.Single(t => t.MethodName == "First").RequirementIds.Should().Equal("PRD-AAA-001");
        tests.Single(t => t.MethodName == "Second").RequirementIds.Should().BeEmpty();
    }

    /// <summary>
    /// Kernel rules count only from <c>[KernelRule("…")]</c> on the method (#5467): a K-ID in the method's doc comment, a
    /// class-level comment or a string is documentation, and a rule never leaks to the next test.
    /// </summary>
    [Fact]
    public void ScanSource_CollectsKernelRulesFromMethodAttributesOnly()
    {
        var tests = RequirementTagScanner.ScanSource(Source(
            "namespace Sample;",
            "/// <summary>Suite proving K-TX-1.</summary>",
            "public class Rules",
            "{",
            "    /// <summary>Validates K-TIME-1.</summary>",
            "    [Fact]",
            "    [KernelRule(\"K-TX-2\")]",
            "    [KernelRuleAttribute(\"K-LIN-2\"), Trait(\"Category\", \"Integration\")]",
            "    [KernelRule(\"K-TX-2\")]",
            "    public void Tagged() { var s = \"[KernelRule(\\\"K-ID-1\\\")]\"; }",
            "",
            "    /// <summary>Validates K-TIME-2.</summary>",
            "    [Theory]",
            "    [InlineData(1)]",
            "    public void Untagged(int x) { }",
            "}"), "tests/Rules.cs");

        tests.Single(t => t.MethodName == "Tagged").KernelRuleIds.Should().Equal("K-LIN-2", "K-TX-2");
        tests.Single(t => t.MethodName == "Untagged").KernelRuleIds.Should().BeEmpty();
        tests.Should().OnlyContain(t => t.RequirementIds.Count == 0);
    }
}

/// <summary>
/// Behavioural tests for how <see cref="RtmGeneratorService"/> reads TRX results and decides whether a
/// requirement is qualified.
/// </summary>
public sealed class RtmGeneratorServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rtm-tests-{Guid.NewGuid():N}");

    public RtmGeneratorServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ParseTestResults_ReadsEveryTrxFileInADirectory()
    {
        WriteTrx("a.trx", ("Sample.A", "One", "Passed"));
        Directory.CreateDirectory(Path.Combine(_dir, "nested"));
        WriteTrx(Path.Combine("nested", "b.trx"), ("Sample.B", "Two", "Failed"));

        var results = RtmGeneratorService.ParseTestResults(_dir, _dir);

        results["Sample.A.One"].Status.Should().Be(TestStatus.Passed);
        results["Sample.B.Two"].Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public void ParseTestResults_MergesTheoryCasesSoAnyFailureWinsAndASkippedCaseIsNeverAPass()
    {
        WriteTrx("theory.trx",
            ("Sample.T", "Cases", "Passed"),
            ("Sample.T", "Cases", "Failed"),
            ("Sample.T", "Cases", "Passed"),
            ("Sample.T", "SkippedThenPassed", "NotExecuted"),
            ("Sample.T", "SkippedThenPassed", "Passed"),
            ("Sample.T", "AllPassed", "Passed"),
            ("Sample.T", "AllPassed", "Passed"));

        var results = RtmGeneratorService.ParseTestResults(_dir, _dir);

        results["Sample.T.Cases"].Status.Should().Be(TestStatus.Failed);
        results["Sample.T.SkippedThenPassed"].Status.Should().Be(TestStatus.Skipped, "every theory case is mandatory");
        results["Sample.T.AllPassed"].Status.Should().Be(TestStatus.Passed);
    }

    [Theory]
    [InlineData("Passed", TestStatus.Passed)]
    [InlineData("Failed", TestStatus.Failed)]
    [InlineData("NotExecuted", TestStatus.Skipped)]
    [InlineData("Inconclusive", TestStatus.NotExecuted)]
    [InlineData("Pending", TestStatus.NotExecuted)]
    [InlineData("", TestStatus.NotExecuted)]
    public void StatusFromOutcome_KeepsPassedFailedSkippedAndNotExecutedApart(string outcome, string expected)
    {
        RtmGeneratorService.StatusFromOutcome(outcome).Should().Be(expected);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("Timeout")]
    [InlineData("Aborted")]
    public void ParseTestResults_TreatsErrorsTimeoutsAndAbortsAsFailures(string outcome)
    {
        WriteTrx("r.trx", ("Sample.E", "Broken", outcome));

        RtmGeneratorService.ParseTestResults(_dir, _dir)["Sample.E.Broken"].Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public void ParseTestResults_ReturnsNothingForAMissingReportInsteadOfAssumingSuccess()
    {
        RtmGeneratorService.ParseTestResults(_dir, Path.Combine(_dir, "missing.trx")).Should().BeEmpty();
    }

    [Fact]
    public void GenerateRtm_WithoutResultsOutsideDraftMode_FailsWithoutWritingDocuments()
    {
        int exitCode = RtmGeneratorService.GenerateRtm(outputDir: "out", reportPath: "missing", repoRoot: _dir);

        exitCode.Should().Be(1);
        File.Exists(Path.Combine(_dir, "out", "Requirements_Traceability_Matrix.md")).Should().BeFalse();
    }

    [Fact]
    public void Classify_QualifiesOnlyWhenEveryMappedTestPassed()
    {
        var mappings = new Dictionary<string, List<TestMappingInfo>>
        {
            ["PRD-A-1"] = [Mapping("S.Pass")],
            ["PRD-A-2"] = [Mapping("S.Pass"), Mapping("S.Fail")],
            ["PRD-A-3"] = [Mapping("S.Skip"), Mapping("S.NeverRan")],
        };
        var results = new Dictionary<string, TestResultInfo>
        {
            ["S.Pass"] = new() { Status = TestStatus.Passed },
            ["S.Fail"] = new() { Status = TestStatus.Failed },
            ["S.Skip"] = new() { Status = TestStatus.Skipped },
        };

        RtmGeneratorService.Classify("PRD-A-1", mappings, results).Should().Be(RequirementState.Qualified);
        RtmGeneratorService.Classify("PRD-A-2", mappings, results).Should().Be(RequirementState.Failed);
        RtmGeneratorService.Classify("PRD-A-3", mappings, results).Should().Be(RequirementState.NotVerified);
        RtmGeneratorService.Classify("PRD-A-4", mappings, results).Should().Be(RequirementState.Planned);
    }

    /// <summary>
    /// Regression for #5324: "at least one passed, none failed" qualified a requirement whose other mapped tests were
    /// skipped or never ran. Every mapped test is mandatory now.
    /// </summary>
    [Fact]
    public void Classify_LeavesARequirementUnverified_WhenOneMandatoryTestSkippedOrDidNotExecute()
    {
        var mappings = new Dictionary<string, List<TestMappingInfo>>
        {
            ["PRD-A-1"] = [Mapping("S.Pass1"), Mapping("S.Pass2"), Mapping("S.Pass3"), Mapping("S.Skip")],
            ["PRD-A-2"] = [Mapping("S.Pass1"), Mapping("S.Pass2"), Mapping("S.NoResult")],
            ["PRD-A-3"] = [Mapping("S.Pass1"), Mapping("S.Inconclusive")],
        };
        var results = new Dictionary<string, TestResultInfo>
        {
            ["S.Pass1"] = new() { Status = TestStatus.Passed },
            ["S.Pass2"] = new() { Status = TestStatus.Passed },
            ["S.Pass3"] = new() { Status = TestStatus.Passed },
            ["S.Skip"] = new() { Status = TestStatus.Skipped },
            ["S.Inconclusive"] = new() { Status = TestStatus.NotExecuted },
        };

        RtmGeneratorService.Classify("PRD-A-1", mappings, results).Should().Be(RequirementState.NotVerified);
        RtmGeneratorService.Classify("PRD-A-2", mappings, results).Should().Be(RequirementState.NotVerified);
        RtmGeneratorService.Classify("PRD-A-3", mappings, results).Should().Be(RequirementState.NotVerified);
    }

    /// <summary>
    /// Regression for #5324: a requirement whose behaviour lives in the database (row-level security, a trigger, an
    /// append-only ledger) was qualified by in-memory and SQLite tests. It now needs a PostgreSQL test among its mapped tests.
    /// </summary>
    [Fact]
    public void Classify_LeavesADatabaseRequirementUnverified_UntilAPostgresTestIsMapped()
    {
        var mappings = new Dictionary<string, List<TestMappingInfo>>
        {
            ["PRD-A-1"] = [Mapping("S.Unit", "tests/Cadence.UnitTests/A/ATests.cs")],
            ["PRD-A-2"] = [Mapping("S.Unit", "tests/Cadence.UnitTests/A/ATests.cs"), Mapping("S.Pg", "tests/Cadence.IntegrationTests/A/APostgresTests.cs")],
            ["PRD-A-3"] = [Mapping("S.Fail", "tests/Cadence.UnitTests/A/ATests.cs")],
        };
        var results = new Dictionary<string, TestResultInfo>
        {
            ["S.Unit"] = new() { Status = TestStatus.Passed },
            ["S.Pg"] = new() { Status = TestStatus.Passed },
            ["S.Fail"] = new() { Status = TestStatus.Failed },
        };

        RtmGeneratorService.Classify("PRD-A-1", mappings, results, requiresDatabase: true).Should().Be(RequirementState.NeedsDatabaseEvidence);
        RtmGeneratorService.Classify("PRD-A-1", mappings, results).Should().Be(RequirementState.Qualified);
        RtmGeneratorService.Classify("PRD-A-2", mappings, results, requiresDatabase: true).Should().Be(RequirementState.Qualified);
        RtmGeneratorService.Classify("PRD-A-3", mappings, results, requiresDatabase: true).Should().Be(RequirementState.Failed);
    }

    [Fact]
    public void ParsePrd_ReadsTheEvidenceLineForItsOwnRequirementOnly()
    {
        string sdlc = Path.Combine(_dir, "sdlc");
        Directory.CreateDirectory(sdlc);
        File.WriteAllText(Path.Combine(sdlc, "prd.md"), string.Join("\n",
            "#### PRD-AAA-001: Isolation",
            "",
            "**Evidence:** PostgreSQL (row-level security).",
            "",
            "Tenants never see each other's rows.",
            "",
            "#### PRD-AAA-002: Rendering",
            "",
            "A pure function."));

        var reqs = RtmGeneratorService.ParsePrd(sdlc);

        reqs["PRD-AAA-001"].RequiresDatabase.Should().BeTrue();
        reqs["PRD-AAA-002"].RequiresDatabase.Should().BeFalse();
    }

    [Fact]
    public void RepositoryPrd_MarksTheTenantIsolationRequirementAsNeedingPostgres()
    {
        string repoRoot = Cadence.ToolingCore.OpenApi.OpenApiToolingService.FindRepoRoot();

        RtmGeneratorService.LoadRequirements(repoRoot)["PRD-CRF-013"].RequiresDatabase.Should().BeTrue();
    }

    /// <summary>
    /// Regression for #5324: a draft or a document with no recorded revision could be committed as the snapshot, and
    /// the <c>rtm</c> gate only checked its stamp.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GenerateRtm_ValidateOnly_RefusesACommittedDraftOrUnrecordedSnapshot(bool draftBanner, bool unrecorded)
    {
        WriteMinimalRepository();
        string committed = Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
        File.WriteAllText(committed, GxPSigner.StampGxpMarkdown("# RTM\n\n" + new RtmEvidenceContext("`0123abc` (clean working tree)", "2026-10-20 00:00:00", "PostgreSQL 18", "test").Render()));
        RtmGeneratorService.GenerateRtm(validateOnly: true, reportPath: ".", repoRoot: _dir, requireCurrentSnapshot: false, requireRecordedEvidence: true).Should().Be(0);

        string body = "# RTM\n\n"
            + (draftBanner ? $"> ⚠️ **{RtmGeneratorService.DraftBanner}** ⚠️\n\n" : "")
            + (unrecorded ? RtmEvidenceContext.NotRecorded.Render() : "");
        File.WriteAllText(committed, GxPSigner.StampGxpMarkdown(body));

        RtmGeneratorService.IsUnverifiedSnapshot(File.ReadAllText(committed)).Should().BeTrue();
        RtmGeneratorService.GenerateRtm(validateOnly: true, reportPath: ".", repoRoot: _dir, requireCurrentSnapshot: false, requireRecordedEvidence: true).Should().Be(1);
    }

    [Fact]
    public void SyncCompliance_RefusesADraftSnapshot()
    {
        Cadence.ToolingCore.Services.SyncGxPService.SyncCompliance(draft: true, snapshot: true, repoRoot: _dir).Should().Be(1);
        File.Exists(Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md")).Should().BeFalse();
    }

    /// <summary>
    /// Regression for #5324 and #5328: an alias (SRS or Trace ID) used to inherit its parent's tests and show
    /// "Verified via Parent". A parent's evidence no longer qualifies a child with its own acceptance criteria, and a
    /// child's tests still never qualify the parent.
    /// </summary>
    [Fact]
    public void GenerateRtm_JudgesAnAliasOnlyByItsOwnTests_NeverByItsParent()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "docs", "SDLC"));
        File.WriteAllText(Path.Combine(_dir, "docs", "SDLC", "prd.md"), "#### PRD-AAA-001: Widget\n#### PRD-AAA-002: Gadget\n");
        File.WriteAllText(Path.Combine(_dir, "docs", "SRS.md"), string.Join("\n",
            "| `SRS-AAA-001` | `PRD-AAA-001` | x | Child of a qualified parent |",
            "| `SRS-AAA-002` | `PRD-AAA-002` | x | Child with its own test |"));
        Directory.CreateDirectory(Path.Combine(_dir, "tests"));
        WriteTaggedTest("Parent", "PRD-AAA-001");
        WriteTaggedTest("Child", "SRS-AAA-002");
        WriteTrx("a.trx", ("Sample.ParentTests", "Parent", "Passed"), ("Sample.ChildTests", "Child", "Passed"));

        RtmGeneratorService.GenerateRtm(reportPath: ".", repoRoot: _dir).Should().Be(0);
        string rtm = File.ReadAllText(Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md"));

        rtm.Should().Contain("| `SRS-AAA-001` | `PRD-AAA-001` | Child of a qualified parent | None | 📋 **Planned** |");
        rtm.Should().Contain("| `SRS-AAA-002` | `PRD-AAA-002` | Child with its own test | `Child` (tests/Child.cs) 🟢 | ✅ **Qualified** |");
        rtm.Should().Contain("| PRD-AAA-002 | PRD | **Gadget** | Sprint Roadmap |", "the child's test does not qualify its parent");
        rtm.Should().NotContain("Verified via Parent");
        RtmGeneratorService.GenerateRtm(validateOnly: true, reportPath: ".", repoRoot: _dir).Should().Be(0, "the RTM lists the alias's own test");
    }

    [Fact]
    public void GenerateRtm_RecordsTheEvidenceContext_AndTheDatabaseTheFixturesReported()
    {
        WriteMinimalRepository();
        WriteTrx("a.trx", ("Sample.FirstTests", "First", "Passed"));
        File.WriteAllLines(Path.Combine(_dir, "kernel.database.txt"),
        [
            "fixture=kernel", "source=CADENCE_TEST_POSTGRES", "server_version=16.10", "extensions=plpgsql 1.0",
            "latest_migration=20261001_Latest", "app_role=NOSUPERUSER NOBYPASSRLS",
        ]);
        var evidence = new RtmEvidenceContext("`abc123` (clean working tree)", "2026-10-15 19:00:00", "", "`pnpm cadence gxp sync` on Linux");

        RtmGeneratorService.GenerateRtm(reportPath: ".", repoRoot: _dir, evidence: evidence).Should().Be(0);

        foreach (var document in new[] { "Requirements_Traceability_Matrix.md", "IQ_OQ_PQ_Execution_Report.md" })
        {
            File.ReadAllText(Path.Combine(_dir, "docs", "SDLC", document)).Should()
                .Contain("- **Tested revision:** `abc123` (clean working tree)")
                .And.Contain("- **Generated (UTC):** 2026-10-15 19:00:00")
                .And.Contain("PostgreSQL 16.10 from CADENCE_TEST_POSTGRES, extensions plpgsql 1.0, migrations through `20261001_Latest`")
                .And.Contain("application role (NOSUPERUSER NOBYPASSRLS)")
                .And.Contain("- **Runner:** `pnpm cadence gxp sync` on Linux");
        }
    }

    [Fact]
    public void GenerateRtm_SaysSo_WhenNoDatabaseFixtureRecordedItsEnvironment()
    {
        WriteMinimalRepository();
        WriteTrx("a.trx", ("Sample.FirstTests", "First", "Passed"));

        RtmGeneratorService.GenerateRtm(reportPath: ".", repoRoot: _dir, evidence: new RtmEvidenceContext("r", "t", "", "x")).Should().Be(0);

        File.ReadAllText(Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md"))
            .Should().Contain("- **Database:** none recorded: no PostgreSQL fixture ran, so no database test executed");
    }

    [Fact]
    public void ComparableContent_IgnoresOnlyTheEvidenceBlockAndTheStamp()
    {
        string Document(RtmEvidenceContext evidence, string body) =>
            GxPSigner.StampGxpMarkdown($"# RTM\n\n{evidence.Render()}\n{body}\n");

        var first = Document(new("`a`", "1", "db", "ci"), "| PRD-A-1 | ✅ **Qualified** |");
        var second = Document(new("`b`", "2", "other db", "sync"), "| PRD-A-1 | ✅ **Qualified** |");
        var changed = Document(new("`a`", "1", "db", "ci"), "| PRD-A-1 | ⚠️ **Not Verified** |");

        RtmEvidenceContext.ComparableContent(first).Should().Be(RtmEvidenceContext.ComparableContent(second));
        RtmEvidenceContext.ComparableContent(first).Should().NotBe(RtmEvidenceContext.ComparableContent(changed));
        GxPSigner.VerifyStamp(second).Should().BeTrue("the stamp still covers the evidence block");
    }

    [Fact]
    public void QualificationReport_CountsSkippedAndNotExecutedSeparately()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "docs", "SDLC"));
        File.WriteAllText(Path.Combine(_dir, "docs", "SDLC", "prd.md"), "#### PRD-AAA-001: Widget\n");
        Directory.CreateDirectory(Path.Combine(_dir, "tests"));
        WriteTaggedTest("Passes", "PRD-AAA-001");
        WriteTaggedTest("Skips", "PRD-AAA-001");
        WriteTaggedTest("NeverRuns", "PRD-AAA-001");
        WriteTrx("a.trx", ("Sample.PassesTests", "Passes", "Passed"), ("Sample.SkipsTests", "Skips", "NotExecuted"));

        RtmGeneratorService.GenerateRtm(reportPath: ".", repoRoot: _dir).Should().Be(0);

        File.ReadAllText(Path.Combine(_dir, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md")).Should()
            .Contain("**Passed:** 1 ").And.Contain("**Skipped:** 1 ").And.Contain("**Not Executed:** 1")
            .And.Contain("⚪ (NOT EXECUTED) NOT EXECUTED").And.Contain("Qualified Requirements: 0 of 1");
    }

    [Fact]
    public void FindUnknownTags_ReportsIdsNoRequirementDocumentDefines()
    {
        var tests = new[]
        {
            new DiscoveredTest("S.Known", "Known", "tests/S.cs", 1, ["PRD-A-1"]),
            new DiscoveredTest("S.Typo", "Typo", "tests/S.cs", 9, ["PRD-A-1", "PRD-A-99"]),
        };
        var requirements = new Dictionary<string, RequirementInfo> { ["PRD-A-1"] = new() { Id = "PRD-A-1" } };

        var unknown = RtmGeneratorService.FindUnknownTags(tests, requirements);

        unknown.Keys.Should().Equal("PRD-A-99");
        unknown["PRD-A-99"].Should().ContainSingle().Which.FullName.Should().Be("S.Typo");
    }

    [Fact]
    public void FindMappingDrift_ReportsTestsMissingFromTheRtmAndTestsNoLongerDeclared()
    {
        string rtm = string.Join("\n",
            "## 2. Requirements with Mapped Tests",
            "",
            "| Requirement ID | Source Document | Title / Description | Mapped Test Cases | Status |",
            "| :--- | :--- | :--- | :--- | :--- |",
            "| PRD-A-1 | PRD | **Pipes | and `code` (x) in a title** | `Kept` (tests/S.cs) 🟢<br>`Removed` (tests/S.cs) 🟢 | ✅ **Qualified** |",
            "",
            "## 3. Planned Requirements (No Mapped Tests)",
            "",
            "| PRD-A-2 | PRD | **Planned** | Sprint Roadmap |");
        var tests = new[]
        {
            new DiscoveredTest("S.Kept", "Kept", "tests/S.cs", 1, ["PRD-A-1"]),
            new DiscoveredTest("S.Added", "Added", "tests/S.cs", 5, ["PRD-A-2"]),
            new DiscoveredTest("S.Removed", "Removed", "tests/S.cs", 9, []),
        };
        var requirements = new Dictionary<string, RequirementInfo>
        {
            ["PRD-A-1"] = new() { Id = "PRD-A-1" },
            ["PRD-A-2"] = new() { Id = "PRD-A-2" },
        };

        var drift = RtmGeneratorService.FindMappingDrift(rtm, tests, requirements);

        drift.Should().BeEquivalentTo(
            "missing from the RTM: PRD-A-2 <- Added (tests/S.cs)",
            "listed in the RTM but no longer declared: PRD-A-1 <- Removed (tests/S.cs)");
    }

    [Fact]
    public void FindMappingDrift_ReadsAliasTestsFromSection4_AndIgnoresUnknownIds()
    {
        var tests = new[] { new DiscoveredTest("S.T", "T", "tests/S.cs", 1, ["SRS-A-1", "PRD-NOPE-1"]) };
        var requirements = new Dictionary<string, RequirementInfo>
        {
            ["SRS-A-1"] = new() { Id = "SRS-A-1", ParentPrdId = "PRD-A-1", IsLegacyAlias = true },
        };
        string rtm = string.Join("\n",
            "## 2. Requirements with Mapped Tests",
            "",
            "## 4. Traceability Aliases (Trace and SRS IDs)",
            "",
            "| Alias ID | Canonical Parent PRD | Requirement Summary | Mapped Test Cases | Status |",
            "| :--- | :--- | :--- | :--- | :--- |",
            "| `SRS-A-1` | `PRD-A-1` | Summary | `T` (tests/S.cs) 🟢 | ✅ **Qualified** |");

        RtmGeneratorService.FindMappingDrift(rtm, tests, requirements).Should().BeEmpty();
        RtmGeneratorService.FindMappingDrift("## 2. Requirements with Mapped Tests\n", tests, requirements)
            .Should().Equal("missing from the RTM: SRS-A-1 <- T (tests/S.cs)");
    }

    [Fact]
    public void ValidateRtm_FailsOnceATestTagChangesWithoutRegeneratingTheRtm()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "docs", "SDLC"));
        File.WriteAllText(Path.Combine(_dir, "docs", "SDLC", "prd.md"), "#### PRD-AAA-001: Widget\n#### PRD-AAA-002: Gadget\n");
        Directory.CreateDirectory(Path.Combine(_dir, "tests"));
        WriteTaggedTest("First", "PRD-AAA-001");

        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir).Should().Be(0);

        WriteTaggedTest("Second", "PRD-AAA-002");

        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir).Should().Be(1);
    }

    [Fact]
    public void GenerateRtm_TwiceFromTheSameTests_WritesIdenticalDocuments()
    {
        WriteMinimalRepository();
        string rtm = Path.Combine(_dir, "docs", "SDLC", "Requirements_Traceability_Matrix.md");
        string report = Path.Combine(_dir, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md");

        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        var firstRtm = File.ReadAllText(rtm);
        var firstReport = File.ReadAllText(report);
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);

        File.ReadAllText(rtm).Should().Be(firstRtm);
        File.ReadAllText(report).Should().Be(firstReport);
        GxPSigner.VerifyStamp(firstRtm).Should().BeTrue();
        GxPSigner.VerifyStamp(firstReport).Should().BeTrue();
    }

    [Fact]
    public void QualificationReport_CountsOnlyTaggedTests_SoUntaggedTestsLeaveItUnchanged()
    {
        WriteMinimalRepository();
        WriteTrx("a.trx", ("Sample.FirstTests", "First", "Passed"));
        string report = Path.Combine(_dir, "docs", "SDLC", "IQ_OQ_PQ_Execution_Report.md");

        RtmGeneratorService.GenerateRtm(reportPath: ".", repoRoot: _dir).Should().Be(0);
        string before = File.ReadAllText(report);
        WriteTrx("b.trx", ("Sample.Untagged", "Passes", "Passed"), ("Sample.Untagged", "IsSkipped", "NotExecuted"));
        RtmGeneratorService.GenerateRtm(reportPath: ".", repoRoot: _dir).Should().Be(0);

        File.ReadAllText(report).Should().Be(before);
        before.Should().Contain("**Requirement-Tagged Test Methods:** 1").And.Contain("**Passed:** 1 ");
    }

    [Theory]
    [InlineData("Requirements_Traceability_Matrix.md")]
    [InlineData("IQ_OQ_PQ_Execution_Report.md")]
    public void ValidateRtm_FailsWhenAGeneratedDocumentIsEditedByHand(string document)
    {
        WriteMinimalRepository();
        RtmGeneratorService.GenerateRtm(draft: true, repoRoot: _dir).Should().Be(0);
        string path = Path.Combine(_dir, "docs", "SDLC", document);

        File.WriteAllText(path, File.ReadAllText(path).Replace("# ", "# Edited ", StringComparison.Ordinal));

        RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true, repoRoot: _dir).Should().Be(1);
    }

    [Fact]
    public void VerifyStamp_AcceptsTheStampedDocumentAndRejectsEditsOrAMissingStamp()
    {
        var stamped = GxPSigner.StampGxpMarkdown("# Report\n\nAll requirements qualified.\n");

        stamped.Should().Contain("Unsigned working copy").And.NotContain("BEGIN SIGNATURE");
        GxPSigner.VerifyStamp(stamped).Should().BeTrue();
        GxPSigner.VerifyStamp(stamped.Replace("\n", "\r\n", StringComparison.Ordinal)).Should().BeTrue();
        GxPSigner.VerifyStamp(stamped.Replace("All requirements", "Most requirements", StringComparison.Ordinal)).Should().BeFalse();
        GxPSigner.VerifyStamp("# Report\n\nAll requirements qualified.\n").Should().BeFalse();
    }

    [Fact]
    public void GenerateRtm_ValidateOnly_FailsOnAMisappliedRequirementTag()
    {
        WriteMinimalRepository();
        RtmGeneratorService.GenerateRtm(validateOnly: true, reportPath: ".", repoRoot: _dir).Should().Be(0);

        File.WriteAllText(Path.Combine(_dir, "tests", "SuiteTests.cs"), string.Join("\n",
            "namespace Sample;",
            "/// <summary>Suite.",
            "/// @" + "req:PRD-AAA-001",
            "/// </summary>",
            "public class SuiteTests",
            "{",
            "    [Fact]",
            "    public void Untagged() { }",
            "}"));

        RtmGeneratorService.GenerateRtm(validateOnly: true, reportPath: ".", repoRoot: _dir).Should().Be(1);
    }

    [Fact]
    public void RepositoryTests_HaveNoMisappliedRequirementTags()
    {
        string repoRoot = Cadence.ToolingCore.OpenApi.OpenApiToolingService.FindRepoRoot();
        var scan = RequirementTagScanner.ScanDirectoriesWithViolations(repoRoot, RtmGeneratorService.TestSearchDirs);

        scan.Violations.Select(v => v.ToString()).Should().BeEmpty(
            "requirement tags belong only on the test methods whose assertions prove them (see docs/TESTING.md)");
    }

    [Fact]
    public void RepositoryTests_DeclareOnlyRequirementIdsThatExist()
    {
        string repoRoot = Cadence.ToolingCore.OpenApi.OpenApiToolingService.FindRepoRoot();
        var tests = RequirementTagScanner.ScanDirectories(repoRoot, RtmGeneratorService.TestSearchDirs);
        var requirements = RtmGeneratorService.LoadRequirements(repoRoot);

        RtmGeneratorService.FindUnknownTags(tests, requirements).Keys.Should().BeEmpty(
            "every requirement ID a test declares must be defined in docs/SDLC or docs/SRS.md (see docs/TESTING.md)");
    }

    private static TestMappingInfo Mapping(string fullName, string file = "") => new() { FullName = fullName, TestName = fullName, File = file };

    private void WriteMinimalRepository()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "docs", "SDLC"));
        File.WriteAllText(Path.Combine(_dir, "docs", "SDLC", "prd.md"), "#### PRD-AAA-001: Widget\n");
        Directory.CreateDirectory(Path.Combine(_dir, "tests"));
        WriteTaggedTest("First", "PRD-AAA-001");
    }

    private void WriteTaggedTest(string name, string requirementId) =>
        File.WriteAllText(Path.Combine(_dir, "tests", $"{name}.cs"), string.Join("\n",
            "namespace Sample;",
            $"public class {name}Tests",
            "{",
            "    [Fact]",
            $"    [GxPRequirement(\"{requirementId}\")]",
            $"    public void {name}() {{ }}",
            "}"));

    private void WriteTrx(string fileName, params (string ClassName, string Method, string Outcome)[] results)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var definitions = new XElement(ns + "TestDefinitions");
        var outcomes = new XElement(ns + "Results");
        foreach (var (className, method, outcome) in results)
        {
            string id = Guid.NewGuid().ToString();
            definitions.Add(new XElement(ns + "UnitTest",
                new XAttribute("id", id),
                new XElement(ns + "TestMethod", new XAttribute("className", className), new XAttribute("name", method))));
            outcomes.Add(new XElement(ns + "UnitTestResult",
                new XAttribute("testId", id),
                new XAttribute("outcome", outcome),
                new XAttribute("duration", "00:00:00.01")));
        }

        new XDocument(new XElement(ns + "TestRun", definitions, outcomes)).Save(Path.Combine(_dir, fileName));
    }
}
