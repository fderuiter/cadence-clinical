using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cadence.ToolingCore.Services;

/// <summary>A single disallowed Unicode emoji glyph discovered in a scanned text buffer.</summary>
public readonly record struct EmojiHit(int Line, int Column, int CodePoint, string Glyph);

/// <summary>
/// Scans frontend UI, locale, fixture, VitePress theme, and built public bundle assets for disallowed
/// Unicode emoji glyphs (<c>Extended_Pictographic</c>, <c>Regional_Indicator</c>, and keycap sequences).
/// Native AOT clean (zero reflection, zero backtracking regular expressions).
/// </summary>
public static class EmojiValidatorService
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly string[] ScopedRoots =
    [
        "packages/ui",
        "apps/web",
        "apps/subject-portal",
        "packages/demo-fixtures",
        "docs/.vitepress/theme",
    ];

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vue",
        ".ts",
        ".tsx",
        ".mts",
        ".cts",
        ".js",
        ".jsx",
        ".mjs",
        ".cjs",
        ".css",
        ".html",
        ".htm",
        ".svg",
        ".json",
    };

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        "dist",
        "coverage",
        "playwright-report",
        "test-results",
        ".git",
        ".cadence",
        ".venv",
        "bin",
        "obj",
        "TestResults",
    };

    private static readonly (int Start, int End)[] ExtendedPictographicRanges =
    [
        (0xA9, 0xA9),
        (0xAE, 0xAE),
        (0x203C, 0x203C),
        (0x2049, 0x2049),
        (0x2122, 0x2122),
        (0x2139, 0x2139),
        (0x2194, 0x2199),
        (0x21A9, 0x21AA),
        (0x231A, 0x231B),
        (0x2328, 0x2328),
        (0x23CF, 0x23CF),
        (0x23E9, 0x23F3),
        (0x23F8, 0x23FA),
        (0x24C2, 0x24C2),
        (0x25AA, 0x25AB),
        (0x25B6, 0x25B6),
        (0x25C0, 0x25C0),
        (0x25FB, 0x25FE),
        (0x2600, 0x2604),
        (0x260E, 0x260E),
        (0x2611, 0x2611),
        (0x2614, 0x2615),
        (0x2618, 0x2618),
        (0x261D, 0x261D),
        (0x2620, 0x2620),
        (0x2622, 0x2623),
        (0x2626, 0x2626),
        (0x262A, 0x262A),
        (0x262E, 0x262F),
        (0x2638, 0x263A),
        (0x2640, 0x2640),
        (0x2642, 0x2642),
        (0x2648, 0x2653),
        (0x265F, 0x2660),
        (0x2663, 0x2663),
        (0x2665, 0x2666),
        (0x2668, 0x2668),
        (0x267B, 0x267B),
        (0x267E, 0x267F),
        (0x2692, 0x2697),
        (0x2699, 0x2699),
        (0x269B, 0x269C),
        (0x26A0, 0x26A1),
        (0x26A7, 0x26A7),
        (0x26AA, 0x26AB),
        (0x26B0, 0x26B1),
        (0x26BD, 0x26BE),
        (0x26C4, 0x26C5),
        (0x26C8, 0x26C8),
        (0x26CE, 0x26CF),
        (0x26D1, 0x26D1),
        (0x26D3, 0x26D4),
        (0x26E9, 0x26EA),
        (0x26F0, 0x26F5),
        (0x26F7, 0x26FA),
        (0x26FD, 0x26FD),
        (0x2702, 0x2702),
        (0x2705, 0x2705),
        (0x2708, 0x270D),
        (0x270F, 0x270F),
        (0x2712, 0x2712),
        (0x2714, 0x2714),
        (0x2716, 0x2716),
        (0x271D, 0x271D),
        (0x2721, 0x2721),
        (0x2728, 0x2728),
        (0x2733, 0x2734),
        (0x2744, 0x2744),
        (0x2747, 0x2747),
        (0x274C, 0x274C),
        (0x274E, 0x274E),
        (0x2753, 0x2755),
        (0x2757, 0x2757),
        (0x2763, 0x2764),
        (0x2795, 0x2797),
        (0x27A1, 0x27A1),
        (0x27B0, 0x27B0),
        (0x27BF, 0x27BF),
        (0x2934, 0x2935),
        (0x2B05, 0x2B07),
        (0x2B1B, 0x2B1C),
        (0x2B50, 0x2B50),
        (0x2B55, 0x2B55),
        (0x3030, 0x3030),
        (0x303D, 0x303D),
        (0x3297, 0x3297),
        (0x3299, 0x3299),
        (0x1F004, 0x1F004),
        (0x1F02C, 0x1F02F),
        (0x1F094, 0x1F09F),
        (0x1F0AF, 0x1F0B0),
        (0x1F0C0, 0x1F0C0),
        (0x1F0CF, 0x1F0D0),
        (0x1F0F6, 0x1F0FF),
        (0x1F170, 0x1F171),
        (0x1F17E, 0x1F17F),
        (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A),
        (0x1F1AE, 0x1F1E5),
        (0x1F201, 0x1F20F),
        (0x1F21A, 0x1F21A),
        (0x1F22F, 0x1F22F),
        (0x1F232, 0x1F23A),
        (0x1F23C, 0x1F23F),
        (0x1F249, 0x1F25F),
        (0x1F266, 0x1F321),
        (0x1F324, 0x1F393),
        (0x1F396, 0x1F397),
        (0x1F399, 0x1F39B),
        (0x1F39E, 0x1F3F0),
        (0x1F3F3, 0x1F3F5),
        (0x1F3F7, 0x1F3FA),
        (0x1F400, 0x1F4FD),
        (0x1F4FF, 0x1F53D),
        (0x1F549, 0x1F54E),
        (0x1F550, 0x1F567),
        (0x1F56F, 0x1F570),
        (0x1F573, 0x1F57A),
        (0x1F587, 0x1F587),
        (0x1F58A, 0x1F58D),
        (0x1F590, 0x1F590),
        (0x1F595, 0x1F596),
        (0x1F5A4, 0x1F5A5),
        (0x1F5A8, 0x1F5A8),
        (0x1F5B1, 0x1F5B2),
        (0x1F5BC, 0x1F5BC),
        (0x1F5C2, 0x1F5C4),
        (0x1F5D1, 0x1F5D3),
        (0x1F5DC, 0x1F5DE),
        (0x1F5E1, 0x1F5E1),
        (0x1F5E3, 0x1F5E3),
        (0x1F5E8, 0x1F5E8),
        (0x1F5EF, 0x1F5EF),
        (0x1F5F3, 0x1F5F3),
        (0x1F5FA, 0x1F64F),
        (0x1F680, 0x1F6C5),
        (0x1F6CB, 0x1F6D2),
        (0x1F6D5, 0x1F6E5),
        (0x1F6E9, 0x1F6E9),
        (0x1F6EB, 0x1F6F0),
        (0x1F6F3, 0x1F6FF),
        (0x1F7DA, 0x1F7FF),
        (0x1F80C, 0x1F80F),
        (0x1F848, 0x1F84F),
        (0x1F85A, 0x1F85F),
        (0x1F888, 0x1F88F),
        (0x1F8AE, 0x1F8AF),
        (0x1F8BC, 0x1F8BF),
        (0x1F8C2, 0x1F8CF),
        (0x1F8D9, 0x1F8FF),
        (0x1F90C, 0x1F93A),
        (0x1F93C, 0x1F945),
        (0x1F947, 0x1F9FF),
        (0x1FA58, 0x1FA5F),
        (0x1FA6E, 0x1FAFF),
        (0x1FC00, 0x1FFFD),
    ];

    /// <summary>Validates repository or staged frontend files and writes results to standard output.</summary>
    public static bool Validate(
        string? repoRoot = null,
        IReadOnlyList<string>? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        string resolvedRoot = Path.GetFullPath(repoRoot ?? PreCommitOpenApiService.FindRepoRoot());
        var violations = FindViolations(resolvedRoot, targetFiles, preCommit, indexReader);
        if (violations.Count == 0)
        {
            Console.WriteLine("no-emojis: 0 disallowed Unicode emoji glyphs found.");
            return true;
        }

        Console.WriteLine($"no-emojis: found {violations.Count} disallowed Unicode emoji glyph(s):");
        foreach (string violation in violations)
        {
            Console.WriteLine($"  {violation}");
        }

        return false;
    }

    /// <summary>Finds all disallowed emoji glyph violations across scoped workspace files or target files.</summary>
    public static List<string> FindViolations(
        string repoRoot,
        IReadOnlyList<string>? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        var violations = new List<string>();
        if (targetFiles is { Count: 0 })
        {
            return violations;
        }

        string fullRoot = Path.GetFullPath(repoRoot);
        var reader = GitIndexReader.ResolvePreCommitReader(fullRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var effectiveReader = reader is { HasAuthoritativeIndex: true } ? reader : null;

            if (preCommit || targetFiles is not null)
            {
                IEnumerable<string> rawCandidates = targetFiles
                    ?? (IReadOnlyList<string>)PreCommitOpenApiService.GetStagedFiles(fullRoot);

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string rawFile in rawCandidates)
                {
                    if (string.IsNullOrWhiteSpace(rawFile))
                    {
                        continue;
                    }

                    string? rel = GitIndexReader.TryNormalizeRelativePath(fullRoot, rawFile);
                    if (string.IsNullOrEmpty(rel) ||
                        !IsInScopedFrontendPath(rel) ||
                        !HasScannedExtension(rel) ||
                        HasIgnoredSegment(rel) ||
                        !seen.Add(rel))
                    {
                        continue;
                    }

                    string diskPath = Path.Combine(fullRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    string? content = TryReadUtf8Text(diskPath, rel, effectiveReader);
                    if (content is not null)
                    {
                        ScanText(content.AsSpan(), rel, violations);
                    }
                }

                return violations;
            }

            foreach (string scope in ScopedRoots)
            {
                string scopeDir = Path.Combine(fullRoot, scope.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(scopeDir))
                {
                    WalkDirectory(fullRoot, scopeDir, violations);
                }
            }

            return violations;
        }
    }

    /// <summary>Returns structured emoji hits in a character span up to <paramref name="maxViolations"/>.</summary>
    public static List<EmojiHit> FindEmojis(ReadOnlySpan<char> text, int maxViolations = 20)
    {
        var hits = new List<EmojiHit>();
        int line = 1;
        int col = 1;
        int index = 0;

        while (index < text.Length)
        {
            if (Rune.DecodeFromUtf16(text[index..], out Rune rune, out int charsConsumed) != System.Buffers.OperationStatus.Done)
            {
                index++;
                col++;
                continue;
            }

            index += charsConsumed;
            int value = rune.Value;

            if (value == '\n')
            {
                line++;
                col = 1;
                continue;
            }

            if (value == '\r')
            {
                continue;
            }

            bool isUnqualifiedLegalMark = value is 0x00A9 or 0x00AE or 0x2122 &&
                (index >= text.Length || text[index] != '\uFE0F');

            if (!isUnqualifiedLegalMark && IsDisallowedEmojiRune(value))
            {
                hits.Add(new EmojiHit(line, col, value, rune.ToString()));
                if (hits.Count >= maxViolations)
                {
                    return hits;
                }
            }

            col++;
        }

        return hits;
    }

    /// <summary>Scans a character span and appends formatted violation strings.</summary>
    public static void ScanText(
        ReadOnlySpan<char> text,
        string displayPath,
        List<string> violations,
        int maxViolationsPerFile = 20)
    {
        foreach (EmojiHit hit in FindEmojis(text, maxViolationsPerFile))
        {
            violations.Add(
                $"{displayPath}:{hit.Line}:{hit.Column}: disallowed emoji glyph U+{hit.CodePoint:X4} ('{hit.Glyph}'); use ClinicalIcon or 1.75px Lucide SVG icons (ADR-2679).");
        }
    }

    /// <summary>Returns true if the Unicode scalar value is pictographic, regional indicator, or combining keycap.</summary>
    public static bool IsDisallowedEmojiRune(int codePoint)
    {
        if (codePoint == 0x20E3 || codePoint is >= 0x1F1E6 and <= 0x1F1FF)
        {
            return true;
        }

        int lo = 0;
        int hi = ExtendedPictographicRanges.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            var (start, end) = ExtendedPictographicRanges[mid];
            if (codePoint < start)
            {
                hi = mid - 1;
            }
            else if (codePoint > end)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static void WalkDirectory(string fullRoot, string currentDir, List<string> violations)
    {
        foreach (string subDir in Directory.EnumerateDirectories(currentDir))
        {
            if (!IgnoredDirectoryNames.Contains(Path.GetFileName(subDir)))
            {
                WalkDirectory(fullRoot, subDir, violations);
            }
        }

        foreach (string file in Directory.EnumerateFiles(currentDir))
        {
            if (!HasScannedExtension(file))
            {
                continue;
            }

            string rel = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
            string? content = TryReadUtf8Text(file, rel, reader: null);
            if (content is not null)
            {
                ScanText(content.AsSpan(), rel, violations);
            }
        }
    }

    private static string? TryReadUtf8Text(string fullPath, string relPath, GitIndexReader? reader)
    {
        byte[] bytes;
        if (reader is not null)
        {
            if (!reader.TryReadBytes(relPath, out bytes))
            {
                return null;
            }
        }
        else
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }

            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (IOException)
            {
                return null;
            }
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsInScopedFrontendPath(string relativePath)
    {
        foreach (string scope in ScopedRoots)
        {
            if (relativePath.Equals(scope, StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(scope + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasScannedExtension(string pathValue) =>
        ScannedExtensions.Contains(Path.GetExtension(pathValue));

    private static bool HasIgnoredSegment(string relativePath)
    {
        foreach (string segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IgnoredDirectoryNames.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }
}
