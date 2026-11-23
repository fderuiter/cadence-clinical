using System;
using System.IO;

using Microsoft.AspNetCore.Hosting;

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
}
