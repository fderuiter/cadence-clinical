using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-013`'s <see cref="ConcurrencyTokenIncrementInterceptor"/> — the app-level fix for
/// the gap MIG-SUBJ-011's sign-off flagged: neither Npgsql/PostgreSQL nor SQLite auto-increments an arbitrary
/// shadow `_version` column on UPDATE the way SQL Server's native rowversion does, so without this
/// interceptor a stale write can never actually be detected (the shadow token never changes). Uses SQLite (a
/// real relational provider whose generated UPDATE statements genuinely include a concurrency-token
/// <c>WHERE</c> clause), not InMemory, per this ticket's own "SQLite for relational-behavior tests"
/// instruction. `SiteComplianceCache` is used as the concrete aggregate under test (a simple, easily-mutable
/// entity with no complex-type/value-converter interactions to control for).
/// </summary>
public class ConcurrencyTokenIncrementInterceptorTests
{
    private static readonly StudyCode Study = new("STUDY-CONCURRENCY");
    private static readonly SiteCode Site = new("SITE-CONCURRENCY");
    private const string Milestone = "SITE_ACTIVATION";

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ConcurrencyTokenIncrementInterceptor())
            .Options);

    private static long ReadShadowVersion(ExecutionDbContext context, SiteComplianceCache cache) =>
        (long)context.Entry(cache).Property("_version").CurrentValue!;

    [Fact]
    public async Task SavingChangesAsync_GivenNewlyAddedEntity_LeavesShadowVersionAtItsDefaultStartingValue()
    {
        using var connection = OpenSqliteConnection();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "Form-A" });
        context.SiteComplianceCaches.Add(cache);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ReadShadowVersion(context, cache).Should().Be(0L);
    }

    [Fact]
    public async Task SavingChangesAsync_GivenAModifiedEntity_IncrementsShadowVersionByExactlyOne()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, new[] { "Form-A" }));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(connection);
        var cache = await context.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);
        long before = ReadShadowVersion(context, cache);

        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "Form-B" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ReadShadowVersion(context, cache).Should().Be(before + 1);
    }

    [Fact]
    public async Task SavingChangesAsync_GivenTwoDomainMutationsBeforeOneSaveChangesAsync_IncrementsShadowVersionOnlyOnce()
    {
        // Mirrors MIG-SUBJ-012's own "two ApplyWebhookUpdate calls, one SaveChangesAsync" boundary check: the
        // interceptor increments once per SaveChangesAsync call for a Modified entry, not once per domain
        // mutation -- it has no way to observe individual method calls, only the entry's state at save time.
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, new[] { "Form-A" }));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(connection);
        var cache = await context.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);
        long before = ReadShadowVersion(context, cache);

        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "Form-B" });
        cache.ApplyWebhookUpdate(isComplete: false, missingDocuments: new[] { "Form-C" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ReadShadowVersion(context, cache).Should().Be(before + 1);
    }

    [Fact]
    public async Task SavingChangesAsync_NeverIncrementsSiteComplianceCacheVersionDomainCounter()
    {
        // SiteComplianceCache.Version is domain data (IsConcurrencyToken(false), MIG-SUBJ-012) -- this
        // interceptor must not touch it; the aggregate's own webhook-update logic is the only thing that
        // changes it.
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, new[] { "Form-A" }));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext(connection);
        var cache = await context.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);
        int domainVersionBeforeSave = cache.Version;

        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "Form-B" });
        int domainVersionAfterMutationBeforeSave = cache.Version;
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // The aggregate's own webhook-update increment already happened synchronously, before
        // SaveChangesAsync was even called; the interceptor running during the save must not add a second
        // increment on top of that.
        domainVersionAfterMutationBeforeSave.Should().Be(domainVersionBeforeSave + 1);
        cache.Version.Should().Be(domainVersionAfterMutationBeforeSave);
    }

    [Fact]
    public void ClinicalSubjectAndSubjectConsentShadowVersionProperties_AreConfiguredAsPlainConcurrencyTokensNotRowVersions()
    {
        // Ensures the reconfiguration this ticket made to all three aggregates' shadow `_version` columns
        // (dropping IsRowVersion() in favor of IsConcurrencyToken() alone) actually landed. The property is
        // still ValueGenerated.OnAdd here -- EF Core's own convention for a property with HasDefaultValue()
        // configured, unrelated to IsConcurrencyToken() -- but the important invariant is that it is NOT
        // OnAddOrUpdate: that is what IsRowVersion() implies, and what would silently discard this
        // interceptor's manually-assigned UPDATE-time value instead of sending it in the generated SQL (see
        // the interceptor's own doc comment). OnAdd only affects INSERT-time omission-with-default behavior,
        // which this ticket's interceptor never touches (it only acts on Modified entries).
        using var connection = OpenSqliteConnection();
        using var context = CreateContext(connection);

        AssertPlainClientSuppliedConcurrencyToken(context.Model.FindEntityType(typeof(ClinicalSubject)));
        AssertPlainClientSuppliedConcurrencyToken(context.Model.FindEntityType(typeof(SubjectConsent)));
        AssertPlainClientSuppliedConcurrencyToken(context.Model.FindEntityType(typeof(SiteComplianceCache)));

        static void AssertPlainClientSuppliedConcurrencyToken(IEntityType? entityType)
        {
            var property = entityType?.FindProperty("_version")
                ?? throw new System.InvalidOperationException("Expected a shadow _version property.");
            property.IsConcurrencyToken.Should().BeTrue();
            property.ValueGenerated.Should().NotBe(ValueGenerated.OnAddOrUpdate,
                "IsRowVersion()'s implied ValueGeneratedOnAddOrUpdate() would silently discard this " +
                "interceptor's manually-assigned value instead of sending it in the generated UPDATE SQL");
        }
    }

    // ---- Baseline: proves the fix creates a genuine, detectable conflict (no retry involved) -----------

    [Fact]
    public async Task SaveChangesAsync_GivenTwoContextsLoadTheSameRowAndTheFirstSavesFirst_TheSecondsStaleSaveThrowsDbUpdateConcurrencyException()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, new[] { "Form-A" }));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var contextA = CreateContext(connection);
        var cacheA = await contextA.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);

        await using var contextB = CreateContext(connection);
        var cacheB = await contextB.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);

        cacheA.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "from-A" });
        await contextA.SaveChangesAsync(TestContext.Current.CancellationToken);

        cacheB.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "from-B" });
        var act = async () => await contextB.SaveChangesAsync(TestContext.Current.CancellationToken);

        // This is the crux of the whole gap MIG-SUBJ-011's sign-off flagged: without a real per-write
        // auto-increment mechanism, contextB's stale _version would still match the row and this save would
        // silently succeed (a lost update), never raising DbUpdateConcurrencyException at all.
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
