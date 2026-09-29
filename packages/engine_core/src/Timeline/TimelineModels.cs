using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Core.Domain.Timeline;

/// <summary>
/// Anchor event types for calculating relative visit dates.
/// @req:PRD-SYS-053
/// </summary>
public enum AnchorEventType
{
    SCREENING,
    CONSENT,
    RANDOMIZATION,
    FIRST_DOSE,
    CYCLE_START,
    PREVIOUS_VISIT,
    STUDY_START
}

/// <summary>
/// Visit deviation classification.
/// @req:PRD-SYS-053
/// </summary>
public enum VisitDeviationClassification
{
    WITHIN_WINDOW,
    EARLY_VISIT,
    LATE_VISIT,
    OUT_OF_WINDOW_MAJOR,
    MISSED_VISIT
}

/// <summary>
/// Severity of visit deviation.
/// @req:PRD-SYS-053
/// </summary>
public enum DeviationSeverity
{
    NONE,
    MINOR,
    MAJOR
}

/// <summary>
/// Temporal boundary envelope defining permissible calendar day deviations around target.
/// @req:PRD-SYS-053
/// </summary>
public record VisitWindowEnvelope(
    int WindowBeforeDays = 0,
    int WindowAfterDays = 0,
    int MajorDeviationToleranceDays = 7
);

/// <summary>
/// Specification for a single clinical encounter or visit milestone in the study timeline.
/// @req:PRD-SYS-053
/// </summary>
public record EncounterScheduleItem(
    string EncounterId,
    string Name,
    AnchorEventType AnchorEvent = AnchorEventType.FIRST_DOSE,
    string? PredecessorEncounterId = null,
    int TargetDayOffset = 0,
    VisitWindowEnvelope? Window = null,
    bool IsMandatory = true,
    string? EpochId = null,
    int? CycleNumber = null
)
{
    public VisitWindowEnvelope Window { get; init; } = Window ?? new VisitWindowEnvelope();
}

/// <summary>
/// Specification for repeating oncology/multi-cycle treatment regimens.
/// @req:PRD-SYS-053
/// </summary>
public record CycleTemplateDefinition(
    string TemplateId,
    string Name,
    int CycleCount,
    int CycleLengthDays,
    IReadOnlyList<EncounterScheduleItem>? EncountersPerCycle = null
)
{
    public IReadOnlyList<EncounterScheduleItem> EncountersPerCycle { get; init; } = EncountersPerCycle ?? Array.Empty<EncounterScheduleItem>();
}

/// <summary>
/// Calculated calendar milestone projection for a subject's clinical visit.
/// @req:PRD-SYS-053
/// </summary>
public record CalculatedVisitSchedule(
    string EncounterId,
    string Name,
    string TargetDate,
    string WindowStartDate,
    string WindowEndDate,
    string AnchorUsed,
    string AnchorDate,
    int? CycleNumber = null,
    bool IsMandatory = true,
    string? EpochId = null
);

/// <summary>
/// Audit-trailed evaluation of an actual visit completion against scheduled window.
/// @req:PRD-SYS-053
/// </summary>
public record VisitDeviationAssessment(
    string EncounterId,
    string ActualDate,
    string TargetDate,
    string WindowStartDate,
    string WindowEndDate,
    int DeltaDaysFromTarget,
    VisitDeviationClassification Classification,
    bool IsDeviation,
    DeviationSeverity DeviationSeverity,
    string Rationale,
    DateTime? EvalTimestamp = null
)
{
    public DateTime? EvalTimestamp { get; init; } = EvalTimestamp ?? DateTime.UtcNow;
}

/// <summary>
/// Immutable, version-pinned schedule specification for a clinical trial protocol.
/// @req:PRD-SYS-053
/// </summary>
public record TimelineScheduleDefinition
{
    public string StudyId { get; init; }
    public int VersionIndex { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<CycleTemplateDefinition> CycleTemplates { get; init; }
    public IReadOnlyList<EncounterScheduleItem> Encounters { get; init; }
    public string? ReasonForChange { get; init; }
    public string? Sha256Checksum { get; init; }

    public TimelineScheduleDefinition(
        string studyId,
        int versionIndex = 1,
        bool isActive = true,
        IReadOnlyList<CycleTemplateDefinition>? cycleTemplates = null,
        IReadOnlyList<EncounterScheduleItem>? encounters = null,
        string? reasonForChange = null,
        string? sha256Checksum = null)
    {
        StudyId = studyId;
        VersionIndex = versionIndex;
        IsActive = isActive;
        CycleTemplates = cycleTemplates ?? Array.Empty<CycleTemplateDefinition>();
        Encounters = encounters ?? Array.Empty<EncounterScheduleItem>();
        ReasonForChange = reasonForChange;

        if (!string.IsNullOrEmpty(sha256Checksum))
        {
            Sha256Checksum = sha256Checksum;
        }
        else
        {
            var payload = new
            {
                cycle_templates = CycleTemplates,
                encounters = Encounters,
                study_id = StudyId,
                version_index = VersionIndex
            };
            string canonical = JsonSerializer.Serialize(payload);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            Sha256Checksum = Convert.ToHexStringLower(hash);
        }
    }
}
