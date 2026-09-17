using System.Collections.Generic;

namespace Cadence.Ddf;

public record CompilerWarning(
    string Field,
    object? GeneratedValue,
    string WarningReason
);

public record ItemLocalization(
    string? Label = null,
    string? Description = null,
    string? Hint = null
);

[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S101:Types should name in PascalCase", Justification = "DDF acronym naming convention")]
public record DDFItemIR(
    string Id,
    string Name,
    string Type = "string",
    string? Appearance = null,
    string? Relevant = null,
    string? Constraint = null,
    string? ConstraintMessage = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? Localizations = null
);

[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S101:Types should name in PascalCase", Justification = "DDF acronym naming convention")]
public record DDFStudyIR(
    string StudyId,
    string Name,
    IReadOnlyList<DDFItemIR>? Items = null,
    string? Version = null,
    IReadOnlyDictionary<string, object?>? Metadata = null
);

public record AccessibilityReport(
    string Status = "PASS",
    string FormHash = "",
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Violations = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Passes = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Incomplete = null,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Inapplicable = null,
    IReadOnlyList<string>? LayoutErrors = null
);

public record CompileOptions(
    bool ValidateAccessibility = true,
    string DefaultLanguage = "en",
    bool StrictSchemaValidation = true
);

[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarAnalyzer.CSharp", "S101:Types should name in PascalCase", Justification = "DDF acronym naming convention")]
public record DDFCompilationResult(
    string StudyId,
    string Name,
    string OdmXml,
    string OpenRosaXml,
    string FormHash,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Warnings = null,
    AccessibilityReport? AccessibilityReport = null,
    string Status = "COMPLETED"
);
