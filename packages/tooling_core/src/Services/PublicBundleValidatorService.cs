using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Deid;
using Cadence.ToolingCore.Primitives;
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
    /// <summary>Maximum allowed uncompressed cold-boot payload for staged <c>/demo/</c> (<c>1.35 MiB</c>).</summary>
    public const long MaxDemoColdBootRawBytes = 1_415_577;

    /// <summary>Maximum allowed gzip-compressed cold-boot payload for staged <c>/demo/</c> (<c>450 KiB</c>).</summary>
    public const long MaxDemoColdBootGzipBytes = 460_800;

    /// <summary>Maximum allowed uncompressed asset volume in staged <c>/subject-portal/assets/</c> (<c>920 KiB</c>).</summary>
    public const long MaxSubjectPortalAssetsRawBytes = 942_080;

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

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b|\b[A-CEGHJ-PR-TW-Z][A-CEGHJ-NPR-TW-Z]\s*\d{2}\s*\d{2}\s*\d{2}\s*[A-D]\b", RegexOptions.IgnoreCase)]
    private static partial Regex BundleSsnOrNinRegex();

    [GeneratedRegex(@"(?<![a-zA-Z0-9._%+-])[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b")]
    private static partial Regex BundleEmailRegex();

    /// <summary>Default build output directories inspected by <c>check public-bundle</c>, relative to the repository root.</summary>
    public static IReadOnlyList<string> DefaultBundleDirectories => DefaultBundles;

    /// <summary>
    /// Returns <c>true</c> when at least one default <c>dist/</c> bundle directory exists under <paramref name="repoRoot"/>.
    /// Used by unscoped <c>cadence check</c> so <c>public-bundle</c> is skipped only when none of the three build output
    /// directories exist, while partial <c>dist/</c> output still runs and fails closed.
    /// </summary>
    public static bool AnyDefaultBundleExists(string repoRoot) =>
        DefaultBundles.Any(bundle => Directory.Exists(Path.Combine(repoRoot, bundle)));

    /// <summary>Resolves the repository root directory from the current working directory or binary path.</summary>
    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();

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
            bool isStandaloneDemoBundle = !isDocsBundle && IsStandaloneDemoBundleDirectory(fullDir);

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
                bool isStagedDemoSubpath = isDocsBundle && IsStagedSubpathBundle(relativeInsideBundle);
                bool fileIsDocsPortal = isDocsBundle && !isStagedDemoSubpath;
                bool fileIsDemoBundle = isStagedDemoSubpath || isStandaloneDemoBundle;
                allFindings.AddRange(ScanBundleFile(relativeToRepo, relativeInsideBundle, content, fileIsDocsPortal, fileIsDemoBundle));
            }

            if (isDocsBundle)
            {
                string stagedDemoDir = Path.Combine(fullDir, "demo");
                if (Directory.Exists(stagedDemoDir))
                {
                    ValidateStagedDemoColdBootBudget(repoRoot, stagedDemoDir, allFindings);
                }

                string stagedPortalDir = Path.Combine(fullDir, "subject-portal");
                if (Directory.Exists(stagedPortalDir))
                {
                    ValidateStagedSubjectPortalAssetBudget(repoRoot, stagedPortalDir, allFindings);
                }
            }
            else if (fullDir.Replace('\\', '/').EndsWith("/docs/.vitepress/dist/demo", StringComparison.OrdinalIgnoreCase))
            {
                ValidateStagedDemoColdBootBudget(repoRoot, fullDir, allFindings);
            }
            else if (fullDir.Replace('\\', '/').EndsWith("/docs/.vitepress/dist/subject-portal", StringComparison.OrdinalIgnoreCase))
            {
                ValidateStagedSubjectPortalAssetBudget(repoRoot, fullDir, allFindings);
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
        bool isDocsBundle,
        bool isDemoBundle = false)
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
        bool isExecutableJs = extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                              extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                              extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase);
        bool isScriptLike = isExecutableJs ||
                            extension.Equals(".map", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".webmanifest", StringComparison.OrdinalIgnoreCase);
        bool isDocsSerializedProseChunk = isDocsBundle && IsDocsSerializedProseChunk(relativeInsideBundle);
        bool effectiveDemoBundle = isDemoBundle || IsStagedSubpathBundle(relativeInsideBundle);
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

        // 3 & 4. External assets and executable local backend URLs.
        ScanExternalAssets(displayPath, content, liveMarkup, isHtml, isSvg, isCss, isScriptLike, isDocsSerializedProseChunk, findings);
        ScanExecutableLocalUrls(displayPath, content, liveMarkup, isDocsBundle, isHtml, isSvg, isCss, isScriptLike, isDocsSerializedProseChunk, findings);

        // 5. Content-Security-Policy and inline handler enforcement on HTML and SVG markup.
        if (isHtml)
        {
            ValidateHtmlCsp(displayPath, content, liveMarkup!, findings);
            ValidateHtmlBundlePreloadsAndScripts(displayPath, content, liveMarkup!, isDocsBundle, effectiveDemoBundle, findings);
            ScanInlineEventAttributes(displayPath, content, liveMarkup!, findings);
        }
        else if (isSvg)
        {
            ScanInlineEventAttributes(displayPath, content, MaskCodeBlocksPreservingOffsets(content), findings);
        }

        // 6. Test-only (axe-core) and demo-forbidden (keycloak-js) chunk/payload contamination.
        if (isExecutableJs && !isDocsSerializedProseChunk)
        {
            ValidateExecutableChunkContamination(displayPath, relativeInsideBundle, content, isDocsBundle, effectiveDemoBundle, findings);
        }

        // 7. Disallowed Unicode emoji glyphs in web/portal/demo bundles and non-prose docs theme assets (ADR-2679).
        bool isJsonOrManifest = extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                                extension.Equals(".webmanifest", StringComparison.OrdinalIgnoreCase);
        bool scanForEmojiGlyphs = !isDocsBundle
            ? isHtml || isSvg || isCss || isExecutableJs || isJsonOrManifest
            : !isDocsSerializedProseChunk && (isCss || isSvg);
        if (scanForEmojiGlyphs)
        {
            foreach (EmojiHit hit in EmojiValidatorService.FindEmojis(content.AsSpan(), maxViolations: 5))
            {
                findings.Add(new PublicBundleFinding(
                    displayPath,
                    hit.Line,
                    hit.Column,
                    "emoji_glyph",
                    $"Disallowed Unicode emoji glyph U+{hit.CodePoint:X4} in public bundle (ADR-2679)"));
            }
        }

        return findings;
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
        string lower = matchValue.ToLowerInvariant();
        return (lower.EndsWith(".js", StringComparison.Ordinal) ||
                lower.EndsWith(".mjs", StringComparison.Ordinal) ||
                lower.EndsWith(".css", StringComparison.Ordinal) ||
                lower.EndsWith(".map", StringComparison.Ordinal)) &&
               matchStart > 0 && content[matchStart - 1] == '/';
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

    private static bool IsStandaloneDemoBundleDirectory(string fullDir)
    {
        string normalized = fullDir.Replace('\\', '/').TrimEnd('/');
        return File.Exists(Path.Combine(fullDir, "mockServiceWorker.js")) ||
               normalized.EndsWith("/demo", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/docs/.vitepress/dist/subject-portal", StringComparison.OrdinalIgnoreCase);
    }
}
