using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Api;

internal static class KernelCsvExportWriter
{
    public static async Task WriteStudyCsvAsync(
        Stream destination,
        ProjectedStudyExport export,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 16384, leaveOpen: true);

        const string header = "study_code,site_code,subject_code,enrollment_ref,observation_ref,collection_instance_ref,collection_spec_id,planned_activity_id,item_id,concept_ref,concept_code,concept_label,observed_at,revision,value_kind,result_text,result_decimal,result_unit,lock_state,masked,mask_reason,recorded_by,recorded_at,valid_at,known_at";
        await writer.WriteLineAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);

        var validAtIso = KernelExportFormatting.FormatInstant(export.Snapshot.ValidAt);
        var knownAtIso = KernelExportFormatting.FormatInstant(export.Snapshot.KnownAt);

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
                row.ObservedAt is { } obsAt ? KernelExportFormatting.FormatInstant(obsAt) : string.Empty,
                row.Revision?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.ValueKind,
                row.ResultText,
                row.ResultDecimal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.ResultUnit ?? string.Empty,
                row.LockState ?? string.Empty,
                row.Masked ? "true" : "false",
                row.MaskReason ?? string.Empty,
                row.RecordedBy ?? string.Empty,
                row.RecordedAt is { } recAt ? KernelExportFormatting.FormatInstant(recAt) : string.Empty,
                validAtIso,
                knownAtIso,
            };

            var line = string.Join(',', fields.Select(KernelExportFormatting.EscapeCsvField));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
