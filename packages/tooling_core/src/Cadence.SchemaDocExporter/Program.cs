using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Cadence.Database;
using Cadence.ToolingCore.Primitives;
using Cadence.ToolingCore.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.SchemaDocExporter;

/// <summary>
/// Validates migration snapshots, <c>.Designer.cs</c> files, attributes, and DDL uniqueness across all 18 persistence
/// modules in <see cref="PersistenceModuleCatalog"/>, and writes one Markdown file per module into <c>docs/schema</c>
/// plus a static <c>README.md</c> index. No database connection is opened.
/// </summary>
public static class Program
{
    public const string GeneratorCommand = "pnpm cadence generate-schema-doc";

    // Design-time factories have no fallback connection string (ADR-2260). Building the model never opens a
    // connection, so a credential-free placeholder is enough.
    private static readonly string[] DesignTimeArguments = ["--connection", "Host=localhost;Database=cadence_schema_export"];

    public static int Main(string[] args)
    {
        string repoRoot = WorkspacePrimitives.FindRepoRoot();
        bool verifyOnly = args.Any(a => string.Equals(a, "--verify-only", StringComparison.Ordinal));
        string? moduleFilter = ReadOptionValue(args, "--module", "-m");
        string outputDir = ReadOptionValue(args, "--output-dir", "-o")
            ?? Path.Combine(repoRoot, "docs", "schema");

        if (moduleFilter is not null && !verifyOnly)
        {
            Console.Error.WriteLine("[ERROR] '--module' may only be used together with '--verify-only'.");
            return 1;
        }

        if (!PersistenceModuleCatalog.TryResolveTargets(moduleFilter, allowAll: true, out var targetCatalog, out string catalogError))
        {
            Console.Error.WriteLine($"[ERROR] {catalogError}");
            return 1;
        }

        try
        {
            var validationErrors = new List<string>();
            var modules = new List<ModuleDocument>(targetCatalog.Count);

            foreach (PersistenceModule entry in targetCatalog.OrderBy(m => m.Module, StringComparer.Ordinal))
            {
                var factories = DesignTimeFactories(Assembly.Load(entry.AssemblyName)).ToList();
                if (factories.Count != 1)
                {
                    validationErrors.Add(
                        $"[{entry.Module}] Assembly '{entry.AssemblyName}' must expose exactly 1 IDesignTimeDbContextFactory<>, found {factories.Count}.");
                    continue;
                }

                var doc = InspectAndRender(entry, factories[0], repoRoot, verifyOnly, validationErrors);
                modules.Add(doc);
            }

            if (validationErrors.Count > 0)
            {
                Console.Error.WriteLine($"[ERROR] Persistence migration and model verification failed ({validationErrors.Count} issue(s)):");
                foreach (string err in validationErrors)
                {
                    Console.Error.WriteLine($"  - {err}");
                }

                return 1;
            }

            if (verifyOnly)
            {
                Console.WriteLine($"✔ Verified {modules.Count} persistence module(s): zero pending model changes, valid migration metadata, and unique DDL creations.");
                return 0;
            }

            var duplicate = modules.GroupBy(m => m.Module, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                Console.Error.WriteLine($"[ERROR] More than one design-time factory produces module '{duplicate.Key}'.");
                return 1;
            }

            Directory.CreateDirectory(outputDir);
            foreach (var module in modules)
            {
                File.WriteAllText(Path.Combine(outputDir, module.Module + ".md"), module.Markdown);
            }

            File.WriteAllText(Path.Combine(outputDir, "README.md"), Index(modules));

            // Files for a module that no longer exists would otherwise linger as stale documentation.
            var expected = modules.Select(m => m.Module + ".md").Append("README.md").ToHashSet(StringComparer.Ordinal);
            foreach (string stale in Directory.GetFiles(outputDir, "*.md").Where(f => !expected.Contains(Path.GetFileName(f))))
            {
                File.Delete(stale);
            }

            Console.WriteLine($"Wrote {modules.Count} module schema documents to {outputDir}.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Schema documentation export failed: {ex.GetBaseException().Message}");
            return 1;
        }
    }

    /// <summary>Renders the static <c>docs/schema/README.md</c> module index without volatile table counts.</summary>
    public static string Index(IReadOnlyList<ModuleDocument> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var sb = new StringBuilder();
        sb.Append("# Database schema\n\n");
        sb.Append("<!-- Generated by `").Append(GeneratorCommand).Append("`. Do not edit by hand. -->\n\n");
        sb.Append("One document per module, rendered from each module's EF Core design-time model. All modules share the ");
        sb.Append("`cadence` database, each in its own schema ([ADR-2447](../adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)). ");
        sb.Append("`pnpm cadence check schema-doc` fails when these files differ from the models, so a change to an entity or ");
        sb.Append("its configuration is committed together with its documentation.\n\n");
        sb.Append("| Module | DbContext |\n| --- | --- |\n");
        foreach (var module in modules)
        {
            sb.Append("| [").Append(module.Module).Append("](").Append(module.Module).Append(".md) | `")
                .Append(module.ContextName).Append("` |\n");
        }

        return sb.ToString();
    }

    private static string? ReadOptionValue(string[] args, string longFlag, string shortFlag)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, longFlag, StringComparison.Ordinal) || string.Equals(a, shortFlag, StringComparison.Ordinal));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IEnumerable<Type> DesignTimeFactories(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(IsFactoryInterface))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static bool IsFactoryInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>);

    private static ModuleDocument InspectAndRender(
        PersistenceModule catalogEntry,
        Type factoryType,
        string repoRoot,
        bool verifyOnly,
        List<string> validationErrors)
    {
        object factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
        var create = factoryType.GetInterfaces().Single(IsFactoryInterface).GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;
        using var context = (DbContext)create.Invoke(factory, [DesignTimeArguments])!;

        string contextName = context.GetType().Name;
        string derivedModule = (contextName.EndsWith("DbContext", StringComparison.Ordinal) ? contextName[..^"DbContext".Length] : contextName)
            .ToLowerInvariant();
        if (!string.Equals(derivedModule, catalogEntry.Module, StringComparison.Ordinal) ||
            !string.Equals(contextName, catalogEntry.DbContextName, StringComparison.Ordinal))
        {
            validationErrors.Add(
                $"[{catalogEntry.Module}] Catalog expects '{catalogEntry.DbContextName}' (module '{catalogEntry.Module}'), but factory '{factoryType.FullName}' created '{contextName}'.");
        }

        string migrationsDir = catalogEntry.GetMigrationsDirectory(repoRoot);
        var moduleErrors = SchemaMigrationValidator.Validate(
            context,
            catalogEntry.Module,
            migrationsDir,
            catalogEntry.Schema);
        validationErrors.AddRange(moduleErrors);

        string markdown = verifyOnly
            ? string.Empty
            : SchemaDocumentation.Render(context, $"`{catalogEntry.Module}` database schema", GeneratorCommand);
        return new ModuleDocument(catalogEntry.Module, contextName, markdown);
    }

    /// <summary>Rendered documentation metadata for one persistence module.</summary>
    public sealed record ModuleDocument(string Module, string ContextName, string Markdown);
}
