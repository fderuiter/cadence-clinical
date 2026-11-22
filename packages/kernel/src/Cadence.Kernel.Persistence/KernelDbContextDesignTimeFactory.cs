using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="KernelDbContext"/>.
/// Resolves <c>-- --connection "..."</c>, <c>ConnectionStrings__cadence</c>, or <c>POSTGRES_CONNECTION_STRING</c>,
/// falling back to a credential-free placeholder for offline model-only commands (ADR-2260, ADR-2498). The kernel does
/// not reference <c>Cadence.Database</c>, so this mirrors <c>CadenceDatabaseExtensions.ResolveDesignTimeConnectionString</c>.
/// </summary>
internal sealed class KernelDbContextDesignTimeFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    internal const string PlaceholderConnectionString = "Host=localhost;Database=cadence_design";

    public KernelDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>();
        KernelDatabase.UseKernelNpgsql(options, ResolveConnectionString(args));
        return new KernelDbContext(options.Options);
    }

    internal static string ResolveConnectionString(string[]? args = null, Func<string, string?>? getEnvironmentVariable = null)
    {
        int index = args is null ? -1 : Array.IndexOf(args, "--connection");
        if (args is not null && index >= 0 && index + 1 < args.Length && args[index + 1] is { Length: > 0 } explicitConn)
        {
            return explicitConn;
        }

        string? primary = getEnvironmentVariable is not null
            ? getEnvironmentVariable("ConnectionStrings__cadence")
            : Environment.GetEnvironmentVariable("ConnectionStrings__cadence");
        if (!string.IsNullOrEmpty(primary))
        {
            return primary;
        }

        string? fallback = getEnvironmentVariable is not null
            ? getEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            : Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        return !string.IsNullOrEmpty(fallback) ? fallback : PlaceholderConnectionString;
    }
}
