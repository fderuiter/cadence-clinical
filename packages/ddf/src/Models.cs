using System.Collections.Generic;

namespace Cadence.Ddf;

/// <summary>An item's text in one language (a BCP 47 tag); the templates write one label and hint per language.</summary>
public record ItemLocalization(
    string Language,
    string Label,
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
    string? ConstraintSeverity = null,
    string? ConstraintCode = null,
    IReadOnlyList<ItemLocalization>? Localizations = null
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

public record SoACellMutation(
    string ActivityId,
    string EncounterId,
    bool IsApplicable,
    string? Details = null,
    string? Footnotes = null,
    string? Window = null,
    string? CollectionCondition = null,
    string? BiomedicalConceptCode = null
);

public record BatchCellMutationRequest(
    string StudyId,
    string VersionId,
    IReadOnlyList<SoACellMutation> Mutations,
    string? ChangeReason = null,
    IReadOnlyDictionary<string, string>? ActiveStandardsProfileConcepts = null
);

public record BatchCellMutationResult(
    string StudyId,
    string VersionId,
    int AppliedCount,
    int UnchangedCount,
    int ValidationErrorsCount,
    IReadOnlyList<string> UpdatedNodeIds,
    IReadOnlyList<string> LineageNodeIds,
    IReadOnlyList<string> ValidationErrors,
    string Status = "COMPLETED"
);

