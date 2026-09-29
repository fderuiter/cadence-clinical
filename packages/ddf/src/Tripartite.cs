using System;
using System.Collections.Generic;

namespace Cadence.Ddf;

public static class ClinicalDataType
{
    public const string String = "string";
    public const string Integer = "integer";
    public const string Float = "float";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Time = "time";
    public const string Boolean = "boolean";
    public const string Codelist = "codelist";
    public const string Text = "text";
    public const string RepeatLog = "repeat_log";
}

public static class WidgetType
{
    public const string TextInput = "text_input";
    public const string Textarea = "textarea";
    public const string NumberInput = "number_input";
    public const string RadioGroup = "radio_group";
    public const string SelectDropdown = "select_dropdown";
    public const string MultiSelect = "multi_select";
    public const string DatePicker = "date_picker";
    public const string TimePicker = "time_picker";
    public const string SliderVas = "slider_vas";
    public const string BodyMapCoordinate = "body_map_coordinate";
    public const string RepeatingLogGrid = "repeating_log_grid";
    public const string MedicalAutocode = "medical_autocode";
    public const string SensorStreamCard = "sensor_stream_card";
}

public static class ViewportMode
{
    public const string DesktopEdc = "desktop_edc";
    public const string MobileEpro = "mobile_epro";
    public const string TabletEsource = "tablet_esource";
}

public static class MigrationMode
{
    public const string Prospective = "prospective";
    public const string Retrospective = "retrospective";
}

public record CdiscMetadata(
    string Oid,
    string Domain = "VS",
    string VariableName = "VSORRES",
    string? CodelistOid = null,
    string? UcumUnit = null,
    string CoreDesignation = "Req",
    string? SdtmTarget = null
);

public record ConstraintDefinition(
    bool Required = true,
    double? MinValue = null,
    double? MaxValue = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? RegexPattern = null,
    IReadOnlyList<string>? AllowedValues = null
);

public record VariableDefinition(
    string Id,
    string Name,
    CdiscMetadata Cdisc,
    string DataType = ClinicalDataType.String,
    ConstraintDefinition? Constraints = null,
    bool PhiPiiMasked = false,
    string SdvTier = "TSDV_REPRESENTATIVE",
    bool Part11ReasonRequired = true
);

public record DataSchema(
    string FormId,
    string Name,
    string CdiscFormOid,
    IReadOnlyList<VariableDefinition> Variables,
    string SchemaVersion = "2.0"
);

public record FieldWidgetConfig(
    string VariableId,
    string Widget = WidgetType.TextInput,
    int GridColumnSpan = 12,
    string? Placeholder = null,
    string? HelpText = null,
    string? CssClass = null,
    bool Readonly = false,
    bool Hidden = false
);

public record GridSection(
    string Id,
    string Title,
    IReadOnlyList<FieldWidgetConfig> Widgets,
    int Columns = 12
);

public record UiSchema(
    string FormId,
    IReadOnlyList<GridSection> Sections,
    string DefaultViewport = ViewportMode.DesktopEdc,
    IReadOnlyDictionary<string, string>? ThemeTokens = null,
    string SchemaVersion = "2.0"
);

public record AstEditCheck(
    string Id,
    string Name,
    IReadOnlyDictionary<string, object?> AstExpression,
    string QueryMessage,
    string Severity = "HARD",
    IReadOnlyList<string>? TargetVariableIds = null
);

public record DerivedMathRule(
    string Id,
    string TargetVariableId,
    string FormulaExpression,
    IReadOnlyList<string>? InputVariableIds = null,
    IReadOnlyDictionary<string, double>? UnitConversion = null
);

public record CrossVisitDependency(
    string Id,
    string TriggerVisitId,
    string TriggerVariableId,
    IReadOnlyDictionary<string, object?> TriggerCondition,
    IReadOnlyList<string>? TargetVisitIdsToSpawn = null,
    IReadOnlyList<string>? TargetVisitIdsToSuppress = null
);

public record LogicSchema(
    string FormId,
    IReadOnlyList<AstEditCheck> EditChecks,
    IReadOnlyList<DerivedMathRule> DerivedRules,
    IReadOnlyList<CrossVisitDependency> CrossVisitDependencies,
    string SchemaVersion = "2.0"
);

public record TripartiteFormDefinition(
    string FormId,
    string ProtocolId,
    DataSchema DataSchema,
    UiSchema UiSchema,
    LogicSchema LogicSchema,
    int VersionIndex = 1,
    string VersionTag = "1.0.0",
    DateTimeOffset? CreatedAt = null,
    string CreatedBy = "system",
    string ReasonForChange = "Initial eCRF baseline release"
);

public record MigrationDirective(
    string VariableId,
    string Action,
    string MigrationMode = MigrationMode.Prospective,
    string? BackfillQueryMessage = null
);

public record PatientMigrationMatrix(
    string ProtocolId,
    string FromVersionTag,
    string ToVersionTag,
    IReadOnlyList<MigrationDirective> Directives,
    DateTimeOffset? EffectiveDate = null
);

public record SyntheticPayload(
    string PayloadId,
    string TestScenario,
    IReadOnlyDictionary<string, object?> Data,
    IReadOnlyDictionary<string, bool> ExpectedRuleOutcomes
);

public record UatEvidenceReport(
    string ReportId,
    string FormId,
    string ProtocolId,
    int TotalScenariosEvaluated,
    int ScenariosPassed,
    int ScenariosFailed,
    double RuleCoveragePercentage,
    double ExecutionDurationMs,
    string Sha256EvidenceDigest,
    bool GxpComplianceVerified = true,
    DateTimeOffset? ExecutedAt = null
);
