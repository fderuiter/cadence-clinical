using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Verifies <see cref="LockReadinessEvaluator"/> across all four clinical readiness gates:
/// open queries, missing required items, unverified observation revisions, and missing or superseded form signatures.
/// </summary>
public sealed class LockReadinessEvaluatorTests
{
    private const string SpecId = "cs.vitals";
    private const string SystolicItem = "item.sbp";
    private const string DiastolicItem = "item.dbp";
    private const string OptionalCommentItem = "item.comment";

    private static readonly DateTimeOffset BaseTime = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildSnapshot_WhenAllFourGatesSatisfied_ReportsReadyForLockWithAllConditionsSatisfied()
    {
        var world = CreateWorld();
        var sbp = RecordObservation(world, new QuantityValue(120m, "mm[Hg]"), BaseTime.AddMinutes(-5));
        var dbp = RecordObservation(world, new QuantityValue(80m, "mm[Hg]"), BaseTime.AddMinutes(-4));
        world.Instance.Join(SystolicItem, sbp.Id, BaseTime.AddMinutes(-5));
        world.Instance.Join(DiastolicItem, dbp.Id, BaseTime.AddMinutes(-4));

        var observations = new Dictionary<ObservationId, Observation>
        {
            [sbp.Id] = sbp,
            [dbp.Id] = dbp,
        };
        var hash = CollectionInstance.ContentHashOf(
        [
            new CollectionContentEntry(sbp.Id, sbp.Revision, sbp.Value),
            new CollectionContentEntry(dbp.Id, dbp.Revision, dbp.Value),
        ]);

        var snapshot = LockReadinessEvaluator.BuildSnapshot(
            world.StudyId,
            "STUDY-101",
            NodeRef.For(world.StudyId),
            [world.Instance],
            observations,
            world.Enrollments,
            world.Versions,
            openQueries: [],
            answeredQueryIds: new HashSet<AssertionId>(),
            blindedObservationIds: new HashSet<ObservationId>(),
            verifiedObservationIds: new HashSet<ObservationId> { sbp.Id, dbp.Id },
            signedCollectionHashes: new HashSet<(CollectionInstanceId, string)> { (world.Instance.Id, hash) });

        snapshot.ReadyForLock.Should().BeTrue();
        snapshot.CanFreeze.Should().BeTrue();
        snapshot.OpenQueryCount.Should().Be(0);
        snapshot.MissingRequiredItemCount.Should().Be(0);
        snapshot.UnverifiedRevisionCount.Should().Be(0);
        snapshot.MissingSignatureCount.Should().Be(0);
        snapshot.Blockers.Should().BeEmpty();
        snapshot.Explanation.Conditions.Select(c => (c.Code, c.Met)).Should().Equal(
            (LockReadinessEvaluator.QueriesClosedCondition, true),
            (LockReadinessEvaluator.RequiredItemsCompleteCondition, true),
            (LockReadinessEvaluator.ErrorFindingsResolvedCondition, true),
            (LockReadinessEvaluator.RevisionsVerifiedCondition, true),
            (LockReadinessEvaluator.SignaturesCompleteCondition, true));
    }

    [Fact]
    public void BuildSnapshot_WhenObservationRevisionIsUnverifiedOrBlinded_BlocksLockedAndPreservesFreeze()
    {
        var world = CreateWorld();
        var sbp = RecordObservation(world, new QuantityValue(120m, "mm[Hg]"), BaseTime.AddMinutes(-5));
        var dbp = RecordObservation(world, new MissingValue(NullFlavor.NotDone, "Cuff unavailable"), BaseTime.AddMinutes(-4));
        var blindedArm = RecordObservation(world, new TextValue("Active Arm A"), BaseTime.AddMinutes(-3));
        world.Instance.Join(SystolicItem, sbp.Id, BaseTime.AddMinutes(-5));
        world.Instance.Join(DiastolicItem, dbp.Id, BaseTime.AddMinutes(-4));

        var observations = new Dictionary<ObservationId, Observation>
        {
            [sbp.Id] = sbp,
            [dbp.Id] = dbp,
            [blindedArm.Id] = blindedArm,
        };
        var hash = CollectionInstance.ContentHashOf(
        [
            new CollectionContentEntry(sbp.Id, sbp.Revision, sbp.Value),
            new CollectionContentEntry(dbp.Id, dbp.Revision, dbp.Value),
        ]);

        // Only sbp is verified; reasoned MissingValue dbp and blindedArm lack verification.
        var snapshot = LockReadinessEvaluator.BuildSnapshot(
            world.StudyId,
            "STUDY-101",
            NodeRef.For(world.Enrollment.Id),
            [world.Instance],
            observations,
            world.Enrollments,
            world.Versions,
            openQueries: [],
            answeredQueryIds: new HashSet<AssertionId>(),
            blindedObservationIds: new HashSet<ObservationId> { blindedArm.Id },
            verifiedObservationIds: new HashSet<ObservationId> { sbp.Id },
            signedCollectionHashes: new HashSet<(CollectionInstanceId, string)> { (world.Instance.Id, hash) });

        snapshot.ReadyForLock.Should().BeFalse();
        snapshot.CanFreeze.Should().BeTrue();
        snapshot.MissingRequiredItemCount.Should().Be(0, "reasoned MissingValue satisfies required item gate");
        snapshot.MissingSignatureCount.Should().Be(0);
        snapshot.UnverifiedRevisionCount.Should().Be(2);

        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.UnverifiedRevision
            && b.ObservationId == dbp.Id
            && !b.Blinded
            && b.Owner == LockReadinessEvaluator.MonitorOwner
            && b.NextCapability == "observation.verify");

        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.UnverifiedRevision
            && b.ObservationId == blindedArm.Id
            && b.Blinded);

        snapshot.Explanation.Conditions.Should().ContainSingle(c =>
            c.Code == LockReadinessEvaluator.RevisionsVerifiedCondition && !c.Met);
    }

    [Fact]
    public void BuildSnapshot_WhenFormSignatureIsMissingOrSupersededAfterCorrection_ReportsMissingSignatureBlocker()
    {
        var world = CreateWorld();
        var sbp = RecordObservation(world, new QuantityValue(120m, "mm[Hg]"), BaseTime.AddMinutes(-5));
        var dbp = RecordObservation(world, new QuantityValue(80m, "mm[Hg]"), BaseTime.AddMinutes(-4));
        world.Instance.Join(SystolicItem, sbp.Id, BaseTime.AddMinutes(-5));
        world.Instance.Join(DiastolicItem, dbp.Id, BaseTime.AddMinutes(-4));

        var initialHash = CollectionInstance.ContentHashOf(
        [
            new CollectionContentEntry(sbp.Id, sbp.Revision, sbp.Value),
            new CollectionContentEntry(dbp.Id, dbp.Revision, dbp.Value),
        ]);

        // Correct sbp to revision 2 so the earlier signature's content hash is now superseded.
        sbp.Correct(new QuantityValue(134m, "mm[Hg]"), "Source chart addendum", "crc-1", BaseTime.AddMinutes(-1))
            .Should().BeNull();

        var observations = new Dictionary<ObservationId, Observation>
        {
            [sbp.Id] = sbp,
            [dbp.Id] = dbp,
        };

        var snapshot = LockReadinessEvaluator.BuildSnapshot(
            world.StudyId,
            "STUDY-101",
            NodeRef.For(world.Instance.Id),
            [world.Instance],
            observations,
            world.Enrollments,
            world.Versions,
            openQueries: [],
            answeredQueryIds: new HashSet<AssertionId>(),
            blindedObservationIds: new HashSet<ObservationId>(),
            verifiedObservationIds: new HashSet<ObservationId> { sbp.Id, dbp.Id },
            signedCollectionHashes: new HashSet<(CollectionInstanceId, string)> { (world.Instance.Id, initialHash) });

        snapshot.ReadyForLock.Should().BeFalse();
        snapshot.UnverifiedRevisionCount.Should().Be(0);
        snapshot.MissingSignatureCount.Should().Be(1);
        var sigBlocker = snapshot.Blockers.Should().ContainSingle(b => b.Kind == LockBlockerKind.MissingSignature).Subject;
        sigBlocker.Owner.Should().Be(LockReadinessEvaluator.InvestigatorOwner);
        sigBlocker.NextCapability.Should().Be("collection.sign");
        sigBlocker.Target.Should().Be(NodeRef.For(world.Instance.Id));
        snapshot.Explanation.Conditions.Should().ContainSingle(c =>
            c.Code == LockReadinessEvaluator.SignaturesCompleteCondition && !c.Met);
    }

    [Fact]
    public void BuildSnapshot_CombinesAllFourGatesAndAssignsQueryOwnersByAnsweredState()
    {
        var world = CreateWorld();
        var sbp = RecordObservation(world, new QuantityValue(125m, "mm[Hg]"), BaseTime.AddMinutes(-5));
        var comment = RecordObservation(world, new TextValue("Arm cuff repositioned"), BaseTime.AddMinutes(-4));
        world.Instance.Join(SystolicItem, sbp.Id, BaseTime.AddMinutes(-5));
        world.Instance.Join(OptionalCommentItem, comment.Id, BaseTime.AddMinutes(-4));
        // DiastolicItem remains unrecorded -> MissingRequiredItemCount == 1

        var openUnanswered = Assertion.Query(AssertionId.New(), sbp, "Confirm posture", "cra-1", BaseTime.AddMinutes(-3)).Value!;
        var openAnswered = Assertion.Query(AssertionId.New(), comment, "Confirm cuff size", "cra-1", BaseTime.AddMinutes(-2)).Value!;

        var observations = new Dictionary<ObservationId, Observation>
        {
            [sbp.Id] = sbp,
            [comment.Id] = comment,
        };

        var snapshot = LockReadinessEvaluator.BuildSnapshot(
            world.StudyId,
            "STUDY-101",
            NodeRef.For(world.StudyId),
            [world.Instance],
            observations,
            world.Enrollments,
            world.Versions,
            openQueries: [openUnanswered, openAnswered],
            answeredQueryIds: new HashSet<AssertionId> { openAnswered.Id },
            blindedObservationIds: new HashSet<ObservationId>(),
            verifiedObservationIds: new HashSet<ObservationId> { sbp.Id },
            signedCollectionHashes: new HashSet<(CollectionInstanceId, string)>());

        snapshot.ReadyForLock.Should().BeFalse();
        snapshot.OpenQueryCount.Should().Be(2);
        snapshot.MissingRequiredItemCount.Should().Be(1);
        snapshot.UnverifiedRevisionCount.Should().Be(1);
        snapshot.MissingSignatureCount.Should().Be(1);

        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.OpenQuery
            && b.QueryAssertionId == openUnanswered.Id
            && !b.Answered
            && b.Owner == LockReadinessEvaluator.SiteOwner);
        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.OpenQuery
            && b.QueryAssertionId == openAnswered.Id
            && b.Answered
            && b.Owner == LockReadinessEvaluator.MonitorOwner);
    }

    private static Observation RecordObservation(TestWorld world, ObservationValue value, DateTimeOffset observedAt) =>
        Observation.Record(
            ObservationId.New(),
            world.TenantId,
            world.Enrollment.Id,
            world.ParticipantId,
            world.StudyId,
            world.SbpConcept,
            value,
            observedAt,
            "Recorded from source",
            "crc-1",
            BaseTime).Value!;

    private static TestWorld CreateWorld()
    {
        var tenantId = TenantId.New();
        var studyId = StudyId.New();
        var studySiteId = StudySiteId.New();
        var participantId = ParticipantId.New();
        SubjectCode.TryFrom("SUBJ-001", out var subjectCode);

        var enrollment = Enrollment.Screen(
            EnrollmentId.New(),
            tenantId,
            participantId,
            studyId,
            studySiteId,
            subjectCode,
            BaseTime.AddDays(-1),
            BaseTime).Value!;

        var sbpConcept = new ConceptRef(ConceptId.New(), 1);
        var dbpConcept = new ConceptRef(ConceptId.New(), 1);
        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new CollectionItem(SystolicItem, sbpConcept, "mm[Hg]", Required: true, Prompt: "Systolic BP"),
                        new CollectionItem(DiastolicItem, dbpConcept, "mm[Hg]", Required: true, Prompt: "Diastolic BP"),
                        new CollectionItem(OptionalCommentItem, sbpConcept, Required: false, ValueKind: ObservationValueKind.Text, Prompt: "Comments"),
                    ]),
            ],
        };

        var versionId = ProtocolVersionId.New();
        var version = ProtocolVersion.CreateFirst(versionId, tenantId, studyId, ProtocolDesignDocument.Write(design)).Value!;
        var instance = CollectionInstance.Open(
            CollectionInstanceId.New(),
            enrollment,
            versionId,
            SpecId,
            plannedActivityId: null,
            BaseTime.AddMinutes(-10),
            "crc-1",
            BaseTime.AddMinutes(-10));

        return new TestWorld(
            tenantId,
            studyId,
            participantId,
            sbpConcept,
            enrollment,
            instance,
            new Dictionary<EnrollmentId, Enrollment> { [enrollment.Id] = enrollment },
            new Dictionary<ProtocolVersionId, ProtocolVersion> { [versionId] = version });
    }

    private sealed record TestWorld(
        TenantId TenantId,
        StudyId StudyId,
        ParticipantId ParticipantId,
        ConceptRef SbpConcept,
        Enrollment Enrollment,
        CollectionInstance Instance,
        IReadOnlyDictionary<EnrollmentId, Enrollment> Enrollments,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersion> Versions);
}
