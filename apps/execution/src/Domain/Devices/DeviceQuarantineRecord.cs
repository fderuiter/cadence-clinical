using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Devices;

/// <summary>
/// Active or historical device quarantine action restricting inventory dispensation.
/// @req:PRD-DEV-003
/// </summary>
public sealed class DeviceQuarantineRecord : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string? DeficiencyId { get; private set; }
    public string? DeviceInventoryId { get; private set; }
    public string Scope { get; private set; } = "DEVICE_SERIAL";
    public string? LotNumber { get; private set; }
    public string? SerialNumber { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string Status { get; private set; } = "ACTIVE";
    public DateTimeOffset QuarantinedAt { get; private set; }
    public string QuarantinedBy { get; private set; } = string.Empty;
    public DateTimeOffset? ReleasedAt { get; private set; }
    public string? ReleasedBy { get; private set; }
    public string? ReleaseJustification { get; private set; }

    private DeviceQuarantineRecord() { } // EF Core

    public DeviceQuarantineRecord(
        Guid id,
        string studyId,
        string siteId,
        string reason,
        DateTimeOffset quarantinedAt,
        string scope = "DEVICE_SERIAL",
        string? deficiencyId = null,
        string? deviceInventoryId = null,
        string? lotNumber = null,
        string? serialNumber = null,
        string quarantinedBy = "svc:execution-quarantine-engine")
        : base(id)
    {
        StudyId = studyId;
        SiteId = siteId;
        Reason = reason;
        Scope = scope;
        DeficiencyId = deficiencyId;
        DeviceInventoryId = deviceInventoryId;
        LotNumber = lotNumber;
        SerialNumber = serialNumber;
        QuarantinedBy = quarantinedBy;
        QuarantinedAt = quarantinedAt;
        Status = "ACTIVE";
    }

    public void Release(string releasedBy, string justification, DateTimeOffset timestamp)
    {
        Status = "RELEASED";
        ReleasedBy = releasedBy;
        ReleaseJustification = justification;
        ReleasedAt = timestamp;
    }
}
