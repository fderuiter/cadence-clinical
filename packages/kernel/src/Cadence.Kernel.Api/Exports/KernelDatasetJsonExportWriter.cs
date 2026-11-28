using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

internal static class KernelDatasetJsonExportWriter
{
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
        writer.WriteString("datasetJSONCreationDateTime", KernelExportFormatting.FormatInstant(export.GeneratedAt));
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
        writer.WriteString("valid_at", KernelExportFormatting.FormatInstant(snapshot.ValidAt));
        writer.WriteString("known_at", KernelExportFormatting.FormatInstant(snapshot.KnownAt));
        writer.WriteString("lock_filter", export.LockFilter);
        writer.WriteEndObject();

        writer.WritePropertyName("hidden");
        writer.WriteStartObject();
        writer.WriteBoolean("more_exist", export.Hidden.MoreExist);
        writer.WritePropertyName("reasons");
        writer.WriteStartArray();
        foreach (var reason in export.Hidden.Reasons)
        {
            writer.WriteStringValue(reason);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("hiddenScopes");
        writer.WriteStartObject();
        writer.WriteBoolean("more_exist", export.HiddenScopes.MoreExist);
        writer.WritePropertyName("reasons");
        writer.WriteStartArray();
        foreach (var reason in export.HiddenScopes.Reasons)
        {
            writer.WriteStringValue(reason);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("itemGroupData");
        writer.WriteStartObject();

        var groups = export.Rows
            .GroupBy(r => $"IG.{KernelExportFormatting.SanitizeOid(r.CollectionSpecId)}", StringComparer.Ordinal)
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
                var label = snapshot.Collections.FirstOrDefault(c => string.Equals(c.CollectionSpecId, specId, StringComparison.Ordinal))?.CollectionSpecName
                    ?? specId;
                WriteDatasetJsonGroup(writer, group.Key, label, group.ToList());
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
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
            WriteNullableString(writer, r.ObservedAt is { } obsAt ? KernelExportFormatting.FormatInstant(obsAt) : null);
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
            WriteNullableString(writer, r.RecordedAt is { } recAt ? KernelExportFormatting.FormatInstant(recAt) : null);
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
}
