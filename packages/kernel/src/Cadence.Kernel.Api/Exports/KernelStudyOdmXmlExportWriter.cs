using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelStudyOdmXmlExportWriter
{
    public static async Task WriteStudyOdmXmlAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
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
        await xml.WriteAttributeStringAsync("cadence", "HiddenMoreExist", CadenceNamespace, export.Hidden.MoreExist ? "true" : "false").ConfigureAwait(false);
        if (export.Hidden.MoreExist)
        {
            await xml.WriteAttributeStringAsync("cadence", "HiddenReasons", CadenceNamespace, string.Join(',', export.Hidden.Reasons)).ConfigureAwait(false);
        }

        await WriteOdmStudyMetadataAsync(
            xml,
            snapshot.StudyCode,
            snapshot.StudyTitle,
            snapshot.ProtocolVersions,
            export.Rows).ConfigureAwait(false);

        var (visibleAttestations, blindedSignatureAttestationIds) = FilterStudyOdmAttestations(export);

        await WriteOdmAdminDataAsync(xml, snapshot.StudyCode, visibleAttestations, blindedSignatureAttestationIds).ConfigureAwait(false);

        if (export.HiddenScopes.MoreExist)
        {
            await xml.WriteStartElementAsync("cadence", "HiddenScopes", CadenceNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "MoreExist", null, "true").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Reasons", null, string.Join(',', export.HiddenScopes.Reasons)).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteStartElementAsync(null, "ClinicalData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "StudyOID", null, snapshot.StudyCode).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "MetaDataVersionOID", null, mdvOid).ConfigureAwait(false);

        var visibleRowsByEnrollment = export.Rows
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

                var byForm = eventGroup.GroupBy(
                    r => r.GroupingCollectionInstanceId is { } gcid
                        ? $"CI.{gcid.Value:D}"
                        : (r.CollectionInstanceRef ?? $"FORM.{r.CollectionSpecId}"),
                    StringComparer.Ordinal);
                foreach (var formGroup in byForm)
                {
                    var sample = formGroup.First();
                    var formOid = $"FORM.{SanitizeOid(sample.CollectionSpecId)}";
                    await xml.WriteStartElementAsync(null, "FormData", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "FormOID", null, formOid).ConfigureAwait(false);
                    var visibleRepeatKey = formGroup.FirstOrDefault(r => r.CollectionInstanceRef is not null)?.CollectionInstanceRef;
                    if (visibleRepeatKey is not null)
                    {
                        await xml.WriteAttributeStringAsync(null, "FormRepeatKey", null, visibleRepeatKey).ConfigureAwait(false);
                    }

                    await xml.WriteStartElementAsync(null, "ItemGroupData", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "ItemGroupOID", null, $"IG.{SanitizeOid(sample.CollectionSpecId)}").ConfigureAwait(false);

                    foreach (var row in formGroup)
                    {
                        await WriteOdmItemDataAsync(xml, row).ConfigureAwait(false);
                    }

                    await xml.WriteEndElementAsync().ConfigureAwait(false);

                    if (sample.GroupingCollectionInstanceId is { } formInstanceId
                        && attestationsByTarget.TryGetValue(formInstanceId.Value, out var formSigs))
                    {
                        foreach (var sig in formSigs)
                        {
                            await WriteOdmSignatureElementAsync(
                                xml,
                                sig,
                                first.SiteCode,
                                blindedSignatureAttestationIds.Contains(sig.AttestationId)).ConfigureAwait(false);
                        }
                    }

                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndDocumentAsync().ConfigureAwait(false);
        await xml.FlushAsync().ConfigureAwait(false);
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

    private static (List<ExportAttestationSnapshot> Visible, HashSet<AttestationId> BlindedSignatures) FilterStudyOdmAttestations(
        ProjectedStudyExport export)
    {
        var snapshot = export.Snapshot;
        var visibleEnrollmentIds = snapshot.Enrollments
            .Where(e => export.Visibility.Masks.For(NodeRef.For(e.EnrollmentId)) is null
                && export.Visibility.Masks.For(NodeRef.For(e.StudySiteId)) is null)
            .Select(e => e.EnrollmentId)
            .ToHashSet();

        var blindedObsGuids = new HashSet<Guid>();
        var blindedCollectionGuids = new HashSet<Guid>();
        var blindedEnrollmentGuids = new HashSet<Guid>();
        var visibleTargetIds = new HashSet<Guid> { snapshot.StudyId.Value };
        foreach (var protocol in snapshot.ProtocolVersions)
        {
            visibleTargetIds.Add(protocol.ProtocolVersionId.Value);
        }

        foreach (var enrollmentId in visibleEnrollmentIds)
        {
            visibleTargetIds.Add(enrollmentId.Value);
        }

        foreach (var coll in snapshot.Collections.Where(c => visibleEnrollmentIds.Contains(c.EnrollmentId)))
        {
            visibleTargetIds.Add(coll.CollectionInstanceId.Value);
        }

        foreach (var obs in snapshot.Observations.Where(o => visibleEnrollmentIds.Contains(o.EnrollmentId)))
        {
            if (obs.IsBlindedTreatment && !export.Visibility.UnblindedEnrollments.Contains(obs.EnrollmentId))
            {
                blindedObsGuids.Add(obs.ObservationId.Value);
                blindedEnrollmentGuids.Add(obs.EnrollmentId.Value);
                if (obs.CollectionInstanceId is { } cid)
                {
                    blindedCollectionGuids.Add(cid.Value);
                }
            }
            else
            {
                visibleTargetIds.Add(obs.ObservationId.Value);
            }
        }

        var blindedLockAttestationIds = new HashSet<AttestationId>();
        var nonBlindedLockTargetsCoveringBlinded = new HashSet<Guid>();
        foreach (var l in snapshot.Locks)
        {
            bool lockAccessMasked = (l.EnrollmentId is { } eid && !visibleEnrollmentIds.Contains(eid))
                || (l.StudySiteId is { } sid && export.Visibility.Masks.For(NodeRef.For(sid)) is not null);
            if (lockAccessMasked)
            {
                continue;
            }

            bool lockBlinded = l.BlindedScope
                && (l.EnrollmentId is null || !export.Visibility.UnblindedEnrollments.Contains(l.EnrollmentId.Value));
            if (lockBlinded)
            {
                blindedLockAttestationIds.Add(l.AttestationId);
                if (l.ReleaseAttestationId is { } relAtt)
                {
                    blindedLockAttestationIds.Add(relAtt);
                }

                continue;
            }

            visibleTargetIds.Add(l.LockId.Value);
            if (l.ReleaseAssertionId is { } relId)
            {
                visibleTargetIds.Add(relId.Value);
            }

            if (blindedObsGuids.Count > 0
                && (l.Scope.Kind == NodeKinds.Study
                    || (l.EnrollmentId is { } covEid && blindedEnrollmentGuids.Contains(covEid.Value))
                    || (l.Scope.Kind == NodeKinds.CollectionInstance && blindedCollectionGuids.Contains(l.Scope.Id))))
            {
                nonBlindedLockTargetsCoveringBlinded.Add(l.LockId.Value);
                if (l.ReleaseAssertionId is { } covRelId)
                {
                    nonBlindedLockTargetsCoveringBlinded.Add(covRelId.Value);
                }
            }
        }

        var visibleAttestations = snapshot.Attestations
            .Where(a => visibleTargetIds.Contains(a.TargetId)
                && !blindedObsGuids.Contains(a.TargetId)
                && !blindedLockAttestationIds.Contains(a.AttestationId))
            .ToList();

        var blindedSignatureIds = visibleAttestations
            .Where(a => blindedCollectionGuids.Contains(a.TargetId)
                || blindedEnrollmentGuids.Contains(a.TargetId)
                || nonBlindedLockTargetsCoveringBlinded.Contains(a.TargetId)
                || (blindedObsGuids.Count > 0 && a.TargetId == snapshot.StudyId.Value))
            .Select(a => a.AttestationId)
            .ToHashSet();

        return (visibleAttestations, blindedSignatureIds);
    }

    private static async Task WriteOdmAdminDataAsync(
        XmlWriter xml,
        string studyCode,
        IReadOnlyList<ExportAttestationSnapshot> attestations,
        IReadOnlySet<AttestationId> blindedSignatureAttestationIds)
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
            if (!blindedSignatureAttestationIds.Contains(att.AttestationId))
            {
                await xml.WriteElementStringAsync(null, "LegalReason", OdmNamespace, att.Reason).ConfigureAwait(false);
            }

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

    private static async Task WriteOdmSignatureElementAsync(
        XmlWriter xml,
        ExportAttestationSnapshot att,
        string siteCode,
        bool blindedSignature)
    {
        await xml.WriteStartElementAsync(null, "Signature", OdmNamespace).ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "UserRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "UserOID", null, blindedSignature ? "masked" : att.ActorId).ConfigureAwait(false);
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
}
