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

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelCasebookOdmXmlExportWriter
{
    public static async Task WriteCasebookOdmXmlAsync(
        Stream destination,
        KernelCasebookResponse casebook,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var protocols = casebook.ProtocolVersions ?? [];
        var latestProtocol = protocols.Count > 0 ? protocols[^1] : null;
        var versionNumber = latestProtocol?.Number ?? 1;
        var mdvOid = $"MDV.{versionNumber.ToString(CultureInfo.InvariantCulture)}";

        var clinicalForms = BuildCasebookOdmFormBuckets(casebook);
        var signatureDefs = BuildCasebookSignatureDefinitions(casebook.Attestations);
        var signaturesByFormRef = new Dictionary<string, List<(KernelCasebookAttestationView Attestation, string SignatureOid)>>(StringComparer.Ordinal);
        var subjectLevelSignatures = new List<(KernelCasebookAttestationView Attestation, string SignatureOid)>();
        var matchedFormRefs = clinicalForms
            .Where(b => b.FormRepeatKey is not null)
            .Select(b => b.FormRepeatKey!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var sigEntry in signatureDefs)
        {
            var targetRef = sigEntry.Attestation.TargetRef;
            if (!string.Equals(targetRef, "masked", StringComparison.Ordinal) && matchedFormRefs.Contains(targetRef))
            {
                if (!signaturesByFormRef.TryGetValue(targetRef, out var list))
                {
                    list = [];
                    signaturesByFormRef[targetRef] = list;
                }

                list.Add(sigEntry);
            }
            else
            {
                subjectLevelSignatures.Add(sigEntry);
            }
        }

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
        await xml.WriteAttributeStringAsync(null, "SourceSystem", null, "Cadence.Kernel.Api").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "CasebookDigestSha256", CadenceNamespace, casebook.CasebookDigestSha256).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "ValidAt", CadenceNamespace, FormatInstant(casebook.ValidAt)).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync("cadence", "KnownAt", CadenceNamespace, FormatInstant(casebook.KnownAt)).ConfigureAwait(false);

        var compiledOdmFallback = latestProtocol is null
            ? null
            : latestProtocol.CompiledOdmXml
                ?? TryCompileUsdmOdmViaReflection(latestProtocol.RawContent, casebook.StudyCode, versionNumber.ToString(CultureInfo.InvariantCulture));
        await KernelCasebookOdmMetadataWriter.WriteCasebookOdmStudyMetadataAsync(
            xml,
            casebook,
            versionNumber,
            latestProtocol?.Design,
            compiledOdmFallback,
            clinicalForms).ConfigureAwait(false);

        if (signatureDefs.Count > 0)
        {
            await xml.WriteStartElementAsync(null, "AdminData", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "StudyOID", null, casebook.StudyCode).ConfigureAwait(false);
            foreach (var (att, sigOid) in signatureDefs)
            {
                await xml.WriteStartElementAsync(null, "SignatureDef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "OID", null, sigOid).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Methodology", null, "Electronic").ConfigureAwait(false);
                await xml.WriteElementStringAsync(null, "Meaning", OdmNamespace, att.Meaning).ConfigureAwait(false);
                if (!string.Equals(att.Reason, "masked", StringComparison.Ordinal)
                    && !string.Equals(att.Ref, "masked", StringComparison.Ordinal))
                {
                    await xml.WriteElementStringAsync(null, "LegalReason", OdmNamespace, att.Reason).ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteStartElementAsync(null, "ClinicalData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "StudyOID", null, casebook.StudyCode).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "MetaDataVersionOID", null, mdvOid).ConfigureAwait(false);

        await xml.WriteStartElementAsync(null, "SubjectData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "SubjectKey", null, casebook.SubjectCode).ConfigureAwait(false);
        if (!casebook.Masked && !string.Equals(casebook.EnrollmentRef, "masked", StringComparison.Ordinal))
        {
            await xml.WriteAttributeStringAsync("cadence", "EnrollmentRef", CadenceNamespace, casebook.EnrollmentRef).ConfigureAwait(false);
        }

        if (casebook.Masked)
        {
            await xml.WriteAttributeStringAsync("cadence", "Masked", CadenceNamespace, "true").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "MaskReason", CadenceNamespace, casebook.Mask ?? "NotPermitted").ConfigureAwait(false);
        }

        await xml.WriteStartElementAsync(null, "SiteRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "LocationOID", null, casebook.SiteCode).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);

        foreach (var eventGroup in clinicalForms.GroupBy(b => b.EventOid, StringComparer.Ordinal))
        {
            await xml.WriteStartElementAsync(null, "StudyEventData", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "StudyEventOID", null, eventGroup.Key).ConfigureAwait(false);

            foreach (var bucket in eventGroup)
            {
                await xml.WriteStartElementAsync(null, "FormData", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "FormOID", null, bucket.FormOid).ConfigureAwait(false);
                if (bucket.FormRepeatKey is not null)
                {
                    await xml.WriteAttributeStringAsync(null, "FormRepeatKey", null, bucket.FormRepeatKey).ConfigureAwait(false);
                }

                if (bucket.FormMasked)
                {
                    await xml.WriteAttributeStringAsync("cadence", "Masked", CadenceNamespace, "true").ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync("cadence", "MaskReason", CadenceNamespace, bucket.FormMaskReason ?? "Blinded").ConfigureAwait(false);
                }

                if (bucket.Observations.Count > 0)
                {
                    await xml.WriteStartElementAsync(null, "ItemGroupData", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "ItemGroupOID", null, bucket.ItemGroupOid).ConfigureAwait(false);

                    foreach (var obs in bucket.Observations)
                    {
                        await WriteCasebookOdmItemDataAsync(xml, obs, casebook.SiteCode).ConfigureAwait(false);
                    }

                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                if (bucket.FormRepeatKey is not null
                    && signaturesByFormRef.TryGetValue(bucket.FormRepeatKey, out var formSignatures))
                {
                    foreach (var (att, sigOid) in formSignatures)
                    {
                        await WriteCasebookOdmSignatureElementAsync(xml, att, sigOid, casebook.SiteCode).ConfigureAwait(false);
                    }
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        foreach (var (att, sigOid) in subjectLevelSignatures)
        {
            await WriteCasebookOdmSignatureElementAsync(xml, att, sigOid, casebook.SiteCode).ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndDocumentAsync().ConfigureAwait(false);
        await xml.FlushAsync().ConfigureAwait(false);
    }

    private static List<CasebookOdmFormBucket> BuildCasebookOdmFormBuckets(KernelCasebookResponse casebook)
    {
        if (casebook.Masked)
        {
            return [];
        }

        var buckets = new List<CasebookOdmFormBucket>(casebook.Forms.Count + 1);
        var byGroupingId = new Dictionary<Guid, CasebookOdmFormBucket>();
        var byFormRef = new Dictionary<string, CasebookOdmFormBucket>(StringComparer.Ordinal);
        var bySpecId = new Dictionary<string, CasebookOdmFormBucket>(StringComparer.Ordinal);

        foreach (var form in casebook.Forms)
        {
            var eventKey = form.PlannedActivityId ?? "unscheduled";
            var eventOid = $"SE.{SanitizeOid(eventKey)}";
            var eventName = form.PlannedActivityName ?? (form.PlannedActivityId ?? "Unscheduled");
            var specId = string.IsNullOrWhiteSpace(form.CollectionSpecId) ? "unscheduled" : form.CollectionSpecId;
            var formOid = $"FORM.{SanitizeOid(specId)}";
            var igOid = $"IG.{SanitizeOid(specId)}";
            var repeatKey = !form.Masked && !string.Equals(form.Ref, "masked", StringComparison.Ordinal)
                ? form.Ref
                : null;
            var bucket = new CasebookOdmFormBucket(
                EventOid: eventOid,
                EventName: eventName,
                IsScheduled: form.PlannedActivityId is not null,
                SpecId: specId,
                FormOid: formOid,
                ItemGroupOid: igOid,
                FormName: form.Title,
                FormRepeatKey: repeatKey,
                FormMasked: form.Masked,
                FormMaskReason: form.Mask,
                Observations: []);
            buckets.Add(bucket);
            if (form.GroupingFormId is { } groupingGuid)
            {
                byGroupingId.TryAdd(groupingGuid, bucket);
            }

            if (repeatKey is not null)
            {
                byFormRef.TryAdd(repeatKey, bucket);
            }

            bySpecId.TryAdd(specId, bucket);
        }

        foreach (var obs in casebook.Observations)
        {
            var specId = string.IsNullOrWhiteSpace(obs.CollectionSpecId) ? "unscheduled" : obs.CollectionSpecId!;
            CasebookOdmFormBucket? target = null;
            if (obs.GroupingFormId is { } obsGroupingGuid)
            {
                byGroupingId.TryGetValue(obsGroupingGuid, out target);
            }

            if (target is null && obs.CollectionInstanceRef is { } ciRef)
            {
                byFormRef.TryGetValue(ciRef, out target);
            }

            if (target is null)
            {
                bySpecId.TryGetValue(specId, out target);
            }

            if (target is null)
            {
                target = new CasebookOdmFormBucket(
                    EventOid: "SE.unscheduled",
                    EventName: "Unscheduled",
                    IsScheduled: false,
                    SpecId: specId,
                    FormOid: $"FORM.{SanitizeOid(specId)}",
                    ItemGroupOid: $"IG.{SanitizeOid(specId)}",
                    FormName: specId,
                    FormRepeatKey: obs.CollectionInstanceRef,
                    FormMasked: false,
                    FormMaskReason: null,
                    Observations: []);
                buckets.Add(target);
                bySpecId[specId] = target;
                if (obs.CollectionInstanceRef is { } adhocRef)
                {
                    byFormRef.TryAdd(adhocRef, target);
                }
            }

            target.Observations.Add(obs);
        }

        return buckets;
    }

    private static List<(KernelCasebookAttestationView Attestation, string SignatureOid)> BuildCasebookSignatureDefinitions(
        IReadOnlyList<KernelCasebookAttestationView> attestations)
    {
        var result = new List<(KernelCasebookAttestationView, string)>(attestations.Count);
        var usedOids = new HashSet<string>(StringComparer.Ordinal);
        var maskedCounter = 0;
        foreach (var att in attestations)
        {
            string candidate;
            if (string.Equals(att.Ref, "masked", StringComparison.Ordinal))
            {
                maskedCounter++;
                candidate = $"SD.masked.{maskedCounter.ToString(CultureInfo.InvariantCulture)}";
            }
            else
            {
                candidate = $"SD.{SanitizeOid(att.Ref)}";
            }

            var sigOid = candidate;
            var suffix = 2;
            while (!usedOids.Add(sigOid))
            {
                sigOid = $"{candidate}.{suffix.ToString(CultureInfo.InvariantCulture)}";
                suffix++;
            }

            result.Add((att, sigOid));
        }

        return result;
    }

    private static async Task WriteCasebookOdmItemDataAsync(
        XmlWriter xml,
        KernelCasebookObservationView obs,
        string siteCode)
    {
        var itemOid = $"IT.{SanitizeOid(obs.ItemId ?? obs.ConceptCode)}";
        await xml.WriteStartElementAsync(null, "ItemData", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "ItemOID", null, itemOid).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "Value", null, obs.DisplayValue).ConfigureAwait(false);
        if (!obs.Masked && obs.Unit is not null)
        {
            await xml.WriteAttributeStringAsync("cadence", "Unit", CadenceNamespace, obs.Unit).ConfigureAwait(false);
        }

        if (obs.EffectiveLock is not null)
        {
            await xml.WriteAttributeStringAsync("cadence", "LockState", CadenceNamespace, obs.EffectiveLock).ConfigureAwait(false);
        }

        if (obs.Masked)
        {
            await xml.WriteAttributeStringAsync("cadence", "Masked", CadenceNamespace, "true").ConfigureAwait(false);
            await xml.WriteAttributeStringAsync("cadence", "MaskReason", CadenceNamespace, obs.Mask ?? "Blinded").ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            return;
        }

        if (obs.Revisions.Count > 0)
        {
            var latestRev = obs.Revisions[^1];
            await xml.WriteStartElementAsync(null, "AuditRecord", OdmNamespace).ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "UserRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "UserOID", null, latestRev.RecordedBy ?? "masked").ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "LocationRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "LocationOID", null, siteCode).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "DateTimeStamp", OdmNamespace, FormatInstant(latestRev.RecordedAt)).ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "ReasonForChange", OdmNamespace, latestRev.ReasonForChange ?? string.Empty).ConfigureAwait(false);
            await xml.WriteElementStringAsync(null, "SourceID", OdmNamespace, obs.Ref).ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);

            if (obs.Revisions.Count > 1)
            {
                await xml.WriteStartElementAsync("cadence", "Revisions", CadenceNamespace).ConfigureAwait(false);
                foreach (var rev in obs.Revisions)
                {
                    await xml.WriteStartElementAsync("cadence", "Revision", CadenceNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Number", null, rev.Revision.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Value", null, rev.DisplayValue).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "RecordedBy", null, rev.RecordedBy ?? "masked").ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "RecordedAt", null, FormatInstant(rev.RecordedAt)).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "ReasonForChange", null, rev.ReasonForChange ?? string.Empty).ConfigureAwait(false);
                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task WriteCasebookOdmSignatureElementAsync(
        XmlWriter xml,
        KernelCasebookAttestationView att,
        string signatureOid,
        string siteCode)
    {
        await xml.WriteStartElementAsync(null, "Signature", OdmNamespace).ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "UserRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "UserOID", null, att.SignerId).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "LocationRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "LocationOID", null, siteCode).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteStartElementAsync(null, "SignatureRef", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "SignatureOID", null, signatureOid).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "DateTimeStamp", OdmNamespace, FormatInstant(att.SignedAt)).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }
}
