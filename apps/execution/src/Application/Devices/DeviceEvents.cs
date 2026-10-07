using System;

namespace Cadence.Apps.Execution.Application.Devices;

/// <summary>
/// Transactional outbox event emitted when a medical device deficiency is reported.
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// </summary>
public record DeviceDeficiencyReportedEvent(
    Guid DeficiencyId,
    string StudyId,
    string SiteId,
    string? SubjectId,
    string UdiDi,
    string LotNumber,
    string? SerialNumber,
    string DeficiencyType,
    string DeficiencyDescription,
    bool CouldLeadToSade,
    string? SafetyCaseId,
    DateTimeOffset ReportedAt,
    string ReportedBy);

/// <summary>
/// Transactional outbox event emitted when a device serial or lot is quarantined in RTSM.
/// @req:PRD-DEV-003
/// </summary>
public record DeviceQuarantinedEvent(
    Guid QuarantineId,
    string StudyId,
    string SiteId,
    string Scope,
    string? UdiDi,
    string? LotNumber,
    string? SerialNumber,
    string Reason,
    DateTimeOffset QuarantinedAt,
    string QuarantinedBy);
