using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="KernelDbContext"/>.
/// The connection string comes from <c>-- --connection "..."</c>, <c>ConnectionStrings__cadence</c>, or
/// <c>POSTGRES_CONNECTION_STRING</c>; when all three are omitted it throws instead of falling back to a
/// hardcoded credential (ADR-2260).
/// </summary>
internal sealed class KernelDbContextDesignTimeFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    public KernelDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseModuleDesignTimeMappings();
        KernelDatabase.UseKernelNpgsql(options, ResolveConnectionString(args));
        return new KernelDbContext(options.Options);
    }

    internal static string ResolveConnectionString(string[]? args, Func<string, string?>? getEnvironmentVariable = null) =>
        ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionString(
            args,
            getEnvironmentVariable ?? Environment.GetEnvironmentVariable);
}
