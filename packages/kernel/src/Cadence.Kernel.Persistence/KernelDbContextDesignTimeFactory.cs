using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="KernelDbContext"/>.
/// The connection string comes from <c>-- --connection "..."</c> or <c>POSTGRES_CONNECTION_STRING</c>; with
/// neither set it throws instead of falling back to a hardcoded credential (ADR-2260). The kernel does not
/// reference <c>Cadence.Database</c>, so this mirrors <c>CadenceDatabaseExtensions.ResolveDesignTimeConnectionString</c>.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class KernelDbContextDesignTimeFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    private const string ConnectionArgument = "--connection";
    private const string ConnectionStringVariable = "POSTGRES_CONNECTION_STRING";

    public KernelDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(ResolveConnectionString(args), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", KernelDbContext.Schema))
            .Options;

        return new KernelDbContext(options);
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
