using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The eCRF correction through the kernel command pipeline on PostgreSQL (ADR-2620, #5366), as the application role
/// under Row-Level Security: a correction, its reason, its audit link, its event and its outbox row commit together or
/// not at all; a stale expected version is refused; and a lock and a correction serialise on the scope's advisory
/// lock, exclusive for the lock and shared for the correction (ADR-2621), so no correction commits after a lock. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelObservationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string EntryReason = KernelObservationScenario.EntryReason;
    private const string CorrectionReason = KernelObservationScenario.CorrectionReason;

    private readonly KernelObservationScenario _observations = new(fixture);

    public static TheoryData<string> CommitStages => new(Enum.GetNames<CommitStage>());

    public static TheoryData<string> LockScopeKinds => new(LockScopes.Kinds);

    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.UnitOfWork.FaultInjector = null;
        }
    }

    /// <summary>
    /// A correction supersedes the recorded value without overwriting it: the observation holds the new value at version
    /// 2, both revisions are kept with their reasons, the first is recorded until the second, and the one activity wrote
    /// the audit link (with the reason), the event and its outbox row.
    /// </summary>
    [Fact(DisplayName = "Correction_CommitsValueRevisionAuditEventAndOutbox_AsOneActivity")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CorrectionCommitsValueRevisionAuditEventAndOutboxAsOneActivity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, enrollment) = await NewEnrollmentAsync(ct);
        var recorded = await RecordAsync(tenant, enrollment, Mmhg(182), ct);
        recorded.Version.Should().Be(1);
        Tick();

        var corrected = await _observations.Protocol.OkAsync(Correct(tenant, recorded, Mmhg(128)), ct);

        corrected.Value.Should().Be(Mmhg(128));
        corrected.Version.Should().Be(2);
        corrected.Revision.Should().Be(2);
        await using var db = fixture.CreateContext(tenant);
        var observation = await db.Observations.AsNoTracking().SingleAsync(o => o.Id == recorded.ObservationId, ct);
        observation.Value.Should().Be(Mmhg(128));
        observation.VersionIndex.Should().Be(2);
        observation.ReasonForChange.Should().Be(CorrectionReason);
        observation.Revisions.OrderBy(r => r.Number).Select(r => (r.Number, r.Value, r.ReasonForChange))
            .Should().Equal((1, (ObservationValue)Mmhg(182), EntryReason), (2, (ObservationValue)Mmhg(128), CorrectionReason));
        var record = await fixture.ObservationRecords.FindAsync(tenant, recorded.ObservationId, ct);
        record!.Revisions[0].RecordedUntil.Should().Be(record.Revisions[1].RecordedAt, "the first value is recorded until the correction");
        record.Revisions[1].RecordedUntil.Should().BeNull();

        var audit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(Observation) && a.AggregateId == recorded.ObservationId.ToString())
            .OrderBy(a => a.Sequence).ToListAsync(ct);
        audit.Select(a => (a.Action, a.AggregateVersion, a.ReasonForChange))
            .Should().Equal(("Created", 1L, EntryReason), ("Updated", 2L, CorrectionReason));
        var activity = audit[1].ActivityId;
        var correctedEvent = await db.DomainEvents.AsNoTracking().SingleAsync(e => e.ActivityId == activity, ct);
        correctedEvent.Type.Should().Be("ObservationCorrected");
        correctedEvent.Payload.Should().Contain(CorrectionReason);
        (await db.OutboxMessages.AsNoTracking().CountAsync(o => o.EventId == correctedEvent.EventId, ct)).Should().Be(1);
        (await db.ProvenanceActivities.AsNoTracking().SingleAsync(p => p.Id == activity, ct)).Outcome.Should().Be("Committed");
    }

    /// <summary>
    /// K-TX-2 for the correction: a failure at any stage of the commit leaves the recorded value, its single revision,
    /// the audit chain, the events and the outbox exactly as they were.
    /// </summary>
    [Theory(DisplayName = "Correction_FailingAtAnyCommitStage_ChangesNothing")]
    [MemberData(nameof(CommitStages))]
    public async Task CorrectionFailingAtAnyCommitStageChangesNothing(string stageName)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, enrollment) = await NewEnrollmentAsync(ct);
        var recorded = await RecordAsync(tenant, enrollment, Mmhg(182), ct);
        var before = await CountsAsync(tenant, ct);
        var stage = Enum.Parse<CommitStage>(stageName);
        fixture.UnitOfWork.FaultInjector = s =>
        {
            if (s == stage)
            {
                throw new InvalidOperationException($"Injected fault at {stage}.");
            }
        };

        var act = () => fixture.Sender.SendAsync(Correct(tenant, recorded, Mmhg(128)), cancellationToken: ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"Injected fault at {stage}.");
        fixture.UnitOfWork.FaultInjector = null;
        (await CountsAsync(tenant, ct)).Should().Be(before);
        await using var db = fixture.CreateContext(tenant);
        var observation = await db.Observations.AsNoTracking().SingleAsync(o => o.Id == recorded.ObservationId, ct);
        observation.Value.Should().Be(Mmhg(182));
        observation.VersionIndex.Should().Be(1);
        observation.Revisions.Should().ContainSingle();
    }

    /// <summary>
    /// A correction naming a version that is no longer current is refused with 409 and writes nothing, so a user who
    /// corrected from a stale screen never overwrites a correction they did not see.
    /// </summary>
    [Fact(DisplayName = "Correction_WithAStaleExpectedVersion_IsRefusedAndWritesNothing")]
    public async Task CorrectionWithAStaleExpectedVersionIsRefusedAndWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, enrollment) = await NewEnrollmentAsync(ct);
        var recorded = await RecordAsync(tenant, enrollment, Mmhg(182), ct);
        await _observations.Protocol.OkAsync(Correct(tenant, recorded, Mmhg(128)), ct);
        var before = await CountsAsync(tenant, ct);

        var stale = await fixture.Sender.SendAsync(Correct(tenant, recorded, Mmhg(129)), cancellationToken: ct);

        stale.Error!.Code.Should().Be(ObservationErrors.VersionStale.Code);
        stale.Error.HttpStatus.Should().Be(409);
        (await CountsAsync(tenant, ct)).Should().Be(before);
    }

    /// <summary>
    /// Two corrections from the same version race: exactly one commits and the other is refused as stale, because both
    /// take the lock aggregate's advisory lock and the second reads the version the first committed.
    /// </summary>
    [Fact(DisplayName = "ConcurrentCorrections_FromTheSameVersion_OneCommitsAndOneIsStale")]
    public async Task ConcurrentCorrectionsFromTheSameVersionOneCommitsAndOneIsStale()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, enrollment) = await NewEnrollmentAsync(ct);
        var recorded = await RecordAsync(tenant, enrollment, Mmhg(182), ct);

        var results = await Task.WhenAll(
            fixture.Sender.SendAsync(Correct(tenant, recorded, Mmhg(128)), cancellationToken: ct),
            fixture.Sender.SendAsync(Correct(tenant, recorded, Mmhg(138)), cancellationToken: ct));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => !r.IsSuccess).Error!.Code.Should().Be(ObservationErrors.VersionStale.Code);
        await using var db = fixture.CreateContext(tenant);
        (await db.Observations.AsNoTracking().SingleAsync(o => o.Id == recorded.ObservationId, ct)).VersionIndex.Should().Be(2);
    }

    /// <summary>
    /// The lock-versus-write race at each scope that covers an observation (ADR-2621), made deterministic: a correction
    /// is held inside its commit while a lock is sent. The lock waits for the correction's shared scope lock, then
    /// commits after it; a correction sent after the lock is refused with 423. In the other order, a lock held inside its
    /// commit makes a concurrent correction wait on its exclusive scope lock and then refuse. Either way no correction
    /// commits after the lock (the "unlocked" check holds at commit time).
    /// </summary>
    [Theory(DisplayName = "LockAndCorrection_Race_NoCorrectionCommitsAfterTheLock")]
    [MemberData(nameof(LockScopeKinds))]
    public async Task LockAndCorrectionRaceNoCorrectionCommitsAfterTheLock(string scopeKind)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        // 1. The correction is mid-commit when the lock arrives.
        var subject = await _observations.NewEnrollmentAsync(ct);
        var recorded = await RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(182), ct);
        var lockCommand = await _observations.LockAsync(subject.Tenant, subject.Study, subject.Scope(scopeKind, recorded.ObservationId), ct);
        using (var paused = new KernelCommitPause(fixture))
        {
            var correction = paused.HoldAsync(() => fixture.Sender.SendAsync(Correct(subject.Tenant, recorded, Mmhg(128)), cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var locking = fixture.Sender.SendAsync(lockCommand, cancellationToken: ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            locking.IsCompleted.Should().BeFalse("the lock waits for the correction's shared scope lock ({0})", Describe(locking));
            paused.Release();

            (await correction).IsSuccess.Should().BeTrue();
            (await locking).IsSuccess.Should().BeTrue();
        }

        var after = await fixture.Sender.SendAsync(Correct(subject.Tenant, recorded, Mmhg(138), expectedVersion: 2), cancellationToken: ct);
        after.Error!.Code.Should().Be(ObservationErrors.Locked.Code);
        after.Error.HttpStatus.Should().Be(423);
        await AssertNoCorrectionAfterLockAsync(subject.Tenant, recorded.ObservationId, lockCommand.AssertionId, expectedCorrections: 1, ct);

        // 2. The lock is mid-commit when the correction arrives.
        subject = await _observations.NewEnrollmentAsync(ct);
        recorded = await RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(182), ct);
        lockCommand = await _observations.LockAsync(subject.Tenant, subject.Study, subject.Scope(scopeKind, recorded.ObservationId), ct);
        using (var paused = new KernelCommitPause(fixture))
        {
            var locking = paused.HoldAsync(() => fixture.Sender.SendAsync(lockCommand, cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var correction = fixture.Sender.SendAsync(Correct(subject.Tenant, recorded, Mmhg(128)), cancellationToken: ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            correction.IsCompleted.Should().BeFalse("the correction waits for the lock's exclusive scope lock ({0})", Describe(correction));
            paused.Release();

            (await locking).IsSuccess.Should().BeTrue();
            (await correction).Error!.Code.Should().Be(ObservationErrors.Locked.Code);
        }

        await AssertNoCorrectionAfterLockAsync(subject.Tenant, recorded.ObservationId, lockCommand.AssertionId, expectedCorrections: 0, ct);
    }

    /// <summary>
    /// A caller authorized for one enrollment cannot correct another enrollment's observation by naming their own: the
    /// observation is reported as not found and nothing is written.
    /// </summary>
    [Fact(DisplayName = "Correction_NamingAnotherEnrollment_IsNotFound")]
    public async Task CorrectionNamingAnotherEnrollmentIsNotFound()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, enrollment) = await NewEnrollmentAsync(ct);
        var recorded = await RecordAsync(tenant, enrollment, Mmhg(182), ct);
        var other = await ScreenAsync(tenant, await AddStudySiteAsync(tenant, recorded.StudyId, ct), ct);

        var result = await fixture.Sender.SendAsync(
            new CorrectObservation(tenant, recorded.ObservationId, other, Mmhg(128), 1, KernelProtocolScenario.Key(), CorrectionReason), cancellationToken: ct);

        result.Error!.Code.Should().Be(KernelErrors.NotFound.Code);
        await using var db = fixture.CreateContext(tenant);
        (await db.Observations.AsNoTracking().SingleAsync(o => o.Id == recorded.ObservationId, ct)).Value.Should().Be(Mmhg(182));
    }

    /// <summary>
    /// Typed values (ADR-2623) survive PostgreSQL: each variant is stored as <c>jsonb</c> and read back equal, through
    /// the domain model and the observation read, although jsonb reorders the members; a quantity keeps its entered
    /// precision. A value that was not obtained is recorded with its reason and corrected like any other (K-FACT-3).
    /// </summary>
    [Fact(DisplayName = "TypedValues_RoundTripThroughJsonb_AndAMissingValueIsCorrectable")]
    public async Task TypedValuesRoundTripThroughJsonbAndAMissingValueIsCorrectable()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, enrollment) = await NewEnrollmentAsync(ct);
        ObservationValue[] values =
        [
            new QuantityValue(1.50m, "mg/dL"),
            new QuantityValue(0.5m, "mg/dL", QuantityComparator.LessThan),
            new CodedValue(CodeSystemVersionId.New(), "C49488"),
            new TextValue("Headache, mild"),
            new BooleanValue(false),
            new DateTimeValue("2026-03"),
            new MissingValue(NullFlavor.NotDone, "Patient refused"),
        ];

        foreach (var value in values)
        {
            var recorded = await RecordAsync(tenant, enrollment, value, ct);
            recorded.Value.Should().Be(value);
            await using var db = fixture.CreateContext(tenant);
            (await db.Observations.AsNoTracking().SingleAsync(o => o.Id == recorded.ObservationId, ct)).Value.Should().Be(value);
            (await fixture.ObservationRecords.FindAsync(tenant, recorded.ObservationId, ct))!.Revisions.Single().Value.Should().Be(value);
        }

        var missing = await RecordAsync(tenant, enrollment, new MissingValue(NullFlavor.NotDone, "Cuff unavailable"), ct);
        var corrected = await _observations.Protocol.OkAsync(Correct(tenant, missing, Mmhg(118)), ct);
        corrected.Value.Should().Be(Mmhg(118));
        var stored = await StoredJsonAsync(tenant, missing.ObservationId, ct);
        stored.Should().Contain("\"value\": 118").And.Contain("\"type\": \"quantity\"");
        (await StoredJsonAsync(tenant, (await RecordAsync(tenant, enrollment, new QuantityValue(1.50m, "mg/dL"), ct)).ObservationId, ct))
            .Should().Contain("1.50", "jsonb keeps the scale the value was entered with");
    }

    /// <summary>A correction to the same value, or to an empty value, is refused as a rule violation.</summary>
    [Fact(DisplayName = "Correction_UnchangedOrEmptyValue_IsRefused")]
    public async Task CorrectionUnchangedOrEmptyValueIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, enrollment) = await NewEnrollmentAsync(ct);
        var recorded = await RecordAsync(tenant, enrollment, Mmhg(182), ct);

        (await fixture.Sender.SendAsync(Correct(tenant, recorded, Mmhg(182)), cancellationToken: ct)).Error!.Code
            .Should().Be(ObservationErrors.ValueUnchanged.Code);
        (await fixture.Sender.SendAsync(Correct(tenant, recorded, new TextValue(" ")), cancellationToken: ct)).Error!.Code
            .Should().Be(ObservationErrors.ValueInvalid.Code);
    }

    private static string Describe<T>(Task<CommandResult<T>> task) =>
        task.IsCompletedSuccessfully ? $"completed: {task.Result.Error?.Code ?? "success"} {task.Result.Error?.Message}" : task.Status.ToString();

    private async Task AssertNoCorrectionAfterLockAsync(
        TenantId tenant, ObservationId observation, AssertionId lockId, int expectedCorrections, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var key = observation.ToString();
        var lockKey = lockId.ToString();
        var lockSequence = (await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.AggregateType == nameof(Assertion) && a.AggregateId == lockKey, ct)).Sequence;
        var corrections = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(Observation) && a.AggregateId == key && a.Action == "Updated")
            .ToListAsync(ct);
        corrections.Should().HaveCount(expectedCorrections);
        corrections.Should().OnlyContain(c => c.Sequence < lockSequence, "no correction commits after the lock");
    }

    private async Task<(int Revisions, int Audit, int Events, int Outbox)> CountsAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        return (
            await db.ObservationRevisions.CountAsync(ct),
            await db.AuditEntries.CountAsync(ct),
            await db.DomainEvents.CountAsync(ct),
            await db.OutboxMessages.CountAsync(ct));
    }

    /// <summary>Records one observation in a new tenant, for tests of the observation tables.</summary>
    internal static async Task SeedAsync(KernelPostgresFixture fixture, CancellationToken ct)
    {
        using var tests = new KernelObservationPostgresTests(fixture);
        var (tenant, enrollment) = await tests.NewEnrollmentAsync(ct);
        await tests.RecordAsync(tenant, enrollment, Mmhg(120), ct);
    }

    private async Task<(TenantId Tenant, EnrollmentId Enrollment)> NewEnrollmentAsync(CancellationToken ct)
    {
        var subject = await _observations.NewEnrollmentAsync(ct);
        return (subject.Tenant, subject.Enrollment);
    }

    private Task<ObservationState> RecordAsync(TenantId tenant, EnrollmentId enrollment, ObservationValue value, CancellationToken ct) =>
        _observations.RecordAsync(tenant, enrollment, value, ct);

    private static CorrectObservation Correct(TenantId tenant, ObservationState observation, ObservationValue value, long expectedVersion = 1) =>
        KernelObservationScenario.Correct(tenant, observation, value, expectedVersion);

    private static QuantityValue Mmhg(decimal value) => KernelObservationScenario.Mmhg(value);

    private async Task<string> StoredJsonAsync(TenantId tenant, ObservationId observation, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        return await db.Database
            .SqlQuery<string>($"SELECT value::text AS \"Value\" FROM kernel.observations WHERE id = {observation.Value}")
            .SingleAsync(ct);
    }

    private void Tick() => _observations.Tick();

    private Task<StudySiteId> AddStudySiteAsync(TenantId tenant, StudyId study, CancellationToken ct) => _observations.AddStudySiteAsync(tenant, study, ct);

    private Task<EnrollmentId> ScreenAsync(TenantId tenant, StudySiteId studySite, CancellationToken ct) => _observations.ScreenAsync(tenant, studySite, ct);
}
