using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-017`'s <see cref="EfAuditLogWriter"/>: the explicit, application-initiated
/// audit-row write path that appends to the caller's own <see cref="ExecutionDbContext"/> `ChangeTracker`
/// (never a second context) and Merkle-chains its <see cref="AuditLogEntry"/> rows per `tableName`,
/// independent of and alongside `GxPAuditInterceptor`'s own per-CLR-type chain (`02_technical_design.md`
/// §4.4).
/// @req:PRD-SYS-053
/// </summary>
[GxPRequirement("PRD-SYS-053")]
public class EfAuditLogWriterTests
{
    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private static ExecutionDbContext CreateContext(string databaseName, GxPAuditInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ExecutionDbContext>().UseInMemoryDatabase(databaseName);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ExecutionDbContext(builder.Options);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context, new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        await writer.WriteAsync("clinical_subjects", "SUBJ-1", "ENROLL_SUBJECT", "user-1", "Initial enrollment",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = await context.AuditLogEntries.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        entry.EntityName.Should().Be("clinical_subjects");
        entry.EntityId.Should().Be("SUBJ-1");
        entry.Action.Should().Be("ENROLL_SUBJECT");
        entry.UserId.Should().Be("user-1");
        entry.ReasonForChange.Should().Be("Initial enrollment");
        entry.PredecessorHash.Should().BeEmpty();
        entry.BlockHash.Should().NotBeNullOrEmpty();
        entry.VersionIndex.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var writer = new EfAuditLogWriter(context, clock);

        await writer.WriteAsync("clinical_subjects", "SUBJ-1", "ENROLL_SUBJECT", "user-1", "Initial enrollment",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        await writer.WriteAsync("clinical_subjects", "SUBJ-1", "TRANSITION_ENROLLED", "user-1", "State transition",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entries = await context.AuditLogEntries.AsNoTracking()
            .OrderBy(e => e.Timestamp).ToListAsync(TestContext.Current.CancellationToken);

        entries.Should().HaveCount(2);
        entries[0].PredecessorHash.Should().BeEmpty();
        entries[1].PredecessorHash.Should().Be(entries[0].BlockHash);
        entries[1].BlockHash.Should().NotBe(entries[0].BlockHash);
        entries[1].VersionIndex.Should().Be(2);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task WriteAsync_GivenDifferentTableNames_ChainsEachIndependently()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context, new FakeClock(DateTimeOffset.UtcNow));

        await writer.WriteAsync("clinical_subjects", "SUBJ-1", "ENROLL_SUBJECT", "user-1", "reason",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await writer.WriteAsync("subject_consents", "CONSENT-1", "RECORD_CONSENT", "user-1", "reason",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var consentEntry = await context.AuditLogEntries.AsNoTracking()
            .SingleAsync(e => e.EntityName == "subject_consents", TestContext.Current.CancellationToken);

        // A second, unrelated tableName's chain starts fresh — it does not chain off "clinical_subjects".
        consentEntry.PredecessorHash.Should().BeEmpty();
        consentEntry.VersionIndex.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context, new FakeClock(DateTimeOffset.UtcNow));

        await writer.WriteAsync("clinical_subjects", "SUBJ-1", "ENROLL_SUBJECT", "user-1", "reason",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);

        context.ChangeTracker.Entries<AuditLogEntry>().Should().ContainSingle(e => e.State == EntityState.Added);
        (await context.AuditLogEntries.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await context.AuditLogEntries.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context, new FakeClock(DateTimeOffset.UtcNow));
        var oldValues = new Dictionary<string, object?> { ["status"] = "Screening" };
        var newValues = new Dictionary<string, object?> { ["status"] = "Enrolled" };

        await writer.WriteAsync("clinical_subjects", "SUBJ-1", "TRANSITION_ENROLLED", "user-1", "reason",
            oldValues, newValues, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = await context.AuditLogEntries.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        entry.ChangesJson.Should().NotBeNullOrEmpty();
        entry.ChangesJson.Should().Contain("Screening").And.Contain("Enrolled");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context, new FakeClock(DateTimeOffset.UtcNow));

        await writer.WriteAsync("clinical_subjects", "SUBJ-1", "BLOCKED_ENROLLMENT", "user-1", "reason",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = await context.AuditLogEntries.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        entry.ChangesJson.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall()
    {
        // Demonstrates the ticket's central requirement together with §4.4's "alongside, not instead of"
        // framing: adding a ClinicalSubject (interceptor-audited) and calling this writer (explicitly
        // audited) in the same unit of work, then a single SaveChangesAsync, commits both mutations and
        // both independent audit trails atomically, with no collision between their EntityName scopes.
        await using var context = CreateContext(
            Guid.NewGuid().ToString(), new GxPAuditInterceptor(() => "qa.reviewer@site.org"));
        var writer = new EfAuditLogWriter(context, new FakeClock(DateTimeOffset.UtcNow));
        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-COEXIST"), new StudyCode("STUDY-COEXIST"), siteCode: null,
            enrollmentIndex: 1, encryptedDemographics: null, stratFactors: null);

        context.ClinicalSubjects.Add(subject);
        await writer.WriteAsync("clinical_subjects", "SUBJ-COEXIST", "ENROLL_SUBJECT", "user-1", "Initial enrollment",
            oldValues: null, newValues: null, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var interceptorEntries = await context.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == nameof(ClinicalSubject)).ToListAsync(TestContext.Current.CancellationToken);
        var writerEntries = await context.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == "clinical_subjects").ToListAsync(TestContext.Current.CancellationToken);

        interceptorEntries.Should().ContainSingle();
        interceptorEntries[0].UserId.Should().Be("qa.reviewer@site.org");
        writerEntries.Should().ContainSingle();
        writerEntries[0].UserId.Should().Be("user-1");
        writerEntries[0].Action.Should().Be("ENROLL_SUBJECT");
    }
}
