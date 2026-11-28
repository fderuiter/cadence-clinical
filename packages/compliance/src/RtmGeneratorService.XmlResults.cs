using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cadence.Compliance;

public static partial class RtmGeneratorService
{
    [GeneratedRegex(@"^\[[^\]]+\]\s+")]
    private static partial Regex JUnitProjectPrefixRegex();

    /// <summary>
    /// Reads TRX results keyed by <c>Namespace.Class.Method</c> and Vitest/Playwright JUnit XML results keyed by
    /// <c>{repoRelativeFile}::{Suite &gt; … &gt; Title}</c>. <paramref name="reportPath"/> may be a TRX/JUnit XML file
    /// or a directory of TRX and JUnit XML files. Parameterized cases and duplicate runs of the same test are merged:
    /// any failure wins, then a case that did not execute, then a skipped case; the test passes only when every case passed.
    /// </summary>
    public static Dictionary<string, TestResultInfo> ParseTestResults(string repoRoot, string reportPath)
    {
        var results = new Dictionary<string, TestResultInfo>(StringComparer.Ordinal);

        var trxFiles = new List<string>();
        var xmlFiles = new List<string>();
        if (File.Exists(reportPath))
        {
            if (reportPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                xmlFiles.Add(reportPath);
            }
            else
            {
                trxFiles.Add(reportPath);
            }
        }
        else if (Directory.Exists(reportPath))
        {
            trxFiles.AddRange(Directory.GetFiles(reportPath, "*.trx", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal));
            xmlFiles.AddRange(Directory.GetFiles(reportPath, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal));
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

        if (xmlFiles.Count > 0)
        {
            ParseJUnitTestResults(repoRoot, xmlFiles, results);
        }

        return results;
    }

    private static void ParseJUnitTestResults(
        string repoRoot,
        IReadOnlyList<string> xmlFiles,
        Dictionary<string, TestResultInfo> results)
    {
        var jsDiscovered = RequirementTagScanner.ScanDirectories(repoRoot, TestSearchDirs)
            .Where(t => RequirementTagScanner.IsJavaScriptOrTypeScriptTestFile(t.File))
            .ToList();
        var jsFiles = jsDiscovered
            .Select(t => t.File)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        var knownFullNames = jsDiscovered
            .Select(t => t.FullName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string xmlFile in xmlFiles)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(xmlFile);
            }
            catch (System.Xml.XmlException ex)
            {
                Console.WriteLine($"[RTM WARNING] Skipping unreadable XML file {xmlFile}: {ex.Message}");
                continue;
            }

            string? rootName = doc.Root?.Name.LocalName;
            if (rootName is not ("testsuites" or "testsuite"))
            {
                continue;
            }

            string xmlFileName = Path.GetFileName(xmlFile);
            var fileResults = new Dictionary<string, TestResultInfo>(StringComparer.Ordinal);

            foreach (XElement tc in doc.Descendants().Where(e => e.Name.LocalName == "testcase"))
            {
                string parentSuiteFile = tc.Parent?.Attribute("file")?.Value
                    ?? tc.Parent?.Attribute("filepath")?.Value
                    ?? tc.Parent?.Attribute("name")?.Value
                    ?? "";
                string rawClassName = tc.Attribute("classname")?.Value
                    ?? tc.Attribute("file")?.Value
                    ?? parentSuiteFile;
                string rawName = tc.Attribute("name")?.Value ?? "";
                if (string.IsNullOrWhiteSpace(rawClassName) && string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                string normalizedName = NormalizeJUnitTestName(rawName);
                string resolvedFile = ResolveJUnitTestFile(
                    repoRoot,
                    rawClassName,
                    normalizedName,
                    xmlFileName,
                    jsFiles,
                    knownFullNames,
                    out string matchedTitlePath);

                string fullName = string.IsNullOrEmpty(resolvedFile)
                    ? matchedTitlePath
                    : $"{resolvedFile}::{matchedTitlePath}";
                string status = StatusFromJUnitTestCase(tc);
                string duration = tc.Attribute("time")?.Value ?? "0.0";
                fileResults[fullName] = Merge(fileResults.GetValueOrDefault(fullName), status, duration);
            }

            foreach (var (fullName, fileResult) in fileResults)
            {
                results[fullName] = MergeAcrossTrxFiles(results.GetValueOrDefault(fullName), fileResult, fullName);
            }
        }
    }

    private static string NormalizeJUnitTestName(string rawName) =>
        rawName
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace(" \u203A ", " > ", StringComparison.Ordinal)
            .Trim();

    private static string ResolveJUnitTestFile(
        string repoRoot,
        string rawClassName,
        string normalizedName,
        string xmlFileName,
        IReadOnlyList<string> jsFiles,
        HashSet<string> knownFullNames,
        out string matchedTitlePath)
    {
        matchedTitlePath = normalizedName;
        string cls = rawClassName.Trim();
        if (cls.Length == 0)
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(cls))
        {
            cls = Path.GetRelativePath(repoRoot, cls);
        }

        cls = cls.Replace('\\', '/');
        while (cls.StartsWith("./", StringComparison.Ordinal))
        {
            cls = cls[2..];
        }

        cls = cls.TrimStart('/');

        string strippedProjectName = JUnitProjectPrefixRegex().Replace(normalizedName, "");

        string? exact = jsFiles.FirstOrDefault(f => string.Equals(f, cls, StringComparison.Ordinal));
        if (exact is not null)
        {
            matchedTitlePath = PickKnownTitlePath(exact, normalizedName, strippedProjectName, cls, knownFullNames);
            return exact;
        }

        var suffixCandidates = jsFiles
            .Where(f => f.EndsWith("/" + cls, StringComparison.Ordinal))
            .ToList();
        if (suffixCandidates.Count == 1)
        {
            string only = suffixCandidates[0];
            matchedTitlePath = PickKnownTitlePath(only, normalizedName, strippedProjectName, cls, knownFullNames);
            return only;
        }

        if (suffixCandidates.Count > 1)
        {
            foreach (string candidate in suffixCandidates)
            {
                string candidateTitle = PickKnownTitlePath(candidate, normalizedName, strippedProjectName, cls, knownFullNames);
                if (knownFullNames.Contains($"{candidate}::{candidateTitle}"))
                {
                    matchedTitlePath = candidateTitle;
                    return candidate;
                }
            }

            string? hintedPrefix = null;
            if (xmlFileName.Contains("subject-portal", StringComparison.OrdinalIgnoreCase))
            {
                hintedPrefix = "apps/subject-portal/";
            }
            else if (xmlFileName.Contains("web", StringComparison.OrdinalIgnoreCase))
            {
                hintedPrefix = "apps/web/";
            }
            else if (xmlFileName.Contains("usdm", StringComparison.OrdinalIgnoreCase))
            {
                hintedPrefix = "packages/usdm-schemas/";
            }
            else if (xmlFileName.Contains("docs", StringComparison.OrdinalIgnoreCase))
            {
                hintedPrefix = "docs/";
            }

            if (hintedPrefix is not null)
            {
                string? hinted = suffixCandidates.FirstOrDefault(f => f.StartsWith(hintedPrefix, StringComparison.Ordinal));
                if (hinted is not null)
                {
                    matchedTitlePath = PickKnownTitlePath(hinted, normalizedName, strippedProjectName, cls, knownFullNames);
                    return hinted;
                }
            }

            matchedTitlePath = PickKnownTitlePath(suffixCandidates[0], normalizedName, strippedProjectName, cls, knownFullNames);
            return suffixCandidates[0];
        }

        matchedTitlePath = strippedProjectName != normalizedName && strippedProjectName.Length > 0
            ? strippedProjectName
            : normalizedName;
        return cls;
    }

    private static string PickKnownTitlePath(
        string candidateFile,
        string normalizedName,
        string strippedProjectName,
        string rawClass,
        HashSet<string> knownFullNames)
    {
        if (knownFullNames.Contains($"{candidateFile}::{normalizedName}"))
        {
            return normalizedName;
        }

        if (strippedProjectName != normalizedName && knownFullNames.Contains($"{candidateFile}::{strippedProjectName}"))
        {
            return strippedProjectName;
        }

        string classPrefix = rawClass + " > ";
        if (normalizedName.StartsWith(classPrefix, StringComparison.Ordinal))
        {
            string withoutClass = normalizedName[classPrefix.Length..];
            if (knownFullNames.Contains($"{candidateFile}::{withoutClass}"))
            {
                return withoutClass;
            }
        }

        return normalizedName;
    }

    private static string StatusFromJUnitTestCase(XElement testCase)
    {
        if (testCase.Elements().Any(e => e.Name.LocalName is "failure" or "error"))
        {
            return TestStatus.Failed;
        }

        if (testCase.Elements().Any(e => e.Name.LocalName == "skipped"))
        {
            return TestStatus.Skipped;
        }

        string? statusAttr = testCase.Attribute("status")?.Value;
        if (statusAttr is not null &&
            (statusAttr.Equals("skipped", StringComparison.OrdinalIgnoreCase)
             || statusAttr.Equals("disabled", StringComparison.OrdinalIgnoreCase)
             || statusAttr.Equals("pending", StringComparison.OrdinalIgnoreCase)
             || statusAttr.Equals("todo", StringComparison.OrdinalIgnoreCase)))
        {
            return TestStatus.Skipped;
        }

        return TestStatus.Passed;
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
}
