using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// eCRF page saves through the kernel command pipeline on PostgreSQL (ADR-2622, #5459), as the application role under
/// Row-Level Security: a form instance opens under the protocol version effective at its site, a page records and
/// corrects its values in one transaction or not at all, one stale revision refuses the whole page, concurrent saves
/// serialise on the form's advisory lock, and a lock at any covering scope refuses the save (ADR-2621). Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelCollectionPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private readonly KernelCollectionScenario _forms = new(fixture);

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
    /// A form opens under the protocol version effective at the participant's site when the values were collected, and
    /// records a GovernedBy edge from that version. It is refused before any version is effective, for a CollectionSpec
    /// the version lacks or a visit that does not collect it, for an unknown enrollment and for a time in the future.
    /// </summary>
    [Fact(DisplayName = "Open_ResolvesTheGoverningVersion_AndRefusesWhatItDoesNotGovern")]
    public async Task OpenResolvesTheGoverningVersionAndRefusesWhatItDoesNotGovern()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _forms.Protocol.NewStudyAsync(ct);
        var studySite = await _forms.Observations.AddStudySiteAsync(tenant, study, ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, await _forms.Observations.ScreenAsync(tenant, studySite, ct));
        var beforeAnyVersion = Tick();
        var version = await _forms.EffectiveVersionAsync(tenant, study, ct);

        (await SendAsync(Open(subject, CollectionInstanceId.New(), beforeAnyVersion), ct)).Error.Should().Be(CollectionErrors.NoGoverningVersion);
        (await SendAsync(Open(subject, CollectionInstanceId.New(), Tick(), specId: "cs.labs"), ct)).Error!.Code.Should().Be(CollectionErrors.SpecUnknown.Code);
        (await SendAsync(Open(subject, CollectionInstanceId.New(), Tick(), visit: "act.week24"), ct)).Error!.Code.Should().Be(CollectionErrors.SpecUnknown.Code);
        (await SendAsync(Open(subject with { Enrollment = EnrollmentId.New() }, CollectionInstanceId.New(), Tick()), ct)).Error
            .Should().Be(CollectionErrors.EnrollmentUnknown);
        (await SendAsync(Open(subject, CollectionInstanceId.New(), Tick().AddHours(1)), ct)).Error.Should().Be(CollectionErrors.CollectedAtInvalid);
        (await SendAsync(Open(subject, CollectionInstanceId.New(), Tick(), specId: ""), ct)).Error!.Code.Should().Be(KernelErrors.CommandInvalid.Code);

        var collectedAt = Tick();
        var opened = await _forms.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), collectedAt), ct);

        opened.Should().BeEquivalentTo(new CollectionInstanceState(opened.CollectionInstanceId, subject.Enrollment, version, SpecId, VisitId, collectedAt, 1));
        await using var db = fixture.CreateContext(tenant);
        (await db.LineageEdges.AsNoTracking().CountAsync(
            e => e.Kind == LineageEdgeKind.GovernedBy && e.FromId == version.Value && e.ToId == opened.CollectionInstanceId.Value, ct))
            .Should().Be(1);
        (await SendAsync(Open(subject, opened.CollectionInstanceId, Tick()), ct)).Error.Should().Be(KernelErrors.AlreadyExists);
    }

    /// <summary>
    /// A first page records two values and a second records a third and corrects one, leaving one unchanged: each save
    /// is one activity that writes the form, its changed observations, their audit links and events together; every new
    /// observation version has a GovernedBy edge from the protocol version; and the result and the form read return
    /// every value at its current revision.
    /// </summary>
    [Fact(DisplayName = "Save_RecordsAndCorrectsAPage_AsOneActivity")]
    [KernelRule("K-FACT-6")]
    public async Task SaveRecordsAndCorrectsAPageAsOneActivity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);

        var first = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(91))), ct);
        Tick();
        var second = await _forms.Protocol.OkAsync(
            Save(form, Value(Systolic, Mmhg(128), 1), Value(Diastolic, Mmhg(91), 1), Value(Comment, new TextValue("Left arm, seated"))), ct);

        first.Version.Should().Be(2);
        first.Values.Select(v => (v.ItemId, v.Revision, v.Changed)).Should().Equal((Diastolic, 1, true), (Systolic, 1, true));
        second.Version.Should().Be(3);
        second.Values.Select(v => (v.ItemId, v.Revision, v.Changed))
            .Should().Equal((Comment, 1, true), (Diastolic, 1, false), (Systolic, 2, true));
        second.Values[2].ObservationId.Should().Be(first.Values[1].ObservationId, "a correction keeps the observation");

        var record = await fixture.CollectionInstanceRecords.FindAsync(form.Subject.Tenant, form.Instance, ct);
        record!.Version.Should().Be(3);
        record.ProtocolVersionId.Should().Be(form.Version);
        record.Values.Select(v => (v.ItemId, v.Value, v.Revision))
            .Should().Equal(
                (Comment, (ObservationValue)new TextValue("Left arm, seated"), 1), (Diastolic, (ObservationValue)Mmhg(91), 1), (Systolic, (ObservationValue)Mmhg(128), 2));
        record.Locks.Should().BeEmpty();

        await using var db = fixture.CreateContext(form.Subject.Tenant);
        var instanceKey = form.Instance.ToString();
        var saves = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(CollectionInstance) && a.AggregateId == instanceKey)
            .OrderBy(a => a.Sequence).ToListAsync(ct);
        saves.Select(a => (a.Action, a.AggregateVersion)).Should().Equal(("Created", 1L), ("Updated", 2L), ("Updated", 3L));
        var secondActivity = saves[2].ActivityId;
        (await db.AuditEntries.AsNoTracking().Where(a => a.ActivityId == secondActivity).Select(a => a.AggregateType).ToListAsync(ct))
            .Should().BeEquivalentTo([nameof(CollectionInstance), nameof(Observation), nameof(Observation)], "the unchanged value is not written");
        (await db.DomainEvents.AsNoTracking().Where(e => e.ActivityId == secondActivity).Select(e => e.Type).ToListAsync(ct))
            .Should().BeEquivalentTo("ObservationCorrected", "ObservationRecorded", "CollectedValuesSaved");
        (await db.ProvenanceActivities.AsNoTracking().SingleAsync(p => p.Id == secondActivity, ct)).Outcome.Should().Be("Committed");

        var versionId = form.Version.Value;
        var governed = await db.LineageEdges.AsNoTracking()
            .Where(e => e.Kind == LineageEdgeKind.GovernedBy && e.FromId == versionId && e.ToKind == NodeKinds.Observation)
            .Select(e => new { e.ToId, e.ToVersion })
            .ToListAsync(ct);
        governed.Select(e => (e.ToId, e.ToVersion)).Should().BeEquivalentTo(
            [
                (first.Values[0].ObservationId.Value, 1L), (first.Values[1].ObservationId.Value, 1L),
                (second.Values[2].ObservationId.Value, 2L), (second.Values[0].ObservationId.Value, 1L),
            ],
            "every observation version a save writes is governed by the form's protocol version");
    }

    /// <summary>
    /// K-TX-2 for a page save: a failure at any stage of the commit leaves the form, its values, the audit chain, the
    /// events and the outbox exactly as they were.
    /// </summary>
    [Theory(DisplayName = "Save_FailingAtAnyCommitStage_ChangesNothing")]
    [MemberData(nameof(CommitStages))]
    public async Task SaveFailingAtAnyCommitStageChangesNothing(string stageName)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182))), ct);
        var before = await CountsAsync(form, ct);
        var stage = Enum.Parse<CommitStage>(stageName);
        fixture.UnitOfWork.FaultInjector = s =>
        {
            if (s == stage)
            {
                throw new InvalidOperationException($"Injected fault at {stage}.");
            }
        };

        var act = () => fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128), 1), Value(Diastolic, Mmhg(91))), cancellationToken: ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"Injected fault at {stage}.");
        fixture.UnitOfWork.FaultInjector = null;
        (await CountsAsync(form, ct)).Should().Be(before);
        var record = await fixture.CollectionInstanceRecords.FindAsync(form.Subject.Tenant, form.Instance, ct);
        record!.Version.Should().Be(2);
        record.Values.Select(v => (v.ItemId, v.Value, v.Revision)).Should().Equal((Systolic, (ObservationValue)Mmhg(182), 1));
    }

    /// <summary>
    /// One stale value refuses the whole page with 409 and an error naming that item, and writes nothing, so a user who
    /// saved from a stale screen never overwrites a value they did not see, even alongside a value that was current.
    /// </summary>
    [Fact(DisplayName = "Save_WithOneStaleRevision_IsRefusedWholeAndWritesNothing")]
    [KernelRule("K-FACT-6")]
    public async Task SaveWithOneStaleRevisionIsRefusedWholeAndWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(91))), ct);
        await _forms.Protocol.OkAsync(Save(form, Value(Diastolic, Mmhg(92), 1)), ct);
        var before = await CountsAsync(form, ct);

        var stale = await fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128), 1), Value(Diastolic, Mmhg(93), 1)), cancellationToken: ct);

        stale.Error!.Code.Should().Be(CollectionErrors.RevisionStale.Code);
        stale.Error.HttpStatus.Should().Be(409);
        stale.Error.Errors.Should().ContainKey(Diastolic).And.HaveCount(1);
        (await fixture.Sender.SendAsync(Save(form, Value(Comment, new TextValue("late")), Value(Systolic, Mmhg(1), 0)), cancellationToken: ct))
            .Error!.Code.Should().Be(CollectionErrors.RevisionStale.Code, "an item that has a value is not new");
        (await CountsAsync(form, ct)).Should().Be(before);
    }

    /// <summary>
    /// Two users save different items of one form at once: both commit, one after the other on the form's advisory lock,
    /// and the form ends with both values and one version per save.
    /// </summary>
    [Fact(DisplayName = "ConcurrentSaves_OfDifferentItems_BothCommit")]
    public async Task ConcurrentSavesOfDifferentItemsBothCommit()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);

        var results = await Task.WhenAll(
            fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128))), cancellationToken: ct),
            fixture.Sender.SendAsync(Save(form, Value(Diastolic, Mmhg(81))), cancellationToken: ct));

        results.Should().OnlyContain(r => r.IsSuccess);
        results.Select(r => r.Value!.Version).Should().BeEquivalentTo([2L, 3L]);
        var record = await fixture.CollectionInstanceRecords.FindAsync(form.Subject.Tenant, form.Instance, ct);
        record!.Version.Should().Be(3);
        record.Values.Select(v => v.ItemId).Should().BeEquivalentTo(Systolic, Diastolic);
    }

    /// <summary>
    /// Two users save the same item from the same revision at once: exactly one commits and the other is refused with
    /// 409, because the second reads the revision the first committed.
    /// </summary>
    [Fact(DisplayName = "ConcurrentSaves_OfOneItem_OneCommitsAndOneIsStale")]
    public async Task ConcurrentSavesOfOneItemOneCommitsAndOneIsStale()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182))), ct);

        var results = await Task.WhenAll(
            fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128), 1)), cancellationToken: ct),
            fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(138), 1)), cancellationToken: ct));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Single(r => !r.IsSuccess).Error!.HttpStatus.Should().Be(409);
        results.Single(r => !r.IsSuccess).Error!.Code.Should().Be(CollectionErrors.RevisionStale.Code);
        var record = await fixture.CollectionInstanceRecords.FindAsync(form.Subject.Tenant, form.Instance, ct);
        record!.Values.Single().Revision.Should().Be(2);
    }

    /// <summary>
    /// The lock-versus-save race at each scope that covers a form value (ADR-2621, ADR-2622), made deterministic: a save
    /// held inside its commit makes a lock wait, which then commits after it, and the next save is refused with 423; a
    /// lock held inside its commit makes a save wait, which is then refused. No value is written after the lock.
    /// </summary>
    [Theory(DisplayName = "LockAndSave_Race_NoValueCommitsAfterTheLock")]
    [MemberData(nameof(LockScopeKinds))]
    public async Task LockAndSaveRaceNoValueCommitsAfterTheLock(string scopeKind)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        // 1. The save is mid-commit when the lock arrives.
        var form = await _forms.NewFormAsync(ct);
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182))), ct);
        var lockCommand = await _forms.Observations.LockAsync(form.Subject.Tenant, form.Subject.Study, form.Scope(scopeKind, saved.Values[0].ObservationId), ct);
        using (var paused = new KernelCommitPause(fixture))
        {
            var save = paused.HoldAsync(() => fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128), 1)), cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var locking = fixture.Sender.SendAsync(lockCommand, cancellationToken: ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            locking.IsCompleted.Should().BeFalse("the lock waits for the save's shared scope lock");
            paused.Release();

            (await save).IsSuccess.Should().BeTrue();
            (await locking).IsSuccess.Should().BeTrue();
        }

        var after = await fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(138), 2)), cancellationToken: ct);
        after.Error!.Code.Should().Be(ObservationErrors.Locked.Code);
        after.Error.HttpStatus.Should().Be(423);
        await AssertNothingWrittenAfterLockAsync(form, lockCommand.AssertionId, expectedSaves: 2, ct);
        (await fixture.CollectionInstanceRecords.FindAsync(form.Subject.Tenant, form.Instance, ct))!.Locks
            .Should().ContainSingle().Which.LockId.Should().Be(lockCommand.AssertionId);

        // 2. The lock is mid-commit when the save arrives.
        form = await _forms.NewFormAsync(ct);
        saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182))), ct);
        lockCommand = await _forms.Observations.LockAsync(form.Subject.Tenant, form.Subject.Study, form.Scope(scopeKind, saved.Values[0].ObservationId), ct);
        using (var paused = new KernelCommitPause(fixture))
        {
            var locking = paused.HoldAsync(() => fixture.Sender.SendAsync(lockCommand, cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var save = fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128), 1), Value(Diastolic, Mmhg(81))), cancellationToken: ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            save.IsCompleted.Should().BeFalse("the save waits for the lock's exclusive scope lock");
            paused.Release();

            (await locking).IsSuccess.Should().BeTrue();
            (await save).Error!.Code.Should().Be(ObservationErrors.Locked.Code);
        }

        await AssertNothingWrittenAfterLockAsync(form, lockCommand.AssertionId, expectedSaves: 1, ct);
    }

    /// <summary>
    /// A lock on the form also covers its values when they are corrected or queried one at a time, outside a page save.
    /// </summary>
    [Fact(DisplayName = "FormLock_CoversItsValues_OutsideAPageSave")]
    public async Task FormLockCoversItsValuesOutsideAPageSave()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182))), ct);
        var observation = saved.Values[0].ObservationId;
        await _forms.Protocol.OkAsync(await _forms.Observations.LockAsync(form.Subject.Tenant, form.Subject.Study, NodeRef.For(form.Instance), ct), ct);

        var correction = await fixture.Sender.SendAsync(
            new CorrectObservation(form.Subject.Tenant, observation, form.Subject.Enrollment, Mmhg(128), 1, KernelProtocolScenario.Key(), "Transcription error"),
            cancellationToken: ct);
        var query = await fixture.Sender.SendAsync(
            new RaiseQuery(form.Subject.Tenant, AssertionId.New(), form.Subject.Enrollment, observation, "Please confirm the value.", KernelProtocolScenario.Key(), "Data review"),
            cancellationToken: ct);

        correction.Error!.Code.Should().Be(ObservationErrors.Locked.Code);
        query.Error!.Code.Should().Be(AssertionErrors.Locked.Code);
        (await fixture.ObservationRecords.FindAsync(form.Subject.Tenant, observation, ct))!.Locks
            .Should().ContainSingle().Which.Scope.Should().Be(NodeRef.For(form.Instance));
    }

    /// <summary>Sending the same save again with its idempotency key returns the stored result and writes nothing.</summary>
    [Fact(DisplayName = "Save_Replayed_ReturnsTheStoredResult")]
    public async Task SaveReplayedReturnsTheStoredResult()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var command = Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(91)));
        var first = await _forms.Protocol.OkAsync(command, ct);
        var before = await CountsAsync(form, ct);

        var replayed = await _forms.Protocol.OkAsync(command, ct);

        replayed.Should().BeEquivalentTo(first);
        (await CountsAsync(form, ct)).Should().Be(before);
    }

    /// <summary>
    /// Values that do not fit the form's CollectionSpec refuse the page with 422 and an error per item: an item the spec
    /// lacks, a quantity in the wrong unit, a value of the wrong kind; a malformed save (no values, an item named twice)
    /// is refused by the validator before the handler runs. Nothing is written.
    /// </summary>
    [Fact(DisplayName = "Save_ValuesThatDoNotFitTheSpec_AreRefusedPerItem")]
    public async Task SaveValuesThatDoNotFitTheSpecAreRefusedPerItem()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var before = await CountsAsync(form, ct);

        var invalid = await fixture.Sender.SendAsync(
            Save(form, Value(Systolic, new QuantityValue(17, "kPa")), Value(Diastolic, new TextValue("high")), Value("item.weight", Mmhg(80))),
            cancellationToken: ct);
        var empty = await fixture.Sender.SendAsync(Save(form), cancellationToken: ct);
        var twice = await fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(120)), Value(Systolic, Mmhg(121))), cancellationToken: ct);
        var elsewhere = await fixture.Sender.SendAsync(
            Save(form, Value(Systolic, Mmhg(120))) with { EnrollmentId = (await _forms.NewFormAsync(ct)).Subject.Enrollment }, cancellationToken: ct);

        invalid.Error!.Code.Should().Be(CollectionErrors.ValuesInvalid.Code);
        invalid.Error.HttpStatus.Should().Be(422);
        invalid.Error.Errors!.Keys.Should().BeEquivalentTo(Systolic, Diastolic, "item.weight");
        empty.Error!.Code.Should().Be(KernelErrors.CommandInvalid.Code);
        twice.Error!.Code.Should().Be(KernelErrors.CommandInvalid.Code);
        elsewhere.Error.Should().Be(KernelErrors.NotFound, "a form of another enrollment is reported as missing");
        (await CountsAsync(form, ct)).Should().Be(before);
    }

    /// <summary>Saves one value on a new form in a new tenant, for tests of the form tables.</summary>
    internal static async Task SeedAsync(KernelPostgresFixture fixture, CancellationToken ct)
    {
        var forms = new KernelCollectionScenario(fixture);
        var form = await forms.NewFormAsync(ct);
        await forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120))), ct);
    }

    private DateTimeOffset Tick() => _forms.Tick();

    private Task<CommandResult<CollectionInstanceState>> SendAsync(OpenCollectionInstance command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private async Task AssertNothingWrittenAfterLockAsync(Form form, AssertionId lockId, int expectedSaves, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(form.Subject.Tenant);
        var lockKey = lockId.ToString();
        var lockSequence = (await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.AggregateType == nameof(Assertion) && a.AggregateId == lockKey, ct)).Sequence;
        var instanceKey = form.Instance.ToString();
        var saves = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(CollectionInstance) && a.AggregateId == instanceKey && a.Action == "Updated")
            .ToListAsync(ct);
        saves.Should().HaveCount(expectedSaves);
        saves.Should().OnlyContain(s => s.Sequence < lockSequence, "no value commits after the lock");
        (await db.AuditEntries.AsNoTracking().CountAsync(a => a.AggregateType == nameof(Observation) && a.Sequence > lockSequence, ct))
            .Should().Be(0);
    }

    private async Task<(int Instances, int Members, int Revisions, int Audit, int Events, int Outbox)> CountsAsync(Form form, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(form.Subject.Tenant);
        return (
            await db.CollectionInstances.CountAsync(ct),
            await db.Set<CollectionInstanceMember>().CountAsync(ct),
            await db.ObservationRevisions.CountAsync(ct),
            await db.AuditEntries.CountAsync(ct),
            await db.DomainEvents.CountAsync(ct),
            await db.OutboxMessages.CountAsync(ct));
    }
}
