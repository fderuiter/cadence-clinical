using System;
using System.Collections.Generic;

namespace Cadence.Apps.Execution.Domain.Cec;

/// <summary>
/// Laboratory analyte result attached to a candidate event dossier.
/// @req:PRD-CEC-001
/// </summary>
public sealed record CecLabPanel(
    string TestName,
    string? LoincCode,
    string ResultValue,
    string ResultUnit,
    double? ReferenceLow,
    double? ReferenceHigh,
    bool IsOutOfRange,
    int? ToxicityGrade);

/// <summary>
/// 12-lead ECG tracing summary attached to a candidate event dossier.
/// @req:PRD-CEC-001
/// </summary>
public sealed record CecEcgRecord(
    string EcgId,
    DateTime Timestamp,
    string Rhythm,
    List<string>? StElevationLeads,
    List<string>? StDepressionLeads,
    List<string>? QWaveLeads,
    bool LbbbPresent,
    int? QtcfMs,
    string Interpretation);

/// <summary>
/// Diagnostic imaging or specialty report attached to a candidate event dossier.
/// @req:PRD-CEC-001
/// </summary>
public sealed record CecDiagnosticAttachment(
    string AttachmentId,
    string DocumentType,
    string Title,
    string Summary,
    List<string> KeyFindings);
