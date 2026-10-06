using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Coding;

/// <summary>
/// Persistent synonym rule mapping a verbatim medical term to a dictionary target code.
/// Supports Study-level and Enterprise-level rule scoping and 21 CFR Part 11 GxP audit lineage.
/// @req:PRD-SYS-001
/// </summary>
public sealed class SynonymRule : IGxPAuditable
{
    public Guid Id { get; private set; }
    public string Scope { get; private set; } = "STUDY"; // "STUDY" | "ENTERPRISE"
    public string? StudyId { get; private set; }
    public string VerbatimText { get; private set; } = string.Empty;
    public string DictionaryType { get; private set; } = "MEDDRA"; // "MEDDRA" | "WHODRUG"
    public string DictionaryVersion { get; private set; } = string.Empty;
    public string TargetCode { get; private set; } = string.Empty;
    public string TargetTerm { get; private set; } = string.Empty;
    public string? TargetLevel { get; private set; } // "LLT" | "PT" | null
    public string Status { get; private set; } = "ACTIVE"; // "ACTIVE" | "INACTIVE"
    public Guid ImportJobId { get; private set; }

    // IGxPAuditable properties
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public long VersionIndex { get; set; } = 1;

    private SynonymRule() { } // EF Core constructor

    public SynonymRule(
        Guid id,
        string scope,
        string? studyId,
        string verbatimText,
        string dictionaryType,
        string dictionaryVersion,
        string targetCode,
        string targetTerm,
        string? targetLevel,
        Guid importJobId,
        string createdBy,
        string reasonForChange,
        DateTime createdAt)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Scope = (scope ?? "STUDY").Trim().ToUpperInvariant();
        StudyId = string.IsNullOrWhiteSpace(studyId) ? null : studyId.Trim();
        VerbatimText = verbatimText?.Trim() ?? string.Empty;
        DictionaryType = (dictionaryType ?? "MEDDRA").Trim().ToUpperInvariant();
        DictionaryVersion = dictionaryVersion?.Trim() ?? string.Empty;
        TargetCode = targetCode?.Trim() ?? string.Empty;
        TargetTerm = targetTerm?.Trim() ?? string.Empty;
        TargetLevel = string.IsNullOrWhiteSpace(targetLevel) ? null : targetLevel.Trim().ToUpperInvariant();
        Status = "ACTIVE";
        ImportJobId = importJobId;
        CreatedBy = createdBy?.Trim() ?? string.Empty;
        ReasonForChange = reasonForChange?.Trim() ?? string.Empty;
        CreatedAt = createdAt;
        VersionIndex = 1;
    }

    public void Deactivate(string reasonForChange)
    {
        Status = "INACTIVE";
        if (!string.IsNullOrWhiteSpace(reasonForChange))
        {
            ReasonForChange = reasonForChange.Trim();
        }
    }
}
