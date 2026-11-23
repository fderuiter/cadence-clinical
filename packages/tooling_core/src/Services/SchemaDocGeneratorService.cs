using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Keeps <c>docs/schema/*.md</c> equal to the database schema the EF Core models define, while verifying that all 18
/// persistence modules have zero pending model changes, valid migration attributes and <c>.Designer.cs</c> files, and
/// unique DDL creations. Rendered by <c>Cadence.SchemaDocExporter</c> without opening a database connection.
/// </summary>
public static class SchemaDocGeneratorService
{
    private const string ExporterProject = "packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj";

    /// <summary>
    /// Regenerates <c>docs/schema</c>, or with <paramref name="validate"/> only reports how it differs from the models
    /// and returns 1 when it does.
    /// </summary>
    public static int GenerateSchemaDocumentation(
        bool validate = false,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        string docsDir = Path.Combine(repoRoot, "docs", "schema");

        GitIndexReader? ownedReader = null;
        GitIndexReader? reader = validate
            ? GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out ownedReader)
            : null;
        using (ownedReader)
        {
            if (validate && preCommit)
            {
                var overlappingUnstaged = GitIndexReader.GetOverlappingUnstagedProjectClosurePaths(
                    repoRoot,
                    [ExporterProject],
                    reader,
                    static p => p.StartsWith("docs/schema/", StringComparison.OrdinalIgnoreCase));
                if (overlappingUnstaged.Count > 0)
                {
                    string sample = string.Join(", ", overlappingUnstaged.Take(5));
                    Console.WriteLine($"[ERROR] Unstaged or untracked working-tree edits overlap MSBuild/schema-doc inputs ({sample}). Stage or stash them before committing so staged EF Core schema docs can be verified fail-closed.");
                    return 1;
                }
            }

            string exportDir = Path.Combine(Path.GetTempPath(), "cadence-schema-doc-" + Guid.NewGuid().ToString("N"));

            Console.WriteLine("Rendering the database schema documentation from the EF Core models...");
            try
            {
                if (!RunExporter(repoRoot, exportDir))
                {
                    return 1;
                }

                IReadOnlyList<string> drift;
                if (validate && reader is { HasAuthoritativeIndex: true })
                {
                    using var stagedDocsWorkspace = reader.Materialize(
                        reader.Paths.Where(p => p.StartsWith("docs/schema/", StringComparison.OrdinalIgnoreCase)));
                    drift = FindDrift(exportDir, Path.Combine(stagedDocsWorkspace.RootPath, "docs", "schema"));
                }
                else
                {
                    drift = FindDrift(exportDir, docsDir);
                }

                if (drift.Count == 0)
                {
                    Console.WriteLine("✔ docs/schema matches the EF Core models.");
                    return 0;
                }

                if (validate)
                {
                    Console.WriteLine("[ERROR] docs/schema differs from the EF Core models:");
                    foreach (string line in drift)
                    {
                        Console.WriteLine($"  - {line}");
                    }

                    Console.WriteLine("Run 'pnpm cadence generate-schema-doc' and commit docs/schema.");
                    return 1;
                }

                Directory.CreateDirectory(docsDir);
                foreach (string file in Directory.GetFiles(exportDir, "*.md"))
                {
                    File.Copy(file, Path.Combine(docsDir, Path.GetFileName(file)), overwrite: true);
                }

                foreach (string stale in Stale(exportDir, docsDir))
                {
                    File.Delete(Path.Combine(docsDir, stale));
                }

                foreach (string line in drift)
                {
                    Console.WriteLine($"  updated {line}");
                }

                Console.WriteLine("✔ docs/schema regenerated.");
                return 0;
            }
            finally
            {
                if (Directory.Exists(exportDir))
                {
                    Directory.Delete(exportDir, recursive: true);
                }
            }
        }
    }

    /// <summary>
    /// Compares freshly rendered documents with the committed ones, ignoring line-ending differences.
    /// </summary>
    public static IReadOnlyList<string> FindDrift(string renderedDir, string committedDir)
    {
        var drift = new List<string>();
        foreach (string rendered in Directory.GetFiles(renderedDir, "*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(rendered);
            string committed = Path.Combine(committedDir, name);
            if (!File.Exists(committed))
            {
                drift.Add($"{name} (missing)");
            }
            else if (Normalize(File.ReadAllText(rendered)) != Normalize(File.ReadAllText(committed)))
            {
                drift.Add($"{name} (out of date)");
            }
        }

        drift.AddRange(Stale(renderedDir, committedDir).Select(name => $"{name} (no module defines it)"));
        return drift;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static IEnumerable<string> Stale(string renderedDir, string committedDir)
    {
        if (!Directory.Exists(committedDir))
        {
            return [];
        }

        var rendered = Directory.GetFiles(renderedDir, "*.md").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        return Directory.GetFiles(committedDir)
            .Select(f => Path.GetFileName(f))
            .Where(name => !rendered.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool RunExporter(string repoRoot, string exportDir) =>
        ExporterBuildFreshness.RunDotnetExporter(
            repoRoot,
            ExporterProject,
            ["--output-dir", exportDir],
            "schema documentation exporter");

    private static string FindRepoRoot() => RepositoryRoot.Find();
}
