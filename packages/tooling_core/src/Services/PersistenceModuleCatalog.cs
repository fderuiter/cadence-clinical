using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Metadata for one EF Core persistence module in the single PostgreSQL <c>cadence</c> database (ADR-2247, ADR-2447).
/// </summary>
public sealed record PersistenceModule(
    string Module,
    string Schema,
    string DbContextName,
    string AssemblyName,
    string ProjectRelativePath,
    string MigrationsRelativeDir,
    IReadOnlyList<string>? Aliases = null)
{
    /// <summary>Full path to the module's <c>.csproj</c> file under <paramref name="repoRoot"/>.</summary>
    public string GetProjectPath(string repoRoot) =>
        Path.Combine(repoRoot, ProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Full path to the module's EF Core <c>Migrations</c> directory under <paramref name="repoRoot"/>.</summary>
    public string GetMigrationsDirectory(string repoRoot)
    {
        string projectDir = Path.GetDirectoryName(GetProjectPath(repoRoot))!;
        return Path.Combine(projectDir, MigrationsRelativeDir.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Returns <c>true</c> when <paramref name="candidate"/> matches <see cref="Module"/> or an alias.</summary>
    public bool Matches(string candidate)
    {
        string normalized = NormalizeName(candidate);
        if (string.Equals(Module, normalized, StringComparison.Ordinal))
        {
            return true;
        }

        return Aliases is not null && Aliases.Any(a => string.Equals(NormalizeName(a), normalized, StringComparison.Ordinal));
    }

    internal static string NormalizeName(string value) =>
        value.Trim().ToLowerInvariant();
}

/// <summary>
/// Canonical catalog of all 17 EF Core persistence modules across the Clinical Kernel, host-mounted kernel modules,
/// and standalone clinical services. Shared by <c>cadence migrations</c> and <c>Cadence.SchemaDocExporter</c>.
/// </summary>
public static class PersistenceModuleCatalog
{
    /// <summary>All 17 persistence modules in canonical alphabetical order by <see cref="PersistenceModule.Module"/>.</summary>
    public static IReadOnlyList<PersistenceModule> All { get; } =
    [
        new("ctms", "ctms", "CtmsDbContext", "Cadence.Apps.Ctms", "apps/ctms/src/Cadence.Apps.Ctms.csproj", "Infrastructure/Persistence/Migrations"),
        new("econsent", "econsent", "EconsentDbContext", "Cadence.Apps.Econsent", "apps/econsent/src/Cadence.Apps.Econsent.csproj", "Infrastructure/Persistence/Migrations"),
        new("eisf", "eisf", "EisfDbContext", "Cadence.Apps.Eisf", "apps/eisf/src/Cadence.Apps.Eisf.csproj", "Infrastructure/Persistence/Migrations"),
        new("etmf", "etmf", "EtmfDbContext", "Cadence.Apps.Etmf", "apps/etmf/src/Cadence.Apps.Etmf.csproj", "Infrastructure/Persistence/Migrations"),
        new("execution", "edc", "ExecutionDbContext", "Cadence.Apps.Execution", "apps/execution/src/Cadence.Apps.Execution.csproj", "Infrastructure/Persistence/Migrations"),
        new("executionmodule", "execution", "ExecutionModuleDbContext", "Cadence.Modules.Execution", "apps/execution/module/Cadence.Modules.Execution.csproj", "Infrastructure/Migrations", ["execution-module", "execution_module"]),
        new("fileshare", "fileshare", "FileshareDbContext", "Cadence.Apps.Fileshare", "apps/fileshare/src/Cadence.Apps.Fileshare.csproj", "Infrastructure/Persistence/Migrations"),
        new("imaging", "imaging", "ImagingDbContext", "Cadence.Apps.Imaging", "apps/imaging/src/Cadence.Apps.Imaging.csproj", "Infrastructure/Persistence/Migrations"),
        new("intelligence", "intelligence", "IntelligenceDbContext", "Cadence.Modules.Intelligence", "apps/intelligence/src/Cadence.Modules.Intelligence.csproj", "Infrastructure/Persistence/Migrations"),
        new("interop", "interop", "InteropDbContext", "Cadence.Apps.Interop", "apps/interop/src/Cadence.Apps.Interop.csproj", "Infrastructure/Persistence/Migrations"),
        new("kernel", "kernel", "KernelDbContext", "Cadence.Kernel.Persistence", "packages/kernel/src/Cadence.Kernel.Persistence/Cadence.Kernel.Persistence.csproj", "Migrations"),
        new("knowledge", "knowledge", "KnowledgeDbContext", "Cadence.Apps.Knowledge", "apps/knowledge/src/Cadence.Apps.Knowledge.csproj", "Infrastructure/Persistence/Migrations"),
        new("notifications", "notifications", "NotificationsDbContext", "Cadence.Apps.Notifications", "apps/notifications/src/Cadence.Apps.Notifications.csproj", "Infrastructure/Persistence/Migrations"),
        new("payments", "payments", "PaymentsDbContext", "Cadence.Apps.Payments", "apps/payments/src/Cadence.Apps.Payments.csproj", "Infrastructure/Persistence/Migrations"),
        new("quality", "quality", "QualityDbContext", "Cadence.Apps.Quality", "apps/quality/src/Cadence.Apps.Quality.csproj", "Infrastructure/Persistence/Migrations"),
        new("safety", "safety", "SafetyDbContext", "Cadence.Apps.Safety", "apps/safety/src/Cadence.Apps.Safety.csproj", "Infrastructure/Persistence/Migrations"),
        new("tickets", "tickets", "TicketsDbContext", "Cadence.Apps.Tickets", "apps/tickets/src/Cadence.Apps.Tickets.csproj", "Infrastructure/Persistence/Migrations"),
    ];

    /// <summary>Comma-separated list of canonical module names for CLI help and error messages.</summary>
    public static string ModuleList => string.Join(", ", All.Select(m => m.Module));

    /// <summary>Finds a persistence module by canonical name or alias, or returns <c>null</c> when unknown.</summary>
    public static PersistenceModule? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return All.FirstOrDefault(m => m.Matches(name));
    }

    /// <summary>
    /// Resolves either <c>all</c> (when <paramref name="allowAll"/> is <c>true</c>) or a single persistence module.
    /// </summary>
    public static bool TryResolveTargets(
        string? moduleOrAll,
        bool allowAll,
        out IReadOnlyList<PersistenceModule> modules,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(moduleOrAll))
        {
            if (allowAll)
            {
                modules = All;
                error = string.Empty;
                return true;
            }

            modules = [];
            error = $"Module name is required. Known modules ({All.Count}): {ModuleList}.";
            return false;
        }

        if (string.Equals(moduleOrAll.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            if (allowAll)
            {
                modules = All;
                error = string.Empty;
                return true;
            }

            modules = [];
            error = $"This command targets a single module rather than 'all'. Known modules ({All.Count}): {ModuleList}.";
            return false;
        }

        var match = Find(moduleOrAll);
        if (match is null)
        {
            modules = [];
            error = $"Unknown persistence module '{moduleOrAll}'. Known modules ({All.Count}): {ModuleList}.";
            return false;
        }

        modules = [match];
        error = string.Empty;
        return true;
    }
}
