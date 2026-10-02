using System;
using System.Collections.Generic;

namespace Cadence.Contracts.Events;

/// <summary>
/// Event published to the MassTransit transactional outbox whenever an eCRF submission undergoes real-time fraud sentinel evaluation.
/// </summary>
public record FraudSentinelEvaluatedEvent(
    Guid EventId,
    string SubmissionId,
    string StudyId,
    string SubjectId,
    string? SiteId,
    string FormId,
    string? VisitId,
    DateTime EvaluatedAt,
    int TotalNumericObservations,
    bool IsFlagged,
    IReadOnlyList<string> ChecksExecuted
);

/// <summary>
/// Event published to the MassTransit transactional outbox when a statistical fraud anomaly (digit preference or site variance) is detected.
/// </summary>
public record FraudSentinelFlaggedEvent(
    Guid EventId,
    string SubmissionId,
    string StudyId,
    string SubjectId,
    string? SiteId,
    string FormId,
    string? VisitId,
    DateTime FlaggedAt,
    string AnomalyType,
    string Severity,
    double? ChiSquareScore,
    double? PValue,
    double? VarianceRatio,
    string Summary,
    IReadOnlyList<string> Violations
);
