using System;
using System.Collections.Generic;

namespace Cadence.Apps.Execution.Application.Coding;

public record SynonymRowDto(
    int RowNumber,
    string VerbatimText,
    string DictionaryType,
    string DictionaryVersion,
    string TargetCode,
    string TargetTerm,
    string? TargetLevel);

public record InvalidRowDetailResponse(
    int RowNumber,
    string VerbatimText,
    string DictionaryType,
    string TargetCode,
    string ErrorReason);

public record PreflightValidationReportResponse(
    int TotalRows,
    int ValidCount,
    int DuplicateCount,
    int InvalidCount,
    IReadOnlyList<InvalidRowDetailResponse> InvalidRows,
    bool CanImport);

public record SynonymRuleResponse(
    Guid Id,
    string Scope,
    string? StudyId,
    string VerbatimText,
    string DictionaryType,
    string DictionaryVersion,
    string TargetCode,
    string TargetTerm,
    string? TargetLevel,
    string Status,
    Guid ImportJobId,
    string CreatedBy,
    DateTime CreatedAt,
    string ReasonForChange);

public record BulkSynonymImportJobResponse(
    Guid Id,
    string Scope,
    string? StudyId,
    string FileName,
    string FileHash,
    int TotalRows,
    int ValidRows,
    int DuplicateRows,
    int InvalidRows,
    string Status,
    string CreatedBy,
    DateTime CreatedAt,
    string ReasonForChange);

public record BulkImportExecutionResponse(
    BulkSynonymImportJobResponse Job,
    IReadOnlyList<SynonymRuleResponse> ImportedRules);
