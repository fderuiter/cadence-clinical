using System;
using System.IO;
using System.Linq;

using Cadence.Compliance;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// AGENTS.md: every K-rule in the kernel specification becomes a test (#5467). Each rule the specification defines
/// needs a test method carrying <c>[KernelRule("K-…")]</c>, outside the shrink-only per-area shards under
/// <c>tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/&lt;AREA&gt;.baseline.txt</c>, and no test may name a rule
/// the specification does not define. See <see cref="KernelRuleCoverage"/>.
/// </summary>
public class KernelRuleCoverageTests
{
    private static readonly Lazy<DiscoveredTest[]> Tests = new(() => [.. KernelRuleCoverage.ScanTests()]);

    /// <summary>
    /// Every rule the specification defines has a test that names it with <c>[KernelRule]</c>, or is listed in the
    /// baseline; a listed rule that has gained a test, or that the specification no longer defines, fails until its line
    /// is deleted.
    /// </summary>
    [Fact(DisplayName = "EveryKernelRule_HasATestThatNamesIt_OrIsInTheShrinkOnlyBaseline")]
    [Trait("Category", "Architecture")]
    public void EveryKernelRuleHasATestThatNamesItOrIsInTheShrinkOnlyBaseline()
    {
        var defined = KernelRuleCoverage.ReadDefinedRules();
        Tests.Value.Should().Contain(t => t.File.StartsWith("tests/Cadence.UnitTests/", StringComparison.Ordinal), "the scan must see the test sources");

        var coverage = KernelRuleCoverage.Verify(defined, Tests.Value.SelectMany(t => t.KernelRuleIds));

        coverage.IsClean.Should().BeTrue(coverage.Describe());
    }

    /// <summary>
    /// Every <c>[KernelRule]</c> on a test names a rule the specification defines, so a typo or a rule that was renamed or
    /// removed cannot look like coverage.
    /// </summary>
    [Fact(DisplayName = "EveryKernelRuleTag_NamesARuleTheSpecificationDefines")]
    [Trait("Category", "Architecture")]
    public void EveryKernelRuleTagNamesARuleTheSpecificationDefines()
    {
        var unknown = KernelRuleCoverage.UnknownTags(KernelRuleCoverage.ReadDefinedRules(), Tests.Value);

        unknown.Should().BeEmpty(
            "[KernelRule] names a rule defined in bold in {0}: {1}",
            KernelRuleCoverage.SpecificationPath,
            string.Join("; ", unknown));
    }

    /// <summary>
    /// The specification defines each rule exactly once, in the <c>K-AREA-N</c> form, and the parser finds them; the
    /// baseline is sorted and every line is a well-formed rule ID.
    /// </summary>
    [Fact(DisplayName = "KernelSpecification_DefinesEachRuleOnce_AndBaselineIsSorted")]
    [Trait("Category", "Architecture")]
    public void KernelSpecificationDefinesEachRuleOnceAndBaselineIsSorted()
    {
        var defined = KernelRuleCoverage.ReadDefinedRules();

        defined.Should().HaveCountGreaterThan(50, "the parser must find the specification's rule definitions");
        defined.Should().OnlyHaveUniqueItems("a rule is defined once; other mentions are references");
        defined.Should().Contain(["K-TRUTH-1", "K-SCOPE-1", "K-XS-1", "K-FACT-1", "K-SIM-5"]);

        var baseline = KernelRuleCoverage.ReadBaseline();
        baseline.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
        baseline.Should().OnlyContain(r => KernelRuleCoverage.IsWellFormed(r), "each line is one rule ID");

        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();
        File.Exists(Path.Combine(repoRoot, "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage.baseline.txt"))
            .Should().BeFalse("monolithic kernel-rule-coverage.baseline.txt must not exist once sharded by K-AREA");

        string shardDir = Path.Combine(repoRoot, KernelRuleCoverage.BaselineDirectory);
        Directory.Exists(shardDir).Should().BeTrue();
        var shardFiles = Directory.GetFiles(shardDir, "*.baseline.txt", SearchOption.TopDirectoryOnly);
        shardFiles.Should().NotBeEmpty();
        foreach (string shardFile in shardFiles)
        {
            string expectedArea = Path.GetFileName(shardFile)[..^".baseline.txt".Length];
            var lines = File.ReadAllLines(shardFile)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToList();
            lines.Should().NotBeEmpty("empty area shards must be deleted automatically");
            lines.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
            lines.Should().OnlyContain(r => KernelRuleCoverage.ExtractArea(r) == expectedArea);
        }
    }

    [Fact(DisplayName = "KernelRuleCoverage_UpdateMode_DeletesCoveredAreaShards_AndNeverAddsUntestedRules")]
    [Trait("Category", "Architecture")]
    public void KernelRuleCoverageUpdateModeDeletesCoveredAreaShardsAndNeverAddsUntestedRules()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"kernel-rule-shards-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            KernelRuleCoverage.WriteShards(["K-MOD-2", "K-MOD-3", "K-SIM-1"], tempDir);
            string simShard = Path.Combine(tempDir, "SIM.baseline.txt");
            string modShard = Path.Combine(tempDir, "MOD.baseline.txt");
            File.Exists(simShard).Should().BeTrue();

            var coverage = KernelRuleCoverage.Verify(
                definedRules: ["K-MOD-2", "K-MOD-3", "K-SIM-1", "K-NEW-1"],
                taggedRules: ["K-MOD-2", "K-SIM-1"],
                baselineDirectory: tempDir,
                updateOverride: true);

            File.Exists(simShard).Should().BeFalse("SIM shard reached zero entries and must be deleted");
            File.Exists(modShard).Should().BeTrue();
            KernelRuleCoverage.ReadBaseline(tempDir).Should().Equal("K-MOD-3");
            coverage.CoveredButInBaseline.Should().BeEmpty();
            coverage.Untested.Should().Equal("K-NEW-1");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// The parser takes a rule from a list item or quoted principle that opens with its bold ID, and ignores bold or plain
    /// references elsewhere in a sentence, a table or a code span.
    /// </summary>
    [Fact(DisplayName = "ParseDefinedRules_ReadsDefinitionsNotReferences")]
    [Trait("Category", "Architecture")]
    public void ParseDefinedRulesReadsDefinitionsNotReferences()
    {
        const string specification =
            "> **K-TRUTH-1** No product owns clinical truth.\n" +
            "- **K-FACT-1** Values are immutable (see K-TIME-1).\n" +
            "  - **K-FACT-9** A nested rule.\r\n" +
            "Prose that cites **K-TX-2** in bold is a reference.\n" +
            "| **K-ID-1** | a table cell |\n" +
            "- `**K-ID-2**` in a code span\n" +
            "- **K-lower-1** is not an ID\n";

        KernelRuleCoverage.ParseDefinedRules(specification).Should().Equal("K-TRUTH-1", "K-FACT-1", "K-FACT-9");
    }

    /// <summary>
    /// The ratchet: an untested rule outside the baseline, a listed rule that is now tagged, a listed rule the
    /// specification does not define and a duplicate line are each reported; a tag on an unknown rule is reported with
    /// its test.
    /// </summary>
    [Fact(DisplayName = "Compare_ReportsUntestedStaleUnknownAndDuplicateEntries")]
    [Trait("Category", "Architecture")]
    public void CompareReportsUntestedStaleUnknownAndDuplicateEntries()
    {
        var coverage = KernelRuleCoverage.Compare(
            ["K-A-1", "K-A-2", "K-A-3", "K-A-4"],
            ["K-A-1", "K-A-3"],
            ["K-A-3", "K-A-4", "K-A-4", "K-GONE-1"]);

        coverage.Untested.Should().Equal("K-A-2");
        coverage.CoveredButInBaseline.Should().Equal("K-A-3");
        coverage.BaselineNotInSpecification.Should().Equal("K-GONE-1");
        coverage.DuplicateBaselineEntries.Should().Equal("K-A-4");
        coverage.IsClean.Should().BeFalse();
        coverage.Describe().Should().Contain("+ K-A-2").And.Contain("- K-A-3").And.Contain("- K-GONE-1");

        KernelRuleCoverage.Compare(["K-A-1", "K-A-2"], ["K-A-1"], ["K-A-2"]).IsClean.Should().BeTrue();

        var tests = new[]
        {
            new DiscoveredTest("S.Known", "Known", "tests/S.cs", 3, []) { KernelRuleIds = ["K-A-1"] },
            new DiscoveredTest("S.Typo", "Typo", "tests/S.cs", 9, []) { KernelRuleIds = ["K-A-1", "K-A-9", "KA1"] },
        };
        KernelRuleCoverage.UnknownTags(["K-A-1"], tests).Select(u => u.ToString())
            .Should().Equal("tests/S.cs:9 S.Typo names K-A-9", "tests/S.cs:9 S.Typo names KA1");
    }
}
