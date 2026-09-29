using System;

using Cadence.Database;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Registers kernel persistence. Called by the kernel API host only; application modules must not call it (K-XS-2).
/// @req:PRD-SYS-053
/// </summary>
public static class KernelPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the kernel <see cref="DbContext"/> against PostgreSQL with the GxP audit interceptor, taking
    /// time from <see cref="IClock"/> (K-TIME-3).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The <c>cadence</c> database connection string.</param>
    /// <param name="currentActor">
    /// Resolves the acting user for audit fields. Interim: the command pipeline (#4779) replaces this with the
    /// resolved kernel Actor and rejects writes without one (K-TX-5).
    /// </param>
    public static IServiceCollection AddKernelPersistence(
        this IServiceCollection services,
        string connectionString,
        Func<IServiceProvider, string> currentActor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(currentActor);

        services.AddDbContext<KernelDbContext>((sp, options) =>
        {
            var clock = sp.GetService<IClock>() ?? SystemClock.Instance;
            options
                .UseNpgsql(connectionString, npgsql => npgsql
                    .MigrationsHistoryTable("__ef_migrations_history", KernelDbContext.Schema)
                    .EnableRetryOnFailure())
                .AddInterceptors(new GxPAuditInterceptor(() => currentActor(sp), () => clock.UtcNow.UtcDateTime));
        });

        return services;
    }
}
