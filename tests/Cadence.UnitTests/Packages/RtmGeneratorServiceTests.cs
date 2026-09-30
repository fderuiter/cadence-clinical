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
    public void ScanSource_CollectsIdsFromAttributesTraitsAndMethodDocComments()
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
            "    [Trait(\"Requirement\", \"SRS-CCC-001\")]",
            "    public void Many()",
            "    {",
            "        /// @req:Trace-7",
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
    public void ParseTestResults_MergesTheoryCasesSoAnyFailureWins()
    {
        WriteTrx("theory.trx",
            ("Sample.T", "Cases", "Passed"),
            ("Sample.T", "Cases", "Failed"),
            ("Sample.T", "Cases", "Passed"),
            ("Sample.T", "SkippedThenPassed", "NotExecuted"),
            ("Sample.T", "SkippedThenPassed", "Passed"));

        var results = RtmGeneratorService.ParseTestResults(_dir, _dir);

        results["Sample.T.Cases"].Status.Should().Be(TestStatus.Failed);
        results["Sample.T.SkippedThenPassed"].Status.Should().Be(TestStatus.Passed);
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
    public void Classify_QualifiesOnlyWhenAMappedTestPassedAndNoneFailed()
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
    public void FindMappingDrift_IgnoresUnknownIdsAndAliasesWhichTheRtmListsElsewhere()
    {
        var tests = new[] { new DiscoveredTest("S.T", "T", "tests/S.cs", 1, ["SRS-A-1", "PRD-NOPE-1"]) };
        var requirements = new Dictionary<string, RequirementInfo>
        {
            ["SRS-A-1"] = new() { Id = "SRS-A-1", ParentPrdId = "PRD-A-1", IsLegacyAlias = true },
        };

        RtmGeneratorService.FindMappingDrift("## 2. Requirements with Mapped Tests\n", tests, requirements).Should().BeEmpty();
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
    public void RepositoryTests_DeclareOnlyRequirementIdsThatExist()
    {
        string repoRoot = Cadence.ToolingCore.OpenApi.OpenApiToolingService.FindRepoRoot();
        var tests = RequirementTagScanner.ScanDirectories(repoRoot, RtmGeneratorService.TestSearchDirs);
        var requirements = RtmGeneratorService.LoadRequirements(repoRoot);

        RtmGeneratorService.FindUnknownTags(tests, requirements).Keys.Should().BeEmpty(
            "every requirement ID a test declares must be defined in docs/SDLC or docs/SRS.md (see docs/TESTING.md)");
    }

    private static TestMappingInfo Mapping(string fullName) => new() { FullName = fullName, TestName = fullName };

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
