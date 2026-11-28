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
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;

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
    /// records <see cref="LineageEdgeKind.CollectedBy"/> edges from its governing CollectionSpec and PlannedActivity
    /// <c>protocol_element</c> nodes (or from CollectionSpec only when unscheduled). It is refused before any version is
    /// effective, for a CollectionSpec the version lacks or a visit that does not collect it, for an unknown enrollment
    /// and for a time in the future.
    /// </summary>
    [Fact(DisplayName = "Open_ResolvesTheGoverningVersion_AndRefusesWhatItDoesNotGovern")]
    [GxPRequirement("PRD-CRF-012")]
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
        var unscheduled = await _forms.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), Tick(), visit: null), ct);

        opened.Should().BeEquivalentTo(new CollectionInstanceState(opened.CollectionInstanceId, subject.Enrollment, version, SpecId, VisitId, collectedAt, 1));
        opened.VisitTimingAssessment.Should().BeNull("a screening enrollment without an Enrolled period has no reference start");
        await using var db = fixture.CreateContext(tenant);
        (await db.LineageEdges.AsNoTracking().CountAsync(
            e => e.Kind == LineageEdgeKind.GovernedBy && e.ToKind == NodeKinds.CollectionInstance, ct))
            .Should().Be(0);
        var specElement = ProtocolElementId.For(study, SpecId).Value;
        var visitElement = ProtocolElementId.For(study, VisitId).Value;
        (await db.LineageEdges.AsNoTracking()
            .Where(e => e.ToKind == NodeKinds.CollectionInstance && e.ToId == opened.CollectionInstanceId.Value)
            .Select(e => new { e.FromKind, e.FromId, e.FromVersion, e.Kind })
            .ToListAsync(ct))
            .Should().BeEquivalentTo(
            [
                new { FromKind = NodeKinds.ProtocolElement, FromId = specElement, FromVersion = 1L, Kind = LineageEdgeKind.CollectedBy },
                new { FromKind = NodeKinds.ProtocolElement, FromId = visitElement, FromVersion = 1L, Kind = LineageEdgeKind.CollectedBy },
            ]);
        (await db.LineageEdges.AsNoTracking()
            .Where(e => e.ToKind == NodeKinds.CollectionInstance && e.ToId == unscheduled.CollectionInstanceId.Value)
            .Select(e => new { e.FromKind, e.FromId, e.FromVersion, e.Kind })
            .ToListAsync(ct))
            .Should().BeEquivalentTo(
            [
                new { FromKind = NodeKinds.ProtocolElement, FromId = specElement, FromVersion = 1L, Kind = LineageEdgeKind.CollectedBy },
            ]);
        (await SendAsync(Open(subject, opened.CollectionInstanceId, Tick()), ct)).Error.Should().Be(KernelErrors.AlreadyExists);
    }

    /// <summary>
    /// Opening a timed visit for an enrolled participant returns an optional <see cref="VisitTimingAssessment"/> derived
    /// purely from <c>CollectedAt</c> and the <c>Enrolled</c> period start (ADR-2487, ADR-2507, K-TIME-3, K-TIME-7),
    /// normalizing legacy Day 0 timings to StudyDay 1, preserving the assessment on idempotent replay, never blocking
    /// early or late capture, and returning <c>null</c> for unscheduled forms, untimed activities and screening subjects.
    /// </summary>
    [Fact(DisplayName = "Open_EvaluatesVisitTimingForEnrolledParticipants_WithoutBlockingOrPersistingSchedule")]
    [KernelRule("K-TIME-3")]
    [KernelRule("K-TIME-7")]
    public async Task OpenEvaluatesVisitTimingForEnrolledParticipantsWithoutBlockingOrPersistingSchedule()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _forms.Protocol.NewStudyAsync(ct);
        var systolic = await _forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic BP.", KernelProtocolScenario.Key(), "Test concept"),
            ct);
        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity("act.baseline", "Day 0 Baseline", "ep.treatment", [ActivityLocation.Site], [SpecId], new ActivityTiming(0, 0, 1)),
                new PlannedActivity(VisitId, "Week 12 visit", "ep.treatment", [ActivityLocation.Site], [SpecId], new ActivityTiming(84, 3, 3)),
                new PlannedActivity("act.ae", "Adverse event follow-up", "ep.treatment", [ActivityLocation.Site], [SpecId], Timing: null),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(SpecId, "Vital signs", [new CollectionItem(Systolic, systolic, "mm[Hg]", Required: true)], Repeating: true),
            ],
        };
        var version = ProtocolVersionId.New();
        (await _forms.Protocol.CreateDraftAsync(tenant, study, version, ct, ProtocolDesignDocument.Write(design))).IsSuccess.Should().BeTrue();
        var inReview = await _forms.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await _forms.Protocol.OkAsync(_forms.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _forms.Protocol.OkAsync(new ApproveProtocolVersion(tenant, version, signature, KernelProtocolScenario.Key(), "Approved"), ct);
        var effectivity = EffectivityId.New();
        await _forms.Protocol.OkAsync(new DeclareEffectivity(tenant, effectivity, version, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = System.Text.Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsVersion = ArtifactVersionId.New();
        await _forms.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethicsVersion,
                ArtifactKind.EthicsApproval,
                study,
                "Ethics approval",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://ethics/timing"),
                KernelProtocolScenario.Key(),
                "Filed"),
            ct);
        await _forms.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Effective"),
            ct);

        var studySite = await _forms.Observations.AddStudySiteAsync(tenant, study, ct);
        var screeningSubject = new KernelObservationScenario.Subject(tenant, study, studySite, await _forms.Observations.ScreenAsync(tenant, studySite, ct));
        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, await _forms.Observations.ScreenAsync(tenant, studySite, ct));

        var enrolledAt = new DateTimeOffset(DateOnly.FromDateTime(Tick().UtcDateTime).AddDays(1), new TimeOnly(9, 30), TimeSpan.Zero);
        fixture.Clock.Set(enrolledAt.AddHours(3));
        await _forms.Protocol.OkAsync(
            new ChangeEnrollmentStatus(tenant, subject.Enrollment, EnrollmentStatus.Enrolled, enrolledAt, KernelProtocolScenario.Key(), "Eligible and enrolled"),
            ct);
        Tick();
        await _forms.Protocol.OkAsync(
            new ChangeEnrollmentStatus(tenant, subject.Enrollment, EnrollmentStatus.Randomized, enrolledAt.AddHours(1), KernelProtocolScenario.Key(), "Randomized"),
            ct);
        Tick();
        await _forms.Protocol.OkAsync(
            new ChangeEnrollmentStatus(tenant, subject.Enrollment, EnrollmentStatus.Active, enrolledAt.AddHours(2), KernelProtocolScenario.Key(), "First dose"),
            ct);

        var day1 = DateOnly.FromDateTime(enrolledAt.UtcDateTime);
        fixture.Clock.Advance(TimeSpan.FromDays(95));

        // Legacy raw Day 0 baseline visit normalizes to SDTM StudyDay 1 (target == day1).
        var baselineOpened = await _forms.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), enrolledAt, visit: "act.baseline"), ct);
        baselineOpened.VisitTimingAssessment.Should().Be(new VisitTimingAssessment(
            new ExpectedVisit(
                new PlannedVisit("act.baseline", "Day 0 Baseline", "ep.treatment", 1, 1, 0, 1),
                day1,
                day1,
                day1.AddDays(1),
                VisitWindowStatus.Open),
            day1,
            0,
            VisitTiming.InWindow,
            0));

        // Timed Week 12 visit (StudyDay 84 -> offset 83 days, window ±3): Early, InWindow (with replay), and Late all succeed without blocking capture.
        var targetWeek12 = day1.AddDays(83);
        var expectedWeek12 = new PlannedVisit(VisitId, "Week 12 visit", "ep.treatment", 1, 84, 3, 3);

        var earlyAt = new DateTimeOffset(targetWeek12.AddDays(-5), new TimeOnly(10, 0), TimeSpan.Zero);
        var earlyOpened = await _forms.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), earlyAt), ct);
        earlyOpened.VisitTimingAssessment.Should().Be(new VisitTimingAssessment(
            new ExpectedVisit(expectedWeek12, targetWeek12, targetWeek12.AddDays(-3), targetWeek12.AddDays(3), VisitWindowStatus.NotYetOpen),
            targetWeek12.AddDays(-5),
            -5,
            VisitTiming.Early,
            2));

        var inWindowAt = new DateTimeOffset(targetWeek12.AddDays(2), new TimeOnly(14, 15), TimeSpan.Zero);
        var replayCommand = Open(subject, CollectionInstanceId.New(), inWindowAt);
        var inWindowOpened = await _forms.Protocol.OkAsync(replayCommand, ct);
        var replayed = await SendAsync(replayCommand, ct);
        inWindowOpened.VisitTimingAssessment.Should().Be(new VisitTimingAssessment(
            new ExpectedVisit(expectedWeek12, targetWeek12, targetWeek12.AddDays(-3), targetWeek12.AddDays(3), VisitWindowStatus.Open),
            targetWeek12.AddDays(2),
            2,
            VisitTiming.InWindow,
            0));
        replayed.Replayed.Should().BeTrue();
        replayed.Value.Should().BeEquivalentTo(inWindowOpened);

        var lateAt = new DateTimeOffset(targetWeek12.AddDays(7), new TimeOnly(16, 45), TimeSpan.Zero);
        var lateOpened = await _forms.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), lateAt), ct);
        lateOpened.VisitTimingAssessment.Should().Be(new VisitTimingAssessment(
            new ExpectedVisit(expectedWeek12, targetWeek12, targetWeek12.AddDays(-3), targetWeek12.AddDays(3), VisitWindowStatus.Closed),
            targetWeek12.AddDays(7),
            7,
            VisitTiming.Late,
            4));

        // Unscheduled forms, untimed event-driven activities, and screening enrollments return null VisitTimingAssessment.
        (await _forms.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), inWindowAt, visit: null), ct))
            .VisitTimingAssessment.Should().BeNull();
        (await _forms.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), inWindowAt, visit: "act.ae"), ct))
            .VisitTimingAssessment.Should().BeNull();
        (await _forms.Protocol.OkAsync(Open(screeningSubject, CollectionInstanceId.New(), inWindowAt), ct))
            .VisitTimingAssessment.Should().BeNull();
    }

    /// <summary>
    /// A first page records two values and a second records a third and corrects one, leaving one unchanged: each save
    /// is one activity that writes the form, its changed observations, their audit links and events together; every new
    /// observation version has a <see cref="LineageEdgeKind.CollectedBy"/> edge from its governing CollectionItem
    /// <c>protocol_element</c> node only; and the result and the form read return every value at its current revision,
    /// with the specification's items in their order.
    /// </summary>
    [Fact(DisplayName = "Save_RecordsAndCorrectsAPage_AsOneActivity")]
    [KernelRule("K-FACT-6")]
    [GxPRequirement("PRD-SYS-001")]
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
        record.Items.Select(i => (i.Id, i.Required)).Should().Equal((Systolic, true), (Diastolic, true), (Comment, false));

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

        var study = form.Subject.Study;
        (await db.LineageEdges.AsNoTracking().CountAsync(
            e => e.Kind == LineageEdgeKind.GovernedBy && (e.ToKind == NodeKinds.CollectionInstance || e.ToKind == NodeKinds.Observation), ct))
            .Should().Be(0);
        var collectedObservations = await db.LineageEdges.AsNoTracking()
            .Where(e => e.Kind == LineageEdgeKind.CollectedBy && e.ToKind == NodeKinds.Observation)
            .Select(e => new { e.FromId, e.FromVersion, e.ToId, e.ToVersion })
            .ToListAsync(ct);
        collectedObservations.Select(e => (e.FromId, e.FromVersion, e.ToId, e.ToVersion)).Should().BeEquivalentTo(
            [
                (ProtocolElementId.For(study, Diastolic).Value, 1L, first.Values[0].ObservationId.Value, 1L),
                (ProtocolElementId.For(study, Systolic).Value, 1L, first.Values[1].ObservationId.Value, 1L),
                (ProtocolElementId.For(study, Comment).Value, 1L, second.Values[0].ObservationId.Value, 1L),
                (ProtocolElementId.For(study, Systolic).Value, 1L, second.Values[2].ObservationId.Value, 2L),
            ],
            "each observation version a save writes is collected only by its own CollectionItem protocol_element");
        var collectedInstance = await db.LineageEdges.AsNoTracking()
            .Where(e => e.Kind == LineageEdgeKind.CollectedBy && e.ToKind == NodeKinds.CollectionInstance && e.ToId == form.Instance.Value)
            .Select(e => new { e.FromId, e.FromVersion, e.ToVersion })
            .ToListAsync(ct);
        collectedInstance.Select(e => (e.FromId, e.FromVersion, e.ToVersion)).Should().BeEquivalentTo(
            [
                (ProtocolElementId.For(study, SpecId).Value, 1L, 1L),
                (ProtocolElementId.For(study, VisitId).Value, 1L, 1L),
                (ProtocolElementId.For(study, SpecId).Value, 1L, 2L),
                (ProtocolElementId.For(study, VisitId).Value, 1L, 2L),
                (ProtocolElementId.For(study, SpecId).Value, 1L, 3L),
                (ProtocolElementId.For(study, VisitId).Value, 1L, 3L),
            ]);
    }

    /// <summary>
    /// K-TX-2 for a page save: a failure at any stage of the commit leaves the form, its values, the audit chain, the
    /// events and the outbox exactly as they were.
    /// </summary>
    [Theory(DisplayName = "Save_FailingAtAnyCommitStage_ChangesNothing")]
    [MemberData(nameof(CommitStages))]
    [GxPRequirement("PRD-EVT-001")]
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
    [GxPRequirement("PRD-CRF-012")]
    public async Task LockAndSaveRaceNoValueCommitsAfterTheLock(string scopeKind)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        // 1. The save is mid-commit when the lock arrives (Frozen commits after it; Locked rejects unverified revision 2 / superseded signature with ScopeNotReady).
        var form = await _forms.NewFormAsync(ct);
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(80))), ct);
        var systolicObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId;
        var lockCommand = await _forms.Observations.LockAsync(form.Subject.Tenant, form.Subject.Study, form.Scope(scopeKind, systolicObs), ct, LockKind.Frozen);
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

        // 1b. A Locked assertion arriving while a save is mid-commit waits on the shared lock and then fails with 409 ScopeNotReady.
        form = await _forms.NewFormAsync(ct);
        saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(80))), ct);
        await _forms.VerifyAndSignAsync(form, ct);
        systolicObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId;
        var hardLockRace = await _forms.Observations.LockAsync(form.Subject.Tenant, form.Subject.Study, form.Scope(scopeKind, systolicObs), ct, LockKind.Locked);
        using (var paused = new KernelCommitPause(fixture))
        {
            var save = paused.HoldAsync(() => fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128), 1)), cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var locking = fixture.Sender.SendAsync(hardLockRace, cancellationToken: ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            locking.IsCompleted.Should().BeFalse("the hard lock waits for the save's shared scope lock");
            paused.Release();

            (await save).IsSuccess.Should().BeTrue();
            (await locking).Error!.Code.Should().Be(AssertionErrors.ScopeNotReady.Code);
        }

        // 2. The lock is mid-commit when the save arrives.
        form = await _forms.NewFormAsync(ct);
        saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(80))), ct);
        await _forms.VerifyAndSignAsync(form, ct);
        systolicObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId;
        lockCommand = await _forms.Observations.LockAsync(form.Subject.Tenant, form.Subject.Study, form.Scope(scopeKind, systolicObs), ct);
        using (var paused = new KernelCommitPause(fixture))
        {
            var locking = paused.HoldAsync(() => fixture.Sender.SendAsync(lockCommand, cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var save = fixture.Sender.SendAsync(Save(form, Value(Systolic, Mmhg(128), 1), Value(Diastolic, Mmhg(81), 1)), cancellationToken: ct);
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
    [GxPRequirement("PRD-CRF-012")]
    public async Task FormLockCoversItsValuesOutsideAPageSave()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(80))), ct);
        await _forms.VerifyAndSignAsync(form, ct);
        var observation = saved.Values.Single(v => v.ItemId == Systolic).ObservationId;
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

    /// <summary>
    /// Coded items hydrate their governed codelist options on form and observation reads, validate membership against
    /// <c>IConceptRegistry.GetGroupAsync</c> (when <c>Codelist</c> is set) or <c>LookupAsync</c> (when <c>Codelist</c>
    /// is null) before commit with HTTP 422, and still permit <see cref="MissingValue"/> reasons under K-FACT-3.
    /// </summary>
    [Fact(DisplayName = "CodedItems_HydrateCodelists_ValidateMembership_AndAllowMissingValues")]
    public async Task CodedItemsHydrateCodelistsValidateMembershipAndAllowMissingValues()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (form, csVersion) = await _forms.NewCodedFormAsync(ct);
        var tenant = form.Subject.Tenant;
        var before = await CountsAsync(form, ct);

        var loaded = await fixture.CollectionInstanceRecords.FindAsync(tenant, form.Instance, ct);
        loaded.Should().NotBeNull();
        loaded!.ItemCodelists.Should().NotBeNull();
        loaded.ItemCodelists!.Keys.Should().Equal(Position);
        loaded.ItemCodelists[Position].Should().Equal(
            new CodelistOptionView("SITTING", "Sitting", "SITTING"),
            new CodelistOptionView("SUPINE", "Supine", "SUPINE"));

        var batchLoaded = await fixture.CollectionInstanceRecords.ListRecordsForEnrollmentAsync(tenant, form.Subject.Enrollment, ct);
        batchLoaded.Should().ContainSingle().Which.ItemCodelists![Position].Select(o => o.Code).Should().Equal("SITTING", "SUPINE");

        // Rejects a code that belongs to another group in the same CodeSystemVersion, and an unknown code on a Codelist-null item.
        var rejected = await fixture.Sender.SendAsync(
            Save(
                form,
                Value(Position, new CodedValue(csVersion, "Y")),
                Value(AdverseTerm, new CodedValue(csVersion, "NOT_IN_SYSTEM"))),
            cancellationToken: ct);

        rejected.Error!.Code.Should().Be(CollectionErrors.ValuesInvalid.Code);
        rejected.Error.HttpStatus.Should().Be(422);
        rejected.Error.Errors!.Keys.Should().BeEquivalentTo(Position, AdverseTerm);
        (await CountsAsync(form, ct)).Should().Be(before, "a rejected coded save writes nothing");

        // Accepts a valid codelist member and a valid code-system member (null Codelist).
        var saved = await _forms.Protocol.OkAsync(
            Save(
                form,
                Value(Systolic, Mmhg(118)),
                Value(Position, new CodedValue(csVersion, "SITTING")),
                Value(AdverseTerm, new CodedValue(csVersion, "LLT_HEADACHE"))),
            ct);
        var posObservationId = saved.Values.Single(v => v.ItemId == Position).ObservationId;

        var obsRecord = await fixture.ObservationRecords.FindAsync(tenant, posObservationId, ct);
        obsRecord.Should().NotBeNull();
        obsRecord!.ExpectedCodeSystemVersionId.Should().Be(csVersion);
        obsRecord.ExpectedCodelist.Should().Be(PositionCodelist);
        obsRecord.ExpectedCodelistOptions.Should().Equal(
            new CodelistOptionView("SITTING", "Sitting", "SITTING"),
            new CodelistOptionView("SUPINE", "Supine", "SUPINE"));

        // Observation correction rejects out-of-codelist code with 422 and accepts a valid codelist code.
        var badCorrection = await fixture.Sender.SendAsync(
            new CorrectObservation(
                tenant,
                posObservationId,
                form.Subject.Enrollment,
                new CodedValue(csVersion, "Y"),
                obsRecord.Version,
                KernelProtocolScenario.Key(),
                "Attempt invalid position code"),
            cancellationToken: ct);
        badCorrection.Error!.Code.Should().Be(CollectionErrors.ValuesInvalid.Code);
        badCorrection.Error.HttpStatus.Should().Be(422);
        badCorrection.Error.Errors!.Keys.Should().Equal(Position);

        var corrected = await _forms.Protocol.OkAsync(
            new CorrectObservation(
                tenant,
                posObservationId,
                form.Subject.Enrollment,
                new CodedValue(csVersion, "SUPINE"),
                obsRecord.Version,
                KernelProtocolScenario.Key(),
                "Corrected position from source worksheet"),
            ct);
        corrected.Revision.Should().Be(2);

        // MissingValue still bypasses codelist checks under K-FACT-3.
        var missingSaved = await _forms.Protocol.OkAsync(
            Save(form, Value(Position, new MissingValue(NullFlavor.NotDone, "Measured standing"), expectedRevision: 2)),
            ct);
        missingSaved.Values.Single(v => v.ItemId == Position).Revision.Should().Be(3);
    }

    /// <summary>
    /// Scalar headers, batch observation revisions/projections, and targeted query lock checks run with AsNoTracking,
    /// invoke <see cref="ProtocolDesignDocument.Read"/> zero times, honour active Locked assertions while ignoring Frozen,
    /// and enforce PostgreSQL Row-Level Security across tenants.
    /// </summary>
    [Fact(DisplayName = "BatchProjections_HeadersRevisionsAndQueryTargets_SkipProtocolParsesAndEnforceRls")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task BatchProjectionsHeadersRevisionsAndQueryTargetsSkipProtocolParsesAndEnforceRls()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var formA = await _forms.NewFormAsync(ct);
        var tenant = formA.Subject.Tenant;
        var formBId = (await _forms.Protocol.OkAsync(Open(formA.Subject, CollectionInstanceId.New(), Tick()), ct)).CollectionInstanceId;
        var formB = new Form(formA.Subject, formA.Version, formBId);

        var savedA1 = await _forms.Protocol.OkAsync(Save(formA, Value(Systolic, Mmhg(122)), Value(Diastolic, Mmhg(80))), ct);
        Tick();
        var savedA2 = await _forms.Protocol.OkAsync(Save(formA, Value(Systolic, Mmhg(128), expectedRevision: 1)), ct);
        var savedB1 = await _forms.Protocol.OkAsync(Save(formB, Value(Systolic, Mmhg(118))), ct);

        var sbpA = savedA2.Values.Single(v => v.ItemId == Systolic).ObservationId;
        var dbpA = savedA1.Values.Single(v => v.ItemId == Diastolic).ObservationId;
        var sbpB = savedB1.Values.Single(v => v.ItemId == Systolic).ObservationId;

        var fullA = await fixture.CollectionInstanceRecords.FindAsync(tenant, formA.Instance, ct);
        fullA.Should().NotBeNull();

        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            var singleHeader = await fixture.CollectionInstanceRecords.FindHeaderAsync(tenant, formA.Instance, ct);
            singleHeader.Should().Be(CollectionInstanceHeader.From(fullA!));

            var batchHeaders = await fixture.CollectionInstanceRecords.FindHeadersAsync(
                tenant, [formA.Instance, formB.Instance, CollectionInstanceId.New()], ct);
            batchHeaders.Keys.Should().BeEquivalentTo([formA.Instance, formB.Instance]);
            batchHeaders[formB.Instance].CollectionSpecId.Should().Be(SpecId);

            var revisions = await fixture.CollectionInstanceRecords.FindObservationRevisionsAsync(
                tenant, [sbpA, dbpA, sbpB, ObservationId.New()], cancellationToken: ct);
            revisions.Should().HaveCount(3);
            revisions[sbpA].Should().Be(2);
            revisions[dbpA].Should().Be(1);
            revisions[sbpB].Should().Be(1);

            var projections = await fixture.CollectionInstanceRecords.FindObservationProjectionsAsync(
                tenant, [sbpA, dbpA, sbpB], [formA.Instance, formB.Instance], ct);
            projections[sbpA].Should().Be(new CollectionObservationProjection(2, false));
            projections[dbpA].Should().Be(new CollectionObservationProjection(1, false));
            projections[sbpB].Should().Be(new CollectionObservationProjection(1, false));

            var targetUnlocked = await fixture.CollectionInstanceRecords.FindQueryTargetAsync(
                tenant, formA.Instance, observation: sbpA, checkLocks: true, cancellationToken: ct);
            targetUnlocked.Should().NotBeNull();
            targetUnlocked!.IsLocked.Should().BeFalse();
            targetUnlocked.ItemId.Should().Be(Systolic);
            targetUnlocked.ObservationId.Should().Be(sbpA);
            targetUnlocked.Revision.Should().Be(2);

            var blankTarget = await fixture.CollectionInstanceRecords.FindQueryTargetAsync(
                tenant, formA.Instance, itemId: Comment, checkLocks: true, cancellationToken: ct);
            blankTarget.Should().NotBeNull();
            blankTarget!.ObservationId.Should().BeNull();
            blankTarget.Revision.Should().BeNull();

            reads.Count.Should().Be(0, "header, batch revision, and query-target projections never parse ProtocolDesign JSON");
        }

        // Frozen does not mark CollectionQueryTarget.IsLocked, whereas Locked at covering scope does.
        var frozen = await _forms.Protocol.OkAsync(
            await _forms.Observations.LockAsync(tenant, formA.Subject.Study, NodeRef.For(formA.Subject.Enrollment), ct, LockKind.Frozen), ct);
        (await fixture.CollectionInstanceRecords.FindQueryTargetAsync(tenant, formA.Instance, observation: sbpA, cancellationToken: ct))!
            .IsLocked.Should().BeFalse();
        await _forms.Observations.OkReleaseAsync(tenant, formA.Subject.Study, frozen.LockId, ct);

        await _forms.Protocol.OkAsync(Save(formB, Value(Diastolic, Mmhg(76))), ct);
        await _forms.VerifyAndSignAsync(formA, ct);
        await _forms.VerifyAndSignAsync(formB, ct);
        await _forms.Protocol.OkAsync(
            await _forms.Observations.LockAsync(tenant, formA.Subject.Study, NodeRef.For(formA.Subject.Enrollment), ct, LockKind.Locked), ct);
        (await fixture.CollectionInstanceRecords.FindQueryTargetAsync(tenant, formA.Instance, observation: sbpA, cancellationToken: ct))!
            .IsLocked.Should().BeTrue();

        // Tenant isolation under PostgreSQL RLS: another tenant sees nothing.
        var (otherTenant, _) = await _forms.Protocol.NewStudyAsync(ct);
        (await fixture.CollectionInstanceRecords.FindHeaderAsync(otherTenant, formA.Instance, ct)).Should().BeNull();
        (await fixture.CollectionInstanceRecords.FindHeadersAsync(otherTenant, [formA.Instance, formB.Instance], ct)).Should().BeEmpty();
        (await fixture.CollectionInstanceRecords.FindObservationRevisionsAsync(otherTenant, [sbpA, dbpA], cancellationToken: ct)).Should().BeEmpty();
        (await fixture.CollectionInstanceRecords.FindObservationProjectionsAsync(otherTenant, [sbpA, dbpA], [formA.Instance], ct)).Should().BeEmpty();
        (await fixture.CollectionInstanceRecords.FindQueryTargetAsync(otherTenant, formA.Instance, observation: sbpA, cancellationToken: ct)).Should().BeNull();
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
