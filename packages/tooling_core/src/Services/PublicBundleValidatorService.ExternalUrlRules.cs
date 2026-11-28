using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class PublicBundleValidatorService
{
    private static readonly string[] ForbiddenExternalAssetHosts =
    [
        "images.unsplash.com",
        "unsplash.com",
        "img.shields.io",
        "shields.io",
        "cdn.jsdelivr.net",
        "unpkg.com",
        "cdnjs.cloudflare.com",
        "fonts.googleapis.com",
        "fonts.gstatic.com",
    ];

    [GeneratedRegex(@"url\(\s*(?<url>[^)]+)\)|@import\s+(?:url\(\s*)?(?<imp>""[^""]+""|'[^']+'|[^\s;)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlOrImportRegex();

    [GeneratedRegex(@"https?://[^\s""'`<>()\[\]{},;]+", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalScriptAssetUrlRegex();

    [GeneratedRegex(@"(?:https?|wss?)://(?:localhost(?::\d+|/[A-Za-z0-9_-])|(?:127\.0\.0\.1|0\.0\.0\.0|\[::1\])(?::\d+)?\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LocalBackendUrlRegex();

    private static void ScanExternalAssets(
        string displayPath,
        string content,
        string? liveMarkup,
        bool isHtml,
        bool isSvg,
        bool isCss,
        bool isScriptLike,
        bool isDocsSerializedProseChunk,
        List<PublicBundleFinding> findings)
    {
        if (isHtml || isSvg)
        {
            foreach (Match tagMatch in HtmlResourceTagRegex().Matches(liveMarkup!))
            {
                string tagName = tagMatch.Groups["tag"].Value.ToLowerInvariant();
                string attrs = tagMatch.Groups["attrs"].Value;

                if (tagName == "link" && !IsSubresourceLinkTag(attrs))
                {
                    continue;
                }

                foreach (GroupCollection attrGroups in ResourceAttributeRegex().Matches(attrs).Select(m => m.Groups))
                {
                    string attrName = attrGroups["name"].Value;
                    string rawVal = attrGroups["val"].Value.Trim();
                    if (ContainsExternalResourceUrl(rawVal, attrName))
                    {
                        (int line, int column) = LineAndColumn(content, tagMatch.Index);
                        findings.Add(new PublicBundleFinding(
                            displayPath,
                            line,
                            column,
                            "external_asset",
                            $"External resource reference in <{tagName}> attribute '{attrName}'"));
                    }
                }
            }

            foreach (Group styleGroup in InlineStyleContentRegex().Matches(liveMarkup!).Select(m => m.Groups["css"]))
            {
                ScanCssExternalUrls(displayPath, content, styleGroup.Value, styleGroup.Index, findings);
            }
        }
        else if (isCss)
        {
            ScanCssExternalUrls(displayPath, content, content, 0, findings);
        }
        else if (isScriptLike && !isDocsSerializedProseChunk)
        {
            foreach (Match extMatch in ExternalScriptAssetUrlRegex().Matches(content))
            {
                string url = extMatch.Value;
                if (IsForbiddenExternalBundleAssetUrl(url))
                {
                    (int line, int column) = LineAndColumn(content, extMatch.Index);
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        line,
                        column,
                        "external_asset",
                        "External media or CDN asset URL embedded in bundle script"));
                }
            }
        }
    }

    private static void ScanExecutableLocalUrls(
        string displayPath,
        string content,
        string? liveMarkup,
        bool isDocsBundle,
        bool isHtml,
        bool isSvg,
        bool isCss,
        bool isScriptLike,
        bool isDocsSerializedProseChunk,
        List<PublicBundleFinding> findings)
    {
        if (!isDocsBundle)
        {
            foreach (Match localMatch in LocalBackendUrlRegex().Matches(content))
            {
                (int line, int column) = LineAndColumn(content, localMatch.Index);
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    line,
                    column,
                    "executable_local_url",
                    "Executable local backend URL emitted in web bundle"));
            }
        }
        else if (isHtml)
        {
            foreach (Group bodyGroup in InlineScriptTagRegex().Matches(liveMarkup!).Select(m => m.Groups["body"]))
            {
                string scriptBody = bodyGroup.Value;
                foreach (Match localMatch in LocalBackendUrlRegex().Matches(scriptBody))
                {
                    (int line, int column) = LineAndColumn(content, bodyGroup.Index + localMatch.Index);
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        line,
                        column,
                        "executable_local_url",
                        "Executable local backend URL in inline portal <script>"));
                }
            }

            foreach (Group attrsGroup in HtmlResourceTagRegex().Matches(liveMarkup!).Select(m => m.Groups["attrs"]))
            {
                string attrs = attrsGroup.Value;
                foreach (Match localMatch in LocalBackendUrlRegex().Matches(attrs))
                {
                    (int line, int column) = LineAndColumn(content, attrsGroup.Index + localMatch.Index);
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        line,
                        column,
                        "executable_local_url",
                        "Executable local backend URL in HTML resource tag"));
                }
            }
        }
        else if (!isDocsSerializedProseChunk && (isScriptLike || isCss || isSvg))
        {
            foreach (Match localMatch in LocalBackendUrlRegex().Matches(content))
            {
                (int line, int column) = LineAndColumn(content, localMatch.Index);
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    line,
                    column,
                    "executable_local_url",
                    "Executable local backend URL in executable documentation runtime asset"));
            }
        }
    }

    private static void ScanCssExternalUrls(
        string displayPath,
        string fullContent,
        string cssSegment,
        int baseOffset,
        List<PublicBundleFinding> findings)
    {
        foreach (Match cssUrl in CssUrlOrImportRegex().Matches(cssSegment))
        {
            string rawUrl = (cssUrl.Groups["url"].Success ? cssUrl.Groups["url"].Value : cssUrl.Groups["imp"].Value)
                .Trim()
                .Trim('"', '\'');
            if (IsExternalUrl(rawUrl))
            {
                (int line, int column) = LineAndColumn(fullContent, baseOffset + cssUrl.Index);
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    line,
                    column,
                    "external_asset",
                    "External stylesheet or CSS url() asset in public bundle"));
            }
        }
    }

    private static bool IsSubresourceLinkTag(string attrs)
    {
        Match relMatch = LinkRelAttrRegex().Match(attrs);
        if (!relMatch.Success)
        {
            return true;
        }

        string relTokens = relMatch.Groups["rel"].Value.ToLowerInvariant();
        string[] subresourceRels = ["stylesheet", "preload", "modulepreload", "prefetch", "icon", "shortcut", "apple-touch-icon", "manifest"];
        return subresourceRels.Any(r => relTokens.Contains(r, StringComparison.Ordinal));
    }

    private static bool ContainsExternalResourceUrl(string attrValue, string attrName)
    {
        if (attrName.Equals("srcset", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string candidate in attrValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string firstToken = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                if (IsExternalUrl(firstToken))
                {
                    return true;
                }
            }

            return false;
        }

        return IsExternalUrl(attrValue);
    }

    private static bool IsExternalUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("//", StringComparison.Ordinal);

    private static bool IsForbiddenExternalBundleAssetUrl(string url)
    {
        string lower = url.TrimEnd('\'', '"', '`', ';', ',', ')', ']', '}').ToLowerInvariant();
        if (ForbiddenExternalAssetHosts.Any(h => lower.Contains(h, StringComparison.Ordinal)))
        {
            return true;
        }

        if (lower.Contains("github.com/fderuiter/cadence-clinical", StringComparison.Ordinal) ||
            (lower.Contains("://github.com/", StringComparison.Ordinal) && lower.Contains("/blob/", StringComparison.Ordinal)) ||
            lower.Contains("www.w3.org/", StringComparison.Ordinal))
        {
            return false;
        }

        int qIdx = lower.IndexOf('?', StringComparison.Ordinal);
        int hIdx = lower.IndexOf('#', StringComparison.Ordinal);
        int cut = qIdx >= 0 && hIdx >= 0 ? Math.Min(qIdx, hIdx) : Math.Max(qIdx, hIdx);
        string withoutQuery = (cut >= 0 ? lower[..cut] : lower).TrimEnd('\'', '"', '`', ';', ',', ')', ']', '}');
        string[] assetExts = [".js", ".mjs", ".cjs", ".css", ".wasm", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".svg", ".ico", ".woff", ".woff2", ".ttf", ".otf", ".mp4", ".webm", ".mp3"];
        return assetExts.Any(ext => withoutQuery.EndsWith(ext, StringComparison.Ordinal));
    }
}
