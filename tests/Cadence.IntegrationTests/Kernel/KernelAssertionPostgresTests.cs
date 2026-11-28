using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Assertions and scoped locks on PostgreSQL (spec §8.2, §10.5, ADR-2621), as the application role under Row-Level
/// Security: a signed lock at a study, study site, enrollment or observation refuses recording and correcting within its
/// scope until a signed release; verifications name the revision they judged; queries are answered and closed by
/// retraction; and nothing about an observation changes when something is asserted about it. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelAssertionPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelObservationScenario _observations = new(fixture);

    private static QuantityValue Mmhg(decimal value) => KernelObservationScenario.Mmhg(value);

    /// <summary>
    /// Database lock: a <see cref="LockKind.Locked"/> lock at study scope refuses recording a new observation and
    /// correcting an existing one with 423. Releasing it with a reason and a signature allows both again, and the
    /// released lock stays on record.
    /// </summary>
    [Fact(DisplayName = "StudyLock_RefusesRecordAndCorrect_UntilItIsReleased")]
    public async Task StudyLockRefusesRecordAndCorrectUntilItIsReleased()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var recorded = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(182), ct);
        await _observations.Protocol.OkAsync(Verify(subject, recorded.ObservationId, 1), ct);

        var locked = await _observations.Protocol.OkAsync(await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(subject.Study), ct), ct);

        locked.Released.Should().BeFalse();
        var record = await fixture.Sender.SendAsync(_observations.Record(subject.Tenant, subject.Enrollment, Mmhg(120)), cancellationToken: ct);
        record.Error!.Code.Should().Be(ObservationErrors.Locked.Code);
        record.Error.HttpStatus.Should().Be(423);
        (await fixture.Sender.SendAsync(KernelObservationScenario.Correct(subject.Tenant, recorded, Mmhg(128)), cancellationToken: ct)).Error!.Code
            .Should().Be(ObservationErrors.Locked.Code);
        var view = await fixture.ObservationRecords.FindAsync(subject.Tenant, recorded.ObservationId, ct);
        view!.Locks.Should().ContainSingle().Which.Should().Match<LockView>(l =>
            l.LockId == locked.LockId && l.Kind == LockKind.Locked && l.Scope == NodeRef.For(subject.Study) && l.AttestationId == locked.AttestationId);

        (await fixture.Sender.SendAsync(await _observations.ReleaseAsync(subject.Tenant, subject.Study, locked.LockId, ct), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.SelfReleaseDenied.Code);
        var released = await _observations.OkReleaseAsync(subject.Tenant, subject.Study, locked.LockId, ct);

        released.Released.Should().BeTrue();
        (await _observations.Protocol.OkAsync(KernelObservationScenario.Correct(subject.Tenant, recorded, Mmhg(128)), ct)).Revision.Should().Be(2);
        await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(120), ct);
        (await fixture.ObservationRecords.FindAsync(subject.Tenant, recorded.ObservationId, ct))!.Locks.Should().BeEmpty();
        await using var db = fixture.CreateContext(subject.Tenant);
        var stored = await db.Assertions.AsNoTracking()
            .Where(a => a.StudyId == subject.Study && a.Kind == AssertionKind.Locked)
            .OrderBy(a => a.AssertedAt)
            .ToListAsync(ct);
        stored.Select(a => (a.Kind, a.RetractsId)).Should().Equal((AssertionKind.Locked, (AssertionId?)null), (AssertionKind.Locked, locked.LockId));
        var events = await db.DomainEvents.AsNoTracking().Where(e => e.Type == "LockApplied" || e.Type == "LockReleased").Select(e => e.Type).ToListAsync(ct);
        events.Should().BeEquivalentTo("LockApplied", "LockReleased");
        (await db.LineageEdges.AsNoTracking()
            .Where(e => e.ToKind == NodeKinds.Observation && e.ToId == recorded.ObservationId.Value && e.ToVersion == 1)
            .Select(e => new { e.FromKind, e.FromId, e.FromVersion, e.Kind })
            .ToListAsync(ct))
            .Should().BeEquivalentTo(
            [
                new { FromKind = NodeKinds.Concept, FromId = recorded.Concept.ConceptId.Value, FromVersion = (long)recorded.Concept.Version, Kind = LineageEdgeKind.DefinedBy },
            ]);
        (await db.LineageEdges.AsNoTracking()
            .Where(e => e.ToKind == NodeKinds.Assertion && e.ToId == locked.LockId.Value)
            .Select(e => new { e.FromKind, e.FromId, e.Kind })
            .ToListAsync(ct))
            .Should().BeEquivalentTo(
            [
                new { FromKind = NodeKinds.Study, FromId = subject.Study.Value, Kind = LineageEdgeKind.DefinedBy },
            ]);
        var releaseId = stored[1].Id.Value;
        (await db.LineageEdges.AsNoTracking()
            .Where(e => e.ToKind == NodeKinds.Assertion && e.ToId == releaseId)
            .Select(e => new { e.FromKind, e.FromId, e.Kind })
            .ToListAsync(ct))
            .Should().BeEquivalentTo(
            [
                new { FromKind = NodeKinds.Assertion, FromId = locked.LockId.Value, Kind = LineageEdgeKind.DerivedFrom },
                new { FromKind = NodeKinds.Study, FromId = subject.Study.Value, Kind = LineageEdgeKind.DefinedBy },
            ]);
    }

    /// <summary>
    /// A lock at one enrollment refuses only that enrollment's writes; a lock at one observation refuses only its
    /// correction. Neighbouring enrollments and observations stay writable.
    /// </summary>
    [Fact(DisplayName = "NarrowLocks_RefuseOnlyTheirScope")]
    public async Task NarrowLocksRefuseOnlyTheirScope()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var neighbour = await _observations.ScreenAsync(subject.Tenant, subject.StudySite, ct);
        var first = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(182), ct);
        var second = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(140), ct);

        await _observations.Protocol.OkAsync(await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(first.ObservationId), ct, LockKind.Frozen), ct);

        (await fixture.Sender.SendAsync(KernelObservationScenario.Correct(subject.Tenant, first, Mmhg(128)), cancellationToken: ct)).Error!.Code
            .Should().Be(ObservationErrors.Locked.Code);
        await _observations.Protocol.OkAsync(KernelObservationScenario.Correct(subject.Tenant, second, Mmhg(130)), ct);
        var third = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(120), ct);
        await _observations.Protocol.OkAsync(Verify(subject, first.ObservationId, 1), ct);
        await _observations.Protocol.OkAsync(Verify(subject, second.ObservationId, 2), ct);
        await _observations.Protocol.OkAsync(Verify(subject, third.ObservationId, 1), ct);

        await _observations.Protocol.OkAsync(await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(subject.Enrollment), ct), ct);

        (await fixture.Sender.SendAsync(_observations.Record(subject.Tenant, subject.Enrollment, Mmhg(120)), cancellationToken: ct)).Error!.Code
            .Should().Be(ObservationErrors.Locked.Code);
        await _observations.RecordAsync(subject.Tenant, neighbour, Mmhg(120), ct);
        (await fixture.ObservationRecords.FindAsync(subject.Tenant, first.ObservationId, ct))!.Locks.Select(l => l.Scope.Kind)
            .Should().Equal(NodeKinds.Enrollment, NodeKinds.Observation);
    }

    /// <summary>
    /// A lock is refused when its signature signs a different statement, when its scope belongs to another study, and
    /// when a lock of the same kind already holds there. A released lock is not released twice. None of these write.
    /// </summary>
    [Fact(DisplayName = "Lock_RefusesAWrongSignatureAForeignScopeAndADuplicate")]
    public async Task LockRefusesAWrongSignatureAForeignScopeAndADuplicate()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var (_, otherStudy) = await _observations.Protocol.NewStudyAsync(subject.Tenant, ct);
        var scope = NodeRef.For(subject.StudySite);

        var signedForFrozen = await _observations.LockAsync(subject.Tenant, subject.Study, scope, ct, LockKind.Frozen);
        (await fixture.Sender.SendAsync(signedForFrozen with { Kind = LockKind.Locked }, cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.AttestationRejected.Code);
        (await fixture.Sender.SendAsync(await _observations.LockAsync(subject.Tenant, otherStudy, scope, ct), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.ScopeUnknown.Code);
        (await fixture.Sender.SendAsync(await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(subject.StudySite, 2), ct), cancellationToken: ct))
            .Error!.Code.Should().Be(AssertionErrors.ScopeInvalid.Code);

        var locked = await _observations.Protocol.OkAsync(await _observations.LockAsync(subject.Tenant, subject.Study, scope, ct), ct);
        (await fixture.Sender.SendAsync(await _observations.LockAsync(subject.Tenant, subject.Study, scope, ct), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.AlreadyLocked.Code);
        await _observations.OkReleaseAsync(subject.Tenant, subject.Study, locked.LockId, ct);
        (await _observations.SendReleaseAsync(subject.Tenant, subject.Study, locked.LockId, ct)).Error!.Code
            .Should().Be(AssertionErrors.AlreadyRetracted.Code);

        await using var db = fixture.CreateContext(subject.Tenant);
        (await db.Assertions.AsNoTracking().CountAsync(ct)).Should().Be(2, "only the lock and its release were recorded");
    }

    /// <summary>
    /// A verification names the revision it judged (ADR-2621): verifying a revision that is no longer current is
    /// refused, and after a correction the earlier verification still names revision 1, so nobody reads the corrected
    /// value as verified. The observation's value and version are not changed by asserting something about it.
    /// </summary>
    [Fact(DisplayName = "Verification_NamesTheRevisionItJudged")]
    public async Task VerificationNamesTheRevisionItJudged()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var recorded = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(182), ct);

        var verified = await _observations.Protocol.OkAsync(Verify(subject, recorded.ObservationId, 1), ct);
        verified.Revision.Should().Be(1);
        var corrected = await _observations.Protocol.OkAsync(KernelObservationScenario.Correct(subject.Tenant, recorded, Mmhg(128)), ct);
        corrected.Version.Should().Be(2, "a verification does not change the observation");

        var stale = await fixture.Sender.SendAsync(Verify(subject, recorded.ObservationId, 1), cancellationToken: ct);
        stale.Error!.Code.Should().Be(AssertionErrors.RevisionStale.Code);
        var view = await fixture.ObservationRecords.FindAsync(subject.Tenant, recorded.ObservationId, ct);
        view!.Assertions.Should().ContainSingle().Which.Should().Match<AssertionView>(a =>
            a.AssertionId == verified.AssertionId && a.Kind == AssertionKind.Verified && a.Revision == 1 && a.RetractedBy == null);
    }

    /// <summary>
    /// A query is raised about an observation, answered, and closed by retracting it; a closed query takes no answer and
    /// is not retracted twice. A <see cref="LockKind.Locked"/> lock refuses new assertions, while a
    /// <see cref="LockKind.Frozen"/> one still allows review. A lock is released only with <see cref="ReleaseLock"/>.
    /// </summary>
    [Fact(DisplayName = "Query_IsAnsweredAndClosed_AndLocksDecideWhatMayStillBeAsserted")]
    public async Task QueryIsAnsweredAndClosedAndLocksDecideWhatMayStillBeAsserted()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var recorded = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(182), ct);

        (await fixture.Sender.SendAsync(Query(subject, recorded.ObservationId, " "), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.TextInvalid.Code);
        var query = await _observations.Protocol.OkAsync(Query(subject, recorded.ObservationId, "Value above the expected range; please confirm."), ct);
        var answer = await _observations.Protocol.OkAsync(Answer(subject, query.AssertionId, "Confirmed against the source document."), ct);
        answer.Target.Should().Be(NodeRef.For(query.AssertionId));
        (await fixture.Sender.SendAsync(Answer(subject, answer.AssertionId, "An answer to an answer"), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.NotAQuery.Code);

        var closed = await _observations.Protocol.OkAsync(Retract(subject, query.AssertionId), ct);
        closed.Retracts.Should().Be(query.AssertionId);
        (await fixture.Sender.SendAsync(Answer(subject, query.AssertionId, "Too late"), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.AlreadyRetracted.Code);
        (await fixture.Sender.SendAsync(Retract(subject, query.AssertionId), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.AlreadyRetracted.Code);

        var frozen = await _observations.Protocol.OkAsync(await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(subject.Enrollment), ct, LockKind.Frozen), ct);
        var verified = await _observations.Protocol.OkAsync(Verify(subject, recorded.ObservationId, 1), ct);
        (await fixture.Sender.SendAsync(Retract(subject, frozen.LockId), cancellationToken: ct)).Error!.Code
            .Should().Be(AssertionErrors.NotRetractable.Code);
        await _observations.Protocol.OkAsync(await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(subject.Study), ct), ct);
        var refused = await fixture.Sender.SendAsync(Query(subject, recorded.ObservationId, "After lock"), cancellationToken: ct);
        refused.Error!.Code.Should().Be(AssertionErrors.Locked.Code);
        refused.Error.HttpStatus.Should().Be(423);

        var view = await fixture.ObservationRecords.FindAsync(subject.Tenant, recorded.ObservationId, ct);
        view!.Version.Should().Be(1, "asserting something about an observation never changes it");
        view.Assertions.Select(a => (a.Kind, Closed: a.RetractedBy != null))
            .Should().Equal((AssertionKind.Queried, true), (AssertionKind.QueryAnswered, false), (AssertionKind.Verified, false));
        view.Assertions[0].RetractedBy.Should().Be(closed.AssertionId);
        view.Locks.Select(l => l.Kind).Should().Equal(LockKind.Locked, LockKind.Frozen);

        await using var db = fixture.CreateContext(subject.Tenant);
        var assertionIds = new[] { query.AssertionId.Value, answer.AssertionId.Value, closed.AssertionId.Value, verified.AssertionId.Value, frozen.LockId.Value };
        var edges = await db.LineageEdges.AsNoTracking()
            .Where(e => e.ToKind == NodeKinds.Assertion && assertionIds.Contains(e.ToId))
            .Select(e => new { e.FromKind, e.FromId, e.FromVersion, e.ToId, e.Kind })
            .ToListAsync(ct);
        edges.Select(e => (e.FromKind, e.FromId, e.FromVersion, e.ToId, e.Kind)).Should().BeEquivalentTo(
            [
                (NodeKinds.Observation, recorded.ObservationId.Value, 1L, query.AssertionId.Value, LineageEdgeKind.DerivedFrom),
                (NodeKinds.Assertion, query.AssertionId.Value, 1L, answer.AssertionId.Value, LineageEdgeKind.DerivedFrom),
                (NodeKinds.Observation, recorded.ObservationId.Value, 1L, answer.AssertionId.Value, LineageEdgeKind.DerivedFrom),
                (NodeKinds.Assertion, query.AssertionId.Value, 1L, closed.AssertionId.Value, LineageEdgeKind.DerivedFrom),
                (NodeKinds.Observation, recorded.ObservationId.Value, 1L, closed.AssertionId.Value, LineageEdgeKind.DerivedFrom),
                (NodeKinds.Observation, recorded.ObservationId.Value, 1L, verified.AssertionId.Value, LineageEdgeKind.DerivedFrom),
                (NodeKinds.Enrollment, subject.Enrollment.Value, 1L, frozen.LockId.Value, LineageEdgeKind.DefinedBy),
            ]);
    }

    /// <summary>A lock statement is signed before it is recorded; an attestation naming an assertion already recorded is refused.</summary>
    [Fact(DisplayName = "LockStatement_IsNotSignableOnceRecorded")]
    public async Task LockStatementIsNotSignableOnceRecorded()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var locked = await _observations.Protocol.OkAsync(await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(subject.Study), ct), ct);

        var late = await fixture.Sender.SendAsync(
            _observations.Protocol.AttestCommand(
                subject.Tenant, AttestationMeaning.Approved,
                LockStatement.Target(locked.LockId, LockStatement.Apply(locked.LockId, LockKind.Locked, NodeRef.For(subject.Study)))),
            cancellationToken: ct);

        late.Error!.Code.Should().Be(TrustErrors.TargetNotSignable.Code);
    }

    /// <summary>
    /// SDV preconditions (ADR-2621, PRD-QRY-005): <see cref="VerifyObservation"/> is refused with 409
    /// (<see cref="AssertionErrors.QueryOpen"/>) while an un-retracted <see cref="AssertionKind.Queried"/> assertion holds
    /// (including after an answer is recorded), succeeds once the query is retracted, refuses duplicate verification on the
    /// same revision with 409 (<see cref="AssertionErrors.AlreadyVerified"/>), succeeds again after verification retraction,
    /// and serializes concurrent verifications under the observation lock so at most one active verification is committed.
    /// </summary>
    [Fact(DisplayName = "VerifyObservation_RefusesOpenQueryAndDuplicateVerification_AndSerializesConcurrentVerifications")]
    [GxPRequirement("PRD-QRY-005")]
    public async Task VerifyObservationRefusesOpenQueryAndDuplicateVerificationAndSerializesConcurrentVerifications()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var recorded = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(144), ct);

        var query = await _observations.Protocol.OkAsync(Query(subject, recorded.ObservationId, "Confirm cuff size on source chart."), ct);

        var openQueryAttempt = await fixture.Sender.SendAsync(Verify(subject, recorded.ObservationId, 1), cancellationToken: ct);
        openQueryAttempt.Error!.Code.Should().Be(AssertionErrors.QueryOpen.Code);
        openQueryAttempt.Error.HttpStatus.Should().Be(409);

        await _observations.Protocol.OkAsync(Answer(subject, query.AssertionId, "Standard adult cuff used."), ct);
        var answeredQueryAttempt = await fixture.Sender.SendAsync(Verify(subject, recorded.ObservationId, 1), cancellationToken: ct);
        answeredQueryAttempt.Error!.Code.Should().Be(AssertionErrors.QueryOpen.Code);

        await _observations.Protocol.OkAsync(Retract(subject, query.AssertionId), ct);

        var verifiedRev1 = await _observations.Protocol.OkAsync(Verify(subject, recorded.ObservationId, 1), ct);
        verifiedRev1.Revision.Should().Be(1);

        var duplicateRev1 = await fixture.Sender.SendAsync(Verify(subject, recorded.ObservationId, 1), cancellationToken: ct);
        duplicateRev1.Error!.Code.Should().Be(AssertionErrors.AlreadyVerified.Code);
        duplicateRev1.Error.HttpStatus.Should().Be(409);

        await _observations.Protocol.OkAsync(Retract(subject, verifiedRev1.AssertionId), ct);
        var reVerifiedRev1 = await _observations.Protocol.OkAsync(Verify(subject, recorded.ObservationId, 1), ct);
        reVerifiedRev1.Revision.Should().Be(1);

        var corrected = await _observations.Protocol.OkAsync(KernelObservationScenario.Correct(subject.Tenant, recorded, Mmhg(138)), ct);
        corrected.Revision.Should().Be(2);

        var concurrent = await Task.WhenAll(
            fixture.Sender.SendAsync(Verify(subject, recorded.ObservationId, 2), cancellationToken: ct),
            fixture.Sender.SendAsync(Verify(subject, recorded.ObservationId, 2), cancellationToken: ct));

        concurrent.Count(r => r.IsSuccess).Should().Be(1);
        var loser = concurrent.Single(r => !r.IsSuccess);
        loser.Error!.Code.Should().Be(AssertionErrors.AlreadyVerified.Code);
        loser.Error.HttpStatus.Should().Be(409);

        var view = await fixture.ObservationRecords.FindAsync(subject.Tenant, recorded.ObservationId, ct);
        view!.Assertions.Where(a => a.Kind == AssertionKind.Verified && a.Revision == 2 && a.RetractedAt == null)
            .Should().ContainSingle();
    }

    /// <summary>
    /// <see cref="IObservationRecords.FindAsync"/> includes <see cref="Assertion"/> audit rows (queries, answers,
    /// verifications, covering locks, and their retractions/releases) alongside <see cref="Observation"/> audit rows
    /// ordered by <c>Sequence</c>, while excluding locks on non-covering scopes.
    /// </summary>
    [Fact(DisplayName = "ObservationAudit_IncludesAssertionsCoveringLocksAndRetractions_OrderedBySequence")]
    public async Task ObservationAuditIncludesAssertionsCoveringLocksAndRetractionsOrderedBySequence()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var subject = await _observations.NewEnrollmentAsync(ct);
        var neighbour = await _observations.ScreenAsync(subject.Tenant, subject.StudySite, ct);

        // Lock on a sibling enrollment does NOT cover `recorded` and must NOT appear in its audit trail.
        await _observations.Protocol.OkAsync(
            await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(neighbour), ct, LockKind.Frozen), ct);

        var recorded = await _observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(160), ct);
        var query = await _observations.Protocol.OkAsync(Query(subject, recorded.ObservationId, "Confirm supine reading."), ct);
        await _observations.Protocol.OkAsync(Answer(subject, query.AssertionId, "Confirmed supine after 5 minutes."), ct);
        await _observations.Protocol.OkAsync(Retract(subject, query.AssertionId), ct);
        var verified = await _observations.Protocol.OkAsync(Verify(subject, recorded.ObservationId, 1), ct);
        await _observations.Protocol.OkAsync(Retract(subject, verified.AssertionId), ct);
        var coveringLock = await _observations.Protocol.OkAsync(
            await _observations.LockAsync(subject.Tenant, subject.Study, NodeRef.For(subject.Study), ct, LockKind.Frozen), ct);
        await _observations.OkReleaseAsync(subject.Tenant, subject.Study, coveringLock.LockId, ct);
        await _observations.Protocol.OkAsync(KernelObservationScenario.Correct(subject.Tenant, recorded, Mmhg(154)), ct);

        var view = await fixture.ObservationRecords.FindAsync(subject.Tenant, recorded.ObservationId, ct);
        view.Should().NotBeNull();
        view!.Audit.Select(a => a.Sequence).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        view.Audit.Where(a => a.Subject == nameof(Observation))
            .Select(a => (a.Action, a.Version))
            .Should().Equal(("Created", 1L), ("Updated", 2L));

        var assertionAudit = view.Audit.Where(a => a.Subject == nameof(Assertion)).ToList();
        assertionAudit.Should().HaveCount(
            7,
            "query, answer, query retraction, verification, verification retraction, covering study lock, and lock release must be included while excluding the sibling enrollment lock");
        assertionAudit.Select(a => a.ReasonForChange).Should().Contain(["Data review", "Site response", "Query resolved", "Source data verified"]);
    }

    /// <summary>Records one observation with a query about it in a new tenant, for tests of the assertion table.</summary>
    internal static async Task SeedAsync(KernelPostgresFixture fixture, CancellationToken ct)
    {
        var observations = new KernelObservationScenario(fixture);
        var subject = await observations.NewEnrollmentAsync(ct);
        var recorded = await observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(120), ct);
        await observations.Protocol.OkAsync(Query(subject, recorded.ObservationId, "Please confirm."), ct);
    }

    private static VerifyObservation Verify(KernelObservationScenario.Subject subject, ObservationId observation, int revision) =>
        new(subject.Tenant, AssertionId.New(), subject.Enrollment, observation, revision, KernelProtocolScenario.Key(), "Source data verified");

    private static RaiseQuery Query(KernelObservationScenario.Subject subject, ObservationId observation, string text) =>
        new(subject.Tenant, AssertionId.New(), subject.Enrollment, observation, text, KernelProtocolScenario.Key(), "Data review");

    private static AnswerQuery Answer(KernelObservationScenario.Subject subject, AssertionId query, string text) =>
        new(subject.Tenant, AssertionId.New(), subject.Enrollment, query, text, KernelProtocolScenario.Key(), "Site response");

    private static RetractAssertion Retract(KernelObservationScenario.Subject subject, AssertionId retracted) =>
        new(subject.Tenant, AssertionId.New(), subject.Enrollment, retracted, KernelProtocolScenario.Key(), "Query resolved");
}
