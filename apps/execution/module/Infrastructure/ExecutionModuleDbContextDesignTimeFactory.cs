using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="ExecutionModuleDbContext"/>. The
/// connection string comes from <c>-- --connection "..."</c>, <c>ConnectionStrings__cadence</c>, or
/// <c>POSTGRES_CONNECTION_STRING</c>; when all three are omitted it throws instead of falling back to a hardcoded
/// credential (ADR-2260).
/// </summary>
internal sealed class ExecutionModuleDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ExecutionModuleDbContext>
{
    public ExecutionModuleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
            .UseModuleDesignTimeMappings()
            .UseNpgsql(
                ResolveConnectionString(args),
                npgsql => npgsql.MigrationsHistoryTable(ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable, ExecutionModuleDbContext.Schema))
            .Options;
        return new ExecutionModuleDbContext(options);
    }

    internal static string ResolveConnectionString(string[]? args, Func<string, string?>? getEnvironmentVariable = null) =>
        ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionString(
            args,
            getEnvironmentVariable ?? Environment.GetEnvironmentVariable);
}
