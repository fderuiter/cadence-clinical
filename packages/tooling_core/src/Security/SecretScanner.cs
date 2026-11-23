using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Services;

namespace Cadence.ToolingCore.Security;

public record SecretScanFinding(
    string FilePath,
    int LineNumber,
    string PatternName,
    string FindingSnippet
);

public static class SecretScanner
{
    private static readonly (string PatternName, Regex Pattern)[] SecretPatterns =
    [
        ("Private Key Header", new Regex(@"-----BEGIN (?:(?:RSA|DSA|EC|OPENSSH|PGP|ENCRYPTED) )?PRIVATE KEY-----", RegexOptions.Compiled)),
        ("AWS Access Key ID", new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.Compiled)),
        ("AWS Secret Key", new Regex(@"(?i)aws_secret_access_key\s*[:=]\s*['""]?[A-Za-z0-9/+=]{40}['""]?", RegexOptions.Compiled)),
        ("GCP API Key", new Regex(@"\bAIza[0-9A-Za-z\-_]{35}\b", RegexOptions.Compiled)),
        ("Azure Storage Account Key", new Regex(@"(?i)AccountKey\s*=\s*[A-Za-z0-9+/]{86}==", RegexOptions.Compiled)),
        ("GitHub Token", new Regex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36}\b|\bgithub_pat_[A-Za-z0-9_]{82}\b", RegexOptions.Compiled)),
        ("Slack Token", new Regex(@"\bxox[baprs]-[0-9A-Za-z\-]{10,}\b", RegexOptions.Compiled)),
        ("Stripe Live Secret Key", new Regex(@"\b(?:sk|rk)_live_[0-9A-Za-z]{24,}\b", RegexOptions.Compiled)),
        ("Hardcoded Compliance Secret", new Regex(@"(?i)\b(?:AUDIT_LOG_SECRET_KEY|INBOUND_EMAIL_HMAC_SECRET|GATEWAY_SECRET_KEY)\b\s*[:=]\s*['""][^'""\r\n]{8,}['""]|development_secret_key_for_rtm_generation", RegexOptions.Compiled)),
    ];

    /// <summary>Compiled secret patterns shared with post-build public bundle verification.</summary>
    public static IReadOnlyList<(string PatternName, Regex Pattern)> Patterns => SecretPatterns;

    private static readonly string[] ExcludedDirKeywords =
    [
        "tests", "fixtures", ".git", "bin", "obj", "node_modules", ".venv", ".agents", ".cadence", "baselines", "TestResults", "coverage",
    ];

    private static readonly string[] BinaryExtensions =
    [
        ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".woff", ".woff2", ".ttf", ".otf",
        ".eot", ".pdf", ".zip", ".gz", ".tar", ".sqlite", ".db",
    ];

    public static List<SecretScanFinding> ScanFiles(
        string repoRoot,
        IEnumerable<string> filesToScan,
        GitIndexReader? indexReader = null)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(filesToScan);

        var findings = new List<SecretScanFinding>();

        foreach (var file in filesToScan)
        {
            string? rel;
            if (indexReader is not null)
            {
                rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, file);
                if (string.IsNullOrEmpty(rel) || !indexReader.FileExists(rel))
                {
                    continue;
                }
            }
            else
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            }

            var pathParts = rel.Split('/');

            if (pathParts.Any(p => ExcludedDirKeywords.Contains(p, StringComparer.OrdinalIgnoreCase)) ||
                rel.StartsWith("docs/SDLC/runs/", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith(".test.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("mockGateway.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("SecretScanner.cs", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("SecurityAuditorService.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string filename = Path.GetFileName(rel);
            if (filename is ".secrets.baseline" or "pnpm-lock.yaml" or "uv.lock" ||
                BinaryExtensions.Any(ext => filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                IEnumerable<string> lines;
                if (indexReader is not null)
                {
                    if (!indexReader.TryReadLines(rel, out string[] indexedLines))
                    {
                        continue;
                    }

                    lines = indexedLines;
                }
                else
                {
                    lines = File.ReadLines(file);
                }

                int lineNum = 1;
                foreach (var line in lines)
                {
                    if (line.Contains("pragma: allowlist secret", StringComparison.OrdinalIgnoreCase))
                    {
                        lineNum++;
                        continue;
                    }

                    foreach (var (pName, regex) in SecretPatterns)
                    {
                        if (regex.IsMatch(line))
                        {
                            findings.Add(new SecretScanFinding(
                                FilePath: rel,
                                LineNumber: lineNum,
                                PatternName: pName,
                                FindingSnippet: line.Trim()
                            ));
                        }
                    }

                    lineNum++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip unreadable files
            }
        }

        return findings;
    }
}
