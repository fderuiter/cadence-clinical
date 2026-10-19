using System;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
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
/// eConsent and Knowledge accept e-signatures, so a token used on one instance of either service must be refused by a
/// fresh instance on the same database, which is also what a restarted instance sees (#5434, ADR-2515). Each
/// "instance" is its own service provider over its own context, with no memory shared between them. Execution has the
/// same proof through its HTTP surface in <c>BatchSignOffSignatureTests</c>. Needs <c>CADENCE_TEST_POSTGRES</c> or
/// <c>CADENCE_TEST_DOCKER=1</c>.
/// </summary>
public sealed class SignatureTokenSingleUsePostgresTests(ThrowawayPostgresDatabase database) : IClassFixture<ThrowawayPostgresDatabase>
{
    private static readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    /// <summary>eConsent: a used consent-capture token is refused by a second instance.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public Task Econsent_TokenUsedOnOneInstance_IsRefusedByAFreshInstance() =>
        UsedOnceAcrossInstancesAsync(connection => new EconsentDbContextFactory().CreateDbContext(Args(connection)));

    /// <summary>Knowledge: a used delegation sign-off token is refused by a second instance.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public Task Knowledge_TokenUsedOnOneInstance_IsRefusedByAFreshInstance() =>
        UsedOnceAcrossInstancesAsync(connection => new KnowledgeDbContextFactory().CreateDbContext(Args(connection)));

    private async Task UsedOnceAcrossInstancesAsync<TContext>(Func<string, TContext> createContext)
        where TContext : DbContext
    {
        database.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string connection = database.ConnectionString!;
        await using (var migrating = createContext(connection))
        {
            await migrating.Database.MigrateAsync(ct);
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
