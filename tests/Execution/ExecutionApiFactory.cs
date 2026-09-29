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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Cadence.UnitTests.Execution;

/// <summary>
/// `MIG-SUBJ-025`'s formalization, for golden-master fixture-replay use, of the
/// <see cref="WebApplicationFactory{TEntryPoint}"/> override
/// <c>ExecutionProgramStartupTests.ExecutionProgramTestFactory</c> (`MIG-SUBJ-024`,
/// `tests/Execution/Endpoints/SiteComplianceEndpointsRoutingTests.cs`) first proved out: boots the REAL,
/// unmodified <c>Program.cs</c> composition root, overriding only <see cref="ExecutionDbContext"/>'s
/// underlying provider (Npgsql to a uniquely-named <c>Microsoft.EntityFrameworkCore.InMemory</c> database) so
/// tests can exercise the real API surface without a live PostgreSQL instance. Every other <c>Program.cs</c>
/// registration (validators, MassTransit's in-memory transport, the audit/concurrency interceptors) is left
/// completely untouched.
/// <para>
/// A dedicated, isolated <c>Microsoft.EntityFrameworkCore.InMemory</c> internal service provider
/// (<c>UseInternalServiceProvider</c>) is required, not just a swapped <c>UseXxx(...)</c> call on the ambient
/// one: EF Core refuses to build a context once two relational providers' services are both present in the
/// same container, and <c>Program.cs</c>'s own Npgsql registration is not removable via a
/// <c>DbContextOptions&lt;T&gt;</c> descriptor swap alone. See <c>ExecutionProgramStartupTests</c>' own
/// remarks (`MIG-SUBJ-024`) for the full empirical account of why a same-relational-family swap (Sqlite) was
/// tried first and rejected there, and why the non-relational InMemory provider is the one that actually
/// coexists safely.
/// </para>
/// <para>
/// Unlike `MIG-SUBJ-024`'s own factory (freshly constructed, unshared, once per test method), each
/// <see cref="ExecutionApiFactory"/> instance generates its own uniquely-named in-memory database in its
/// constructor -- so xUnit fixture usage that shares one instance across an entire test class or collection
/// (e.g. <c>IClassFixture&lt;ExecutionApiFactory&gt;</c>, per <c>02_technical_design.md</c> §6.1's
/// <c>SubjectGoldenMasterTests</c>) gives every scenario replayed against that one shared instance a
/// consistent, cumulative view of the database -- required for the fixtures' multi-step scenarios (e.g.
/// `SCN-001-A` then `SCN-001-B`, whose `enrollment_index` must be sequential across the pair, not
/// independently zero) -- while two different test classes/collections, each with their own factory instance,
/// never see each other's rows.
/// </para>
/// @req:PRD-SUB-001
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
        builder.UseTestGatewaySecret();
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
