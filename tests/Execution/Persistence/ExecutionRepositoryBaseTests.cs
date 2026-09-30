using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

using Polly;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for <see cref="ExecutionRepositoryBase.SaveChangesAsync"/> and
/// <see cref="SaveChangesResiliencePipelineFactory"/> (#4766): a concurrency conflict is rejected, never retried
/// over the other writer's change, while transient database failures are still retried, and a retried save
/// writes each audit entry exactly once. Uses SQLite for real concurrency-token checks.
/// @req:PRD-SUB-001
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
            .UseSqlite(connection)
            .AddInterceptors((IInterceptor[])[new ConcurrencyTokenIncrementInterceptor(), new GxPAuditInterceptor(() => actor), .. extra])
            .Options);

    private static async Task SeedComplianceCacheAsync(SqliteConnection connection)
    {
        await using var setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync(Ct);
        setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, Site, Milestone, false, ["Form-A"]));
        await setup.SaveChangesAsync(Ct);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task SaveChangesAsync_GivenAStaleWrite_RejectsItAndKeepsTheOtherWritersChange()
    {
        using var connection = OpenSqliteConnection();
        await SeedComplianceCacheAsync(connection);

        // This caller reads the row first...
        await using var thisCallerContext = CreateContext(connection);
        var repository = new SiteComplianceRepository(thisCallerContext, SaveChangesResiliencePipelineFactory.Create());
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
    [GxPRequirement("PRD-SUB-001")]
    public async Task SaveChangesAsync_GivenARowDeletedAfterItWasRead_RejectsTheSave()
    {
        using var connection = OpenSqliteConnection();
        await SeedComplianceCacheAsync(connection);

        await using var thisCallerContext = CreateContext(connection);
        var repository = new SiteComplianceRepository(thisCallerContext, SaveChangesResiliencePipelineFactory.Create());
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
    [GxPRequirement("PRD-SYS-002")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task TwoConcurrentFormUpdates_OneSucceeds_TheOtherConflicts_AndTheAuditShowsOneChange()
    {
        using var connection = OpenSqliteConnection();
        FormSubmissionId formId;
        await using (var setup = CreateContext(connection, "creator"))
        {
            await setup.Database.EnsureCreatedAsync(Ct);
            var form = FormSubmission.Initialize(Study, new SubjectCode("SUBJ-RACE"), new FormId("FORM-VS"),
                siteId: null, visitId: null, protocolVersion: "1.0.0", initialPayload: "{}").Value!;
            setup.FormSubmissions.Add(form);
            await setup.SaveChangesAsync(Ct);
            formId = form.Id;
        }

        await using var firstContext = CreateContext(connection, "first-editor");
        await using var secondContext = CreateContext(connection, "second-editor");
        var first = new FormSubmissionRepository(firstContext, SaveChangesResiliencePipelineFactory.Create());
        var second = new FormSubmissionRepository(secondContext, SaveChangesResiliencePipelineFactory.Create());

        // Both read the same version before either saves.
        var firstCopy = (await first.GetForUpdateAsync(formId, Ct))!;
        var secondCopy = (await second.GetForUpdateAsync(formId, Ct))!;

        firstCopy.UpdatePayload("{\"source\": \"first\"}", "First correction").IsSuccess.Should().BeTrue();
        await first.SaveChangesAsync(Ct);

        secondCopy.UpdatePayload("{\"source\": \"second\"}", "Second correction").IsSuccess.Should().BeTrue();
        var act = async () => await second.SaveChangesAsync(Ct);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var verify = CreateContext(connection);
        (await verify.FormSubmissions.AsNoTracking().SingleAsync(Ct)).Payload.Should().Be("{\"source\": \"first\"}");
        var entries = await verify.AuditLogEntries.AsNoTracking().ToListAsync(Ct);
        entries.Select(e => e.UserId).Should().BeEquivalentTo(["creator", "first-editor"],
            "the rejected edit leaves no audit entry; only the create and the winning change are recorded");
        AuditLogChainVerifier.Verify(entries).Should().OnlyContain(r => r.Valid);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task SaveChangesAsync_AfterATransientFailure_RetriesAndWritesEachAuditEntryOnce()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(Ct);
        }

        var failOnce = new FailFirstInsertInterceptor();
        await using var context = CreateContext(connection, "writer", failOnce);
        var repository = new SiteComplianceRepository(context, SaveChangesResiliencePipelineFactory.Create());
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
    [GxPRequirement("PRD-SUB-001")]
    public async Task Pipeline_DoesNotRetryAConcurrencyConflict()
    {
        int attempts = 0;
        var pipeline = SaveChangesResiliencePipelineFactory.Create();

        var act = async () => await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw new DbUpdateConcurrencyException("conflict");
        }, Ct);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        attempts.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task Pipeline_RetriesATransientFailure()
    {
        int attempts = 0;
        var pipeline = SaveChangesResiliencePipelineFactory.Create();

        await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts == 1
                ? throw new DbUpdateException("wrapped", SerializationFailure())
                : ValueTask.CompletedTask;
        }, Ct);

        attempts.Should().Be(2);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void IsTransient_RecognisesOnlyTransientDatabaseFailures()
    {
        SaveChangesResiliencePipelineFactory.IsTransient(SerializationFailure()).Should().BeTrue();
        SaveChangesResiliencePipelineFactory.IsTransient(new DbUpdateException("wrapped", SerializationFailure())).Should().BeTrue();
        SaveChangesResiliencePipelineFactory.IsTransient(new DbUpdateConcurrencyException("conflict")).Should().BeFalse();
        SaveChangesResiliencePipelineFactory.IsTransient(
            new DbUpdateException("unique", new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)))
            .Should().BeFalse();
        SaveChangesResiliencePipelineFactory.IsTransient(new InvalidOperationException()).Should().BeFalse();
    }

    private static PostgresException SerializationFailure() =>
        new("could not serialize access", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);

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
    }
}
