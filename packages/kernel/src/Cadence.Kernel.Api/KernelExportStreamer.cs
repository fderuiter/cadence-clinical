using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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

        var visibleTargetIds = export.Rows
            .Where(r => r.SourceObservation is not null)
            .SelectMany(r => new[] { r.SourceObservation!.ObservationId.Value, r.SourceObservation.EnrollmentId.Value }
                .Concat(r.SourceObservation.CollectionInstanceId is { } cid ? [cid.Value] : []))
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

                    if (sample.SourceObservation?.CollectionInstanceId is { } formInstanceId
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

        var forms = snapshot.Collections
            .Select(c => new KernelCasebookFormView(
                NodeRef.For(c.CollectionInstanceId).ToString(),
                c.CollectionSpecId,
                c.CollectionSpecName,
                c.PlannedActivityId,
                c.PlannedActivityName,
                c.CollectedAt,
                c.OpenedAt,
                c.OpenedBy,
                c.ContentHashAtCut,
                Masked: false,
                Mask: null))
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
                    NodeRef.For(obs.ObservationId, obs.CurrentRevision.Number).ToString(),
                    obs.CollectionInstanceId is { } cid ? NodeRef.For(cid).ToString() : null,
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

        var blindedGuids = blindedObservationIds.Select(id => id.Value).ToHashSet();

        var queries = snapshot.Assertions
            .Where(a => a.Kind is AssertionKind.Queried or AssertionKind.QueryAnswered)
            .Select(a =>
            {
                bool targetBlinded = a.Target.Kind == NodeKinds.Observation && blindedGuids.Contains(a.Target.Id);
                return new KernelCasebookQueryView(
                    NodeRef.For(a.AssertionId).ToString(),
                    a.Kind.ToString(),
                    a.Target.ToString(),
                    targetBlinded ? "masked" : a.Text,
                    a.AssertedBy,
                    a.AssertedAt,
                    targetBlinded ? "masked" : a.ReasonForChange,
                    Closed: a.RetractedBy is not null,
                    ClosedAt: a.RetractedAt);
            })
            .ToList();

        var verifications = snapshot.Assertions
            .Where(a => a.Kind == AssertionKind.Verified)
            .Select(a => new KernelCasebookVerificationView(
                NodeRef.For(a.AssertionId).ToString(),
                a.Target.ToString(),
                a.Revision,
                a.AssertedBy,
                a.AssertedAt,
                a.ReasonForChange,
                Retracted: a.RetractedBy is not null,
                RetractedAt: a.RetractedAt))
            .ToList();

        var locks = snapshot.Locks
            .Select(l => l.BlindedScope && !mayUnblind
                ? KernelLockResponse.FromMasked(l, MaskReason.Blinded)
                : KernelLockResponse.FromVisible(l))
            .ToList();

        var attestations = snapshot.Attestations
            .Where(a => !(a.TargetKind == NodeKinds.Observation && blindedGuids.Contains(a.TargetId)))
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
            attestations);

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
            AuditTrail: snapshot.AuditEntries);
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
            sb.AppendLine($"    <tr><td>{Html(q.Kind)}</td><td>{Html(q.TargetRef)}</td><td>{Html(q.Text ?? string.Empty)}</td><td>{Html(q.AssertedBy ?? "-")}</td><td>{Html(FormatInstant(q.AssertedAt))}</td><td>{(q.Closed ? "Closed" : "Open")}</td></tr>");
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

    public static async Task WriteCasebookPdfAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken)
    {
        var lines = BuildCasebookPdfLines(casebook);
        var pages = PaginatePdfLines(lines, maxLinesPerPage: 44);
        var pdfBytes = RenderPdf17Document(casebook, pages);
        await destination.WriteAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteStudyBundleZipAsync(
        Stream destination,
        ProjectedStudyExport export,
        StudyExportFormat format,
        string requestedBy,
        IReadOnlyList<KernelCasebookResponse> archiveCasebooks,
        CancellationToken cancellationToken)
    {
        var entryTimestamp = NormalizeZipTimestamp(export.GeneratedAt);
        var manifestFiles = new List<BundleManifestFileEntry>();
        var manifestCasebooks = new List<BundleManifestCasebookEntry>();

        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddZipEntryAsync(
                archive,
                "data/observations.csv",
                "text/csv",
                entryTimestamp,
                manifestFiles,
                (stream, ct) => WriteStudyCsvAsync(stream, export, ct),
                cancellationToken).ConfigureAwait(false);

            await AddZipEntryAsync(
                archive,
                "data/dataset.json",
                "application/json",
                entryTimestamp,
                manifestFiles,
                (stream, ct) => WriteStudyDatasetJsonAsync(stream, export, ct),
                cancellationToken).ConfigureAwait(false);

            await AddZipEntryAsync(
                archive,
                "metadata/study.odm.xml",
                "application/xml",
                entryTimestamp,
                manifestFiles,
                (stream, ct) => WriteStudyOdmXmlAsync(stream, export, ct),
                cancellationToken).ConfigureAwait(false);

            var collectionGroups = BuildCollectionTables(export);
            foreach (var table in collectionGroups)
            {
                var tablePath = $"data/collection_{table.TableName}.csv";
                var tableExport = export with { Rows = table.Rows };
                await AddZipEntryAsync(
                    archive,
                    tablePath,
                    "text/csv",
                    entryTimestamp,
                    manifestFiles,
                    (stream, ct) => WriteStudyCsvAsync(stream, tableExport, ct),
                    cancellationToken).ConfigureAwait(false);
            }

            if (format is StudyExportFormat.SasBundle or StudyExportFormat.StudyArchive)
            {
                var sasScript = BuildSasSyntaxScript(export, collectionGroups);
                await AddZipUtf8EntryAsync(
                    archive,
                    "syntax/load_study.sas",
                    "text/x-sas",
                    sasScript,
                    entryTimestamp,
                    manifestFiles,
                    cancellationToken).ConfigureAwait(false);
            }

            if (format is StudyExportFormat.RBundle or StudyExportFormat.StudyArchive)
            {
                var rScript = BuildRSyntaxScript(export, collectionGroups);
                await AddZipUtf8EntryAsync(
                    archive,
                    "syntax/load_study.R",
                    "text/x-r",
                    rScript,
                    entryTimestamp,
                    manifestFiles,
                    cancellationToken).ConfigureAwait(false);
            }

            if (format == StudyExportFormat.StudyArchive)
            {
                var maskedLocks = BuildMaskedStudyLocks(export);
                await AddZipEntryAsync(
                    archive,
                    "locks/locks.json",
                    "application/json",
                    entryTimestamp,
                    manifestFiles,
                    (stream, ct) => JsonSerializer.SerializeAsync(stream, maskedLocks, CasebookJsonOptions, ct),
                    cancellationToken).ConfigureAwait(false);

                var auditPayload = BuildMaskedAuditTrail(export);
                await AddZipEntryAsync(
                    archive,
                    "audit/audit_trail.json",
                    "application/json",
                    entryTimestamp,
                    manifestFiles,
                    (stream, ct) => JsonSerializer.SerializeAsync(stream, auditPayload, CasebookJsonOptions, ct),
                    cancellationToken).ConfigureAwait(false);

                var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var maskedCounter = 0;
                foreach (var casebook in archiveCasebooks)
                {
                    string slug;
                    if (casebook.Masked)
                    {
                        maskedCounter++;
                        slug = $"masked-{maskedCounter.ToString("D3", CultureInfo.InvariantCulture)}";
                    }
                    else
                    {
                        var baseSlug = $"{SanitizeBundleToken(casebook.SiteCode)}-{SanitizeBundleToken(casebook.SubjectCode)}";
                        slug = baseSlug;
                        var suffix = 2;
                        while (!usedSlugs.Add(slug))
                        {
                            slug = $"{baseSlug}-{suffix.ToString(CultureInfo.InvariantCulture)}";
                            suffix++;
                        }
                    }

                    var pdfPath = $"casebooks/{slug}.casebook.pdf";
                    var odmPath = $"casebooks/{slug}.casebook.odm.xml";

                    var pdfDigest = await AddZipEntryAsync(
                        archive,
                        pdfPath,
                        "application/pdf",
                        entryTimestamp,
                        manifestFiles,
                        (stream, ct) => WriteCasebookPdfAsync(stream, casebook, ct),
                        cancellationToken).ConfigureAwait(false);

                    var odmDigest = await AddZipEntryAsync(
                        archive,
                        odmPath,
                        "application/xml",
                        entryTimestamp,
                        manifestFiles,
                        (stream, ct) => WriteCasebookOdmXmlAsync(stream, casebook, ct),
                        cancellationToken).ConfigureAwait(false);

                    manifestCasebooks.Add(new BundleManifestCasebookEntry(
                        EnrollmentRef: casebook.EnrollmentRef,
                        SiteCode: casebook.SiteCode,
                        SubjectCode: casebook.SubjectCode,
                        Masked: casebook.Masked,
                        Mask: casebook.Mask,
                        CasebookDigestSha256: casebook.CasebookDigestSha256,
                        PdfPath: pdfPath,
                        PdfSha256: pdfDigest,
                        OdmXmlPath: odmPath,
                        OdmXmlSha256: odmDigest));
                }
            }

            var formatCode = format switch
            {
                StudyExportFormat.SasBundle => "sas_bundle",
                StudyExportFormat.RBundle => "r_bundle",
                _ => "study_archive",
            };

            var manifest = new StudyExportBundleManifest(
                Producer: ProducerName,
                Format: formatCode,
                StudyRef: NodeRef.For(export.Snapshot.StudyId).ToString(),
                StudyCode: export.Snapshot.StudyCode,
                StudyTitle: export.Snapshot.StudyTitle,
                LockFilter: export.LockFilter,
                ValidAt: FormatInstant(export.Snapshot.ValidAt),
                KnownAt: FormatInstant(export.Snapshot.KnownAt),
                GeneratedAt: FormatInstant(export.GeneratedAt),
                RequestedBy: requestedBy,
                Files: manifestFiles,
                Casebooks: manifestCasebooks);

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = entryTimestamp;
            await using var manifestStream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await JsonSerializer.SerializeAsync(manifestStream, manifest, CasebookJsonOptions, cancellationToken).ConfigureAwait(false);
            await manifestStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
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
        IReadOnlyList<KernelCasebookAttestationView> attestations)
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

    private static DateTimeOffset NormalizeZipTimestamp(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var minZip = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var maxZip = new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);
        if (utc < minZip)
        {
            return minZip;
        }

        return utc > maxZip ? maxZip : utc;
    }

    private static async Task<string> AddZipEntryAsync(
        ZipArchive archive,
        string entryPath,
        string contentType,
        DateTimeOffset entryTimestamp,
        List<BundleManifestFileEntry> manifestFiles,
        Func<Stream, CancellationToken, Task> writer,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await writer(buffer, cancellationToken).ConfigureAwait(false);
        var payload = buffer.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        entry.LastWriteTime = entryTimestamp;
        await using (var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await entryStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await entryStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        manifestFiles.Add(new BundleManifestFileEntry(
            Path: entryPath,
            ContentType: contentType,
            SizeBytes: payload.LongLength,
            Sha256: digest));
        return digest;
    }

    private static Task<string> AddZipUtf8EntryAsync(
        ZipArchive archive,
        string entryPath,
        string contentType,
        string content,
        DateTimeOffset entryTimestamp,
        List<BundleManifestFileEntry> manifestFiles,
        CancellationToken cancellationToken) =>
        AddZipEntryAsync(
            archive,
            entryPath,
            contentType,
            entryTimestamp,
            manifestFiles,
            async (stream, ct) =>
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            },
            cancellationToken);

    private static List<CollectionTableSlice> BuildCollectionTables(ProjectedStudyExport export)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slices = new List<CollectionTableSlice>();

        var grouped = export.Rows
            .GroupBy(
                r => r.Masked && r.SourceObservation is null ? "masked_records" : r.CollectionSpecId,
                StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in grouped)
        {
            var rawName = SanitizeIdentifierToken(group.Key);
            var candidate = rawName;
            var suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = $"{rawName}_{suffix.ToString(CultureInfo.InvariantCulture)}";
                suffix++;
            }

            var label = string.Equals(group.Key, "masked_records", StringComparison.Ordinal)
                ? "Masked Site Records"
                : export.Snapshot.Collections
                    .FirstOrDefault(c => string.Equals(c.CollectionSpecId, group.Key, StringComparison.Ordinal))
                    ?.CollectionSpecName
                    ?? group.Key;

            slices.Add(new CollectionTableSlice(candidate, group.Key, label, group.ToList()));
        }

        return slices;
    }

    private static string BuildSasSyntaxScript(
        ProjectedStudyExport export,
        IReadOnlyList<CollectionTableSlice> collectionTables)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("/* ========================================================================= */");
        sb.AppendLine($"/* Cadence Clinical Kernel - SAS Statistical Import Script                   */");
        sb.AppendLine($"/* Study: {EscapeSasComment(export.Snapshot.StudyCode)} ({EscapeSasComment(export.Snapshot.StudyTitle)}) */");
        sb.AppendLine($"/* Lock Filter: {EscapeSasComment(export.LockFilter)}                        */");
        sb.AppendLine($"/* Valid At: {FormatInstant(export.Snapshot.ValidAt)}                        */");
        sb.AppendLine($"/* Known At: {FormatInstant(export.Snapshot.KnownAt)}                        */");
        sb.AppendLine($"/* Generated At: {FormatInstant(export.GeneratedAt)}                         */");
        sb.AppendLine($"/* Producer: {ProducerName}                                                  */");
        sb.AppendLine("/* ========================================================================= */");
        sb.AppendLine();
        sb.AppendLine("%let cadence_bundle_dir = .;");
        sb.AppendLine();
        sb.Append(BuildSasDatasetImportBlock(
            datasetName: "observations",
            datasetLabel: $"Bitemporal Observations ({export.Snapshot.StudyCode})",
            relativeCsvPath: "data/observations.csv"));

        foreach (var slice in collectionTables)
        {
            sb.AppendLine();
            var sasMember = TruncateSasMemberName($"col_{slice.TableName}");
            sb.Append(BuildSasDatasetImportBlock(
                datasetName: sasMember,
                datasetLabel: $"{slice.Label} ({slice.SpecId})",
                relativeCsvPath: $"data/collection_{slice.TableName}.csv"));
        }

        var itemLabels = export.Rows
            .Where(r => !r.Masked)
            .GroupBy(r => r.ItemId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (ItemId: g.Key, ConceptLabel: g.First().ConceptLabel))
            .ToList();

        if (itemLabels.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("proc format;");
            sb.AppendLine("  value $cadence_item_lbl");
            foreach (var (itemId, conceptLabel) in itemLabels)
            {
                sb.AppendLine($"    '{EscapeSasSingleQuoted(itemId)}' = '{EscapeSasSingleQuoted(conceptLabel)}'");
            }

            sb.AppendLine("  ;");
            sb.AppendLine("run;");
        }

        return sb.ToString();
    }

    private static string BuildSasDatasetImportBlock(string datasetName, string datasetLabel, string relativeCsvPath)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine($"data work.{datasetName}(label='{EscapeSasSingleQuoted(datasetLabel)}');");
        sb.AppendLine($"  infile \"&cadence_bundle_dir/{relativeCsvPath}\" dsd dlm=',' firstobs=2 truncover lrecl=32767 encoding='utf-8';"); // deid-ignore
        sb.AppendLine("  length");
        sb.AppendLine("    study_code $64 site_code $64 subject_code $64 enrollment_ref $96 observation_ref $112");
        sb.AppendLine("    collection_instance_ref $96 collection_spec_id $96 planned_activity_id $96 item_id $96");
        sb.AppendLine("    concept_ref $128 concept_code $96 concept_label $256 observed_at $40 revision 8");
        sb.AppendLine("    value_kind $32 result_text $512 result_decimal 8 result_unit $64 lock_state $32");
        sb.AppendLine("    masked $8 mask_reason $64 recorded_by $128 recorded_at $40 valid_at $40 known_at $40;");
        sb.AppendLine("  input");
        sb.AppendLine("    study_code $ site_code $ subject_code $ enrollment_ref $ observation_ref $");
        sb.AppendLine("    collection_instance_ref $ collection_spec_id $ planned_activity_id $ item_id $");
        sb.AppendLine("    concept_ref $ concept_code $ concept_label $ observed_at $ revision");
        sb.AppendLine("    value_kind $ result_text $ result_decimal result_unit $ lock_state $");
        sb.AppendLine("    masked $ mask_reason $ recorded_by $ recorded_at $ valid_at $ known_at $;");
        sb.AppendLine("  label");
        sb.AppendLine("    study_code = 'Study Code'");
        sb.AppendLine("    site_code = 'Site Code'");
        sb.AppendLine("    subject_code = 'Pseudonymous Subject Code'");
        sb.AppendLine("    enrollment_ref = 'Enrollment NodeRef'");
        sb.AppendLine("    observation_ref = 'Observation Revision NodeRef'");
        sb.AppendLine("    collection_instance_ref = 'Collection Instance NodeRef'");
        sb.AppendLine("    collection_spec_id = 'Collection Specification Identifier'");
        sb.AppendLine("    planned_activity_id = 'Planned Activity Identifier'");
        sb.AppendLine("    item_id = 'Collection Item Identifier'");
        sb.AppendLine("    concept_ref = 'Concept Reference'");
        sb.AppendLine("    concept_code = 'Concept Code'");
        sb.AppendLine("    concept_label = 'Concept Label'");
        sb.AppendLine("    observed_at = 'Observed Timestamp (ISO-8601 UTC)'");
        sb.AppendLine("    revision = 'Observation Revision Number'");
        sb.AppendLine("    value_kind = 'Observation Value Kind'");
        sb.AppendLine("    result_text = 'Entered Result Text'");
        sb.AppendLine("    result_decimal = 'Numeric Quantity Result'");
        sb.AppendLine("    result_unit = 'UCUM Unit'");
        sb.AppendLine("    lock_state = 'Effective Scope Lock'");
        sb.AppendLine("    masked = 'K-AUTH-4 Masked Flag'");
        sb.AppendLine("    mask_reason = 'K-AUTH-4 Mask Reason'");
        sb.AppendLine("    recorded_by = 'Recorded By Actor'");
        sb.AppendLine("    recorded_at = 'Recorded Timestamp (ISO-8601 UTC)'");
        sb.AppendLine("    valid_at = 'Snapshot Valid-At Coordinate'");
        sb.AppendLine("    known_at = 'Snapshot Known-At Coordinate';");
        sb.AppendLine("run;");
        return sb.ToString();
    }

    private static string BuildRSyntaxScript(
        ProjectedStudyExport export,
        IReadOnlyList<CollectionTableSlice> collectionTables)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("# ===========================================================================");
        sb.AppendLine("# Cadence Clinical Kernel - R Statistical Import Script");
        sb.AppendLine($"# Study: {EscapeRComment(export.Snapshot.StudyCode)} ({EscapeRComment(export.Snapshot.StudyTitle)})");
        sb.AppendLine($"# Lock Filter: {EscapeRComment(export.LockFilter)}");
        sb.AppendLine($"# Valid At: {FormatInstant(export.Snapshot.ValidAt)}");
        sb.AppendLine($"# Known At: {FormatInstant(export.Snapshot.KnownAt)}");
        sb.AppendLine($"# Generated At: {FormatInstant(export.GeneratedAt)}");
        sb.AppendLine($"# Producer: {ProducerName}");
        sb.AppendLine("# ===========================================================================");
        sb.AppendLine();
        sb.AppendLine("cadence_col_classes <- c(");
        sb.AppendLine("  study_code = \"character\", site_code = \"character\", subject_code = \"character\",");
        sb.AppendLine("  enrollment_ref = \"character\", observation_ref = \"character\", collection_instance_ref = \"character\",");
        sb.AppendLine("  collection_spec_id = \"character\", planned_activity_id = \"character\", item_id = \"character\",");
        sb.AppendLine("  concept_ref = \"character\", concept_code = \"character\", concept_label = \"character\",");
        sb.AppendLine("  observed_at = \"character\", revision = \"integer\", value_kind = \"character\",");
        sb.AppendLine("  result_text = \"character\", result_decimal = \"numeric\", result_unit = \"character\",");
        sb.AppendLine("  lock_state = \"character\", masked = \"character\", mask_reason = \"character\",");
        sb.AppendLine("  recorded_by = \"character\", recorded_at = \"character\", valid_at = \"character\", known_at = \"character\"");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("cadence_col_labels <- c(");
        sb.AppendLine("  study_code = \"Study Code\", site_code = \"Site Code\", subject_code = \"Pseudonymous Subject Code\",");
        sb.AppendLine("  enrollment_ref = \"Enrollment NodeRef\", observation_ref = \"Observation Revision NodeRef\",");
        sb.AppendLine("  collection_instance_ref = \"Collection Instance NodeRef\", collection_spec_id = \"Collection Specification Identifier\",");
        sb.AppendLine("  planned_activity_id = \"Planned Activity Identifier\", item_id = \"Collection Item Identifier\",");
        sb.AppendLine("  concept_ref = \"Concept Reference\", concept_code = \"Concept Code\", concept_label = \"Concept Label\",");
        sb.AppendLine("  observed_at = \"Observed Timestamp (ISO-8601 UTC)\", revision = \"Observation Revision Number\",");
        sb.AppendLine("  value_kind = \"Observation Value Kind\", result_text = \"Entered Result Text\",");
        sb.AppendLine("  result_decimal = \"Numeric Quantity Result\", result_unit = \"UCUM Unit\",");
        sb.AppendLine("  lock_state = \"Effective Scope Lock\", masked = \"K-AUTH-4 Masked Flag\",");
        sb.AppendLine("  mask_reason = \"K-AUTH-4 Mask Reason\", recorded_by = \"Recorded By Actor\",");
        sb.AppendLine("  recorded_at = \"Recorded Timestamp (ISO-8601 UTC)\", valid_at = \"Snapshot Valid-At Coordinate\",");
        sb.AppendLine("  known_at = \"Snapshot Known-At Coordinate\"");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("read_cadence_csv <- function(csv_path, table_label) {");
        sb.AppendLine("  frame <- utils::read.csv(csv_path, stringsAsFactors = FALSE, na.strings = \"\", colClasses = cadence_col_classes, fileEncoding = \"UTF-8\")");
        sb.AppendLine("  frame$masked <- tolower(frame$masked) == \"true\"");
        sb.AppendLine("  for (col_name in intersect(names(cadence_col_labels), names(frame))) {");
        sb.AppendLine("    attr(frame[[col_name]], \"label\") <- unname(cadence_col_labels[[col_name]])");
        sb.AppendLine("  }");
        sb.AppendLine("  attr(frame, \"label\") <- table_label");
        sb.AppendLine("  frame");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("load_cadence_study <- function(bundle_dir = \".\") {");
        sb.AppendLine($"  observations <- read_cadence_csv(file.path(bundle_dir, \"data\", \"observations.csv\"), \"{EscapeRDoubleQuoted($"Bitemporal Observations ({export.Snapshot.StudyCode})")}\")");
        sb.AppendLine("  collections <- list()");
        foreach (var slice in collectionTables)
        {
            sb.AppendLine($"  collections[[\"{EscapeRDoubleQuoted(slice.TableName)}\"]] <- read_cadence_csv(file.path(bundle_dir, \"data\", \"collection_{EscapeRDoubleQuoted(slice.TableName)}.csv\"), \"{EscapeRDoubleQuoted($"{slice.Label} ({slice.SpecId})")}\")");
        }

        sb.AppendLine("  list(");
        sb.AppendLine($"    study_code = \"{EscapeRDoubleQuoted(export.Snapshot.StudyCode)}\",");
        sb.AppendLine($"    lock_filter = \"{EscapeRDoubleQuoted(export.LockFilter)}\",");
        sb.AppendLine($"    valid_at = \"{FormatInstant(export.Snapshot.ValidAt)}\",");
        sb.AppendLine($"    known_at = \"{FormatInstant(export.Snapshot.KnownAt)}\",");
        sb.AppendLine("    observations = observations,");
        sb.AppendLine("    collections = collections");
        sb.AppendLine("  )");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static IReadOnlyList<KernelLockResponse> BuildMaskedStudyLocks(ProjectedStudyExport export)
    {
        var enrollmentSiteMap = export.Snapshot.Enrollments.ToDictionary(e => e.EnrollmentId.Value, e => e.StudySiteId);
        var collectionEnrollmentMap = export.Snapshot.Collections.ToDictionary(c => c.CollectionInstanceId.Value, c => (c.EnrollmentId, c.StudySiteId));
        var obsEnrollmentMap = export.Snapshot.Observations.ToDictionary(o => o.ObservationId.Value, o => (o.EnrollmentId, o.StudySiteId, o.IsBlindedTreatment));

        var responses = new List<KernelLockResponse>(export.Snapshot.Locks.Count);
        foreach (var entry in export.Snapshot.Locks)
        {
            MaskReason? reason = export.Visibility.Masks.For(entry.Scope);
            if (reason is null)
            {
                if (entry.Scope.Kind == NodeKinds.Enrollment && enrollmentSiteMap.TryGetValue(entry.Scope.Id, out var siteId))
                {
                    reason = export.Visibility.Masks.For(NodeRef.For(siteId));
                }
                else if (entry.Scope.Kind == NodeKinds.CollectionInstance
                    && collectionEnrollmentMap.TryGetValue(entry.Scope.Id, out var colScope))
                {
                    reason = export.Visibility.Masks.For(NodeRef.For(colScope.EnrollmentId))
                        ?? export.Visibility.Masks.For(NodeRef.For(colScope.StudySiteId));
                }
                else if (entry.Scope.Kind == NodeKinds.Observation
                    && obsEnrollmentMap.TryGetValue(entry.Scope.Id, out var obsScope))
                {
                    reason = export.Visibility.Masks.For(NodeRef.For(obsScope.EnrollmentId))
                        ?? export.Visibility.Masks.For(NodeRef.For(obsScope.StudySiteId));
                    if (reason is null && (entry.BlindedScope || obsScope.IsBlindedTreatment)
                        && !export.Visibility.UnblindedEnrollments.Contains(obsScope.EnrollmentId))
                    {
                        reason = MaskReason.Blinded;
                    }
                }
            }

            if (reason is null && entry.BlindedScope && export.Visibility.UnblindedEnrollments.Count == 0)
            {
                reason = MaskReason.Blinded;
            }

            responses.Add(reason is { } mask
                ? KernelLockResponse.FromMasked(entry, mask)
                : KernelLockResponse.FromVisible(entry));
        }

        return responses;
    }

    private static IReadOnlyList<BundleAuditEntryView> BuildMaskedAuditTrail(ProjectedStudyExport export)
    {
        MaskReason? restrictedReason = null;
        foreach (var site in export.Snapshot.StudySites)
        {
            restrictedReason ??= export.Visibility.Masks.For(NodeRef.For(site.StudySiteId));
        }

        foreach (var enrollment in export.Snapshot.Enrollments)
        {
            restrictedReason ??= export.Visibility.Masks.For(NodeRef.For(enrollment.EnrollmentId));
        }

        if (restrictedReason is null
            && export.Snapshot.Observations.Any(o => o.IsBlindedTreatment && !export.Visibility.UnblindedEnrollments.Contains(o.EnrollmentId)))
        {
            restrictedReason = MaskReason.Blinded;
        }

        var entries = new List<BundleAuditEntryView>(export.Snapshot.AuditEntries.Count);
        foreach (var audit in export.Snapshot.AuditEntries)
        {
            var isStudyLevel = string.Equals(audit.Subject, "Study", StringComparison.OrdinalIgnoreCase);
            if (restrictedReason is { } maskReason && !isStudyLevel)
            {
                entries.Add(new BundleAuditEntryView(
                    Sequence: audit.Sequence,
                    EventKind: audit.Action,
                    ObservationRef: audit.Subject,
                    Revision: audit.Version is { } maskedVer ? (int)maskedVer : null,
                    RecordedAt: audit.RecordedAt,
                    ActorId: null,
                    Summary: "masked",
                    Masked: true,
                    MaskReason: maskReason.ToString()));
                continue;
            }

            entries.Add(new BundleAuditEntryView(
                Sequence: audit.Sequence,
                EventKind: audit.Action,
                ObservationRef: audit.Subject,
                Revision: audit.Version is { } ver ? (int)ver : null,
                RecordedAt: audit.RecordedAt,
                ActorId: audit.ActorId,
                Summary: audit.ReasonForChange,
                Masked: false,
                MaskReason: null));
        }

        return entries;
    }

    private static List<PdfStyledLine> BuildCasebookPdfLines(KernelCasebookResponse casebook)
    {
        var lines = new List<PdfStyledLine>(128)
        {
            new(PdfFontKind.Heading, 15, $"Participant Casebook: {casebook.SubjectCode}"),
            new(PdfFontKind.Body, 10, $"Study: {casebook.StudyCode} ({casebook.StudyTitle}) | Site: {casebook.SiteCode} | Status: {casebook.EnrollmentStatus ?? "n/a"}"),
            new(PdfFontKind.Body, 9, $"Enrollment Ref: {casebook.EnrollmentRef} | Site Ref: {casebook.SiteRef} | Requested By: {casebook.RequestedBy}"),
            new(PdfFontKind.Body, 9, $"Valid At: {FormatInstant(casebook.ValidAt)} | Known At: {FormatInstant(casebook.KnownAt)} | Generated At: {FormatInstant(casebook.GeneratedAt)}"),
            new(PdfFontKind.Mono, 8, $"Producer: {casebook.Producer} | Canonical SHA-256: {casebook.CasebookDigestSha256}"),
        };

        if (casebook.Masked)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 10, $"K-AUTH-4 Visibility Notice: masked ({casebook.Mask ?? "NotPermitted"})"));
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "1. Forms & Collection Instances"));
        if (casebook.Forms.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No collection instances recorded at this bitemporal coordinate."));
        }
        else
        {
            foreach (var form in casebook.Forms)
            {
                var formDetail = form.Masked
                    ? $"[masked ({form.Mask ?? "NotPermitted"})]"
                    : $"{form.Title} ({form.CollectionSpecId}) | Visit: {form.PlannedActivityName ?? form.PlannedActivityId ?? "Unscheduled"} | Ref: {form.Ref}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, $"- {formDetail}", maxChars: 96);
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "2. Clinical Observations & Revision History"));
        if (casebook.Observations.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No clinical observations recorded at this bitemporal coordinate."));
        }
        else
        {
            foreach (var obs in casebook.Observations)
            {
                var valWithUnit = obs.Unit is not null && !obs.Masked ? $"{obs.DisplayValue} {obs.Unit}" : obs.DisplayValue;
                var maskSuffix = obs.Masked ? $" | Mask: masked ({obs.Mask ?? "Blinded"})" : string.Empty;
                var obsLine = $"- [{obs.ItemId ?? obs.ConceptCode}] {obs.ConceptLabel}: {valWithUnit} (r{obs.CurrentRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"}, Lock: {obs.EffectiveLock ?? "Unlocked"}{maskSuffix})";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, obsLine, maxChars: 96);

                foreach (var rev in obs.Revisions)
                {
                    var revVal = rev.Unit is not null && !obs.Masked ? $"{rev.DisplayValue} {rev.Unit}" : rev.DisplayValue;
                    var stateTag = rev.Superseded ? "superseded" : "current";
                    var revLine = $"    r{rev.Revision.ToString(CultureInfo.InvariantCulture)} [{stateTag}] {revVal} | by {rev.RecordedBy ?? "masked"} at {FormatInstant(rev.RecordedAt)} | reason: {rev.ReasonForChange ?? "n/a"}";
                    AppendWrappedLines(lines, PdfFontKind.Mono, 8, revLine, maxChars: 102);
                }
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "3. Queries & Source Data Verifications"));
        if (casebook.Queries.Count == 0 && casebook.Verifications.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No queries or source data verifications recorded."));
        }
        else
        {
            foreach (var q in casebook.Queries)
            {
                var qLine = $"- [{q.Kind}] Target: {q.TargetRef} | State: {(q.Closed ? "Closed" : "Open")} | Actor: {q.AssertedBy ?? "-"} | At: {FormatInstant(q.AssertedAt)} | Detail: {q.Text ?? string.Empty}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, qLine, maxChars: 96);
            }

            foreach (var v in casebook.Verifications)
            {
                var vLine = $"- [Verified] Target: {v.ObservationRef} (r{v.Revision?.ToString(CultureInfo.InvariantCulture) ?? "1"}) | State: {(v.Retracted ? "Retracted" : "Active")} | Actor: {v.VerifiedBy ?? "-"} | At: {FormatInstant(v.VerifiedAt)}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, vLine, maxChars: 96);
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "4. Scope Locks"));
        if (casebook.Locks.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No scope locks covering this enrollment."));
        }
        else
        {
            foreach (var lk in casebook.Locks)
            {
                var lockState = lk.Released ? "Released" : "InEffect";
                var lkLine = lk.Masked
                    ? $"- [masked ({lk.Mask ?? "NotPermitted"})]"
                    : $"- [{lk.Kind ?? "Lock"}] Scope: {lk.ScopeRef} | State: {lockState} | By: {lk.LockedBy ?? "-"} | Reason: {lk.ReasonForChange ?? "-"}";
                AppendWrappedLines(lines, PdfFontKind.Body, 9, lkLine, maxChars: 96);
            }
        }

        lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, string.Empty));
        lines.Add(new PdfStyledLine(PdfFontKind.Subheading, 11, "5. Electronic Signatures (21 CFR Part 11 Attestations)"));
        if (casebook.Attestations.Count == 0)
        {
            lines.Add(new PdfStyledLine(PdfFontKind.Body, 9, "No electronic signatures recorded on visible records."));
        }
        else
        {
            foreach (var att in casebook.Attestations)
            {
                var attLine = $"- {att.Ref} | Target: {att.TargetRef} | Meaning: {att.Meaning} | Signer: {att.SignerId} | Method: {att.Method} | Signed: {FormatInstant(att.SignedAt)} | Hash: {att.TargetContentHash}";
                AppendWrappedLines(lines, PdfFontKind.Mono, 8, attLine, maxChars: 102);
            }
        }

        return lines;
    }

    private static void AppendWrappedLines(
        List<PdfStyledLine> target,
        PdfFontKind font,
        int fontSize,
        string text,
        int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            target.Add(new PdfStyledLine(font, fontSize, text));
            return;
        }

        var remaining = text.AsSpan();
        var continuationIndent = "    ";
        var firstChunk = true;
        while (remaining.Length > 0)
        {
            var limit = firstChunk ? maxChars : Math.Max(20, maxChars - continuationIndent.Length);
            if (remaining.Length <= limit)
            {
                var finalLine = firstChunk ? remaining.ToString() : continuationIndent + remaining.ToString();
                target.Add(new PdfStyledLine(font, fontSize, finalLine));
                break;
            }

            var slice = remaining[..limit];
            var splitAt = slice.LastIndexOf(' ');
            if (splitAt <= limit / 3)
            {
                splitAt = limit;
            }

            var segment = remaining[..splitAt].TrimEnd().ToString();
            target.Add(new PdfStyledLine(font, fontSize, firstChunk ? segment : continuationIndent + segment));
            remaining = remaining[splitAt..].TrimStart();
            firstChunk = false;
        }
    }

    private static List<IReadOnlyList<PdfStyledLine>> PaginatePdfLines(
        IReadOnlyList<PdfStyledLine> lines,
        int maxLinesPerPage)
    {
        var pages = new List<IReadOnlyList<PdfStyledLine>>();
        for (var idx = 0; idx < lines.Count; idx += maxLinesPerPage)
        {
            var count = Math.Min(maxLinesPerPage, lines.Count - idx);
            var chunk = new List<PdfStyledLine>(count);
            for (var j = 0; j < count; j++)
            {
                chunk.Add(lines[idx + j]);
            }

            pages.Add(chunk);
        }

        if (pages.Count == 0)
        {
            pages.Add([new PdfStyledLine(PdfFontKind.Body, 10, "Empty casebook.")]);
        }

        return pages;
    }

    private static byte[] RenderPdf17Document(
        KernelCasebookResponse casebook,
        IReadOnlyList<IReadOnlyList<PdfStyledLine>> pages)
    {
        // Object layout:
        // 1: Catalog
        // 2: Pages
        // 3: Font F1 (Helvetica)
        // 4: Font F2 (Helvetica-Bold)
        // 5: Font F3 (Courier)
        // 6: Info dictionary
        // 7 + 2*p: Page object
        // 8 + 2*p: Content stream object
        var totalObjects = 6 + (pages.Count * 2);
        var offsets = new long[totalObjects + 1];

        using var output = new MemoryStream(8192);
        WriteAscii(output, "%PDF-1.7\n");
        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        offsets[1] = output.Position;
        WriteAscii(output, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kidsRefs = string.Join(' ', Enumerable.Range(0, pages.Count)
            .Select(p => $"{(7 + (p * 2)).ToString(CultureInfo.InvariantCulture)} 0 R"));
        offsets[2] = output.Position;
        WriteAscii(output, $"2 0 obj\n<< /Type /Pages /Kids [{kidsRefs}] /Count {pages.Count.ToString(CultureInfo.InvariantCulture)} >>\nendobj\n");

        offsets[3] = output.Position;
        WriteAscii(output, "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        offsets[4] = output.Position;
        WriteAscii(output, "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        offsets[5] = output.Position;
        WriteAscii(output, "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n");

        var pdfCreationDate = $"D:{casebook.GeneratedAt.ToUniversalTime():yyyyMMddHHmmss}Z";
        offsets[6] = output.Position;
        WriteAscii(
            output,
            $"6 0 obj\n<< /Title ({EscapePdfLiteral($"Participant Casebook - {casebook.SubjectCode}")}) /Producer ({EscapePdfLiteral(casebook.Producer)}) /Creator (Cadence Clinical Kernel) /Subject ({EscapePdfLiteral($"Study {casebook.StudyCode} | SHA-256 {casebook.CasebookDigestSha256}")}) /CreationDate ({EscapePdfLiteral(pdfCreationDate)}) >>\nendobj\n");

        for (var p = 0; p < pages.Count; p++)
        {
            var pageObjId = 7 + (p * 2);
            var streamObjId = 8 + (p * 2);
            var contentBytes = BuildPdfPageContentBytes(casebook, pages[p], p + 1, pages.Count);

            offsets[pageObjId] = output.Position;
            WriteAscii(
                output,
                $"{pageObjId.ToString(CultureInfo.InvariantCulture)} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {streamObjId.ToString(CultureInfo.InvariantCulture)} 0 R >>\nendobj\n");

            offsets[streamObjId] = output.Position;
            WriteAscii(output, $"{streamObjId.ToString(CultureInfo.InvariantCulture)} 0 obj\n<< /Length {contentBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n");
            output.Write(contentBytes);
            WriteAscii(output, "\nendstream\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {(totalObjects + 1).ToString(CultureInfo.InvariantCulture)}\n");
        WriteAscii(output, "0000000000 65535 f \n");
        for (var objId = 1; objId <= totalObjects; objId++)
        {
            WriteAscii(output, $"{offsets[objId].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        WriteAscii(
            output,
            $"trailer\n<< /Size {(totalObjects + 1).ToString(CultureInfo.InvariantCulture)} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

        return output.ToArray();
    }

    private static byte[] BuildPdfPageContentBytes(
        KernelCasebookResponse casebook,
        IReadOnlyList<PdfStyledLine> lines,
        int pageNumber,
        int totalPages)
    {
        var sb = new StringBuilder(2048);
        sb.Append("0.82 0.86 0.91 RG 0.75 w 48 730 m 564 730 l S\n");

        var cursorY = 744;
        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line.Text))
            {
                var fontAlias = line.Font switch
                {
                    PdfFontKind.Heading or PdfFontKind.Subheading => "/F2",
                    PdfFontKind.Mono => "/F3",
                    _ => "/F1",
                };

                sb.Append("BT ");
                sb.Append(fontAlias);
                sb.Append(' ');
                sb.Append(line.FontSize.ToString(CultureInfo.InvariantCulture));
                sb.Append(" Tf 48 ");
                sb.Append(cursorY.ToString(CultureInfo.InvariantCulture));
                sb.Append(" Td (");
                sb.Append(EscapePdfLiteral(line.Text));
                sb.Append(") Tj ET\n");
            }

            cursorY -= line.Font switch
            {
                PdfFontKind.Heading => 20,
                PdfFontKind.Subheading => 16,
                _ => 14,
            };
        }

        sb.Append("0.82 0.86 0.91 RG 0.75 w 48 44 m 564 44 l S\n");
        var footer = $"Page {pageNumber.ToString(CultureInfo.InvariantCulture)} of {totalPages.ToString(CultureInfo.InvariantCulture)} | Study: {casebook.StudyCode} | Subject: {casebook.SubjectCode} | SHA-256: {casebook.CasebookDigestSha256}";
        sb.Append("BT /F3 7 Tf 48 32 Td (");
        sb.Append(EscapePdfLiteral(footer));
        sb.Append(") Tj ET");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void WriteAscii(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes);
    }

    private static string EscapePdfLiteral(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length + 8);
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '(':
                    sb.Append(@"\(");
                    break;
                case ')':
                    sb.Append(@"\)");
                    break;
                case '\r':
                case '\n':
                case '\t':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(ch is >= ' ' and <= '~' ? ch : '?');
                    break;
            }
        }

        return sb.ToString();
    }

    private static string SanitizeIdentifierToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "records";
        }

        var chars = raw
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var cleaned = string.Join('_', new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(cleaned))
        {
            return "records";
        }

        return char.IsDigit(cleaned[0]) ? $"c_{cleaned}" : cleaned;
    }

    private static string SanitizeBundleToken(string raw)
    {
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        return chars.Length == 0 ? "item" : new string(chars);
    }

    private static string TruncateSasMemberName(string raw) =>
        raw.Length <= 32 ? raw : raw[..32].TrimEnd('_');

    private static string EscapeSasSingleQuoted(string raw) =>
        raw.Replace("'", "''", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    private static string EscapeSasComment(string raw) =>
        raw.Replace("*/", "* /", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    private static string EscapeRDoubleQuoted(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    private static string EscapeRComment(string raw) =>
        raw.Replace('\r', ' ').Replace('\n', ' ');

    private enum PdfFontKind
    {
        Heading,
        Subheading,
        Body,
        Mono,
    }

    private sealed record PdfStyledLine(PdfFontKind Font, int FontSize, string Text);

    private sealed record CollectionTableSlice(
        string TableName,
        string SpecId,
        string Label,
        IReadOnlyList<ProjectedObservationRow> Rows);

    private sealed record StudyExportBundleManifest(
        string Producer,
        string Format,
        string StudyRef,
        string StudyCode,
        string StudyTitle,
        string LockFilter,
        string ValidAt,
        string KnownAt,
        string GeneratedAt,
        string RequestedBy,
        IReadOnlyList<BundleManifestFileEntry> Files,
        IReadOnlyList<BundleManifestCasebookEntry> Casebooks);

    private sealed record BundleManifestFileEntry(
        string Path,
        string ContentType,
        long SizeBytes,
        string Sha256);

    private sealed record BundleManifestCasebookEntry(
        string EnrollmentRef,
        string SiteCode,
        string SubjectCode,
        bool Masked,
        string? Mask,
        string CasebookDigestSha256,
        string PdfPath,
        string PdfSha256,
        string OdmXmlPath,
        string OdmXmlSha256);

    private sealed record BundleAuditEntryView(
        long Sequence,
        string EventKind,
        string? ObservationRef,
        int? Revision,
        DateTimeOffset RecordedAt,
        string? ActorId,
        string Summary,
        bool Masked,
        string? MaskReason);
}
