using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Cadence.Compliance;

/// <summary>A database test that did not execute in a test run.</summary>
/// <param name="FullName">The test's <c>Namespace.Class.Method</c>.</param>
/// <param name="Assembly">The test assembly's file name.</param>
/// <param name="Reason">The skip message, if the TRX records one.</param>
public sealed record SkippedDatabaseTest(string FullName, string Assembly, string Reason);

/// <summary>
/// Finds the PostgreSQL-backed tests a run skipped (#5325). Every test in the integration and E2E assemblies needs a
/// database, so a skip there means the run never exercised it; <c>cadence ci</c> and <c>gxp sync</c> refuse such a
/// run unless told to allow it. TRX files carry no traits, so the tests are recognised by assembly.
/// </summary>
public static class DatabaseTestEvidence
{
    /// <summary>The test assemblies whose tests all need PostgreSQL.</summary>
    public static readonly IReadOnlySet<string> DatabaseAssemblies =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Cadence.IntegrationTests.dll", "Cadence.E2ETests.dll" };

    /// <summary>The test project directories (repository-relative, forward slashes) whose tests all need PostgreSQL.</summary>
    public static readonly IReadOnlyList<string> DatabaseTestDirectories = ["tests/Cadence.IntegrationTests/", "tests/Cadence.E2ETests/"];

    /// <summary>True when <paramref name="relativePath"/> is a source file of a test project whose tests all need PostgreSQL.</summary>
    public static bool IsDatabaseTestFile(string relativePath)
    {
        string path = relativePath.Replace('\\', '/');
        return DatabaseTestDirectories.Any(dir => path.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The 10M-edge lineage benchmark opt-in test (<c>CADENCE_LINEAGE_BENCHMARK=1</c>).</summary>
    public const string LineageBenchmarkTest =
        "Cadence.IntegrationTests.Kernel.LineageBenchmarkPostgresTests.BenchmarkTenMillionEdgesTraceLatency";

    /// <summary>The Chromium browser-to-database observation correction test (<c>CADENCE_TEST_BROWSER=1</c>, ADR-2531).</summary>
    public const string BrowserCorrectionTest =
        "Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests.BrowserCorrectionReachesTheDatabaseWithItsReason";

    /// <summary>
    /// Tests that are skipped by design in non-snapshot runs even with a database, because they need a separate opt-in:
    /// the 10M-edge lineage benchmark (<c>CADENCE_LINEAGE_BENCHMARK=1</c>) and the Chromium browser-to-database
    /// correction check (<c>CADENCE_TEST_BROWSER=1</c>, executed in <c>accessibility-e2e</c>). During GxP snapshots // deid-ignore
    /// (<see cref="SnapshotOptInTests"/>), <see cref="BrowserCorrectionTest"/> is mandatory (ADR-2531).
    /// </summary>
    public static readonly IReadOnlySet<string> OptInTests = new HashSet<string>(StringComparer.Ordinal)
    {
        LineageBenchmarkTest,
        BrowserCorrectionTest,
    };

    /// <summary>
    /// Tests remaining opt-in during GxP snapshots (<c>CADENCE_GXP_SNAPSHOT=1</c>): only the 10M-edge lineage benchmark
    /// stays opt-in, whereas <see cref="BrowserCorrectionTest"/> must execute and be recorded in TRX evidence.
    /// </summary>
    public static readonly IReadOnlySet<string> SnapshotOptInTests = new HashSet<string>(StringComparer.Ordinal)
    {
        LineageBenchmarkTest,
    };

    /// <summary>The skipped or not-executed tests from the database assemblies in the TRX files under <paramref name="resultsPath"/>.</summary>
    public static IReadOnlyList<SkippedDatabaseTest> FindSkipped(string resultsPath) =>
        FindSkipped(resultsPath, repoRoot: null, requireBrowserEvidence: null);

    /// <summary>
    /// The skipped or not-executed tests from the database assemblies in the TRX files under <paramref name="resultsPath"/>,
    /// plus (when <paramref name="repoRoot"/> is supplied) any test declared under <see cref="DatabaseTestDirectories"/>
    /// that recorded neither a pass nor a failure in those TRX files. When <paramref name="requireBrowserEvidence"/> is
    /// true (or <see cref="GxpSnapshotPolicy.IsSnapshotMode"/> is active when null), <see cref="BrowserCorrectionTest"/>
    /// must execute in at least one TRX report under <paramref name="resultsPath"/>.
    /// </summary>
    public static IReadOnlyList<SkippedDatabaseTest> FindSkipped(string resultsPath, string? repoRoot, bool? requireBrowserEvidence = null)
    {
        bool enforceBrowser = requireBrowserEvidence ?? GxpSnapshotPolicy.IsSnapshotMode();
        IReadOnlySet<string> activeOptInTests = enforceBrowser ? SnapshotOptInTests : OptInTests;

        string[] trxFiles = [];
        if (File.Exists(resultsPath))
        {
            trxFiles = [resultsPath];
        }
        else if (Directory.Exists(resultsPath))
        {
            trxFiles = [.. Directory.GetFiles(resultsPath, "*.trx", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)];
        }

        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"; // deid-ignore: XML namespace, not PHI
        var skipped = new SortedDictionary<string, SkippedDatabaseTest>(StringComparer.Ordinal);
        var deferredOptInSkips = new Dictionary<string, SkippedDatabaseTest>(StringComparer.Ordinal);
        var executedCompletelyInSomeTrx = new HashSet<string>(StringComparer.Ordinal);

        foreach (var trxFile in trxFiles)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(trxFile);
            }
            catch (XmlException)
            {
                continue;
            }

            var definitions = new Dictionary<string, (string FullName, string Assembly)>(StringComparer.Ordinal);
            foreach (var unitTest in doc.Descendants(ns + "UnitTest"))
            {
                var method = unitTest.Element(ns + "TestMethod");
                string id = unitTest.Attribute("id")?.Value ?? "";
                string assembly = Path.GetFileName(method?.Attribute("codeBase")?.Value ?? unitTest.Attribute("storage")?.Value ?? "");
                if (method is not null && id.Length > 0 && DatabaseAssemblies.Contains(assembly))
                {
                    definitions[id] = ($"{method.Attribute("className")?.Value}.{method.Attribute("name")?.Value}", assembly);
                }
            }

            var fileSeen = new HashSet<string>(StringComparer.Ordinal);
            var fileSkipped = new Dictionary<string, SkippedDatabaseTest>(StringComparer.Ordinal);
            foreach (var result in doc.Descendants(ns + "UnitTestResult"))
            {
                if (!definitions.TryGetValue(result.Attribute("testId")?.Value ?? "", out var test)
                    || activeOptInTests.Contains(test.FullName))
                {
                    continue;
                }

                fileSeen.Add(test.FullName);
                if (RtmGeneratorService.StatusFromOutcome(result.Attribute("outcome")?.Value) is TestStatus.Passed or TestStatus.Failed)
                {
                    continue;
                }

                string reason = result.Descendants(ns + "Message").FirstOrDefault()?.Value.Trim() is { Length: > 0 } message
                    ? message
                    : result.Descendants(ns + "StdOut").FirstOrDefault()?.Value.Trim() ?? "";
                fileSkipped.TryAdd(test.FullName, new SkippedDatabaseTest(test.FullName, test.Assembly, reason));
            }

            foreach (string seenTest in fileSeen)
            {
                if (!fileSkipped.ContainsKey(seenTest))
                {
                    executedCompletelyInSomeTrx.Add(seenTest);
                }
            }

            foreach (var (fullName, skipRecord) in fileSkipped)
            {
                if (OptInTests.Contains(fullName))
                {
                    deferredOptInSkips.TryAdd(fullName, skipRecord);
                }
                else
                {
                    skipped.TryAdd(fullName, skipRecord);
                }
            }
        }

        foreach (var (fullName, skipRecord) in deferredOptInSkips)
        {
            if (!executedCompletelyInSomeTrx.Contains(fullName))
            {
                skipped.TryAdd(fullName, skipRecord);
            }
        }

        bool enforceImplicitBrowserEvidence = string.IsNullOrWhiteSpace(repoRoot)
            || Directory.Exists(Path.Combine(repoRoot, "tests", "Cadence.IntegrationTests"));
        if (enforceBrowser && enforceImplicitBrowserEvidence && trxFiles.Length > 0 && !executedCompletelyInSomeTrx.Contains(BrowserCorrectionTest))
        {
            skipped.TryAdd(
                BrowserCorrectionTest,
                new SkippedDatabaseTest(
                    BrowserCorrectionTest,
                    "Cadence.IntegrationTests.dll",
                    "No browser TRX result recorded for mandatory browser-to-database test (CADENCE_TEST_BROWSER=1)."));
        }

        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            string[] searchDirs = [.. DatabaseTestDirectories.Select(d => d.TrimEnd('/'))];
            var discovered = RequirementTagScanner.ScanDirectories(repoRoot, searchDirs);
            if (discovered.Count > 0)
            {
                var recordedResults = RtmGeneratorService.ParseTestResults(repoRoot, resultsPath);
                foreach (var test in discovered)
                {
                    if (activeOptInTests.Contains(test.FullName))
                    {
                        continue;
                    }

                    if (!recordedResults.TryGetValue(test.FullName, out var info)
                        || info.Status is not (TestStatus.Passed or TestStatus.Failed))
                    {
                        string assembly = test.File.Replace('\\', '/').StartsWith("tests/Cadence.E2ETests/", StringComparison.OrdinalIgnoreCase)
                            ? "Cadence.E2ETests.dll"
                            : "Cadence.IntegrationTests.dll";
                        string reason = info is null ? "No TRX result recorded for mandatory database test." : $"Outcome: {info.Status}";
                        skipped.TryAdd(test.FullName, new SkippedDatabaseTest(test.FullName, assembly, reason));
                    }
                }
            }
        }

        return [.. skipped.Values];
    }

    /// <summary>
    /// True when at least one <c>*.database.txt</c> metadata file written by <c>PostgresTestEvidence</c> exists under
    /// <paramref name="resultsPath"/> with a recorded fixture name and PostgreSQL server version.
    /// </summary>
    public static bool HasRecordedEnvironment(string resultsPath)
    {
        if (!Directory.Exists(resultsPath))
        {
            return false;
        }

        var files = Directory.GetFiles(resultsPath, "*.database.txt", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return false;
        }

        return files.Any(file =>
        {
            var values = File.ReadAllLines(file)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0].Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.Ordinal);
            return values.TryGetValue("fixture", out var fixture) && !string.IsNullOrWhiteSpace(fixture)
                && values.TryGetValue("server_version", out var version) && !string.IsNullOrWhiteSpace(version);
        });
    }

    /// <summary>
    /// The database environment the fixtures recorded under <paramref name="resultsPath"/>
    /// (<c>*.database.txt</c>, written by <c>PostgresTestEvidence</c> in the test support project): the server
    /// version and source, the extensions, the latest migration and the application role's attributes.
    /// </summary>
    public static string DescribeEnvironment(string resultsPath)
    {
        var files = Directory.Exists(resultsPath)
            ? Directory.GetFiles(resultsPath, "*.database.txt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : [];
        if (files.Length == 0)
        {
            return "none recorded: no PostgreSQL fixture ran, so no database test executed";
        }

        return string.Join("; ", files.Select(file =>
        {
            var values = File.ReadAllLines(file)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0], StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.Ordinal);
            string Value(string key) => values.TryGetValue(key, out var value) ? value : "not recorded";
            return $"{Value("fixture")} fixture: PostgreSQL {Value("server_version")} from {Value("source")}, extensions {Value("extensions")}, "
                + $"migrations through `{Value("latest_migration")}`, tests connected as the application role ({Value("app_role")})";
        }));
    }
}
