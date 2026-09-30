using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Cadence.Kernel.Time;

namespace Cadence.Compliance;

public class RequirementInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ParentPrdId { get; set; }
    public bool IsLegacyAlias { get; set; }
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
    public string Status { get; set; } = TestStatus.NotRun;
    public string Message { get; set; } = string.Empty;
    public string Time { get; set; } = "0.0";
}

/// <summary>Outcome of a test as recorded in the TRX results.</summary>
public static class TestStatus
{
    public const string Passed = "PASSED";
    public const string Failed = "FAILED";
    public const string Skipped = "SKIPPED";

    /// <summary>The test exists in source but has no result in any TRX file.</summary>
    public const string NotRun = "NOT RUN";

    /// <summary>Draft mode: no TRX results were supplied at all.</summary>
    public const string Unverified = "UNVERIFIED";
}

/// <summary>Verification state of one requirement.</summary>
public enum RequirementState
{
    /// <summary>At least one mapped test passed and none failed.</summary>
    Qualified,

    /// <summary>At least one mapped test failed.</summary>
    Failed,

    /// <summary>Tests are mapped but none of them passed (skipped, not run, or no results in draft mode).</summary>
    NotVerified,

    /// <summary>No test declares this requirement.</summary>
    Planned,
}

/// <summary>
/// Generates the Requirements Traceability Matrix and the IQ/OQ/PQ execution report from the requirement
/// documents, the requirement tags on test methods (<see cref="RequirementTagScanner"/>) and TRX results.
/// A requirement is reported as qualified only when a test that declares it actually ran and passed.
/// </summary>
public static partial class RtmGeneratorService
{
    public const string StableTimestamp = "2026-07-23 22:38:25 UTC";

    /// <summary>Directories scanned for test methods. Tests only live under <c>tests/</c>.</summary>
    public static readonly string[] TestSearchDirs = ["tests"];

    /// <summary>
    /// Where <c>dotnet cadence gxp sync</c> writes one TRX file per test project (gitignored).
    /// <see cref="GenerateRtm"/> reads every <c>*.trx</c> file under it.
    /// </summary>
    public const string DefaultResultsDir = "TestResults/gxp";

    public static int GenerateRtm(
        string outputDir = "docs/SDLC",
        string reportPath = DefaultResultsDir,
        bool draft = false,
        bool validateOnly = false,
        bool dynamicTimestamp = false,
        string? repoRoot = null,
        bool honorDraftEnvironment = true,
        IClock? clock = null)
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
        var tests = RequirementTagScanner.ScanDirectories(repoRoot, TestSearchDirs);
        var testMappings = BuildMappings(tests);

        // Legacy and SRS aliases inherit their parent's tests for display; a tag on an alias never
        // qualifies the parent requirement.
        foreach (var alias in allRequirements.Values.Where(r => !string.IsNullOrEmpty(r.ParentPrdId)))
        {
            if (!testMappings.ContainsKey(alias.Id) && testMappings.TryGetValue(alias.ParentPrdId!, out var parentTests))
            {
                testMappings[alias.Id] = [.. parentTests];
            }
        }

        // 3. Results
        string fullReportPath = Path.IsPathRooted(reportPath) ? reportPath : Path.Combine(repoRoot, reportPath);
        var testResults = ParseTestResults(repoRoot, fullReportPath);
        bool hasResults = testResults.Count > 0;

        if (!hasResults && !draft && !validateOnly)
        {
            Console.WriteLine($"[RTM ERROR] No test results found at {fullReportPath}. Run `dotnet cadence gxp sync`, or pass --draft for an unverified draft.");
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

        string timestamp = dynamicTimestamp
            ? (clock ?? SystemClock.Instance).UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC", CultureInfo.InvariantCulture)
            : StableTimestamp;

        if (!validateOnly)
        {
            string rtmOut = Path.Combine(outputDir, "Requirements_Traceability_Matrix.md");
            GenerateRtmMarkdown(allRequirements, testMappings, testResults, unknownTags, rtmOut, timestamp, draft);

            string qualOut = Path.Combine(outputDir, "IQ_OQ_PQ_Execution_Report.md");
            GenerateQualificationReport(allRequirements, testMappings, tests, testResults, qualOut, timestamp, draft);
            return 0;
        }

        // Validate-only runs are side-effect free: the checked-in GxP documents are only ever
        // regenerated through the sync tooling, never as a by-product of a validation pass.
        int exitCode = 0;

        if (unknownTags.Count > 0)
        {
            Console.WriteLine($"[RTM ERROR] Tests declare requirement IDs that no requirement document defines ({unknownTags.Count}):");
            foreach (var kv in unknownTags)
            {
                Console.WriteLine($"  {kv.Key}: {string.Join(", ", kv.Value.Select(t => $"{t.File}:{t.Line}"))}");
            }

            exitCode = 1;
        }

        string checkedInRtm = Path.Combine(outputDir, "Requirements_Traceability_Matrix.md");
        if (File.Exists(checkedInRtm))
        {
            var drift = FindMappingDrift(File.ReadAllText(checkedInRtm), tests, allRequirements);
            if (drift.Count > 0)
            {
                Console.WriteLine($"[RTM ERROR] {Path.GetRelativePath(repoRoot, checkedInRtm)} is out of date: its test mapping differs from the tags in tests/ ({drift.Count}). Run `dotnet cadence gxp sync`.");
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
            if (File.Exists(generatedDoc) && !GxPSigner.VerifyStamp(File.ReadAllText(generatedDoc)))
            {
                Console.WriteLine($"[RTM ERROR] {Path.GetRelativePath(repoRoot, generatedDoc)} does not match its integrity stamp: it was edited by hand or predates the stamp. Run `dotnet cadence gxp sync`.");
                exitCode = 1;
            }
        }

        var failedReqs = allRequirements.Values
            .Where(r => Classify(r.Id, testMappings, testResults) == RequirementState.Failed)
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
                .Where(r => !r.IsLegacyAlias && Classify(r.Id, testMappings, testResults) != RequirementState.Qualified)
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
                if (requirements.TryGetValue(id, out var req) && !req.IsLegacyAlias)
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
                inMappedSection = line.StartsWith("## 2.", StringComparison.Ordinal);
                continue;
            }

            if (!inMappedSection || !line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| :", StringComparison.Ordinal)
                || line.StartsWith("| Requirement ID", StringComparison.Ordinal))
            {
                continue;
            }

            // | ID | Source | **Title** | `Test` (file) 🟢<br>... | Status |. The title can contain anything, so
            // the tests column is read from the right.
            string[] cells = line.Trim().Trim('|').Split(" | ");
            if (cells.Length < 5)
            {
                continue;
            }

            string id = cells[0].Trim();
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
        IReadOnlyDictionary<string, TestResultInfo> results)
    {
        if (!mappings.TryGetValue(requirementId, out var mapped) || mapped.Count == 0)
        {
            return RequirementState.Planned;
        }

        var statuses = mapped.Select(m => StatusOf(m.FullName, results)).ToList();
        if (statuses.Contains(TestStatus.Failed)) return RequirementState.Failed;
        if (statuses.Contains(TestStatus.Passed)) return RequirementState.Qualified;
        return RequirementState.NotVerified;
    }

    private static string StatusOf(string fullName, IReadOnlyDictionary<string, TestResultInfo> results) =>
        results.TryGetValue(fullName, out var res) ? res.Status : TestStatus.NotRun;

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

        var mdFiles = Directory.GetFiles(sdlcDir, "*.md", SearchOption.AllDirectories);
        foreach (var file in mdFiles)
        {
            string fileName = Path.GetFileName(file);
            if (fileName == "Requirements_Traceability_Matrix.md" || fileName == "IQ_OQ_PQ_Execution_Report.md") continue;

            string relPath = GetRelativePath(file, FindRepoRoot()).Replace('\\', '/');
            string[] lines = File.ReadAllLines(file);

            foreach (var line in lines)
            {
                var hmatch = headingPattern.Match(line);
                if (hmatch.Success)
                {
                    string reqId = hmatch.Groups[1].Value.Trim();
                    reqs[reqId] = new RequirementInfo
                    {
                        Id = reqId,
                        Title = hmatch.Groups[2].Value.Trim(),
                        Source = relPath
                    };
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
    /// method are merged: any failure wins, then any pass, otherwise the method counts as skipped.
    /// </summary>
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

            foreach (var r in doc.Descendants(ns + "UnitTestResult"))
            {
                string testId = r.Attribute("testId")?.Value ?? "";
                if (!testDefs.TryGetValue(testId, out var fullName)) continue;

                string status = (r.Attribute("outcome")?.Value ?? "").ToUpperInvariant() switch
                {
                    "PASSED" => TestStatus.Passed,
                    "FAILED" or "ERROR" or "TIMEOUT" or "ABORTED" => TestStatus.Failed,
                    _ => TestStatus.Skipped,
                };

                results[fullName] = Merge(results.GetValueOrDefault(fullName), status, r.Attribute("duration")?.Value ?? "0.0");
            }
        }

        return results;
    }

    private static TestResultInfo Merge(TestResultInfo? existing, string status, string duration)
    {
        if (existing is null) return new TestResultInfo { Status = status, Time = duration };
        if (existing.Status == TestStatus.Failed || status == TestStatus.Failed) return new TestResultInfo { Status = TestStatus.Failed, Time = duration };
        if (existing.Status == TestStatus.Passed || status == TestStatus.Passed) return new TestResultInfo { Status = TestStatus.Passed, Time = existing.Time };
        return existing;
    }

    private static string Emoji(string status) => status switch
    {
        TestStatus.Passed => "🟢",
        TestStatus.Failed => "🔴",
        TestStatus.Skipped => "⚪ (SKIPPED)",
        TestStatus.Unverified => "⚪ (UNVERIFIED)",
        _ => "⚪ (NOT RUN)",
    };

    private static void GenerateRtmMarkdown(
        Dictionary<string, RequirementInfo> reqs,
        Dictionary<string, List<TestMappingInfo>> mappings,
        Dictionary<string, TestResultInfo> results,
        SortedDictionary<string, List<DiscoveredTest>> unknownTags,
        string outputPath,
        string timestamp,
        bool draft)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---\nsearch: false\n---\n");
        if (draft)
        {
            sb.AppendLine("> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️");
            sb.AppendLine("> *This document was generated in draft mode with missing test results. It is NOT eligible for GxP production release.*\n");
        }
        sb.AppendLine("# Requirements Traceability Matrix (RTM)\n");
        sb.AppendLine($"*Generated on:* {timestamp}");
        sb.AppendLine("*Regulatory Compliance Standards:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5, IEC 62304 Section 5.7 & 5.8\n");
        sb.AppendLine("> Generated by `dotnet cadence gxp sync`. Do not edit by hand. A requirement is **Qualified** only when a test that");
        sb.AppendLine("> declares it on the test method ran and passed, and no test declaring it failed. See `docs/TESTING.md`.\n");

        var canonical = reqs.Values.Where(r => !r.IsLegacyAlias).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var aliases = reqs.Values.Where(r => r.IsLegacyAlias).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var byState = canonical.ToLookup(r => Classify(r.Id, mappings, results));

        int total = canonical.Count;
        int qualified = byState[RequirementState.Qualified].Count();

        sb.AppendLine("## 1. Traceability Summary\n");
        sb.AppendLine($"- **Total Documented Requirements (PRD):** {total}");
        sb.AppendLine($"- **Qualified (mapped tests passed):** {qualified}");
        sb.AppendLine($"- **Failed (a mapped test failed):** {byState[RequirementState.Failed].Count()}");
        sb.AppendLine($"- **Not Verified (mapped tests skipped or not run):** {byState[RequirementState.NotVerified].Count()}");
        sb.AppendLine($"- **Planned (no mapped tests):** {byState[RequirementState.Planned].Count()}");
        sb.AppendLine($"- **Traceability Aliases (Trace and SRS IDs):** {aliases.Count}");
        sb.AppendLine($"- **Unknown Requirement IDs Declared by Tests:** {unknownTags.Count}");
        sb.AppendLine($"- **Qualified Coverage:** {(total > 0 ? qualified * 100.0 / total : 0):F1}%\n");

        sb.AppendLine("## 2. Requirements with Mapped Tests\n");
        sb.AppendLine("| Requirement ID | Source Document | Title / Description | Mapped Test Cases | Status |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

        foreach (var req in canonical.Where(r => mappings.ContainsKey(r.Id) && mappings[r.Id].Count > 0))
        {
            var links = mappings[req.Id]
                .OrderBy(m => m.File, StringComparer.Ordinal)
                .ThenBy(m => m.Line)
                .Select(m => $"`{m.TestName}` ({m.File}) {Emoji(StatusOf(m.FullName, results))}");
            string statusStr = Classify(req.Id, mappings, results) switch
            {
                RequirementState.Qualified => "✅ **Qualified**",
                RequirementState.Failed => "❌ **Failed**",
                _ => draft ? "⚠️ **Unverified**" : "⚠️ **Not Verified**",
            };
            sb.AppendLine($"| {req.Id} | {SourceDoc(req)} | **{req.Title}** | {string.Join("<br>", links)} | {statusStr} |");
        }

        sb.AppendLine("\n## 3. Planned Requirements (No Mapped Tests)\n");
        sb.AppendLine("| Requirement ID | Source Document | Title / Description | Scheduled Milestone |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var req in byState[RequirementState.Planned])
        {
            sb.AppendLine($"| {req.Id} | {SourceDoc(req)} | **{req.Title}** | Sprint Roadmap |");
        }

        sb.AppendLine("\n## 4. Traceability Aliases (Trace and SRS IDs)\n");
        sb.AppendLine("Aliases report their canonical parent's state. A test tagged only with an alias does not qualify the parent.\n");
        sb.AppendLine("| Alias ID | Canonical Parent PRD | Requirement Summary | Status |");
        sb.AppendLine("| :--- | :--- | :--- | :--- |");

        foreach (var alias in aliases)
        {
            string parent = alias.ParentPrdId ?? "N/A";
            string status = !string.IsNullOrEmpty(alias.ParentPrdId) && Classify(alias.ParentPrdId, mappings, results) == RequirementState.Qualified
                ? "✅ **Verified via Parent**"
                : "📋 **Scheduled via Parent**";
            sb.AppendLine($"| `{alias.Id}` | `{parent}` | {alias.Title} | {status} |");
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

    private static string SourceDoc(RequirementInfo req) => req.Source.Contains("SRS", StringComparison.Ordinal) ? "SRS" : "PRD";

    private static void GenerateQualificationReport(
        Dictionary<string, RequirementInfo> reqs,
        Dictionary<string, List<TestMappingInfo>> mappings,
        IReadOnlyList<DiscoveredTest> tests,
        Dictionary<string, TestResultInfo> results,
        string outputPath,
        string timestamp,
        bool draft)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---\nsearch: false\n---\n");
        if (draft)
        {
            sb.AppendLine("> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️");
            sb.AppendLine("> *This document was generated in draft mode with missing test results. It is NOT eligible for GxP production release.*\n");
        }
        sb.AppendLine("# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report\n");
        sb.AppendLine($"*Execution Date:* {timestamp}");
        sb.AppendLine("*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B\n");

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
        int notRun = taggedStatuses.Count - passedRun - failedRun - skippedRun;
        int executed = passedRun + failedRun;

        sb.AppendLine("### Validation Result Summary\n");
        sb.AppendLine("Counts cover the requirement-tagged test methods in section 3. Whole-suite totals are in the TRX files under");
        sb.AppendLine("`TestResults/gxp/`, so this report only changes when a tagged test or its result changes.\n");
        sb.AppendLine($"- **Requirement-Tagged Test Methods:** {tagged.Count}");
        sb.AppendLine($"- **Passed:** {passedRun} 🟢");
        sb.AppendLine($"- **Failed/Errors:** {failedRun} 🔴");
        sb.AppendLine($"- **Skipped:** {skippedRun} ⚪");
        sb.AppendLine($"- **Not Run:** {notRun}");
        sb.AppendLine($"- **Pass Rate (of executed):** {(executed > 0 ? passedRun * 100.0 / executed : 0):F2}%\n");

        sb.AppendLine("## 2. Installation Qualification (IQ)\n");
        sb.AppendLine("The Installation Qualification verifies that the software execution environment and static quality checks are fully compliant.\n");

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
        int qualified = canonical.Count(r => Classify(r.Id, mappings, results) == RequirementState.Qualified);

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
