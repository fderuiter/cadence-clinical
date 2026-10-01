using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cadence.Ddf;

public enum ClinicalDataType
{
    String,
    Integer,
    Float,
    Date,
    Datetime,
    Time,
    Boolean,
    Codelist,
    Text,
    RepeatLog
}

public enum WidgetType
{
    TextInput,
    Textarea,
    NumberInput,
    RadioGroup,
    SelectDropdown,
    MultiSelect,
    DatePicker,
    TimePicker,
    SliderVas,
    BodyMapCoordinate,
    RepeatingLogGrid,
    MedicalAutocode,
    SensorStreamCard
}

public enum ViewportMode
{
    DesktopEdc,
    MobileEpro,
    TabletEsource
}

public enum MigrationMode
{
    Prospective,
    Retrospective
}

public sealed record CdiscMetadata(
    [property: JsonPropertyName("domain")] string Domain = "VS",
    [property: JsonPropertyName("variable_name")] string VariableName = "VSORRES",
    [property: JsonPropertyName("oid")] string Oid = "",
    [property: JsonPropertyName("codelist_oid")] string? CodelistOid = null,
    [property: JsonPropertyName("ucum_unit")] string? UcumUnit = null,
    [property: JsonPropertyName("core_designation")] string CoreDesignation = "Req",
    [property: JsonPropertyName("sdtm_target")] string? SdtmTarget = null
);

public sealed record ConstraintDefinition(
    [property: JsonPropertyName("required")] bool Required = true,
    [property: JsonPropertyName("min_value")] double? MinValue = null,
    [property: JsonPropertyName("max_value")] double? MaxValue = null,
    [property: JsonPropertyName("min_length")] int? MinLength = null,
    [property: JsonPropertyName("max_length")] int? MaxLength = null,
    [property: JsonPropertyName("regex_pattern")] string? RegexPattern = null,
    [property: JsonPropertyName("allowed_values")] IReadOnlyList<string>? AllowedValues = null
);

public sealed record VariableDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("data_type")] ClinicalDataType DataType = ClinicalDataType.String,
    [property: JsonPropertyName("cdisc")] CdiscMetadata? Cdisc = null,
    [property: JsonPropertyName("constraints")] ConstraintDefinition? Constraints = null,
    [property: JsonPropertyName("phi_pii_masked")] bool PhiPiiMasked = false,
    [property: JsonPropertyName("sdv_tier")] string SdvTier = "TSDV_REPRESENTATIVE",
    [property: JsonPropertyName("part11_reason_required")] bool Part11ReasonRequired = true
);

public sealed record DataSchema(
    [property: JsonPropertyName("schema_version")] string SchemaVersion = "2.0",
    [property: JsonPropertyName("form_id")] string FormId = "",
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("cdisc_form_oid")] string CdiscFormOid = "",
    [property: JsonPropertyName("variables")] IReadOnlyList<VariableDefinition>? Variables = null
);

public sealed record FieldWidgetConfig(
    [property: JsonPropertyName("variable_id")] string VariableId,
    [property: JsonPropertyName("widget")] WidgetType Widget = WidgetType.TextInput,
    [property: JsonPropertyName("grid_column_span")] int GridColumnSpan = 12,
    [property: JsonPropertyName("placeholder")] string? Placeholder = null,
    [property: JsonPropertyName("help_text")] string? HelpText = null,
    [property: JsonPropertyName("css_class")] string? CssClass = null,
    [property: JsonPropertyName("readonly")] bool Readonly = false,
    [property: JsonPropertyName("hidden")] bool Hidden = false
);

public sealed record GridSection(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("columns")] int Columns = 12,
    [property: JsonPropertyName("widgets")] IReadOnlyList<FieldWidgetConfig>? Widgets = null
);

public sealed record UiSchema(
    [property: JsonPropertyName("schema_version")] string SchemaVersion = "2.0",
    [property: JsonPropertyName("form_id")] string FormId = "",
    [property: JsonPropertyName("sections")] IReadOnlyList<GridSection>? Sections = null,
    [property: JsonPropertyName("default_viewport")] ViewportMode DefaultViewport = ViewportMode.DesktopEdc,
    [property: JsonPropertyName("theme_tokens")] IReadOnlyDictionary<string, string>? ThemeTokens = null
);

public sealed record AstEditCheck(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ast_expression")] JsonNode? AstExpression = null,
    [property: JsonPropertyName("severity")] string Severity = "HARD",
    [property: JsonPropertyName("query_message")] string QueryMessage = "",
    [property: JsonPropertyName("target_variable_ids")] IReadOnlyList<string>? TargetVariableIds = null
);

public sealed record DerivedMathRule(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("target_variable_id")] string TargetVariableId,
    [property: JsonPropertyName("formula_expression")] string FormulaExpression = "",
    [property: JsonPropertyName("input_variable_ids")] IReadOnlyList<string>? InputVariableIds = null,
    [property: JsonPropertyName("unit_conversion")] IReadOnlyDictionary<string, double>? UnitConversion = null
);

public sealed record CrossVisitDependency(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("trigger_visit_id")] string TriggerVisitId,
    [property: JsonPropertyName("trigger_variable_id")] string TriggerVariableId,
    [property: JsonPropertyName("trigger_condition")] JsonNode? TriggerCondition = null,
    [property: JsonPropertyName("target_visit_ids_to_spawn")] IReadOnlyList<string>? TargetVisitIdsToSpawn = null,
    [property: JsonPropertyName("target_visit_ids_to_suppress")] IReadOnlyList<string>? TargetVisitIdsToSuppress = null
);

public sealed record LogicSchema(
    [property: JsonPropertyName("schema_version")] string SchemaVersion = "2.0",
    [property: JsonPropertyName("form_id")] string FormId = "",
    [property: JsonPropertyName("edit_checks")] IReadOnlyList<AstEditCheck>? EditChecks = null,
    [property: JsonPropertyName("derived_rules")] IReadOnlyList<DerivedMathRule>? DerivedRules = null,
    [property: JsonPropertyName("cross_visit_dependencies")] IReadOnlyList<CrossVisitDependency>? CrossVisitDependencies = null
);

public sealed record TripartiteFormDefinition(
    [property: JsonPropertyName("form_id")] string FormId,
    [property: JsonPropertyName("protocol_id")] string ProtocolId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("version_tag")] string VersionTag,
    [property: JsonPropertyName("data_schema")] DataSchema DataSchema,
    [property: JsonPropertyName("ui_schema")] UiSchema UiSchema,
    [property: JsonPropertyName("logic_schema")] LogicSchema LogicSchema,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("created_by")] string CreatedBy = "svc:ddf-compiler",
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange = "Initial eCRF baseline release"
);

public sealed record MigrationDirective(
    [property: JsonPropertyName("variable_id")] string VariableId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("migration_mode")] MigrationMode MigrationMode = MigrationMode.Prospective,
    [property: JsonPropertyName("backfill_query_message")] string? BackfillQueryMessage = null
);

public sealed record PatientMigrationMatrix(
    [property: JsonPropertyName("protocol_id")] string ProtocolId,
    [property: JsonPropertyName("from_version_tag")] string FromVersionTag,
    [property: JsonPropertyName("to_version_tag")] string ToVersionTag,
    [property: JsonPropertyName("directives")] IReadOnlyList<MigrationDirective>? Directives = null,
    [property: JsonPropertyName("effective_date")] DateTimeOffset EffectiveDate = default
);

public sealed record PatientMigrationResult(
    [property: JsonPropertyName("protocol_id")] string ProtocolId,
    [property: JsonPropertyName("from_version")] string FromVersion,
    [property: JsonPropertyName("to_version")] string ToVersion,
    [property: JsonPropertyName("patient_data")] IReadOnlyDictionary<string, object?> PatientData,
    [property: JsonPropertyName("queries_raised")] IReadOnlyList<string> QueriesRaised,
    [property: JsonPropertyName("directives_applied_count")] int DirectivesAppliedCount,
    [property: JsonPropertyName("is_compliant")] bool IsCompliant
);

public sealed record SyntheticPayload(
    [property: JsonPropertyName("payload_id")] string PayloadId,
    [property: JsonPropertyName("test_scenario")] string TestScenario,
    [property: JsonPropertyName("data")] IReadOnlyDictionary<string, object?> Data,
    [property: JsonPropertyName("expected_rule_outcomes")] IReadOnlyDictionary<string, bool> ExpectedRuleOutcomes
);

public sealed record UatEvidenceReport(
    [property: JsonPropertyName("report_id")] string ReportId,
    [property: JsonPropertyName("form_id")] string FormId,
    [property: JsonPropertyName("protocol_id")] string ProtocolId,
    [property: JsonPropertyName("executed_at")] DateTimeOffset ExecutedAt,
    [property: JsonPropertyName("total_scenarios_evaluated")] int TotalScenariosEvaluated,
    [property: JsonPropertyName("scenarios_passed")] int ScenariosPassed,
    [property: JsonPropertyName("scenarios_failed")] int ScenariosFailed,
    [property: JsonPropertyName("rule_coverage_percentage")] double RuleCoveragePercentage,
    [property: JsonPropertyName("execution_duration_ms")] double ExecutionDurationMs,
    [property: JsonPropertyName("gxp_compliance_verified")] bool GxpComplianceVerified,
    [property: JsonPropertyName("sha256_evidence_digest")] string Sha256EvidenceDigest
);
