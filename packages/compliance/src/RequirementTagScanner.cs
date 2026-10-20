using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.Compliance;

/// <summary>
/// A test method discovered in C# source together with the requirement IDs it declares.
/// </summary>
/// <param name="FullName">Namespace, class (nested classes joined with <c>+</c>) and method, as xUnit reports it in TRX.</param>
/// <param name="MethodName">The test method name.</param>
/// <param name="File">Repository-relative path of the source file.</param>
/// <param name="Line">1-based line of the method signature.</param>
/// <param name="RequirementIds">Requirement IDs declared on the method, sorted and de-duplicated.</param>
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

/// <summary>The tests found in a set of C# sources and the requirement tags among them that the <c>rtm</c> gate refuses.</summary>
/// <param name="Tests">Every test method found, with the requirement IDs it declares.</param>
/// <param name="Violations">Misapplied or unsupported requirement tags.</param>
public sealed record RequirementTagScan(IReadOnlyList<DiscoveredTest> Tests, IReadOnlyList<RequirementTagViolation> Violations);

/// <summary>
/// Finds xUnit test methods in C# source and the requirement IDs each one declares.
/// <para>
/// A requirement counts only when it is declared on the test method itself, through
/// <c>[GxPRequirement("…")]</c> or an <c>@req:</c> line in the method's doc comment (above the method or inside its
/// body). <see cref="Scan"/> also reports the tags the <c>rtm</c> gate refuses (#5326):
/// </para>
/// <list type="bullet">
/// <item><description>a requirement tag on a class, record or struct, which would read as a claim for every test in it;</description></item>
/// <item><description><c>[Trait("Requirement", "…")]</c>, which is not a supported form and is not counted;</description></item>
/// <item><description>a requirement declared outside the tests that may verify it (<see cref="RestrictedRequirements"/>):
/// <c>PRD-SYS-053</c>, the agent lifecycle hooks, belongs only on <c>Packages/AgentHook*</c> tests.</description></item>
/// </list>
/// <para>
/// The same scan collects the Clinical Kernel rules a method declares with <c>[KernelRule("K-…")]</c>
/// (<see cref="DiscoveredTest.KernelRuleIds"/>). Only the attribute on the method counts; a K-ID in a doc comment
/// is documentation.
/// </para>
/// </summary>
public static partial class RequirementTagScanner
{
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

    /// <summary>Scans every <c>*.cs</c> file under the given directories (skipping <c>bin</c> and <c>obj</c>).</summary>
    public static IReadOnlyList<DiscoveredTest> ScanDirectories(string repoRoot, IEnumerable<string> searchDirs) =>
        ScanDirectoriesWithViolations(repoRoot, searchDirs).Tests;

    /// <summary>
    /// Scans every <c>*.cs</c> file under the given directories (skipping <c>bin</c> and <c>obj</c>) for tests and for
    /// requirement tags the <c>rtm</c> gate refuses.
    /// </summary>
    public static RequirementTagScan ScanDirectoriesWithViolations(string repoRoot, IEnumerable<string> searchDirs)
    {
        var tests = new List<DiscoveredTest>();
        var violations = new List<RequirementTagViolation>();
        foreach (string dirName in searchDirs)
        {
            string dirPath = Path.Combine(repoRoot, dirName);
            if (!Directory.Exists(dirPath)) continue;

            var files = Directory.GetFiles(dirPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsBuildOutput(Path.GetRelativePath(repoRoot, f)))
                .OrderBy(f => f, StringComparer.Ordinal);

            foreach (string file in files)
            {
                string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                var scan = Scan(File.ReadAllText(file), relPath);
                tests.AddRange(scan.Tests);
                violations.AddRange(scan.Violations);
            }
        }

        return new RequirementTagScan(tests, violations);
    }

    private static bool IsBuildOutput(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');
        return segments.Any(s => s is "bin" or "obj" or "node_modules");
    }

    /// <summary>Scans one C# source file for test methods.</summary>
    public static IReadOnlyList<DiscoveredTest> ScanSource(string source, string relativePath) =>
        Scan(source, relativePath).Tests;

    /// <summary>Scans one C# source file for test methods and for requirement tags the <c>rtm</c> gate refuses.</summary>
    public static RequirementTagScan Scan(string source, string relativePath)
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

                foreach (string id in ids)
                {
                    if (RestrictedRequirements.TryGetValue(id, out var rule) && !rule.IsAllowed(relativePath))
                    {
                        violations.Add(new RequirementTagViolation(relativePath, i + 1,
                            $"{methodName} declares {id}, which only {rule.Allowed} may declare. Tag the requirement the test's assertions prove, or none."));
                    }
                }
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

    private static void PopClosedTypes(Stack<(string Name, int Depth)> typeStack, int depth)
    {
        while (typeStack.Count > 0 && depth < typeStack.Peek().Depth)
        {
            typeStack.Pop();
        }
    }

    private static List<string> CollectRequirementIds(string attributeBlock, string leadingBlock, string body)
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

    private static List<string> CollectKernelRuleIds(string attributeBlock) =>
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
