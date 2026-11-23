using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cadence.Compliance;

public class RequirementInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ParentPrdId { get; set; }
    public bool IsLegacyAlias { get; set; }

    /// <summary>
    /// The requirement's behaviour lives in the database (row-level security, triggers, constraints, transactions), so
    /// only a PostgreSQL test can prove it. Declared in the PRD with an <c>**Evidence:** PostgreSQL</c> line under
    /// the requirement's heading (#5324).
    /// </summary>
    public bool RequiresDatabase { get; set; }
}

public class TestMappingInfo
{
    public string File { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Line { get; set; }
}

public class TestResultInfo
{
    public string Status { get; set; } = TestStatus.NotExecuted;
    public string Message { get; set; } = string.Empty;
    public string Time { get; set; } = "0.0";
}

/// <summary>Outcome of a test as recorded in the TRX results.</summary>
public static class TestStatus
{
    public const string Passed = "PASSED";
    public const string Failed = "FAILED";
    public const string Skipped = "SKIPPED";

    /// <summary>
    /// The test exists in source but did not execute: it has no result in any TRX file, or its result is neither a
    /// pass, a failure nor a skip (for example inconclusive or pending).
    /// </summary>
    public const string NotExecuted = "NOT EXECUTED";

    /// <summary>Draft mode: no TRX results were supplied at all.</summary>
    public const string Unverified = "UNVERIFIED";
}

/// <summary>Verification state of one requirement.</summary>
public enum RequirementState
{
    /// <summary>Every mapped test passed. Each mapped test is mandatory.</summary>
    Qualified,

    /// <summary>At least one mapped test failed.</summary>
    Failed,

    /// <summary>
    /// No mapped test failed, but at least one did not pass: it was skipped, did not execute, or has no result
    /// (draft mode). Passing tests next to it do not make up for it.
    /// </summary>
    NotVerified,

    /// <summary>
    /// Every mapped test passed, but the requirement needs PostgreSQL evidence (<see cref="RequirementInfo.RequiresDatabase"/>)
    /// and none of its tests is a database test: in-memory or SQLite doubles can't exercise it.
    /// </summary>
    NeedsDatabaseEvidence,

    /// <summary>No test declares this requirement.</summary>
    Planned,
}

/// <summary>
/// Generates the Requirements Traceability Matrix and the IQ/OQ/PQ execution report from the requirement
/// documents, the requirement tags on test methods (<see cref="RequirementTagScanner"/>) and TRX results.
/// A requirement is qualified only when every test that declares it ran and passed (#5324). An alias (an
/// <c>SRS-*</c> or <c>Trace-*</c> entry with a parent PRD) is judged by its own tests: a parent's tests never qualify
/// it, and its tests never qualify the parent.
/// </summary>
public static partial class RtmGeneratorService
{
    /// <summary>Directories scanned for test methods. Tests only live under <c>tests/</c>.</summary>
    public static readonly string[] TestSearchDirs = ["tests"];

    /// <summary>
    /// Where <c>gxp sync</c>, <c>gxp snapshot</c> and <c>cadence ci</c> write one TRX file per test project (gitignored).
    /// <see cref="GenerateRtm"/> reads every <c>*.trx</c> file under it.
    /// </summary>
    public const string DefaultResultsDir = "TestResults/gxp";

    /// <summary>The banner a draft RTM or IQ/OQ/PQ report opens with. A committed document never carries it.</summary>
    public const string DraftBanner = "DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT";

    public static int GenerateRtm(
        string outputDir = "docs/SDLC",
        string reportPath = DefaultResultsDir,
        bool draft = false,
        bool validateOnly = false,
        string? repoRoot = null,
        bool honorDraftEnvironment = true,
        RtmEvidenceContext? evidence = null,
        bool requireCurrentSnapshot = true,
        bool requireRecordedEvidence = false)
    {
        repoRoot ??= FindRepoRoot();
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

    /// <summary>Loads PRD requirements plus the SRS/Trace aliases that point at them.</summary>
    public static Dictionary<string, RequirementInfo> LoadRequirements(string repoRoot)
    {
        var srsReqs = ParseSrs(Path.Combine(repoRoot, "docs"));
        var prdReqs = ParsePrd(Path.Combine(repoRoot, "docs", "SDLC"));

        var allRequirements = new Dictionary<string, RequirementInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in prdReqs) allRequirements[kv.Key] = kv.Value;
        foreach (var kv in srsReqs)
        {
            allRequirements.TryAdd(kv.Key, kv.Value);
        }

        return allRequirements;
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

    public static Dictionary<string, RequirementInfo> ParseSrs(string docsDir)
    {
        var reqs = new Dictionary<string, RequirementInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(docsDir)) return reqs;

        var listPattern = new Regex(@"[-*]\s*\*\*Trace[\s-]*(\d+)\s*:\s*(.+?):\s*\*\*\s*(.*)", RegexOptions.Compiled);
        var headingPattern = new Regex(@"#{1,6}\s*Trace[\s-]*(\d+)\s*:\s*(.*)", RegexOptions.Compiled);
        var parentPattern = new Regex(@"Parent PRD:\s*`?(PRD-[A-Z0-9]+-\d+)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var tablePattern = new Regex(@"\|\s*`?(Trace-\d+|SRS-[A-Z0-9]+-\d+)`?\s*\|\s*`?(PRD-[A-Z0-9]+-\d+)`?\s*\|\s*([^|]+)\|\s*([^|]+)\|", RegexOptions.Compiled);

        var mdFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            string fileName = Path.GetFileName(file);
            if (fileName == "Requirements_Traceability_Matrix.md" || fileName == "IQ_OQ_PQ_Execution_Report.md") continue;

            string relPath = GetRelativePath(file, FindRepoRoot()).Replace('\\', '/');
            string[] lines = File.ReadAllLines(file);

            RequirementInfo? currentTrace = null;

            foreach (var line in lines)
            {
                var tmatch = tablePattern.Match(line);
                if (tmatch.Success)
                {
                    string reqId = tmatch.Groups[1].Value.Trim();
                    string parentId = tmatch.Groups[2].Value.Trim();
                    string title = tmatch.Groups[4].Value.Trim();
                    reqs[reqId] = new RequirementInfo
                    {
                        Id = reqId,
                        Title = title,
                        ParentPrdId = parentId,
                        IsLegacyAlias = true,
                        Source = relPath
                    };
                    continue;
                }

                var match = listPattern.Match(line);
                if (match.Success)
                {
                    string reqId = $"Trace-{match.Groups[1].Value.Trim()}";
                    currentTrace = new RequirementInfo
                    {
                        Id = reqId,
                        Title = match.Groups[2].Value.Trim(),
                        Description = match.Groups[3].Value.Trim(),
                        IsLegacyAlias = true,
                        Source = relPath
                    };
                    reqs[reqId] = currentTrace;
                    continue;
                }

                var hmatch = headingPattern.Match(line);
                if (hmatch.Success)
                {
                    string reqId = $"Trace-{hmatch.Groups[1].Value.Trim()}";
                    currentTrace = new RequirementInfo
                    {
                        Id = reqId,
                        Title = hmatch.Groups[2].Value.Trim(),
                        IsLegacyAlias = true,
                        Source = relPath
                    };
                    reqs[reqId] = currentTrace;
                    continue;
                }

                if (currentTrace != null)
                {
                    var pmatch = parentPattern.Match(line);
                    if (pmatch.Success)
                    {
                        currentTrace.ParentPrdId = pmatch.Groups[1].Value.Trim();
                    }
                    if (line.StartsWith('#') || line.StartsWith("---", StringComparison.Ordinal))
                    {
                        currentTrace = null;
                    }
                }
            }
        }

        return reqs;
    }

    public static Dictionary<string, RequirementInfo> ParsePrd(string sdlcDir)
    {
        var reqs = new Dictionary<string, RequirementInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(sdlcDir)) return reqs;

        var headingPattern = new Regex(@"#{1,6}\s*(PRD-[A-Z0-9]+-\d+)\s*:\s*(.*)", RegexOptions.Compiled);
        var boldPattern = new Regex(@"[-*]\s*\*\*(PRD-[A-Z0-9]+-\d+)\s*:\s*(.+?)\*\*", RegexOptions.Compiled);
        var databaseEvidencePattern = new Regex(@"^\s*(?:[-*]\s*)?\*\*Evidence:\*\*\s*PostgreSQL\b", RegexOptions.Compiled);

        var mdFiles = Directory.GetFiles(sdlcDir, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            string fileName = Path.GetFileName(file);
            if (fileName == "Requirements_Traceability_Matrix.md" || fileName == "IQ_OQ_PQ_Execution_Report.md") continue;

            string relPath = GetRelativePath(file, FindRepoRoot()).Replace('\\', '/');
            string[] lines = File.ReadAllLines(file);
            RequirementInfo? current = null;

            foreach (var line in lines)
            {
                if (line.StartsWith('#'))
                {
                    current = null;
                }

                var hmatch = headingPattern.Match(line);
                if (hmatch.Success)
                {
                    string reqId = hmatch.Groups[1].Value.Trim();
                    reqs[reqId] = current = new RequirementInfo
                    {
                        Id = reqId,
                        Title = hmatch.Groups[2].Value.Trim(),
                        Source = relPath
                    };
                }
                else if (current is not null && databaseEvidencePattern.IsMatch(line))
                {
                    current.RequiresDatabase = true;
                }
                else
                {
                    var bmatch = boldPattern.Match(line);
                    if (bmatch.Success)
                    {
                        string reqId = bmatch.Groups[1].Value.Trim();
                        reqs[reqId] = new RequirementInfo
                        {
                            Id = reqId,
                            Title = bmatch.Groups[2].Value.Trim(),
                            Source = relPath
                        };
                    }
                }
            }
        }

        return reqs;
    }

    /// <summary>
    /// Reads TRX results keyed by <c>Namespace.Class.Method</c>. <paramref name="reportPath"/> may be a TRX
    /// file or a directory of TRX files (one per test project). Theory cases and duplicate runs of the same
    /// method are merged: any failure wins, then a case that did not execute, then a skipped case; the method passes
    /// only when every case passed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SonarAnalyzer.CSharp",
        "S5332:Using http protocol is insecure. Use https instead.",
        Justification = "Visual Studio TRX XML namespace URI is a schema identifier, not an HTTP endpoint.")]
    public static Dictionary<string, TestResultInfo> ParseTestResults(string repoRoot, string reportPath)
    {
        var results = new Dictionary<string, TestResultInfo>(StringComparer.Ordinal);

        var trxFiles = new List<string>();
        if (File.Exists(reportPath))
        {
            trxFiles.Add(reportPath);
        }
        else if (Directory.Exists(reportPath))
        {
            trxFiles.AddRange(Directory.GetFiles(reportPath, "*.trx", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal));
        }

        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        foreach (var trxFile in trxFiles)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(trxFile);
            }
            catch (System.Xml.XmlException ex)
            {
                Console.WriteLine($"[RTM WARNING] Skipping unreadable TRX file {trxFile}: {ex.Message}");
                continue;
            }

            var testDefs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ut in doc.Descendants(ns + "UnitTest"))
            {
                string id = ut.Attribute("id")?.Value ?? "";
                var method = ut.Element(ns + "TestMethod");
                if (method != null && !string.IsNullOrEmpty(id))
                {
                    testDefs[id] = $"{method.Attribute("className")?.Value}.{method.Attribute("name")?.Value}";
                }
            }

            var fileResults = new Dictionary<string, TestResultInfo>(StringComparer.Ordinal);
            foreach (var r in doc.Descendants(ns + "UnitTestResult"))
            {
                string testId = r.Attribute("testId")?.Value ?? "";
                if (!testDefs.TryGetValue(testId, out var fullName)) continue;

                string status = StatusFromOutcome(r.Attribute("outcome")?.Value);
                fileResults[fullName] = Merge(fileResults.GetValueOrDefault(fullName), status, r.Attribute("duration")?.Value ?? "0.0");
            }

            foreach (var (fullName, fileResult) in fileResults)
            {
                results[fullName] = MergeAcrossTrxFiles(results.GetValueOrDefault(fullName), fileResult, fullName);
            }
        }

        return results;
    }

    /// <summary>
    /// Maps a TRX <c>outcome</c> to a <see cref="TestStatus"/>. xUnit writes <c>NotExecuted</c> for a skipped test;
    /// any outcome that is not a pass, a failure or a skip counts as not executed, never as passed.
    /// </summary>
    public static string StatusFromOutcome(string? outcome) => (outcome ?? "").ToUpperInvariant() switch
    {
        "PASSED" => TestStatus.Passed,
        "FAILED" or "ERROR" or "TIMEOUT" or "ABORTED" or "DISCONNECTED" => TestStatus.Failed,
        "NOTEXECUTED" => TestStatus.Skipped,
        _ => TestStatus.NotExecuted,
    };

    private static TestResultInfo MergeAcrossTrxFiles(TestResultInfo? existing, TestResultInfo incoming, string fullName)
    {
        if (existing is null) return incoming;
        if (existing.Status == TestStatus.Failed || incoming.Status == TestStatus.Failed)
        {
            return new TestResultInfo
            {
                Status = TestStatus.Failed,
                Time = existing.Status == TestStatus.Failed ? existing.Time : incoming.Time,
            };
        }

        // Opt-in tests (such as BrowserCorrectionReachesTheDatabaseWithItsReason) skip in dotnet-tests when their
        // dedicated flag is unset and execute in their dedicated browser CI job; a complete pass in that // deid-ignore
        // dedicated TRX overrides the non-opt-in skip, while any failure still wins above. // deid-ignore
        if (DatabaseTestEvidence.OptInTests.Contains(fullName) &&
            (existing.Status == TestStatus.Passed || incoming.Status == TestStatus.Passed))
        {
            return existing.Status == TestStatus.Passed ? existing : incoming;
        }

        return Merge(existing, incoming.Status, incoming.Time);
    }

    private static TestResultInfo Merge(TestResultInfo? existing, string status, string duration)
    {
        if (existing is null) return new TestResultInfo { Status = status, Time = duration };
        return new TestResultInfo { Status = Rank(status) > Rank(existing.Status) ? status : existing.Status, Time = existing.Time };

        static int Rank(string s) => s switch
        {
            TestStatus.Failed => 3,
            TestStatus.NotExecuted => 2,
            TestStatus.Skipped => 1,
            _ => 0,
        };
    }

    private static string Emoji(string status) => status switch
    {
        TestStatus.Passed => "🟢",
        TestStatus.Failed => "🔴",
        TestStatus.Skipped => "⚪ (SKIPPED)",
        TestStatus.Unverified => "⚪ (UNVERIFIED)",
        _ => "⚪ (NOT EXECUTED)",
    };

    private static void GenerateRtmMarkdown(
        Dictionary<string, RequirementInfo> reqs,
        Dictionary<string, List<TestMappingInfo>> mappings,
        Dictionary<string, TestResultInfo> results,
        SortedDictionary<string, List<DiscoveredTest>> unknownTags,
        string outputPath,
        RtmEvidenceContext evidence,
        bool draft)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---\nsearch: false\n---\n");
        if (draft)
        {
            sb.AppendLine($"> ⚠️ **{DraftBanner}** ⚠️");
            sb.AppendLine("> *This document was generated in draft mode with missing test results. It is NOT eligible for GxP production release.*\n");
        }
        sb.AppendLine("# Requirements Traceability Matrix (RTM)\n");
        sb.AppendLine("*Regulatory Compliance Standards:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5, IEC 62304 Section 5.7 & 5.8\n");
        sb.AppendLine(evidence.Render());
        sb.AppendLine("> Generated by `pnpm cadence gxp snapshot`. Do not edit by hand. Every test that declares a requirement on the test");
        sb.AppendLine("> method is mandatory: the requirement is **Qualified** only when all of them ran and passed against the revision");
        sb.AppendLine("> above. A parent requirement's tests never qualify an alias, and an alias's tests never qualify its parent.");
        sb.AppendLine("> See `docs/TESTING.md`.\n");

        var canonical = reqs.Values.Where(r => !r.IsLegacyAlias).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var aliases = reqs.Values.Where(r => r.IsLegacyAlias).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var byState = canonical.ToLookup(r => Classify(r.Id, mappings, results, r.RequiresDatabase));

        int total = canonical.Count;
        int qualified = byState[RequirementState.Qualified].Count();

        sb.AppendLine("## 1. Traceability Summary\n");
        sb.AppendLine($"- **Total Documented Requirements (PRD):** {total}");
        sb.AppendLine($"- **Qualified (every mapped test passed):** {qualified}");
        sb.AppendLine($"- **Failed (a mapped test failed):** {byState[RequirementState.Failed].Count()}");
        sb.AppendLine($"- **Not Verified (a mapped test was skipped or not executed):** {byState[RequirementState.NotVerified].Count()}");
        sb.AppendLine($"- **Not Verified (needs PostgreSQL evidence, no database test mapped):** {byState[RequirementState.NeedsDatabaseEvidence].Count()}");
        sb.AppendLine($"- **Planned (no mapped tests):** {byState[RequirementState.Planned].Count()}");
        sb.AppendLine($"- **Traceability Aliases (Trace and SRS IDs):** {aliases.Count}");
        sb.AppendLine($"- **Unknown Requirement IDs Declared by Tests:** {unknownTags.Count}");
        sb.AppendLine($"- **Qualified Coverage:** {(total > 0 ? qualified * 100.0 / total : 0):F1}%\n");

        sb.AppendLine("## 2. Requirements with Mapped Tests\n");
        sb.AppendLine("| Requirement ID | Source Document | Title / Description | Mapped Test Cases | Status |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

        foreach (var req in canonical.Where(r => mappings.ContainsKey(r.Id) && mappings[r.Id].Count > 0))
        {
            sb.AppendLine($"| {req.Id} | {SourceDoc(req)} | **{req.Title}** | {MappedTests(mappings[req.Id], results)} | {StateLabel(Classify(req.Id, mappings, results, req.RequiresDatabase), draft)} |");
        }

        sb.AppendLine("\n## 3. Planned Requirements (No Mapped Tests)\n");
        sb.AppendLine("| Requirement ID | Source Document | Title / Description | Scheduled Milestone |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var req in byState[RequirementState.Planned])
        {
            sb.AppendLine($"| {req.Id} | {SourceDoc(req)} | **{req.Title}** | Sprint Roadmap |");
        }

        sb.AppendLine("\n## 4. Traceability Aliases (Trace and SRS IDs)\n");
        sb.AppendLine("An alias has its own acceptance criteria, so it is judged only by the tests that declare the alias itself. Its");
        sb.AppendLine("parent's tests never qualify it, and its own tests never qualify the parent.\n");
        sb.AppendLine("| Alias ID | Canonical Parent PRD | Requirement Summary | Mapped Test Cases | Status |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

        foreach (var alias in aliases)
        {
            string parent = alias.ParentPrdId ?? "N/A";
            string tests = mappings.TryGetValue(alias.Id, out var own) && own.Count > 0 ? MappedTests(own, results) : "None";
            sb.AppendLine($"| `{alias.Id}` | `{parent}` | {alias.Title} | {tests} | {StateLabel(Classify(alias.Id, mappings, results, alias.RequiresDatabase), draft)} |");
        }

        sb.AppendLine("\n## 5. Unknown Requirement IDs Declared by Tests\n");
        if (unknownTags.Count == 0)
        {
            sb.AppendLine("None. Every requirement ID declared by a test is defined in the PRD or SRS.");
        }
        else
        {
            sb.AppendLine("These IDs are declared by tests but defined in no requirement document, so they trace to nothing.\n");
            sb.AppendLine("| Declared ID | Tests |");
            sb.AppendLine("| :--- | :--- |");
            foreach (var kv in unknownTags)
            {
                sb.AppendLine($"| `{kv.Key}` | {string.Join("<br>", kv.Value.Select(t => $"`{t.MethodName}` ({t.File})"))} |");
            }
        }

        string stampedContent = GxPSigner.StampGxpMarkdown(sb.ToString());
        File.WriteAllText(outputPath, stampedContent);
    }

    private static string MappedTests(IEnumerable<TestMappingInfo> mapped, IReadOnlyDictionary<string, TestResultInfo> results) =>
        string.Join("<br>", mapped
            .OrderBy(m => m.File, StringComparer.Ordinal)
            .ThenBy(m => m.Line)
            .Select(m => $"`{m.TestName}` ({m.File}) {Emoji(StatusOf(m.FullName, results))}"));

    private static string StateLabel(RequirementState state, bool draft) => state switch
    {
        RequirementState.Qualified => "✅ **Qualified**",
        RequirementState.Failed => "❌ **Failed**",
        RequirementState.Planned => "📋 **Planned**",
        RequirementState.NeedsDatabaseEvidence => "⚠️ **Not Verified (needs PostgreSQL evidence)**",
        _ => draft ? "⚠️ **Unverified**" : "⚠️ **Not Verified**",
    };

    private static string SourceDoc(RequirementInfo req) => req.Source.Contains("SRS", StringComparison.Ordinal) ? "SRS" : "PRD";

    private static void GenerateQualificationReport(
        Dictionary<string, RequirementInfo> reqs,
        Dictionary<string, List<TestMappingInfo>> mappings,
        IReadOnlyList<DiscoveredTest> tests,
        Dictionary<string, TestResultInfo> results,
        string outputPath,
        RtmEvidenceContext evidence,
        bool draft)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---\nsearch: false\n---\n");
        if (draft)
        {
            sb.AppendLine($"> ⚠️ **{DraftBanner}** ⚠️");
            sb.AppendLine("> *This document was generated in draft mode with missing test results. It is NOT eligible for GxP production release.*\n");
        }
        sb.AppendLine("# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report\n");
        sb.AppendLine("*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B\n");
        sb.AppendLine(evidence.Render());

        sb.AppendLine("## 1. Executive Summary & Verification Declaration\n");
        sb.AppendLine("This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.\n");

        // Only the requirement-tagged methods listed in section 3 are counted. Whole-suite totals would change with
        // every untagged test any branch adds, so two branches could never both merge without regenerating (ADR-2287).
        var tagged = tests.Where(t => t.RequirementIds.Count > 0).OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        var taggedStatuses = tagged
            .Select(t => (results.GetValueOrDefault(t.FullName) ?? new TestResultInfo()).Status)
            .ToList();
        int passedRun = taggedStatuses.Count(s => s == TestStatus.Passed);
        int failedRun = taggedStatuses.Count(s => s == TestStatus.Failed);
        int skippedRun = taggedStatuses.Count(s => s == TestStatus.Skipped);
        int notExecuted = taggedStatuses.Count - passedRun - failedRun - skippedRun;
        int executed = passedRun + failedRun;

        sb.AppendLine("### Validation Result Summary\n");
        sb.AppendLine("Counts cover the requirement-tagged test methods in section 3. Whole-suite totals are in the TRX files under");
        sb.AppendLine("`TestResults/gxp/`, so this report only changes when a tagged test or its result changes.\n");
        sb.AppendLine($"- **Requirement-Tagged Test Methods:** {tagged.Count}");
        sb.AppendLine($"- **Passed:** {passedRun} 🟢");
        sb.AppendLine($"- **Failed/Errors:** {failedRun} 🔴");
        sb.AppendLine($"- **Skipped:** {skippedRun} ⚪");
        sb.AppendLine($"- **Not Executed:** {notExecuted}");
        sb.AppendLine($"- **Pass Rate (of executed):** {(executed > 0 ? passedRun * 100.0 / executed : 0):F2}%\n");

        sb.AppendLine("## 2. Installation Qualification (IQ)\n");
        sb.AppendLine("The Installation Qualification records the environment the tests ran in: the tested revision, the database and the");
        sb.AppendLine("runner are in the evidence block at the top of this report.\n");

        sb.AppendLine("## 3. Operational Qualification (OQ)\n");
        sb.AppendLine("Test methods that declare at least one requirement. Theory cases are merged per method. Durations are left");
        sb.AppendLine("out so the report only changes when results change; they are in the TRX files under `TestResults/gxp/`.\n");
        sb.AppendLine("| Test Case Name | Class | Target Req | Status |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var test in tagged)
        {
            var res = results.GetValueOrDefault(test.FullName) ?? new TestResultInfo();
            string className = test.FullName[..test.FullName.LastIndexOf('.')];
            sb.AppendLine($"| `{test.MethodName}` | `{className}` | {string.Join(", ", test.RequirementIds)} | {Emoji(res.Status)} {res.Status} |");
        }

        var canonical = reqs.Values.Where(r => !r.IsLegacyAlias).ToList();
        int qualified = canonical.Count(r => Classify(r.Id, mappings, results, r.RequiresDatabase) == RequirementState.Qualified);

        sb.AppendLine("\n## 4. Performance Qualification (PQ) & Scenario Validation\n");
        sb.AppendLine($"Qualified Requirements: {qualified} of {canonical.Count}\n");

        sb.AppendLine("## 5. Qualification Review & Authorization\n");
        sb.AppendLine("```");
        sb.AppendLine("Lead Systems Validation Engineer:   ___________________________   Date: _______________");
        sb.AppendLine("Director of Clinical Quality Assurance: ___________________________   Date: _______________");
        sb.AppendLine("```\n");

        string stampedContent = GxPSigner.StampGxpMarkdown(sb.ToString());
        File.WriteAllText(outputPath, stampedContent);
    }

    private static string GetRelativePath(string fullPath, string basePath)
    {
        return Path.GetRelativePath(basePath, fullPath);
    }

    private static string FindRepoRoot()
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
