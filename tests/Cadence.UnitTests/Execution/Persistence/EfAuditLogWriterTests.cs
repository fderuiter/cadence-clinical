using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for the single audit writer (#4768): <see cref="EfAuditLogWriter"/> only labels the next save, and
/// <see cref="GxPAuditInterceptor"/> writes exactly one entry per changed entity on one chain per aggregate
/// type, which <see cref="AuditLogChainVerifier"/> verifies end to end.
/// @req:PRD-SYS-001
/// </summary>
[GxPRequirement("PRD-SYS-001")]
public class EfAuditLogWriterTests
{
    private const string Actor = "qa.reviewer@site.org";

    private static ExecutionDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new GxPAuditInterceptor(() => Actor, SystemClock.Instance))
            .Options);

    private static ClinicalSubject NewSubject(string code) =>
        ClinicalSubject.Create(new SubjectCode(code), new StudyCode("STUDY-CHAIN"), siteCode: null,
            enrollmentIndex: 1, encryptedDemographics: "cipher-1", stratFactors: null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SubjectUpdate_WritesOneEntryPerChange_AndTheChainVerifies()
    {
        string db = Guid.NewGuid().ToString();
        SubjectId id;
        await using (var context = CreateContext(db))
        {
            var writer = new EfAuditLogWriter(context);
            var subject = NewSubject("SUBJ-ONE");
            id = subject.Id;
            context.ClinicalSubjects.Add(subject);
            await writer.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "ENROLL_SUBJECT", "user-1", "Enrollment", null, null, Ct);
            await context.SaveChangesAsync(Ct);
        }

        await using (var context = CreateContext(db))
        {
            var writer = new EfAuditLogWriter(context);
            var subject = await context.ClinicalSubjects.SingleAsync(s => s.Id == id, Ct);
            subject.UpdateDemographics("cipher-2");
            subject.ReasonForChange = "Correction";
            await writer.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "UPDATE_DEMOGRAPHICS", "user-1", "Correction", null, null, Ct);
            await context.SaveChangesAsync(Ct);
        }

        await using var verify = CreateContext(db);
        var entries = await verify.AuditLogEntries.AsNoTracking().ToListAsync(Ct);

        entries.Should().HaveCount(2, "one entry per change, not one from the writer and another from the interceptor");
        entries.Should().OnlyContain(e => e.EntityName == nameof(ClinicalSubject) && e.EntityId == id.ToString());
        entries.Select(e => e.Action).Should().BeEquivalentTo("ENROLL_SUBJECT", "UPDATE_DEMOGRAPHICS");
        var update = entries.Single(e => e.Action == "UPDATE_DEMOGRAPHICS");
        update.UserId.Should().Be("user-1");
        update.ReasonForChange.Should().Be("Correction");
        update.VersionIndex.Should().Be(2, "the entry carries the entity's own version");

        AuditLogChainVerifier.Verify(entries).Should().ContainSingle()
            .Which.Should().Be(new AuditLogChainResult(nameof(ClinicalSubject), true, 2, 2, null));
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ChangeWithoutARecordedAction_IsStillAudited_WithTheEntityStateAsAction()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        context.ClinicalSubjects.Add(NewSubject("SUBJ-PLAIN"));

        await context.SaveChangesAsync(Ct);

        var entry = await context.AuditLogEntries.AsNoTracking().SingleAsync(Ct);
        entry.Action.Should().Be("Added");
        entry.UserId.Should().Be(Actor);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ActionWithNoEntityChange_IsWrittenAsAnActionOnlyEntryOnTheSameChain()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context);
        var subject = NewSubject("SUBJ-BLOCKED");
        context.ClinicalSubjects.Add(subject);
        await context.SaveChangesAsync(Ct);

        await writer.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "BLOCKED_ENROLLMENT", "user-2", "Site not activated",
            oldValues: null, newValues: new Dictionary<string, object?> { ["target"] = "ENROLLED" }, Ct);
        await context.SaveChangesAsync(Ct);

        var entries = await context.AuditLogEntries.AsNoTracking().ToListAsync(Ct);
        var blocked = entries.Single(e => e.Action == "BLOCKED_ENROLLMENT");
        blocked.VersionIndex.Should().Be(0, "no entity version changed");
        blocked.UserId.Should().Be("user-2");
        blocked.ChangesJson.Should().Contain("ENROLLED");
        AuditLogChainVerifier.Verify(entries).Should().OnlyContain(r => r.Valid);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task WriteAsync_DoesNotItselfSave()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context);

        await writer.WriteAsync(nameof(ClinicalSubject), "SUBJ-1", "ENROLL_SUBJECT", "user-1", "Enrollment", null, null, Ct);

        (await context.AuditLogEntries.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_DetectsATamperedEntry()
    {
        var chain = BuildChain("ClinicalSubject", 3);
        var original = chain[1];
        chain[1] = new AuditLogEntry
        {
            Id = original.Id,
            EntityName = original.EntityName,
            EntityId = original.EntityId,
            Action = original.Action,
            Timestamp = original.Timestamp,
            UserId = "someone-else",
            ReasonForChange = original.ReasonForChange,
            VersionIndex = original.VersionIndex,
            ChangesJson = original.ChangesJson,
            PredecessorHash = original.PredecessorHash,
            BlockHash = original.BlockHash,
        };

        var result = AuditLogChainVerifier.Verify(chain).Single();

        result.Valid.Should().BeFalse();
        result.VerifiedCount.Should().Be(1);
        result.Error.Should().Contain("block hash mismatch");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_DetectsAFork()
    {
        var chain = BuildChain("ClinicalSubject", 2);
        chain.Add(Entry("ClinicalSubject", "fork", chain[0].BlockHash));

        var result = AuditLogChainVerifier.Verify(chain).Single();

        result.Valid.Should().BeFalse();
        result.Error.Should().Contain("forks");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_VerifiesLegacyTableNameChainsAlongsideNewOnes_WithoutRewritingThem()
    {
        // Rows written before #4768 by the old explicit writer are keyed by table name ("clinical_subjects").
        // They stay as their own read-only chain; new entries chain under the aggregate type name.
        var entries = BuildChain("clinical_subjects", 2).Concat(BuildChain(nameof(ClinicalSubject), 3)).ToList();

        var results = AuditLogChainVerifier.Verify(entries);

        results.Should().HaveCount(2).And.OnlyContain(r => r.Valid);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_DetectsDirectEditToReasonForChange_AndReportsMismatch()
    {
        var chain = BuildChain("ClinicalSubject", 3);
        var original = chain[1];
        chain[1] = new AuditLogEntry
        {
            Id = original.Id,
            EntityName = original.EntityName,
            EntityId = original.EntityId,
            Action = original.Action,
            Timestamp = original.Timestamp,
            UserId = original.UserId,
            ReasonForChange = "Tampered Reason For Change",
            VersionIndex = original.VersionIndex,
            ChangesJson = original.ChangesJson,
            PredecessorHash = original.PredecessorHash,
            BlockHash = original.BlockHash,
        };

        var result = AuditLogChainVerifier.Verify(chain).Single();

        result.Valid.Should().BeFalse();
        result.VerifiedCount.Should().Be(1);
        result.Error.Should().Contain("block hash mismatch");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_ValidatesLegacyAuditLogs_UsingFallback()
    {
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        string legacyHash = AuditLogEntry.ComputeLegacyBlockHash(
            "ClinicalSubject", "SUBJ-1", "ENROLL_SUBJECT", timestamp, "user-1", 1, null, string.Empty);

        var legacyEntry = new AuditLogEntry
        {
            EntityName = "ClinicalSubject",
            EntityId = "SUBJ-1",
            Action = "ENROLL_SUBJECT",
            Timestamp = timestamp,
            UserId = "user-1",
            ReasonForChange = "Historical Reason",
            VersionIndex = 1,
            PredecessorHash = string.Empty,
            BlockHash = legacyHash,
        };

        var result = AuditLogChainVerifier.Verify(new[] { legacyEntry }).Single();

        result.Valid.Should().BeTrue();
        result.VerifiedCount.Should().Be(1);
        result.Error.Should().BeNull();
    }

    private static List<AuditLogEntry> BuildChain(string entityName, int length)
    {
        var chain = new List<AuditLogEntry>();
        string predecessor = string.Empty;
        for (int i = 0; i < length; i++)
        {
            var entry = Entry(entityName, $"step-{i}", predecessor);
            chain.Add(entry);
            predecessor = entry.BlockHash;
        }

        return chain;
    }

    private static AuditLogEntry Entry(string entityName, string action, string predecessor, string reasonForChange = "Initial reason")
    {
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new AuditLogEntry
        {
            EntityName = entityName,
            EntityId = "SUBJ-1",
            Action = action,
            Timestamp = timestamp,
            UserId = "user-1",
            ReasonForChange = reasonForChange,
            VersionIndex = 1,
            PredecessorHash = predecessor,
            BlockHash = AuditLogEntry.ComputeBlockHash(entityName, "SUBJ-1", action, timestamp, "user-1", reasonForChange, 1, null, predecessor),
        };
    }
}
