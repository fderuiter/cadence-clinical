using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Security;

public record SecretScanFinding(
    string FilePath,
    int LineNumber,
    string PatternName,
    string FindingSnippet
);

public static class SecretScanner
{
    private static readonly (string PatternName, Regex Pattern)[] SecretPatterns = new[]
    {
        ("AWS Secret Key", new Regex(@"(?i)aws_secret_access_key\s*=\s*['""][A-Za-z0-9/+=]{40}['""]", RegexOptions.Compiled)),
        ("Private Key Header", new Regex(@"-----BEGIN (?:RSA |EC |PGP )?PRIVATE KEY-----", RegexOptions.Compiled))
    };

    private static readonly string[] AllowedPasses = new[]
    {
        "POSTGRES_PASSWORD", "cadence_password", "test_secret", "dummy", "example", ".secrets.baseline", "sample"
    };

    private static readonly string[] ExcludedDirKeywords = new[]
    {
        "tests", "fixtures", ".git", "bin", "obj", "node_modules", ".venv", ".agents", "scripts"
    };

    public static List<SecretScanFinding> ScanFiles(
        string repoRoot,
        IEnumerable<string> filesToScan)
    {
        var findings = new List<SecretScanFinding>();

        foreach (var file in filesToScan)
        {
            if (!File.Exists(file)) continue;

            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var pathParts = rel.Split('/');

            if (pathParts.Any(p => ExcludedDirKeywords.Contains(p, StringComparer.OrdinalIgnoreCase)) ||
                rel.EndsWith(".vue", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith(".test.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("mockGateway.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("build-docs.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("SecurityAuditorService.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string filename = Path.GetFileName(file);
            if (filename == ".secrets.baseline" ||
                filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                int lineNum = 1;
                foreach (var line in File.ReadLines(file))
                {
                    if (AllowedPasses.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase)))
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
            catch
            {
                // Skip unreadable files
            }
        }

        return findings;
    }
}
