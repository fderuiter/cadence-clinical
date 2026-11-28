using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Evidence;

/// <summary>
/// Unit tests verifying O(1) hash-indexed scope lookups in <see cref="ScopedAssertions"/> and batch form-hydration
/// indexes (<see cref="BatchAssertionIndex"/>, <see cref="BatchLockIndex"/>, <see cref="BatchErrorTargetIndex"/>,
/// and <see cref="BatchFormBlindingLookup"/>) against single-form projection semantics.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BatchFormHydrationIndexTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly StudySiteId Site = StudySiteId.New();
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies that <see cref="ScopedAssertions"/> orders active locks widest-first across all five lock hierarchy
    /// levels, breaks ties within the same level by first-seen scope position (even when duplicate scopes are supplied),
    /// excludes retracted locks, and preserves chronological <c>(AssertedAt, Id)</c> ordering in <c>About</c>.
    /// </summary>
    [Fact(DisplayName = "ScopedAssertions_OrdersLocksWidestFirstAndPreservesChronologicalAssertions")]
    public void ScopedAssertionsOrdersLocksWidestFirstAndPreservesChronologicalAssertions()
    {
        var enrollment = CreateEnrollment("SUBJ-001");
        var version = ProtocolVersionId.New();
        var form = CollectionInstance.Open(
            CollectionInstanceId.New(),
            enrollment,
            version,
            "cs.vitals",
            "visit.day1",
            T0,
            "crc@cadence.test",
            T0);
        var obsA = CreateObservation(enrollment, ConceptId.New(), new QuantityValue(120m, "mm[Hg]"), T0);
        var obsB = CreateObservation(enrollment, ConceptId.New(), new QuantityValue(80m, "mm[Hg]"), T0);
        var att = AttestationId.New();

        // Supply duplicate scopes intentionally to verify first-seen index tie-breaking.
        List<NodeRef> scopes =
        [
            NodeRef.For(Study),
            NodeRef.For(Site),
            NodeRef.For(enrollment.Id),
            NodeRef.For(form.Id),
            NodeRef.For(obsB.Id),
            NodeRef.For(obsA.Id),
            NodeRef.For(obsB.Id),
        ];

        var obsALock = Assertion.Lock(AssertionId.New(), Tenant, LockKind.Frozen, Study, enrollment.Id, NodeRef.For(obsA.Id), att, "dm@cadence.test", T0.AddMinutes(1));
        var obsBLock = Assertion.Lock(AssertionId.New(), Tenant, LockKind.Frozen, Study, enrollment.Id, NodeRef.For(obsB.Id), att, "dm@cadence.test", T0.AddMinutes(2));
        var formLock = Assertion.Lock(AssertionId.New(), Tenant, LockKind.Locked, Study, enrollment.Id, NodeRef.For(form.Id), att, "dm@cadence.test", T0.AddMinutes(3));
        var enrollmentLock = Assertion.Lock(AssertionId.New(), Tenant, LockKind.Frozen, Study, enrollment.Id, NodeRef.For(enrollment.Id), att, "dm@cadence.test", T0.AddMinutes(4));
        var siteLock = Assertion.Lock(AssertionId.New(), Tenant, LockKind.Frozen, Study, null, NodeRef.For(Site), att, "dm@cadence.test", T0.AddMinutes(5));
        var studyLock = Assertion.Lock(AssertionId.New(), Tenant, LockKind.Locked, Study, null, NodeRef.For(Study), att, "dm@cadence.test", T0.AddMinutes(6));
        var retractedFormLock = Assertion.Lock(AssertionId.New(), Tenant, LockKind.Frozen, Study, enrollment.Id, NodeRef.For(form.Id), att, "dm@cadence.test", T0.AddMinutes(7));
        var releaseFormLock = Assertion.Retract(AssertionId.New(), retractedFormLock, att, "dm@cadence.test", T0.AddMinutes(8));

        var query = Assertion.Query(AssertionId.New(), obsA, "Confirm SBP", "cra@cadence.test", T0.AddMinutes(10), 1).Value!;
        var answer = Assertion.Answer(AssertionId.New(), query, "Confirmed", "crc@cadence.test", T0.AddMinutes(11), 1).Value!;
        var verify = Assertion.Verify(AssertionId.New(), obsB, 1, "cra@cadence.test", T0.AddMinutes(9));

        var scoped = new ScopedAssertions(
            scopes,
            [
                obsALock,
                obsBLock,
                formLock,
                enrollmentLock,
                siteLock,
                studyLock,
                retractedFormLock,
                releaseFormLock,
                answer,
                query,
                verify,
            ]);

        scoped.Locks().Select(l => l.LockId).Should().Equal(
            studyLock.Id,
            siteLock.Id,
            enrollmentLock.Id,
            formLock.Id,
            obsBLock.Id,
            obsALock.Id);

        var aboutObs = scoped.About(a => a.TargetKind == NodeKinds.Observation && (a.TargetId == obsA.Id.Value || a.TargetId == obsB.Id.Value));
        aboutObs.Select(a => a.AssertionId).Should().Equal(verify.Id, query.Id, answer.Id);

        var auditIds = scoped.CoveringAuditIds(a => a.TargetKind == NodeKinds.Observation && a.TargetId == obsA.Id.Value);
        auditIds.Should().Contain(retractedFormLock.Id);
        auditIds.Should().Contain(releaseFormLock.Id);
        auditIds.Should().Contain(query.Id);
        auditIds.Should().Contain(answer.Id);
        auditIds.Should().NotContain(verify.Id);
    }

    /// <summary>
    /// Verifies that <see cref="BatchAssertionIndex"/> produces the exact per-form <see cref="AssertionView"/> slice as
    /// <see cref="KernelCollectionInstanceRecords.FilterFormAssertions"/> across queries, answers, retractions, and
    /// revision-bound <see cref="AssertionKind.Verified"/> checks (ADR-2621).
    /// </summary>
    [Fact(DisplayName = "BatchAssertionIndex_MatchesFilterFormAssertionsAndRevisionBoundVerification")]
    public void BatchAssertionIndexMatchesFilterFormAssertionsAndRevisionBoundVerification()
    {
        var enrollment = CreateEnrollment("SUBJ-002");
        var obs1 = CreateObservation(enrollment, ConceptId.New(), new QuantityValue(118m, "mm[Hg]"), T0);
        var obs2 = CreateObservation(enrollment, ConceptId.New(), new QuantityValue(76m, "mm[Hg]"), T0);
        var obsOtherForm = CreateObservation(enrollment, ConceptId.New(), new QuantityValue(72m, "/min"), T0);

        var verifyRev1 = Assertion.Verify(AssertionId.New(), obs1, 1, "cra@cadence.test", T0.AddMinutes(1));
        var retractedVerifyObs2 = Assertion.Verify(AssertionId.New(), obs2, 1, "cra@cadence.test", T0.AddMinutes(2));
        var retractVerifyObs2 = Assertion.Retract(AssertionId.New(), retractedVerifyObs2, null, "cra@cadence.test", T0.AddMinutes(3));

        var openQuery = Assertion.Query(AssertionId.New(), obs1, "Check SBP", "cra@cadence.test", T0.AddMinutes(4), 1).Value!;
        var answeredQuery = Assertion.Query(AssertionId.New(), obs2, "Check DBP", "cra@cadence.test", T0.AddMinutes(5), 1).Value!;
        var answer = Assertion.Answer(AssertionId.New(), answeredQuery, "Source matches", "crc@cadence.test", T0.AddMinutes(6), 1).Value!;
        var otherQuery = Assertion.Query(AssertionId.New(), obsOtherForm, "Check HR", "cra@cadence.test", T0.AddMinutes(4), 1).Value!;
        var otherAnswer = Assertion.Answer(AssertionId.New(), otherQuery, "HR confirmed", "crc@cadence.test", T0.AddMinutes(7), 1).Value!;

        List<NodeRef> scopes =
        [
            NodeRef.For(obs1.Id),
            NodeRef.For(obs2.Id),
            NodeRef.For(obsOtherForm.Id),
        ];
        var scoped = new ScopedAssertions(
            scopes,
            [
                otherAnswer,
                answer,
                otherQuery,
                answeredQuery,
                openQuery,
                retractVerifyObs2,
                retractedVerifyObs2,
                verifyRev1,
            ]);

        var allGuids = new HashSet<Guid> { obs1.Id.Value, obs2.Id.Value, obsOtherForm.Id.Value };
        var allAssertions = scoped.About(a => a.TargetKind == NodeKinds.Observation && allGuids.Contains(a.TargetId));
        var index = new BatchAssertionIndex(allAssertions);

        var form1Members = new HashSet<Guid> { obs1.Id.Value, obs2.Id.Value };
        var form2Members = new HashSet<Guid> { obsOtherForm.Id.Value };
        var emptyFormMembers = new HashSet<Guid> { Guid.NewGuid() };

        index.ForForm(form1Members).Should().Equal(KernelCollectionInstanceRecords.FilterFormAssertions(allAssertions, form1Members));
        index.ForForm(form2Members).Should().Equal(KernelCollectionInstanceRecords.FilterFormAssertions(allAssertions, form2Members));
        index.ForForm(emptyFormMembers).Should().BeEmpty();

        index.IsVerified(obs1.Id.Value, 1).Should().BeTrue();
        index.IsVerified(obs1.Id.Value, 2).Should().BeFalse("verification is bound to revision 1 (ADR-2621)");
        index.IsVerified(obs2.Id.Value, 1).Should().BeFalse("retracted verifications do not count as verified");
    }

    /// <summary>
    /// Verifies that <see cref="BatchLockIndex"/> and <see cref="BatchErrorTargetIndex"/> match linear scan oracles
    /// across multi-scope locks and dataset/observation validation findings.
    /// </summary>
    [Fact(DisplayName = "BatchLockIndex_And_BatchErrorTargetIndex_MatchLinearOraclesAcrossAllScopes")]
    public void BatchLockIndexAndBatchErrorTargetIndexMatchLinearOraclesAcrossAllScopes()
    {
        var studyGuid = Study.Value;
        var siteAGuid = Site.Value;
        var siteBGuid = Guid.NewGuid();
        var enrGuid = Guid.NewGuid();
        var formAGuid = Guid.NewGuid();
        var formBGuid = Guid.NewGuid();
        var obs1Guid = Guid.NewGuid();
        var obs2Guid = Guid.NewGuid();
        var obs3Guid = Guid.NewGuid();
        var att = AttestationId.New();

        IReadOnlyList<LockView> allLocks =
        [
            new LockView(AssertionId.New(), LockKind.Frozen, new NodeRef(NodeKinds.Study, studyGuid), att, T0, "dm@cadence.test", "Study freeze"),
            new LockView(AssertionId.New(), LockKind.Locked, new NodeRef(NodeKinds.StudySite, siteBGuid), att, T0.AddMinutes(1), "dm@cadence.test", "Site B lock"),
            new LockView(AssertionId.New(), LockKind.Frozen, new NodeRef(NodeKinds.Enrollment, enrGuid), att, T0.AddMinutes(2), "dm@cadence.test", "Subject freeze"),
            new LockView(AssertionId.New(), LockKind.Locked, new NodeRef(NodeKinds.CollectionInstance, formAGuid), att, T0.AddMinutes(3), "dm@cadence.test", "Form A lock"),
            new LockView(AssertionId.New(), LockKind.Frozen, new NodeRef(NodeKinds.Observation, obs2Guid), att, T0.AddMinutes(4), "dm@cadence.test", "Obs 2 freeze"),
            new LockView(AssertionId.New(), LockKind.Frozen, new NodeRef(NodeKinds.Observation, obs1Guid), att, T0.AddMinutes(5), "dm@cadence.test", "Obs 1 freeze"),
        ];

        var lockIndex = new BatchLockIndex(allLocks);
        var formAMembers = new HashSet<Guid> { obs1Guid, obs2Guid };
        var formBMembers = new HashSet<Guid> { obs3Guid };

        var expectedFormALocks = allLocks
            .Where(l =>
                (l.Scope.Kind == NodeKinds.Study && l.Scope.Id == studyGuid)
                || (l.Scope.Kind == NodeKinds.StudySite && l.Scope.Id == siteAGuid)
                || (l.Scope.Kind == NodeKinds.Enrollment && l.Scope.Id == enrGuid)
                || (l.Scope.Kind == NodeKinds.CollectionInstance && l.Scope.Id == formAGuid)
                || (l.Scope.Kind == NodeKinds.Observation && formAMembers.Contains(l.Scope.Id)))
            .ToList();

        lockIndex.ForForm(studyGuid, siteAGuid, enrGuid, formAGuid, formAMembers).Should().Equal(expectedFormALocks);
        lockIndex.SummarizeForForm(studyGuid, siteAGuid, enrGuid, formAGuid, formAMembers)
            .Should().Be((true, true, expectedFormALocks.Count));

        var expectedFormBLocks = allLocks
            .Where(l =>
                (l.Scope.Kind == NodeKinds.Study && l.Scope.Id == studyGuid)
                || (l.Scope.Kind == NodeKinds.StudySite && l.Scope.Id == siteBGuid)
                || (l.Scope.Kind == NodeKinds.Enrollment && l.Scope.Id == enrGuid)
                || (l.Scope.Kind == NodeKinds.CollectionInstance && l.Scope.Id == formBGuid)
                || (l.Scope.Kind == NodeKinds.Observation && formBMembers.Contains(l.Scope.Id)))
            .ToList();
        lockIndex.ForForm(studyGuid, siteBGuid, enrGuid, formBGuid, formBMembers).Should().Equal(expectedFormBLocks);

        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets =
        [
            (ValidationFindingId.New(), new FindingTarget(FindingTargetKind.DatasetRecord, "cs.vitals", formAGuid.ToString("D"), "sbp")),
            (ValidationFindingId.New(), new FindingTarget(FindingTargetKind.DatasetRecord, "cs.vitals", formAGuid.ToString("D"), "dbp")),
            (ValidationFindingId.New(), new FindingTarget(FindingTargetKind.DatasetRecord, "cs.vitals", "non-guid-key", "hr")),
            (ValidationFindingId.New(), new FindingTarget(FindingTargetKind.Observation, "cs.vitals", new NodeRef(NodeKinds.Observation, obs1Guid).ToString(), "sbp")),
            (ValidationFindingId.New(), new FindingTarget(FindingTargetKind.Observation, "cs.vitals", new NodeRef(NodeKinds.Observation, obs2Guid).ToString(), "dbp")),
            (ValidationFindingId.New(), new FindingTarget(FindingTargetKind.Observation, "cs.vitals", new NodeRef(NodeKinds.Observation, obs3Guid).ToString(), "hr")),
        ];

        var errorIndex = new BatchErrorTargetIndex(activeErrorTargets);
        errorIndex.CountForForm(formAGuid, formAMembers)
            .Should().Be(KernelCollectionInstanceRecords.CountFormErrorFindings(formAGuid, formAMembers, activeErrorTargets));
        errorIndex.CountForForm(formBGuid, formBMembers)
            .Should().Be(KernelCollectionInstanceRecords.CountFormErrorFindings(formBGuid, formBMembers, activeErrorTargets));
    }

    /// <summary>
    /// Verifies that <see cref="BatchFormBlindingLookup"/> matches <see cref="KernelCollectionInstanceRecords.ClassifyBlindedMembers"/>
    /// across blinded specs, blinded items, blinded concepts, historical revisions, signed snapshots, and per-observation
    /// <see cref="NullFlavor.Masked"/> values without tainting unmasked sibling observations (K-AUTH-4).
    /// </summary>
    [Fact(DisplayName = "BatchFormBlindingLookup_MatchesClassifyBlindedMembersAndIsolatesMaskedValues")]
    public void BatchFormBlindingLookupMatchesClassifyBlindedMembersAndIsolatesMaskedValues()
    {
        var enrollment = CreateEnrollment("SUBJ-003");
        var version = ProtocolVersionId.New();

        var sbpConcept = DefineConcept("Systolic Blood Pressure", "Brachial systolic blood pressure");
        var ipConcept = DefineConcept("Investigational Product Dose", "Blinded investigational product administration");
        var concepts = new Dictionary<ConceptId, Concept>
        {
            [sbpConcept.Id] = sbpConcept,
            [ipConcept.Id] = ipConcept,
        };

        IReadOnlyList<CollectionItem> vitalsItems =
        [
            new CollectionItem("sbp", sbpConcept.Current),
            new CollectionItem("kit_number", sbpConcept.Current),
            new CollectionItem("dose_check", ipConcept.Current),
        ];
        var design = new ProtocolDesign
        {
            SchemaVersion = 1,
            Title = "Blinding Parity Protocol",
            CollectionSpecs =
            [
                new CollectionSpec("cs.vitals", "Vital Signs", vitalsItems),
                new CollectionSpec("cs.randomization", "Treatment Assignment", [new CollectionItem("arm", sbpConcept.Current)]),
            ],
        };
        IReadOnlyList<ProtocolDesign> designs = [design];

        // Form 1: sbp is Masked on this form ONLY; Form 2 has normal unmasked sbp on the exact same (version, spec, item, concept).
        var formMaskedSbp = CollectionInstance.Open(CollectionInstanceId.New(), enrollment, version, "cs.vitals", null, T0, "crc@cadence.test", T0);
        var maskedSbpObs = CreateObservation(enrollment, sbpConcept.Id, new MissingValue(NullFlavor.Masked), T0);
        var kitObs1 = CreateObservation(enrollment, sbpConcept.Id, new TextValue("KIT-101"), T0);
        formMaskedSbp.Join("sbp", maskedSbpObs.Id, T0);
        formMaskedSbp.Join("kit_number", kitObs1.Id, T0);

        var formUnmaskedSbp = CollectionInstance.Open(CollectionInstanceId.New(), enrollment, version, "cs.vitals", null, T0.AddMinutes(5), "crc@cadence.test", T0.AddMinutes(5));
        var unmaskedSbpObs = CreateObservation(enrollment, sbpConcept.Id, new QuantityValue(122m, "mm[Hg]"), T0.AddMinutes(5));
        var doseObs = CreateObservation(enrollment, ipConcept.Id, new QuantityValue(50m, "mg"), T0.AddMinutes(5));
        formUnmaskedSbp.Join("sbp", unmaskedSbpObs.Id, T0.AddMinutes(5));
        formUnmaskedSbp.Join("dose_check", doseObs.Id, T0.AddMinutes(5));

        // Form 3: signed snapshot contains a Masked value on an otherwise unblinded item.
        var formSignedMasked = CollectionInstance.Open(CollectionInstanceId.New(), enrollment, version, "cs.vitals", null, T0.AddMinutes(10), "crc@cadence.test", T0.AddMinutes(10));
        var normalSbpObs3 = CreateObservation(enrollment, sbpConcept.Id, new QuantityValue(118m, "mm[Hg]"), T0.AddMinutes(10));
        formSignedMasked.Join("sbp", normalSbpObs3.Id, T0.AddMinutes(10));
        var signedMaskedAttestations = new List<FormAttestationView>
        {
            new(
                AttestationId.New(),
                AttestationMeaning.Approved,
                "pi@cadence.test",
                T0.AddMinutes(11),
                ContentHash.Compute("{\"signed\":true}"),
                "Investigator sign-off",
                Superseded: true,
                [new SignedValueView("sbp", normalSbpObs3.Id, 1, new MissingValue(NullFlavor.Masked))]),
        };

        var observations = new Dictionary<ObservationId, Observation>
        {
            [maskedSbpObs.Id] = maskedSbpObs,
            [kitObs1.Id] = kitObs1,
            [unmaskedSbpObs.Id] = unmaskedSbpObs,
            [doseObs.Id] = doseObs,
            [normalSbpObs3.Id] = normalSbpObs3,
        };

        var lookup = BatchFormBlindingLookup.FromConcepts(BlindedTreatmentClassifier.Instance, concepts, designs);

        var actual1 = lookup.ClassifyForm(formMaskedSbp, "Vital Signs", vitalsItems, observations, []);
        var expected1 = KernelCollectionInstanceRecords.ClassifyBlindedMembers(
            BlindedTreatmentClassifier.Instance,
            formMaskedSbp,
            "Vital Signs",
            vitalsItems,
            observations,
            concepts,
            designs,
            []);
        actual1.BlindedItems.Should().BeEquivalentTo(expected1.BlindedItems);
        actual1.BlindedObservations.Should().BeEquivalentTo(expected1.BlindedObservations);
        actual1.BlindedObservations.Should().Contain(maskedSbpObs.Id);

        var actual2 = lookup.ClassifyForm(formUnmaskedSbp, "Vital Signs", vitalsItems, observations, []);
        var expected2 = KernelCollectionInstanceRecords.ClassifyBlindedMembers(
            BlindedTreatmentClassifier.Instance,
            formUnmaskedSbp,
            "Vital Signs",
            vitalsItems,
            observations,
            concepts,
            designs,
            []);
        actual2.BlindedItems.Should().BeEquivalentTo(expected2.BlindedItems);
        actual2.BlindedObservations.Should().BeEquivalentTo(expected2.BlindedObservations);
        actual2.BlindedItems.Should().NotContain("sbp", "a Masked value on Form 1 must never taint unmasked sbp on Form 2");
        actual2.BlindedObservations.Should().NotContain(unmaskedSbpObs.Id);
        actual2.BlindedObservations.Should().Contain(doseObs.Id);

        var actual3 = lookup.ClassifyForm(formSignedMasked, "Vital Signs", vitalsItems, observations, signedMaskedAttestations);
        var expected3 = KernelCollectionInstanceRecords.ClassifyBlindedMembers(
            BlindedTreatmentClassifier.Instance,
            formSignedMasked,
            "Vital Signs",
            vitalsItems,
            observations,
            concepts,
            designs,
            signedMaskedAttestations);
        actual3.BlindedItems.Should().BeEquivalentTo(expected3.BlindedItems);
        actual3.BlindedObservations.Should().BeEquivalentTo(expected3.BlindedObservations);
    }

    /// <summary>
    /// Verifies that <see cref="BatchFormBlindingLookup"/> memoizes structural classification calls for custom
    /// <see cref="IBlindedTreatmentClassifier"/> implementations so 500 forms and 2,000 forms execute the exact same
    /// O(unique items) number of structural classifier invocations.
    /// </summary>
    [Fact(DisplayName = "BatchFormBlindingLookup_MemoizesCustomBlindedTreatmentClassifierAcrossBatch")]
    public void BatchFormBlindingLookupMemoizesCustomBlindedTreatmentClassifierAcrossBatch()
    {
        var callsAt500 = RunCustomClassifierBatch(formCount: 500);
        var callsAt2000 = RunCustomClassifierBatch(formCount: 2_000);

        callsAt500.StructuralCalls.Should().BeGreaterThan(0);
        callsAt2000.StructuralCalls.Should().Be(
            callsAt500.StructuralCalls,
            "structural blinding per (protocolVersion, spec, item, concept) must be memoized once per batch regardless of form count");
        callsAt2000.MaskedValueCalls.Should().Be(callsAt500.MaskedValueCalls * 4);
    }

    private static (int StructuralCalls, int MaskedValueCalls) RunCustomClassifierBatch(int formCount)
    {
        var enrollment = CreateEnrollment("SUBJ-SCALE");
        var version = ProtocolVersionId.New();
        var sbpConcept = DefineConcept("SBP", "Systolic BP");
        var customBlindedConcept = DefineConcept("Custom Blinded Marker", "Classified by custom spy");
        var concepts = new Dictionary<ConceptId, Concept>
        {
            [sbpConcept.Id] = sbpConcept,
            [customBlindedConcept.Id] = customBlindedConcept,
        };
        IReadOnlyList<CollectionItem> items =
        [
            new CollectionItem("sbp", sbpConcept.Current),
            new CollectionItem("custom_secret", customBlindedConcept.Current),
        ];
        IReadOnlyList<ProtocolDesign> designs =
        [
            new ProtocolDesign
            {
                SchemaVersion = 1,
                Title = "Custom Classifier Batch",
                CollectionSpecs = [new CollectionSpec("cs.vitals", "Vital Signs", items)],
            },
        ];

        var spy = new CountingCustomBlindingClassifier(customBlindedConcept.Id);
        var lookup = BatchFormBlindingLookup.FromConcepts(spy, concepts, designs);

        for (var i = 0; i < formCount; i++)
        {
            var form = CollectionInstance.Open(CollectionInstanceId.New(), enrollment, version, "cs.vitals", null, T0.AddSeconds(i), "crc@cadence.test", T0.AddSeconds(i));
            ObservationValue sbpVal = i % 100 == 0
                ? new MissingValue(NullFlavor.Masked)
                : new QuantityValue(120m, "mm[Hg]");
            var obs1 = CreateObservation(enrollment, sbpConcept.Id, sbpVal, T0.AddSeconds(i));
            var obs2 = CreateObservation(enrollment, customBlindedConcept.Id, new TextValue("X"), T0.AddSeconds(i));
            form.Join("sbp", obs1.Id, T0.AddSeconds(i));
            form.Join("custom_secret", obs2.Id, T0.AddSeconds(i));
            var map = new Dictionary<ObservationId, Observation>
            {
                [obs1.Id] = obs1,
                [obs2.Id] = obs2,
            };

            var (blindedItems, blindedObs) = lookup.ClassifyForm(form, "Vital Signs", items, map, []);
            blindedItems.Should().Contain("custom_secret");
            blindedObs.Should().Contain(obs2.Id);
            if (i % 100 == 0)
            {
                blindedItems.Should().Contain("sbp");
                blindedObs.Should().Contain(obs1.Id);
            }
            else
            {
                blindedItems.Should().NotContain("sbp");
                blindedObs.Should().NotContain(obs1.Id);
            }
        }

        return (spy.StructuralCalls, spy.MaskedValueCalls);
    }

    private static Enrollment CreateEnrollment(string code) =>
        Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            Study,
            Site,
            SubjectCode.From(code),
            T0,
            T0).Value!;

    private static Observation CreateObservation(
        Enrollment enrollment,
        ConceptId conceptId,
        ObservationValue value,
        DateTimeOffset at) =>
        Observation.Record(
            ObservationId.New(),
            Tenant,
            enrollment.Id,
            enrollment.ParticipantId,
            Study,
            new ConceptRef(conceptId, 1),
            value,
            at,
            "Initial entry",
            "crc@cadence.test",
            at).Value!;

    private static Concept DefineConcept(string label, string definition) =>
        Concept.Define(
            ConceptId.New(),
            Tenant,
            ConceptOrigin.Sponsor,
            externalCode: null,
            ConceptKind.BiomedicalConcept,
            label,
            definition,
            T0).Value!;

    private sealed class CountingCustomBlindingClassifier(ConceptId blindedConceptId) : IBlindedTreatmentClassifier
    {
        public int StructuralCalls { get; private set; }

        public int MaskedValueCalls { get; private set; }

        public bool IsBlindedObservation(
            ConceptId conceptId,
            ObservationValue? value,
            BlindedConceptMetadata? concept,
            string? collectionSpecId,
            string? itemId,
            IReadOnlyList<ProtocolDesign> designs)
        {
            if (value is MissingValue { Reason: NullFlavor.Masked })
            {
                MaskedValueCalls++;
                return true;
            }

            StructuralCalls++;
            return conceptId == blindedConceptId;
        }

        public bool IsBlindedCollectionSpec(
            string? collectionSpecId,
            string? collectionSpecName = null,
            string? operationalNeed = null,
            IReadOnlyList<ProtocolDesign>? designs = null)
        {
            StructuralCalls++;
            return false;
        }

        public bool IsBlindedCollectionItem(
            string? collectionSpecId,
            string? collectionTitle,
            CollectionItem? item,
            ObservationValue? value = null,
            BlindedConceptMetadata? concept = null,
            IReadOnlyList<ProtocolDesign>? designs = null)
        {
            if (value is MissingValue { Reason: NullFlavor.Masked })
            {
                MaskedValueCalls++;
                return true;
            }

            StructuralCalls++;
            return item is not null && item.Concept.ConceptId == blindedConceptId;
        }

        public bool IsBlindedVariable(
            string? defaultDataset,
            string? variable,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null)
        {
            StructuralCalls++;
            return false;
        }

        public bool IsBlindedFinding(
            FindingTarget target,
            IReadOnlyList<ObservedVariable> observedValues,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null)
        {
            StructuralCalls++;
            return false;
        }
    }
}
