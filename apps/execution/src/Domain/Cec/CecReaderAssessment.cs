using System;
using System.Collections.Generic;

namespace Cadence.Apps.Execution.Domain.Cec;

/// <summary>
/// Assessment recorded by a blinded primary reader for a CEC dossier.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// </summary>
public sealed record CecReaderAssessment(
    string AssessmentId,
    string DossierId,
    string ReaderId,
    string ReaderName,
    string ReaderRole,
    string Classification,
    string? Subtype,
    string AdjudicatedOnsetDate,
    List<string> CriteriaMet,
    int ConfidenceScore,
    string ClinicalRationale,
    DateTime SubmittedAt);
