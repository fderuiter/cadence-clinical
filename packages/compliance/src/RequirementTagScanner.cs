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

/// <summary>
/// Finds xUnit test methods in C# source and the requirement IDs each one declares.
/// <para>
/// A requirement counts only when it is declared on the test method itself, through any of:
/// <c>[GxPRequirement("…")]</c>, <c>[Trait("Requirement", "…")]</c>, or an <c>@req:</c> line in the method's
/// doc comment (above the method or inside its body). Class-level tags are documentation and are ignored,
/// so a class comment can never mark every test in the file as verifying a requirement.
/// </para>
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

    [GeneratedRegex(@"Trait\s*\(\s*""Requirement""\s*,\s*""([^""]+)""\s*\)")]
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

    public static IReadOnlyList<string> ExtractRequirementIds(string text) =>
        RequirementIdRegex().Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Scans every <c>*.cs</c> file under the given directories (skipping <c>bin</c> and <c>obj</c>).</summary>
    public static IReadOnlyList<DiscoveredTest> ScanDirectories(string repoRoot, IEnumerable<string> searchDirs)
    {
        var tests = new List<DiscoveredTest>();
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
                tests.AddRange(ScanSource(File.ReadAllText(file), relPath));
            }
        }

        return tests;
    }

    private static bool IsBuildOutput(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');
        return segments.Any(s => s is "bin" or "obj" or "node_modules");
    }

    /// <summary>Scans one C# source file.</summary>
    public static IReadOnlyList<DiscoveredTest> ScanSource(string source, string relativePath)
    {
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        string[] code = StripCommentsAndStrings(lines);

        var results = new List<DiscoveredTest>();
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

        return results;
    }

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

        foreach (Match m in RequirementTraitRegex().Matches(attributeBlock).Cast<Match>())
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
