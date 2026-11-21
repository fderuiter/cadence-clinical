using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Deid;
using Cadence.ToolingCore.Security;

namespace Cadence.ToolingCore.Services;

/// <summary>A violation discovered in a built public bundle artifact; matched PHI or secret values are never printed.</summary>
public sealed record PublicBundleFinding(string File, int Line, int Column, string Category, string Reason);

/// <summary>
/// Post-build verifier (<c>check public-bundle</c> / <c>validate-public-bundle</c>) for public static build outputs
/// (<c>docs/.vitepress/dist</c>, <c>apps/web/dist</c>, and <c>apps/subject-portal/dist</c>). Every generated text file is
/// scanned with zero comment or pragma exemptions (<c>deid-ignore</c>, <c>deid: ignore</c>, and <c>pragma: allowlist</c>
/// are ignored). Rejects national identifiers, unapproved emails, secrets, external assets, executable local backend URLs,
/// and missing or permissive Content-Security-Policy meta directives.
/// </summary>
public static partial class PublicBundleValidatorService
{
    private static readonly HashSet<string> BundleTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".js", ".mjs", ".cjs", ".css", ".svg", ".json", ".xml", ".txt", ".webmanifest", ".map",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".ico", ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".pdf", ".zip", ".gz", ".tar", ".mp4", ".webm", ".mp3", ".wav", ".wasm",
    };

    private static readonly string[] DefaultBundles =
    [
        "docs/.vitepress/dist",
        "apps/web/dist",
        "apps/subject-portal/dist",
    ];

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

    /// <summary>
    /// Validates all built public bundles (<c>docs/.vitepress/dist</c>, <c>apps/web/dist</c>, and
    /// <c>apps/subject-portal/dist</c>, or explicit <paramref name="bundleDirectories"/>) and returns <c>0</c> only when
    /// every bundle exists, is non-empty, and passes all zero-exemption checks.
    /// </summary>
    public static int ValidatePublicBundles(string? repoRoot = null, IReadOnlyList<string>? bundleDirectories = null)
    {
        repoRoot ??= FindRepoRoot();
        var targets = bundleDirectories is { Count: > 0 } ? bundleDirectories : DefaultBundles;

        Console.WriteLine("Executing post-build public bundle verification (zero comment exemptions)...");

        var allFindings = new List<PublicBundleFinding>();
        int totalFilesScanned = 0;

        foreach (string bundleDir in targets)
        {
            string fullDir = Path.IsPathRooted(bundleDir) ? bundleDir : Path.Combine(repoRoot, bundleDir);
            string displayDir = Path.GetRelativePath(repoRoot, fullDir).Replace('\\', '/');

            if (!Directory.Exists(fullDir))
            {
                Console.WriteLine($"[PUBLIC BUNDLE ERROR] Required build output directory '{displayDir}' does not exist. Build the static bundles before running 'check public-bundle'.");
                return 1;
            }

            string rootIndex = Path.Combine(fullDir, "index.html");
            if (!File.Exists(rootIndex))
            {
                Console.WriteLine($"[PUBLIC BUNDLE ERROR] Required entrypoint '{displayDir}/index.html' is missing.");
                return 1;
            }

            var textFiles = EnumerateBundleTextFiles(fullDir).Order(StringComparer.Ordinal).ToList();
            if (textFiles.Count == 0)
            {
                Console.WriteLine($"[PUBLIC BUNDLE ERROR] Build output directory '{displayDir}' contains zero scannable text files.");
                return 1;
            }

            bool isDocsBundle = displayDir.StartsWith("docs/.vitepress/dist", StringComparison.OrdinalIgnoreCase) ||
                                fullDir.Replace('\\', '/').EndsWith("/docs/.vitepress/dist", StringComparison.OrdinalIgnoreCase);

            foreach (string file in textFiles)
            {
                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.WriteLine($"[PUBLIC BUNDLE ERROR] Could not read '{file}': {ex.Message}");
                    return 1;
                }

                totalFilesScanned++;
                string relativeToRepo = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                string relativeInsideBundle = Path.GetRelativePath(fullDir, file).Replace('\\', '/');
                bool fileIsDocsPortal = isDocsBundle && !IsStagedSubpathBundle(relativeInsideBundle);
                allFindings.AddRange(ScanBundleFile(relativeToRepo, relativeInsideBundle, content, fileIsDocsPortal));
            }
        }

        foreach (var finding in allFindings)
        {
            Console.WriteLine($"{finding.File}:{finding.Line}:{finding.Column}: [PUBLIC BUNDLE VIOLATION] Category: '{finding.Category}' — {finding.Reason}");
        }

        if (allFindings.Count > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {allFindings.Count} public bundle compliance/security violations across {totalFilesScanned} generated files.");
            return 1;
        }

        Console.WriteLine($"✔ Public bundle verification passed cleanly ({totalFilesScanned} generated text files scanned; 0 violations).");
        return 0;
    }

    /// <summary>
    /// Scans a single generated text file's content with zero comment or pragma exemptions.
    /// </summary>
    public static IReadOnlyList<PublicBundleFinding> ScanBundleFile(
        string displayPath,
        string relativeInsideBundle,
        string content,
        bool isDocsBundle)
    {
        ArgumentNullException.ThrowIfNull(displayPath);
        ArgumentNullException.ThrowIfNull(relativeInsideBundle);
        ArgumentNullException.ThrowIfNull(content);

        var findings = new List<PublicBundleFinding>();
        string extension = Path.GetExtension(relativeInsideBundle);
        bool isHtml = extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        bool isSvg = extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);
        bool isCss = extension.Equals(".css", StringComparison.OrdinalIgnoreCase);
        bool isScriptLike = extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".map", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".webmanifest", StringComparison.OrdinalIgnoreCase);
        bool isDocsSerializedProseChunk = isDocsBundle && IsDocsSerializedProseChunk(relativeInsideBundle);
        string? liveMarkup = isHtml || isSvg ? MaskCodeBlocksPreservingOffsets(content) : null;

        // 1. National identifiers and unapproved email addresses (zero comment/pragma exemptions).
        foreach (Match ssnMatch in BundleSsnOrNinRegex().Matches(content))
        {
            if (IsLikelyAssetHexToken(content, ssnMatch.Value, ssnMatch.Index, ssnMatch.Index + ssnMatch.Length))
            {
                continue;
            }

            (int line, int column) = LineAndColumn(content, ssnMatch.Index);
            findings.Add(new PublicBundleFinding(displayPath, line, column, DetectorCategory.SsnNationalId, "National identifier pattern detected in public bundle"));
        }

        if (content.Contains('@', StringComparison.Ordinal))
        {
            foreach (Match emailMatch in BundleEmailRegex().Matches(content))
            {
                if (!DeidComplianceService.IsApprovedSyntheticEmail(emailMatch.Value) &&
                    !IsLikelyPackageSpecifier(content, emailMatch.Value, emailMatch.Index))
                {
                    (int line, int column) = LineAndColumn(content, emailMatch.Index);
                    findings.Add(new PublicBundleFinding(displayPath, line, column, "unapproved_email", "Unapproved email domain in public bundle"));
                }
            }
        }

        // 2. Hardcoded secrets, private keys, cloud keys, and compliance tokens (zero pragma exemptions).
        string[] lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string lineText = lines[i];
            foreach (var (patternName, pattern) in SecretScanner.Patterns)
            {
                Match secretMatch = pattern.Match(lineText);
                if (secretMatch.Success)
                {
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        i + 1,
                        secretMatch.Index + 1,
                        "secret",
                        $"Secret pattern '{patternName}' detected in public bundle"));
                }
            }
        }

        // 3. External assets in HTML/SVG markup, CSS stylesheets, and JS bundles.
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

        // 4. Executable local backend URLs (localhost, 127.0.0.1, 0.0.0.0, [::1]).
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

        // 5. Content-Security-Policy enforcement on HTML pages.
        if (isHtml)
        {
            ValidateHtmlCsp(displayPath, content, findings);
        }

        return findings;
    }

    private static void ValidateHtmlCsp(string displayPath, string content, List<PublicBundleFinding> findings)
    {
        Match cspMeta = CspMetaTagRegex().Match(content);
        if (!cspMeta.Success)
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                1,
                1,
                "csp_policy",
                "Missing <meta http-equiv=\"Content-Security-Policy\"> tag on static HTML page"));
            return;
        }

        Match contentAttr = CspContentAttrRegex().Match(cspMeta.Value);
        if (!contentAttr.Success || string.IsNullOrWhiteSpace(contentAttr.Groups["policy"].Value))
        {
            (int line, int column) = LineAndColumn(content, cspMeta.Index);
            findings.Add(new PublicBundleFinding(
                displayPath,
                line,
                column,
                "csp_policy",
                "Empty Content-Security-Policy meta content attribute"));
            return;
        }

        string policy = contentAttr.Groups["policy"].Value;
        var directives = ParseCspDirectives(policy);
        (int cspLine, int cspCol) = LineAndColumn(content, cspMeta.Index);

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
                continue;
            }

            if (sources.Contains('*', StringComparison.Ordinal) ||
                sources.Contains("http:", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("https:", StringComparison.OrdinalIgnoreCase) ||
                sources.Contains("'unsafe-eval'", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    cspLine,
                    cspCol,
                    "csp_policy",
                    $"Content-Security-Policy directive '{directive}' permits external or unsafe-eval sources"));
            }
        }
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

    private static bool IsStagedSubpathBundle(string relativeInsideBundle)
    {
        string normalized = relativeInsideBundle.Replace('\\', '/');
        return normalized.StartsWith("demo/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("subject-portal/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDocsSerializedProseChunk(string relativeInsideBundle)
    {
        string normalized = relativeInsideBundle.Replace('\\', '/');
        if (normalized.Equals("hashmap.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(normalized);
        return fileName.Contains(".md.", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("@localSearchIndex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyAssetHexToken(string content, string matchValue, int matchStart, int matchEnd)
    {
        // Avoid misclassifying lowercase hex Vite chunk filenames as UK NINOs,
        // while always flagging dashed SSNs and uppercase or spaced UK NINOs.
        if (matchValue.Contains('-', StringComparison.Ordinal) || matchValue.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        if (!matchValue.All(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f')))
        {
            return false;
        }

        bool precededByAssetDelimiter = matchStart > 0 && content[matchStart - 1] is '.' or '-' or '/';
        bool followedByAssetDelimiter = matchEnd < content.Length && content[matchEnd] is '.' or '-';
        return precededByAssetDelimiter && followedByAssetDelimiter;
    }

    private static bool IsLikelyPackageSpecifier(string content, string matchValue, int matchStart)
    {
        // Scoped or versioned JS file paths such as `pkg@1.2.3` have digits right after `@`; real domains have letters in TLD,
        // and file-like endings `.js`/`.mjs`/`.css`/`.vue` preceded by `/` are module specifiers rather than emails.
        string lower = matchValue.ToLowerInvariant();
        if ((lower.EndsWith(".js", StringComparison.Ordinal) ||
             lower.EndsWith(".mjs", StringComparison.Ordinal) ||
             lower.EndsWith(".css", StringComparison.Ordinal) ||
             lower.EndsWith(".map", StringComparison.Ordinal)) &&
            matchStart > 0 && content[matchStart - 1] == '/')
        {
            return true;
        }

        return false;
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

    private static IEnumerable<string> EnumerateBundleTextFiles(string bundleDir)
    {
        foreach (string file in Directory.EnumerateFiles(bundleDir, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if ((ext.Length > 0 && BundleTextExtensions.Contains(ext)) ||
                (!BinaryExtensions.Contains(ext) && IsLikelyTextFile(file)))
            {
                yield return file;
            }
        }
    }

    private static bool IsLikelyTextFile(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var buffer = new byte[1024];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Array.IndexOf(buffer, (byte)0, 0, read) < 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (int Line, int Column) LineAndColumn(string content, int offset)
    {
        int line = 1;
        int lineStart = 0;
        int limit = Math.Min(offset, content.Length);
        for (int i = 0; i < limit; i++)
        {
            if (content[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, limit - lineStart + 1);
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }

        return Directory.GetCurrentDirectory();
    }

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b|\b[A-CEGHJ-PR-TW-Z][A-CEGHJ-NPR-TW-Z]\s*\d{2}\s*\d{2}\s*\d{2}\s*[A-D]\b", RegexOptions.IgnoreCase)]
    private static partial Regex BundleSsnOrNinRegex();

    [GeneratedRegex(@"(?<![a-zA-Z0-9._%+-])[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b")]
    private static partial Regex BundleEmailRegex();

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

    [GeneratedRegex(@"url\(\s*(?<url>[^)]+)\)|@import\s+(?:url\(\s*)?(?<imp>""[^""]+""|'[^']+'|[^\s;)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlOrImportRegex();

    [GeneratedRegex(@"https?://[^\s""'`<>()\[\]{},;]+", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalScriptAssetUrlRegex();

    [GeneratedRegex(@"(?:https?|wss?)://(?:localhost(?::\d+|/[A-Za-z0-9_-])|(?:127\.0\.0\.1|0\.0\.0\.0|\[::1\])(?::\d+)?\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LocalBackendUrlRegex();

    [GeneratedRegex(@"<meta\b[^>]*\bhttp-equiv\s*=\s*(?:""Content-Security-Policy""|'Content-Security-Policy'|Content-Security-Policy)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex CspMetaTagRegex();

    [GeneratedRegex(@"\bcontent\s*=\s*(?:""(?<policy>[^""]*)""|'(?<policy>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex CspContentAttrRegex();
}
