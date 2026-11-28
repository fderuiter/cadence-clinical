using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Primitives;

/// <summary>
/// Shared AOT-safe helpers for GitHub Actions <c>${{ ... }}</c> workflow expressions and logical clause splitting.
/// </summary>
internal static partial class WorkflowExpressionPrimitives
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceCollapseRegex();

    public static string CollapseWhitespace(string value) =>
        WhitespaceCollapseRegex().Replace(value.Trim(), " ");

    public static string UnwrapExpression(string expression)
    {
        string current = expression.Trim();
        if (current.StartsWith("${{", StringComparison.Ordinal) && current.EndsWith("}}", StringComparison.Ordinal))
        {
            current = current[3..^2].Trim();
        }

        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && HasBalancedOuterParentheses(current))
        {
            current = current[1..^1].Trim();
        }

        return current;
    }

    public static bool HasBalancedOuterParentheses(ReadOnlySpan<char> text)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            UpdateQuoteAndDepth(text[i], ref quote, ref depth);
            if (depth == 0 && i < text.Length - 1)
            {
                return false;
            }
        }

        return depth == 0 && quote == '\0';
    }

    public static List<string> SplitTopLevelLogical(string text, string op)
    {
        var parts = new List<string>();
        int depth = 0;
        char quote = '\0';
        int start = 0;

        int index = 0;
        while (index < text.Length)
        {
            UpdateQuoteAndDepth(text[index], ref quote, ref depth);
            if (quote == '\0' &&
                depth == 0 &&
                index + op.Length <= text.Length &&
                text.AsSpan(index, op.Length).SequenceEqual(op))
            {
                parts.Add(text[start..index].Trim());
                index += op.Length;
                start = index;
            }
            else
            {
                index++;
            }
        }

        parts.Add(text[start..].Trim());
        return parts;
    }

    public static IReadOnlyList<string> ExtractExpressions(string text)
    {
        var expressions = new List<string>();
        int searchFrom = 0;
        while (searchFrom < text.Length)
        {
            int open = text.IndexOf("${{", searchFrom, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            int close = FindExpressionClose(text, open + 3);
            if (close < 0)
            {
                break;
            }

            expressions.Add(text[(open + 3)..close].Trim());
            searchFrom = close + 2;
        }

        return expressions;
    }

    public static string StripExpressions(string text)
    {
        int firstOpen = text.IndexOf("${{", StringComparison.Ordinal);
        if (firstOpen < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        int cursor = 0;
        while (cursor < text.Length)
        {
            int open = text.IndexOf("${{", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(text, cursor, text.Length - cursor);
                break;
            }

            sb.Append(text, cursor, open - cursor);
            int close = FindExpressionClose(text, open + 3);
            if (close < 0)
            {
                sb.Append(text, open, text.Length - open);
                break;
            }

            cursor = close + 2;
        }

        return sb.ToString();
    }

    private static int FindExpressionClose(string text, int startIndex)
    {
        char quote = '\0';
        int i = startIndex;
        while (i + 1 < text.Length)
        {
            char ch = text[i];
            if (quote != '\0')
            {
                if (quote == '\'' && ch == '\'' && text[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (ch == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                i++;
                continue;
            }

            if (ch == '}' && text[i + 1] == '}')
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    private static void UpdateQuoteAndDepth(char ch, ref char quote, ref int depth)
    {
        if (quote != '\0')
        {
            if (ch == quote)
            {
                quote = '\0';
            }

            return;
        }

        if (ch is '\'' or '"')
        {
            quote = ch;
        }
        else if (ch == '(')
        {
            depth++;
        }
        else if (ch == ')' && depth > 0)
        {
            depth--;
        }
    }
}
