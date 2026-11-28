using System;
using System.Collections.Generic;

namespace Cadence.ToolingCore.Services;

public static partial class YamlSyntaxValidatorService
{
    private static bool ScanQuotedContinuation(
        string line,
        int lineNo,
        string displayPath,
        ref char activeQuote,
        ref int quoteStartLine,
        Stack<(char Open, int Line)> flowStack,
        List<string> errors)
    {
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (activeQuote == '"' && c == '\\')
            {
                if (!ValidateDoubleQuoteEscape(line, ref i, lineNo, displayPath, errors))
                {
                    return false;
                }

                i++;
                continue;
            }

            if (activeQuote == '\'' && c == '\'' && i + 1 < line.Length && line[i + 1] == '\'')
            {
                i += 2;
                continue;
            }

            if (c == activeQuote)
            {
                activeQuote = '\0';
                string trailing = line[(i + 1)..];
                return ValidateAfterClosedQuote(trailing, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors);
            }

            i++;
        }

        return true;
    }

    private static bool ScanExpression(
        string text,
        int lineNo,
        string displayPath,
        ref char activeQuote,
        ref int quoteStartLine,
        Stack<(char Open, int Line)> flowStack,
        List<string> errors,
        out bool hadTopLevelMappingColon)
    {
        hadTopLevelMappingColon = false;
        bool startsWithQuote = text.Length > 0 && text[0] is '"' or '\'' && flowStack.Count == 0;

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (activeQuote != '\0')
            {
                if (activeQuote == '"' && c == '\\')
                {
                    if (!ValidateDoubleQuoteEscape(text, ref i, lineNo, displayPath, errors))
                    {
                        return false;
                    }

                    i++;
                    continue;
                }

                if (activeQuote == '\'' && c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == activeQuote)
                {
                    activeQuote = '\0';
                    if (startsWithQuote && flowStack.Count == 0)
                    {
                        return ValidateAfterClosedQuote(text[(i + 1)..], lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors);
                    }
                }

                i++;
                continue;
            }

            if (c == '#' && (i == 0 || text[i - 1] is ' ' or '\t'))
            {
                return true;
            }

            if (c is '"' or '\'')
            {
                bool canOpenQuote = i == 0 || text[i - 1] is ' ' or '\t' or '[' or '{' or ',' or ':';
                if (canOpenQuote)
                {
                    activeQuote = c;
                    quoteStartLine = lineNo;
                }

                i++;
                continue;
            }

            if (c is '[' or '{')
            {
                if (flowStack.Count > 0 || i == 0 || (i > 0 && text[i - 1] == '$'))
                {
                    flowStack.Push((c, lineNo));
                }

                i++;
                continue;
            }

            if (c is ']' or '}')
            {
                if (flowStack.Count > 0)
                {
                    var (open, _) = flowStack.Peek();
                    char expected = open == '[' ? ']' : '}';
                    if (c != expected)
                    {
                        errors.Add($"{displayPath}:{lineNo}: Mismatched flow bracket '{c}' (expected '{expected}' to close '{open}').");
                        return false;
                    }

                    flowStack.Pop();
                }
                else if (i == 0)
                {
                    errors.Add($"{displayPath}:{lineNo}: Unexpected closing flow bracket '{c}'.");
                    return false;
                }

                i++;
                continue;
            }

            if (flowStack.Count == 0 && c == ':' && (i + 1 == text.Length || text[i + 1] is ' ' or '\t'))
            {
                hadTopLevelMappingColon = true;
            }

            i++;
        }

        return true;
    }

    private static bool ValidateAfterClosedQuote(
        string trailing,
        int lineNo,
        string displayPath,
        ref char activeQuote,
        ref int quoteStartLine,
        Stack<(char Open, int Line)> flowStack,
        List<string> errors)
    {
        if (flowStack.Count > 0)
        {
            return ScanExpression(trailing, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _);
        }

        string trimmed = trailing.TrimStart(' ', '\t');
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return true;
        }

        errors.Add($"{displayPath}:{lineNo}: Unexpected trailing content '{trimmed}' after quoted scalar.");
        return false;
    }

    private static bool ValidateDoubleQuoteEscape(string text, ref int backslashIndex, int lineNo, string displayPath, List<string> errors)
    {
        if (backslashIndex + 1 >= text.Length)
        {
            return true;
        }

        char next = text[backslashIndex + 1];
        int hexDigits = next switch
        {
            'x' => 2,
            'u' => 4,
            'U' => 8,
            _ => 0,
        };

        if (hexDigits > 0)
        {
            if (backslashIndex + 1 + hexDigits >= text.Length)
            {
                errors.Add($"{displayPath}:{lineNo}: Incomplete '\\{next}' Unicode escape sequence in double-quoted string.");
                return false;
            }

            for (int h = 0; h < hexDigits; h++)
            {
                if (!Uri.IsHexDigit(text[backslashIndex + 2 + h]))
                {
                    errors.Add($"{displayPath}:{lineNo}: Invalid hex digit in '\\{next}' escape sequence in double-quoted string.");
                    return false;
                }
            }

            backslashIndex += 1 + hexDigits;
            return true;
        }

        bool validSimple = next is '0' or 'a' or 'b' or 't' or '\t' or 'n' or 'v' or 'f' or 'r' or 'e' or ' ' or '"' or '/' or '\\' or 'N' or '_' or 'L' or 'P';
        if (!validSimple)
        {
            errors.Add($"{displayPath}:{lineNo}: Invalid escape sequence '\\{next}' in double-quoted string.");
            return false;
        }

        backslashIndex += 1;
        return true;
    }
}
