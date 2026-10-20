using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="ExecutionModuleDbContext"/>. The
/// connection string comes from <c>-- --connection "..."</c> or <c>POSTGRES_CONNECTION_STRING</c>; with neither set it
/// throws instead of falling back to a hardcoded credential (ADR-2260).
/// </summary>
internal sealed class ExecutionModuleDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ExecutionModuleDbContext>
{
    private const string ConnectionArgument = "--connection";
    private const string ConnectionStringVariable = "POSTGRES_CONNECTION_STRING";

    public ExecutionModuleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
            .UseNpgsql(
                ResolveConnectionString(args),
                npgsql => npgsql.MigrationsHistoryTable(ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable, ExecutionModuleDbContext.Schema))
            .Options;
        return new ExecutionModuleDbContext(options);
    }

    private static string ResolveConnectionString(string[]? args)
    {
        if (args is not null)
        {
            int index = Array.IndexOf(args, ConnectionArgument);
            if (index >= 0 && index + 1 < args.Length && args[index + 1] is { Length: > 0 } fromArgs)
            {
                return fromArgs;
            }
        }

        return Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException(
                $"{ConnectionStringVariable} is not set. Export it, or pass {ConnectionArgument} \"...\" after -- on the dotnet ef command line.");
    }
}
