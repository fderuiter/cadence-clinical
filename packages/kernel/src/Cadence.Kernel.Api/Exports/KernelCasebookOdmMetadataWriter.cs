using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelCasebookOdmMetadataWriter
{
    public static async Task WriteCasebookOdmStudyMetadataAsync(
        XmlWriter xml,
        KernelCasebookResponse casebook,
        int versionNumber,
        ProtocolDesign? design,
        string? compiledOdmXml,
        IReadOnlyList<CasebookOdmFormBucket> clinicalForms)
    {
        var numText = versionNumber.ToString(CultureInfo.InvariantCulture);
        await xml.WriteStartElementAsync(null, "Study", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "OID", null, casebook.StudyCode).ConfigureAwait(false);

        await xml.WriteStartElementAsync(null, "GlobalVariables", OdmNamespace).ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "StudyName", OdmNamespace, casebook.StudyCode).ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "StudyDescription", OdmNamespace, casebook.StudyTitle).ConfigureAwait(false);
        await xml.WriteElementStringAsync(null, "ProtocolName", OdmNamespace, casebook.StudyCode).ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);

        await xml.WriteStartElementAsync(null, "MetaDataVersion", OdmNamespace).ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "OID", null, $"MDV.{numText}").ConfigureAwait(false);
        await xml.WriteAttributeStringAsync(null, "Name", null, $"Version {numText}").ConfigureAwait(false);

        if (design is null && !string.IsNullOrWhiteSpace(compiledOdmXml))
        {
            var copiedItemOids = new HashSet<string>(StringComparer.Ordinal);
            var copiedFormOids = new HashSet<string>(StringComparer.Ordinal);
            var copiedIgOids = new HashSet<string>(StringComparer.Ordinal);
            var copiedEventOids = new HashSet<string>(StringComparer.Ordinal);
            TryCopyCompiledMetaDataChildren(xml, compiledOdmXml, copiedItemOids, copiedFormOids, copiedIgOids, copiedEventOids);
            if (copiedFormOids.Count > 0)
            {
                foreach (var bucket in clinicalForms)
                {
                    foreach (var obs in bucket.Observations)
                    {
                        var rawItemId = obs.ItemId ?? obs.ConceptCode;
                        var itemOid = $"IT.{SanitizeOid(rawItemId)}";
                        if (copiedItemOids.Add(itemOid))
                        {
                            await xml.WriteStartElementAsync(null, "ItemDef", OdmNamespace).ConfigureAwait(false);
                            await xml.WriteAttributeStringAsync(null, "OID", null, itemOid).ConfigureAwait(false);
                            await xml.WriteAttributeStringAsync(null, "Name", null, obs.ConceptLabel).ConfigureAwait(false);
                            await xml.WriteAttributeStringAsync(null, "DataType", null, !obs.Masked && obs.NumericValue is not null ? "decimal" : "text").ConfigureAwait(false);
                            await xml.WriteEndElementAsync().ConfigureAwait(false);
                        }
                    }
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
                return;
            }
        }

        var eventDefs = new List<(string EventOid, string EventName, bool IsScheduled, List<string> FormOids)>();
        var eventIndexByOid = new Dictionary<string, int>(StringComparer.Ordinal);
        if (design is not null)
        {
            foreach (var activity in design.PlannedActivities)
            {
                var eOid = $"SE.{SanitizeOid(activity.Id)}";
                if (!eventIndexByOid.ContainsKey(eOid))
                {
                    eventIndexByOid[eOid] = eventDefs.Count;
                    eventDefs.Add((eOid, activity.Name, true, activity.CollectionSpecIds.Select(id => $"FORM.{SanitizeOid(id)}").Distinct(StringComparer.Ordinal).ToList()));
                }
            }
        }

        foreach (var bucket in clinicalForms)
        {
            if (!eventIndexByOid.TryGetValue(bucket.EventOid, out var idx))
            {
                eventIndexByOid[bucket.EventOid] = eventDefs.Count;
                eventDefs.Add((bucket.EventOid, bucket.EventName, bucket.IsScheduled, [bucket.FormOid]));
            }
            else if (!eventDefs[idx].FormOids.Contains(bucket.FormOid, StringComparer.Ordinal))
            {
                eventDefs[idx].FormOids.Add(bucket.FormOid);
            }
        }

        if (eventDefs.Count > 0)
        {
            await xml.WriteStartElementAsync(null, "Protocol", OdmNamespace).ConfigureAwait(false);
            foreach (var ev in eventDefs)
            {
                await xml.WriteStartElementAsync(null, "StudyEventRef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "StudyEventOID", null, ev.EventOid).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Mandatory", null, ev.IsScheduled ? "Yes" : "No").ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);

            foreach (var ev in eventDefs)
            {
                await xml.WriteStartElementAsync(null, "StudyEventDef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "OID", null, ev.EventOid).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Name", null, ev.EventName).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Repeating", null, "No").ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Type", null, ev.IsScheduled ? "Scheduled" : "Unscheduled").ConfigureAwait(false);
                foreach (var fOid in ev.FormOids)
                {
                    await xml.WriteStartElementAsync(null, "FormRef", OdmNamespace).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "FormOID", null, fOid).ConfigureAwait(false);
                    await xml.WriteAttributeStringAsync(null, "Mandatory", null, ev.IsScheduled ? "Yes" : "No").ConfigureAwait(false);
                    await xml.WriteEndElementAsync().ConfigureAwait(false);
                }

                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }
        }

        var formDefs = new List<(string FormOid, string IgOid, string Name, List<(string ItemOid, bool Mandatory)> ItemRefs)>();
        var formIndexByOid = new Dictionary<string, int>(StringComparer.Ordinal);
        var itemDefs = new List<(string ItemOid, string Name, string DataType, string? ConceptText)>();
        var seenItemOids = new HashSet<string>(StringComparer.Ordinal);

        if (design is not null)
        {
            foreach (var spec in design.CollectionSpecs)
            {
                var sanitizedSpec = SanitizeOid(spec.Id);
                var fOid = $"FORM.{sanitizedSpec}";
                var igOid = $"IG.{sanitizedSpec}";
                var itemRefs = new List<(string ItemOid, bool Mandatory)>(spec.Items.Count);
                foreach (var item in spec.Items)
                {
                    var itemOid = $"IT.{SanitizeOid(item.Id)}";
                    if (itemRefs.All(ir => !string.Equals(ir.ItemOid, itemOid, StringComparison.Ordinal)))
                    {
                        itemRefs.Add((itemOid, item.Required));
                    }

                    if (seenItemOids.Add(itemOid))
                    {
                        var dataType = MapItemDataType(item.ValueKind, item.Unit);
                        itemDefs.Add((itemOid, item.Id, dataType, item.Concept.ToString()));
                    }
                }

                if (!formIndexByOid.ContainsKey(fOid))
                {
                    formIndexByOid[fOid] = formDefs.Count;
                    formDefs.Add((fOid, igOid, spec.Name, itemRefs));
                }
            }
        }

        foreach (var bucket in clinicalForms)
        {
            if (!formIndexByOid.TryGetValue(bucket.FormOid, out var fIdx))
            {
                fIdx = formDefs.Count;
                formIndexByOid[bucket.FormOid] = fIdx;
                formDefs.Add((bucket.FormOid, bucket.ItemGroupOid, bucket.FormName, []));
            }

            foreach (var obs in bucket.Observations)
            {
                var rawItemId = obs.ItemId ?? obs.ConceptCode;
                var itemOid = $"IT.{SanitizeOid(rawItemId)}";
                if (formDefs[fIdx].ItemRefs.All(ir => !string.Equals(ir.ItemOid, itemOid, StringComparison.Ordinal)))
                {
                    formDefs[fIdx].ItemRefs.Add((itemOid, false));
                }

                if (seenItemOids.Add(itemOid))
                {
                    var dataType = !obs.Masked && obs.NumericValue is not null ? "decimal" : "text";
                    var conceptText = string.Equals(obs.ConceptRef, "masked", StringComparison.Ordinal) ? null : obs.ConceptRef;
                    itemDefs.Add((itemOid, obs.ConceptLabel, dataType, conceptText));
                }
            }
        }

        if (formDefs.Count == 0)
        {
            formDefs.Add(("FORM.casebook", "IG.casebook", "Participant Casebook", []));
        }

        foreach (var formDef in formDefs)
        {
            await xml.WriteStartElementAsync(null, "FormDef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "OID", null, formDef.FormOid).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Name", null, formDef.Name).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Repeating", null, "No").ConfigureAwait(false);
            await xml.WriteStartElementAsync(null, "ItemGroupRef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "ItemGroupOID", null, formDef.IgOid).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Mandatory", null, formDef.ItemRefs.Count > 0 ? "Yes" : "No").ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        foreach (var formDef in formDefs)
        {
            await xml.WriteStartElementAsync(null, "ItemGroupDef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "OID", null, formDef.IgOid).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Name", null, formDef.Name).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Repeating", null, "No").ConfigureAwait(false);
            foreach (var (itemOid, mandatory) in formDef.ItemRefs)
            {
                await xml.WriteStartElementAsync(null, "ItemRef", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "ItemOID", null, itemOid).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Mandatory", null, mandatory ? "Yes" : "No").ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        foreach (var itemDef in itemDefs)
        {
            await xml.WriteStartElementAsync(null, "ItemDef", OdmNamespace).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "OID", null, itemDef.ItemOid).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "Name", null, itemDef.Name).ConfigureAwait(false);
            await xml.WriteAttributeStringAsync(null, "DataType", null, itemDef.DataType).ConfigureAwait(false);
            if (itemDef.ConceptText is not null)
            {
                await xml.WriteStartElementAsync(null, "Alias", OdmNamespace).ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Context", null, "Cadence.Concept").ConfigureAwait(false);
                await xml.WriteAttributeStringAsync(null, "Name", null, itemDef.ConceptText).ConfigureAwait(false);
                await xml.WriteEndElementAsync().ConfigureAwait(false);
            }

            await xml.WriteEndElementAsync().ConfigureAwait(false);
        }

        await xml.WriteEndElementAsync().ConfigureAwait(false);
        await xml.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static string MapItemDataType(ObservationValueKind? kind, string? unit)
    {
        if (unit is not null)
        {
            return "decimal";
        }

        return kind switch
        {
            ObservationValueKind.Quantity => "decimal",
            ObservationValueKind.Boolean => "boolean",
            ObservationValueKind.DateTime => "datetime",
            _ => "text",
        };
    }
}
