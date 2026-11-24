using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class EnvVarExtractorService
{
    [GeneratedRegex(@"GetEnvironmentVariable\s*\(\s*""([^""]+)""")]
    private static partial Regex DotnetEnvRegex();

    [GeneratedRegex(@"(?:Configuration|configuration|GetService<IConfiguration>\(\)\??|GetRequiredService<IConfiguration>\(\))\s*\[\s*""([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)""\s*\]|\.(?:GetValue(?:<[^>]+>)?|GetSection)\s*\(\s*""([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)""|\bconfiguration\s*,\s*""([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)""")]
    private static partial Regex DotnetConfigLiteralRegex();

    [GeneratedRegex(@"\bconst\s+string\s+\w*(?:Configuration|Config|ConnectionString)Key\s*=\s*""([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)""")]
    private static partial Regex DotnetConfigKeyConstantRegex();

    [GeneratedRegex(@"\bconst\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*""([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+)""")]
    private static partial Regex DotnetScreamingConstantDeclRegex();

    [GeneratedRegex(@"(?:(?:Configuration|configuration|GetService<IConfiguration>\(\)\??|GetRequiredService<IConfiguration>\(\))\s*\[\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\]|\.(?:GetValue(?:<[^>]+>)?|GetSection)\s*\(\s*([A-Za-z_][A-Za-z0-9_.]*)|(?:Environment\.)?GetEnvironmentVariable\s*\(\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\))")]
    private static partial Regex DotnetConfigIdentifierRefRegex();

    [GeneratedRegex(@"os\.environ\.get\s*\(\s*""([^""]+)""|os\.environ\[""([^""]+)""\]")]
    private static partial Regex PythonEnvRegex();

    [GeneratedRegex(@"import\.meta\.env\.(VITE_[A-Z0-9_]+)")]
    private static partial Regex ViteImportMetaEnvRegex();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParentheticalNoteRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex BacktickTokenRegex();

    [GeneratedRegex(@"`((?:apps|packages|scripts|tests|docs|\.github)/[^`\s]+)`")]
    private static partial Regex CitedRepoPathRegex();

    [GeneratedRegex(@":\d+(?:-\d+)?$")]
    private static partial Regex LineSuffixRegex();

    private static readonly HashSet<string> IgnoredOsEnvironmentVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH",
        "PATHEXT",
        "HOME",
        "USER",
        "USERNAME",
        "USERPROFILE",
        "APPDATA",
        "LOCALAPPDATA",
        "SYSTEMROOT",
        "COMSPEC",
        "TMP",
        "TEMP",
        "TMPDIR",
        "SHELL",
        "TERM",
        "NO_COLOR",
    };

    public static int ExtractAndValidateEnvVars(bool checkOnly = true, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Extracting and verifying environment variable declarations...");

        var envVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var screamingConstantsByIdent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var referencedConfigIdents = new HashSet<string>(StringComparer.Ordinal);

        var searchDirs = new[] { "apps", "packages" };
        foreach (var dir in searchDirs)
        {
            string fullPath = Path.Combine(repoRoot, dir);
            if (!Directory.Exists(fullPath)) continue;

            foreach (var file in Directory.EnumerateFiles(fullPath, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}dist-demo{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                string ext = Path.GetExtension(file).ToLowerInvariant();
                string content;
                try { content = File.ReadAllText(file); } catch { continue; }

                if (ext == ".cs")
                {
                    ExtractDotnetEnvVarsFromContent(content, envVars, screamingConstantsByIdent, referencedConfigIdents);
                }
                else if (ext == ".py")
                {
                    var pyMatches = PythonEnvRegex().Matches(content);
                    for (int i = 0; i < pyMatches.Count; i++)
                    {
                        var m = pyMatches[i];
                        if (m.Groups[1].Success) envVars.Add(m.Groups[1].Value);
                        if (m.Groups[2].Success) envVars.Add(m.Groups[2].Value);
                    }
                }
                else if (ext is ".js" or ".mjs" or ".cjs" or ".ts" or ".vue")
                {
                    var viteMatches = ViteImportMetaEnvRegex().Matches(content);
                    for (int i = 0; i < viteMatches.Count; i++)
                    {
                        if (viteMatches[i].Groups[1].Success)
                        {
                            envVars.Add(viteMatches[i].Groups[1].Value);
                        }
                    }
                }
            }
        }

        foreach (string identRef in referencedConfigIdents)
        {
            int lastDot = identRef.LastIndexOf('.');
            string shortIdent = lastDot >= 0 ? identRef[(lastDot + 1)..] : identRef;
            if (screamingConstantsByIdent.TryGetValue(shortIdent, out var values))
            {
                foreach (string val in values)
                {
                    envVars.Add(val);
                }
            }
        }

        envVars.RemoveWhere(IgnoredOsEnvironmentVariables.Contains);
        Console.WriteLine($"Discovered {envVars.Count} distinct environment variable accesses in code.");

        string envExamplePath = Path.Combine(repoRoot, ".env.example");
        string envDocPath = Path.Combine(repoRoot, "docs", "ENVIRONMENT_VARIABLES.md");

        if (!File.Exists(envExamplePath))
        {
            Console.WriteLine($"[ERROR] .env.example not found at {envExamplePath}");
            return 1;
        }

        bool hasError = false;

        if (checkOnly)
        {
            var exampleVars = ExtractVarsFromEnvFile(envExamplePath);
            var exampleVarsCaseInsensitive = new HashSet<string>(exampleVars, StringComparer.OrdinalIgnoreCase);
            var missingInExample = envVars
                .Where(v => !exampleVarsCaseInsensitive.Contains(v))
                .Order(StringComparer.Ordinal)
                .ToList();

            if (missingInExample.Count > 0)
            {
                Console.WriteLine($"[ERROR] {missingInExample.Count} environment variables found in code are missing active declarations in .env.example:");
                foreach (var missing in missingInExample)
                {
                    Console.WriteLine($"  - {missing}");
                }
                hasError = true;
            }

            if (File.Exists(envDocPath))
            {
                string docText = File.ReadAllText(envDocPath);
                var docConcreteKeys = ExtractConcreteDocKeys(docText);

                foreach (var v in exampleVars.Order(StringComparer.Ordinal))
                {
                    if (!docConcreteKeys.Contains(v) && !docText.Contains($"`{v}`", StringComparison.Ordinal))
                    {
                        Console.WriteLine($"[ERROR] Environment variable '{v}' in .env.example is missing from docs/ENVIRONMENT_VARIABLES.md");
                        hasError = true;
                    }
                }

                var missingFromExample = docConcreteKeys
                    .Where(k => !exampleVars.Contains(k))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (missingFromExample.Count > 0)
                {
                    Console.WriteLine($"[ERROR] {missingFromExample.Count} concrete environment variable(s) documented in docs/ENVIRONMENT_VARIABLES.md are missing active declarations in .env.example:");
                    foreach (string key in missingFromExample)
                    {
                        Console.WriteLine($"  - {key}");
                    }
                    hasError = true;
                }

                foreach (string missingPath in FindMissingCitedPaths(repoRoot, docText))
                {
                    Console.WriteLine($"[ERROR] docs/ENVIRONMENT_VARIABLES.md cites non-existent repository path '{missingPath}'.");
                    hasError = true;
                }
            }
            else
            {
                Console.WriteLine($"[ERROR] Documentation file not found at {envDocPath}");
                hasError = true;
            }
        }

        if (hasError)
        {
            Console.WriteLine("❌ Environment variable verification failed with discrepancies.");
            return 1;
        }

        Console.WriteLine("✔ Environment variable extraction and verification complete.");
        return 0;
    }

    private static void ExtractDotnetEnvVarsFromContent(
        string content,
        HashSet<string> envVars,
        Dictionary<string, HashSet<string>> screamingConstantsByIdent,
        HashSet<string> referencedConfigIdents)
    {
        var csMatches = DotnetEnvRegex().Matches(content);
        for (int i = 0; i < csMatches.Count; i++)
        {
            if (csMatches[i].Groups[1].Success)
            {
                envVars.Add(csMatches[i].Groups[1].Value.Trim());
            }
        }

        var configLiteralMatches = DotnetConfigLiteralRegex().Matches(content);
        for (int i = 0; i < configLiteralMatches.Count; i++)
        {
            var m = configLiteralMatches[i];
            for (int g = 1; g <= 3; g++)
            {
                if (m.Groups[g].Success)
                {
                    envVars.Add(m.Groups[g].Value.Trim());
                }
            }
        }

        var configConstMatches = DotnetConfigKeyConstantRegex().Matches(content);
        for (int i = 0; i < configConstMatches.Count; i++)
        {
            if (configConstMatches[i].Groups[1].Success)
            {
                envVars.Add(configConstMatches[i].Groups[1].Value.Trim());
            }
        }

        var constDecls = DotnetScreamingConstantDeclRegex().Matches(content);
        for (int i = 0; i < constDecls.Count; i++)
        {
            string ident = constDecls[i].Groups[1].Value;
            string val = constDecls[i].Groups[2].Value.Trim();
            if (!screamingConstantsByIdent.TryGetValue(ident, out var list))
            {
                list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                screamingConstantsByIdent[ident] = list;
            }
            list.Add(val);
        }

        var identRefs = DotnetConfigIdentifierRefRegex().Matches(content);
        for (int i = 0; i < identRefs.Count; i++)
        {
            var m = identRefs[i];
            for (int g = 1; g <= 3; g++)
            {
                if (m.Groups[g].Success)
                {
                    referencedConfigIdents.Add(m.Groups[g].Value);
                }
            }
        }
    }

    public static HashSet<string> ExtractVarsFromEnvFile(string filePath)
    {
        var vars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(filePath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            if (trimmed.Contains('=', StringComparison.Ordinal))
            {
                string key = trimmed.Split('=', 2)[0].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    vars.Add(key);
                }
            }
        }
        return vars;
    }

    public static HashSet<string> ExtractConcreteDocKeys(string docText)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in docText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith('|') || line.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('|');
            if (parts.Length < 3)
            {
                continue;
            }

            string col1 = ParentheticalNoteRegex().Replace(parts[1].Trim(), "");
            if (col1.Length == 0 || string.Equals(col1, "Key", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match tokenMatch in BacktickTokenRegex().Matches(col1))
            {
                string token = tokenMatch.Groups[1].Value.Trim();
                if (token.Length == 0 ||
                    token.Contains('<', StringComparison.Ordinal) ||
                    token.Contains('>', StringComparison.Ordinal) ||
                    token.Contains('*', StringComparison.Ordinal) ||
                    token.StartsWith("__", StringComparison.Ordinal))
                {
                    continue;
                }

                keys.Add(token);
            }
        }

        return keys;
    }

    public static IReadOnlyList<string> FindMissingCitedPaths(string repoRoot, string docText)
    {
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in CitedRepoPathRegex().Matches(docText))
        {
            string raw = match.Groups[1].Value.TrimEnd('.', ',', ';', ')');
            string pathOnly = LineSuffixRegex().Replace(raw, "");
            if (pathOnly.Contains('<', StringComparison.Ordinal) ||
                pathOnly.Contains('>', StringComparison.Ordinal) ||
                pathOnly.Contains('*', StringComparison.Ordinal) ||
                !seen.Add(raw))
            {
                continue;
            }

            string fullPath = Path.Combine(repoRoot, pathOnly.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                missing.Add(raw);
            }
        }

        return missing;
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
}
