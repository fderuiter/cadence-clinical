using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class PublicBundleValidatorService
{
    [GeneratedRegex(@"<pre\b[^>]*>[\s\S]*?</pre>", RegexOptions.IgnoreCase)]
    private static partial Regex PreBlockRegex();

    [GeneratedRegex(@"<code\b[^>]*>[\s\S]*?</code>", RegexOptions.IgnoreCase)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"<(?<tag>script|link|img|source|video|audio|iframe|embed|object|image|use)\b(?<attrs>[^>]*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlResourceTagRegex();

    [GeneratedRegex(@"\b(?<name>src|href|xlink:href|poster|data|srcset)\s*=\s*(?:""(?<val>[^""]*)""|'(?<val>[^']*)'|(?<val>[^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceAttributeRegex();

    [GeneratedRegex(@"\brel\s*=\s*(?:""(?<rel>[^""]*)""|'(?<rel>[^']*)'|(?<rel>[^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRelAttrRegex();

    [GeneratedRegex(@"<style\b[^>]*>(?<css>[\s\S]*?)</style>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleContentRegex();

    [GeneratedRegex(@"<script\b(?![^>]*\bsrc\s*=)(?<attrs>[^>]*)>(?<body>[\s\S]*?)</script>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptTagRegex();

    [GeneratedRegex(@"<meta\b[^>]*\bhttp-equiv\s*=\s*(?:""Content-Security-Policy""|'Content-Security-Policy'|Content-Security-Policy)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex CspMetaTagRegex();

    [GeneratedRegex(@"\bcontent\s*=\s*(?:""(?<policy>[^""]*)""|'(?<policy>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex CspContentAttrRegex();

    [GeneratedRegex(@"<(?:script|link)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrLinkOpenTagRegex();

    [GeneratedRegex(@"</head\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadCloseTagRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"<script\b[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex FullScriptBlockRegex();

    [GeneratedRegex(@"<(?<tag>[a-zA-Z][a-zA-Z0-9:-]*)(?<attrs>(?:""[^""]*""|'[^']*'|[^""'>])*)/?>")]
    private static partial Regex AnyOpenTagRegex();

    [GeneratedRegex(@"\G[\s/]*(?<name>[a-zA-Z_:@][a-zA-Z0-9_.:@-]*)(?<hasEq>\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'=<>`]*))?")]
    private static partial Regex SequentialHtmlAttributeRegex();

    private static void ValidateHtmlCsp(
        string displayPath,
        string content,
        string liveMarkup,
        List<PublicBundleFinding> findings)
    {
        MatchCollection cspMatches = CspMetaTagRegex().Matches(liveMarkup);
        if (cspMatches.Count == 0)
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                1,
                1,
                "csp_policy",
                "Missing <meta http-equiv=\"Content-Security-Policy\"> tag on static HTML page"));
            return;
        }

        if (cspMatches.Count > 1)
        {
            (int dupLine, int dupCol) = LineAndColumn(content, cspMatches[1].Index);
            findings.Add(new PublicBundleFinding(
                displayPath,
                dupLine,
                dupCol,
                "csp_policy",
                "Multiple <meta http-equiv=\"Content-Security-Policy\"> tags found; emit a single hoisted policy in <head>"));
        }

        Match cspMeta = cspMatches[0];
        (int cspLine, int cspCol) = LineAndColumn(content, cspMeta.Index);

        Match headClose = HeadCloseTagRegex().Match(liveMarkup);
        if (headClose.Success && cspMeta.Index > headClose.Index)
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                cspLine,
                cspCol,
                "csp_policy",
                "<meta http-equiv=\"Content-Security-Policy\"> must reside inside <head>"));
        }

        Match precedingScriptOrLink = ScriptOrLinkOpenTagRegex().Match(liveMarkup, 0, cspMeta.Index);
        if (precedingScriptOrLink.Success)
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                cspLine,
                cspCol,
                "csp_policy",
                "<meta http-equiv=\"Content-Security-Policy\"> must precede all <script> and <link> tags in <head>"));
        }

        Match contentAttr = CspContentAttrRegex().Match(cspMeta.Value);
        if (!contentAttr.Success || string.IsNullOrWhiteSpace(contentAttr.Groups["policy"].Value))
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                cspLine,
                cspCol,
                "csp_policy",
                "Empty Content-Security-Policy meta content attribute"));
            return;
        }

        string policy = contentAttr.Groups["policy"].Value;
        var directives = ParseCspDirectives(policy);

        string[] requiredSameOriginDirectives = ["default-src", "script-src", "media-src", "worker-src", "connect-src"];
        foreach (string directive in requiredSameOriginDirectives)
        {
            if (!directives.TryGetValue(directive, out string? sources) ||
                !sources.Contains("'self'", StringComparison.Ordinal))
            {
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    cspLine,
                    cspCol,
                    "csp_policy",
                    $"Content-Security-Policy must restrict '{directive}' to 'self'"));
            }
        }

        if (directives.TryGetValue("script-src", out string? scriptSources) &&
            (scriptSources.Contains("'unsafe-inline'", StringComparison.OrdinalIgnoreCase) ||
             scriptSources.Contains("'unsafe-hashes'", StringComparison.OrdinalIgnoreCase) ||
             scriptSources.Contains("blob:", StringComparison.OrdinalIgnoreCase) ||
             scriptSources.Contains("data:", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                cspLine,
                cspCol,
                "csp_policy",
                "Content-Security-Policy directive 'script-src' must never permit 'unsafe-inline', 'unsafe-hashes', blob:, or data:"));
        }

        if (directives.TryGetValue("frame-src", out string? frameSources))
        {
            if (!frameSources.Contains("'self'", StringComparison.Ordinal))
            {
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    cspLine,
                    cspCol,
                    "csp_policy",
                    "Content-Security-Policy directive 'frame-src' must include 'self'"));
            }

            if (frameSources.Contains("data:", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    cspLine,
                    cspCol,
                    "csp_policy",
                    "Content-Security-Policy directive 'frame-src' must never permit data: URLs"));
            }
        }

        foreach ((string directive, string sources) in directives)
        {
            if (sources.Contains('*', StringComparison.Ordinal) ||
                sources.Contains("http:", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("https:", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("ws:", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("wss:", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("'unsafe-eval'", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("127.0.0.1", StringComparison.Ordinal) ||
                sources.Contains("0.0.0.0", StringComparison.Ordinal) ||
                sources.Contains("[::1]", StringComparison.Ordinal))
            {
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    cspLine,
                    cspCol,
                    "csp_policy",
                    $"Content-Security-Policy directive '{directive}' permits external, localhost, wildcard, or unsafe-eval sources"));
            }
        }
    }

    private static void ScanInlineEventAttributes(
        string displayPath,
        string fullContent,
        string liveMarkup,
        List<PublicBundleFinding> findings)
    {
        string markupWithoutNonElementBodies = MaskScriptStyleAndCommentBlocksPreservingOffsets(liveMarkup);
        foreach (Match openTag in AnyOpenTagRegex().Matches(markupWithoutNonElementBodies))
        {
            Group attrsGroup = openTag.Groups["attrs"];
            if (attrsGroup.Length == 0)
            {
                continue;
            }

            string attrs = attrsGroup.Value;
            int pos = 0;
            while (pos < attrs.Length)
            {
                Match attrMatch = SequentialHtmlAttributeRegex().Match(attrs, pos);
                if (!attrMatch.Success || attrMatch.Length == 0)
                {
                    break;
                }

                pos = attrMatch.Index + attrMatch.Length;
                if (!attrMatch.Groups["hasEq"].Success)
                {
                    continue;
                }

                Group nameGroup = attrMatch.Groups["name"];
                if (IsInlineEventHandlerAttributeName(nameGroup.Value))
                {
                    (int line, int col) = LineAndColumn(fullContent, attrsGroup.Index + nameGroup.Index);
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        line,
                        col,
                        "csp_policy",
                        $"Inline event handler attribute '{nameGroup.Value}' is forbidden on public HTML/SVG markup"));
                }
            }
        }
    }

    private static bool IsInlineEventHandlerAttributeName(string attrName)
    {
        if (attrName.Length <= 2 || !attrName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (int i = 2; i < attrName.Length; i++)
        {
            if (!char.IsAsciiLetter(attrName[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string MaskScriptStyleAndCommentBlocksPreservingOffsets(string markup)
    {
        char[] buffer = markup.ToCharArray();
        MaskMatchesInPlace(buffer, HtmlCommentRegex().Matches(markup));
        MaskMatchesInPlace(buffer, FullScriptBlockRegex().Matches(markup));
        MaskMatchesInPlace(buffer, InlineStyleContentRegex().Matches(markup));
        return new string(buffer);
    }

    private static Dictionary<string, string> ParseCspDirectives(string policy)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string segment in policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int spaceIdx = segment.IndexOf(' ', StringComparison.Ordinal);
            if (spaceIdx > 0)
            {
                map[segment[..spaceIdx].Trim()] = segment[(spaceIdx + 1)..].Trim();
            }
            else
            {
                map[segment] = string.Empty;
            }
        }

        return map;
    }

    private static string MaskCodeBlocksPreservingOffsets(string html)
    {
        if (!html.Contains("<pre", StringComparison.OrdinalIgnoreCase) &&
            !html.Contains("<code", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        char[] buffer = html.ToCharArray();
        MaskMatchesInPlace(buffer, PreBlockRegex().Matches(html));
        MaskMatchesInPlace(buffer, CodeBlockRegex().Matches(html));
        return new string(buffer);
    }

    private static void MaskMatchesInPlace(char[] buffer, MatchCollection matches)
    {
        foreach (Match match in matches)
        {
            int end = match.Index + match.Length;
            for (int i = match.Index; i < end; i++)
            {
                if (buffer[i] != '\n')
                {
                    buffer[i] = ' ';
                }
            }
        }
    }
}
