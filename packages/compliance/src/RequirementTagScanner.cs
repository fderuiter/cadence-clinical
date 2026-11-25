using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    private static readonly HashSet<string> JsSuiteModifiers = new(StringComparer.Ordinal)
    {
        "only",
        "skip",
        "concurrent",
        "sequential",
        "serial",
        "parallel",
        "shuffle",
        "todo",
    };

    private static readonly HashSet<string> JsTestModifiers = new(StringComparer.Ordinal)
    {
        "only",
        "skip",
        "todo",
        "fixme",
        "concurrent",
        "sequential",
        "serial",
        "fails",
    };

    private static readonly HashSet<string> JsRegexLeadingKeywords = new(StringComparer.Ordinal)
    {
        "return",
        "typeof",
        "void",
        "case",
        "throw",
        "in",
        "of",
        "await",
        "delete",
        "yield",
        "new",
        "else",
        "do",
    };

    [GeneratedRegex(@"(?:PRD|SRS)-[A-Z0-9]+-\d+|Trace-\d+")]
    private static partial Regex RequirementIdRegex();

    [GeneratedRegex(@"GxPRequirement\s*\(([^)]*)\)")]
    private static partial Regex GxPRequirementRegex();

    [GeneratedRegex(@"Trait\s*\(\s*""Requirement""")]
    private static partial Regex RequirementTraitRegex();

    [GeneratedRegex(@"\bKernelRule(?:Attribute)?\s*\(\s*""([^""]*)""\s*\)")]
    private static partial Regex KernelRuleRegex();

    [GeneratedRegex(@"@req:(.*)")]
    private static partial Regex ReqDocRegex();

    [GeneratedRegex(@"(?:\[|,)\s*(?:Xunit\.)?(?:Fact|Theory)(?:Attribute)?\s*(?:\(|,|\])")]
    private static partial Regex TestAttributeRegex();

    [GeneratedRegex(@"\b(?:class|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex TypeDeclarationRegex();

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z0-9_.]+)")]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"^(?:(?:public|internal|protected|private|static|async|override|virtual|sealed|new|unsafe)\s+)*[A-Za-z_][A-Za-z0-9_<>,\[\]?.\s]*?\s([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*\(")]
    private static partial Regex MethodSignatureRegex();

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

    private static RequirementTagScan ScanCSharp(string source, string relativePath)
    {
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        string[] code = StripCommentsAndStrings(lines);

        var results = new List<DiscoveredTest>();
        var violations = new List<RequirementTagViolation>();
        string ns = string.Empty;
        var typeStack = new Stack<(string Name, int Depth)>();
        string? pendingType = null;
        int depth = 0;

        // Attribute and doc-comment lines directly above the current line, and the attribute code alone.
        var leadingBlock = new StringBuilder();
        var leadingAttributes = new StringBuilder();
        int bracketDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string trimmed = raw.Trim();
            string codeLine = code[i];

            // Strings are blanked in `codeLine`, so a sample source held in a string literal never matches.
            if (codeLine.Contains("Trait", StringComparison.Ordinal) && RequirementTraitRegex().IsMatch(raw))
            {
                violations.Add(new RequirementTagViolation(relativePath, i + 1,
                    "[Trait(\"Requirement\", …)] is not a supported requirement tag and is not counted; use [GxPRequirement(\"…\")] or an @req: line on the test method."));
            }

            var nsMatch = NamespaceRegex().Match(codeLine);
            if (nsMatch.Success)
            {
                ns = nsMatch.Groups[1].Value;
            }

            bool isAttributeOrComment = bracketDepth > 0
                || trimmed.StartsWith('[')
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith('*')
                || trimmed.Length == 0;

            if (isAttributeOrComment)
            {
                leadingBlock.AppendLine(raw);
                if (!trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    leadingAttributes.AppendLine(raw);
                }
                bracketDepth = Math.Max(0, bracketDepth + codeLine.Count(c => c == '[') - codeLine.Count(c => c == ']'));
                depth += codeLine.Count(c => c == '{') - codeLine.Count(c => c == '}');
                PopClosedTypes(typeStack, depth);
                continue;
            }

            string block = leadingBlock.ToString();
            string attributes = leadingAttributes.ToString();
            leadingBlock.Clear();
            leadingAttributes.Clear();

            var typeMatch = TypeDeclarationRegex().Match(codeLine);
            var methodMatch = MethodSignatureRegex().Match(codeLine.Trim());

            if (typeMatch.Success && !codeLine.Contains('=', StringComparison.Ordinal))
            {
                pendingType = typeMatch.Groups[1].Value;
                var classIds = CollectRequirementIds(attributes, block, string.Empty);
                if (classIds.Count > 0)
                {
                    violations.Add(new RequirementTagViolation(relativePath, i + 1,
                        $"{pendingType} declares {string.Join(", ", classIds)} on the type. Requirement tags count only on a test method whose assertions prove the requirement; move the tag to those methods or remove it."));
                }
            }
            else if (methodMatch.Success && typeStack.Count > 0 && TestAttributeRegex().IsMatch(string.Join('\n', StripCommentsAndStrings(attributes.Split('\n')))))
            {
                string methodName = methodMatch.Groups[1].Value;
                int bodyEnd = FindMemberEnd(code, i);
                var body = string.Join('\n', lines.Skip(i).Take(bodyEnd - i + 1));
                var ids = CollectRequirementIds(attributes, block, body);

                string typeChain = string.Join('+', typeStack.Reverse().Select(t => t.Name));
                string fullName = string.IsNullOrEmpty(ns) ? $"{typeChain}.{methodName}" : $"{ns}.{typeChain}.{methodName}";
                results.Add(new DiscoveredTest(fullName, methodName, relativePath, i + 1, ids)
                {
                    KernelRuleIds = CollectKernelRuleIds(attributes),
                });

                ReportRestrictedViolations(violations, relativePath, i + 1, methodName, ids);
            }

            foreach (char c in codeLine)
            {
                if (c == '{')
                {
                    depth++;
                    if (pendingType != null)
                    {
                        typeStack.Push((pendingType, depth));
                        pendingType = null;
                    }
                }
                else if (c == '}')
                {
                    depth--;
                    PopClosedTypes(typeStack, depth);
                }
                else if (c == ';' && pendingType != null && depth == 0)
                {
                    // `record Foo(...);` has no body.
                    pendingType = null;
                }
            }
        }

        return new RequirementTagScan(results, violations);
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

    private enum JsTokenKind
    {
        Identifier,
        StringLiteral,
        Comment,
        OpenParen,
        CloseParen,
        OpenBrace,
        CloseBrace,
        Dot,
        Comma,
        Semicolon,
        Other,
    }

    private readonly record struct JsToken(JsTokenKind Kind, string Text, int Line);

    private static RequirementTagScan ScanJavaScriptOrTypeScript(string source, string relativePath)
    {
        string normalizedSource = source.Replace("\r\n", "\n");
        List<JsToken> tokens = TokenizeJsTs(normalizedSource);
        var results = new List<DiscoveredTest>();
        var violations = new List<RequirementTagViolation>();
        var suiteStack = new Stack<(string Title, int Depth)>();
        var pendingComments = new List<JsToken>();
        int braceDepth = 0;

        int i = 0;
        while (i < tokens.Count)
        {
            JsToken token = tokens[i];
            if (token.Kind == JsTokenKind.Comment)
            {
                pendingComments.Add(token);
                i++;
                continue;
            }

            if (TryMatchJsSuiteOrTest(tokens, i, out bool isSuite, out string title, out int titleLine, out int callOpenParenIndex, out int callCloseParenIndex))
            {
                int declLine = tokens[i].Line;
                if (isSuite)
                {
                    var suiteIds = CollectJsCommentRequirementIds(pendingComments)
                        .Concat(CollectJsInlineReqIds(title))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToList();
                    bool hasRawAtReq = suiteIds.Count > 0
                        || pendingComments.Any(c => ReqDocRegex().IsMatch(c.Text))
                        || ReqDocRegex().IsMatch(title);
                    if (hasRawAtReq)
                    {
                        string label = suiteIds.Count > 0 ? string.Join(", ", suiteIds) : "@req:";
                        int violationLine = pendingComments.FirstOrDefault(c => ReqDocRegex().IsMatch(c.Text)).Line;
                        if (violationLine == 0) violationLine = titleLine > 0 ? titleLine : declLine;
                        violations.Add(new RequirementTagViolation(
                            relativePath,
                            violationLine,
                            $"describe(\"{title}\") declares {label} on the suite. Requirement tags count only on an individual it(...) or test(...) case whose assertions prove the requirement; move the tag to those tests or remove it."));
                    }

                    pendingComments.Clear();

                    int bodyOpenBrace = FindSuiteBodyOpenBrace(tokens, callOpenParenIndex, callCloseParenIndex);
                    if (bodyOpenBrace >= 0)
                    {
                        for (int k = i; k <= bodyOpenBrace; k++)
                        {
                            if (tokens[k].Kind == JsTokenKind.OpenBrace) braceDepth++;
                            else if (tokens[k].Kind == JsTokenKind.CloseBrace)
                            {
                                braceDepth--;
                                PopClosedTypes(suiteStack, braceDepth);
                            }
                        }

                        suiteStack.Push((title, braceDepth));
                        i = bodyOpenBrace + 1;
                        continue;
                    }

                    AdvanceBraceDepth(tokens, i, callCloseParenIndex, ref braceDepth, suiteStack);
                    i = callCloseParenIndex + 1;
                    continue;
                }

                // Individual `it(...)` or `test(...)` declaration
                var bodyComments = new List<JsToken>();
                for (int k = callOpenParenIndex + 1; k < callCloseParenIndex; k++)
                {
                    if (tokens[k].Kind == JsTokenKind.Comment)
                    {
                        bodyComments.Add(tokens[k]);
                    }
                }

                var ids = CollectJsCommentRequirementIds(pendingComments)
                    .Concat(CollectJsInlineReqIds(title))
                    .Concat(CollectJsCommentRequirementIds(bodyComments))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();
                pendingComments.Clear();

                string normalizedTitle = NormalizeJsTitle(title);
                string safeMethodName = SanitizeMarkdownTableTestName(normalizedTitle);
                string titlePath = suiteStack.Count == 0
                    ? normalizedTitle
                    : string.Join(" > ", suiteStack.Reverse().Select(s => NormalizeJsTitle(s.Title)).Append(normalizedTitle));
                string fullName = $"{relativePath}::{titlePath}";

                results.Add(new DiscoveredTest(fullName, safeMethodName, relativePath, declLine, ids));
                ReportRestrictedViolations(violations, relativePath, declLine, safeMethodName, ids);

                AdvanceBraceDepth(tokens, i, callCloseParenIndex, ref braceDepth, suiteStack);
                i = callCloseParenIndex + 1;
                continue;
            }

            FlushOrphanedJsComments(pendingComments, suiteStack, relativePath, violations);

            if (token.Kind == JsTokenKind.OpenBrace)
            {
                braceDepth++;
            }
            else if (token.Kind == JsTokenKind.CloseBrace)
            {
                braceDepth--;
                PopClosedTypes(suiteStack, braceDepth);
            }

            i++;
        }

        FlushOrphanedJsComments(pendingComments, suiteStack, relativePath, violations);
        return new RequirementTagScan(results, violations);
    }

    private static void AdvanceBraceDepth(
        List<JsToken> tokens,
        int startInclusive,
        int endInclusive,
        ref int braceDepth,
        Stack<(string Title, int Depth)> suiteStack)
    {
        for (int k = startInclusive; k <= endInclusive && k < tokens.Count; k++)
        {
            if (tokens[k].Kind == JsTokenKind.OpenBrace)
            {
                braceDepth++;
            }
            else if (tokens[k].Kind == JsTokenKind.CloseBrace)
            {
                braceDepth--;
                PopClosedTypes(suiteStack, braceDepth);
            }
        }
    }

    private static void FlushOrphanedJsComments(
        List<JsToken> pendingComments,
        Stack<(string Title, int Depth)> suiteStack,
        string relativePath,
        List<RequirementTagViolation> violations)
    {
        if (pendingComments.Count == 0) return;

        foreach (JsToken comment in pendingComments)
        {
            if (!ReqDocRegex().IsMatch(comment.Text)) continue;

            var ids = CollectJsCommentRequirementIds([comment]);
            string label = ids.Count > 0 ? string.Join(", ", ids) : "@req:";
            string scopeDesc = suiteStack.Count == 0
                ? $"File-level comment declares {label}"
                : $"Comment in suite \"{suiteStack.Peek().Title}\" declares {label} outside an individual test";
            violations.Add(new RequirementTagViolation(
                relativePath,
                comment.Line,
                $"{scopeDesc}. Requirement tags count only on an individual it(...) or test(...) case whose assertions prove the requirement; move the tag to those tests or remove it."));
        }

        pendingComments.Clear();
    }

    private static IReadOnlyList<string> CollectJsCommentRequirementIds(IEnumerable<JsToken> comments)
    {
        var ids = new List<string>();
        foreach (JsToken comment in comments)
        {
            foreach (string line in comment.Text.Split('\n'))
            {
                Match reqMatch = ReqDocRegex().Match(line);
                if (reqMatch.Success)
                {
                    ids.AddRange(ExtractRequirementIds(reqMatch.Groups[1].Value));
                }
            }
        }

        return ids;
    }

    private static IReadOnlyList<string> CollectJsInlineReqIds(string text)
    {
        var ids = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            Match reqMatch = ReqDocRegex().Match(line);
            if (reqMatch.Success)
            {
                ids.AddRange(ExtractRequirementIds(reqMatch.Groups[1].Value));
            }
        }

        return ids;
    }

    private static string NormalizeJsTitle(string rawTitle) =>
        rawTitle.Replace("\r\n", " ").Replace('\n', ' ').Trim();

    private static string SanitizeMarkdownTableTestName(string title) =>
        title.Replace('`', '\'').Replace('|', '/');

    private static bool TryMatchJsSuiteOrTest(
        List<JsToken> tokens,
        int startIndex,
        out bool isSuite,
        out string title,
        out int titleLine,
        out int callOpenParenIndex,
        out int callCloseParenIndex)
    {
        isSuite = false;
        title = string.Empty;
        titleLine = 0;
        callOpenParenIndex = -1;
        callCloseParenIndex = -1;

        if (tokens[startIndex].Kind != JsTokenKind.Identifier)
        {
            return false;
        }

        // Reject member accesses like `foo.test(...)` or `page.test(...)`
        if (startIndex > 0)
        {
            int prev = PreviousNonCommentIndex(tokens, startIndex - 1);
            if (prev >= 0 && tokens[prev].Kind == JsTokenKind.Dot)
            {
                return false;
            }

            // Reject function declarations `function test(...)`
            if (prev >= 0 && tokens[prev].Kind == JsTokenKind.Identifier && tokens[prev].Text is "function" or "async")
            {
                return false;
            }
        }

        string root = tokens[startIndex].Text;
        if (root is not ("describe" or "suite" or "it" or "test"))
        {
            return false;
        }

        int cursor = NextNonCommentIndex(tokens, startIndex + 1);
        bool sawDescribeMember = false;
        bool hasParameterizedCall = false;

        if (root is "describe" or "suite")
        {
            isSuite = true;
        }

        while (cursor >= 0 && cursor < tokens.Count && tokens[cursor].Kind == JsTokenKind.Dot)
        {
            int propIdx = NextNonCommentIndex(tokens, cursor + 1);
            if (propIdx < 0 || tokens[propIdx].Kind != JsTokenKind.Identifier)
            {
                return false;
            }

            string prop = tokens[propIdx].Text;
            if (!isSuite && root == "test" && prop == "describe" && !sawDescribeMember)
            {
                isSuite = true;
                sawDescribeMember = true;
                cursor = NextNonCommentIndex(tokens, propIdx + 1);
                continue;
            }

            if (prop is "each" or "for")
            {
                int eachOpen = NextNonCommentIndex(tokens, propIdx + 1);
                if (eachOpen < 0 || tokens[eachOpen].Kind != JsTokenKind.OpenParen)
                {
                    // Tagged template `it.each`...`` is unsupported; skip cleanly.
                    return false;
                }

                int eachClose = FindMatchingCloseParen(tokens, eachOpen);
                if (eachClose < 0) return false;
                hasParameterizedCall = true;
                cursor = NextNonCommentIndex(tokens, eachClose + 1);
                break;
            }

            if (isSuite)
            {
                if (!JsSuiteModifiers.Contains(prop))
                {
                    return false;
                }
            }
            else
            {
                if (!JsTestModifiers.Contains(prop))
                {
                    return false;
                }
            }

            cursor = NextNonCommentIndex(tokens, propIdx + 1);
        }

        if (cursor < 0 || cursor >= tokens.Count || tokens[cursor].Kind != JsTokenKind.OpenParen)
        {
            return false;
        }

        int firstArgIdx = NextNonCommentIndex(tokens, cursor + 1);
        if (firstArgIdx < 0 || tokens[firstArgIdx].Kind != JsTokenKind.StringLiteral)
        {
            // e.g. `test.describe.configure({ mode: "serial" })` or `test(myHelper)` without string literal title
            return false;
        }

        int closeParen = FindMatchingCloseParen(tokens, cursor);
        if (closeParen < 0)
        {
            return false;
        }

        _ = hasParameterizedCall;
        title = tokens[firstArgIdx].Text;
        titleLine = tokens[firstArgIdx].Line;
        callOpenParenIndex = cursor;
        callCloseParenIndex = closeParen;
        return true;
    }

    private static int FindSuiteBodyOpenBrace(List<JsToken> tokens, int openParenIndex, int closeParenIndex)
    {
        int parenDepth = 0;
        for (int i = openParenIndex + 1; i < closeParenIndex; i++)
        {
            JsTokenKind kind = tokens[i].Kind;
            if (kind == JsTokenKind.OpenParen) parenDepth++;
            else if (kind == JsTokenKind.CloseParen) parenDepth--;
            else if (kind == JsTokenKind.OpenBrace && parenDepth == 0)
            {
                // Could be options object `describe("title", { concurrent: true }, () => { ... })` or callback body `() => { ... }`.
                // Scan top-level comma-separated arguments inside `describe(...)`; the callback body is the `{` of the last argument!
            }
        }

        // Find the last top-level argument inside `( ... )` and locate its first `{` at parenDepth == 0 (or after `=>` / `function(...)`).
        int lastArgStart = openParenIndex + 1;
        parenDepth = 0;
        int braceDepth = 0;
        for (int i = openParenIndex + 1; i < closeParenIndex; i++)
        {
            JsTokenKind kind = tokens[i].Kind;
            if (kind == JsTokenKind.OpenParen) parenDepth++;
            else if (kind == JsTokenKind.CloseParen) parenDepth--;
            else if (kind == JsTokenKind.OpenBrace) braceDepth++;
            else if (kind == JsTokenKind.CloseBrace) braceDepth--;
            else if (kind == JsTokenKind.Comma && parenDepth == 0 && braceDepth == 0)
            {
                // Check if there is a non-comment token after this comma (ignores trailing comma `describe("x", () => {},)`).
                int nextTok = NextNonCommentIndex(tokens, i + 1);
                if (nextTok > 0 && nextTok < closeParenIndex)
                {
                    lastArgStart = i + 1;
                }
            }
        }

        parenDepth = 0;
        for (int i = lastArgStart; i < closeParenIndex; i++)
        {
            JsTokenKind kind = tokens[i].Kind;
            if (kind == JsTokenKind.OpenParen) parenDepth++;
            else if (kind == JsTokenKind.CloseParen) parenDepth--;
            else if (kind == JsTokenKind.OpenBrace && parenDepth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMatchingCloseParen(List<JsToken> tokens, int openParenIndex)
    {
        int depth = 0;
        for (int i = openParenIndex; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == JsTokenKind.OpenParen) depth++;
            else if (tokens[i].Kind == JsTokenKind.CloseParen)
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static int NextNonCommentIndex(List<JsToken> tokens, int startIndex)
    {
        for (int i = startIndex; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != JsTokenKind.Comment) return i;
        }

        return -1;
    }

    private static int PreviousNonCommentIndex(List<JsToken> tokens, int startIndex)
    {
        for (int i = startIndex; i >= 0; i--)
        {
            if (tokens[i].Kind != JsTokenKind.Comment) return i;
        }

        return -1;
    }

    private static List<JsToken> TokenizeJsTs(string source)
    {
        var tokens = new List<JsToken>();
        int i = 0;
        int line = 1;
        JsTokenKind prevSigKind = JsTokenKind.Semicolon;
        string prevSigText = string.Empty;

        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\n')
            {
                line++;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            // Line comment `// ...`
            if (c == '/' && next == '/')
            {
                int startLine = line;
                int start = i;
                while (i < source.Length && source[i] != '\n') i++;
                tokens.Add(new JsToken(JsTokenKind.Comment, source[start..i], startLine));
                continue;
            }

            // Block comment `/* ... */`
            if (c == '/' && next == '*')
            {
                int startLine = line;
                int start = i;
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '\n') line++;
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }

                    i++;
                }

                tokens.Add(new JsToken(JsTokenKind.Comment, source[start..i], startLine));
                continue;
            }

            // Regex literal `/.../flags` (single-line)
            if (c == '/' && CanStartJsRegexLiteral(prevSigKind, prevSigText) && TrySkipJsRegexLiteral(source, i, out int afterRegex))
            {
                tokens.Add(new JsToken(JsTokenKind.Other, "/", line));
                prevSigKind = JsTokenKind.Other;
                prevSigText = "/";
                i = afterRegex;
                continue;
            }

            // Quoted string `'...'` or `"..."`
            if (c is '\'' or '"')
            {
                int startLine = line;
                char quote = c;
                i++;
                var sb = new StringBuilder();
                while (i < source.Length)
                {
                    char ch = source[i];
                    if (ch == '\n') line++;
                    if (ch == '\\' && i + 1 < source.Length)
                    {
                        char esc = source[i + 1];
                        if (esc == '\n') line++;
                        sb.Append(UnescapeJsChar(esc));
                        i += 2;
                        continue;
                    }

                    if (ch == quote)
                    {
                        i++;
                        break;
                    }

                    sb.Append(ch);
                    i++;
                }

                string text = sb.ToString();
                tokens.Add(new JsToken(JsTokenKind.StringLiteral, text, startLine));
                prevSigKind = JsTokenKind.StringLiteral;
                prevSigText = text;
                continue;
            }

            // Template literal `` `...` `` (preserving nested `${...}` balance)
            if (c == '`')
            {
                int startLine = line;
                string text = ReadJsTemplateLiteral(source, ref i, ref line);
                tokens.Add(new JsToken(JsTokenKind.StringLiteral, text, startLine));
                prevSigKind = JsTokenKind.StringLiteral;
                prevSigText = text;
                continue;
            }

            // Identifier
            if (IsJsIdentifierStart(c))
            {
                int start = i;
                i++;
                while (i < source.Length && IsJsIdentifierPart(source[i])) i++;
                string ident = source[start..i];
                tokens.Add(new JsToken(JsTokenKind.Identifier, ident, line));
                prevSigKind = JsTokenKind.Identifier;
                prevSigText = ident;
                continue;
            }

            JsTokenKind punctKind = c switch
            {
                '(' => JsTokenKind.OpenParen,
                ')' => JsTokenKind.CloseParen,
                '{' => JsTokenKind.OpenBrace,
                '}' => JsTokenKind.CloseBrace,
                '.' => JsTokenKind.Dot,
                ',' => JsTokenKind.Comma,
                ';' => JsTokenKind.Semicolon,
                _ => JsTokenKind.Other,
            };

            string punctText = c.ToString();
            tokens.Add(new JsToken(punctKind, punctText, line));
            prevSigKind = punctKind;
            prevSigText = punctText;
            i++;
        }

        return tokens;
    }

    private static string ReadJsTemplateLiteral(string source, ref int i, ref int line)
    {
        i++; // skip opening `
        var sb = new StringBuilder();
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\n') line++;
            if (c == '\\' && i + 1 < source.Length)
            {
                char esc = source[i + 1];
                if (esc == '\n') line++;
                sb.Append(UnescapeJsChar(esc));
                i += 2;
                continue;
            }

            if (c == '`')
            {
                i++;
                break;
            }

            if (c == '$' && i + 1 < source.Length && source[i + 1] == '{')
            {
                i += 2;
                sb.Append("%s");
                SkipBalancedTemplateExpression(source, ref i, ref line);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static void SkipBalancedTemplateExpression(string source, ref int i, ref int line)
    {
        int depth = 1;
        while (i < source.Length && depth > 0)
        {
            char c = source[i];
            if (c == '\n')
            {
                line++;
                i++;
                continue;
            }

            char next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '\n') line++;
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }

                    i++;
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                char q = c;
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\n') line++;
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        if (source[i + 1] == '\n') line++;
                        i += 2;
                        continue;
                    }

                    if (source[i] == q)
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                continue;
            }

            if (c == '`')
            {
                _ = ReadJsTemplateLiteral(source, ref i, ref line);
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    i++;
                    return;
                }
            }

            i++;
        }
    }

    private static bool CanStartJsRegexLiteral(JsTokenKind prevKind, string prevText) =>
        prevKind switch
        {
            JsTokenKind.OpenParen or JsTokenKind.Comma or JsTokenKind.OpenBrace or JsTokenKind.CloseBrace or JsTokenKind.Semicolon => true,
            JsTokenKind.Other => prevText is not ("]" or "++" or "--"),
            JsTokenKind.Identifier => JsRegexLeadingKeywords.Contains(prevText),
            _ => false,
        };

    private static bool TrySkipJsRegexLiteral(string source, int slashIndex, out int afterRegexIndex)
    {
        afterRegexIndex = slashIndex;
        int i = slashIndex + 1;
        if (i >= source.Length || source[i] is '/' or '*' or '\n')
        {
            return false;
        }

        bool inCharClass = false;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\n')
            {
                return false;
            }

            if (c == '\\')
            {
                if (i + 1 >= source.Length || source[i + 1] == '\n') return false;
                i += 2;
                continue;
            }

            if (c == '[')
            {
                inCharClass = true;
                i++;
                continue;
            }

            if (c == ']' && inCharClass)
            {
                inCharClass = false;
                i++;
                continue;
            }

            if (c == '/' && !inCharClass)
            {
                i++;
                while (i < source.Length && char.IsLetter(source[i])) i++;
                afterRegexIndex = i;
                return true;
            }

            i++;
        }

        return false;
    }

    private static char UnescapeJsChar(char esc) => esc switch
    {
        'n' => ' ',
        'r' => ' ',
        't' => ' ',
        _ => esc,
    };

    private static bool IsJsIdentifierStart(char c) =>
        char.IsLetter(c) || c is '_' or '$';

    private static bool IsJsIdentifierPart(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '$';

    private static void PopClosedTypes(Stack<(string Name, int Depth)> typeStack, int depth)
    {
        while (typeStack.Count > 0 && depth < typeStack.Peek().Depth)
        {
            typeStack.Pop();
        }
    }

    private static IReadOnlyList<string> CollectRequirementIds(string attributeBlock, string leadingBlock, string body)
    {
        var ids = new List<string>();

        foreach (Match m in GxPRequirementRegex().Matches(attributeBlock).Cast<Match>())
        {
            ids.AddRange(ExtractRequirementIds(m.Groups[1].Value));
        }

        foreach (string docLine in (leadingBlock + "\n" + body).Split('\n').Where(l => l.TrimStart().StartsWith("///", StringComparison.Ordinal)))
        {
            var reqMatch = ReqDocRegex().Match(docLine);
            if (reqMatch.Success)
            {
                ids.AddRange(ExtractRequirementIds(reqMatch.Groups[1].Value));
            }
        }

        return ids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<string> CollectKernelRuleIds(string attributeBlock) =>
        KernelRuleRegex().Matches(attributeBlock)
            .Select(m => m.Groups[1].Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Returns the last line of the member starting at <paramref name="start"/>.</summary>
    private static int FindMemberEnd(string[] code, int start)
    {
        int depth = 0;
        bool opened = false;
        for (int i = start; i < code.Length; i++)
        {
            foreach (char c in code[i])
            {
                if (c == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (c == '}')
                {
                    depth--;
                    if (opened && depth == 0) return i;
                }
                else if (c == ';' && !opened && depth == 0)
                {
                    return i; // expression-bodied member
                }
            }
        }

        return code.Length - 1;
    }

    /// <summary>
    /// Blanks out comments, string literals and char literals so braces and brackets inside them are not counted.
    /// Handles regular, verbatim (<c>@"…"</c>), interpolated and raw (<c>"""…"""</c>) strings.
    /// </summary>
    internal static string[] StripCommentsAndStrings(string[] lines)
    {
        var output = new string[lines.Length];
        bool inBlockComment = false;
        bool inVerbatim = false;
        int rawQuotes = 0;

        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li];
            var sb = new StringBuilder(line.Length);
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }

                    continue;
                }

                if (rawQuotes > 0)
                {
                    if (CountRun(line, i, '"') >= rawQuotes)
                    {
                        i += rawQuotes;
                        rawQuotes = 0;
                    }
                    else
                    {
                        i++;
                    }

                    continue;
                }

                if (inVerbatim)
                {
                    if (c == '"' && next == '"')
                    {
                        i += 2;
                    }
                    else if (c == '"')
                    {
                        inVerbatim = false;
                        i++;
                    }
                    else
                    {
                        i++;
                    }

                    continue;
                }

                if (c == '/' && next == '/')
                {
                    break;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i += 2;
                    continue;
                }

                int quoteRun = c == '"' ? CountRun(line, i, '"') : 0;
                if (quoteRun >= 3)
                {
                    rawQuotes = quoteRun;
                    i += quoteRun;
                    continue;
                }

                if (c == '@' && next == '"')
                {
                    inVerbatim = true;
                    i += 2;
                    continue;
                }

                if (c == '$' && next == '@' && i + 2 < line.Length && line[i + 2] == '"')
                {
                    inVerbatim = true;
                    i += 3;
                    continue;
                }

                if (c == '"')
                {
                    i = SkipQuoted(line, i, '"');
                    continue;
                }

                if (c == '\'')
                {
                    i = SkipQuoted(line, i, '\'');
                    continue;
                }

                sb.Append(c);
                i++;
            }

            output[li] = sb.ToString();
        }

        return output;
    }

    private static int CountRun(string line, int start, char ch)
    {
        int n = 0;
        while (start + n < line.Length && line[start + n] == ch) n++;
        return n;
    }

    private static int SkipQuoted(string line, int start, char quote)
    {
        int i = start + 1;
        while (i < line.Length)
        {
            if (line[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (line[i] == quote)
            {
                return i + 1;
            }

            i++;
        }

        return line.Length;
    }
}
