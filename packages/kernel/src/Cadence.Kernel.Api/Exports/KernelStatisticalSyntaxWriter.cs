using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelStatisticalSyntaxWriter
{
    public static List<CollectionTableSlice> BuildCollectionTables(ProjectedStudyExport export)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slices = new List<CollectionTableSlice>();

        var grouped = export.Rows
            .GroupBy(r => r.CollectionSpecId, StringComparer.Ordinal)
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

            var label = export.Snapshot.Collections
                .FirstOrDefault(c => string.Equals(c.CollectionSpecId, group.Key, StringComparison.Ordinal))
                ?.CollectionSpecName
                ?? group.Key;

            slices.Add(new CollectionTableSlice(candidate, group.Key, label, group.ToList()));
        }

        return slices;
    }

    public static string BuildSasSyntaxScript(
        ProjectedStudyExport export,
        IReadOnlyList<CollectionTableSlice> collectionTables)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("/* ========================================================================= */");
        sb.AppendLine("/* Cadence Clinical Kernel - SAS Statistical Import Script                   */");
        sb.AppendLine($"/* Study: {EscapeSasComment(export.Snapshot.StudyCode)} ({EscapeSasComment(export.Snapshot.StudyTitle)}) */");
        sb.AppendLine($"/* Lock Filter: {EscapeSasComment(export.LockFilter)}                        */");
        sb.AppendLine($"/* Valid At: {FormatInstant(export.Snapshot.ValidAt)}                        */");
        sb.AppendLine($"/* Known At: {FormatInstant(export.Snapshot.KnownAt)}                        */");
        sb.AppendLine($"/* Generated At: {FormatInstant(export.GeneratedAt)}                         */");
        if (export.Hidden.MoreExist)
        {
            sb.AppendLine($"/* Hidden Scope Notice: more_exist=true ({EscapeSasComment(string.Join(", ", export.Hidden.Reasons))}) */");
        }

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

    public static string BuildRSyntaxScript(
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
        if (export.Hidden.MoreExist)
        {
            sb.AppendLine($"# Hidden Scope Notice: more_exist=true ({EscapeRComment(string.Join(", ", export.Hidden.Reasons))})");
        }

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
}
