using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Ctms.Domain.Events;

/// <summary>
/// Marker interface for CTMS local aggregate domain events.
/// </summary>
public interface ICtmsDomainEvent
{
    DateTimeOffset Timestamp { get; }
    string UserId { get; }
    string ChangeReason { get; }
}

public abstract record MonitoringVisitDomainEvent(
    string VisitId,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : ICtmsDomainEvent, IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt => Timestamp;
    public string EventType => GetType().Name;
}

public sealed record MonitoringVisitStartedDomainEvent(
    string VisitId,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : MonitoringVisitDomainEvent(VisitId, Timestamp, UserId, ChangeReason);

public sealed record MonitoringVisitCompletedDomainEvent(
    string VisitId,
    DateTimeOffset ActualDate,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : MonitoringVisitDomainEvent(VisitId, Timestamp, UserId, ChangeReason);

public sealed record MonitoringVisitReportDraftedDomainEvent(
    string VisitId,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : MonitoringVisitDomainEvent(VisitId, Timestamp, UserId, ChangeReason);

public sealed record MonitoringVisitApprovedDomainEvent(
    string VisitId,
    string Signer,
    DateTimeOffset SigningTimestamp,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : MonitoringVisitDomainEvent(VisitId, Timestamp, UserId, ChangeReason);

public sealed record MonitoringVisitCancelledDomainEvent(
    string VisitId,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : MonitoringVisitDomainEvent(VisitId, Timestamp, UserId, ChangeReason);

public sealed record MonitoringVisitStatusChangedDomainEvent(
    string VisitId,
    MonitoringVisitStatus PreviousStatus,
    MonitoringVisitStatus NewStatus,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : MonitoringVisitDomainEvent(VisitId, Timestamp, UserId, ChangeReason);

public abstract record MonitoringVisitFindingDomainEvent(
    string FindingId,
    string VisitId,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : ICtmsDomainEvent, IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt => Timestamp;
    public string EventType => GetType().Name;
}

public sealed record MonitoringVisitFindingStatusChangedDomainEvent(
    string FindingId,
    string VisitId,
    FindingResolutionStatus PreviousStatus,
    FindingResolutionStatus NewStatus,
    DateTimeOffset Timestamp,
    string UserId,
    string ChangeReason) : MonitoringVisitFindingDomainEvent(FindingId, VisitId, Timestamp, UserId, ChangeReason);
