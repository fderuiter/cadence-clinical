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
    public void RepositoryTests_DeclareOnlyRequirementIdsThatExist()
    {
        string repoRoot = Cadence.ToolingCore.OpenApi.OpenApiToolingService.FindRepoRoot();
        var tests = RequirementTagScanner.ScanDirectories(repoRoot, RtmGeneratorService.TestSearchDirs);
        var requirements = RtmGeneratorService.LoadRequirements(repoRoot);

        RtmGeneratorService.FindUnknownTags(tests, requirements).Keys.Should().BeEmpty(
            "every requirement ID a test declares must be defined in docs/SDLC or docs/SRS.md (see docs/TESTING.md)");
    }

    private static TestMappingInfo Mapping(string fullName) => new() { FullName = fullName, TestName = fullName };

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
