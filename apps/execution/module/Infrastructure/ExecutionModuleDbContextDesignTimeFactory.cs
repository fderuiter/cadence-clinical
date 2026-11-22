using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="ExecutionModuleDbContext"/>.
/// Resolves <c>-- --connection "..."</c>, <c>ConnectionStrings__cadence</c>, or <c>POSTGRES_CONNECTION_STRING</c>,
/// falling back to a credential-free offline placeholder for model-only operations (ADR-2260, ADR-2498).
/// </summary>
internal sealed class ExecutionModuleDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ExecutionModuleDbContext>
{
    public ExecutionModuleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
            .UseNpgsql(
                ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionString(args),
                npgsql => npgsql.MigrationsHistoryTable(ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable, ExecutionModuleDbContext.Schema))
            .Options;
        return new ExecutionModuleDbContext(options);
    }
}
