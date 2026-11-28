using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class CodeMetadataCatalog
{
    [GeneratedRegex(@"\bconst\s+DEV_PORT\s*=\s*(\d+)\s*;")]
    private static partial Regex ViteDevPortRegex();

    [GeneratedRegex(@"AddProject<[^>]+>\s*\(\s*""([^""]+)""\s*\)")]
    private static partial Regex AppHostAddProjectRegex();

    [GeneratedRegex(@"AddViteApp\s*\(\s*""([^""]+)""")]
    private static partial Regex AppHostAddViteAppRegex();

    [GeneratedRegex(@"AddContainer\s*\(\s*""keycloak""[\s\S]{0,200}?\.WithHttpEndpoint\s*\(\s*port:\s*(\d+)")]
    private static partial Regex AppHostKeycloakPortRegex();

    [GeneratedRegex(@"Generated from the EF Core model of `([^`]+)`")]
    private static partial Regex SchemaModelTypeRegex();

    [GeneratedRegex(@"Unqualified tables live in the `([^`]+)` schema\.")]
    private static partial Regex SchemaDefaultRegex();

    [GeneratedRegex(@"(?:^##\s+([a-z0-9_]+)\.[A-Za-z0-9_]+|^\|\s*\[`([a-z0-9_]+)\.[A-Za-z0-9_]+`\])", RegexOptions.Multiline)]
    private static partial Regex SchemaQualifiedTableRegex();

    [GeneratedRegex(@"HasDefaultSchema\s*\(\s*(?:""([^""]+)""|([A-Za-z0-9_]+DbContext)\.Schema)\s*\)")]
    private static partial Regex HasDefaultSchemaRegex();

    private static string ResolveHostFromRelativePath(string relativePath)
    {
        if (relativePath.StartsWith("apps/execution/module/", StringComparison.Ordinal) ||
            relativePath.StartsWith("apps/intelligence/", StringComparison.Ordinal) ||
            relativePath.StartsWith("apps/kernel-api/", StringComparison.Ordinal) ||
            relativePath.StartsWith("packages/kernel/", StringComparison.Ordinal))
        {
            return "kernel";
        }

        if (relativePath.StartsWith("apps/", StringComparison.Ordinal))
        {
            string rest = relativePath["apps/".Length..];
            int slash = rest.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                return rest[..slash];
            }
        }

        return relativePath;
    }

    private static string ResolveSchemaForDbContext(
        string repoRoot,
        string dbContext,
        string host,
        IReadOnlyList<DatabaseModuleSchema> dbModules)
    {
        var known = dbModules.FirstOrDefault(m => string.Equals(m.DbContext, dbContext, StringComparison.Ordinal));
        if (known is not null)
        {
            return known.Schema;
        }

        foreach (string file in EnumerateProductionCSharpFiles(repoRoot))
        {
            if (!Path.GetFileName(file).Equals(dbContext + ".cs", StringComparison.Ordinal))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            var m = HasDefaultSchemaRegex().Match(content);
            if (m.Success)
            {
                if (m.Groups[1].Success)
                {
                    return m.Groups[1].Value;
                }

                if (m.Groups[2].Success)
                {
                    var referenced = dbModules.FirstOrDefault(mod => string.Equals(mod.DbContext, m.Groups[2].Value, StringComparison.Ordinal));
                    if (referenced is not null)
                    {
                        return referenced.Schema;
                    }
                }
            }
        }

        return string.Equals(host, "execution", StringComparison.Ordinal) ? "edc" : host;
    }

    private static string ResolveSchemaFromModuleDoc(string fallbackSchema, string doc)
    {
        var defaultMatch = SchemaDefaultRegex().Match(doc);
        if (defaultMatch.Success && !string.Equals(defaultMatch.Groups[1].Value, "public", StringComparison.Ordinal))
        {
            return defaultMatch.Groups[1].Value;
        }

        var qualified = SchemaQualifiedTableRegex().Matches(doc)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .FirstOrDefault(s => !string.IsNullOrEmpty(s) && !string.Equals(s, "public", StringComparison.Ordinal));
        return qualified ?? fallbackSchema;
    }

    private static string ResolveHostFromModuleDoc(string fallbackHost, string doc)
    {
        var typeMatch = SchemaModelTypeRegex().Match(doc);
        if (typeMatch.Success)
        {
            string fullType = typeMatch.Groups[1].Value;
            if (fullType.StartsWith("Cadence.Kernel.", StringComparison.Ordinal) ||
                fullType.StartsWith("Cadence.Modules.", StringComparison.Ordinal))
            {
                return "kernel";
            }
        }

        return fallbackHost;
    }

    private static int ParseVitePort(string viteConfigPath, int defaultPort)
    {
        if (!File.Exists(viteConfigPath))
        {
            return defaultPort;
        }

        var m = ViteDevPortRegex().Match(File.ReadAllText(viteConfigPath));
        return m.Success && int.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out int port)
            ? port
            : defaultPort;
    }

    private static (List<string> CoreBackends, List<string> CoreWebApps) ParseAppHostCoreProfile(string appHostSource)
    {
        string[] lines = appHostSource.ReplaceLineEndings("\n").Split('\n');
        var coreProjects = new List<string>();
        var coreWeb = new List<string>();
        int fullBlockDepth = 0;
        bool pendingIfFull = false;

        foreach (string rawLine in lines)
        {
            string trimmed = rawLine.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (fullBlockDepth == 0 && Regex.IsMatch(trimmed, @"^if\s*\(\s*full\s*\)", RegexOptions.CultureInvariant))
            {
                pendingIfFull = true;
            }

            if (pendingIfFull || fullBlockDepth > 0)
            {
                int opens = trimmed.Count(c => c == '{');
                int closes = trimmed.Count(c => c == '}');
                if (pendingIfFull && opens > 0)
                {
                    fullBlockDepth += opens - closes;
                    pendingIfFull = false;
                }
                else if (!pendingIfFull)
                {
                    fullBlockDepth += opens - closes;
                    if (fullBlockDepth < 0)
                    {
                        fullBlockDepth = 0;
                    }
                }

                continue;
            }

            foreach (Match pm in AppHostAddProjectRegex().Matches(trimmed))
            {
                string name = pm.Groups[1].Value;
                if (!coreProjects.Contains(name, StringComparer.Ordinal))
                {
                    coreProjects.Add(name);
                }
            }

            foreach (Match wm in AppHostAddViteAppRegex().Matches(trimmed))
            {
                string name = wm.Groups[1].Value;
                if (!coreWeb.Contains(name, StringComparer.Ordinal))
                {
                    coreWeb.Add(name);
                }
            }
        }

        // Order core backends with gateway first when present, matching documentation order.
        if (coreProjects.Remove("gateway"))
        {
            coreProjects.Insert(0, "gateway");
        }

        return (coreProjects, coreWeb);
    }

    private static IEnumerable<string> EnumerateProductionCSharpFiles(string repoRoot)
    {
        foreach (string rootSub in new[] { "apps", "packages" })
        {
            string baseDir = Path.Combine(repoRoot, rootSub);
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(baseDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (rel.Contains("/bin/", StringComparison.Ordinal) ||
                    rel.Contains("/obj/", StringComparison.Ordinal) ||
                    rel.Contains("/Migrations/", StringComparison.Ordinal) ||
                    rel.Contains("/consumer-tests/", StringComparison.Ordinal) ||
                    rel.Contains("/benchmarks/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}
