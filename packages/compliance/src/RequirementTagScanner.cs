using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Compliance;

/// <summary>
/// A test method or frontend test case discovered in source together with the requirement IDs it declares.
/// </summary>
/// <param name="FullName">
/// For C#, namespace, class (nested classes joined with <c>+</c>) and method, as xUnit reports it in TRX;
/// for JS/TS, <c>{File}::{Suite &gt; … &gt; Title}</c>, matching normalized Vitest and Playwright JUnit XML output.
/// </param>
/// <param name="MethodName">The test method name or normalized JS/TS test title.</param>
/// <param name="File">Repository-relative path of the source file.</param>
/// <param name="Line">1-based line of the test declaration.</param>
/// <param name="RequirementIds">Requirement IDs declared on the test, sorted and de-duplicated.</param>
public sealed record DiscoveredTest(string FullName, string MethodName, string File, int Line, IReadOnlyList<string> RequirementIds)
{
    /// <summary>
    /// Clinical Kernel rules (<c>K-AREA-N</c>) the method declares with <c>[KernelRule("…")]</c>, sorted and
    /// de-duplicated, exactly as written (an ill-formed value is kept so a check can report it).
    /// </summary>
    public IReadOnlyList<string> KernelRuleIds { get; init; } = [];
}

/// <summary>A requirement tag the <c>rtm</c> gate refuses, with where it is and why.</summary>
/// <param name="File">Repository-relative path of the source file.</param>
/// <param name="Line">1-based line of the tag, or of the declaration it is attached to.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
public sealed record RequirementTagViolation(string File, int Line, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{File}:{Line}: {Message}";
}

/// <summary>The tests found in a set of test sources and the requirement tags among them that the <c>rtm</c> gate refuses.</summary>
/// <param name="Tests">Every test method or case found, with the requirement IDs it declares.</param>
/// <param name="Violations">Misapplied or unsupported requirement tags.</param>
public sealed record RequirementTagScan(IReadOnlyList<DiscoveredTest> Tests, IReadOnlyList<RequirementTagViolation> Violations);

/// <summary>
/// Finds xUnit test methods in C# source and Vitest/Playwright test cases in JS/TS source along with the requirement
/// IDs each one declares.
/// <para>
/// A requirement counts only when it is declared on the test itself, through <c>[GxPRequirement("…")]</c> or an
/// <c>@req:</c> line on the C# test method or JS/TS <c>it(…)</c>/<c>test(…)</c> case (immediately above the call or
/// inside its callback body). <see cref="Scan"/> also reports the tags the <c>rtm</c> gate refuses (#5326):
/// </para>
/// <list type="bullet">
/// <item><description>a requirement tag on a C# class/record/struct, a JS/TS file header, or a <c>describe(…)</c> suite, which would read as a claim for every test in it;</description></item>
/// <item><description><c>[Trait("Requirement", "…")]</c>, which is not a supported form and is not counted;</description></item>
/// <item><description>a requirement declared outside the tests that may verify it (<see cref="RestrictedRequirements"/>):
/// <c>PRD-SYS-053</c>, the agent lifecycle hooks, belongs only on <c>Packages/AgentHook*</c> tests.</description></item>
/// </list>
/// <para>
/// The same scan collects the Clinical Kernel rules a C# method declares with <c>[KernelRule("K-…")]</c>
/// (<see cref="DiscoveredTest.KernelRuleIds"/>). Only the attribute on the method counts; a K-ID in a doc comment
/// is documentation.
/// </para>
/// </summary>
public static partial class RequirementTagScanner
{
    private static readonly string[] JsTsTestSuffixes =
    [
        ".spec.js",
        ".spec.ts",
        ".spec.mjs",
        ".spec.cjs",
        ".spec.jsx",
        ".spec.tsx",
        ".test.js",
        ".test.ts",
        ".test.mjs",
        ".test.cjs",
        ".test.jsx",
        ".test.tsx",
    ];

    [GeneratedRegex(@"(?:PRD|SRS)-[A-Z0-9]+-\d+|Trace-\d+")]
    private static partial Regex RequirementIdRegex();

    [GeneratedRegex(@"@req:(.*)")]
    private static partial Regex ReqDocRegex();

    /// <summary>
    /// Requirements that only some test files may declare, keyed by requirement ID: the value names the files that may
    /// and tells whether a repository-relative path is one of them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Allowed, Func<string, bool> IsAllowed)> RestrictedRequirements =
        new Dictionary<string, (string, Func<string, bool>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRD-SYS-053"] = ("tests of the agent lifecycle hooks (tests/**/Packages/AgentHook*.cs)", IsAgentHookTestFile),
        };

    private static bool IsAgentHookTestFile(string relativePath)
    {
        string[] segments = relativePath.Replace('\\', '/').Split('/');
        return segments.Length >= 3
            && segments[0] == "tests"
            && segments[^2] == "Packages"
            && segments[^1].StartsWith("AgentHook", StringComparison.Ordinal)
            && segments[^1].EndsWith(".cs", StringComparison.Ordinal);
    }

    /// <summary>Every requirement ID in <paramref name="text"/>, in order of first appearance.</summary>
    public static IReadOnlyList<string> ExtractRequirementIds(string text) =>
        RequirementIdRegex().Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>True when <paramref name="relativePath"/> names a Vitest or Playwright <c>*.spec.*</c> / <c>*.test.*</c> file.</summary>
    public static bool IsJavaScriptOrTypeScriptTestFile(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return JsTsTestSuffixes.Any(suffix => normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsJavaScriptOrTypeScriptSourcePath(string relativePath)
    {
        string ext = Path.GetExtension(relativePath);
        return ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ts", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mjs", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cjs", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jsx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Scans every C# and JS/TS test file under the given directories (skipping build/vendor output).</summary>
    public static IReadOnlyList<DiscoveredTest> ScanDirectories(string repoRoot, IEnumerable<string> searchDirs) =>
        ScanDirectoriesWithViolations(repoRoot, searchDirs).Tests;

    /// <summary>
    /// Scans every C# and JS/TS test file under the given directories (skipping build/vendor output, and restricting
    /// C# files under <c>packages/</c> to test subdirectories) for tests and for requirement tags the <c>rtm</c> gate refuses.
    /// </summary>
    public static RequirementTagScan ScanDirectoriesWithViolations(string repoRoot, IEnumerable<string> searchDirs)
    {
        var tests = new List<DiscoveredTest>();
        var violations = new List<RequirementTagViolation>();
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (string dirName in searchDirs)
        {
            string dirPath = Path.Combine(repoRoot, dirName);
            if (!Directory.Exists(dirPath)) continue;

            var files = EnumerateScannableTestFiles(repoRoot, dirPath)
                .OrderBy(f => f, StringComparer.Ordinal);

            foreach (string file in files)
            {
                string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (!seenFiles.Add(relPath)) continue;

                var scan = Scan(File.ReadAllText(file), relPath);
                tests.AddRange(scan.Tests);
                violations.AddRange(scan.Violations);
            }
        }

        return new RequirementTagScan(tests, violations);
    }

    private static IEnumerable<string> EnumerateScannableTestFiles(string repoRoot, string rootDir)
    {
        var stack = new Stack<string>();
        stack.Push(rootDir);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            foreach (string subDir in Directory.EnumerateDirectories(current))
            {
                string name = Path.GetFileName(subDir);
                if (IsIgnoredDirectoryName(name)) continue;
                stack.Push(subDir);
            }

            foreach (string file in Directory.EnumerateFiles(current))
            {
                string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (IsBuildOutput(relPath)) continue;

                if (relPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    if (relPath.StartsWith("packages/", StringComparison.Ordinal) && !IsPackageTestPath(relPath))
                    {
                        continue;
                    }

                    yield return file;
                }
                else if (IsJavaScriptOrTypeScriptTestFile(relPath))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsIgnoredDirectoryName(string name) =>
        name is "bin" or "obj" or "node_modules" or "dist" or ".git" or ".temp" or "cache" or "coverage" or "playwright-report" or "test-results"
        || name.StartsWith(".vite", StringComparison.Ordinal)
        || name.StartsWith(".e2e", StringComparison.Ordinal);

    private static bool IsBuildOutput(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');
        return segments.Any(IsIgnoredDirectoryName);
    }

    private static bool IsPackageTestPath(string relativePath)
    {
        string[] segments = relativePath.Split('/');
        for (int i = 1; i < segments.Length - 1; i++)
        {
            string segment = segments[i];
            if (segment.Equals("tests", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("test", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("consumer-tests", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Scans one C# or JS/TS source file for test methods or cases.</summary>
    public static IReadOnlyList<DiscoveredTest> ScanSource(string source, string relativePath) =>
        Scan(source, relativePath).Tests;

    /// <summary>Scans one C# or JS/TS source file for tests and for requirement tags the <c>rtm</c> gate refuses.</summary>
    public static RequirementTagScan Scan(string source, string relativePath)
    {
        string normalizedPath = relativePath.Replace('\\', '/');
        return IsJavaScriptOrTypeScriptSourcePath(normalizedPath)
            ? ScanJavaScriptOrTypeScript(source, normalizedPath)
            : ScanCSharp(source, normalizedPath);
    }

    private static void ReportRestrictedViolations(
        List<RequirementTagViolation> violations,
        string relativePath,
        int line,
        string testDisplayName,
        IReadOnlyList<string> ids)
    {
        foreach (string id in ids)
        {
            if (RestrictedRequirements.TryGetValue(id, out var rule) && !rule.IsAllowed(relativePath))
            {
                violations.Add(new RequirementTagViolation(relativePath, line,
                    $"{testDisplayName} declares {id}, which only {rule.Allowed} may declare. Tag the requirement the test's assertions prove, or none."));
            }
        }
    }

    private static void PopClosedTypes(Stack<(string Name, int Depth)> typeStack, int depth)
    {
        while (typeStack.Count > 0 && depth < typeStack.Peek().Depth)
        {
            typeStack.Pop();
        }
    }
}
