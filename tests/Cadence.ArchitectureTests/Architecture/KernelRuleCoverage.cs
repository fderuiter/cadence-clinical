using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Compliance;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Which Clinical Kernel rules have a test that proves them (#5467). A rule is <em>defined</em> where the
/// specification opens a list item or a quoted principle with its bold ID (<c>- **K-FACT-1** …</c> or
/// <c>&gt; **K-TRUTH-1** …</c>); every other mention is a reference. A rule is <em>covered</em> when a test method under
/// <c>tests/</c> carries <c>[KernelRule("K-FACT-1")]</c>, found by the same source scan the RTM uses
/// (<see cref="RequirementTagScanner"/>). A K-ID in a test name or doc comment does not count.
///
/// Rules not covered yet are listed, one ID per line, in
/// <c>tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage.baseline.txt</c>. The list may only
/// shrink: a rule outside it needs a tagged test, and a listed rule that gains one must leave it.
/// <c>CADENCE_UPDATE_BASELINE=1</c> deletes the covered and unknown lines; it never adds one, so accepting a new
/// untested rule is a reviewed hand edit.
/// </summary>
public static partial class KernelRuleCoverage
{
    public const string SpecificationPath = "docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md";

    public const string BaselinePath = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage.baseline.txt";

    private static readonly string[] Header =
    [
        "# Kernel rules with no test that names them yet (#5467) - " + SpecificationPath + ".",
        "# One rule ID per line, sorted ordinally. The list may only shrink: tag a test that proves the rule with",
        "# [KernelRule(\"K-AREA-N\")] (docs/TESTING.md) and delete its line. CADENCE_UPDATE_BASELINE=1 deletes covered lines only.",
    ];

    [GeneratedRegex(@"^[ \t]*(?:[-*+]|>)[ \t]+\*\*(K-[A-Z]+-[0-9]+)\*\*", RegexOptions.Multiline)]
    private static partial Regex RuleDefinitionRegex();

    [GeneratedRegex(@"^K-[A-Z]+-[0-9]+$")]
    private static partial Regex RuleIdRegex();

    /// <summary>A test method and a rule it names that the specification does not define.</summary>
    public sealed record UnknownTag(string Test, string File, int Line, string RuleId)
    {
        public override string ToString() => $"{File}:{Line} {Test} names {RuleId}";
    }

    /// <summary>The comparison of the specification, the tagged tests and the baseline.</summary>
    public sealed record Coverage(
        IReadOnlyList<string> Untested,
        IReadOnlyList<string> CoveredButInBaseline,
        IReadOnlyList<string> BaselineNotInSpecification,
        IReadOnlyList<string> DuplicateBaselineEntries)
    {
        public bool IsClean =>
            Untested.Count == 0 && CoveredButInBaseline.Count == 0 && BaselineNotInSpecification.Count == 0 && DuplicateBaselineEntries.Count == 0;

        public string Describe()
        {
            var lines = new List<string>();
            if (Untested.Count > 0)
            {
                lines.Add($"{Untested.Count} kernel rule(s) have no test carrying [KernelRule(\"…\")]. Tag a test whose assertions prove the rule (docs/TESTING.md):");
                lines.AddRange(Untested.Select(r => "  + " + r));
            }

            if (CoveredButInBaseline.Count > 0)
            {
                lines.Add($"{CoveredButInBaseline.Count} rule(s) now have a tagged test. Delete these lines from {BaselinePath}:");
                lines.AddRange(CoveredButInBaseline.Select(r => "  - " + r));
            }

            if (BaselineNotInSpecification.Count > 0)
            {
                lines.Add($"{BaselineNotInSpecification.Count} line(s) of {BaselinePath} name no rule {SpecificationPath} defines. Delete them:");
                lines.AddRange(BaselineNotInSpecification.Select(r => "  - " + r));
            }

            if (DuplicateBaselineEntries.Count > 0)
            {
                lines.Add($"{BaselinePath} lists these rules more than once:");
                lines.AddRange(DuplicateBaselineEntries.Select(r => "  - " + r));
            }

            lines.Add($"CADENCE_UPDATE_BASELINE=1 deletes covered and unknown lines from {BaselinePath}; it never adds one.");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>The rule IDs <paramref name="specification"/> defines, in order of definition (duplicates kept).</summary>
    public static IReadOnlyList<string> ParseDefinedRules(string specification) =>
        RuleDefinitionRegex().Matches(specification.Replace("\r\n", "\n", StringComparison.Ordinal))
            .Select(m => m.Groups[1].Value)
            .ToList();

    /// <summary>Whether <paramref name="ruleId"/> has the <c>K-AREA-N</c> form.</summary>
    public static bool IsWellFormed(string ruleId) => RuleIdRegex().IsMatch(ruleId);

    /// <summary>Pure comparison of the defined rules, the rules tagged tests name, and the baseline entries.</summary>
    public static Coverage Compare(IEnumerable<string> definedRules, IEnumerable<string> taggedRules, IReadOnlyList<string> baseline)
    {
        var defined = new SortedSet<string>(definedRules, StringComparer.Ordinal);
        var tagged = new SortedSet<string>(taggedRules, StringComparer.Ordinal);
        var listed = new SortedSet<string>(baseline, StringComparer.Ordinal);

        return new Coverage(
            [.. defined.Where(r => !tagged.Contains(r) && !listed.Contains(r))],
            [.. listed.Where(r => tagged.Contains(r) && defined.Contains(r))],
            [.. listed.Where(r => !defined.Contains(r))],
            [.. baseline.GroupBy(r => r, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).Order(StringComparer.Ordinal)]);
    }

    /// <summary>Tags that name a rule outside <paramref name="definedRules"/>, or that are not a well-formed rule ID.</summary>
    public static IReadOnlyList<UnknownTag> UnknownTags(IEnumerable<string> definedRules, IEnumerable<DiscoveredTest> tests)
    {
        var defined = new HashSet<string>(definedRules, StringComparer.Ordinal);
        return tests
            .SelectMany(t => t.KernelRuleIds.Where(id => !defined.Contains(id)).Select(id => new UnknownTag(t.FullName, t.File, t.Line, id)))
            .OrderBy(u => u.File, StringComparer.Ordinal)
            .ThenBy(u => u.Line)
            .ToList();
    }

    /// <summary>The rules <see cref="SpecificationPath"/> defines.</summary>
    public static IReadOnlyList<string> ReadDefinedRules() =>
        ParseDefinedRules(File.ReadAllText(Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), SpecificationPath)));

    /// <summary>Every test method under <c>tests/</c>, with the rules it names.</summary>
    public static IReadOnlyList<DiscoveredTest> ScanTests() =>
        RequirementTagScanner.ScanDirectories(KernelBoundaryBaseline.RepositoryRoot(), ["tests"]);

    /// <summary>The baseline entries, in file order.</summary>
    public static IReadOnlyList<string> ReadBaseline()
    {
        var path = Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), BaselinePath);
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList()
            : [];
    }

    /// <summary>
    /// Compares the repository's specification, tests and baseline. With <c>CADENCE_UPDATE_BASELINE=1</c> it first
    /// rewrites the baseline without its covered, unknown and duplicate lines, and never adds a line.
    /// </summary>
    public static Coverage Verify(IReadOnlyList<string> definedRules, IEnumerable<string> taggedRules)
    {
        var tagged = taggedRules.ToList();
        var baseline = ReadBaseline();
        var coverage = Compare(definedRules, tagged, baseline);
        if (!KernelBoundaryBaseline.UpdateRequested)
        {
            return coverage;
        }

        var kept = baseline
            .Except(coverage.CoveredButInBaseline.Concat(coverage.BaselineNotInSpecification), StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        File.WriteAllText(
            Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), BaselinePath),
            string.Join("\n", Header.Concat(kept)) + "\n");
        return Compare(definedRules, tagged, ReadBaseline());
    }
}
