using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Cadence.ToolingCore.Services;

public static class DependencyValidatorService
{
    private static readonly string[] ForbiddenPackages = ["node-forge", "elliptic", "jose"];
    private static readonly string[] ForbiddenRuntimeTestPackages =
    [
        "axe-core",
        "@axe-core/playwright",
        "vitest",
        "@vue/test-utils",
        "jsdom",
        "fake-indexeddb",
        "msw",
    ];
    private static readonly string[] DependencySections = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];

    public static int ValidateDependencies(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating dependency manifests and checking frontend dependencies for forbidden cryptographic libraries...");

        var packageManifests = DiscoverPackageManifests(repoRoot);
        string dotnetToolsPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        bool hasDotnetTools = File.Exists(dotnetToolsPath);

        if (packageManifests.Count == 0 && !hasDotnetTools)
        {
            Console.WriteLine("✔ No dependency manifests found, skipping.");
            return 0;
        }

        int errors = 0;

        foreach (string pkgPath in packageManifests)
        {
            string relativePath = Path.GetRelativePath(repoRoot, pkgPath).Replace('\\', '/');
            errors += ValidatePackageManifest(pkgPath, relativePath);
        }

        if (hasDotnetTools)
        {
            errors += ValidateDotnetToolsManifest(dotnetToolsPath);
        }

        if (errors > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {errors} dependency manifest or security violation(s).");
            return 1;
        }

        Console.WriteLine("✔ Dependency manifests and frontend crypto checks passed cleanly.");
        return 0;
    }

    private static List<string> DiscoverPackageManifests(string repoRoot)
    {
        var manifests = new List<string>();

        string rootPkg = Path.Combine(repoRoot, "package.json");
        if (File.Exists(rootPkg))
        {
            manifests.Add(rootPkg);
        }

        foreach (string container in new[] { "apps", "packages" })
        {
            string containerPath = Path.Combine(repoRoot, container);
            if (!Directory.Exists(containerPath))
            {
                continue;
            }

            foreach (string childDir in Directory.GetDirectories(containerPath).OrderBy(d => d, StringComparer.Ordinal))
            {
                string childPkg = Path.Combine(childDir, "package.json");
                if (File.Exists(childPkg))
                {
                    manifests.Add(childPkg);
                }
            }
        }

        return manifests;
    }

    private static int ValidatePackageManifest(string pkgPath, string relativePath)
    {
        try
        {
            string json = File.ReadAllText(pkgPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} root element must be a JSON object.");
                return 1;
            }

            int errors = 0;
            foreach (string section in DependencySections)
            {
                if (!root.TryGetProperty(section, out var depsObj))
                {
                    continue;
                }

                if (depsObj.ValueKind != JsonValueKind.Object)
                {
                    Console.WriteLine($"[ERROR] {relativePath} property '{section}' must be a JSON object.");
                    errors++;
                    continue;
                }

                foreach (var entry in depsObj.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
                    {
                        Console.WriteLine($"[ERROR] {relativePath} dependency '{entry.Name}' in '{section}' must specify a non-empty version string.");
                        errors++;
                    }
                }

                foreach (string forbidden in ForbiddenPackages)
                {
                    if (depsObj.TryGetProperty(forbidden, out _))
                    {
                        Console.WriteLine($"[SECURITY VIOLATION] Forbidden client-side crypto package '{forbidden}' detected in {relativePath} ({section}).");
                        errors++;
                    }
                }

                if (string.Equals(section, "dependencies", StringComparison.Ordinal))
                {
                    foreach (string testPkg in ForbiddenRuntimeTestPackages)
                    {
                        if (depsObj.TryGetProperty(testPkg, out _))
                        {
                            Console.WriteLine($"[DEPENDENCY VIOLATION] Test-only package '{testPkg}' is forbidden in runtime 'dependencies' of {relativePath}; move it to 'devDependencies'.");
                            errors++;
                        }
                    }
                }
            }

            return errors;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.WriteLine($"[ERROR] Failed to parse {relativePath}: {ex.Message}");
            return 1;
        }
    }

    private static int ValidateDotnetToolsManifest(string dotnetToolsPath)
    {
        const string relativePath = ".config/dotnet-tools.json";
        try
        {
            string json = File.ReadAllText(dotnetToolsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} root element must be a JSON object.");
                return 1;
            }

            if (!root.TryGetProperty("tools", out var toolsObj) || toolsObj.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} must contain a 'tools' JSON object.");
                return 1;
            }

            int errors = 0;
            foreach (var tool in toolsObj.EnumerateObject())
            {
                if (tool.Value.ValueKind != JsonValueKind.Object ||
                    !tool.Value.TryGetProperty("version", out var versionProp) ||
                    versionProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(versionProp.GetString()))
                {
                    Console.WriteLine($"[ERROR] {relativePath} tool '{tool.Name}' must define a non-empty string 'version'.");
                    errors++;
                }
            }

            return errors;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Console.WriteLine($"[ERROR] Failed to parse {relativePath}: {ex.Message}");
            return 1;
        }
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
