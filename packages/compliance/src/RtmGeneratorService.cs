using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Compliance;

/// <summary>
/// Generates the Requirements Traceability Matrix and the IQ/OQ/PQ execution report from the requirement
/// documents, the requirement tags on C# and JS/TS tests (<see cref="RequirementTagScanner"/>), TRX results, and
/// Vitest/Playwright JUnit XML results.
/// A requirement is qualified only when every test that declares it ran and passed (#5324). An alias (an
/// <c>SRS-*</c> or <c>Trace-*</c> entry with a parent PRD) is judged by its own tests: a parent's tests never qualify
/// it, and its tests never qualify the parent.
/// </summary>
public static partial class RtmGeneratorService
{
    /// <summary>Directories scanned for C# and JS/TS test cases.</summary>
    public static readonly string[] TestSearchDirs = ["tests", "apps/web/tests", "apps/subject-portal/tests", "packages"];

    /// <summary>
    /// Where <c>gxp sync</c>, <c>gxp snapshot</c> and <c>cadence ci</c> write TRX and JUnit XML files (gitignored).
    /// <see cref="GenerateRtm"/> reads every <c>*.trx</c> and JUnit <c>*.xml</c> file under it.
    /// </summary>
    public const string DefaultResultsDir = "TestResults/gxp";

    /// <summary>
    /// Maximum number of commits <c>refs/heads/main</c> may advance past the committed RTM's tested revision before
    /// <c>check rtm</c> demands a fresh <c>pnpm cadence gxp snapshot</c> (feature branches stay exempt per ADR-2532).
    /// </summary>
    public const int DefaultMaxMainSnapshotCommitLag = 50;

    /// <summary>The banner a draft RTM or IQ/OQ/PQ report opens with. A committed document never carries it.</summary>
    public const string DraftBanner = "DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT";

    private const string RtmRegulatoryStandardsHeader =
        "*Regulatory Compliance Standards:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5, IEC 62304 Section 5.7 & 5.8\n";

    private const string QualificationRegulatoryProtocolHeader =
        "*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B\n";

    public static int GenerateRtm(
        string outputDir = "docs/SDLC",
        string reportPath = DefaultResultsDir,
        bool draft = false,
        bool validateOnly = false,
        string? repoRoot = null,
        bool honorDraftEnvironment = true,
        RtmEvidenceContext? evidence = null,
        bool requireCurrentSnapshot = true,
        bool requireRecordedEvidence = false,
        string? eventName = null,
        string? gitRef = null,
        int maxMainSnapshotCommitLag = DefaultMaxMainSnapshotCommitLag)
    {
        repoRoot ??= ResolveDefaultRepoRoot();
        outputDir = Path.Combine(repoRoot, outputDir);
        Directory.CreateDirectory(outputDir);

        // `gxp sync` passes honorDraftEnvironment: false, so an RTM_DRAFT left set in a shell or CI job can't
        // turn the documents it regenerates from real results into drafts.
        if (honorDraftEnvironment &&
            (Environment.GetEnvironmentVariable("RTM_DRAFT") == "true" ||
             Environment.GetEnvironmentVariable("GENERATE_RTM_DRAFT") == "true"))
        {
            draft = true;
        }

        // 1. Requirements
        var allRequirements = LoadRequirements(repoRoot);

        // 2. Tests and the requirements they declare
        var scan = RequirementTagScanner.ScanDirectoriesWithViolations(repoRoot, TestSearchDirs);
        var tests = scan.Tests;
        var testMappings = BuildMappings(tests);

        // An alias is mapped only to the tests that declare the alias itself. It does not inherit its parent's
        // tests: a child requirement has its own acceptance criteria, so the parent's evidence never qualifies it.

        // 3. Results
        string fullReportPath = Path.IsPathRooted(reportPath) ? reportPath : Path.Combine(repoRoot, reportPath);
        var testResults = ParseTestResults(repoRoot, fullReportPath);
        bool hasResults = testResults.Count > 0;

        if (!hasResults && !draft && !validateOnly)
        {
            Console.WriteLine($"[RTM ERROR] No test results found at {fullReportPath}. Run `pnpm cadence gxp sync`, or pass --draft for an unverified draft.");
            return 1;
        }

        if (!hasResults && draft)
        {
            foreach (var test in tests)
            {
                testResults[test.FullName] = new TestResultInfo { Status = TestStatus.Unverified, Message = "Missing test report." };
            }
        }

        var unknownTags = FindUnknownTags(tests, allRequirements);

        // The database line comes from what the fixtures recorded during this run, never from the caller.
        evidence = evidence is null
            ? RtmEvidenceContext.NotRecorded
            : evidence with { Database = DatabaseTestEvidence.DescribeEnvironment(fullReportPath) };

        if (!validateOnly)
        {
            string rtmOut = Path.Combine(outputDir, "Requirements_Traceability_Matrix.md");
            GenerateRtmMarkdown(allRequirements, testMappings, testResults, unknownTags, rtmOut, evidence, draft);

            string qualOut = Path.Combine(outputDir, "IQ_OQ_PQ_Execution_Report.md");
            GenerateQualificationReport(allRequirements, testMappings, tests, testResults, qualOut, evidence, draft);
            return 0;
        }

        // Validate-only runs are side-effect free: the checked-in GxP documents are only ever
        // regenerated through the sync tooling, never as a by-product of a validation pass.
        int exitCode = 0;

        if (unknownTags.Count > 0)
        {
            Console.WriteLine($"[RTM ERROR] Tests declare requirement IDs that no requirement document defines ({unknownTags.Count}). Correct the requirement tags, or add the requirement to the PRD:");
            foreach (var kv in unknownTags)
            {
                Console.WriteLine($"  {kv.Key}: {string.Join(", ", kv.Value.Select(t => $"{t.File}:{t.Line}"))}");
            }

            exitCode = 1;
        }

        // A tag on a class, an unsupported Trait form, or a requirement declared outside the tests that may verify it
        // makes an RTM claim no test assertion backs (#5326).
        if (scan.Violations.Count > 0)
        {
            Console.WriteLine($"[RTM ERROR] Misapplied requirement tags ({scan.Violations.Count}). A tag belongs on the test method whose assertions prove the requirement (docs/TESTING.md):");
            foreach (var violation in scan.Violations)
            {
                Console.WriteLine($"  {violation}");
            }

            exitCode = 1;
        }

        // On a feature branch the committed RTM is the baseline's snapshot, not the branch's (ADR-2532, #5151): a new or
        // moved tag is expected to differ from it until the integration owner takes the next snapshot.
        string checkedInRtm = Path.Combine(outputDir, "Requirements_Traceability_Matrix.md");
        if (requireCurrentSnapshot && File.Exists(checkedInRtm))
        {
            var drift = FindMappingDrift(File.ReadAllText(checkedInRtm), tests, allRequirements);
            if (drift.Count > 0)
            {
                Console.WriteLine($"[RTM ERROR] {Path.GetRelativePath(repoRoot, checkedInRtm)} is out of date: its test mapping differs from the tags in tests/ ({drift.Count}). Take a new snapshot with `pnpm cadence gxp snapshot`.");
                foreach (string entry in drift.Take(20))
                {
                    Console.WriteLine($"  {entry}");
                }

                if (drift.Count > 20)
                {
                    Console.WriteLine($"  ... and {drift.Count - 20} more");
                }

                exitCode = 1;
            }
        }

        foreach (string generatedDoc in new[] { checkedInRtm, Path.Combine(outputDir, "IQ_OQ_PQ_Execution_Report.md") })
        {
            // The gates and sync pass requireRecordedEvidence: a committed snapshot is evidence only from a recorded run (#5324).
            if (requireRecordedEvidence && File.Exists(generatedDoc) && IsUnverifiedSnapshot(File.ReadAllText(generatedDoc)))
            {
                Console.WriteLine($"[RTM ERROR] {Path.GetRelativePath(repoRoot, generatedDoc)} is a draft or records no tested revision, so it is not validation evidence. Take a new snapshot with `pnpm cadence gxp snapshot`.");
                exitCode = 1;
            }

            if (File.Exists(generatedDoc) && !GxPSigner.VerifyStamp(File.ReadAllText(generatedDoc)))
            {
                Console.WriteLine($"[RTM ERROR] {Path.GetRelativePath(repoRoot, generatedDoc)} does not match its integrity stamp: it was edited by hand or predates the stamp. Take a new snapshot with `pnpm cadence gxp snapshot`.");
                exitCode = 1;
            }
        }

        if (requireRecordedEvidence
            && maxMainSnapshotCommitLag >= 0
            && !GxpSnapshotPolicy.IsSnapshotMode()
            && File.Exists(checkedInRtm)
            && IsMainBranchValidationTarget(repoRoot, eventName, gitRef)
            && !VerifyMainSnapshotCommitLag(repoRoot, checkedInRtm, maxMainSnapshotCommitLag))
        {
            exitCode = 1;
        }

        var failedReqs = allRequirements.Values
            .Where(r => Classify(r.Id, testMappings, testResults, r.RequiresDatabase) == RequirementState.Failed)
            .Select(r => r.Id)
            .ToList();
        if (failedReqs.Count > 0)
        {
            Console.WriteLine($"[RTM ERROR] Requirements with failing tests ({failedReqs.Count}): {string.Join(", ", failedReqs)}");
            exitCode = 1;
        }

        bool strictAll = Environment.GetEnvironmentVariable("RTM_STRICT_ALL") == "true";
        if (strictAll && !draft)
        {
            var unqualified = allRequirements.Values
                .Where(r => !r.IsLegacyAlias && Classify(r.Id, testMappings, testResults, r.RequiresDatabase) != RequirementState.Qualified)
                .Select(r => r.Id)
                .ToList();
            if (unqualified.Count > 0)
            {
                Console.WriteLine($"[RTM ERROR] Requirements not qualified ({unqualified.Count}): {string.Join(", ", unqualified)}");
                exitCode = 1;
            }
        }

        return exitCode;
    }

    /// <summary>Requirement IDs declared by tests that no requirement document defines, with the tests declaring them.</summary>
    public static SortedDictionary<string, List<DiscoveredTest>> FindUnknownTags(
        IEnumerable<DiscoveredTest> tests,
        IReadOnlyDictionary<string, RequirementInfo> requirements)
    {
        var unknown = new SortedDictionary<string, List<DiscoveredTest>>(StringComparer.Ordinal);
        foreach (var test in tests)
        {
            foreach (var id in test.RequirementIds.Where(id => !requirements.ContainsKey(id)))
            {
                if (!unknown.TryGetValue(id, out var list))
                {
                    list = [];
                    unknown[id] = list;
                }

                list.Add(test);
            }
        }

        return unknown;
    }

    /// <summary>
    /// Compares the test-to-requirement mapping in a checked-in RTM with the tags the tests declare now. Each
    /// entry names a requirement and a test that one side lists and the other doesn't. An empty result means
    /// the RTM lists exactly the tests that declare each requirement; it says nothing about test results.
    /// </summary>
    public static IReadOnlyList<string> FindMappingDrift(
        string rtmMarkdown,
        IEnumerable<DiscoveredTest> tests,
        IReadOnlyDictionary<string, RequirementInfo> requirements)
    {
        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var test in tests)
        {
            foreach (var id in test.RequirementIds)
            {
                if (requirements.TryGetValue(id, out var req))
                {
                    expected.Add(MappingKey(req.Id, test.MethodName, test.File));
                }
            }
        }

        var listed = new SortedSet<string>(StringComparer.Ordinal);
        bool inMappedSection = false;
        foreach (string line in rtmMarkdown.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inMappedSection = line.StartsWith("## 2.", StringComparison.Ordinal) || line.StartsWith("## 4.", StringComparison.Ordinal);
                continue;
            }

            if (!inMappedSection || !line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| :", StringComparison.Ordinal)
                || line.StartsWith("| Requirement ID", StringComparison.Ordinal) || line.StartsWith("| Alias ID", StringComparison.Ordinal))
            {
                continue;
            }

            // | ID | Source | **Title** | `Test` (file) 🟢<br>... | Status |, and in section 4
            // | `Alias` | `Parent` | Title | tests | Status |. The title can contain anything, so the tests column is
            // read from the right.
            string[] cells = line.Trim().Trim('|').Split(" | ");
            if (cells.Length < 5)
            {
                continue;
            }

            string id = cells[0].Trim().Trim('`');
            listed.UnionWith(MappedTestPattern().Matches(cells[^2])
                .Select(m => MappingKey(id, m.Groups["name"].Value, m.Groups["file"].Value)));
        }

        var drift = new List<string>();
        drift.AddRange(expected.Except(listed).Select(k => $"missing from the RTM: {k}"));
        drift.AddRange(listed.Except(expected).Select(k => $"listed in the RTM but no longer declared: {k}"));
        return drift;
    }

    private static string MappingKey(string requirementId, string testName, string file) =>
        $"{requirementId.ToUpperInvariant()} <- {testName} ({file.Replace('\\', '/')})";

    [GeneratedRegex(@"`(?<name>[^`]+)` \((?<file>[^)]+)\)")]
    private static partial Regex MappedTestPattern();

    [GeneratedRegex(@"- \*\*Tested revision:\*\* `(?<sha>[0-9a-fA-F]{7,40})`")]
    private static partial Regex TestedRevisionShaRegex();

    public static RequirementState Classify(
        string requirementId,
        IReadOnlyDictionary<string, List<TestMappingInfo>> mappings,
        IReadOnlyDictionary<string, TestResultInfo> results,
        bool requiresDatabase = false)
    {
        if (!mappings.TryGetValue(requirementId, out var mapped) || mapped.Count == 0)
        {
            return RequirementState.Planned;
        }

        // Every mapped test is mandatory: one failure fails the requirement, and one test that was skipped or did
        // not execute leaves it unverified however many others passed.
        var statuses = mapped.Select(m => StatusOf(m.FullName, results)).ToList();
        if (statuses.Contains(TestStatus.Failed)) return RequirementState.Failed;
        if (!statuses.TrueForAll(s => s == TestStatus.Passed)) return RequirementState.NotVerified;

        // A requirement that lives in the database qualifies only with at least one PostgreSQL test among its mapped tests.
        if (requiresDatabase && !mapped.Exists(m => DatabaseTestEvidence.IsDatabaseTestFile(m.File))) return RequirementState.NeedsDatabaseEvidence;
        return RequirementState.Qualified;
    }

    /// <summary>
    /// True when a generated document carries the draft banner or an evidence block that names no tested revision
    /// (<see cref="RtmEvidenceContext.NotRecorded"/>): it was not produced from a recorded test run (#5324).
    /// </summary>
    public static bool IsUnverifiedSnapshot(string document) =>
        document.Contains(DraftBanner, StringComparison.Ordinal)
        || document.Contains($"- **Tested revision:** {RtmEvidenceContext.NotRecorded.Revision}", StringComparison.Ordinal);

    private static string StatusOf(string fullName, IReadOnlyDictionary<string, TestResultInfo> results) =>
        results.TryGetValue(fullName, out var res) ? res.Status : TestStatus.NotExecuted;

    private static Dictionary<string, List<TestMappingInfo>> BuildMappings(IEnumerable<DiscoveredTest> tests)
    {
        var mappings = new Dictionary<string, List<TestMappingInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var test in tests)
        {
            foreach (var id in test.RequirementIds)
            {
                if (!mappings.TryGetValue(id, out var list))
                {
                    list = [];
                    mappings[id] = list;
                }

                if (!list.Any(m => m.FullName == test.FullName))
                {
                    list.Add(new TestMappingInfo { File = test.File, TestName = test.MethodName, FullName = test.FullName, Line = test.Line });
                }
            }
        }

        return mappings;
    }

    private static bool IsMainBranchValidationTarget(string repoRoot, string? eventName, string? gitRef)
    {
        if (!HasGitMetadata(repoRoot))
        {
            return false;
        }

        string effectiveEvent = (eventName ?? Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? string.Empty).Trim();
        if (effectiveEvent.Equals("pull_request", StringComparison.OrdinalIgnoreCase)
            || effectiveEvent.Equals("pull_request_target", StringComparison.OrdinalIgnoreCase)
            || effectiveEvent.Equals("merge_group", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string effectiveRef = (gitRef ?? Environment.GetEnvironmentVariable("GITHUB_REF") ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(effectiveRef))
        {
            effectiveRef = RunGitCommand(repoRoot, "symbolic-ref", "-q", "HEAD")?.Trim() ?? string.Empty;
        }

        return string.Equals(effectiveRef, "refs/heads/main", StringComparison.Ordinal);
    }

    private static bool VerifyMainSnapshotCommitLag(string repoRoot, string checkedInRtm, int maxCommitLag)
    {
        string content = File.ReadAllText(checkedInRtm);
        Match shaMatch = TestedRevisionShaRegex().Match(content);
        if (!shaMatch.Success)
        {
            return true;
        }

        string testedSha = shaMatch.Groups["sha"].Value;
        if (RunGitCommand(repoRoot, "rev-parse", "--verify", "--quiet", "HEAD") is null)
        {
            return true;
        }

        if (RunGitCommand(repoRoot, "cat-file", "-e", $"{testedSha}^{{commit}}") is null)
        {
            string isShallow = RunGitCommand(repoRoot, "rev-parse", "--is-shallow-repository")?.Trim() ?? "false";
            if (string.Equals(isShallow, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Console.WriteLine(
                $"[RTM ERROR] {Path.GetRelativePath(repoRoot, checkedInRtm)} records tested revision `{testedSha}`, " +
                "which does not exist in this repository's history on main. Take a new snapshot with `pnpm cadence gxp snapshot`.");
            return false;
        }

        if (RunGitCommand(repoRoot, "merge-base", "--is-ancestor", testedSha, "HEAD") is null)
        {
            Console.WriteLine(
                $"[RTM ERROR] {Path.GetRelativePath(repoRoot, checkedInRtm)} records tested revision `{testedSha}`, " +
                "which is not an ancestor of HEAD on main. Take a new snapshot with `pnpm cadence gxp snapshot`.");
            return false;
        }

        string? countText = RunGitCommand(repoRoot, "rev-list", "--count", $"{testedSha}..HEAD")?.Trim();
        if (int.TryParse(countText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int lag)
            && lag > maxCommitLag)
        {
            Console.WriteLine(
                $"[RTM ERROR] {Path.GetRelativePath(repoRoot, checkedInRtm)} is {lag} commits behind HEAD on main " +
                $"(tested revision `{testedSha}`, maximum allowed lag {maxCommitLag}). Take a new snapshot with `pnpm cadence gxp snapshot`.");
            return false;
        }

        return true;
    }

    private static bool HasGitMetadata(string repoRoot)
    {
        string dotGit = Path.Combine(repoRoot, ".git");
        return Directory.Exists(dotGit) || File.Exists(dotGit);
    }

    private static string? RunGitCommand(string repoRoot, params string[] arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            _ = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static string ResolveDefaultRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }

        return Directory.GetCurrentDirectory();
    }
}
