using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class ReadmeValidatorService
{
    [GeneratedRegex(@"^\s*export\s+([A-Za-z_][A-Za-z0-9_]*)=(.*)$", RegexOptions.Multiline)]
    private static partial Regex BashExportRegex();

    private static readonly string[] RootReadmeDomainStartupKeys =
    [
        "DemographicsEncryption__KeyBase64",
        "RANDOMIZATION_ALLOCATION_KEY",
        "CGT_BARCODE_HMAC_KEY",
        "FIELD_ENCRYPTION_KEY",
        "FIELD_LOOKUP_HASH_KEY",
        "CADENCE_SCE_SIGNING_KEY",
        "CADENCE_IMAGING_DEID_SIGNING_KEY",
        "CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY",
        "CADENCE_BIOMETRIC_LIVENESS_KEY",
    ];

    public static int ValidateReadmes(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating README documentation coverage across packages and apps...");

        int issues = 0;
        string[] targetDirs = ["apps", "packages"];

        foreach (var dirName in targetDirs)
        {
            string fullDir = Path.Combine(repoRoot, dirName);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var subDir in Directory.GetDirectories(fullDir))
            {
                string name = Path.GetFileName(subDir);
                if (name.StartsWith('.') || name == "__pycache__" || name == "bin" || name == "obj" || name == "node_modules" || name == ".venv")
                {
                    continue;
                }

                // A package deleted upstream leaves its build output behind in existing checkouts. Such a
                // directory is not a project, so it needs no README.
                if (ContainsOnlyBuildOutput(subDir))
                {
                    continue;
                }

                string readmePath = Path.Combine(subDir, "README.md");
                string relPath = Path.GetRelativePath(repoRoot, subDir);

                if (!File.Exists(readmePath))
                {
                    Console.WriteLine($"[MISSING README] Directory '{relPath}' lacks a README.md file.");
                    issues++;
                }
                else
                {
                    string content = File.ReadAllText(readmePath);
                    if (string.IsNullOrWhiteSpace(content) || !content.Contains("# "))
                    {
                        Console.WriteLine($"[INVALID README] README.md in '{relPath}' is empty or lacks title header ('# ')");
                        issues++;
                    }
                    else if (string.Equals(dirName, "apps", StringComparison.Ordinal))
                    {
                        issues += ValidateServiceRunLocallyExports(subDir, relPath, content);
                    }
                }
            }
        }

        string rootReadmePath = Path.Combine(repoRoot, "README.md");
        if (File.Exists(rootReadmePath) &&
            File.Exists(Path.Combine(repoRoot, "packages", "topology", "src", "ServiceTopology.cs")))
        {
            string rootReadme = File.ReadAllText(rootReadmePath);
            foreach (string key in RootReadmeDomainStartupKeys)
            {
                if (!rootReadme.Contains($"`{key}`", StringComparison.Ordinal))
                {
                    Console.WriteLine($"[INVALID README] Root README.md omits required domain startup key `{key}`.");
                    issues++;
                }
            }
        }

        if (issues > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {issues} README coverage or startup-export issue(s).");
            return 1;
        }

        Console.WriteLine("✔ README coverage validated cleanly.");
        return 0;
    }

    private static int ValidateServiceRunLocallyExports(string appDir, string relPath, string readmeContent)
    {
        string srcDir = Path.Combine(appDir, "src");
        if (!Directory.Exists(srcDir))
        {
            return 0;
        }

        string appName = Path.GetFileName(appDir);
        if (appName is "gateway" or "apphost" or "intelligence")
        {
            return 0;
        }

        string[] csFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        if (csFiles.Length == 0)
        {
            return 0;
        }

        string combinedSource = string.Join("\n", csFiles.Select(File.ReadAllText));
        var requiredExports = DeriveRequiredStartupExports(combinedSource, appName);
        if (requiredExports.Count == 0 && !HasBackendServiceCsproj(srcDir))
        {
            return 0;
        }

        string? runLocallySection = ExtractMarkdownH2Section(readmeContent, "Run locally");
        if (string.IsNullOrWhiteSpace(runLocallySection))
        {
            Console.WriteLine($"[INVALID README] '{relPath}/README.md' lacks a '## Run locally' section.");
            return 1;
        }

        var exported = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in BashExportRegex().Matches(runLocallySection))
        {
            string key = match.Groups[1].Value.Trim();
            string rawVal = match.Groups[2].Value.Trim().Trim('"', '\'');
            exported[key] = rawVal;
        }

        int errors = 0;
        foreach (string requiredKey in requiredExports)
        {
            if (string.Equals(requiredKey, "ConnectionStrings__cadence", StringComparison.Ordinal))
            {
                bool hasCadence = exported.TryGetValue("ConnectionStrings__cadence", out string? v1) && !string.IsNullOrWhiteSpace(v1);
                bool hasPostgres = exported.TryGetValue("POSTGRES_CONNECTION_STRING", out string? v2) && !string.IsNullOrWhiteSpace(v2);
                if (!hasCadence && !hasPostgres)
                {
                    Console.WriteLine($"[INVALID README] '{relPath}/README.md' '## Run locally' omits required export 'ConnectionStrings__cadence' (or 'POSTGRES_CONNECTION_STRING').");
                    errors++;
                }

                continue;
            }

            if (!exported.TryGetValue(requiredKey, out string? val) || string.IsNullOrWhiteSpace(val))
            {
                Console.WriteLine($"[INVALID README] '{relPath}/README.md' '## Run locally' omits required startup export '{requiredKey}'.");
                errors++;
            }
        }

        if (requiredExports.Contains("Storage__Provider") &&
            exported.TryGetValue("Storage__Provider", out string? provider) &&
            string.Equals(provider, "filesystem", StringComparison.OrdinalIgnoreCase) &&
            (!exported.TryGetValue("Storage__RootPath", out string? rootPath) || string.IsNullOrWhiteSpace(rootPath)))
        {
            Console.WriteLine($"[INVALID README] '{relPath}/README.md' '## Run locally' sets Storage__Provider=filesystem without exporting 'Storage__RootPath'.");
            errors++;
        }

        return errors;
    }

    private static List<string> DeriveRequiredStartupExports(string source, string appName)
    {
        var keys = new List<string>();
        bool isKernelHost = string.Equals(appName, "kernel-api", StringComparison.Ordinal) ||
                            source.Contains("AddKernelApi(", StringComparison.Ordinal) ||
                            source.Contains("AddKernelPersistence(", StringComparison.Ordinal);

        if (source.Contains("AddCadenceSecurity(", StringComparison.Ordinal) || isKernelHost)
        {
            keys.Add("GATEWAY_SECRET");
        }

        if (source.Contains("AddCadencePostgresDbContext", StringComparison.Ordinal) ||
            source.Contains("ResolvePostgresConnectionString(", StringComparison.Ordinal) ||
            isKernelHost)
        {
            keys.Add("ConnectionStrings__cadence");
        }

        if (source.Contains("AddCadenceStorage(", StringComparison.Ordinal))
        {
            keys.Add("Storage__Provider");
        }

        if (source.Contains("DemographicsEncryptionOptions", StringComparison.Ordinal))
        {
            keys.Add("DemographicsEncryption__KeyBase64");
        }

        if (source.Contains("AllocationProtectionOptions", StringComparison.Ordinal))
        {
            keys.Add("RANDOMIZATION_ALLOCATION_KEY");
        }

        if (source.Contains("CgtBarcodeProtectionOptions", StringComparison.Ordinal) ||
            source.Contains("CGT_BARCODE_HMAC_KEY", StringComparison.Ordinal))
        {
            keys.Add("CGT_BARCODE_HMAC_KEY");
        }

        if (source.Contains("AddCadenceFieldEncryption(", StringComparison.Ordinal))
        {
            keys.Add("FIELD_ENCRYPTION_KEY");
        }

        if (source.Contains("AddCadenceLookupHashing(", StringComparison.Ordinal))
        {
            keys.Add("FIELD_LOOKUP_HASH_KEY");
        }

        if (source.Contains("AddSceSigning(", StringComparison.Ordinal))
        {
            keys.Add("CADENCE_SCE_SIGNING_KEY");
        }

        if (source.Contains("AddImagingDeidentification(", StringComparison.Ordinal))
        {
            keys.Add("CADENCE_IMAGING_DEID_SIGNING_KEY");
            keys.Add("CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY");
        }

        if (source.Contains("AddCadenceBiometricLiveness(", StringComparison.Ordinal) || isKernelHost)
        {
            keys.Add("CADENCE_BIOMETRIC_LIVENESS_KEY");
        }

        return keys;
    }

    private static bool HasBackendServiceCsproj(string srcDir) =>
        Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Any(name => name is not null &&
                         (name.StartsWith("Cadence.Apps.", StringComparison.Ordinal) ||
                          string.Equals(name, "Cadence.Kernel.Host.csproj", StringComparison.Ordinal)));

    private static string? ExtractMarkdownH2Section(string markdown, string headingTitle)
    {
        string normalized = markdown.ReplaceLineEndings("\n");
        string marker = $"## {headingTitle}\n";
        int idx = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        int bodyStart = idx + marker.Length;
        int nextH2 = normalized.IndexOf("\n## ", bodyStart, StringComparison.Ordinal);
        return nextH2 >= 0 ? normalized[bodyStart..nextH2] : normalized[bodyStart..];
    }

    private static bool ContainsOnlyBuildOutput(string directory) =>
        !Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/'))
            .Any(f => !f.Split('/').Any(part => part is "bin" or "obj" or "node_modules"));

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
