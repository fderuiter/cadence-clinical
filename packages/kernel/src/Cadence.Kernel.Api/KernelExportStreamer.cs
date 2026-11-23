using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>
/// Streamed format writers for CSV, CDISC ODM v1.3.2 XML, CDISC Dataset-JSON v1.1.0, and participant casebooks.
/// Preserves <see cref="decimal"/> precision with <see cref="CultureInfo.InvariantCulture"/> and uses caller-provided
/// <c>IClock</c> timestamps exclusively.
/// </summary>
internal static class KernelExportStreamer
{
    public const string ProducerName = "Cadence.Kernel.Api/CasebookExporter/1.0";
    private const string OdmNamespace = "http://www.cdisc.org/ns/odm/v1.3";
    private const string CadenceNamespace = "https://cadence.clinical/ns/kernel-export/v1";

    private static readonly JsonSerializerOptions CasebookJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static async Task WriteStudyCsvAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16384, leaveOpen: true);

        const string header = "study_code,site_code,subject_code,enrollment_ref,observation_ref,collection_instance_ref,collection_spec_id,planned_activity_id,item_id,concept_ref,concept_code,concept_label,observed_at,revision,value_kind,result_text,result_decimal,result_unit,lock_state,masked,mask_reason,recorded_by,recorded_at,valid_at,known_at";
        await writer.WriteLineAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);

        var validAtIso = FormatInstant(export.Snapshot.ValidAt);
        var knownAtIso = FormatInstant(export.Snapshot.KnownAt);

        foreach (var row in export.Rows)
        {
            var fields = new[]
            {
                row.StudyCode,
                row.SiteCode,
                row.SubjectCode,
                row.EnrollmentRef,
                row.ObservationRef,
                row.CollectionInstanceRef ?? string.Empty,
                row.CollectionSpecId,
                row.PlannedActivityId ?? string.Empty,
                row.ItemId,
                row.ConceptRef,
                row.ConceptCode,
                row.ConceptLabel,
                row.ObservedAt is { } obsAt ? FormatInstant(obsAt) : string.Empty,
                row.Revision?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.ValueKind,
                row.ResultText,
                row.ResultDecimal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.ResultUnit ?? string.Empty,
                row.LockState ?? string.Empty,
                row.Masked ? "true" : "false",
                row.MaskReason ?? string.Empty,
                row.RecordedBy ?? string.Empty,
                row.RecordedAt is { } recAt ? FormatInstant(recAt) : string.Empty,
                validAtIso,
                knownAtIso,
            };

            var line = string.Join(',', fields.Select(EscapeCsvField));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteStudyDatasetJsonAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken)
    {
        var snapshot = export.Snapshot;
        var latestVersion = snapshot.ProtocolVersions.Count > 0 ? snapshot.ProtocolVersions[^1].Number : 1;
        var mdvOid = $"MDV.{latestVersion.ToString(CultureInfo.InvariantCulture)}";

        await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("datasetJSONCreationDateTime", FormatInstant(export.GeneratedAt));
        writer.WriteString("datasetJSONVersion", "1.1.0");
        writer.WriteString("fileOID", $"Cadence.Export.{snapshot.StudyCode}.{snapshot.KnownAt.ToUniversalTime():yyyyMMddTHHmmssZ}");
        writer.WriteString("originator", "Cadence Clinical Kernel");
        writer.WritePropertyName("sourceSystem");
        writer.WriteStartObject();
        writer.WriteString("name", "Cadence.Kernel.Api");
        writer.WriteString("version", "1.0.0");
        writer.WriteEndObject();
        writer.WriteString("studyOID", snapshot.StudyCode);
        writer.WriteString("metaDataVersionOID", mdvOid);
        writer.WriteString("metaDataRef", $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/studies/{NodeRef.For(snapshot.StudyId)}/protocol-versions");

        writer.WritePropertyName("asOf");
        writer.WriteStartObject();
        writer.WriteString("valid_at", FormatInstant(snapshot.ValidAt));
        writer.WriteString("known_at", FormatInstant(snapshot.KnownAt));
        writer.WriteString("lock_filter", export.LockFilter);
        writer.WriteEndObject();

        writer.WritePropertyName("itemGroupData");
        writer.WriteStartObject();

        var groups = export.Rows
            .GroupBy(r => r.Masked && r.SourceObservation is null ? "IG.masked" : $"IG.{SanitizeOid(r.CollectionSpecId)}", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        if (groups.Count == 0)
        {
            WriteDatasetJsonGroup(writer, "IG.observations", "Clinical Observations", []);
        }
        else
        {
            foreach (var group in groups)
            {
                var specId = group.First().CollectionSpecId;
                var label = string.Equals(group.Key, "IG.masked", StringComparison.Ordinal)
                    ? "Masked Site Records"
                    : snapshot.Collections.FirstOrDefault(c => string.Equals(c.CollectionSpecId, specId, StringComparison.Ordinal))?.CollectionSpecName
                        ?? specId;
                WriteDatasetJsonGroup(writer, group.Key, label, group.ToList());
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteStudyOdmXmlAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken)
    {
        var snapshot = export.Snapshot;
        var latestProtocol = snapshot.ProtocolVersions.Count > 0 ? snapshot.ProtocolVersions[^1] : null;
        var mdvOid = $"MDV.{(latestProtocol?.Number ?? 1).ToString(CultureInfo.InvariantCulture)}";

        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            CloseOutput = false,
        };

        await using var xml = XmlWriter.Create(destination, settings);
        await xml.WriteStartDocumentAsync().ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "ODM", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("xmlns", "cadence", null, CadenceNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "ODMVersion", null, "1.3.2").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "FileType", null, "Snapshot").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "FileOID", null, $"ODM.{snapshot.StudyCode}.{snapshot.KnownAt.ToUniversalTime():yyyyMMddTHHmmssZ}").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "CreationDateTime", null, FormatInstant(export.GeneratedAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "AsOfDateTime", null, FormatInstant(snapshot.KnownAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "Originator", null, "Cadence Clinical Kernel").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "SourceSystem", null, "Cadence.Kernel.Api").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "ValidAt", CadenceNamespace, FormatInstant(snapshot.ValidAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "KnownAt", CadenceNamespace, FormatInstant(snapshot.KnownAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "LockFilter", CadenceNamespace, export.LockFilter).ConfigureAwait(false);

        await WriteOdmStudyMetadataAsync(
            xml,
            snapshot.StudyCode,
            snapshot.StudyTitle,
            snapshot.ProtocolVersions,
            export.Rows.Where(r => !r.Masked || r.SourceObservation is not null).ToList()).ConfigureAwait(false);

        var blindedCollectionIds = export.Rows
            .Where(r => r.Masked && r.SourceObservation?.CollectionInstanceId is not null)
            .Select(r => r.SourceObservation!.CollectionInstanceId!.Value)
            .ToHashSet();

        var visibleTargetIds = export.Rows
            .Where(r => !r.Masked && r.SourceObservation is not null)
            .SelectMany(r => new[] { r.SourceObservation!.ObservationId.Value, r.SourceObservation.EnrollmentId.Value }
                .Concat(r.SourceObservation.CollectionInstanceId is { } cid && !blindedCollectionIds.Contains(cid) ? [cid.Value] : []))
            .Concat(export.Rows.Where(r => r.SourceObservation is not null).Select(r => r.SourceObservation!.EnrollmentId.Value))
            .Append(snapshot.StudyId.Value)
            .ToHashSet();

        var visibleAttestations = snapshot.Attestations
            .Where(a => visibleTargetIds.Contains(a.TargetId))
            .ToList();

        await WriteOdmAdminDataAsync(xml, snapshot.StudyCode, visibleAttestations).ConfigureAwait(false);

        await xml.WriteStartElementAsync(null, "ClinicalData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "StudyOID", null, snapshot.StudyCode).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "MetaDataVersionOID", null, mdvOid).ConfigureAwait(false);

        var visibleRowsByEnrollment = export.Rows
            .Where(r => r.SourceObservation is not null)
            .GroupBy(r => r.EnrollmentRef, StringComparer.Ordinal)
            .ToList();

        var attestationsByTarget = visibleAttestations
            .GroupBy(a => a.TargetId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var enrollmentGroup in visibleRowsByEnrollment)
        {
            var first = enrollmentGroup.First();
            await xml.WriteStartElementAsync(null, "SubjectData", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "SubjectKey", null, first.SubjectCode).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "EnrollmentRef", CadenceNamespace, first.EnrollmentRef).ConfigureAwait(false);

            await xml.WriteStartElementAsync(null, "SiteRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "LocationOID", null, first.SiteCode).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);

            var byEvent = enrollmentGroup.GroupBy(r => r.PlannedActivityId ?? "SE.unscheduled", StringComparer.Ordinal);
            foreach (var eventGroup in byEvent)
            {
                await xml.WriteStartElementAsync(null, "StudyEventData", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "StudyEventOID", null, $"SE.{SanitizeOid(eventGroup.Key)}").ConfigureAwait(false);

                var byForm = eventGroup.GroupBy(r => r.CollectionInstanceRef ?? $"FORM.{r.CollectionSpecId}", StringComparer.Ordinal);
                foreach (var formGroup in byForm)
                {
                    var sample = formGroup.First();
                    var formOid = $"FORM.{SanitizeOid(sample.CollectionSpecId)}";
                    await xml.WriteStartElementAsync(null, "FormData", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "FormOID", null, formOid).ConfigureAwait(false);
                    if (sample.CollectionInstanceRef is not null)
                    {
                        await xml.WriteAttributeStringAsync(null, "FormRepeatKey", null, sample.CollectionInstanceRef).ConfigureAwait(false);
                    }

                    await xml.WriteStartElementAsync(null, "ItemGroupData", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "ItemGroupOID", null, $"IG.{SanitizeOid(sample.CollectionSpecId)}").ConfigureAwait(false);

                    foreach (var row in formGroup)
                    {
                        await WriteOdmItemDataAsync(xml, row).ConfigureAwait(false);
                    }

                    await xml.WriteEndElementAsync().ConfigureAwait(false);

                    if (!sample.Masked
                        && sample.CollectionInstanceRef is not null
                        && sample.SourceObservation?.CollectionInstanceId is { } formInstanceId
                        && !blindedCollectionIds.Contains(formInstanceId)
                        && attestationsByTarget.TryGetValue(formInstanceId.Value, out var formSigs))
                    {
                        foreach (var sig in formSigs)
                        {
                            await WriteOdmSignatureElementAsync(xml, sig, first.SiteCode).ConfigureAwait(false);
                        }
                    }

                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        var accessMaskedRows = export.Rows.Where(r => r.Masked && r.SourceObservation is null).ToList();
        for (var idx = 0; idx < accessMaskedRows.Count; idx++)
        {
            var maskedRow = accessMaskedRows[idx];
            await xml.WriteStartElementAsync(null, "SubjectData", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "SubjectKey", null, "masked").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "Masked", CadenceNamespace, "true").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "MaskReason", CadenceNamespace, maskedRow.MaskReason ?? "NotPermitted").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "PlaceholderSequence", CadenceNamespace, (idx + 1).ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);

            await xml.WriteStartElementAsync(null, "SiteRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "LocationOID", null, "masked").ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndDocumentAsync().ConfigureAwait(false);
        await xml.FlushAsync().ConfigureAwait(false);
    }

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
            var maskedForms = snapshot.Collections
                .Select(_ => new KernelCasebookFormView(
                    Ref: "masked",
                    CollectionSpecId: "masked",
                    Title: "masked",
                    PlannedActivityId: null,
                    PlannedActivityName: null,
                    CollectedAt: null,
                    OpenedAt: null,
                    OpenedBy: null,
                    ContentHash: null,
                    Masked: true,
                    Mask: maskedReasonText))
                .ToList();

            var maskedObservations = snapshot.Observations
                .Select(_ => new KernelCasebookObservationView(
                    Ref: "masked",
                    CollectionInstanceRef: null,
                    CollectionSpecId: "masked",
                    ItemId: "masked",
                    ConceptRef: "masked",
                    ConceptCode: "masked",
                    ConceptLabel: "masked",
                    ObservedAt: null,
                    CurrentRevision: null,
                    ValueKind: "masked",
                    DisplayValue: "masked",
                    NumericValue: null,
                    Unit: null,
                    EffectiveLock: null,
                    Masked: true,
                    Mask: maskedReasonText,
                    Revisions: []))
                .ToList();

            var maskedLocks = snapshot.Locks
                .Select(l => KernelLockResponse.FromMasked(l, hiddenReason))
                .ToList();

            var maskedDigest = ComputeCanonicalCasebookDigest(
                snapshot.StudyCode,
                "masked",
                snapshot.ValidAt,
                snapshot.KnownAt,
                maskedForms,
                maskedObservations,
                [],
                [],
                maskedLocks,
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
                Forms: maskedForms,
                Observations: maskedObservations,
                Queries: [],
                Verifications: [],
                Locks: maskedLocks,
                Attestations: [],
                AuditTrail: []);
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
                    Revisions: maskedRevisions));
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
                Revisions: revisions));
        }

        var formsWithBlindedObservations = new HashSet<Guid>();
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
                    Mask: isBlindedForm ? MaskReason.Blinded.ToString() : null);
            })
            .ToList();

        var blindedGuids = blindedObservationIds.Select(id => id.Value).ToHashSet();
        var blindedQueryGuids = snapshot.Assertions
            .Where(a => a.Kind == AssertionKind.Queried && a.Target.Kind == NodeKinds.Observation && blindedGuids.Contains(a.Target.Id))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();

        var queries = snapshot.Assertions
            .Where(a => a.Kind is AssertionKind.Queried or AssertionKind.QueryAnswered)
            .Select(a =>
            {
                bool targetBlinded = (a.Target.Kind == NodeKinds.Observation && blindedGuids.Contains(a.Target.Id))
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
                    a.Revision,
                    targetBlinded ? null : a.AssertedBy,
                    a.AssertedAt,
                    targetBlinded ? "masked" : a.ReasonForChange,
                    Retracted: a.RetractedBy is not null,
                    RetractedAt: a.RetractedAt);
            })
            .ToList();

        var locks = snapshot.Locks
            .Select(l => l.BlindedScope && !mayUnblind
                ? KernelLockResponse.FromMasked(l, MaskReason.Blinded)
                : KernelLockResponse.FromVisible(l))
            .ToList();

        var blindedLockGuids = snapshot.Locks
            .Where(l => l.BlindedScope && !mayUnblind)
            .SelectMany(l => l.ReleaseAssertionId is { } rel ? new[] { l.LockId.Value, rel.Value } : [l.LockId.Value])
            .ToHashSet();

        var attestations = snapshot.Attestations
            .Where(a => !((a.TargetKind == NodeKinds.Observation && blindedGuids.Contains(a.TargetId))
                || (a.TargetKind == NodeKinds.CollectionInstance && formsWithBlindedObservations.Contains(a.TargetId))
                || (a.TargetKind == NodeKinds.Assertion && blindedLockGuids.Contains(a.TargetId))))
            .Select(a => new KernelCasebookAttestationView(
                NodeRef.For(a.AttestationId).ToString(),
                new NodeRef(a.TargetKind, a.TargetId).ToString(),
                a.Meaning.ToString(),
                a.ActorId,
                a.AuthenticatedAt,
                a.Method,
                a.RecordedAt,
                a.TargetContentHash,
                a.Reason))
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
            AuditTrail: auditTrail);
    }

    public static Task WriteCasebookJsonAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(destination, casebook, CasebookJsonOptions, cancellationToken);

    public static async Task WriteCasebookOdmXmlAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            CloseOutput = false,
        };

        await using var xml = XmlWriter.Create(destination, settings);
        await xml.WriteStartDocumentAsync().ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "ODM", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("xmlns", "cadence", null, CadenceNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "ODMVersion", null, "1.3.2").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "FileType", null, "Snapshot").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "FileOID", null, $"Casebook.{casebook.StudyCode}.{casebook.SubjectCode}").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "CreationDateTime", null, FormatInstant(casebook.GeneratedAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "AsOfDateTime", null, FormatInstant(casebook.KnownAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "Originator", null, casebook.Producer).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "CasebookDigestSha256", CadenceNamespace, casebook.CasebookDigestSha256).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "ValidAt", CadenceNamespace, FormatInstant(casebook.ValidAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "KnownAt", CadenceNamespace, FormatInstant(casebook.KnownAt)).ConfigureAwait(false);

        await xml.WriteStartElementAsync(null, "ClinicalData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "StudyOID", null, casebook.StudyCode).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "MetaDataVersionOID", null, "MDV.1").ConfigureAwait(false);

        await xml.WriteStartElementAsync(null, "SubjectData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "SubjectKey", null, casebook.SubjectCode).ConfigureAwait(false);
        if (casebook.Masked)
        {
            await xml.WriteAttributeStringAsync("cadence", "Masked", CadenceNamespace, "true").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "MaskReason", CadenceNamespace, casebook.Mask ?? "NotPermitted").ConfigureAwait(false);
        }

        await xml.WriteStartElementAsync(null, "SiteRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "LocationOID", null, casebook.SiteCode).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);

        foreach (var obs in casebook.Observations)
        {
            await xml.WriteStartElementAsync(null, "ItemData", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "ItemOID", null, $"IT.{SanitizeOid(obs.ItemId ?? obs.ConceptCode)}").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Value", null, obs.DisplayValue).ConfigureAwait(false);
            if (obs.Masked)
            {
                await xml.WriteAttributeStringAsync("cadence", "Masked", CadenceNamespace, "true").ConfigureAwait(false);
                await xml.WriteAttributeStringAsync("cadence", "MaskReason", CadenceNamespace, obs.Mask ?? "Blinded").ConfigureAwait(false);
            }

            foreach (var rev in obs.Revisions)
            {
                await xml.WriteStartElementAsync(null, "AuditRecord", OdmNamespace).ConfigureAwait(false);
                await xml.WriteStartElementAsync(null, "UserRef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "UserOID", null, rev.RecordedBy ?? "masked").ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
                await xml.WriteElementStringAsync(null, "DateTimeStamp", OdmNamespace, FormatInstant(rev.RecordedAt)).ConfigureAwait(false);
                await xml.WriteElementStringAsync(null, "ReasonForChange", OdmNamespace, rev.ReasonForChange ?? string.Empty).ConfigureAwait(false);
                await xml.WriteElementStringAsync(null, "SourceID", OdmNamespace, $"{obs.Ref}#rev-{rev.Revision.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        foreach (var att in casebook.Attestations)
        {
            await xml.WriteStartElementAsync(null, "Signature", OdmNamespace).ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "UserRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "UserOID", null, att.SignerId).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "LocationRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "LocationOID", null, casebook.SiteCode).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "SignatureRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "SignatureOID", null, $"SD.{SanitizeOid(att.Ref)}").ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "DateTimeStamp", OdmNamespace, FormatInstant(att.SignedAt)).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndDocumentAsync().ConfigureAwait(false);
        await xml.FlushAsync().ConfigureAwait(false);
    }

    public static async Task WriteCasebookHtmlAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16384, leaveOpen: true);
        var sb = new StringBuilder(4096);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine($"  <title>Participant Casebook - {Html(casebook.SubjectCode)}</title>");
        sb.AppendLine("  <style>body{font-family:system-ui,sans-serif;margin:2rem;color:#111}table{width:100%;border-collapse:collapse;margin-bottom:1.5rem}th,td{border:1px solid #cbd5e1;padding:0.5rem;text-align:left;font-size:0.875rem}th{background:#f8fafc}.badge{display:inline-block;padding:0.15rem 0.45rem;border-radius:4px;background:#e2e8f0;font-size:0.75rem}</style>");
        sb.AppendLine("</head>");
        sb.AppendLine($"<body data-casebook-sha256=\"{Html(casebook.CasebookDigestSha256)}\">");
        sb.AppendLine($"  <h1>Participant Casebook: {Html(casebook.SubjectCode)}</h1>");
        sb.AppendLine($"  <p><strong>Study:</strong> {Html(casebook.StudyCode)} ({Html(casebook.StudyTitle)}) | <strong>Site:</strong> {Html(casebook.SiteCode)} | <strong>As Of:</strong> {Html(FormatInstant(casebook.KnownAt))} | <strong>Generated:</strong> {Html(FormatInstant(casebook.GeneratedAt))}</p>");
        sb.AppendLine($"  <p><strong>Producer:</strong> {Html(casebook.Producer)} | <strong>Canonical SHA-256:</strong> <code>{Html(casebook.CasebookDigestSha256)}</code></p>");

        if (casebook.Masked)
        {
            sb.AppendLine($"  <p class=\"badge\">masked ({Html(casebook.Mask ?? "NotPermitted")})</p>");
        }

        sb.AppendLine("  <h2>Clinical Observations &amp; Revisions</h2>");
        sb.AppendLine("  <table><thead><tr><th>Item</th><th>Concept</th><th>Current Value</th><th>Revision</th><th>Lock</th><th>Mask</th><th>History</th></tr></thead><tbody>");
        foreach (var obs in casebook.Observations)
        {
            var history = string.Join("; ", obs.Revisions.Select(r =>
                $"r{r.Revision.ToString(CultureInfo.InvariantCulture)}: {r.DisplayValue}{(r.Unit is not null ? $" {r.Unit}" : string.Empty)} ({r.ReasonForChange ?? "n/a"}){(r.Superseded ? " [superseded]" : string.Empty)}"));
            var valWithUnit = obs.Unit is not null && !obs.Masked ? $"{obs.DisplayValue} {obs.Unit}" : obs.DisplayValue;
            sb.AppendLine($"    <tr><td>{Html(obs.ItemId ?? "-")}</td><td>{Html(obs.ConceptLabel)}</td><td>{Html(valWithUnit)}</td><td>{Html(obs.CurrentRevision?.ToString(CultureInfo.InvariantCulture) ?? "-")}</td><td>{Html(obs.EffectiveLock ?? "Unlocked")}</td><td>{Html(obs.Mask ?? "-")}</td><td>{Html(history)}</td></tr>");
        }

        sb.AppendLine("  </tbody></table>");

        sb.AppendLine("  <h2>Queries &amp; Source Data Verifications</h2>");
        sb.AppendLine("  <table><thead><tr><th>Kind</th><th>Target</th><th>Detail</th><th>Actor</th><th>Recorded At</th><th>State</th></tr></thead><tbody>");
        foreach (var q in casebook.Queries)
        {
            var targetLabel = q.Revision is { } qRev
                ? $"{q.TargetRef} (r{qRev.ToString(CultureInfo.InvariantCulture)})"
                : q.TargetRef;
            var detail = !string.IsNullOrWhiteSpace(q.ReasonForChange) && q.ReasonForChange != "masked"
                ? $"{q.Text ?? string.Empty} ({q.ReasonForChange})"
                : q.Text ?? string.Empty;
            sb.AppendLine($"    <tr><td>{Html(q.Kind)}</td><td>{Html(targetLabel)}</td><td>{Html(detail)}</td><td>{Html(q.AssertedBy ?? "-")}</td><td>{Html(FormatInstant(q.AssertedAt))}</td><td>{(q.Closed ? "Closed" : "Open")}</td></tr>");
        }

        foreach (var v in casebook.Verifications)
        {
            sb.AppendLine($"    <tr><td>Verified</td><td>{Html(v.ObservationRef)} (r{Html(v.Revision?.ToString(CultureInfo.InvariantCulture) ?? "1")})</td><td>{Html(v.ReasonForChange ?? string.Empty)}</td><td>{Html(v.VerifiedBy ?? "-")}</td><td>{Html(FormatInstant(v.VerifiedAt))}</td><td>{(v.Retracted ? "Retracted" : "Active")}</td></tr>");
        }

        sb.AppendLine("  </tbody></table>");

        sb.AppendLine("  <h2>Electronic Signatures (21 CFR Part 11 Attestations)</h2>");
        sb.AppendLine("  <table><thead><tr><th>Attestation</th><th>Target</th><th>Meaning</th><th>Signer</th><th>Method</th><th>Signed At</th><th>Content Hash</th></tr></thead><tbody>");
        foreach (var a in casebook.Attestations)
        {
            sb.AppendLine($"    <tr><td>{Html(a.Ref)}</td><td>{Html(a.TargetRef)}</td><td>{Html(a.Meaning)}</td><td>{Html(a.SignerId)}</td><td>{Html(a.Method)}</td><td>{Html(FormatInstant(a.SignedAt))}</td><td><code>{Html(a.TargetContentHash)}</code></td></tr>");
        }

        sb.AppendLine("  </tbody></table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        await writer.WriteAsync(sb.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteDatasetJsonGroup(
        Utf8JsonWriter writer,
        string groupOid,
        string label,
        IReadOnlyList<ProjectedObservationRow> rows)
    {
        writer.WritePropertyName(groupOid);
        writer.WriteStartObject();
        writer.WriteNumber("records", rows.Count);
        writer.WriteString("name", groupOid);
        writer.WriteString("label", label);

        writer.WritePropertyName("items");
        writer.WriteStartArray();
        WriteColumnItem(writer, "IT.row_number", "row_number", "Row Number", "integer");
        WriteColumnItem(writer, "IT.study_code", "study_code", "Study Code", "string");
        WriteColumnItem(writer, "IT.site_code", "site_code", "Site Code", "string");
        WriteColumnItem(writer, "IT.subject_code", "subject_code", "Pseudonymous Subject Code", "string");
        WriteColumnItem(writer, "IT.enrollment_ref", "enrollment_ref", "Enrollment Reference", "string");
        WriteColumnItem(writer, "IT.observation_ref", "observation_ref", "Observation Revision Reference", "string");
        WriteColumnItem(writer, "IT.collection_instance_ref", "collection_instance_ref", "Collection Instance Reference", "string");
        WriteColumnItem(writer, "IT.planned_activity_id", "planned_activity_id", "Planned Activity", "string");
        WriteColumnItem(writer, "IT.item_id", "item_id", "Collection Item Identifier", "string");
        WriteColumnItem(writer, "IT.concept_ref", "concept_ref", "Concept Reference", "string");
        WriteColumnItem(writer, "IT.concept_code", "concept_code", "Concept Code", "string");
        WriteColumnItem(writer, "IT.concept_label", "concept_label", "Concept Label", "string");
        WriteColumnItem(writer, "IT.observed_at", "observed_at", "Observed Timestamp", "datetime");
        WriteColumnItem(writer, "IT.revision", "revision", "Observation Revision", "integer");
        WriteColumnItem(writer, "IT.value_kind", "value_kind", "Observation Value Kind", "string");
        WriteColumnItem(writer, "IT.result_text", "result_text", "Entered Result", "string");
        WriteColumnItem(writer, "IT.result_decimal", "result_decimal", "Decimal Quantity Result", "decimal");
        WriteColumnItem(writer, "IT.result_unit", "result_unit", "UCUM Unit", "string");
        WriteColumnItem(writer, "IT.lock_state", "lock_state", "Effective Lock State", "string");
        WriteColumnItem(writer, "IT.masked", "masked", "Masked Flag", "boolean");
        WriteColumnItem(writer, "IT.mask_reason", "mask_reason", "Mask Reason", "string");
        WriteColumnItem(writer, "IT.recorded_by", "recorded_by", "Recorded By Actor", "string");
        WriteColumnItem(writer, "IT.recorded_at", "recorded_at", "Recorded Timestamp", "datetime");
        writer.WriteEndArray();

        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            writer.WriteStartArray();
            writer.WriteNumberValue(i + 1);
            writer.WriteStringValue(r.StudyCode);
            writer.WriteStringValue(r.SiteCode);
            writer.WriteStringValue(r.SubjectCode);
            writer.WriteStringValue(r.EnrollmentRef);
            writer.WriteStringValue(r.ObservationRef);
            WriteNullableString(writer, r.CollectionInstanceRef);
            WriteNullableString(writer, r.PlannedActivityId);
            writer.WriteStringValue(r.ItemId);
            writer.WriteStringValue(r.ConceptRef);
            writer.WriteStringValue(r.ConceptCode);
            writer.WriteStringValue(r.ConceptLabel);
            WriteNullableString(writer, r.ObservedAt is { } obsAt ? FormatInstant(obsAt) : null);
            if (r.Revision is { } rev)
            {
                writer.WriteNumberValue(rev);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteStringValue(r.ValueKind);
            writer.WriteStringValue(r.ResultText);
            if (r.ResultDecimal is { } dec)
            {
                writer.WriteNumberValue(dec);
            }
            else
            {
                writer.WriteNullValue();
            }

            WriteNullableString(writer, r.ResultUnit);
            WriteNullableString(writer, r.LockState);
            writer.WriteBooleanValue(r.Masked);
            WriteNullableString(writer, r.MaskReason);
            WriteNullableString(writer, r.RecordedBy);
            WriteNullableString(writer, r.RecordedAt is { } recAt ? FormatInstant(recAt) : null);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteColumnItem(Utf8JsonWriter writer, string oid, string name, string label, string dataType)
    {
        writer.WriteStartObject();
        writer.WriteString("OID", oid);
        writer.WriteString("name", name);
        writer.WriteString("label", label);
        writer.WriteString("dataType", dataType);
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }

    private static async Task WriteOdmStudyMetadataAsync(
        XmlWriter xml,
        string studyCode,
        string studyTitle,
        IReadOnlyList<ExportProtocolSnapshot> protocols,
        IReadOnlyList<ProjectedObservationRow> visibleRows)
    {
        await xml.WriteStartElementAsync(null, "Study", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "OID", null, studyCode).ConfigureAwait(false);

        await xml.WriteStartElementAsync(null, "GlobalVariables", OdmNamespace).ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "StudyName", OdmNamespace, studyCode).ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "StudyDescription", OdmNamespace, studyTitle).ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "ProtocolName", OdmNamespace, studyCode).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);

        if (protocols.Count == 0)
        {
            await WriteSingleMetaDataVersionAsync(xml, 1, design: null, compiledOdmXml: null, visibleRows).ConfigureAwait(false);
        }
        else
        {
            foreach (var protocol in protocols)
            {
                var compiledXml = protocol.CompiledOdmXml ?? TryCompileUsdmOdmViaReflection(protocol.RawContent, studyCode, protocol.Number.ToString(CultureInfo.InvariantCulture));
                await WriteSingleMetaDataVersionAsync(xml, protocol.Number, protocol.Design, compiledXml, visibleRows).ConfigureAwait(false);
            }
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task WriteSingleMetaDataVersionAsync(
        XmlWriter xml,
        int versionNumber,
        ProtocolDesign? design,
        string? compiledOdmXml,
        IReadOnlyList<ProjectedObservationRow> visibleRows)
    {
        var numText = versionNumber.ToString(CultureInfo.InvariantCulture);
        await xml.WriteStartElementAsync(null, "MetaDataVersion", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "OID", null, $"MDV.{numText}").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "Name", null, $"Version {numText}").ConfigureAwait(false);

        var writtenItemOids = new HashSet<string>(StringComparer.Ordinal);

        if (design is not null)
        {
            if (design.PlannedActivities.Count > 0)
            {
                await xml.WriteStartElementAsync(null, "Protocol", OdmNamespace).ConfigureAwait(false);
                foreach (var activity in design.PlannedActivities)
                {
                    await xml.WriteStartElementAsync(null, "StudyEventRef", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "StudyEventOID", null, $"SE.{SanitizeOid(activity.Id)}").ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Mandatory", null, "Yes").ConfigureAwait(false);
                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);

                foreach (var activity in design.PlannedActivities)
                {
                    await xml.WriteStartElementAsync(null, "StudyEventDef", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "OID", null, $"SE.{SanitizeOid(activity.Id)}").ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Name", null, activity.Name).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Repeating", null, "No").ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Type", null, "Scheduled").ConfigureAwait(false);
                    foreach (var specId in activity.CollectionSpecIds)
                    {
                        await xml.WriteStartElementAsync(null, "FormRef", OdmNamespace).ConfigureAwait(false);
                        await xml.WriteAttributeStringAsync(null, "FormOID", null, $"FORM.{SanitizeOid(specId)}").ConfigureAwait(false);
                        await xml.WriteAttributeStringAsync(null, "Mandatory", null, "Yes").ConfigureAwait(false);
                        await xml.WriteEndElementAsync().ConfigureAwait(false);
                    }

                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }
            }

            foreach (var spec in design.CollectionSpecs)
            {
                var sanitizedSpec = SanitizeOid(spec.Id);
                await xml.WriteStartElementAsync(null, "FormDef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "OID", null, $"FORM.{sanitizedSpec}").ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Name", null, spec.Name).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Repeating", null, "No").ConfigureAwait(false);
                await xml.WriteStartElementAsync(null, "ItemGroupRef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "ItemGroupOID", null, $"IG.{sanitizedSpec}").ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Mandatory", null, "Yes").ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);

                await xml.WriteStartElementAsync(null, "ItemGroupDef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "OID", null, $"IG.{sanitizedSpec}").ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Name", null, spec.Name).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Repeating", null, "No").ConfigureAwait(false);
                foreach (var item in spec.Items)
                {
                    var itemOid = $"IT.{SanitizeOid(item.Id)}";
                    await xml.WriteStartElementAsync(null, "ItemRef", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "ItemOID", null, itemOid).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Mandatory", null, item.Required ? "Yes" : "No").ConfigureAwait(false);
                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);

                foreach (var item in spec.Items)
                {
                    var itemOid = $"IT.{SanitizeOid(item.Id)}";
                    if (!writtenItemOids.Add(itemOid))
                    {
                        continue;
                    }

                    await xml.WriteStartElementAsync(null, "ItemDef", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "OID", null, itemOid).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Name", null, item.Id).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "DataType", null, item.Unit is not null ? "decimal" : "text").ConfigureAwait(false);
                    await xml.WriteStartElementAsync(null, "Alias", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Context", null, "Cadence.Concept").ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Name", null, item.Concept.ToString()).ConfigureAwait(false);
                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(compiledOdmXml))
        {
            TryCopyCompiledMetaDataChildren(xml, compiledOdmXml, writtenItemOids);
        }

        foreach (var row in visibleRows)
        {
            var itemOid = $"IT.{SanitizeOid(row.ItemId)}";
            if (!writtenItemOids.Add(itemOid))
            {
                continue;
            }

            await xml.WriteStartElementAsync(null, "ItemDef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "OID", null, itemOid).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Name", null, row.ConceptLabel).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "DataType", null, row.ResultDecimal is not null ? "decimal" : "text").ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static void TryCopyCompiledMetaDataChildren(XmlWriter xml, string compiledOdmXml, HashSet<string> writtenItemOids)
    {
        try
        {
            var doc = XDocument.Parse(compiledOdmXml);
            var ns = XNamespace.Get(OdmNamespace);
            var mdv = doc.Descendants(ns + "MetaDataVersion").FirstOrDefault();
            if (mdv is null)
            {
                return;
            }

            foreach (var element in mdv.Elements())
            {
                var oidAttr = element.Attribute("OID")?.Value;
                if (element.Name.LocalName == "ItemDef" && oidAttr is not null)
                {
                    writtenItemOids.Add(oidAttr);
                }

                element.WriteTo(xml);
            }
        }
        catch (XmlException)
        {
            // Ignore malformed precompiled XML and fall back to row-synthesized ItemDefs.
        }
    }

    private static string? TryCompileUsdmOdmViaReflection(string rawContent, string studyCode, string versionTag)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        try
        {
            var ddfAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Cadence.Ddf")
                ?? System.Reflection.Assembly.Load("Cadence.Ddf");
            var compilerType = ddfAssembly?.GetType("Cadence.Ddf.UsdmCompiler");
            if (compilerType is null)
            {
                return null;
            }

            var methods = compilerType.GetMethods().Where(m => m.Name == "CompileUsdm").ToList();
            var target = methods.FirstOrDefault(m => m.GetParameters().Length == 6);
            if (target is null)
            {
                return null;
            }

            var compilation = target.Invoke(null, [rawContent, studyCode, versionTag, null, null, null]);
            return compilation?.GetType().GetProperty("OdmXml")?.GetValue(compilation) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task WriteOdmAdminDataAsync(
        XmlWriter xml,
        string studyCode,
        IReadOnlyList<ExportAttestationSnapshot> attestations)
    {
        if (attestations.Count == 0)
        {
            return;
        }

        await xml.WriteStartElementAsync(null, "AdminData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "StudyOID", null, studyCode).ConfigureAwait(false);
        foreach (var att in attestations)
        {
            await xml.WriteStartElementAsync(null, "SignatureDef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "OID", null, $"SD.{att.AttestationId}").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Methodology", null, "Electronic").ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "Meaning", OdmNamespace, att.Meaning.ToString()).ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "LegalReason", OdmNamespace, att.Reason).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task WriteOdmItemDataAsync(XmlWriter xml, ProjectedObservationRow row)
    {
        var itemOid = $"IT.{SanitizeOid(row.ItemId)}";
        await xml.WriteStartElementAsync(null, "ItemData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "ItemOID", null, itemOid).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "Value", null, row.ResultText).ConfigureAwait(false);
        if (row.ResultUnit is not null)
        {
            await xml.WriteAttributeStringAsync("cadence", "Unit", CadenceNamespace, row.ResultUnit).ConfigureAwait(false);
        }

        if (row.LockState is not null)
        {
            await xml.WriteAttributeStringAsync("cadence", "LockState", CadenceNamespace, row.LockState).ConfigureAwait(false);
        }

        if (row.Masked)
        {
            await xml.WriteAttributeStringAsync("cadence", "Masked", CadenceNamespace, "true").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "MaskReason", CadenceNamespace, row.MaskReason ?? "Blinded").ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            return;
        }

        if (row.SourceObservation is { } source)
        {
            var latestRev = source.CurrentRevision;
            await xml.WriteStartElementAsync(null, "AuditRecord", OdmNamespace).ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "UserRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "UserOID", null, latestRev.RecordedBy).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "LocationRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "LocationOID", null, row.SiteCode).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "DateTimeStamp", OdmNamespace, FormatInstant(latestRev.RecordedAt)).ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "ReasonForChange", OdmNamespace, latestRev.ReasonForChange).ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "SourceID", OdmNamespace, row.ObservationRef).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);

            if (source.Revisions.Count > 1)
            {
                await xml.WriteStartElementAsync("cadence", "Revisions", CadenceNamespace).ConfigureAwait(false);
                foreach (var rev in source.Revisions)
                {
                    await xml.WriteStartElementAsync("cadence", "Revision", CadenceNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Number", null, rev.Number.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    var revDisplay = rev.Value is QuantityValue qv ? qv.ToResultString() : rev.Value.ToDisplayString();
                    await xml.WriteAttributeStringAsync(null, "Value", null, revDisplay).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "RecordedBy", null, rev.RecordedBy).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "RecordedAt", null, FormatInstant(rev.RecordedAt)).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "ReasonForChange", null, rev.ReasonForChange).ConfigureAwait(false);
                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task WriteOdmSignatureElementAsync(XmlWriter xml, ExportAttestationSnapshot att, string siteCode)
    {
        await xml.WriteStartElementAsync(null, "Signature", OdmNamespace).ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "UserRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "UserOID", null, att.ActorId).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "LocationRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "LocationOID", null, siteCode).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "SignatureRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "SignatureOID", null, $"SD.{att.AttestationId}").ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "DateTimeStamp", OdmNamespace, FormatInstant(att.RecordedAt)).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }

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
                    ? entry with { ActorId = "masked", Action = "masked", ReasonForChange = "masked" }
                    : entry)
                .ToList();
        }

        return snapshot.AuditEntries
            .Select(entry => string.Equals(entry.Subject, "Enrollment", StringComparison.OrdinalIgnoreCase)
                ? entry
                : entry with { ActorId = "masked", Action = "masked", ReasonForChange = "masked" })
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

    private static string EscapeCsvField(string value)
    {
        if (value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private static string SanitizeOid(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "item";
        }

        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_').ToArray();
        return new string(chars);
    }

    private static string FormatInstant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string Html(string text) => WebUtility.HtmlEncode(text);
}
