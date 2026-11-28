using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class YamlSyntaxValidatorService
{
    private enum BlockKind
    {
        Mapping,
        Sequence,
        Scalar,
    }

    private sealed record BlockFrame(int Indent, BlockKind Kind, HashSet<string>? Keys);

    [GeneratedRegex(@"^[|>](?:[+-][1-9]?|[1-9][+-]?)?(?:\s+#.*|\s*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidBlockScalarHeaderRegex();

    private static bool ProcessBlockSegment(
        string fullLine,
        int indent,
        int lineNo,
        string displayPath,
        Stack<BlockFrame> stack,
        Stack<(char Open, int Line)> flowStack,
        ref char activeQuote,
        ref int quoteStartLine,
        ref bool inBlockScalar,
        ref int blockScalarMinIndent,
        ref int? blockScalarDetectedIndent,
        ref bool allowChildIndent,
        ref bool lastMappingKeyHadEmptyValue,
        ref int lastMappingKeyIndent,
        List<string> errors)
    {
        string segment = fullLine[indent..];

        bool poppedDeeper = false;
        while (stack.Count > 0 && stack.Peek().Indent > indent)
        {
            stack.Pop();
            poppedDeeper = true;
        }

        bool isSeqItem = segment == "-" || (segment.Length > 1 && segment[0] == '-' && (segment[1] == ' ' || segment[1] == '\t'));
        bool isExplicitKey = segment == "?" || (segment.Length > 1 && segment[0] == '?' && (segment[1] == ' ' || segment[1] == '\t'));

        if (isSeqItem)
        {
            if (stack.Count > 0 && stack.Peek().Indent == indent)
            {
                var top = stack.Peek();
                if (top.Kind == BlockKind.Mapping && !(lastMappingKeyHadEmptyValue && lastMappingKeyIndent == indent))
                {
                    errors.Add($"{displayPath}:{lineNo}: Sequence item '-' cannot share indentation column {indent + 1} with mapping entries.");
                    return false;
                }
            }
            else if (poppedDeeper && stack.Count > 0 && stack.Peek().Indent < indent)
            {
                errors.Add($"{displayPath}:{lineNo}: Bad indentation at column {indent + 1} (does not match any outer block level).");
                return false;
            }
            else if (!poppedDeeper && stack.Count > 0 && stack.Peek().Indent < indent && !allowChildIndent)
            {
                errors.Add($"{displayPath}:{lineNo}: Unexpected nested sequence item at column {indent + 1}.");
                return false;
            }

            if (stack.Count == 0 || stack.Peek().Indent < indent)
            {
                stack.Push(new BlockFrame(indent, BlockKind.Sequence, null));
            }

            lastMappingKeyHadEmptyValue = false;
            lastMappingKeyIndent = -1;

            string afterDash = segment.Length > 1 ? segment[1..] : "";
            int spacesAfterDash = YamlPrimitives.CountLeadingSpaces(afterDash);
            if (spacesAfterDash < afterDash.Length && afterDash[spacesAfterDash] == '\t')
            {
                int ws = 0;
                while (ws < afterDash.Length && (afterDash[ws] == ' ' || afterDash[ws] == '\t'))
                {
                    ws++;
                }

                spacesAfterDash = ws;
            }

            string itemText = afterDash[spacesAfterDash..];
            if (itemText.Length == 0 || itemText.StartsWith('#'))
            {
                allowChildIndent = true;
                return true;
            }

            int itemIndent = indent + 1 + spacesAfterDash;
            allowChildIndent = true;
            return ProcessBlockSegment(
                new string(' ', itemIndent) + itemText,
                itemIndent,
                lineNo,
                displayPath,
                stack,
                flowStack,
                ref activeQuote,
                ref quoteStartLine,
                ref inBlockScalar,
                ref blockScalarMinIndent,
                ref blockScalarDetectedIndent,
                ref allowChildIndent,
                ref lastMappingKeyHadEmptyValue,
                ref lastMappingKeyIndent,
                errors);
        }

        if (isExplicitKey)
        {
            if (stack.Count == 0 || stack.Peek().Indent < indent)
            {
                stack.Push(new BlockFrame(indent, BlockKind.Mapping, new HashSet<string>(StringComparer.Ordinal)));
            }

            allowChildIndent = true;
            lastMappingKeyHadEmptyValue = true;
            lastMappingKeyIndent = indent;
            string afterQ = segment.Length > 1 ? segment[1..].TrimStart(' ', '\t') : "";
            if (afterQ.Length > 0 && !afterQ.StartsWith('#'))
            {
                return ScanExpression(afterQ, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _);
            }

            return true;
        }

        if (TrySplitBlockMapping(segment, lineNo, displayPath, errors, out string? keyToken, out string? valuePart, out bool fatalError))
        {
            if (fatalError)
            {
                return false;
            }

            if (stack.Count > 0 && stack.Peek().Indent == indent)
            {
                var top = stack.Peek();
                if (top.Kind != BlockKind.Mapping)
                {
                    errors.Add($"{displayPath}:{lineNo}: Mapping entry '{keyToken}:' cannot share indentation column {indent + 1} with sequence items.");
                    return false;
                }
            }
            else if (poppedDeeper)
            {
                errors.Add($"{displayPath}:{lineNo}: Bad indentation at column {indent + 1} (does not match any outer mapping level).");
                return false;
            }
            else if (stack.Count > 0 && stack.Peek().Indent < indent && !allowChildIndent)
            {
                errors.Add($"{displayPath}:{lineNo}: Unexpected indented mapping entry '{keyToken}:' at column {indent + 1}.");
                return false;
            }

            if (stack.Count == 0 || stack.Peek().Indent < indent)
            {
                stack.Push(new BlockFrame(indent, BlockKind.Mapping, new HashSet<string>(StringComparer.Ordinal)));
            }

            var mappingFrame = stack.Peek();
            string normalizedKey = NormalizeKey(keyToken!);
            if (normalizedKey.Length == 0)
            {
                errors.Add($"{displayPath}:{lineNo}: Empty mapping key before ':'.");
                return false;
            }

            if (IsReservedPlainIndicator(keyToken!))
            {
                errors.Add($"{displayPath}:{lineNo}: Plain scalar key '{keyToken}' cannot start with reserved indicator '{keyToken![0]}'.");
                return false;
            }

            if (normalizedKey != "<<" && mappingFrame.Keys is not null && !mappingFrame.Keys.Add(normalizedKey))
            {
                errors.Add($"{displayPath}:{lineNo}: Duplicate mapping key '{normalizedKey}'.");
                return false;
            }

            string trimmedValue = (valuePart ?? "").TrimStart(' ', '\t');
            if (trimmedValue.Length == 0 || trimmedValue.StartsWith('#'))
            {
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = true;
                lastMappingKeyIndent = indent;
                return true;
            }

            lastMappingKeyHadEmptyValue = false;
            lastMappingKeyIndent = -1;

            string valueAfterProps = StripLeadingNodeProperties(trimmedValue);
            if (valueAfterProps.Length == 0 || valueAfterProps.StartsWith('#'))
            {
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = true;
                lastMappingKeyIndent = indent;
                return true;
            }

            if (valueAfterProps[0] is '|' or '>')
            {
                if (!StartsBlockScalar(valueAfterProps, displayPath, lineNo, errors, out int explicitIndent))
                {
                    return false;
                }

                inBlockScalar = true;
                blockScalarMinIndent = explicitIndent > 0 ? indent + explicitIndent : indent + 1;
                blockScalarDetectedIndent = explicitIndent > 0 ? indent + explicitIndent : null;
                allowChildIndent = false;
                return true;
            }

            if (IsReservedPlainIndicator(valueAfterProps))
            {
                errors.Add($"{displayPath}:{lineNo}: Plain scalar value cannot start with reserved indicator '{valueAfterProps[0]}'.");
                return false;
            }

            if (!ScanExpression(valueAfterProps, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out bool hadNestedUnquotedMappingColon))
            {
                return false;
            }

            if (hadNestedUnquotedMappingColon)
            {
                errors.Add($"{displayPath}:{lineNo}: Nested mapping colon ':' is not allowed in an unquoted mapping value on the same line.");
                return false;
            }

            bool startsQuotedOrFlow = valueAfterProps[0] is '"' or '\'' or '[' or '{';
            allowChildIndent = !startsQuotedOrFlow || activeQuote != '\0' || flowStack.Count > 0;
            return true;
        }

        if (fatalError)
        {
            return false;
        }

        if (IsReservedPlainIndicator(segment))
        {
            errors.Add($"{displayPath}:{lineNo}: Plain scalar cannot start with reserved indicator '{segment[0]}'.");
            return false;
        }

        if (stack.Count > 0)
        {
            var top = stack.Peek();
            if (indent <= top.Indent && top.Kind != BlockKind.Scalar)
            {
                errors.Add($"{displayPath}:{lineNo}: Expected mapping key or sequence item at column {indent + 1}.");
                return false;
            }

            if (indent > top.Indent && !allowChildIndent)
            {
                errors.Add($"{displayPath}:{lineNo}: Unexpected indented scalar at column {indent + 1}.");
                return false;
            }
        }
        else
        {
            stack.Push(new BlockFrame(indent, BlockKind.Scalar, null));
        }

        if (segment[0] is '|' or '>')
        {
            if (!StartsBlockScalar(segment, displayPath, lineNo, errors, out int explicitIndent))
            {
                return false;
            }

            inBlockScalar = true;
            blockScalarMinIndent = explicitIndent > 0 ? indent + explicitIndent : indent + 1;
            blockScalarDetectedIndent = explicitIndent > 0 ? indent + explicitIndent : null;
            allowChildIndent = false;
            return true;
        }

        return ScanExpression(segment, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _);
    }

    private static bool TrySplitBlockMapping(
        string segment,
        int lineNo,
        string displayPath,
        List<string> errors,
        out string? keyToken,
        out string? valuePart,
        out bool fatalError)
    {
        keyToken = null;
        valuePart = null;
        fatalError = false;

        if (segment[0] is '"' or '\'')
        {
            char q = segment[0];
            int quoteIndex = 1;
            while (quoteIndex < segment.Length)
            {
                if (q == '"' && segment[quoteIndex] == '\\')
                {
                    if (!ValidateDoubleQuoteEscape(segment, ref quoteIndex, lineNo, displayPath, errors))
                    {
                        fatalError = true;
                        return false;
                    }

                    quoteIndex++;
                    continue;
                }

                if (q == '\'' && segment[quoteIndex] == '\'' && quoteIndex + 1 < segment.Length && segment[quoteIndex + 1] == '\'')
                {
                    quoteIndex += 2;
                    continue;
                }

                if (segment[quoteIndex] == q)
                {
                    int afterQuote = quoteIndex + 1;
                    while (afterQuote < segment.Length && (segment[afterQuote] == ' ' || segment[afterQuote] == '\t'))
                    {
                        afterQuote++;
                    }

                    if (afterQuote < segment.Length && segment[afterQuote] == ':' &&
                        (afterQuote + 1 == segment.Length || segment[afterQuote + 1] == ' ' || segment[afterQuote + 1] == '\t'))
                    {
                        keyToken = segment[..(quoteIndex + 1)];
                        valuePart = segment[(afterQuote + 1)..];
                        return true;
                    }

                    return false;
                }

                quoteIndex++;
            }

            return false;
        }

        int flowDepth = 0;
        char inlineQuote = '\0';
        int i = 0;
        while (i < segment.Length)
        {
            char c = segment[i];
            if (inlineQuote != '\0')
            {
                if (inlineQuote == '"' && c == '\\')
                {
                    i += 2;
                    continue;
                }

                if (inlineQuote == '\'' && c == '\'' && i + 1 < segment.Length && segment[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == inlineQuote)
                {
                    inlineQuote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'' && flowDepth > 0)
            {
                inlineQuote = c;
                i++;
                continue;
            }

            if (c is '[' or '{')
            {
                flowDepth++;
                i++;
                continue;
            }

            if (c is ']' or '}' && flowDepth > 0)
            {
                flowDepth--;
                i++;
                continue;
            }

            if (flowDepth == 0 && c == '#' && (i == 0 || segment[i - 1] is ' ' or '\t'))
            {
                break;
            }

            if (flowDepth == 0 && c == ':' && (i + 1 == segment.Length || segment[i + 1] is ' ' or '\t'))
            {
                keyToken = segment[..i].Trim();
                valuePart = segment[(i + 1)..];
                return true;
            }

            i++;
        }

        return false;
    }

    private static bool StartsBlockScalar(string value, string displayPath, int lineNo, List<string> errors, out int explicitIndent)
    {
        explicitIndent = 0;
        if (!ValidBlockScalarHeaderRegex().IsMatch(value))
        {
            errors.Add($"{displayPath}:{lineNo}: Invalid block scalar header '{value}'.");
            return false;
        }

        for (int i = 1; i < value.Length && !char.IsWhiteSpace(value[i]) && value[i] != '#'; i++)
        {
            if (value[i] is >= '1' and <= '9')
            {
                explicitIndent = value[i] - '0';
            }
        }

        return true;
    }

    private static string StripLeadingNodeProperties(string value)
    {
        string current = value;
        while (current.Length > 0 && (current[0] == '&' || current[0] == '!'))
        {
            int space = 0;
            while (space < current.Length && current[space] is not ' ' and not '\t')
            {
                space++;
            }

            current = current[space..].TrimStart(' ', '\t');
        }

        return current;
    }

    private static bool IsReservedPlainIndicator(string token) =>
        token.Length > 0 && token[0] is '@' or '`';

    private static string NormalizeKey(string rawKey)
    {
        string trimmed = rawKey.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
