using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using Cadence.Rules.Expressions;

namespace Cadence.Ddf;

internal enum OdmExpressionTokenKind
{
    Identifier,
    Number,
    String,
    Symbol,
}

internal readonly record struct OdmExpressionToken(OdmExpressionTokenKind Kind, string Text);

internal sealed class OdmExpressionLimitExceededException(string message) : FormatException(message);

internal static partial class OdmFormalExpressionTokenizer
{
    public static string NormalizeItemDataXPath(string rawExpression) =>
        ItemDataXPathRegex().Replace(rawExpression, m => m.Groups["oid"].Value);

    public static List<OdmExpressionToken> Tokenize(string input)
    {
        if (input.Length > OdmFormalExpressionParser.MaxExpressionLength)
        {
            throw new OdmExpressionLimitExceededException(
                $"FormalExpression exceeds the maximum length of {OdmFormalExpressionParser.MaxExpressionLength} characters.");
        }

        var tokens = new List<OdmExpressionToken>();
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                char quote = c;
                i++;
                var sb = new StringBuilder();
                while (i < input.Length && input[i] != quote)
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        i++;
                    }

                    if (sb.Length >= RuleExpressionJson.MaxTextLength)
                    {
                        throw new OdmExpressionLimitExceededException(
                            $"String literal in FormalExpression exceeds {RuleExpressionJson.MaxTextLength} characters.");
                    }

                    sb.Append(input[i]);
                    i++;
                }

                if (i >= input.Length)
                {
                    throw new FormatException("Unterminated string literal in FormalExpression.");
                }

                i++;
                AddToken(tokens, new OdmExpressionToken(OdmExpressionTokenKind.String, sb.ToString()));
                continue;
            }

            if (i + 1 < input.Length)
            {
                var two = input.Substring(i, 2);
                if (two is "==" or "!=" or "<>" or "<=" or ">=" or "&&" or "||")
                {
                    AddToken(tokens, new OdmExpressionToken(OdmExpressionTokenKind.Symbol, two));
                    i += 2;
                    continue;
                }
            }

            if (c is '(' or ')' or ',' or '+' or '-' or '*' or '/' or '<' or '>' or '=' or '!')
            {
                AddToken(tokens, new OdmExpressionToken(OdmExpressionTokenKind.Symbol, c.ToString()));
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < input.Length && char.IsAsciiDigit(input[i]))
                {
                    i++;
                }

                if (i < input.Length && input[i] == '.' && i + 1 < input.Length && char.IsAsciiDigit(input[i + 1]))
                {
                    i++;
                    while (i < input.Length && char.IsAsciiDigit(input[i]))
                    {
                        i++;
                    }
                }

                AddToken(tokens, new OdmExpressionToken(OdmExpressionTokenKind.Number, input[start..i]));
                continue;
            }

            if (c == '$' && i + 1 < input.Length && (char.IsAsciiLetter(input[i + 1]) || input[i + 1] == '_'))
            {
                i++;
                c = input[i];
            }

            if (c == '.' && (i + 1 >= input.Length || !char.IsAsciiLetterOrDigit(input[i + 1])))
            {
                AddToken(tokens, new OdmExpressionToken(OdmExpressionTokenKind.Identifier, "."));
                i++;
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < input.Length)
                {
                    char ch = input[i];
                    if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or ':' or '/')
                    {
                        i++;
                        if (i - start > RuleExpressionJson.MaxTextLength)
                        {
                            throw new OdmExpressionLimitExceededException(
                                $"Identifier in FormalExpression exceeds {RuleExpressionJson.MaxTextLength} characters.");
                        }

                        continue;
                    }

                    if (ch == '-' && i + 1 < input.Length && char.IsAsciiLetterOrDigit(input[i + 1]))
                    {
                        i++;
                        if (i - start > RuleExpressionJson.MaxTextLength)
                        {
                            throw new OdmExpressionLimitExceededException(
                                $"Identifier in FormalExpression exceeds {RuleExpressionJson.MaxTextLength} characters.");
                        }

                        continue;
                    }

                    break;
                }

                AddToken(tokens, new OdmExpressionToken(OdmExpressionTokenKind.Identifier, input[start..i]));
                continue;
            }

            throw new FormatException($"Unsupported character '{c}' in FormalExpression.");
        }

        return tokens;
    }

    private static void AddToken(List<OdmExpressionToken> tokens, OdmExpressionToken token)
    {
        if (tokens.Count >= OdmFormalExpressionParser.MaxTokens)
        {
            throw new OdmExpressionLimitExceededException(
                $"FormalExpression exceeds the maximum of {OdmFormalExpressionParser.MaxTokens} tokens.");
        }

        tokens.Add(token);
    }

    [GeneratedRegex(@"ItemData\s*\[\s*@ItemOID\s*=\s*['""](?<oid>[^'""]+)['""]\s*\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ItemDataXPathRegex();
}
