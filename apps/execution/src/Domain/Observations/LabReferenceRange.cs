using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Observations;

/// <summary>
/// Domain model for laboratory reference ranges and normal interval definitions.
/// Conforms to PRD-SYS-121 and 21 CFR Part 11 requirements.
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public sealed class LabReferenceRange : IGxPAuditable
{
    public Guid Id { get; private set; }
    public string StudyId { get; private set; } = string.Empty;
    public string TestCode { get; private set; } = string.Empty;
    public string TestName { get; private set; } = string.Empty;
    public string Source { get; private set; } = "CENTRAL"; // "CENTRAL" or "LOCAL"
    public string? SiteId { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public string NormalizedUnit { get; private set; } = string.Empty;
    public string SexApplicability { get; private set; } = "ALL"; // "M", "F", "ALL", "U"
    public double? AgeLow { get; private set; }
    public double? AgeHigh { get; private set; }
    public double? LowBound { get; private set; }
    public double? HighBound { get; private set; }
    public double? CriticalLow { get; private set; }
    public double? CriticalHigh { get; private set; }
    public string? LoincCode { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsDeleted { get; private set; }

    // IGxPAuditable properties
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public long VersionIndex { get; set; } = 1;

    private LabReferenceRange() { } // EF Core constructor

    public LabReferenceRange(
        Guid id,
        string studyId,
        string testCode,
        string testName,
        string source,
        string? siteId,
        string unit,
        string normalizedUnit,
        string sexApplicability,
        double? ageLow,
        double? ageHigh,
        double? lowBound,
        double? highBound,
        double? criticalLow,
        double? criticalHigh,
        string? loincCode,
        string createdBy,
        string reasonForChange,
        DateTime createdAt,
        bool isActive = true,
        bool isDeleted = false,
        long versionIndex = 1)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        StudyId = studyId?.Trim() ?? string.Empty;
        TestCode = testCode?.Trim().ToUpperInvariant() ?? string.Empty;
        TestName = testName?.Trim() ?? string.Empty;
        Source = string.IsNullOrWhiteSpace(source) ? "CENTRAL" : source.Trim().ToUpperInvariant();
        SiteId = string.IsNullOrWhiteSpace(siteId) || Source == "CENTRAL" ? null : siteId.Trim();
        Unit = unit?.Trim() ?? string.Empty;
        NormalizedUnit = string.IsNullOrWhiteSpace(normalizedUnit) ? Unit : normalizedUnit.Trim();
        SexApplicability = string.IsNullOrWhiteSpace(sexApplicability) ? "ALL" : sexApplicability.Trim().ToUpperInvariant();
        AgeLow = ageLow;
        AgeHigh = ageHigh;
        LowBound = lowBound;
        HighBound = highBound;
        CriticalLow = criticalLow;
        CriticalHigh = criticalHigh;
        LoincCode = string.IsNullOrWhiteSpace(loincCode) ? null : loincCode.Trim();
        CreatedBy = createdBy?.Trim() ?? string.Empty;
        ReasonForChange = reasonForChange?.Trim() ?? string.Empty;
        CreatedAt = createdAt;
        IsActive = isActive;
        IsDeleted = isDeleted;
        VersionIndex = versionIndex <= 0 ? 1 : versionIndex;
    }

    public void Update(
        string testName,
        string unit,
        string normalizedUnit,
        string sexApplicability,
        double? ageLow,
        double? ageHigh,
        double? lowBound,
        double? highBound,
        double? criticalLow,
        double? criticalHigh,
        string? loincCode,
        string updatedBy,
        string reasonForChange)
    {
        TestName = testName?.Trim() ?? TestName;
        Unit = unit?.Trim() ?? Unit;
        NormalizedUnit = string.IsNullOrWhiteSpace(normalizedUnit) ? Unit : normalizedUnit.Trim();
        SexApplicability = string.IsNullOrWhiteSpace(sexApplicability) ? SexApplicability : sexApplicability.Trim().ToUpperInvariant();
        AgeLow = ageLow;
        AgeHigh = ageHigh;
        LowBound = lowBound;
        HighBound = highBound;
        CriticalLow = criticalLow;
        CriticalHigh = criticalHigh;
        LoincCode = string.IsNullOrWhiteSpace(loincCode) ? LoincCode : loincCode.Trim();
        CreatedBy = updatedBy?.Trim() ?? CreatedBy;
        ReasonForChange = reasonForChange?.Trim() ?? ReasonForChange;
        VersionIndex++;
    }

    public void Deactivate(string updatedBy, string reasonForChange)
    {
        IsActive = false;
        IsDeleted = true;
        CreatedBy = updatedBy?.Trim() ?? CreatedBy;
        ReasonForChange = reasonForChange?.Trim() ?? ReasonForChange;
        VersionIndex++;
    }
}
