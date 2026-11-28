using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.Compliance;

public static partial class RequirementTagScanner
{
    [GeneratedRegex(@"GxPRequirement\s*\(([^)]*)\)")]
    private static partial Regex GxPRequirementRegex();

    [GeneratedRegex(@"Trait\s*\(\s*""Requirement""")]
    private static partial Regex RequirementTraitRegex();

    [GeneratedRegex(@"\bKernelRule(?:Attribute)?\s*\(\s*""([^""]*)""\s*\)")]
    private static partial Regex KernelRuleRegex();

    [GeneratedRegex(@"(?:\[|,)\s*(?:Xunit\.)?(?:Fact|Theory)(?:Attribute)?\s*(?:\(|,|\])")]
    private static partial Regex TestAttributeRegex();

    [GeneratedRegex(@"\b(?:class|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex TypeDeclarationRegex();

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z0-9_.]+)")]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"^(?:(?:public|internal|protected|private|static|async|override|virtual|sealed|new|unsafe)\s+)*[A-Za-z_][A-Za-z0-9_<>,\[\]?.\s]*?\s([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*\(")]
    private static partial Regex MethodSignatureRegex();

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
