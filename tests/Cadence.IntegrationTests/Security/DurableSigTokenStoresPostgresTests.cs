using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Signatures;
using Cadence.IntegrationTests.Database;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Security;

/// <summary>
/// Verifies that <c>ctms</c>, <c>payments</c>, <c>imaging</c>, and <c>interop</c> apply their
/// <c>consumed_signature_tokens</c> PostgreSQL migrations cleanly and enforce atomic single-use <c>jti</c>
/// consumption across independent <see cref="PostgresSigTokenUseStore{TContext}"/> instances and concurrent races.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DurableSigTokenStoresPostgresTests(ThrowawayPostgresDatabase database)
    : IClassFixture<ThrowawayPostgresDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task CtmsDurableSigTokenStore_EnforcesSingleUseAcrossInstancesAndConcurrentRaces() =>
        await VerifyDurableStoreAsync<CtmsDbContext>(CtmsDbContextFactory.Schema);

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task PaymentsDurableSigTokenStore_EnforcesSingleUseAcrossInstancesAndConcurrentRaces() =>
        await VerifyDurableStoreAsync<PaymentsDbContext>(PaymentsDbContextFactory.Schema);

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ImagingDurableSigTokenStore_EnforcesSingleUseAcrossInstancesAndConcurrentRaces() =>
        await VerifyDurableStoreAsync<ImagingDbContext>(ImagingDbContextFactory.Schema);

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task InteropDurableSigTokenStore_EnforcesSingleUseAcrossInstancesAndConcurrentRaces() =>
        await VerifyDurableStoreAsync<InteropDbContext>(InteropDbContextFactory.Schema);

    private async Task VerifyDurableStoreAsync<TContext>(string schema)
        where TContext : DbContext
    {
        database.SkipIfUnavailable();
        string connectionString = CadenceDatabaseExtensions.WithModuleSchema(database.AdminDatabaseConnectionString!, schema);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero));

        await using var providerA = BuildProvider<TContext>(connectionString, schema, clock);
        await using var providerB = BuildProvider<TContext>(connectionString, schema, clock);

        await using (var scope = providerA.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            string createSchemaSql = schema switch
            {
                CtmsDbContextFactory.Schema => "CREATE SCHEMA IF NOT EXISTS \"ctms\"",
                PaymentsDbContextFactory.Schema => "CREATE SCHEMA IF NOT EXISTS \"payments\"",
                ImagingDbContextFactory.Schema => "CREATE SCHEMA IF NOT EXISTS \"imaging\"",
                InteropDbContextFactory.Schema => "CREATE SCHEMA IF NOT EXISTS \"interop\"",
                _ => throw new ArgumentOutOfRangeException(nameof(schema), schema, "Unsupported schema."),
            };
            await db.Database.ExecuteSqlRawAsync(createSchemaSql, Ct);
            await db.Database.MigrateAsync(Ct);
        }

        var storeA = providerA.GetRequiredService<ISigTokenUseStore>();
        var storeB = providerB.GetRequiredService<ISigTokenUseStore>();
        storeA.Should().BeOfType<PostgresSigTokenUseStore<TContext>>();

        string tokenId = $"jti-{schema}-{Guid.NewGuid():N}";
        DateTimeOffset retainUntil = clock.UtcNow.AddMinutes(10);

        // 1. First use on replica A succeeds; replay on replica B fails
        (await storeA.TryRecordFirstUseAsync(tokenId, retainUntil, Ct)).Should().BeTrue();
        (await storeB.TryRecordFirstUseAsync(tokenId, retainUntil, Ct)).Should().BeFalse();

        // 2. Concurrent race across both replicas admits exactly one caller
        string raceTokenId = $"jti-race-{schema}-{Guid.NewGuid():N}";
        bool[] raceResults = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(i =>
                (i % 2 == 0 ? storeA : storeB).TryRecordFirstUseAsync(raceTokenId, retainUntil, Ct)));
        raceResults.Count(r => r).Should().Be(1);

        // 3. Advancing past RetainUntil prunes expired rows during subsequent consumption
        clock.Advance(TimeSpan.FromMinutes(15));
        string freshTokenId = $"jti-fresh-{schema}-{Guid.NewGuid():N}";
        (await storeA.TryRecordFirstUseAsync(freshTokenId, clock.UtcNow.AddMinutes(10), Ct)).Should().BeTrue();

        await using (var verifyScope = providerB.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<TContext>();
            var remainingIds = await db.Set<ConsumedSignatureToken>().AsNoTracking().Select(t => t.TokenId).ToListAsync(Ct);
            remainingIds.Should().Contain(freshTokenId)
                .And.NotContain(tokenId)
                .And.NotContain(raceTokenId);
        }
    }

    private static ServiceProvider BuildProvider<TContext>(string connectionString, string schema, IClock clock)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton<IFieldEncryptor>(DesignTimeFieldEncryptor.Instance);
        services.AddDbContext<TContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema)));
        services.AddCadenceDurableSigTokenStore<TContext>();
        return services.BuildServiceProvider();
    }
}
