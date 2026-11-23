using System;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Database;

/// <summary>
/// eConsent, Knowledge and Execution accept e-signatures, so a token used on one instance of any of them must be
/// refused by a fresh instance on the same database, which is also what a restarted instance sees (#5311, #5434,
/// ADR-2515). Each "instance" is its own service provider over its own context, with no memory shared between them.
/// Needs <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c>.
/// </summary>
public sealed class SignatureTokenSingleUsePostgresTests(ThrowawayPostgresDatabase database) : IClassFixture<ThrowawayPostgresDatabase>
{
    private static readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    /// <summary>eConsent: a used consent-capture token is refused by a second instance.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public Task Econsent_TokenUsedOnOneInstance_IsRefusedByAFreshInstance() =>
        UsedOnceAcrossInstancesAsync(
            EconsentDbContextFactory.Schema,
            connection => new EconsentDbContextFactory().CreateDbContext(Args(connection)));

    /// <summary>Knowledge: a used delegation sign-off token is refused by a second instance.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public Task Knowledge_TokenUsedOnOneInstance_IsRefusedByAFreshInstance() =>
        UsedOnceAcrossInstancesAsync(
            KnowledgeDbContextFactory.Schema,
            connection => new KnowledgeDbContextFactory().CreateDbContext(Args(connection)));

    /// <summary>Execution: a used CEC chair adjudication token is refused by a second instance.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public Task Execution_TokenUsedOnOneInstance_IsRefusedByAFreshInstance() =>
        UsedOnceAcrossInstancesAsync(
            ExecutionDbContextFactory.Schema,
            connection => new ExecutionDbContextFactory().CreateDbContext(Args(connection)));

    /// <summary>Tickets: a used Part 11 ticket closure token is refused by a second instance.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public Task Tickets_TokenUsedOnOneInstance_IsRefusedByAFreshInstance() =>
        UsedOnceAcrossInstancesAsync(
            TicketsDbContextFactory.Schema,
            connection => new TicketsDbContextFactory().CreateDbContext(Args(connection)));

    private async Task UsedOnceAcrossInstancesAsync<TContext>(string schema, Func<string, TContext> createContext)
        where TContext : DbContext
    {
        database.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string connection = database.ConnectionString!;
        await using (var migrating = createContext(connection))
        {
            // As CadenceMigrationHostedService does: without its schema the module's tables would land in public,
            // where both modules' consumed_signature_tokens collide (ADR-2447).
#pragma warning disable EF1002 // The schema is the factory's constant; DDL cannot take parameters.
            await migrating.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\"", ct);
#pragma warning restore EF1002
            await migrating.Database.MigrateAsync(ct);
            (await migrating.Database
                .SqlQuery<string>($"SELECT table_schema AS \"Value\" FROM information_schema.tables WHERE table_name = 'consumed_signature_tokens' AND table_schema = {schema}")
                .ToListAsync(ct))
                .Should().ContainSingle("the module keeps its used tokens in its own schema");
        }

        await using var first = Instance(createContext, connection);
        await using var fresh = Instance(createContext, connection);
        var firstStore = first.GetRequiredService<ISigTokenUseStore>();
        var freshStore = fresh.GetRequiredService<ISigTokenUseStore>();
        string tokenId = Guid.NewGuid().ToString();
        var retainUntil = Clock.UtcNow.AddMinutes(10);

        (await firstStore.TryRecordFirstUseAsync(tokenId, retainUntil, ct)).Should().BeTrue();
        (await freshStore.TryRecordFirstUseAsync(tokenId, retainUntil, ct)).Should().BeFalse("the use is recorded in PostgreSQL");
        (await firstStore.TryRecordFirstUseAsync(tokenId, retainUntil, ct)).Should().BeFalse();
        (await freshStore.TryRecordFirstUseAsync(Guid.NewGuid().ToString(), retainUntil, ct)).Should().BeTrue();
    }

    private static ServiceProvider Instance<TContext>(Func<string, TContext> createContext, string connection)
        where TContext : DbContext =>
        new ServiceCollection()
            .AddSingleton<IClock>(Clock)
            .AddScoped(_ => createContext(connection))
            .AddCadenceDurableSigTokenStore<TContext>()
            .BuildServiceProvider();

    private static string[] Args(string connection) =>
        [CadenceDatabaseExtensions.DesignTimeConnectionArgument, connection];
}
