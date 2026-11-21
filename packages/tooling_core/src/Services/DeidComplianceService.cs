using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Deid;

namespace Cadence.ToolingCore.Services;

/// <summary>A piece of possible PHI or PII found in a repository file. The matched value is never kept or printed.</summary>
public sealed record DeidFinding(string File, int Line, int Column, string Category);

/// <summary>
/// The <c>check deid</c> gate: scans repository files for PHI and PII with the full <see cref="DeidDetector"/>, the
/// port of the Python reference repository scanner (<c>packages/deid/cli.py</c>). A line carrying
/// <c>deid-ignore</c>, <c>deid: ignore</c> or <c>pragma: allowlist</c> is skipped, and known development values
/// (local addresses, toolchain URLs, placeholder dates and numbers) are allowed per category.
/// </summary>
public static partial class DeidComplianceService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // The Python reference's list, plus the source types of the .NET and Vue code base.
        ".py", ".js", ".ts", ".tsx", ".jsx", ".json", ".md", ".log", ".txt", ".yaml", ".yml", ".csv", ".toml", ".xml",
        ".html", ".css", ".cs", ".vue", ".sql",
    };

    private static readonly HashSet<string> AlwaysScannedNames = new(StringComparer.Ordinal)
    {
        "Dockerfile", "LICENSE", "Makefile", "Pipfile", "pyproject.toml", "package.json", "pnpm-workspace.yaml",
        "pnpm-lock.yaml", "uv.lock",
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.Ordinal)
    {
        "tests", "test", "testing", "node_modules", ".git", ".venv", ".cadence", "env", "build", "dist", ".github", ".ruff_cache",
        ".pytest_cache", "docs", "scripts", "docker", "verification", "stores", "alembic", "deid", "coverage",
        ".agents", ".jules", "bin", "obj",
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.Ordinal)
    {
        "package.json", "pyproject.toml", "pnpm-workspace.yaml", "pnpm-lock.yaml", "uv.lock", ".secrets.baseline",
        ".deid-baseline.txt", "readme.md", "agents.md", "contributing.md", "license", "architecture.md", "eslint.config.mjs",
    };

    private static readonly string[] AllowedUrlFragments =
    [
        "localhost", "127.0.0.1", "0.0.0.0", "transmit-mock", "cadence-clinical.com", "ccrsoft.com", "brand_domain",
        "github.com", "githubusercontent.com", "pypi.org", "npmjs.com", "cdisc.org", "cadence.clinical", "nih.gov",
        "json-schema.org", "snomed.info", "loinc.org", "hl7.org", "w3.org", "ama-assn.org", "example.com", "example.org",
    ];

    private static readonly HashSet<string> AllowedZipValues = new(StringComparer.Ordinal)
    {
        "12345", "65537", "65536", "65535", "86400", "30000", "27001", "29463", "10000", "00000", "32601", "32700",
        "00101", "93000", "29046",
    };

    private static readonly string[] ZipContextWords =
    [
        "secret", "salt", "key", "public_exponent", "exponent", "interval", "timeout", "ttl", "port", "iso", "bytes",
        "size", "pragma", "chunk", "px", "background", "border", "loinc", "unicode", "regex", "pattern", "concept",
        "xref", "jsonrpc", "code", "parts_count", "xml_name", "description",
    ];

    private static readonly HashSet<string> AllowedDateValues = new(StringComparer.Ordinal)
    {
        "2024-09-27", "1960-01-01", "2026-07-30", "02-aug-2026", "2026-08-04", "1960, 1, 1", "9999-99-99",
    };

    private static readonly string[] DateContextWords =
        ["version", "package", "href", "release", "sas", "epoch", "format", "test", "example", "default"];

    /// <summary>The ratchet baseline of accepted findings, one per line, relative to the repository root.</summary>
    public const string BaselineFile = ".deid-baseline.txt";

    /// <summary>
    /// Scans <paramref name="files"/>, or every file git tracks when none are given, and returns 1 when a finding is
    /// not in <see cref="BaselineFile"/> or a baseline entry for a scanned file no longer matches. Findings name the
    /// file, line, column and category, never the matched value. With <c>CADENCE_UPDATE_BASELINE=1</c> and no files,
    /// the baseline is rewritten from the current findings instead.
    /// </summary>
    public static int ScanDeidCompliance(string[]? files = null, string? repoRoot = null, ComplianceProfile profile = ComplianceProfile.HIPAA)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Executing DEID compliance scan...");

        var detector = new DeidDetector();
        var scanned = new List<string>();
        var findings = new List<(DeidFinding Finding, string Key)>();
        foreach (string relativePath in CandidateFiles(files, repoRoot))
        {
            string content;
            try
            {
                content = File.ReadAllText(Path.Combine(repoRoot, relativePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[WARNING] Error reading '{relativePath}': {ex.Message}");
                continue;
            }

            scanned.Add(relativePath);
            string[] lines = content.Split('\n');
            foreach (var finding in ScanText(relativePath, content, detector, profile))
            {
                findings.Add((finding, BaselineKey(finding, lines[finding.Line - 1])));
            }
        }

        string baselinePath = Path.Combine(repoRoot, BaselineFile);
        bool fullScan = files is not { Length: > 0 };
        if (fullScan && Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE") == "1")
        {
            WriteBaseline(baselinePath, findings.Select(f => f.Key));
            Console.WriteLine($"✔ Wrote {findings.Count} entries to {BaselineFile}.");
            return 0;
        }

        var scannedSet = scanned.ToHashSet(StringComparer.Ordinal);
        var baseline = ReadBaseline(baselinePath).Where(k =>
        {
            int pipeIdx = k.IndexOf('|', StringComparison.Ordinal);
            return pipeIdx > 0 && scannedSet.Contains(k[..pipeIdx]);
        }).ToList();
        var (newFindings, stale) = CompareWithBaseline(findings, baseline);

        foreach (var finding in newFindings)
        {
            Console.WriteLine($"{finding.File}:{finding.Line}:{finding.Column}: [DEID VIOLATION] Category: '{finding.Category}'");
        }

        foreach (string entry in stale)
        {
            Console.WriteLine($"[DEID STALE BASELINE] {entry}");
        }

        if (newFindings.Count > 0 || stale.Count > 0)
        {
            if (newFindings.Count > 0)
            {
                Console.WriteLine($"\n[ERROR] Found {newFindings.Count} DEID compliance violations. Remove the data, or mark a line that is not PHI with `deid-ignore`.");
            }

            if (stale.Count > 0)
            {
                Console.WriteLine($"\n[ERROR] {stale.Count} {BaselineFile} entries no longer match; the baseline may only shrink. Run `CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid` to drop them.");
            }

            return 1;
        }

        Console.WriteLine($"✔ DEID compliance scan passed cleanly ({scanned.Count} files; {baseline.Count} baselined findings).");
        return 0;
    }

    /// <summary>
    /// Splits findings into those not covered by the baseline and baseline entries no finding uses. Entries are
    /// counted, so a line text that appears twice needs two entries.
    /// </summary>
    public static (IReadOnlyList<DeidFinding> NewFindings, IReadOnlyList<string> StaleEntries) CompareWithBaseline(
        IReadOnlyList<(DeidFinding Finding, string Key)> findings,
        IReadOnlyList<string> baseline)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(baseline);
        var remaining = baseline.GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var newFindings = new List<DeidFinding>();
        foreach (var (finding, key) in findings)
        {
            if (remaining.TryGetValue(key, out int count) && count > 0)
            {
                remaining[key] = count - 1;
            }
            else
            {
                newFindings.Add(finding);
            }
        }

        var stale = remaining.Where(kv => kv.Value > 0).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).Order(StringComparer.Ordinal).ToList();
        return (newFindings, stale);
    }

    /// <summary>
    /// A finding's baseline entry: <c>path|category|hash</c>, where the hash is the first 16 hex digits of the SHA-256
    /// of the trimmed line. The entry survives lines moving, carries no PHI, and changes when the line changes.
    /// </summary>
    public static string BaselineKey(DeidFinding finding, string lineContent)
    {
        ArgumentNullException.ThrowIfNull(finding);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(lineContent.Trim()));
        return $"{finding.File}|{finding.Category}|{Convert.ToHexStringLower(hash)[..16]}";
    }

    private static List<string> ReadBaseline(string path) =>
        File.Exists(path)
            ? [.. File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))]
            : [];

    private static void WriteBaseline(string path, IEnumerable<string> keys)
    {
        var lines = new List<string>
        {
            "# DEID scan ratchet baseline (pnpm cadence check deid). Findings that predate the full detector and are not PHI.",
            "# One per line: path|category|first 16 hex digits of SHA-256(trimmed line). Sorted ordinally. The list may only shrink.",
            "# Regenerate with CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid (docs/cli.md).",
        };
        lines.AddRange(keys.Order(StringComparer.Ordinal));
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
    }

    /// <summary>
    /// The findings in one file's text after the pragma and development-value allowlists, as the Python reference's
    /// <c>scan_file</c> filters them.
    /// </summary>
    public static IReadOnlyList<DeidFinding> ScanText(string relativePath, string content, DeidDetector detector, ComplianceProfile profile = ComplianceProfile.HIPAA)
    {
        ArgumentNullException.ThrowIfNull(detector);
        string[] lines = content.Split('\n');
        var findings = new List<DeidFinding>();
        foreach (var result in detector.Detect(content, profile))
        {
            (int line, int column) = LineAndColumn(content, result.Start);
            string lineContent = line <= lines.Length ? lines[line - 1] : string.Empty;
            if (lineContent.Contains("deid-ignore", StringComparison.Ordinal) ||
                lineContent.Contains("pragma: allowlist", StringComparison.Ordinal) ||
                lineContent.Contains("deid: ignore", StringComparison.Ordinal) ||
                IsAllowedDevelopmentValue(result.Category, result.Value, lineContent, relativePath))
            {
                continue;
            }

            findings.Add(new DeidFinding(relativePath, line, column, result.Category));
        }

        return findings;
    }

    /// <summary>Whether a repository-relative path is outside the scan: tests, docs, tooling and generated folders.</summary>
    public static bool IsExcludedPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        string[] parts = normalized.ToLowerInvariant().Split('/');
        if (parts.Any(ExcludedDirectories.Contains))
        {
            return true;
        }

        string name = parts[^1];
        return name.StartsWith("test_", StringComparison.Ordinal)
            || name.EndsWith(".test.js", StringComparison.Ordinal)
            || name.EndsWith(".test.ts", StringComparison.Ordinal)
            || name.EndsWith(".test.jsx", StringComparison.Ordinal)
            || name.EndsWith(".test.tsx", StringComparison.Ordinal)
            || name.EndsWith(".css", StringComparison.Ordinal)
            || ExcludedFileNames.Contains(name);
    }

    /// <summary>Whether a file is text worth scanning: a known text extension or name, or no extension and no NUL byte.</summary>
    public static bool ShouldScanFile(string path)
    {
        if (AlwaysScannedNames.Contains(Path.GetFileName(path)))
        {
            return true;
        }

        string extension = Path.GetExtension(path);
        if (extension.Length > 0)
        {
            return TextExtensions.Contains(extension);
        }

        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[1024];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Array.IndexOf(buffer, (byte)0, 0, read) < 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsAllowedDevelopmentValue(string category, string value, string lineContent, string relativePath)
    {
        string val = value.ToLowerInvariant();
        string line = lineContent.ToLowerInvariant();
        string path = relativePath.ToLowerInvariant();
        return category switch
        {
            DetectorCategory.Urls =>
                AllowedUrlFragments.Any(f => val.Contains(f, StringComparison.Ordinal)) ||
                line.Contains("brand_domain", StringComparison.Ordinal),
            DetectorCategory.Email =>
                val.Contains("cadence", StringComparison.Ordinal) || val.Contains("clinical", StringComparison.Ordinal) ||
                !val.Contains('@', StringComparison.Ordinal),
            DetectorCategory.IpMacAddresses => val is "127.0.0.1" or "0.0.0.0" or "::1",
            DetectorCategory.ZipGeographic =>
                AllowedZipValues.Contains(val) ||
                ZipContextWords.Any(w => line.Contains(w, StringComparison.Ordinal)) ||
                path.Contains("xml_mapping", StringComparison.Ordinal) ||
                path.Contains("concept_maps", StringComparison.Ordinal) ||
                path.Contains("matcher", StringComparison.Ordinal),
            DetectorCategory.TelephoneFax => IsAllowedTelephoneValue(val, lineContent, path),
            DetectorCategory.Dates =>
                AllowedDateValues.Contains(val) || DateContextWords.Any(w => line.Contains(w, StringComparison.Ordinal)),
            _ => false,
        };
    }

    private static bool IsAllowedTelephoneValue(string val, string lineContent, string path)
    {
        string line = lineContent.ToLowerInvariant();
        string digits = val.Replace("+", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return lineContent.Contains("concept", StringComparison.Ordinal)
            || path.Contains("concept_maps", StringComparison.Ordinal)
            || path.Contains("matcher", StringComparison.Ordinal)
            || line.Contains("bandwidth", StringComparison.Ordinal)
            || line.Contains("bitrate", StringComparison.Ordinal)
            || lineContent.Contains("usr_", StringComparison.Ordinal)
            || val is "+1234567890" or "+0987654321"
            || digits.Length == 0 || !digits.All(char.IsAsciiDigit)
            || val.Contains('.', StringComparison.Ordinal)
            || val.Contains("0000000000", StringComparison.Ordinal)
            || DecimalNumberRegex().Matches(lineContent).Any(m => m.Value.Replace(".", string.Empty, StringComparison.Ordinal).Contains(val, StringComparison.Ordinal))
            || (path.EndsWith("demo-seed.json", StringComparison.Ordinal) &&
                UuidRegex().Matches(lineContent).Any(m => m.Value.Contains(val, StringComparison.OrdinalIgnoreCase)));
    }

    private static (int Line, int Column) LineAndColumn(string content, int offset)
    {
        int line = 1;
        int lineStart = 0;
        for (int i = 0; i < offset; i++)
        {
            if (content[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, offset - lineStart + 1);
    }

    // Explicit files (the changed files CI and the pre-commit hook pass), or every file git tracks, so .gitignore is
    // honoured by git itself rather than by the Python reference's pattern emulation.
    private static IEnumerable<string> CandidateFiles(string[]? files, string repoRoot)
    {
        IEnumerable<string> relative = files is { Length: > 0 }
            ? files.Select(f => Path.GetRelativePath(repoRoot, Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)))
            : TrackedFiles(repoRoot);

        return relative
            .Select(p => p.Replace('\\', '/'))
            .Where(p => !p.StartsWith("../", StringComparison.Ordinal) && !IsExcludedPath(p))
            .Where(p => File.Exists(Path.Combine(repoRoot, p)) && ShouldScanFile(Path.Combine(repoRoot, p)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
    }

    private static IEnumerable<string> TrackedFiles(string repoRoot)
    {
        var startInfo = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git ls-files could not be started; the DEID scan needs git to list files.");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files failed in {repoRoot}: {process.StandardError.ReadToEnd()}");
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
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

    [GeneratedRegex(@"\d+\.\d+")]
    private static partial Regex DecimalNumberRegex();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex UuidRegex();
}
