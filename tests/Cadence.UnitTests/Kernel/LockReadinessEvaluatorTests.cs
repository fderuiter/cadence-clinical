using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Pure unit coverage for <see cref="LockReadinessEvaluator.BuildSnapshot"/> across all six hard-lock readiness gates
/// (<c>OpenQuery</c>, <c>MissingRequiredItem</c>, <c>UnverifiedValue</c>, <c>UnsignedForm</c>, <c>UnopenedVisit</c>,
/// and <c>ErrorFinding</c>), verifying that <see cref="LockReadinessSnapshot.CanFreeze"/> stays <c>true</c> while
/// <see cref="LockReadinessSnapshot.ReadyForLock"/> blocks until every gate clears (#5376, #5466).
/// </summary>
public sealed class LockReadinessEvaluatorTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private const string StudyCode = "STUDY-GATE-UNIT";
    private static readonly ConceptRef Systolic = new(ConceptId.New(), 1);
    private static readonly ConceptRef Diastolic = new(ConceptId.New(), 1);
    private static readonly DateTimeOffset BaseInstant = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildSnapshot_WhenAllSixGatesHaveBlockers_ReportsEveryGateCountOwnerCapabilityAndKeepsCanFreezeTrue()
    {
        var rawSiteId = SiteId.New();
        var studySiteId = StudySiteId.New();
        var site = new Site(rawSiteId, Tenant, OrganizationId.New(), "SITE-01", "Metro Clinic", "US", "admin", "seed");
        var studySite = new StudySite(studySiteId, Tenant, Study, rawSiteId, "Active", "admin", "seed");

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs =
            [
                new Epoch("ep.screening", "Screening", 1),
                new Epoch("ep.treatment", "Treatment", 2),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [
                        new CollectionItem("item.sbp", Systolic, "mm[Hg]", Prompt: "Systolic BP", Required: true),
                        new CollectionItem("item.dbp", Diastolic, "mm[Hg]", Prompt: "Diastolic BP", Required: true),
                    ]),
            ],
            PlannedActivities =
            [
                new PlannedActivity("act.day1", "Day 1 Baseline", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(1, 0, 1)),
                new PlannedActivity("act.day14", "Day 14 Follow-up", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(14, 1, 1)),
                new PlannedActivity("act.day84", "Day 84 Closeout", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(84, 2, 2)),
            ],
        };

        var version = ProtocolVersion.CreateFirst(
            ProtocolVersionId.New(),
            Tenant,
            Study,
            ProtocolDesignDocument.Write(design)).Value!;

        var effectivity = Effectivity.Declare(
            EffectivityId.New(),
            Tenant,
            Study,
            version.Id,
            EffectivityScope.Study(),
            BaseInstant.AddDays(-5),
            BaseInstant.AddDays(-5)).Value!;
        effectivity.ChangeStatus(EffectivityStatus.Effective, [ArtifactVersionId.New()], BaseInstant.AddDays(-4), BaseInstant.AddDays(-4)).Should().BeNull();

        // 1. Active enrolled participant (Day 1 = 2026-06-01): Day 1 opened, Day 14 overdue at 2026-06-20, Day 84 future.
        var activeEnrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            Study,
            studySiteId,
            SubjectCode.From("SUBJ-ACTIVE"),
            BaseInstant,
            BaseInstant).Value!;
        activeEnrollment.ChangeStatus(
            EnrollmentStatus.Enrolled,
            BaseInstant.AddHours(1),
            BaseInstant.AddHours(1),
            enrollmentNumber: 1).Should().BeNull();

        // 2. Screen-failed participant: must NEVER produce UnopenedVisit blockers.
        var screenFailedEnrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            Study,
            studySiteId,
            SubjectCode.From("SUBJ-SF"),
            BaseInstant,
            BaseInstant).Value!;
        screenFailedEnrollment.ChangeStatus(
            EnrollmentStatus.ScreenFailed,
            BaseInstant.AddHours(2),
            BaseInstant.AddHours(2)).Should().BeNull();

        // 3. Withdrawn participant (enrolled Day 1 = 2026-06-01, opened Day 1 form, withdrew Day 5 = 2026-06-05):
        //    Day 14 visit falls after withdrawal date and MUST NOT produce an UnopenedVisit blocker.
        var withdrawnEnrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            Study,
            studySiteId,
            SubjectCode.From("SUBJ-WD"),
            BaseInstant,
            BaseInstant).Value!;
        withdrawnEnrollment.ChangeStatus(
            EnrollmentStatus.Enrolled,
            BaseInstant.AddHours(1),
            BaseInstant.AddHours(1),
            enrollmentNumber: 2).Should().BeNull();
        var withdrawalSig = Attestation.Sign(
            AttestationId.New(),
            Tenant,
            Actor.Person("pi@test.org"),
            AttestationMeaning.Approved,
            withdrawnEnrollment.SignableTarget,
            BaseInstant.AddDays(4),
            "password",
            BaseInstant.AddDays(4).AddMinutes(1),
            "Participant withdrew consent").Value!;
        withdrawnEnrollment.Withdraw(
            WithdrawalKind.WithdrawalBySubject,
            withdrawalSig.Id,
            BaseInstant.AddDays(4).AddMinutes(2),
            BaseInstant.AddDays(4).AddMinutes(2)).Should().BeNull();

        // Open Day 1 form for SUBJ-WD and complete + verify + sign it so SUBJ-WD has zero blockers.
        var wdDay1Form = CollectionInstance.Open(
            CollectionInstanceId.New(),
            withdrawnEnrollment,
            version.Id,
            "cs.vitals",
            "act.day1",
            BaseInstant.AddHours(2),
            "crc@test.org",
            BaseInstant.AddHours(2));
        var wdSbp = Observation.Record(
            ObservationId.New(),
            Tenant,
            withdrawnEnrollment.Id,
            withdrawnEnrollment.ParticipantId,
            Study,
            Systolic,
            new QuantityValue(120m, "mm[Hg]"),
            BaseInstant.AddHours(2),
            "Initial entry",
            "crc@test.org",
            BaseInstant.AddHours(2)).Value!;
        var wdDbp = Observation.Record(
            ObservationId.New(),
            Tenant,
            withdrawnEnrollment.Id,
            withdrawnEnrollment.ParticipantId,
            Study,
            Diastolic,
            new MissingValue(NullFlavor.NotDone, "Cuff unavailable"),
            BaseInstant.AddHours(2),
            "Reasoned missing value",
            "crc@test.org",
            BaseInstant.AddHours(2)).Value!;
        wdDay1Form.Join("item.sbp", wdSbp.Id, BaseInstant.AddHours(2));
        wdDay1Form.Join("item.dbp", wdDbp.Id, BaseInstant.AddHours(2));
        var wdHash = CollectionInstance.ContentHashOf([
            new CollectionContentEntry(wdSbp.Id, wdSbp.Revision, wdSbp.Value),
            new CollectionContentEntry(wdDbp.Id, wdDbp.Revision, wdDbp.Value),
        ]);
        var wdFormSig = Attestation.Sign(
            AttestationId.New(),
            Tenant,
            Actor.Person("pi@test.org"),
            AttestationMeaning.Approved,
            new AttestationTarget(NodeKinds.CollectionInstance, wdDay1Form.Id.Value, wdHash),
            BaseInstant.AddHours(3),
            "password",
            BaseInstant.AddHours(3).AddMinutes(1),
            "Sign baseline vitals").Value!;

        // Open Day 1 form for SUBJ-ACTIVE:
        // - item.sbp recorded at rev 1, signed + verified at rev 1, then corrected to rev 2 -> triggers UnverifiedValue (rev 2) AND UnsignedForm (SignatureSuperseded == true)!
        // - item.dbp recorded as NotAsked -> still triggers MissingRequiredItem!
        // - open query on item.sbp -> triggers OpenQuery!
        var activeDay1Form = CollectionInstance.Open(
            CollectionInstanceId.New(),
            activeEnrollment,
            version.Id,
            "cs.vitals",
            "act.day1",
            BaseInstant.AddHours(2),
            "crc@test.org",
            BaseInstant.AddHours(2));
        var activeSbp = Observation.Record(
            ObservationId.New(),
            Tenant,
            activeEnrollment.Id,
            activeEnrollment.ParticipantId,
            Study,
            Systolic,
            new QuantityValue(140m, "mm[Hg]"),
            BaseInstant.AddHours(2),
            "Initial entry",
            "crc@test.org",
            BaseInstant.AddHours(2)).Value!;
        var activeDbpNotAsked = Observation.Record(
            ObservationId.New(),
            Tenant,
            activeEnrollment.Id,
            activeEnrollment.ParticipantId,
            Study,
            Diastolic,
            new MissingValue(NullFlavor.NotAsked),
            BaseInstant.AddHours(2),
            "Placeholder",
            "crc@test.org",
            BaseInstant.AddHours(2)).Value!;
        activeDay1Form.Join("item.sbp", activeSbp.Id, BaseInstant.AddHours(2));
        activeDay1Form.Join("item.dbp", activeDbpNotAsked.Id, BaseInstant.AddHours(2));

        var rev1Hash = CollectionInstance.ContentHashOf([
            new CollectionContentEntry(activeSbp.Id, 1, new QuantityValue(140m, "mm[Hg]")),
            new CollectionContentEntry(activeDbpNotAsked.Id, 1, activeDbpNotAsked.Value),
        ]);
        var supersededSig = Attestation.Sign(
            AttestationId.New(),
            Tenant,
            Actor.Person("pi@test.org"),
            AttestationMeaning.Approved,
            new AttestationTarget(NodeKinds.CollectionInstance, activeDay1Form.Id.Value, rev1Hash),
            BaseInstant.AddHours(3),
            "password",
            BaseInstant.AddHours(3).AddMinutes(1),
            "Signed rev 1").Value!;

        // Correct activeSbp to revision 2 (superseding both rev-1 verification and rev-1 form signature)
        activeSbp.Correct(
            new QuantityValue(136m, "mm[Hg]"),
            "Source chart correction",
            "crc@test.org",
            BaseInstant.AddHours(4)).Should().BeNull();

        var openQuery = Assertion.Query(
            AssertionId.New(),
            activeSbp,
            "Confirm sitting blood pressure after 5 minutes rest.",
            "cra@test.org",
            BaseInstant.AddHours(5)).Value!;

        var errorFinding = new ValidationFindingView(
            ValidationFindingId.New(),
            Study,
            ValidationStage.Collection,
            "vitals-pack",
            LibraryItemId.New(),
            "SBP-RANGE-01",
            1,
            ValidationSeverity.Error,
            new FindingTarget(FindingTargetKind.Observation, "VS", NodeRef.For(activeSbp.Id).ToString()),
            "Systolic BP exceeds protocol alert threshold",
            "<= 135",
            [],
            null,
            null,
            null,
            FindingStatus.Open,
            ValidationRunId.New(),
            ValidationRunId.New(),
            null,
            null,
            null,
            activeEnrollment.Id);

        var evaluatedAt = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
        var snapshot = LockReadinessEvaluator.BuildSnapshot(
            Study,
            StudyCode,
            NodeRef.For(Study),
            instances: [wdDay1Form, activeDay1Form],
            observationsById: new Dictionary<ObservationId, Observation>
            {
                [wdSbp.Id] = wdSbp,
                [wdDbp.Id] = wdDbp,
                [activeSbp.Id] = activeSbp,
                [activeDbpNotAsked.Id] = activeDbpNotAsked,
            },
            enrollmentsById: new Dictionary<EnrollmentId, Enrollment>
            {
                [activeEnrollment.Id] = activeEnrollment,
                [screenFailedEnrollment.Id] = screenFailedEnrollment,
                [withdrawnEnrollment.Id] = withdrawnEnrollment,
            },
            versionsById: new Dictionary<ProtocolVersionId, ProtocolVersion> { [version.Id] = version },
            openQueries: [openQuery],
            answeredQueryIds: new HashSet<AssertionId>(),
            blindedObservationIds: new HashSet<ObservationId>(),
            activeErrorFindings: [errorFinding],
            verifiedRevisions: new HashSet<(ObservationId, int)>
            {
                (wdSbp.Id, 1),
                (wdDbp.Id, 1),
                (activeSbp.Id, 1), // rev 1 verified, rev 2 unverified!
            },
            formSignaturesByInstance: new Dictionary<CollectionInstanceId, IReadOnlyList<Attestation>>
            {
                [wdDay1Form.Id] = [wdFormSig],
                [activeDay1Form.Id] = [supersededSig],
            },
            evaluatedAt: evaluatedAt,
            studySitesById: new Dictionary<StudySiteId, StudySite> { [studySiteId] = studySite },
            sitesById: new Dictionary<SiteId, Site> { [rawSiteId] = site },
            studyEffectivities: [effectivity]);

        snapshot.ReadyForLock.Should().BeFalse();
        snapshot.CanFreeze.Should().BeTrue("Frozen remains available when readiness blockers exist");
        snapshot.OpenQueryCount.Should().Be(1);
        snapshot.MissingRequiredItemCount.Should().Be(1, "NotAsked does not satisfy a required item, whereas reasoned NotDone on SUBJ-WD does");
        snapshot.UnverifiedValueCount.Should().Be(1, "activeSbp revision 2 is unverified");
        snapshot.UnsignedFormCount.Should().Be(1, "activeDay1Form's rev-1 signature was superseded by rev-2 edit");
        snapshot.UnopenedVisitCount.Should().Be(1, "only SUBJ-ACTIVE's overdue Day 14 visit blocks; ScreenFailed, post-withdrawal Day 14, and future Day 84 do not");
        snapshot.OpenErrorFindingCount.Should().Be(1);

        snapshot.Blockers.Should().HaveCount(6);
        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.OpenQuery
            && b.Owner == LockReadinessEvaluator.SiteOwner
            && b.NextCapability == "observation.query.answer");
        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.MissingRequiredItem
            && b.ItemId == "item.dbp"
            && b.Owner == LockReadinessEvaluator.SiteOwner
            && b.NextCapability == "collection.record");
        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.UnverifiedValue
            && b.ObservationId == activeSbp.Id
            && b.CurrentRevision == 2
            && b.Owner == LockReadinessEvaluator.MonitorOwner
            && b.NextCapability == "observation.verify");
        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.UnsignedForm
            && b.CollectionInstanceId == activeDay1Form.Id
            && b.SignatureSuperseded
            && b.Owner == LockReadinessEvaluator.InvestigatorOwner
            && b.NextCapability == "collection.sign");
        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.UnopenedVisit
            && b.SubjectCode == "SUBJ-ACTIVE"
            && b.PlannedActivityId == "act.day14"
            && b.Owner == LockReadinessEvaluator.SiteOwner
            && b.NextCapability == "collection.open");
        snapshot.Blockers.Should().ContainSingle(b =>
            b.Kind == LockBlockerKind.ErrorFinding
            && b.FindingId == errorFinding.Id
            && b.RuleId == "vitals-pack/SBP-RANGE-01"
            && b.Owner == LockReadinessEvaluator.MonitorOwner
            && b.NextCapability == "validation_finding.investigate");

        snapshot.Explanation.Conditions.Where(c => !c.Met).Select(c => c.Code).Should().Contain([
            LockReadinessEvaluator.QueriesClosedCondition,
            LockReadinessEvaluator.RequiredItemsCompleteCondition,
            LockReadinessEvaluator.ObservationsVerifiedCondition,
            LockReadinessEvaluator.FormsSignedCondition,
            LockReadinessEvaluator.ScheduledVisitsOpenedCondition,
            LockReadinessEvaluator.ErrorFindingsResolvedCondition,
        ]);
    }

    [Fact]
    public void BuildSnapshot_WhenAllSixGatesAreSatisfied_ReportsReadyForLockAndSatisfiedConditions()
    {
        var scope = NodeRef.For(Study);
        var snapshot = LockReadinessEvaluator.BuildSnapshot(
            Study,
            StudyCode,
            scope,
            instances: [],
            observationsById: new Dictionary<ObservationId, Observation>(),
            enrollmentsById: new Dictionary<EnrollmentId, Enrollment>(),
            versionsById: new Dictionary<ProtocolVersionId, ProtocolVersion>(),
            openQueries: [],
            answeredQueryIds: new HashSet<AssertionId>(),
            blindedObservationIds: new HashSet<ObservationId>(),
            evaluatedAt: BaseInstant);

        snapshot.ReadyForLock.Should().BeTrue();
        snapshot.CanFreeze.Should().BeTrue();
        snapshot.Blockers.Should().BeEmpty();
        snapshot.OpenQueryCount.Should().Be(0);
        snapshot.MissingRequiredItemCount.Should().Be(0);
        snapshot.UnverifiedValueCount.Should().Be(0);
        snapshot.UnsignedFormCount.Should().Be(0);
        snapshot.UnopenedVisitCount.Should().Be(0);
        snapshot.OpenErrorFindingCount.Should().Be(0);
        snapshot.Explanation.Conditions.Should().HaveCount(6).And.OnlyContain(c => c.Met);
    }
}
