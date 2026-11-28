using System.Collections.Generic;
using System.Text;

namespace Cadence.Compliance;

public static partial class RequirementTagScanner
{
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

    private static readonly HashSet<string> JsRegexLeadingKeywords = new(System.StringComparer.Ordinal)
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
}
