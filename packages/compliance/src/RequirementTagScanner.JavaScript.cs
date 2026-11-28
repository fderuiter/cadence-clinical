using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Compliance;

public static partial class RequirementTagScanner
{
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
}
