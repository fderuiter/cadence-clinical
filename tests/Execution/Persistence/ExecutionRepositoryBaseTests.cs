using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-013`'s <see cref="ExecutionRepositoryBase.SaveChangesAsync"/>: proves the shared
/// <see cref="SaveChangesResiliencePipelineFactory"/>-backed retry wrapper does not just retry blindly but
/// actually *resolves* a genuine stale-write conflict (the ticket's own explicit "which your retry policy
/// then handles" requirement) — and that the retry is still bounded: a genuinely unresolvable conflict (the
/// row deleted out from under the retrying writer) exhausts its attempts and surfaces
/// <see cref="DbUpdateConcurrencyException"/> rather than retrying forever or swallowing it.
/// <see cref="SiteComplianceRepository"/> is used as the concrete repository under test; the wrapper itself
/// lives on the shared base class, so this exercises the same code path <see cref="SubjectRepository"/> and
/// <see cref="ConsentRepository"/> use.
/// @req:PRD-SUB-001
/// </summary>
public class ExecutionRepositoryBaseTests
{
    private static readonly StudyCode Study = new("STUDY-RETRY");
    private static readonly SiteCode Site = new("SITE-RETRY");
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

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task SaveChangesAsync_GivenAStaleWriteThatAnotherWriterAlreadyCommitted_ResolvesTheConflictAndPersistsThisCallersChange()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, new[] { "Form-A" }));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // "Other writer": loads, mutates, and commits first via a plain SaveChangesAsync (no retry wrapper
        // needed -- it is not the stale one).
        await using (var otherWriterContext = CreateContext(connection))
        {
            var cache = await otherWriterContext.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);
            cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "from-other-writer" });
            await otherWriterContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // "This caller": loaded its copy of the row BEFORE the other writer committed, so its own save is
        // now stale -- exactly the scenario ConcurrencyTokenIncrementInterceptorTests proves throws when
        // saved directly. Here it goes through the repository's retry-wrapped SaveChangesAsync instead.
        await using var thisCallerContext = CreateContext(connection);
        var repository = new SiteComplianceRepository(thisCallerContext, SaveChangesResiliencePipelineFactory.Create());
        var staleCache = await thisCallerContext.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);
        staleCache.ApplyWebhookUpdate(isComplete: false, missingDocuments: new[] { "from-this-caller" });

        var act = async () => await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync("the retry policy should resolve the conflict, not merely rediscover it 3 times and give up");

        await using var verifyContext = CreateContext(connection);
        var persisted = await verifyContext.SiteComplianceCaches.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        // Client-wins: this caller's own pending edit must be what is ultimately persisted.
        persisted.MissingDocuments.Should().Equal("from-this-caller");
        persisted.IsComplete.Should().BeFalse();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task SaveChangesAsync_GivenARowDeletedOutFromUnderTheRetryingWriter_ExhaustsRetriesAndStillThrowsDbUpdateConcurrencyException()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, new[] { "Form-A" }));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var thisCallerContext = CreateContext(connection);
        var repository = new SiteComplianceRepository(thisCallerContext, SaveChangesResiliencePipelineFactory.Create());
        var staleCache = await thisCallerContext.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);
        staleCache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "from-this-caller" });

        // Row is deleted entirely (not just modified) after this caller loaded it -- GetDatabaseValuesAsync
        // in the retry's reload step returns null for every attempt, so there is nothing to merge and the
        // conflict is genuinely unresolvable, no matter how many times it is retried.
        await using (var deletingContext = CreateContext(connection))
        {
            var toDelete = await deletingContext.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);
            deletingContext.SiteComplianceCaches.Remove(toDelete);
            await deletingContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var act = async () => await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "a genuinely unresolvable conflict must still surface once retries are exhausted, not retry forever or be silently swallowed");
    }
}
