using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for <see cref="ExecutionRepositoryBase.SaveChangesAsync"/> and
/// <see cref="ExecutionRepositoryBase.ExecuteInTransactionAsync{TResult}"/> (#4766, #5383): a concurrency conflict is
/// rejected, never retried over the other writer's change, while the execution strategy retries transient database
/// failures, and a retried save or unit of work writes each audit entry exactly once. Uses SQLite for real
/// concurrency-token checks, with a retrying strategy that treats PostgreSQL's transient errors as Npgsql does.
/// </summary>
public class ExecutionRepositoryBaseTests
{
    private static readonly StudyCode Study = new("STUDY-RETRY");
    private static readonly SiteCode Site = new("SITE-RETRY");
    private const string Milestone = "SITE_ACTIVATION";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateContext(SqliteConnection connection, string actor = "writer", params IInterceptor[] extra) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection, sqlite => sqlite.ExecutionStrategy(dependencies => new RetryTransientFailures(dependencies)))
            .AddInterceptors((IInterceptor[])[new ConcurrencyTokenIncrementInterceptor(), new GxPAuditInterceptor(() => actor, SystemClock.Instance), .. extra])
            .Options);

    private static Result<bool> AdjudicateChair(CecDossier dossier, string chair, string classification)
    {
        var signedAt = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        return dossier.AdjudicateChair(
            finalClassification: classification,
            finalSubtype: null,
            finalAdjudicatedOnsetDate: "2026-02-10",
            adjudicationRationale: "Chair determination",
            signatureManifest: SignatureManifest.Create(
                SignerId: chair,
                SignerRole: "CEC_CHAIR",
                SigningReason: "I confirm this clinical endpoint adjudication determination is final",
                SignedAt: signedAt,
                PayloadHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            finalizedAt: signedAt);
    }

    private static async Task SeedComplianceCacheAsync(SqliteConnection connection)
    {
        await using var setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync(Ct);
        setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, ["Form-A"]));
        await setup.SaveChangesAsync(Ct);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task SaveChangesAsync_GivenAStaleWrite_RejectsItAndKeepsTheOtherWritersChange()
    {
        using var connection = OpenSqliteConnection();
        await SeedComplianceCacheAsync(connection);

        // This caller reads the row first...
        await using var thisCallerContext = CreateContext(connection);
        var repository = new SiteComplianceRepository(thisCallerContext);
        var staleCache = await thisCallerContext.SiteComplianceCaches.SingleAsync(Ct);

        // ...then another writer changes and commits it.
        await using (var otherWriterContext = CreateContext(connection))
        {
            var cache = await otherWriterContext.SiteComplianceCaches.SingleAsync(Ct);
            cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: ["from-other-writer"]);
            await otherWriterContext.SaveChangesAsync(Ct);
        }

        staleCache.ApplyWebhookUpdate(isComplete: false, missingDocuments: ["from-this-caller"]);
        var act = async () => await repository.SaveChangesAsync(Ct);

        (await act.Should().ThrowAsync<ConcurrencyConflictException>())
            .Which.EntityNames.Should().Equal(nameof(SiteComplianceCache));

        await using var verifyContext = CreateContext(connection);
        var persisted = await verifyContext.SiteComplianceCaches.AsNoTracking().SingleAsync(Ct);
        persisted.MissingDocuments.Should().Equal("from-other-writer");
        persisted.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task SaveChangesAsync_GivenARowDeletedAfterItWasRead_RejectsTheSave()
    {
        using var connection = OpenSqliteConnection();
        await SeedComplianceCacheAsync(connection);

        await using var thisCallerContext = CreateContext(connection);
        var repository = new SiteComplianceRepository(thisCallerContext);
        var staleCache = await thisCallerContext.SiteComplianceCaches.SingleAsync(Ct);
        staleCache.ApplyWebhookUpdate(isComplete: true, missingDocuments: ["from-this-caller"]);

        await using (var deletingContext = CreateContext(connection))
        {
            var toDelete = await deletingContext.SiteComplianceCaches.SingleAsync(Ct);
            deletingContext.SiteComplianceCaches.Remove(toDelete);
            await deletingContext.SaveChangesAsync(Ct);
        }

        var act = async () => await repository.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task TwoConcurrentChairAdjudications_OneSucceeds_TheOtherConflicts_AndTheAuditShowsOneChange()
    {
        using var connection = OpenSqliteConnection();
        const string dossierId = "DOSSIER-RACE";
        await using (var setup = CreateContext(connection, "creator"))
        {
            await setup.Database.EnsureCreatedAsync(Ct);
            setup.CecDossiers.Add(CecDossier.Initialize(
                dossierId: dossierId,
                eventId: "EVT-RACE",
                studyId: Study.Value,
                subjectId: "SUBJ-RACE",
                maskedSubjectId: "MASK-RACE",
                candidateEventType: "CARDIOVASCULAR",
                reportedTerm: "Myocardial Infarction",
                reportedOnsetDate: "2026-02-10"));
            await setup.SaveChangesAsync(Ct);
        }

        await using var firstContext = CreateContext(connection, "first-editor");
        await using var secondContext = CreateContext(connection, "second-editor");
        var first = new CecRepository(firstContext);
        var second = new CecRepository(secondContext);

        // Both read the same version before either saves.
        var firstCopy = (await first.GetDossierForUpdateAsync(dossierId, Ct))!;
        var secondCopy = (await second.GetDossierForUpdateAsync(dossierId, Ct))!;

        AdjudicateChair(firstCopy, "first-editor", "ISCHEMIC_STROKE").IsSuccess.Should().BeTrue();
        await first.SaveChangesAsync(Ct);

        AdjudicateChair(secondCopy, "second-editor", "HEMORRHAGIC_STROKE").IsSuccess.Should().BeTrue();
        var act = async () => await second.SaveChangesAsync(Ct);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var verify = CreateContext(connection);
        (await verify.CecDossiers.AsNoTracking().SingleAsync(Ct)).FinalConsensusClassification.Should().Be("ISCHEMIC_STROKE");
        var entries = await verify.AuditLogEntries.AsNoTracking().ToListAsync(Ct);
        entries.Select(e => e.UserId).Should().BeEquivalentTo(["creator", "first-editor"],
            "the rejected edit leaves no audit entry; only the create and the winning change are recorded");
        AuditLogChainVerifier.Verify(entries).Should().OnlyContain(r => r.Valid);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_AfterATransientFailure_RetriesAndWritesEachAuditEntryOnce()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(Ct);
        }

        var failOnce = new FailFirstInsertInterceptor();
        await using var context = CreateContext(connection, "writer", failOnce);
        var repository = new SiteComplianceRepository(context);
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, true, []);
        context.SiteComplianceCaches.Add(cache);
        AuditActions.Record(context, new AuditAction(
            nameof(SiteComplianceCache), cache.Id.ToString(), "ETMF_COMPLIANCE_UPDATE", "writer", "Webhook"));

        await repository.SaveChangesAsync(Ct);

        failOnce.Failures.Should().Be(1);
        await using var verify = CreateContext(connection);
        var entry = await verify.AuditLogEntries.AsNoTracking().SingleAsync(Ct);
        entry.Action.Should().Be("ETMF_COMPLIANCE_UPDATE", "the retry keeps the semantic action of the failed attempt");
        entry.VersionIndex.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_AfterATransientFailure_RerunsTheWholeUnitOfWorkOnce()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(Ct);
        }

        var failOnce = new FailFirstInsertInterceptor();
        await using var context = CreateContext(connection, "writer", failOnce);
        var repository = new SiteComplianceRepository(context);
        int runs = 0;

        await repository.ExecuteInTransactionAsync(async ct =>
        {
            runs++;
            var cache = SiteComplianceCache.Create(Study, Site, Milestone, true, []);
            context.SiteComplianceCaches.Add(cache);
            AuditActions.Record(context, new AuditAction(
                nameof(SiteComplianceCache), cache.Id.ToString(), "ETMF_COMPLIANCE_UPDATE", "writer", "Webhook"));
            await repository.SaveChangesAsync(ct);
            return cache.Id;
        }, Ct);

        failOnce.Failures.Should().Be(1);
        runs.Should().Be(2, "the transaction is retried as a whole, not the one statement that failed");
        await using var verify = CreateContext(connection);
        (await verify.SiteComplianceCaches.AsNoTracking().CountAsync(Ct))
            .Should().Be(1, "the failed attempt's staged row is discarded before the retry");
        (await verify.AuditLogEntries.AsNoTracking().CountAsync(Ct)).Should().Be(1);
    }

    /// <summary>Fails the first INSERT with a transient serialization failure, then lets later ones through.</summary>
    private sealed class FailFirstInsertInterceptor : DbCommandInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            FailOnce(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            FailOnce(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void FailOnce(DbCommand command)
        {
            if (Failures == 0 && command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                Failures++;
                throw SerializationFailure();
            }
        }

        private static PostgresException SerializationFailure() =>
            new("could not serialize access", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
    }

    /// <summary>Retries the transient PostgreSQL errors the Npgsql retrying strategy retries, without its delays.</summary>
    private sealed class RetryTransientFailures(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is PostgresException { IsTransient: true };
    }
}
