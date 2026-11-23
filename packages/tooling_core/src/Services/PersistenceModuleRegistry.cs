using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Static metadata for a persistence module managed by <c>cadence db</c> and schema documentation tooling.
/// Uses compile-time string literals with zero runtime reflection over <c>DbContext</c> types so <c>Cadence.Cli</c>
/// compiles cleanly with <c>PublishAot=true</c> (ADR-2496, ADR-2498).
/// </summary>
/// <param name="Name">CLI module identifier (for example <c>kernel</c>, <c>execution-module</c>, <c>execution</c>, <c>ctms</c>).</param>
/// <param name="AssemblyName">Compiled .NET assembly name containing the module's <c>IDesignTimeDbContextFactory</c>.</param>
/// <param name="DbContextName">Exact <c>DbContext</c> type name passed via <c>--context</c> to <c>dotnet-ef</c>.</param>
/// <param name="ProjectPath">Repository-relative path to the module's <c>.csproj</c> file.</param>
/// <param name="Schema">Dedicated PostgreSQL schema owned by the module (ADR-2447, ADR-2601).</param>
/// <param name="MigrationsDir">Project-relative path to the EF Core migrations directory.</param>
/// <param name="CompiledModelsNamespace">Target C# namespace for <c>dotnet ef dbcontext optimize</c> output.</param>
/// <param name="SupportsSeed">Whether the module project exposes a standalone <c>--seed</c> CLI entrypoint.</param>
public sealed record PersistenceModuleDescriptor(
    string Name,
    string AssemblyName,
    string DbContextName,
    string ProjectPath,
    string Schema,
    string MigrationsDir,
    string CompiledModelsNamespace,
    bool SupportsSeed = false);

/// <summary>
/// Native AOT-safe registry of all 18 persistence modules in the Cadence repository.
/// Ordered with <c>kernel</c> first so <c>btree_gist</c> and shared kernel schema objects exist before module and
/// service migrations run.
/// </summary>
public static class PersistenceModuleRegistry
{
    /// <summary>
    /// All 18 persistence modules in canonical migration order (<c>kernel</c> first, followed by composition-host modules
    /// and standalone clinical services).
    /// </summary>
    public static IReadOnlyList<PersistenceModuleDescriptor> All { get; } =
    [
        new(
            "kernel",
            "Cadence.Kernel.Persistence",
            "KernelDbContext",
            "packages/kernel/src/Cadence.Kernel.Persistence/Cadence.Kernel.Persistence.csproj",
            "kernel",
            "Migrations",
            "Cadence.Kernel.Persistence.CompiledModels"),
        new(
            "intelligence",
            "Cadence.Modules.Intelligence",
            "IntelligenceDbContext",
            "apps/intelligence/src/Cadence.Modules.Intelligence.csproj",
            "intelligence",
            "Infrastructure/Persistence/Migrations",
            "Cadence.Modules.Intelligence.Infrastructure.Persistence.CompiledModels"),
        new(
            "execution-module",
            "Cadence.Modules.Execution",
            "ExecutionModuleDbContext",
            "apps/execution/module/Cadence.Modules.Execution.csproj",
            "execution",
            "Infrastructure/Migrations",
            "Cadence.Modules.Execution.Infrastructure.CompiledModels"),
        new(
            "execution",
            "Cadence.Apps.Execution",
            "ExecutionDbContext",
            "apps/execution/src/Cadence.Apps.Execution.csproj",
            "edc",
            "Infrastructure/Persistence/Migrations",
            "Cadence.Apps.Execution.Infrastructure.Persistence.CompiledModels",
            SupportsSeed: true),
        CreateStandaloneService("ctms", "Ctms"),
        CreateStandaloneService("econsent", "Econsent"),
        CreateStandaloneService("eisf", "Eisf"),
        CreateStandaloneService("etmf", "Etmf"),
        CreateStandaloneService("fileshare", "Fileshare"),
        CreateStandaloneService("imaging", "Imaging"),
        CreateStandaloneService("interop", "Interop"),
        CreateStandaloneService("knowledge", "Knowledge"),
        CreateStandaloneService("notifications", "Notifications"),
        CreateStandaloneService("org", "Org"),
        CreateStandaloneService("payments", "Payments"),
        CreateStandaloneService("quality", "Quality"),
        CreateStandaloneService("safety", "Safety"),
        CreateStandaloneService("tickets", "Tickets"),
    ];

    /// <summary>All 18 dedicated PostgreSQL schema names in canonical order.</summary>
    public static IReadOnlyList<string> AllSchemas { get; } = All.Select(static m => m.Schema).ToArray();

    /// <summary>All 18 CLI module names in canonical order.</summary>
    public static IReadOnlyList<string> AllNames { get; } = All.Select(static m => m.Name).ToArray();

    /// <summary>Looks up a single persistence module by its CLI name or alias (case-insensitive).</summary>
    public static bool TryGet(string name, [NotNullWhen(true)] out PersistenceModuleDescriptor? module)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            module = null;
            return false;
        }

        string normalized = name.Trim();
        if (string.Equals(normalized, "executionmodule", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "execution_module", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "modules/execution", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "execution-module";
        }
        else if (string.Equals(normalized, "intelligence-module", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(normalized, "intelligence_module", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(normalized, "modules/intelligence", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "intelligence";
        }

        module = All.FirstOrDefault(candidate => string.Equals(candidate.Name, normalized, StringComparison.OrdinalIgnoreCase));
        return module is not null;
    }

    /// <summary>
    /// Resolves <c>all</c> (all 18 modules with <c>kernel</c> first) or a single module name.
    /// </summary>
    public static bool TryResolveTargets(
        string? target,
        out IReadOnlyList<PersistenceModuleDescriptor> modules,
        [NotNullWhen(false)] out string? error)
    {
        string normalized = string.IsNullOrWhiteSpace(target) ? "all" : target.Trim();
        if (string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase))
        {
            modules = All;
            error = null;
            return true;
        }

        if (TryGet(normalized, out var single))
        {
            modules = [single];
            error = null;
            return true;
        }

        modules = [];
        error = $"Unknown persistence module '{target}'. Expected 'all' or one of: {string.Join(", ", AllNames)}.";
        return false;
    }

    private static PersistenceModuleDescriptor CreateStandaloneService(string name, string pascalName) =>
        new(
            name,
            $"Cadence.Apps.{pascalName}",
            $"{pascalName}DbContext",
            $"apps/{name}/src/Cadence.Apps.{pascalName}.csproj",
            name,
            "Infrastructure/Persistence/Migrations",
            $"Cadence.Apps.{pascalName}.Infrastructure.Persistence.CompiledModels");
}
