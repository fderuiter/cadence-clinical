using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Generates <c>docs/api/*.md</c> from the committed <c>docs/openapi/*_openapi.json</c> specs:
/// one page per service listing its operations, and an index.
/// </summary>
public static class ApiDocsGeneratorService
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public static int GenerateApiDocs(bool validate = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string specDir = Path.Combine(repoRoot, "docs", "openapi");
        string docsDir = Path.Combine(repoRoot, "docs", "api");

        var expected = RenderAll(specDir);
        var drift = new List<string>();
        foreach (var (name, content) in expected)
        {
            string path = Path.Combine(docsDir, name);
            if (!File.Exists(path))
            {
                drift.Add($"{name} (missing)");
            }
            else if (File.ReadAllText(path).ReplaceLineEndings("\n") != content)
            {
                drift.Add($"{name} (out of date)");
            }
        }

        var stale = Directory.Exists(docsDir)
            ? Directory.GetFiles(docsDir, "*.md").Select(f => Path.GetFileName(f)).Where(n => !expected.ContainsKey(n)).OrderBy(n => n, StringComparer.Ordinal).ToList()
            : [];
        drift.AddRange(stale.Select(n => $"{n} (no spec in docs/openapi)"));

        if (drift.Count == 0)
        {
            Console.WriteLine("✔ docs/api matches docs/openapi.");
            return 0;
        }

        if (validate)
        {
            Console.WriteLine("[ERROR] docs/api differs from docs/openapi:");
            foreach (string line in drift)
            {
                Console.WriteLine($"  - {line}");
            }
            Console.WriteLine("Run 'pnpm cadence generate-api-docs' and commit docs/api.");
            return 1;
        }

        Directory.CreateDirectory(docsDir);
        foreach (var (name, content) in expected)
        {
            File.WriteAllText(Path.Combine(docsDir, name), content);
        }
        foreach (string name in stale)
        {
            File.Delete(Path.Combine(docsDir, name));
        }
        Console.WriteLine($"✔ docs/api regenerated ({drift.Count} file(s) changed).");
        return 0;
    }

    /// <summary>Renders every page keyed by file name. The index is always included.</summary>
    public static SortedDictionary<string, string> RenderAll(string specDir)
    {
        var pages = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var services = new List<(string Name, int Operations)>();

        var specs = Directory.Exists(specDir)
            ? Directory.GetFiles(specDir, "*_openapi.json").OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();
        foreach (string specPath in specs)
        {
            string name = Path.GetFileName(specPath)[..^"_openapi.json".Length];
            if (name == "cdisc")
            {
                continue;
            }

            var spec = JsonNode.Parse(File.ReadAllText(specPath)) as JsonObject ?? new JsonObject();
            var operations = Operations(spec);
            services.Add((name, operations.Count));
            pages[$"{name}.md"] = RenderService(name, operations);
        }

        pages["index.md"] = RenderIndex(services);
        return pages;
    }

    private static List<(string Method, string Path, string OperationId, string Summary)> Operations(JsonObject spec)
    {
        var result = new List<(string, string, string, string)>();
        if (spec["paths"] is not JsonObject paths)
        {
            return result;
        }

        foreach (var (path, item) in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (item is not JsonObject ops)
            {
                continue;
            }

            foreach (string method in HttpMethods)
            {
                if (ops[method] is JsonObject op)
                {
                    result.Add((
                        method.ToUpperInvariant(),
                        path,
                        op["operationId"]?.GetValue<string>() ?? "",
                        op["summary"]?.GetValue<string>() ?? ""));
                }
            }
        }

        return result;
    }

    private static string RenderService(string name, List<(string Method, string Path, string OperationId, string Summary)> operations)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"# {name} API\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"Generated from `docs/openapi/{name}_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.\n\n");
        if (operations.Count == 0)
        {
            sb.Append("The service exposes no operations.\n");
            return sb.ToString();
        }

        sb.Append("| Method | Path | Operation | Summary |\n");
        sb.Append("| ------ | ---- | --------- | ------- |\n");
        foreach (var op in operations)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| `{op.Method}` | `{op.Path}` | {Escape(op.OperationId)} | {Escape(op.Summary)} |\n");
        }
        return sb.ToString();
    }

    private static string RenderIndex(List<(string Name, int Operations)> services)
    {
        var sb = new StringBuilder();
        sb.Append("# API Reference\n\n");
        sb.Append("Generated from `docs/openapi/*_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.\n\n");
        sb.Append("Each page lists the operations a .NET service serves. The specs come from the services themselves (`pnpm cadence sync-openapi-spec`), and the `openapi` quality gate fails when they drift. `docs/openapi/cdisc_openapi.json` aggregates them. The Python-era contract the web client is still typed against is in `docs/openapi/legacy/`.\n\n");
        sb.Append("| Service | Operations |\n");
        sb.Append("| ------- | ---------- |\n");
        foreach (var svc in services)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| [{svc.Name}]({svc.Name}.md) | {svc.Operations} |\n");
        }
        return sb.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ").Trim();

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
