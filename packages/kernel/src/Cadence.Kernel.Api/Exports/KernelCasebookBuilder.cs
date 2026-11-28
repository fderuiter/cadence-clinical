using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelCasebookBuilder
{
    public static KernelCasebookResponse BuildCasebookModel(
        EnrollmentCasebookSnapshot snapshot,
        DateTimeOffset generatedAt,
        string requestedBy,
        MaskReason? enrollmentMask,
        bool mayUnblind)
    {
        if (enrollmentMask is { } hiddenReason)
        {
            var maskedReasonText = hiddenReason.ToString();
            var maskedDigest = ComputeCanonicalCasebookDigest(
                snapshot.StudyCode,
                "masked",
                snapshot.ValidAt,
                snapshot.KnownAt,
                [],
                [],
                [],
                [],
                [],
                [],
                []);

            return new KernelCasebookResponse(
                Producer: ProducerName,
                GeneratedAt: generatedAt,
                RequestedBy: requestedBy,
                ValidAt: snapshot.ValidAt,
                KnownAt: snapshot.KnownAt,
                CasebookDigestSha256: maskedDigest,
                StudyRef: NodeRef.For(snapshot.StudyId).ToString(),
                StudyCode: snapshot.StudyCode,
                StudyTitle: snapshot.StudyTitle,
                SiteRef: "masked",
                SiteCode: "masked",
                EnrollmentRef: "masked",
                SubjectCode: "masked",
                EnrollmentStatus: null,
                Masked: true,
                Mask: maskedReasonText,
                Forms: [],
                Observations: [],
                Queries: [],
                Verifications: [],
                Locks: [],
                Attestations: [],
                AuditTrail: [],
                ProtocolVersions: snapshot.ProtocolVersions);
        }

        var designs = snapshot.ProtocolVersions
            .Where(p => p.Design is not null)
            .Select(p => p.Design!)
            .ToList();

        var blindedObservationIds = new HashSet<ObservationId>();
        var observations = new List<KernelCasebookObservationView>(snapshot.Observations.Count);

        foreach (var obs in snapshot.Observations)
        {
            if (obs.IsBlindedTreatment && !mayUnblind)
            {
                blindedObservationIds.Add(obs.ObservationId);

                var maskedRevisions = obs.Revisions
                    .Select(r => new KernelCasebookRevisionView(
                        r.Number,
                        "masked",
                        "masked",
                        null,
                        null,
                        ReasonForChange: "masked",
                        RecordedBy: null,
                        r.RecordedAt,
                        r.RecordedUntil,
                        Superseded: r.RecordedUntil is not null))
                    .ToList();

                observations.Add(new KernelCasebookObservationView(
                    Ref: "masked",
                    CollectionInstanceRef: null,
                    obs.CollectionSpecId,
                    obs.ItemId,
                    obs.Concept.ToString(),
                    obs.ConceptCode,
                    obs.ConceptLabel,
                    obs.ObservedAt,
                    obs.CurrentRevision.Number,
                    ValueKind: "masked",
                    DisplayValue: "masked",
                    NumericValue: null,
                    Unit: null,
                    EffectiveLock: obs.EffectiveLock?.ToString(),
                    Masked: true,
                    Mask: MaskReason.Blinded.ToString(),
                    Revisions: maskedRevisions,
                    GroupingFormId: obs.CollectionInstanceId?.Value));
                continue;
            }

            var revisions = obs.Revisions
                .Select(r =>
                {
                    var revNumeric = r.Value is QuantityValue qv ? qv.Value : (decimal?)null;
                    var revUnit = r.Value is QuantityValue qu ? qu.Unit : null;
                    var revDisplay = r.Value is QuantityValue qr ? qr.ToResultString() : r.Value.ToDisplayString();
                    return new KernelCasebookRevisionView(
                        r.Number,
                        r.Value.Kind.ToString(),
                        revDisplay,
                        revNumeric,
                        revUnit,
                        r.ReasonForChange,
                        r.RecordedBy,
                        r.RecordedAt,
                        r.RecordedUntil,
                        Superseded: r.RecordedUntil is not null);
                })
                .ToList();

            var cur = obs.CurrentRevision.Value;
            observations.Add(new KernelCasebookObservationView(
                NodeRef.For(obs.ObservationId, obs.CurrentRevision.Number).ToString(),
                obs.CollectionInstanceId is { } cid2 ? NodeRef.For(cid2).ToString() : null,
                obs.CollectionSpecId,
                obs.ItemId,
                obs.Concept.ToString(),
                obs.ConceptCode,
                obs.ConceptLabel,
                obs.ObservedAt,
                obs.CurrentRevision.Number,
                cur.Kind.ToString(),
                cur is QuantityValue cq ? cq.ToResultString() : cur.ToDisplayString(),
                cur is QuantityValue cqv ? cqv.Value : null,
                cur is QuantityValue cqu ? cqu.Unit : null,
                obs.EffectiveLock?.ToString(),
                Masked: false,
                Mask: null,
                Revisions: revisions,
                GroupingFormId: obs.CollectionInstanceId?.Value));
        }

        var formsWithBlindedObservations = new HashSet<Guid>();
        var completelyBlindedFormGuids = new HashSet<Guid>();
        var forms = snapshot.Collections
            .Select(c =>
            {
                var hasBlindedObs = !mayUnblind && c.ItemIdsByObservation.Keys.Any(blindedObservationIds.Contains);
                var isBlindedForm = !mayUnblind
                    && (BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(c.CollectionSpecId, c.CollectionSpecName, designs: designs)
                        || (c.ItemIdsByObservation.Count > 0 && c.ItemIdsByObservation.Keys.All(blindedObservationIds.Contains)));
                if (hasBlindedObs || isBlindedForm)
                {
                    formsWithBlindedObservations.Add(c.CollectionInstanceId.Value);
                }

                if (isBlindedForm)
                {
                    completelyBlindedFormGuids.Add(c.CollectionInstanceId.Value);
                }

                return new KernelCasebookFormView(
                    isBlindedForm ? "masked" : NodeRef.For(c.CollectionInstanceId).ToString(),
                    c.CollectionSpecId,
                    c.CollectionSpecName,
                    c.PlannedActivityId,
                    c.PlannedActivityName,
                    c.CollectedAt,
                    c.OpenedAt,
                    isBlindedForm ? null : c.OpenedBy,
                    hasBlindedObs || isBlindedForm ? null : c.ContentHashAtCut,
                    Masked: isBlindedForm,
                    Mask: isBlindedForm ? MaskReason.Blinded.ToString() : null,
                    GroupingFormId: c.CollectionInstanceId.Value);
            })
            .ToList();

        var blindedGuids = blindedObservationIds.Select(id => id.Value).ToHashSet();
        var blindedQueryGuids = snapshot.Assertions
            .Where(a => a.Kind == AssertionKind.Queried
                && ((a.Target.Kind == NodeKinds.Observation && blindedGuids.Contains(a.Target.Id))
                    || (a.Target.Kind == NodeKinds.CollectionInstance && formsWithBlindedObservations.Contains(a.Target.Id))))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();

        var queries = snapshot.Assertions
            .Where(a => a.Kind is AssertionKind.Queried or AssertionKind.QueryAnswered)
            .Select(a =>
            {
                bool targetBlinded = (a.Target.Kind == NodeKinds.Observation && blindedGuids.Contains(a.Target.Id))
                    || (a.Target.Kind == NodeKinds.CollectionInstance && formsWithBlindedObservations.Contains(a.Target.Id))
                    || (a.Target.Kind == NodeKinds.Assertion && blindedQueryGuids.Contains(a.Target.Id));
                return new KernelCasebookQueryView(
                    targetBlinded ? "masked" : NodeRef.For(a.AssertionId).ToString(),
                    a.Kind.ToString(),
                    targetBlinded ? "masked" : a.Target.ToString(),
                    targetBlinded ? "masked" : a.Text,
                    targetBlinded ? null : a.AssertedBy,
                    a.AssertedAt,
                    targetBlinded ? "masked" : a.ReasonForChange,
                    Closed: a.RetractedBy is not null,
                    ClosedAt: a.RetractedAt,
                    Revision: targetBlinded ? null : a.Revision);
            })
            .ToList();

        var verifications = snapshot.Assertions
            .Where(a => a.Kind == AssertionKind.Verified)
            .Select(a =>
            {
                bool targetBlinded = a.Target.Kind == NodeKinds.Observation && blindedGuids.Contains(a.Target.Id);
                return new KernelCasebookVerificationView(
                    targetBlinded ? "masked" : NodeRef.For(a.AssertionId).ToString(),
                    targetBlinded ? "masked" : a.Target.ToString(),
                    targetBlinded ? null : a.Revision,
                    targetBlinded ? null : a.AssertedBy,
                    a.AssertedAt,
                    targetBlinded ? "masked" : a.ReasonForChange,
                    Retracted: a.RetractedBy is not null,
                    RetractedAt: a.RetractedAt);
            })
            .ToList();

        bool IsCasebookLockBlinded(StudyLockEntry l) =>
            !mayUnblind
            && (l.BlindedScope
                || (l.Scope.Kind == NodeKinds.Observation && blindedGuids.Contains(l.Scope.Id))
                || (l.Scope.Kind == NodeKinds.CollectionInstance && formsWithBlindedObservations.Contains(l.Scope.Id)));

        var blindedLockTargetGuids = new HashSet<Guid>();
        var blindedLockAttestationIds = new HashSet<AttestationId>();
        foreach (var l in snapshot.Locks.Where(IsCasebookLockBlinded))
        {
            blindedLockTargetGuids.Add(l.LockId.Value);
            blindedLockAttestationIds.Add(l.AttestationId);
            if (l.ReleaseAssertionId is { } relId)
            {
                blindedLockTargetGuids.Add(relId.Value);
            }

            if (l.ReleaseAttestationId is { } relAttId)
            {
                blindedLockAttestationIds.Add(relAttId);
            }
        }

        var locks = snapshot.Locks
            .Select(l => IsCasebookLockBlinded(l)
                ? KernelLockResponse.FromMasked(l, MaskReason.Blinded)
                : KernelLockResponse.FromVisible(l))
            .ToList();

        var blindedLockGuids = snapshot.Locks
            .Where(IsCasebookLockBlinded)
            .SelectMany(l => l.ReleaseAssertionId is { } rel ? new[] { l.LockId.Value, rel.Value } : [l.LockId.Value])
            .ToHashSet();

        var attestations = snapshot.Attestations
            .Select(a =>
            {
                bool isBlindedObsOrLock = (a.TargetKind == NodeKinds.Observation && blindedGuids.Contains(a.TargetId))
                    || blindedLockTargetGuids.Contains(a.TargetId)
                    || blindedLockAttestationIds.Contains(a.AttestationId)
                    || completelyBlindedFormGuids.Contains(a.TargetId);
                bool isBlindedForm = formsWithBlindedObservations.Contains(a.TargetId);
                if (isBlindedObsOrLock)
                {
                    return new KernelCasebookAttestationView(
                        "masked",
                        "masked",
                        a.Meaning.ToString(),
                        "masked",
                        a.AuthenticatedAt,
                        a.Method,
                        a.RecordedAt,
                        "masked",
                        "masked");
                }

                return new KernelCasebookAttestationView(
                    NodeRef.For(a.AttestationId).ToString(),
                    new NodeRef(a.TargetKind, a.TargetId).ToString(),
                    a.Meaning.ToString(),
                    isBlindedForm ? "masked" : a.ActorId,
                    a.AuthenticatedAt,
                    a.Method,
                    a.RecordedAt,
                    isBlindedForm ? "masked" : a.TargetContentHash,
                    isBlindedForm ? "masked" : a.Reason);
            })
            .ToList();

        var auditTrail = ProjectAuditTrail(snapshot, mayUnblind, blindedObservationIds.Count > 0 || formsWithBlindedObservations.Count > 0 || blindedLockGuids.Count > 0);

        var canonicalDigest = ComputeCanonicalCasebookDigest(
            snapshot.StudyCode,
            snapshot.SubjectCode,
            snapshot.ValidAt,
            snapshot.KnownAt,
            forms,
            observations,
            queries,
            verifications,
            locks,
            attestations,
            auditTrail);

        return new KernelCasebookResponse(
            Producer: ProducerName,
            GeneratedAt: generatedAt,
            RequestedBy: requestedBy,
            ValidAt: snapshot.ValidAt,
            KnownAt: snapshot.KnownAt,
            CasebookDigestSha256: canonicalDigest,
            StudyRef: NodeRef.For(snapshot.StudyId).ToString(),
            StudyCode: snapshot.StudyCode,
            StudyTitle: snapshot.StudyTitle,
            SiteRef: NodeRef.For(snapshot.StudySiteId).ToString(),
            SiteCode: snapshot.SiteCode,
            EnrollmentRef: NodeRef.For(snapshot.EnrollmentId).ToString(),
            SubjectCode: snapshot.SubjectCode,
            EnrollmentStatus: snapshot.StatusAtCut?.ToString(),
            Masked: false,
            Mask: null,
            Forms: forms,
            Observations: observations,
            Queries: queries,
            Verifications: verifications,
            Locks: locks,
            Attestations: attestations,
            AuditTrail: auditTrail,
            ProtocolVersions: snapshot.ProtocolVersions);
    }

    public static Task WriteCasebookJsonAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(destination, casebook, CasebookJsonOptions, cancellationToken);

    private static IReadOnlyList<ObservationAuditView> ProjectAuditTrail(
        EnrollmentCasebookSnapshot snapshot,
        bool mayUnblind,
        bool hasBlindedData)
    {
        if (mayUnblind || snapshot.AuditEntries.Count == 0 || !hasBlindedData)
        {
            return snapshot.AuditEntries;
        }

        if (snapshot.BlindedAuditSequences is { } blindedSequences)
        {
            if (blindedSequences.Count == 0)
            {
                return snapshot.AuditEntries;
            }

            return snapshot.AuditEntries
                .Select(entry => blindedSequences.Contains(entry.Sequence)
                    ? new ObservationAuditView(entry.Sequence, entry.RecordedAt, "masked", "masked", "masked", null, "masked")
                    : entry)
                .ToList();
        }

        return snapshot.AuditEntries
            .Select(entry => string.Equals(entry.Subject, "Enrollment", StringComparison.OrdinalIgnoreCase)
                ? entry
                : new ObservationAuditView(entry.Sequence, entry.RecordedAt, "masked", "masked", "masked", null, "masked"))
            .ToList();
    }

    private static string ComputeCanonicalCasebookDigest(
        string studyCode,
        string subjectCode,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        IReadOnlyList<KernelCasebookFormView> forms,
        IReadOnlyList<KernelCasebookObservationView> observations,
        IReadOnlyList<KernelCasebookQueryView> queries,
        IReadOnlyList<KernelCasebookVerificationView> verifications,
        IReadOnlyList<KernelLockResponse> locks,
        IReadOnlyList<KernelCasebookAttestationView> attestations,
        IReadOnlyList<ObservationAuditView> auditTrail)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            study_code = studyCode,
            subject_code = subjectCode,
            valid_at = FormatInstant(validAt),
            known_at = FormatInstant(knownAt),
            forms,
            observations,
            queries,
            verifications,
            locks,
            attestations,
            audit_trail = auditTrail,
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
