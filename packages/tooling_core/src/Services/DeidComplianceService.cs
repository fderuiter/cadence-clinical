using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Deid;
using Cadence.ToolingCore.Primitives;

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

    /// <summary>Resolves the repository root directory from the current working directory or binary path.</summary>
    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();

    [GeneratedRegex(@"\d+\.\d+")]
    private static partial Regex DecimalNumberRegex();

    [GeneratedRegex(@"[0-9a-fA-F]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex HexTokenRegex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex UuidRegex();
}
