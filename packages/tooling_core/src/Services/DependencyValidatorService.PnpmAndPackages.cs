using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class DependencyValidatorService
{
    private static readonly string[] WorkspaceContainers = ["apps", "packages"];
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
    private static readonly string[] DependencySections =
    [
        "dependencies",
        "devDependencies",
        "peerDependencies",
        "optionalDependencies",
    ];

    private static List<string> DiscoverPackageManifests(RepoSnapshot snapshot)
    {
        var manifests = new List<string>();
        if (snapshot.Exists("package.json"))
        {
            manifests.Add("package.json");
        }

        if (snapshot.Reader is { HasAuthoritativeIndex: true })
        {
            var indexedChildren = snapshot.Reader.Paths
                .Where(IsChildWorkspacePackageJson)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            manifests.AddRange(indexedChildren);
            return manifests;
        }

        foreach (string container in WorkspaceContainers)
        {
            string containerPath = Path.Combine(snapshot.RepoRoot, container);
            if (!Directory.Exists(containerPath))
            {
                continue;
            }

            foreach (string childDir in Directory.GetDirectories(containerPath).OrderBy(d => d, StringComparer.Ordinal))
            {
                string childName = Path.GetFileName(childDir);
                string relPkg = $"{container}/{childName}/package.json";
                if (snapshot.Exists(relPkg))
                {
                    manifests.Add(relPkg);
                }
            }
        }

        return manifests;
    }

    private static bool IsChildWorkspacePackageJson(string relativePath)
    {
        string[] parts = relativePath.Split('/');
        return parts.Length == 3 &&
               (string.Equals(parts[0], "apps", StringComparison.Ordinal) ||
                string.Equals(parts[0], "packages", StringComparison.Ordinal)) &&
               string.Equals(parts[2], "package.json", StringComparison.Ordinal);
    }

    private static int ValidatePnpmWorkspaceCatalog(
        RepoSnapshot snapshot,
        out Dictionary<string, string>? catalog)
    {
        const string relativePath = "pnpm-workspace.yaml";
        catalog = null;

        if (!snapshot.TryReadBytes(relativePath, out byte[] bytes, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        var yamlErrors = YamlSyntaxValidatorService.ValidateBytes(bytes, relativePath);
        if (yamlErrors.Count > 0)
        {
            foreach (string err in yamlErrors)
            {
                Console.WriteLine($"[ERROR] {err}");
            }

            return yamlErrors.Count;
        }

        string text = WorkspacePrimitives.StrictUtf8.GetString(WorkspacePrimitives.StripBom(bytes));
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inCatalog = false;
        int errors = 0;

        foreach (string rawLine in GitIndexReader.SplitLines(text))
        {
            string trimmed = YamlPrimitives.StripLineComment(rawLine).Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!char.IsWhiteSpace(rawLine[0]))
            {
                inCatalog = string.Equals(trimmed, "catalog:", StringComparison.Ordinal);
                continue;
            }

            if (!inCatalog || !YamlPrimitives.TrySplitMapping(trimmed, out string key, out string rawVal))
            {
                continue;
            }

            string value = YamlPrimitives.UnquoteScalar(rawVal);
            if (key.Length == 0 || value.Length == 0)
            {
                Console.WriteLine($"[ERROR] {relativePath} catalog entry '{rawLine.Trim()}' must specify a non-empty package name and version.");
                errors++;
                continue;
            }

            parsed[key] = value;
        }

        if (parsed.Count == 0)
        {
            Console.WriteLine($"[ERROR] {relativePath} must define a non-empty 'catalog:' section for shared frontend packages (ADR-2527).");
            errors++;
        }

        catalog = parsed;
        return errors;
    }

    private static int ValidateWorkspacePackageManifests(
        RepoSnapshot snapshot,
        IReadOnlyList<string> packageManifests,
        Dictionary<string, string>? catalog)
    {
        int errors = 0;
        var literalOwnersByPackage = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string relativePath in packageManifests)
        {
            errors += ValidateSinglePackageManifest(snapshot, relativePath, catalog, literalOwnersByPackage);
        }

        if (catalog is not null)
        {
            foreach (var (pkgName, owners) in literalOwnersByPackage.Where(kv => kv.Value.Count > 1))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] Shared npm package '{pkgName}' uses literal versions across multiple manifests ({string.Join(", ", owners)}); move it to 'catalog:' in pnpm-workspace.yaml.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateSinglePackageManifest(
        RepoSnapshot snapshot,
        string relativePath,
        Dictionary<string, string>? catalog,
        Dictionary<string, List<string>> literalOwnersByPackage)
    {
        if (!snapshot.TryReadText(relativePath, out string json, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        try
        {
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

                errors += ValidatePackageDependencySection(relativePath, section, depsObj, catalog, literalOwnersByPackage);
            }

            return errors;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse {relativePath}: {ex.Message}");
            return 1;
        }
    }

    private static int ValidatePackageDependencySection(
        string relativePath,
        string section,
        JsonElement depsObj,
        Dictionary<string, string>? catalog,
        Dictionary<string, List<string>> literalOwnersByPackage)
    {
        int errors = 0;

        foreach (var entry in depsObj.EnumerateObject())
        {
            string? spec = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(spec))
            {
                Console.WriteLine($"[ERROR] {relativePath} dependency '{entry.Name}' in '{section}' must specify a non-empty version string.");
                errors++;
                continue;
            }

            if (catalog is not null && catalog.ContainsKey(entry.Name) && !string.Equals(spec, "catalog:", StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} ({section}) must reference '{entry.Name}' via \"catalog:\" instead of literal '{spec}'.");
                errors++;
            }
            else if (catalog is not null &&
                     !string.Equals(spec, "catalog:", StringComparison.Ordinal) &&
                     !spec.StartsWith("workspace:", StringComparison.Ordinal))
            {
                if (!literalOwnersByPackage.TryGetValue(entry.Name, out var owners))
                {
                    owners = [];
                    literalOwnersByPackage[entry.Name] = owners;
                }

                if (!owners.Contains(relativePath, StringComparer.Ordinal))
                {
                    owners.Add(relativePath);
                }
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

        return errors;
    }
}
