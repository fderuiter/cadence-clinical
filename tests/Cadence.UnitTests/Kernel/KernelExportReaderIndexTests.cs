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
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Verifies that indexed snapshot assembly and blinding memoization in <see cref="KernelExportReader"/> and
/// <see cref="BlindedObservationClassifier"/> preserve exact parity with the reference semantics across bitemporal cuts,
/// deterministic <c>(AssertedAt, Id)</c> tie-breaking, lock precedence, protocol amendments, and per-observation
/// <see cref="NullFlavor.Masked"/> values.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelExportReaderIndexTests
{
    private static readonly DateTimeOffset T0 = FakeClock.DefaultStart;

    [Fact(DisplayName = "ExportReaderIndex_AssembleAndBlindedAuditKeys_MatchReferenceOracleAcrossBitemporalCuts")]
    public void AssembleAndBlindedAuditKeysMatchReferenceOracleAcrossBitemporalCuts()
    {
        var tenant = TenantId.New();
        var study = StudyId.New();
        var siteA = StudySiteId.New();
        var siteB = StudySiteId.New();
        var participant1 = ParticipantId.New();
        var participant2 = ParticipantId.New();
        var participant3 = ParticipantId.New();

        var enrollment1 = Enrollment.Screen(
            EnrollmentId.New(), tenant, participant1, study, siteA, SubjectCode.From("SUBJ-001"), T0, T0).Value!;
        var enrollment2 = Enrollment.Screen(
            EnrollmentId.New(), tenant, participant2, study, siteA, SubjectCode.From("SUBJ-002"), T0, T0).Value!;
        var enrollment3 = Enrollment.Screen(
            EnrollmentId.New(), tenant, participant3, study, siteB, SubjectCode.From("SUBJ-003"), T0, T0).Value!;

        var studySiteByEnrollment = new Dictionary<EnrollmentId, StudySiteId>
        {
            [enrollment1.Id] = siteA,
            [enrollment2.Id] = siteA,
            [enrollment3.Id] = siteB,
        };

        var sbpConcept = Concept.Define(
            ConceptId.New(),
            tenant,
            ConceptOrigin.CdiscBiomedicalConcept,
            "C25298",
            ConceptKind.BiomedicalConcept,
            "Systolic Blood Pressure v1",
            "Arterial systolic pressure.",
            T0).Value!;
        sbpConcept.Revise("Systolic Blood Pressure v2", "Arterial systolic pressure revised.", T0.AddMinutes(5)).Should().BeNull();

        var trtConcept = Concept.Define(
            ConceptId.New(),
            tenant,
            ConceptOrigin.Sponsor,
            null,
            ConceptKind.BiomedicalConcept,
            "Investigational Drug Kit",
            "Blinded randomization kit number.",
            T0).Value!;

        var unindexedConceptRef = new ConceptRef(ConceptId.New(), 1);
        var conceptsById = new Dictionary<ConceptId, Concept>
        {
            [sbpConcept.Id] = sbpConcept,
            [trtConcept.Id] = trtConcept,
        };

        var protocolV1Id = ProtocolVersionId.New();
        var protocolV2Id = ProtocolVersionId.New();

        var designV1 = new ProtocolDesign
        {
            Title = "Phase III Cardiology v1",
            Epochs = [new Epoch("ep.trt", "Treatment", 1)],
            Interventions = [new Intervention("int.active", "Active Investigational Product", new ConceptRef(trtConcept.Id, 1))],
            PlannedActivities =
            [
                new PlannedActivity("act.v1", "Week 4 Visit (v1)", "ep.trt", [ActivityLocation.Site], ["cs.vitals", "cs.rand"]),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs (v1)",
                    [
                        new CollectionItem("item.sbp", new ConceptRef(sbpConcept.Id, 1), "mm[Hg]", Required: true),
                        new CollectionItem("item.note", unindexedConceptRef, ValueKind: ObservationValueKind.Text),
                    ]),
                new CollectionSpec(
                    "cs.rand",
                    "Randomization Allocation",
                    [
                        new CollectionItem("item.kit", new ConceptRef(trtConcept.Id, 1), ValueKind: ObservationValueKind.Text),
                    ]),
                new CollectionSpec(
                    "cs.dispense",
                    "Kit Check",
                    [
                        new CollectionItem("item.dispense", new ConceptRef(trtConcept.Id, 1), ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };

        var designV2 = designV1 with
        {
            Title = "Phase III Cardiology v2",
            PlannedActivities =
            [
                new PlannedActivity("act.v1", "Week 4 Visit (Amended v2)", "ep.trt", [ActivityLocation.Site], ["cs.vitals", "cs.rand"]),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs (Amended v2)",
                    [
                        new CollectionItem("item.sbp", new ConceptRef(sbpConcept.Id, 2), "mm[Hg]", Required: true),
                        new CollectionItem("item.note", unindexedConceptRef, ValueKind: ObservationValueKind.Text),
                    ]),
                designV1.CollectionSpecs[1],
                designV1.CollectionSpecs[2],
            ],
        };

        IReadOnlyList<ExportProtocolSnapshot> protocols =
        [
            new ExportProtocolSnapshot(protocolV1Id, 1, ProtocolVersionStatus.Superseded, designV1, "{}", "sha256:" + new string('1', 64), null),
            new ExportProtocolSnapshot(protocolV2Id, 2, ProtocolVersionStatus.Approved, designV2, "{}", "sha256:" + new string('2', 64), null),
        ];
        IReadOnlyList<ProtocolDesign> designs = [designV1, designV2];

        // Masked observation first on (sbpConcept v1, cs.vitals, item.sbp), followed by unmasked observation on the exact same tuple.
        var obsMaskedFirst = Observation.Record(
            ObservationId.New(), tenant, enrollment1.Id, participant1, study, new ConceptRef(sbpConcept.Id, 1),
            new MissingValue(NullFlavor.Masked, "Withheld per DSMB"), T0.AddMinutes(10), "Initial", "crc@site", T0.AddMinutes(10)).Value!;

        var obsUnmaskedAfterMasked = Observation.Record(
            ObservationId.New(), tenant, enrollment2.Id, participant2, study, new ConceptRef(sbpConcept.Id, 1),
            new QuantityValue(122m, "mm[Hg]"), T0.AddMinutes(11), "Initial", "crc@site", T0.AddMinutes(11)).Value!;
        obsUnmaskedAfterMasked.Correct(new QuantityValue(118m, "mm[Hg]"), "Source chart recount", "crc@site", T0.AddMinutes(30)).Should().BeNull();

        // Unmasked observation on v2 concept that is later corrected to Masked at T0 + 35m.
        var obsCorrectedToMasked = Observation.Record(
            ObservationId.New(), tenant, enrollment3.Id, participant3, study, new ConceptRef(sbpConcept.Id, 2),
            new QuantityValue(130m, "mm[Hg]"), T0.AddMinutes(12), "Initial", "crc@site", T0.AddMinutes(12)).Value!;
        obsCorrectedToMasked.Correct(new MissingValue(NullFlavor.Masked), "Masked post-review", "dm@sponsor", T0.AddMinutes(35)).Should().BeNull();

        var obsBlindedDrug = Observation.Record(
            ObservationId.New(), tenant, enrollment1.Id, participant1, study, new ConceptRef(trtConcept.Id, 1),
            new TextValue("KIT-9041"), T0.AddMinutes(13), "Dispensed", "pharm@site", T0.AddMinutes(13)).Value!;

        var obsUnindexedConcept = Observation.Record(
            ObservationId.New(), tenant, enrollment2.Id, participant2, study, unindexedConceptRef,
            new TextValue("Resting quietly"), T0.AddMinutes(14), "Entered", "crc@site", T0.AddMinutes(14)).Value!;

        var obsRecordedLate = Observation.Record(
            ObservationId.New(), tenant, enrollment3.Id, participant3, study, new ConceptRef(sbpConcept.Id, 2),
            new QuantityValue(126m, "mm[Hg]"), T0.AddMinutes(15), "Late entry", "crc@site", T0.AddMinutes(50)).Value!;

        var coll1 = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment1, protocolV1Id, "cs.vitals", "act.v1", T0.AddMinutes(10), "crc@site", T0.AddMinutes(10));
        coll1.Join("item.sbp", obsMaskedFirst.Id, T0.AddMinutes(10));

        var coll2 = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment2, protocolV1Id, "cs.vitals", "act.v1", T0.AddMinutes(11), "crc@site", T0.AddMinutes(11));
        coll2.Join("item.sbp", obsUnmaskedAfterMasked.Id, T0.AddMinutes(11));
        coll2.Join("item.note", obsUnindexedConcept.Id, T0.AddMinutes(25));

        var coll3 = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment3, protocolV2Id, "cs.vitals", "act.v1", T0.AddMinutes(12), "crc@site", T0.AddMinutes(12));
        coll3.Join("item.sbp", obsCorrectedToMasked.Id, T0.AddMinutes(12));

        var collRand = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment1, protocolV1Id, "cs.rand", "act.v1", T0.AddMinutes(13), "pharm@site", T0.AddMinutes(13));
        collRand.Join("item.kit", obsBlindedDrug.Id, T0.AddMinutes(13));

        // Empty collection instance whose spec contains a blinded item (tests BuildBlindedAuditKeys item-level spec scan).
        var collEmptyBlindedItemSpec = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment2, protocolV2Id, "cs.dispense", null, T0.AddMinutes(14), "pharm@site", T0.AddMinutes(14));

        // Unmapped spec/activity fallback check.
        var collUnmappedSpec = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment3, protocolV2Id, "cs.custom", "act.custom", T0.AddMinutes(15), "crc@site", T0.AddMinutes(15));

        IReadOnlyList<CollectionInstance> collectionsRaw = [coll1, coll2, coll3, collRand, collEmptyBlindedItemSpec, collUnmappedSpec];
        IReadOnlyList<Observation> observationsRaw =
        [
            obsMaskedFirst,
            obsUnmaskedAfterMasked,
            obsCorrectedToMasked,
            obsBlindedDrug,
            obsUnindexedConcept,
            obsRecordedLate,
        ];

        // Assertions: verified, queried, two answers sharing the exact same AssertedAt, and two retractions sharing AssertedAt.
        var verify1 = Assertion.Verify(AssertionId.New(), obsUnmaskedAfterMasked, 1, "cra@sponsor", T0.AddMinutes(16));
        var query1 = Assertion.Query(AssertionId.New(), obsUnmaskedAfterMasked, "Confirm cuff size", "dm@sponsor", T0.AddMinutes(18), 1).Value!;

        var answerIdLow = new AssertionId(Guid.Parse("00000000-0000-0000-0000-000000000010"));
        var answerIdHigh = new AssertionId(Guid.Parse("00000000-0000-0000-0000-000000000020"));
        var answerA = Assertion.Answer(answerIdHigh, query1, "Standard adult cuff", "crc@site", T0.AddMinutes(22), 1).Value!;
        var answerB = Assertion.Answer(answerIdLow, query1, "Verified in nurse note", "crc@site", T0.AddMinutes(22), 1).Value!;

        var retractIdEarlyGuid = new AssertionId(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var retractIdLateGuid = new AssertionId(Guid.Parse("00000000-0000-0000-0000-000000000099"));
        var retractTieHigh = Assertion.Retract(retractIdLateGuid, verify1, null, "cra@sponsor", T0.AddMinutes(28));
        var retractTieLow = Assertion.Retract(retractIdEarlyGuid, verify1, null, "cra@sponsor", T0.AddMinutes(28));

        var queryBlinded = Assertion.Query(AssertionId.New(), obsBlindedDrug, "Verify kit seal", "pharm@site", T0.AddMinutes(19), 1).Value!;
        var retractQueryBlinded = Assertion.Retract(AssertionId.New(), queryBlinded, null, "pharm@site", T0.AddMinutes(26));

        IReadOnlyList<Assertion> assertionsRaw =
        [
            verify1,
            query1,
            answerA,
            answerB,
            retractTieHigh,
            retractTieLow,
            queryBlinded,
            retractQueryBlinded,
        ];

        // Locks: Frozen at siteA, Locked at enrollment2 (overrides siteA Frozen!), Frozen at enrollment1, released Locked at siteB, Locked on obsCorrectedToMasked.
        var lockFrozenSiteA = NewLock(study, null, siteA, LockKind.Frozen, NodeRef.For(siteA), blinded: false, released: false, T0.AddMinutes(20));
        var lockHardEnrollment2 = NewLock(study, enrollment2.Id, siteA, LockKind.Locked, NodeRef.For(enrollment2.Id), blinded: false, released: false, T0.AddMinutes(21));
        var lockFrozenColl2 = NewLock(study, enrollment2.Id, siteA, LockKind.Frozen, NodeRef.For(coll2.Id), blinded: false, released: false, T0.AddMinutes(22));
        var lockReleasedStudy = NewLock(study, null, null, LockKind.Locked, NodeRef.For(study), blinded: false, released: true, T0.AddMinutes(15));
        var lockHardObs3 = NewLock(study, enrollment3.Id, siteB, LockKind.Locked, NodeRef.For(obsCorrectedToMasked.Id), blinded: true, released: false, T0.AddMinutes(36));

        IReadOnlyList<StudyLockEntry> lockEntries =
        [
            lockFrozenSiteA,
            lockHardEnrollment2,
            lockFrozenColl2,
            lockReleasedStudy,
            lockHardObs3,
        ];

        IReadOnlyList<ExportAttestationSnapshot> attestations =
        [
            new ExportAttestationSnapshot(
                AttestationId.New(), "pharm@site", AttestationMeaning.Approved, NodeKinds.CollectionInstance,
                collRand.Id.Value, "sha256:" + new string('a', 64), T0.AddMinutes(14), "pwd+otp", T0.AddMinutes(14), "Signed randomization"),
            new ExportAttestationSnapshot(
                AttestationId.New(), "crc@site", AttestationMeaning.Approved, NodeKinds.CollectionInstance,
                coll2.Id.Value, "sha256:" + new string('b', 64), T0.AddMinutes(15), "pwd+otp", T0.AddMinutes(15), "Signed vitals"),
            new ExportAttestationSnapshot(
                AttestationId.New(), "dm@sponsor", AttestationMeaning.Approved, NodeKinds.Assertion,
                lockHardObs3.LockId.Value, "sha256:" + new string('c', 64), T0.AddMinutes(36), "pwd+otp", T0.AddMinutes(36), "Signed lock"),
        ];

        foreach (var knownAt in new[] { T0.AddMinutes(20), T0.AddMinutes(32), T0.AddMinutes(60) })
        {
            var assertionsAtCut = assertionsRaw.Where(a => a.AssertedAt <= knownAt).ToList();
            var locksAtCut = lockEntries.Where(l => l.LockedAt <= knownAt).ToList();
            var attestationsAtCut = attestations.Where(a => a.RecordedAt <= knownAt).ToList();

            var (actualCollections, actualObservations) = KernelExportReader.AssembleObservationsAndCollections(
                protocols,
                designs,
                conceptsById,
                studySiteByEnrollment,
                collectionsRaw,
                observationsRaw,
                assertionsAtCut,
                locksAtCut,
                knownAt);

            var (expectedCollections, expectedObservations) = ReferenceAssemble(
                protocols,
                designs,
                conceptsById,
                studySiteByEnrollment,
                collectionsRaw,
                observationsRaw,
                assertionsAtCut,
                locksAtCut,
                knownAt);

            actualCollections.Should().HaveCount(expectedCollections.Count);
            for (var i = 0; i < expectedCollections.Count; i++)
            {
                actualCollections[i].Should().BeEquivalentTo(expectedCollections[i], opts => opts.WithStrictOrdering());
            }

            actualObservations.Should().HaveCount(expectedObservations.Count);
            for (var i = 0; i < expectedObservations.Count; i++)
            {
                actualObservations[i].Should().BeEquivalentTo(expectedObservations[i], opts => opts.WithStrictOrdering());
            }

            var actualBlindedKeys = KernelExportReader.BuildBlindedAuditKeys(
                actualCollections, actualObservations, locksAtCut, attestationsAtCut, designs);
            var expectedBlindedKeys = ReferenceBuildBlindedAuditKeys(
                expectedCollections, expectedObservations, locksAtCut, attestationsAtCut, designs);

            actualBlindedKeys.Should().BeEquivalentTo(expectedBlindedKeys);
        }

        // Verify key invariants explicitly on the final cut:
        var (_, finalObs) = KernelExportReader.AssembleObservationsAndCollections(
            protocols, designs, conceptsById, studySiteByEnrollment, collectionsRaw, observationsRaw, assertionsRaw, lockEntries, T0.AddMinutes(60));
        finalObs.Single(o => o.ObservationId == obsMaskedFirst.Id).IsBlindedTreatment.Should().BeTrue();
        finalObs.Single(o => o.ObservationId == obsUnmaskedAfterMasked.Id).IsBlindedTreatment.Should().BeFalse(
            "a sibling observation with NullFlavor.Masked must not pollute the memoized item blinding cache");
        finalObs.Single(o => o.ObservationId == obsUnmaskedAfterMasked.Id).EffectiveLock.Should().Be(
            LockKind.Locked,
            "enrollment Locked must override site and form Frozen regardless of lock insertion order");

        var obs2Assertions = finalObs.Single(o => o.ObservationId == obsUnmaskedAfterMasked.Id).Assertions;
        obs2Assertions.Single(a => a.AssertionId == verify1.Id).RetractedBy.Should().Be(
            retractIdEarlyGuid,
            "tied retraction timestamps must deterministically pick the lowest AssertionId");
        obs2Assertions.Where(a => a.Kind == AssertionKind.QueryAnswered).Select(a => a.AssertionId).Should().ContainInOrder(
            answerIdLow, answerIdHigh);
    }

    [Fact(DisplayName = "BlindedObservationLookup_PreservesNullFlavorMasked_WithoutPollutingSharedItemCache")]
    public void BlindedObservationLookupPreservesNullFlavorMaskedWithoutPollutingSharedItemCache()
    {
        var tenant = TenantId.New();
        var sbp = Concept.Define(
            ConceptId.New(), tenant, ConceptOrigin.CdiscBiomedicalConcept, "C25298",
            ConceptKind.BiomedicalConcept, "Systolic Blood Pressure", "Systolic BP.", T0).Value!;
        var concepts = new Dictionary<ConceptId, Concept> { [sbp.Id] = sbp };
        var design = new ProtocolDesign
        {
            Title = "Vitals Study",
            CollectionSpecs =
            [
                new CollectionSpec("cs.vitals", "Vital Signs", [new CollectionItem("item.sbp", new ConceptRef(sbp.Id, 1), "mm[Hg]")]),
            ],
        };

        var lookup = BlindedObservationClassifier.CreateLookup(concepts, [design]);

        lookup.IsBlindedTreatment(sbp.Id, new MissingValue(NullFlavor.Masked), "cs.vitals", "item.sbp")
            .Should().BeTrue("masked values are always treated as blinded");
        lookup.IsBlindedTreatment(sbp.Id, new QuantityValue(120m, "mm[Hg]"), "cs.vitals", "item.sbp")
            .Should().BeFalse("prior masked sibling must not cache true for unmasked observations");
        lookup.IsBlindedTreatment(sbp.Id, new MissingValue(NullFlavor.NotDone), "cs.vitals", "item.sbp")
            .Should().BeFalse("non-masked null flavors are not blinded");
        lookup.IsBlindedTreatment(sbp.Id, new MissingValue(NullFlavor.Masked), "cs.vitals", "item.sbp")
            .Should().BeTrue("masked values must still return true after false was cached for the item");
    }

    private static StudyLockEntry NewLock(
        StudyId study,
        EnrollmentId? enrollment,
        StudySiteId? site,
        LockKind kind,
        NodeRef scope,
        bool blinded,
        bool released,
        DateTimeOffset lockedAt) =>
        new(
            AssertionId.New(),
            study,
            enrollment,
            site,
            enrollment is null ? null : "SUBJ",
            kind,
            scope,
            blinded,
            AttestationId.New(),
            AttestationMeaning.Approved,
            lockedAt,
            "dm@sponsor",
            "Lock applied",
            released,
            released ? AssertionId.New() : null,
            released ? AttestationId.New() : null,
            released ? lockedAt.AddMinutes(1) : null,
            released ? "dm@sponsor" : null,
            released ? "Lock released" : null);

    private static (IReadOnlyList<ExportCollectionSnapshot> Collections, IReadOnlyList<ExportObservationSnapshot> Observations) ReferenceAssemble(
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, Concept> conceptsById,
        IReadOnlyDictionary<EnrollmentId, StudySiteId> studySiteByEnrollment,
        IReadOnlyList<CollectionInstance> collectionsRaw,
        IReadOnlyList<Observation> observationsRaw,
        IReadOnlyList<Assertion> assertionsRaw,
        IReadOnlyList<StudyLockEntry> lockEntries,
        DateTimeOffset knownAt)
    {
        var designByVersion = protocols.Where(p => p.Design is not null).ToDictionary(p => p.ProtocolVersionId, p => p.Design!);
        var collectionByObservation = new Dictionary<ObservationId, (CollectionInstance Instance, string ItemId)>();
        foreach (var instance in collectionsRaw)
        {
            foreach (var member in instance.Members.Where(m => m.JoinedAt <= knownAt))
            {
                collectionByObservation[member.ObservationId] = (instance, member.ItemId);
            }
        }

        var activeLocks = lockEntries.Where(l => !l.Released).ToList();
        var nonLockStated = assertionsRaw.Where(a => !a.IsLock && a.RetractsId is null).ToList();
        var nonLockRetractedBy = assertionsRaw
            .Where(a => !a.IsLock && a.RetractsId is not null)
            .GroupBy(a => a.RetractsId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.AssertedAt).ThenBy(r => r.Id).First());
        var answersByQuery = nonLockStated
            .Where(a => a.Kind == AssertionKind.QueryAnswered && a.TargetKind == NodeKinds.Assertion)
            .GroupBy(a => AssertionId.FromGuid(a.TargetId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var currentAtCutByObservation = new Dictionary<ObservationId, (int Revision, ObservationValue Value)>();
        var observationSnapshots = new List<ExportObservationSnapshot>();
        foreach (var obs in observationsRaw)
        {
            var revsAtCut = obs.Revisions.Where(r => r.RecordedAt <= knownAt).OrderBy(r => r.Number).ToList();
            if (revsAtCut.Count == 0)
            {
                continue;
            }

            var views = revsAtCut
                .Select((r, idx) => new ObservationRevisionView(
                    r.Number, r.Value, r.ReasonForChange, r.RecordedBy, r.RecordedAt,
                    idx + 1 < revsAtCut.Count ? revsAtCut[idx + 1].RecordedAt : null))
                .ToList();
            var currentRev = views[^1];
            currentAtCutByObservation[obs.Id] = (currentRev.Number, currentRev.Value);

            collectionByObservation.TryGetValue(obs.Id, out var membership);
            var studySiteId = studySiteByEnrollment[obs.EnrollmentId];
            conceptsById.TryGetValue(obs.ConceptId, out var concept);
            var revLabel = concept?.Revisions.FirstOrDefault(r => r.Version == obs.ConceptVersion)?.PreferredLabel
                ?? concept?.PreferredLabel
                ?? obs.ConceptId.ToString();
            var conceptCode = concept?.ExternalCode ?? obs.ConceptId.ToString();

            var isBlinded = BlindedObservationClassifier.IsBlindedTreatment(
                obs.ConceptId, currentRev.Value, concept, membership.Instance?.CollectionSpecId, membership.ItemId, designs);

            var coveringScopes = LockScopes.Covering(
                obs.StudyId, studySiteId, obs.EnrollmentId, obs.Id, membership.Instance?.Id);
            var matchingLocks = activeLocks
                .Where(l => coveringScopes.Any(s => s.Kind == l.Scope.Kind && s.Id == l.Scope.Id))
                .ToList();
            LockKind? effectiveLock = matchingLocks.Count switch
            {
                0 => null,
                _ when matchingLocks.Any(l => l.Kind == LockKind.Locked) => LockKind.Locked,
                _ => LockKind.Frozen,
            };

            var directAssertions = nonLockStated
                .Where(a => a.TargetKind == NodeKinds.Observation && a.TargetId == obs.Id.Value)
                .ToList();
            var queryAnswers = directAssertions
                .Where(a => a.Kind == AssertionKind.Queried)
                .SelectMany(q => answersByQuery.GetValueOrDefault(q.Id, []))
                .ToList();
            var aboutViews = directAssertions
                .Concat(queryAnswers)
                .OrderBy(a => a.AssertedAt)
                .ThenBy(a => a.Id)
                .Select(a => nonLockRetractedBy.TryGetValue(a.Id, out var ret)
                    ? new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, ret.Id, ret.AssertedAt)
                    : new AssertionView(a.Id, a.Kind, a.Target, a.TargetRevision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, null, null))
                .ToList();

            observationSnapshots.Add(new ExportObservationSnapshot(
                obs.Id, obs.EnrollmentId, obs.ParticipantId, studySiteId,
                membership.Instance?.Id, membership.Instance?.CollectionSpecId, membership.Instance?.PlannedActivityId,
                membership.ItemId, obs.Concept, conceptCode, revLabel, isBlinded, obs.ObservedAt,
                currentRev, views, effectiveLock, aboutViews));
        }

        var collectionSnapshots = new List<ExportCollectionSnapshot>(collectionsRaw.Count);
        foreach (var instance in collectionsRaw)
        {
            designByVersion.TryGetValue(instance.ProtocolVersionId, out var design);
            var specName = design?.CollectionSpecs.FirstOrDefault(s => s.Id == instance.CollectionSpecId)?.Name
                ?? instance.CollectionSpecId;
            var activityName = instance.PlannedActivityId is null
                ? null
                : design?.PlannedActivities.FirstOrDefault(a => a.Id == instance.PlannedActivityId)?.Name ?? instance.PlannedActivityId;

            var itemsMap = new Dictionary<ObservationId, string>();
            var hashEntries = new List<CollectionContentEntry>();
            foreach (var member in instance.Members.Where(m => m.JoinedAt <= knownAt))
            {
                if (currentAtCutByObservation.TryGetValue(member.ObservationId, out var current))
                {
                    itemsMap[member.ObservationId] = member.ItemId;
                    hashEntries.Add(new CollectionContentEntry(member.ObservationId, current.Revision, current.Value));
                }
            }

            var contentHash = hashEntries.Count == 0 ? null : CollectionInstance.ContentHashOf(hashEntries);
            collectionSnapshots.Add(new ExportCollectionSnapshot(
                instance.Id, instance.EnrollmentId, instance.StudySiteId, instance.ProtocolVersionId,
                instance.CollectionSpecId, specName, instance.PlannedActivityId, activityName,
                instance.CollectedAt, instance.OpenedAt, instance.OpenedBy, contentHash, itemsMap));
        }

        return (collectionSnapshots, observationSnapshots);
    }

    private static HashSet<string> ReferenceBuildBlindedAuditKeys(
        IReadOnlyList<ExportCollectionSnapshot> collections,
        IReadOnlyList<ExportObservationSnapshot> observations,
        IReadOnlyList<StudyLockEntry> locks,
        IReadOnlyList<ExportAttestationSnapshot> attestations,
        IReadOnlyList<ProtocolDesign> designs)
    {
        var blindedObsIds = observations.Where(o => o.IsBlindedTreatment).Select(o => o.ObservationId).ToHashSet();
        var blindedObsGuids = blindedObsIds.Select(id => id.Value).ToHashSet();
        var blindedCollectionGuids = observations
            .Where(o => o.IsBlindedTreatment && o.CollectionInstanceId is not null)
            .Select(o => o.CollectionInstanceId!.Value.Value)
            .ToHashSet();

        foreach (var coll in collections)
        {
            if (BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(coll.CollectionSpecId, coll.CollectionSpecName, designs: designs)
                || coll.ItemIdsByObservation.Keys.Any(blindedObsIds.Contains))
            {
                blindedCollectionGuids.Add(coll.CollectionInstanceId.Value);
                continue;
            }

            var spec = designs.SelectMany(d => d.CollectionSpecs)
                .FirstOrDefault(s => string.Equals(s.Id, coll.CollectionSpecId, StringComparison.Ordinal));
            if (spec is not null && spec.Items.Any(i => BlindedObservationClassifier.IsBlindedTreatment(
                    i.Concept.ConceptId, new TextValue(string.Empty), concept: null, coll.CollectionSpecId, i.Id, designs)))
            {
                blindedCollectionGuids.Add(coll.CollectionInstanceId.Value);
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obs in observations.Where(o => o.IsBlindedTreatment))
        {
            keys.Add(obs.ObservationId.ToString());
            foreach (var assertion in obs.Assertions)
            {
                keys.Add(assertion.AssertionId.ToString());
                if (assertion.RetractedBy is { } retractedBy)
                {
                    keys.Add(retractedBy.ToString());
                }
            }
        }

        foreach (var collGuid in blindedCollectionGuids)
        {
            keys.Add(CollectionInstanceId.FromGuid(collGuid).ToString());
        }

        var blindedLockTargetGuids = new HashSet<Guid>();
        foreach (var l in locks.Where(l => l.BlindedScope))
        {
            keys.Add(l.LockId.ToString());
            blindedLockTargetGuids.Add(l.LockId.Value);
            keys.Add(l.AttestationId.ToString());
            if (l.ReleaseAssertionId is { } relId)
            {
                keys.Add(relId.ToString());
                blindedLockTargetGuids.Add(relId.Value);
            }

            if (l.ReleaseAttestationId is { } relAttId)
            {
                keys.Add(relAttId.ToString());
            }
        }

        foreach (var att in attestations)
        {
            if (blindedObsGuids.Contains(att.TargetId)
                || blindedCollectionGuids.Contains(att.TargetId)
                || blindedLockTargetGuids.Contains(att.TargetId))
            {
                keys.Add(att.AttestationId.ToString());
            }
        }

        return keys;
    }
}
