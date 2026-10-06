using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Cec;

namespace Cadence.Apps.Execution.Application.Cec;

/// <summary>
/// Request to submit a primary reader assessment (Reader 1 or Reader 2).
/// @req:PRD-CEC-002
/// </summary>
public sealed record SubmitReaderAssessmentRequest(
    string ReaderRole,
    string Classification,
    string? Subtype,
    string AdjudicatedOnsetDate,
    List<string>? CriteriaMet,
    int ConfidenceScore,
    string ClinicalRationale,
    string? ReaderName);

/// <summary>
/// Request to package/compile a candidate event dossier with automated PHI redaction and date-shifting.
/// @req:PRD-CEC-001
/// </summary>
public sealed record CompileDossierRequest(
    string DossierId,
    string EventId,
    string StudyId,
    string SubjectId,
    string? MaskedSubjectId,
    string CandidateEventType,
    string ReportedTerm,
    string ReportedOnsetDate,
    string? SiteId,
    string? MaskedSiteId,
    string? VerbatimDescription,
    string? MedicalNarrative,
    List<CecLabPanel>? LabPanels,
    List<CecEcgRecord>? EcgRecords,
    List<CecDiagnosticAttachment>? DiagnosticAttachments);

/// <summary>
/// Response for reader assessment submission.
/// @req:PRD-CEC-002
/// </summary>
public sealed record ReaderAssessmentResponse(
    string DossierId,
    string ReaderRole,
    string WorkflowStatus,
    bool IsDiscordant,
    List<string> DiscordanceReasons,
    string? FinalConsensusClassification,
    DateTime SubmittedAt);
