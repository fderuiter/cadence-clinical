using System;
using System.Text.Json;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Cadence.UnitTests.Execution;

/// <summary>
/// Boots the real execution <c>Program.cs</c> with <see cref="ExecutionDbContext"/> moved onto a uniquely named
/// <c>Microsoft.EntityFrameworkCore.InMemory</c> database, so tests can reach the API without PostgreSQL.
/// <para>
/// Use it only for tests whose assertions never depend on what is stored or how it is queried: authentication
/// rejections that happen before any read, routing, OpenAPI, dependency-injection wiring and the golden-master
/// harness's own assert helpers. The in-memory provider has no transactions, advisory locks, complex-type queries,
/// schemas or timestamp rules, so it passes code that fails on PostgreSQL (the UTC timestamp bug fixed in #4946).
/// Anything that writes through the API and asserts the result runs in <c>Cadence.IntegrationTests.Execution</c> on
/// <c>ExecutionPostgresApiFactory</c> instead (#4990).
/// </para>
/// <para>
/// A dedicated in-memory internal service provider (<c>UseInternalServiceProvider</c>) is required, not just a
/// swapped <c>UseXxx(...)</c> call: EF Core refuses to build a context once two providers' services share one
/// container. Each instance has its own database, so a class fixture sees one cumulative database and two classes
/// never see each other's rows.
/// </para>
/// </summary>
public sealed class ExecutionApiFactory : WebApplicationFactory<Cadence.Apps.Execution.Program>
{
    /// <summary>The snake_case wire-convention options matching <c>Program.cs</c>'s own
    /// <c>ConfigureHttpJsonOptions</c> call, for tests that need to (de)serialize request/response bodies
    /// outside of <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/>'s own JSON helpers.</summary>
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _databaseName = $"golden-master-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestDemographicsKey();
        builder.UseTestPostgresConnectionString();

        builder.ConfigureServices(services =>
        {
            // See this type's own remarks: a completely separate, isolated InMemory service provider is EF
            // Core's documented escape hatch for coexisting with Program.cs's own ambient Npgsql provider
            // registration, which AddDbContext's provider-registration side effects make otherwise
            // unremovable via a mere DbContextOptions<ExecutionDbContext> descriptor swap.
            var inMemoryServices = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.RemoveAll<DbContextOptions<ExecutionDbContext>>();
            // AddDbContext composes every registered configuration action (EF Core 9+), so Program.cs's own
            // (Npgsql plus its GxPAuditInterceptor) must go too, or each save runs two audit interceptors (#4922).
            services.RemoveAll<IDbContextOptionsConfiguration<ExecutionDbContext>>();
            services.AddDbContext<ExecutionDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.UseInternalServiceProvider(inMemoryServices);
                options.AddInterceptors(
                    new ConcurrencyTokenIncrementInterceptor(),
                    new GxPAuditInterceptor(() => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId));
            });

            // MassTransit's EF outbox lock-statement provider is configured for Postgres syntax
            // (Program.cs's own o.UsePostgres() call, unmodified here) and is therefore expected to fail its
            // background polling query against this InMemory-backed context -- harmless, expected noise this
            // suppresses purely to keep golden-master test output readable, matching
            // ExecutionProgramStartupTests' own precedent exactly.
            services.AddLogging(logging =>
            {
                logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                logging.AddFilter("MassTransit", LogLevel.None);
            });
        });

        base.ConfigureWebHost(builder);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        ExecutionJsonSerialization.Configure(options);
        return options;
    }
}
