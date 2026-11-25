using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Deid;

namespace Cadence.ToolingCore.Services;

/// <summary>A piece of possible PHI or PII found in a repository file. The matched value is never kept or printed.</summary>
public sealed record DeidFinding(string File, int Line, int Column, string Category);

/// <summary>
/// The <c>check deid</c> gate: scans repository files for PHI and PII with the full <see cref="DeidDetector"/>,
/// covering application code, authored portal docs (<c>docs/</c>), root portal documents, Vue templates, and client
/// stores while keeping vendored standards (<c>docs/CDISC/</c>, <c>docs/NCI/</c>) and GxP runs (<c>docs/SDLC/runs/</c>)
/// excluded. A line carrying <c>deid-ignore</c>, <c>deid: ignore</c> or <c>pragma: allowlist</c> is skipped in source,
/// and known development values are allowed per category.
/// </summary>
public static partial class DeidComplianceService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Source, configuration, documentation, and asset text extensions across the .NET, Vue, and VitePress code base.
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".json", ".md", ".log", ".txt", ".yaml", ".yml", ".csv",
        ".toml", ".xml", ".html", ".css", ".cs", ".vue", ".sql", ".svg",
    };

    private static readonly HashSet<string> AlwaysScannedNames = new(StringComparer.Ordinal)
    {
        "Dockerfile", "LICENSE", "Makefile", "Pipfile", "pyproject.toml", "package.json", "pnpm-workspace.yaml",
        "pnpm-lock.yaml", "uv.lock",
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.Ordinal)
    {
        "tests", "test", "testing", "node_modules", ".git", ".venv", "env", "build", "dist", ".github", ".ruff_cache",
        ".pytest_cache", "scripts", "docker", "verification", "alembic", "deid", "coverage", ".agents", ".jules",
        ".cadence", "bin", "obj",
    };

    private static readonly string[] ExcludedPathPrefixes =
    [
        "baselines/",
        "docs/cdisc/",
        "docs/nci/",
        "docs/sdlc/runs/",
        "docs/openapi/",
        "docs/schema/",
        "docs/.vitepress/.temp/",
        "docs/.vitepress/cache/",
        "docs/.vitepress/dist/",
    ];

    private static readonly HashSet<string> ExcludedRelativePaths = new(StringComparer.Ordinal)
    {
        "docs/sdlc/requirements_traceability_matrix.md",
        "docs/sdlc/iq_oq_pq_execution_report.md",
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.Ordinal)
    {
        "package.json", "pyproject.toml", "pnpm-workspace.yaml", "pnpm-lock.yaml", "uv.lock", ".secrets.baseline",
        ".deid-baseline.txt", ".duplication-baseline.txt", "eslint.config.mjs",
    };

    private static readonly string[] ApprovedSyntheticEmailDomains =
    [
        "cadence.clinical",
        "cadence-clinical.com",
        "ccrsoft.com",
        "cadence.example",
    ];

    private static readonly HashSet<string> ApprovedCanonicalDemoEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "crc.site101@example.com",
        "pi.site101@example.com",
        "cra.monitor@example.com",
        "dm.lead@example.com",
        "designer_demo@example.com",
        "designer.lead@example.com",
        "safety.officer@example.com",
        "auditor.lead@example.com",
        "stat.author@example.com",
        "stat.approver@example.com",
        "pharmacist.site101@example.com",
        "subject.site101@example.com",
    };

    private static readonly string[] AllowedUrlFragments =
    [
        "localhost", "127.0.0.1", "0.0.0.0", "transmit-mock", "cadence-clinical.com", "ccrsoft.com", "brand_domain",
        "github.com", "githubusercontent.com", "pypi.org", "npmjs.com", "cdisc.org", "cadence.clinical", "nih.gov",
        "json-schema.org", "snomed.info", "loinc.org", "hl7.org", "w3.org", "ama-assn.org", "example.com", "example.org",
        "cadence.example", "cadence-clinical.internal", "storage.local", "://keycloak:", "://host:port",
        "://execution-", "://cadence.", "://test", "fderuiter.github.io", "purl.org/dc/", "purl.org/rss/", "microsoft.com", "dot.net", "schemastore.org",
        "unitsofmeasure.org", "who.int", "who-umc.org", "whocc.no", "meddra.org", "clinicaltrials.gov", "nist.gov",
        "owasp.org", "ecfr.gov", "fda.gov", "ema.europa.eu", "ich.org", "iso.org", "conventionalcommits.org",
        "postgresql.org", "astral.sh", "python.org", "vuejs.org", "vitepress.dev", "vitejs.dev", "playwright.dev",
        "docker.com", "opentelemetry.io", "keycloak.org", "ietf.org", "rfc-editor.org", "spdx.org", "apache.org",
        "opensource.org", "creativecommons.org", "mozilla.org", "npmjs.org", "nuget.org",
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

    /// <summary>Directory containing per-unit ratchet baseline shards, relative to the repository root.</summary>
    public const string BaselineDirectory = "baselines/deid";

    private const string ShardSuffix = ".baseline.txt";

    private static readonly string[] BaselineShardHeader =
    [
        "# DEID scan ratchet baseline shard (pnpm cadence check deid). Findings that predate the full detector and are not PHI.",
        "# One per line: path|category|first 16 hex digits of SHA-256(trimmed matched value). Sorted ordinally. Shards may only shrink.",
        "# Prune resolved entries with CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid [files...] (docs/cli.md).",
    ];

    public static string GetShardRelativePath(string filePath) =>
        $"{BaselineDirectory}/{BaselineUnitPath.ResolveUnit(filePath)}{ShardSuffix}";

    /// <summary>
    /// Scans <paramref name="files"/>, or the resolved <see cref="GitRevisionScopeService"/> candidate files when none are
    /// given, and returns 1 when a finding is not in <see cref="BaselineDirectory"/> shards or a baseline entry for a
    /// scanned file no longer matches. Findings name the file, line, column and category, never the matched value. With
    /// <c>CADENCE_UPDATE_BASELINE=1</c>, only stale entries in the scanned scope are subtracted from touched shards (deleting
    /// empty shards) and new violations are never added.
    /// </summary>
    public static int ScanDeidCompliance(
        string[]? files = null,
        string? repoRoot = null,
        ComplianceProfile profile = ComplianceProfile.HIPAA,
        bool preCommit = false,
        GitIndexReader? indexReader = null,
        string? baseRef = null,
        string? pushBefore = null,
        string? eventName = null,
        string? gitRef = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Executing DEID compliance scan...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ DEID compliance scan passed cleanly (0 files; 0 baselined findings).");
            return 0;
        }

        bool updateBaseline = !preCommit &&
                              Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE") == "1";
        bool forceFullTrackedScan = updateBaseline &&
                                    files is null &&
                                    string.IsNullOrWhiteSpace(baseRef) &&
                                    string.IsNullOrWhiteSpace(pushBefore);

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var detector = new DeidDetector();
            var scanned = new List<string>();
            var findings = new List<(DeidFinding Finding, string Key)>();
            foreach (string relativePath in CandidateFiles(repoRoot, files, preCommit, baseRef, pushBefore, forceTrackedScan: forceFullTrackedScan, reader: reader, eventName: eventName, gitRef: gitRef))
            {
                string content;
                if (reader is not null)
                {
                    if (!reader.TryReadText(relativePath, out content))
                    {
                        continue;
                    }
                }
                else
                {
                    try
                    {
                        content = File.ReadAllText(Path.Combine(repoRoot, relativePath));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Console.WriteLine($"[WARNING] Error reading '{relativePath}': {ex.Message}");
                        continue;
                    }
                }

                scanned.Add(relativePath);
                findings.AddRange(ScanTextWithKeys(relativePath, content, detector, profile));
            }

            var entriesByShard = ReadBaselineShards(repoRoot, reader);
            var allBaselineEntries = entriesByShard
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .SelectMany(kv => kv.Value)
                .ToList();

            List<string> baseline;
            if (forceFullTrackedScan)
            {
                baseline = allBaselineEntries;
            }
            else
            {
                var inScopeSet = scanned.ToHashSet(StringComparer.Ordinal);
                if (files is not null)
                {
                    foreach (string rawFile in files)
                    {
                        string rel = Path.GetRelativePath(repoRoot, Path.IsPathRooted(rawFile) ? rawFile : Path.Combine(repoRoot, rawFile))
                            .Replace('\\', '/');
                        if (!rel.StartsWith("../", StringComparison.Ordinal) && rel != ".." && !IsExcludedPath(rel))
                        {
                            inScopeSet.Add(rel);
                        }
                    }
                }

                baseline = allBaselineEntries.Where(k =>
                {
                    int pipeIdx = k.IndexOf('|', StringComparison.Ordinal);
                    return pipeIdx > 0 && inScopeSet.Contains(k[..pipeIdx]);
                }).ToList();
            }

            var (newFindings, stale) = CompareWithBaseline(findings, baseline);

            int prunedCount = 0;
            if (updateBaseline)
            {
                prunedCount = SubtractStaleEntriesFromShards(repoRoot, entriesByShard, stale);
                if (prunedCount > 0)
                {
                    Console.WriteLine($"✔ Pruned {prunedCount} stale DEID baseline entr{(prunedCount == 1 ? "y" : "ies")} under '{BaselineDirectory}/'.");
                }

                stale = [];
            }

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
                    Console.WriteLine($"\n[ERROR] {stale.Count} '{BaselineDirectory}/' baseline entries no longer match; shards may only shrink. Run `CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid` to drop them.");
                }

                return 1;
            }

            int remainingScopedBaseline = baseline.Count - prunedCount;
            Console.WriteLine($"✔ DEID compliance scan passed cleanly ({scanned.Count} files; {remainingScopedBaseline} baselined findings).");
            return 0;
        }
    }

    /// <summary>
    /// Splits findings into those not covered by the baseline and baseline entries no finding uses. Entries are
    /// counted, so a matched token that appears twice in the same file needs two entries.
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
    /// A finding's baseline entry: <c>path|category|hash</c>, where the hash is the first 16 lowercase hex digits of the
    /// SHA-256 of the trimmed matched substring. Non-PHI edits elsewhere on the line leave the digest unchanged, and no raw
    /// PHI is stored or printed.
    /// </summary>
    public static string BaselineKey(DeidFinding finding, string matchedValue)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(matchedValue);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(matchedValue.Trim()));
        return $"{finding.File}|{finding.Category}|{Convert.ToHexStringLower(hash)[..16]}";
    }

    /// <summary>Writes per-unit DEID baseline shards under <see cref="BaselineDirectory"/> and deletes empty shards.</summary>
    public static void WriteBaselineShards(string repoRoot, IEnumerable<string> keys)
    {
        string deidRootFull = Path.Combine(repoRoot, BaselineDirectory);
        var grouped = keys
            .GroupBy(key => GetShardRelativePath(ExtractFileFromBaselineEntry(key)), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Order(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        if (Directory.Exists(deidRootFull))
        {
            foreach (string existingFull in Directory.GetFiles(deidRootFull, "*" + ShardSuffix, SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, existingFull).Replace('\\', '/');
                if (!grouped.TryGetValue(rel, out var shardKeys) || shardKeys.Count == 0)
                {
                    File.Delete(existingFull);
                    BaselineUnitPath.DeleteEmptyParentDirectories(existingFull, deidRootFull);
                }
            }
        }

        foreach (var (shardRel, shardKeys) in grouped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (shardKeys.Count == 0)
            {
                continue;
            }

            string fullPath = Path.Combine(repoRoot, shardRel);
            WriteShardFile(fullPath, shardKeys);
        }
    }

    private static Dictionary<string, List<string>> ReadBaselineShards(string repoRoot, GitIndexReader? reader)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (reader is { HasAuthoritativeIndex: true })
        {
            foreach (string relPath in reader.Paths
                .Where(p => p.StartsWith(BaselineDirectory + "/", StringComparison.Ordinal) &&
                            p.EndsWith(ShardSuffix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal))
            {
                if (reader.TryReadLines(relPath, out string[] indexedLines))
                {
                    result[relPath] = [.. indexedLines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
                }
            }

            return result;
        }

        string deidRootFull = Path.Combine(repoRoot, BaselineDirectory);
        if (!Directory.Exists(deidRootFull))
        {
            return result;
        }

        foreach (string fullPath in Directory.EnumerateFiles(deidRootFull, "*" + ShardSuffix, SearchOption.AllDirectories))
        {
            string relPath = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
            result[relPath] = [.. File.ReadAllLines(fullPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
        }

        return result;
    }

    private static int SubtractStaleEntriesFromShards(
        string repoRoot,
        Dictionary<string, List<string>> entriesByShard,
        IReadOnlyList<string> staleEntries)
    {
        string deidRootFull = Path.Combine(repoRoot, BaselineDirectory);
        var staleCountsByShard = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (string staleKey in staleEntries)
        {
            string shardRel = GetShardRelativePath(ExtractFileFromBaselineEntry(staleKey));
            if (!staleCountsByShard.TryGetValue(shardRel, out var counts))
            {
                counts = new Dictionary<string, int>(StringComparer.Ordinal);
                staleCountsByShard[shardRel] = counts;
            }

            counts[staleKey] = counts.GetValueOrDefault(staleKey) + 1;
        }

        int totalPruned = 0;
        foreach (var (shardRel, existingEntries) in entriesByShard)
        {
            string shardFullPath = Path.Combine(repoRoot, shardRel);
            if (existingEntries.Count == 0)
            {
                if (File.Exists(shardFullPath))
                {
                    File.Delete(shardFullPath);
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, deidRootFull);
                }

                continue;
            }

            if (!staleCountsByShard.TryGetValue(shardRel, out var toSubtract) || toSubtract.Count == 0)
            {
                continue;
            }

            var kept = new List<string>(existingEntries.Count);
            foreach (string entry in existingEntries)
            {
                if (toSubtract.TryGetValue(entry, out int remainingToDrop) && remainingToDrop > 0)
                {
                    toSubtract[entry] = remainingToDrop - 1;
                    totalPruned++;
                }
                else
                {
                    kept.Add(entry);
                }
            }

            kept.Sort(StringComparer.Ordinal);
            if (kept.Count == 0)
            {
                if (File.Exists(shardFullPath))
                {
                    File.Delete(shardFullPath);
                    BaselineUnitPath.DeleteEmptyParentDirectories(shardFullPath, deidRootFull);
                }
            }
            else if (!existingEntries.SequenceEqual(kept, StringComparer.Ordinal))
            {
                WriteShardFile(shardFullPath, kept);
            }
        }

        return totalPruned;
    }

    private static string ExtractFileFromBaselineEntry(string entry)
    {
        int pipeIdx = entry.IndexOf('|', StringComparison.Ordinal);
        return pipeIdx > 0 ? entry[..pipeIdx] : entry;
    }

    private static void WriteShardFile(string fullPath, IReadOnlyList<string> sortedKeys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, string.Join('\n', BaselineShardHeader.Concat(sortedKeys)) + "\n");
    }

    /// <summary>
    /// The findings in one file's text after applying inline pragma and development-value allowlists.
    /// </summary>
    public static IReadOnlyList<DeidFinding> ScanText(
        string relativePath,
        string content,
        DeidDetector detector,
        ComplianceProfile profile = ComplianceProfile.HIPAA) =>
        [.. ScanTextWithKeys(relativePath, content, detector, profile).Select(item => item.Finding)];

    private static List<(DeidFinding Finding, string Key)> ScanTextWithKeys(
        string relativePath,
        string content,
        DeidDetector detector,
        ComplianceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(detector);
        string[] lines = content.Split('\n');
        var findings = new List<(DeidFinding Finding, string Key)>();
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

            var finding = new DeidFinding(relativePath, line, column, result.Category);
            findings.Add((finding, BaselineKey(finding, result.Value)));
        }

        return findings;
    }

    /// <summary>Whether an email address belongs to an approved synthetic Cadence or RFC 2606 <c>.invalid</c> domain.</summary>
    public static bool IsApprovedSyntheticEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        int atIndex = email.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
        {
            return false;
        }

        string normalizedEmail = email
            .Trim()
            .Trim('.', ',', ';', ':', ')', ']', '>', '<', '"', '\'');
        if (ApprovedCanonicalDemoEmails.Contains(normalizedEmail))
        {
            return true;
        }

        string domain = email[(atIndex + 1)..]
            .Trim()
            .TrimEnd('.', ',', ';', ':', ')', ']', '>', '"', '\'')
            .ToLowerInvariant();
        if (domain.Length == 0)
        {
            return false;
        }

        if (domain == "invalid" || domain.EndsWith(".invalid", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (string approved in ApprovedSyntheticEmailDomains)
        {
            if (domain == approved || domain.EndsWith("." + approved, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a repository-relative path is outside the scan: tests, vendored standards, GxP runs, tooling and generated folders.</summary>
    public static bool IsExcludedPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        string lower = normalized.ToLowerInvariant();
        if (ExcludedRelativePaths.Contains(lower) ||
            ExcludedPathPrefixes.Any(prefix => lower.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
        }

        string[] parts = lower.Split('/');
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
        bool isDocOrLicense = path.StartsWith("docs/", StringComparison.Ordinal) ||
                              path.EndsWith(".md", StringComparison.Ordinal) ||
                              path is "license";
        return category switch
        {
            DetectorCategory.Urls =>
                AllowedUrlFragments.Any(f => val.Contains(f, StringComparison.Ordinal)) ||
                line.Contains("brand_domain", StringComparison.Ordinal),
            DetectorCategory.Email =>
                !val.Contains('@', StringComparison.Ordinal) || IsApprovedSyntheticEmail(val),
            DetectorCategory.IpMacAddresses =>
                val is "127.0.0.1" or "0.0.0.0" or "::1" ||
                (isDocOrLicense && IsDocumentationExampleNetworkAddress(val)),
            DetectorCategory.ZipGeographic =>
                AllowedZipValues.Contains(val) ||
                ZipContextWords.Any(w => line.Contains(w, StringComparison.Ordinal)) ||
                path.Contains("xml_mapping", StringComparison.Ordinal) ||
                path.Contains("concept_maps", StringComparison.Ordinal) ||
                path.Contains("matcher", StringComparison.Ordinal) ||
                path.EndsWith(".svg", StringComparison.Ordinal) ||
                isDocOrLicense,
            DetectorCategory.TelephoneFax => IsAllowedTelephoneValue(val, lineContent, path, isDocOrLicense),
            DetectorCategory.Dates =>
                AllowedDateValues.Contains(val) ||
                DateContextWords.Any(w => line.Contains(w, StringComparison.Ordinal)) ||
                isDocOrLicense,
            DetectorCategory.Age or DetectorCategory.CertificateLicense or DetectorCategory.DeviceSerial
                or DetectorCategory.MedicalRecordAccount or DetectorCategory.HealthPlanBeneficiary
                or DetectorCategory.VehicleIdentifiers when isDocOrLicense => true,
            _ => false,
        };
    }

    private static bool IsDocumentationExampleNetworkAddress(string val) =>
        val.StartsWith("10.", StringComparison.Ordinal) ||
        val.StartsWith("192.168.", StringComparison.Ordinal) ||
        val.StartsWith("192.0.2.", StringComparison.Ordinal) ||
        val.StartsWith("198.51.100.", StringComparison.Ordinal) ||
        val.StartsWith("203.0.113.", StringComparison.Ordinal) ||
        val.StartsWith("172.", StringComparison.Ordinal) ||
        val.StartsWith("255.255.", StringComparison.Ordinal) ||
        val.Contains(':', StringComparison.Ordinal);

    private static bool IsAllowedTelephoneValue(string val, string lineContent, string path, bool isDocOrLicense)
    {
        string line = lineContent.ToLowerInvariant();
        string digits = val.Replace("+", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        return isDocOrLicense
            || lineContent.Contains("concept", StringComparison.Ordinal)
            || path.Contains("concept_maps", StringComparison.Ordinal)
            || path.Contains("matcher", StringComparison.Ordinal)
            || line.Contains("bandwidth", StringComparison.Ordinal)
            || line.Contains("bitrate", StringComparison.Ordinal)
            || line.Contains("bytes", StringComparison.Ordinal)
            || lineContent.Contains("usr_", StringComparison.Ordinal)
            || val is "+1234567890" or "+0987654321"
            || digits.Length == 0 || !digits.All(char.IsAsciiDigit)
            || val.Contains('.', StringComparison.Ordinal)
            || digits.Contains("0000000000", StringComparison.Ordinal)
            || digits.All(c => c == '0')
            || DecimalNumberRegex().Matches(lineContent).Any(m => m.Value.Replace(".", string.Empty, StringComparison.Ordinal).Contains(val, StringComparison.Ordinal))
            || HexTokenRegex().Matches(lineContent).Any(m => m.Value.Contains(val, StringComparison.OrdinalIgnoreCase))
            || UuidRegex().Matches(lineContent).Any(m => m.Value.Contains(val.Trim(), StringComparison.OrdinalIgnoreCase));
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

    // Explicit files, GitRevisionScopeService diff scope, or every file git tracks (git ls-files -z), filtered through
    // IsExcludedPath and ShouldScanFile (TextExtensions, AlwaysScannedNames, and extensionless NUL-byte sniffing).
    public static IReadOnlyList<string> CandidateFiles(
        string repoRoot,
        string[]? files = null,
        bool preCommit = false,
        string? baseRef = null,
        string? pushBefore = null,
        bool forceTrackedScan = false,
        GitIndexReader? reader = null,
        string? eventName = null,
        string? gitRef = null)
    {
        IEnumerable<string> relative;
        if (files is not null)
        {
            relative = files.Select(f => Path.GetRelativePath(repoRoot, Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)));
        }
        else if (forceTrackedScan)
        {
            relative = TrackedFiles(repoRoot);
        }
        else
        {
            var scope = GitRevisionScopeService.ResolveFileScanScope(
                repoRoot,
                explicitFiles: null,
                preCommit: preCommit,
                baseRef: baseRef,
                pushBefore: pushBefore,
                eventName: eventName,
                gitRef: gitRef);
            relative = scope.Kind == GitScopeKind.NonGitDirectory
                ? TrackedFiles(repoRoot)
                : scope.Files;
        }

        return [.. relative
            .Select(p => p.Replace('\\', '/'))
            .Where(p => !p.StartsWith("../", StringComparison.Ordinal) && p != ".." && !IsExcludedPath(p))
            .Where(p => reader is not null
                ? reader.FileExists(p) && ShouldScanStagedFile(p, reader)
                : File.Exists(Path.Combine(repoRoot, p)) && ShouldScanFile(Path.Combine(repoRoot, p)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static bool ShouldScanStagedFile(string relativePath, GitIndexReader reader)
    {
        if (AlwaysScannedNames.Contains(Path.GetFileName(relativePath)))
        {
            return true;
        }

        string extension = Path.GetExtension(relativePath);
        if (extension.Length > 0)
        {
            return TextExtensions.Contains(extension);
        }

        if (!reader.TryReadBytes(relativePath, out byte[] bytes))
        {
            return false;
        }

        int probeLen = Math.Min(bytes.Length, 1024);
        return Array.IndexOf(bytes, (byte)0, 0, probeLen) < 0;
    }

    private static IEnumerable<string> TrackedFiles(string repoRoot) =>
        GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-files", "-z")
        ?? throw new InvalidOperationException($"git ls-files failed in {repoRoot}; the DEID scan needs git to list files.");

    /// <summary>Resolves the repository root directory from the current working directory or binary path.</summary>
    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();

    [GeneratedRegex(@"\d+\.\d+")]
    private static partial Regex DecimalNumberRegex();

    [GeneratedRegex(@"[0-9a-fA-F]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex HexTokenRegex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex UuidRegex();
}
