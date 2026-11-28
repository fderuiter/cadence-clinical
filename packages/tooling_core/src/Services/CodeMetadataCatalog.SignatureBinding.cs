using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class CodeMetadataCatalog
{
    [GeneratedRegex(@"return\s+(""[^""]+""|[A-Za-z0-9_]+)\s*;")]
    private static partial Regex ReturnValueRegex();

    private static string ExtractFluentTailAfterMapCall(string source, int posInsideMapCall)
    {
        int mapCallEnd = ScanBalancedCode(source, posInsideMapCall, initialParenDepth: 1, initialBraceDepth: 0, stopAtParenZero: true);
        if (mapCallEnd < 0 || mapCallEnd >= source.Length)
        {
            return string.Empty;
        }

        int stmtEnd = ScanBalancedCode(source, mapCallEnd, initialParenDepth: 0, initialBraceDepth: 0, stopAtParenZero: false);
        return stmtEnd >= mapCallEnd ? source[mapCallEnd..(stmtEnd + 1)] : string.Empty;
    }

    private static int ScanBalancedCode(
        string source,
        int startIndex,
        int initialParenDepth,
        int initialBraceDepth,
        bool stopAtParenZero)
    {
        int parenDepth = initialParenDepth;
        int braceDepth = initialBraceDepth;
        int i = startIndex;

        while (i < source.Length)
        {
            int skipped = SkipCommentOrLiteral(source, i);
            if (skipped < 0)
            {
                return -1;
            }

            if (skipped > i)
            {
                i = skipped;
                continue;
            }

            char c = source[i];
            if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')')
            {
                if (parenDepth > 0)
                {
                    parenDepth--;
                }

                if (stopAtParenZero && parenDepth == 0 && braceDepth == 0)
                {
                    return i + 1;
                }
            }
            else if (c == '{')
            {
                braceDepth++;
            }
            else if (c == '}' && braceDepth > 0)
            {
                braceDepth--;
            }
            else if (!stopAtParenZero && c == ';' && parenDepth == 0 && braceDepth == 0)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    private static int SkipCommentOrLiteral(string source, int index)
    {
        char c = source[index];
        char next = index + 1 < source.Length ? source[index + 1] : '\0';

        if (c == '/' && next == '/')
        {
            int nl = source.IndexOf('\n', index + 2);
            return nl < 0 ? -1 : nl + 1;
        }

        if (c == '/' && next == '*')
        {
            int close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
            return close < 0 ? -1 : close + 2;
        }

        if (c == '@' && next == '"')
        {
            return SkipVerbatimString(source, index + 2);
        }

        if (c is '"' or '\'')
        {
            return SkipEscapedQuote(source, index + 1, c);
        }

        return index;
    }

    private static int SkipVerbatimString(string source, int cursor)
    {
        while (cursor < source.Length)
        {
            if (source[cursor] == '"')
            {
                if (cursor + 1 < source.Length && source[cursor + 1] == '"')
                {
                    cursor += 2;
                    continue;
                }

                return cursor + 1;
            }

            cursor++;
        }

        return source.Length;
    }

    private static int SkipEscapedQuote(string source, int cursor, char quote)
    {
        while (cursor < source.Length)
        {
            if (source[cursor] == '\\')
            {
                cursor += 2;
                continue;
            }

            if (source[cursor] == quote)
            {
                return cursor + 1;
            }

            cursor++;
        }

        return source.Length;
    }

    private static bool TryExtractInvocationArgs(
        string fluentTail,
        Regex callPrefixRegex,
        out IReadOnlyList<string> args)
    {
        var match = callPrefixRegex.Match(fluentTail);
        if (!match.Success)
        {
            args = [];
            return false;
        }

        int argStart = match.Index + match.Length;
        int parenDepth = 1;
        int braceDepth = 0;
        var parsed = new List<string>();
        int i = argStart;

        while (i < fluentTail.Length)
        {
            char c = fluentTail[i];
            if (c == '"')
            {
                i = SkipEscapedQuote(fluentTail, i + 1, '"');
                continue;
            }

            if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')')
            {
                parenDepth--;
                if (parenDepth == 0)
                {
                    string last = fluentTail[argStart..i].Trim();
                    if (last.Length > 0)
                    {
                        parsed.Add(last);
                    }

                    args = parsed;
                    return parsed.Count > 0;
                }
            }
            else if (c == '{')
            {
                braceDepth++;
            }
            else if (c == '}' && braceDepth > 0)
            {
                braceDepth--;
            }
            else if (c == ',' && parenDepth == 1 && braceDepth == 0)
            {
                parsed.Add(fluentTail[argStart..i].Trim());
                argStart = i + 1;
            }

            i++;
        }

        args = [];
        return false;
    }

    private static string ResolveSemanticActionsFromHelper(
        string source,
        string semArg,
        Dictionary<string, string> constants)
    {
        if (Regex.IsMatch(semArg, "^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
        {
            var headerRegex = new Regex(
                $@"private\s+static\s+string\s+{Regex.Escape(semArg)}\s*\([^)]*\)\s*\{{",
                RegexOptions.CultureInvariant);
            var headerMatch = headerRegex.Match(source);
            if (headerMatch.Success)
            {
                int bodyStart = headerMatch.Index + headerMatch.Length;
                int cursor = bodyStart;
                int braceDepth = 1;
                while (cursor < source.Length && braceDepth > 0)
                {
                    int skipped = SkipCommentOrLiteral(source, cursor);
                    if (skipped < 0)
                    {
                        break;
                    }

                    if (skipped > cursor)
                    {
                        cursor = skipped;
                        continue;
                    }

                    if (source[cursor] == '{')
                    {
                        braceDepth++;
                    }
                    else if (source[cursor] == '}')
                    {
                        braceDepth--;
                        if (braceDepth == 0)
                        {
                            break;
                        }
                    }

                    cursor++;
                }

                string body = source[bodyStart..cursor];
                var values = ReturnValueRegex().Matches(body)
                    .Select(m => StripQuotesAndInterpolation(m.Groups[1].Value, constants))
                    .Where(v => v.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (values.Count > 0)
                {
                    return string.Join(" / ", values);
                }
            }
        }

        return StripQuotesAndInterpolation(semArg, constants);
    }

    private static string ResolveSecondArgAction(
        string source,
        string secondArg,
        string fallbackRoute,
        Dictionary<string, string> constants,
        Dictionary<string, string> actionMethods)
    {
        int arrow = secondArg.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            string rhs = secondArg[(arrow + 2)..].Trim();
            if (constants.TryGetValue(rhs, out string? constVal))
            {
                return constVal;
            }

            foreach (var (methodName, tmpl) in actionMethods)
            {
                if (rhs.StartsWith(methodName + "(", StringComparison.Ordinal))
                {
                    return tmpl;
                }
            }
        }

        if (Regex.IsMatch(secondArg, "^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
        {
            string resolved = ResolveActionFromHelper(source, secondArg, constants, actionMethods);
            if (resolved.Length > 0)
            {
                return resolved;
            }
        }

        return fallbackRoute;
    }

    private static string ResolveActionFromHelper(
        string source,
        string helperName,
        Dictionary<string, string> constants,
        Dictionary<string, string> actionMethods)
    {
        var helperRegex = new Regex(
            $@"private\s+static\s+string\s+{Regex.Escape(helperName)}\s*\([^)]*\)\s*(?:=>|\{{)([\s\S]{{0,320}}?);",
            RegexOptions.CultureInvariant);
        var m = helperRegex.Match(source);
        if (m.Success)
        {
            string body = m.Groups[1].Value;
            foreach (var (methodName, tmpl) in actionMethods)
            {
                if (body.Contains(methodName + "(", StringComparison.Ordinal))
                {
                    return tmpl;
                }
            }

            foreach (var (constName, val) in constants)
            {
                if (body.Contains(constName, StringComparison.Ordinal))
                {
                    return val;
                }
            }
        }

        return string.Empty;
    }

    private static string StripQuotesAndInterpolation(string raw, Dictionary<string, string> constants)
    {
        string trimmed = raw.Trim();
        if (trimmed.StartsWith("$\"", StringComparison.Ordinal) && trimmed.EndsWith('"'))
        {
            string inner = trimmed[2..^1];
            foreach (var (key, val) in constants)
            {
                inner = inner.Replace($"{{{key}}}", val, StringComparison.Ordinal);
            }

            return inner;
        }

        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            return trimmed[1..^1];
        }

        return constants.GetValueOrDefault(trimmed, trimmed);
    }
}
