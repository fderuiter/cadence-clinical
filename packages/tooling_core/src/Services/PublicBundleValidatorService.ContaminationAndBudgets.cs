using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class PublicBundleValidatorService
{
    [GeneratedRegex(@"^(?:axe|axe-core)(?:[-.][A-Za-z0-9_-]+)?\.(?:m?js|cjs)$", RegexOptions.IgnoreCase)]
    private static partial Regex AxeChunkFileNameRegex();

    [GeneratedRegex(@"^(?:keycloak|keycloak-js|vendor-auth)(?:[-.][A-Za-z0-9_-]+)?\.(?:m?js|cjs)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeycloakChunkFileNameRegex();

    [GeneratedRegex(@"^(?:vendor-auth|vendor-media|keycloak|keycloak-js|axe|axe-core|hls)(?:[-.][A-Za-z0-9_-]+)?\.(?:m?js|cjs)$", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenDemoPreloadFileNameRegex();

    private static void ValidateHtmlBundlePreloadsAndScripts(
        string displayPath,
        string content,
        string liveMarkup,
        bool isDocsBundle,
        bool isDemoBundle,
        List<PublicBundleFinding> findings)
    {
        if (isDocsBundle && !isDemoBundle)
        {
            return;
        }

        foreach (Match tagMatch in HtmlResourceTagRegex().Matches(liveMarkup))
        {
            string tagName = tagMatch.Groups["tag"].Value.ToLowerInvariant();
            if (tagName is not ("script" or "link"))
            {
                continue;
            }

            string attrs = tagMatch.Groups["attrs"].Value;
            bool isModulePreload = tagName == "link" &&
                                   LinkRelAttrRegex().Match(attrs) is { Success: true } relMatch &&
                                   relMatch.Groups["rel"].Value.Contains("modulepreload", StringComparison.OrdinalIgnoreCase);

            foreach (GroupCollection attrGroups in ResourceAttributeRegex().Matches(attrs).Select(m => m.Groups))
            {
                string attrName = attrGroups["name"].Value;
                if (!attrName.Equals("src", StringComparison.OrdinalIgnoreCase) &&
                    !attrName.Equals("href", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string rawVal = attrGroups["val"].Value.Trim();
                string assetFileName = ExtractLocalAssetFileName(rawVal);
                if (string.IsNullOrEmpty(assetFileName))
                {
                    continue;
                }

                if (AxeChunkFileNameRegex().IsMatch(assetFileName))
                {
                    (int line, int column) = LineAndColumn(content, tagMatch.Index);
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        line,
                        column,
                        "bundle_contamination",
                        $"HTML <{tagName}> references test-only axe-core chunk '{assetFileName}'"));
                }

                if (isDemoBundle && KeycloakChunkFileNameRegex().IsMatch(assetFileName))
                {
                    (int line, int column) = LineAndColumn(content, tagMatch.Index);
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        line,
                        column,
                        "bundle_contamination",
                        $"Demo HTML <{tagName}> references OIDC keycloak-js chunk '{assetFileName}'"));
                }

                if (isDemoBundle && isModulePreload && ForbiddenDemoPreloadFileNameRegex().IsMatch(assetFileName))
                {
                    (int line, int column) = LineAndColumn(content, tagMatch.Index);
                    findings.Add(new PublicBundleFinding(
                        displayPath,
                        line,
                        column,
                        "bundle_preload",
                        $"Demo HTML <link rel=\"modulepreload\"> must not eagerly preload '{assetFileName}'"));
                }
            }
        }
    }

    private static void ValidateExecutableChunkContamination(
        string displayPath,
        string relativeInsideBundle,
        string content,
        bool isDocsBundle,
        bool isDemoBundle,
        List<PublicBundleFinding> findings)
    {
        if (isDocsBundle && !isDemoBundle)
        {
            return;
        }

        string fileName = Path.GetFileName(relativeInsideBundle);
        if (AxeChunkFileNameRegex().IsMatch(fileName))
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                1,
                1,
                "bundle_contamination",
                $"Test-only axe-core chunk '{fileName}' emitted in runtime bundle"));
        }
        else if (TryFindContaminationMarker(
                     content,
                     ["\"axe-core\"", "'axe-core'", "axe._tree", "axe._audit", "axe.commons", "axe._load"],
                     out int axeOffset))
        {
            (int line, int column) = LineAndColumn(content, axeOffset);
            findings.Add(new PublicBundleFinding(
                displayPath,
                line,
                column,
                "bundle_contamination",
                "Bundled axe-core runtime payload detected in application bundle"));
        }

        if (!isDemoBundle)
        {
            return;
        }

        if (KeycloakChunkFileNameRegex().IsMatch(fileName))
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                1,
                1,
                "bundle_contamination",
                $"Non-demo keycloak-js chunk '{fileName}' emitted in staged/demo bundle"));
        }
        else if (TryFindContaminationMarker(
                     content,
                     ["\"keycloak-js\"", "'keycloak-js'", "KeycloakAdapter", "kc_idp_hint", "3p-cookies/step1.html"],
                     out int kcOffset))
        {
            (int line, int column) = LineAndColumn(content, kcOffset);
            findings.Add(new PublicBundleFinding(
                displayPath,
                line,
                column,
                "bundle_contamination",
                "Bundled keycloak-js OIDC client payload detected in staged/demo bundle"));
        }
    }

    private static bool TryFindContaminationMarker(string content, ReadOnlySpan<string> markers, out int offset)
    {
        foreach (string marker in markers)
        {
            int idx = content.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                offset = idx;
                return true;
            }
        }

        offset = -1;
        return false;
    }

    private static void ValidateStagedDemoColdBootBudget(
        string repoRoot,
        string demoDir,
        List<PublicBundleFinding> findings)
    {
        string indexPath = Path.Combine(demoDir, "index.html");
        if (!File.Exists(indexPath))
        {
            return;
        }

        string displayPath = Path.GetRelativePath(repoRoot, indexPath).Replace('\\', '/');
        string html;
        try
        {
            html = File.ReadAllText(indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            findings.Add(new PublicBundleFinding(displayPath, 1, 1, "bundle_budget", $"Unable to read staged demo index.html: {ex.Message}"));
            return;
        }

        string liveMarkup = MaskCodeBlocksPreservingOffsets(html);
        var coldBootFiles = new HashSet<string>(StringComparer.Ordinal) { Path.GetFullPath(indexPath) };

        foreach (GroupCollection tagGroups in HtmlResourceTagRegex().Matches(liveMarkup).Select(m => m.Groups))
        {
            string tagName = tagGroups["tag"].Value.ToLowerInvariant();
            string attrs = tagGroups["attrs"].Value;
            if (tagName == "link")
            {
                Match relMatch = LinkRelAttrRegex().Match(attrs);
                if (!relMatch.Success)
                {
                    continue;
                }

                string rel = relMatch.Groups["rel"].Value.ToLowerInvariant();
                if (!rel.Contains("modulepreload", StringComparison.Ordinal) &&
                    !rel.Contains("preload", StringComparison.Ordinal) &&
                    !rel.Contains("stylesheet", StringComparison.Ordinal))
                {
                    continue;
                }
            }
            else if (tagName != "script")
            {
                continue;
            }

            foreach (GroupCollection attrGroups in ResourceAttributeRegex().Matches(attrs).Select(m => m.Groups))
            {
                string attrName = attrGroups["name"].Value;
                if (!attrName.Equals("src", StringComparison.OrdinalIgnoreCase) &&
                    !attrName.Equals("href", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? resolved = TryResolveBundleAssetPath(demoDir, attrGroups["val"].Value.Trim());
                if (resolved is not null && File.Exists(resolved))
                {
                    coldBootFiles.Add(resolved);
                }
            }
        }

        long rawTotal = 0;
        long gzipTotal = 0;
        foreach (string filePath in coldBootFiles)
        {
            byte[] rawBytes = File.ReadAllBytes(filePath);
            rawTotal += rawBytes.Length;
            gzipTotal += ComputeGzipByteLength(rawBytes);
        }

        if (rawTotal >= MaxDemoColdBootRawBytes)
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                1,
                1,
                "bundle_budget",
                $"Staged /demo/ cold-boot transfer ({rawTotal:N0} bytes) exceeds the 1.35 MiB ({MaxDemoColdBootRawBytes:N0} bytes) budget"));
        }

        if (gzipTotal >= MaxDemoColdBootGzipBytes)
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                1,
                1,
                "bundle_budget",
                $"Staged /demo/ gzipped cold-boot transfer ({gzipTotal:N0} bytes) exceeds the 450 KiB ({MaxDemoColdBootGzipBytes:N0} bytes) budget"));
        }
    }

    private static void ValidateStagedSubjectPortalAssetBudget(
        string repoRoot,
        string portalDir,
        List<PublicBundleFinding> findings)
    {
        string assetsDir = Path.Combine(portalDir, "assets");
        if (!Directory.Exists(assetsDir))
        {
            return;
        }

        string indexPath = Path.Combine(portalDir, "index.html");
        string displayPath = Path.GetRelativePath(repoRoot, File.Exists(indexPath) ? indexPath : assetsDir).Replace('\\', '/');

        long totalRawBytes = 0;
        foreach (string file in Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories))
        {
            totalRawBytes += new FileInfo(file).Length;
        }

        if (totalRawBytes >= MaxSubjectPortalAssetsRawBytes)
        {
            findings.Add(new PublicBundleFinding(
                displayPath,
                1,
                1,
                "bundle_budget",
                $"Staged /subject-portal/assets/ total volume ({totalRawBytes:N0} bytes) exceeds the 920 KiB ({MaxSubjectPortalAssetsRawBytes:N0} bytes) budget"));
        }
    }

    private static string ExtractLocalAssetFileName(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            IsExternalUrl(rawUrl) ||
            rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string withoutQuery = StripQueryAndHash(rawUrl);
        return Path.GetFileName(withoutQuery);
    }

    private static string? TryResolveBundleAssetPath(string bundleDir, string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            IsExternalUrl(rawUrl) ||
            rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            rawUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string clean = StripQueryAndHash(rawUrl).Replace('\\', '/');
        string[] knownPrefixes = ["/cadence-clinical/demo/", "/demo/", "./", "/"];
        string? matchedPrefix = knownPrefixes.FirstOrDefault(p => clean.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (matchedPrefix is not null)
        {
            clean = clean[matchedPrefix.Length..];
        }

        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        string fullBundleRoot = Path.GetFullPath(bundleDir);
        string candidate = Path.GetFullPath(Path.Combine(fullBundleRoot, clean));
        return candidate.StartsWith(fullBundleRoot, StringComparison.Ordinal) ? candidate : null;
    }

    private static string StripQueryAndHash(string rawUrl)
    {
        int qIdx = rawUrl.IndexOf('?', StringComparison.Ordinal);
        int hIdx = rawUrl.IndexOf('#', StringComparison.Ordinal);
        int cut = qIdx >= 0 && hIdx >= 0 ? Math.Min(qIdx, hIdx) : Math.Max(qIdx, hIdx);
        return cut >= 0 ? rawUrl[..cut] : rawUrl;
    }

    private static long ComputeGzipByteLength(byte[] rawBytes)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(rawBytes, 0, rawBytes.Length);
        }

        return ms.Length;
    }
}
