using System;
using System.IO;
using System.Linq;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cadence.Testing.Hosting;

/// <summary>
/// Host settings for tests that boot a real service <c>Program</c> through <c>WebApplicationFactory</c>.
/// Production code has no fallback connection string, so a test host must supply one explicitly.
/// </summary>
public static class TestHostSettings
{
    /// <summary>
    /// A syntactically valid, credential-free PostgreSQL connection string. It is never opened: the test
    /// factories replace the DbContext provider (InMemory, SQLite or a Testcontainers instance).
    /// </summary>
    public const string PostgresConnectionString = "Host=localhost;Port=5432;Database=cadence_test;Username=cadence_test";

    /// <summary>Supplies <see cref="PostgresConnectionString"/> as <c>POSTGRES_CONNECTION_STRING</c> so startup validation passes.</summary>
    public static IWebHostBuilder UseTestPostgresConnectionString(this IWebHostBuilder builder) =>
        builder.UseSetting("POSTGRES_CONNECTION_STRING", PostgresConnectionString);

    /// <summary>Supplies an isolated filesystem <c>Storage</c> configuration so <c>AddCadenceStorage</c> startup validation passes in every environment.</summary>
    public static IWebHostBuilder UseTestStorage(this IWebHostBuilder builder) =>
        builder
            .UseSetting("Storage:Provider", "filesystem")
            .UseSetting("Storage:RootPath", Path.Combine(Path.GetTempPath(), $"cadence-test-storage-{Guid.NewGuid():N}"));

    /// <summary>
    /// Removes background <see cref="IHostedService"/> pollers and startup migrators while keeping
    /// <see cref="SigTokenUseStoreStartupCheck"/> and any <paramref name="keepHostedServices"/> active so unit hosts do
    /// not retry <c>127.0.0.1:5432</c> in the background.
    /// </summary>
    public static IWebHostBuilder DisableBackgroundHostedServices(
        this IWebHostBuilder builder,
        params Type[] keepHostedServices) =>
        builder.ConfigureTestServices(services => services.RemoveBackgroundHostedServices(keepHostedServices));

    /// <summary>
    /// Removes registered <see cref="IHostedService"/> descriptors from <paramref name="services"/> except
    /// <see cref="SigTokenUseStoreStartupCheck"/> and any types in <paramref name="keepHostedServices"/>.
    /// </summary>
    public static IServiceCollection RemoveBackgroundHostedServices(
        this IServiceCollection services,
        params Type[] keepHostedServices)
    {
        foreach (var descriptor in services
            .Where(d => d.ServiceType == typeof(IHostedService) && !ShouldKeepHostedService(d, keepHostedServices))
            .ToList())
        {
            services.Remove(descriptor);
        }

        return services;
    }

    private static bool ShouldKeepHostedService(ServiceDescriptor descriptor, Type[] keepHostedServices)
    {
        var implementationType = descriptor.ImplementationType
            ?? descriptor.ImplementationInstance?.GetType()
            ?? descriptor.ImplementationFactory?.GetType().GenericTypeArguments.LastOrDefault();

        return implementationType is not null
            && (implementationType == typeof(SigTokenUseStoreStartupCheck)
                || keepHostedServices.Contains(implementationType));
    }
}
