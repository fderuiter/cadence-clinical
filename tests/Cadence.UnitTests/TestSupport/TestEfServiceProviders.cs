using System;
using System.Linq;

using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cadence.UnitTests.TestSupport;

/// <summary>
/// Shared internal EF Core <see cref="IServiceProvider"/> instances and test-host helpers for unit tests that swap
/// service <see cref="DbContext"/> registrations onto SQLite or EF Core InMemory without fragmenting EF Core's
/// global <c>ServiceProviderCache</c> across 18 persistence modules.
/// </summary>
internal static class TestEfServiceProviders
{
    public static IServiceProvider InMemory { get; } = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    public static IServiceProvider Sqlite { get; } = new ServiceCollection()
        .AddEntityFrameworkSqlite()
        .BuildServiceProvider();

    public static void RemoveBackgroundHostedServices(
        this IServiceCollection services,
        params Type[] preserveImplementationTypes)
    {
        foreach (var descriptor in services
            .Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType != typeof(SigTokenUseStoreStartupCheck)
                && !string.Equals(d.ImplementationType?.Name, "InferenceStartupCheck", StringComparison.Ordinal)
                && (d.ImplementationType is null || !preserveImplementationTypes.Contains(d.ImplementationType)))
            .ToList())
        {
            services.Remove(descriptor);
        }
    }

    public static void ReplaceWithInMemoryDbContext<TDbContext>(
        this IServiceCollection services,
        string databaseName,
        bool useSharedInternalProvider = true)
        where TDbContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TDbContext>>();
        services.AddDbContext<TDbContext>((sp, options) =>
        {
            options.UseInMemoryDatabase(databaseName);
            if (useSharedInternalProvider)
            {
                options.UseInternalServiceProvider(InMemory);
            }

            options.UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance);
        });
    }
}
