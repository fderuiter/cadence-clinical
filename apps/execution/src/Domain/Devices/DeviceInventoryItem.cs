using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Devices;

/// <summary>
/// Serialized investigational medical device or in-vitro diagnostic (IVD) inventory item tracked in RTSM.
/// Enforces programmatic dispensation gating when quarantined or expired.
/// @req:PRD-DEV-001
/// @req:PRD-DEV-003
/// </summary>
public sealed class DeviceInventoryItem : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string DeviceIdentifier { get; private set; } = string.Empty;
    public string BrandName { get; private set; } = string.Empty;
    public string? DeviceModel { get; private set; }
    public string DeviceType { get; private set; } = "THERAPEUTIC";
    public string DeviceClassification { get; private set; } = "CLASS_IIA";
    public string UdiDi { get; private set; } = string.Empty;
    public string? UdiPi { get; private set; }
    public string UdiCarrierFormat { get; private set; } = "GS1_128";
    public string LotNumber { get; private set; } = string.Empty;
    public string? SerialNumber { get; private set; }
    public DateTimeOffset? ExpirationDate { get; private set; }
    public DateTimeOffset? ManufacturingDate { get; private set; }
    public string? SoftwareVersion { get; private set; }
    public string Status { get; private set; } = "AVAILABLE";
    public bool IsDispensationBlocked { get; private set; }
    public string? AllocatedSubjectId { get; private set; }
    public DateTimeOffset? DispensedAt { get; private set; }
    public string? DispensedBy { get; private set; }
    public DateTimeOffset? QuarantinedAt { get; private set; }
    public string? QuarantinedBy { get; private set; }
    public string? QuarantineReason { get; private set; }

    private DeviceInventoryItem() { } // EF Core

    public DeviceInventoryItem(
        Guid id,
        string studyId,
        string siteId,
        string brandName,
        string udiDi,
        string lotNumber,
        string? serialNumber = null,
        string? deviceModel = null,
        string deviceType = "THERAPEUTIC",
        string deviceClassification = "CLASS_IIA",
        string? udiCarrierFormat = "GS1_128",
        DateTimeOffset? expirationDate = null,
        DateTimeOffset? manufacturingDate = null,
        string? softwareVersion = null,
        string createdBy = "svc:execution",
        string reasonForChange = "Investigational medical device inventory intake")
        : base(id)
    {
        StudyId = studyId;
        SiteId = siteId;
        BrandName = brandName;
        UdiDi = udiDi;
        LotNumber = lotNumber;
        SerialNumber = serialNumber;
        DeviceModel = deviceModel;
        DeviceType = deviceType;
        DeviceClassification = deviceClassification;
        UdiCarrierFormat = udiCarrierFormat ?? "GS1_128";
        ExpirationDate = expirationDate;
        ManufacturingDate = manufacturingDate;
        SoftwareVersion = softwareVersion;

        UdiPi = !string.IsNullOrEmpty(serialNumber) ? $"(21){serialNumber}(10){lotNumber}" : $"(10){lotNumber}";
        DeviceIdentifier = !string.IsNullOrEmpty(serialNumber) ? $"{udiDi}-{serialNumber}" : $"{udiDi}-{lotNumber}";
        Status = "AVAILABLE";
        IsDispensationBlocked = false;
    }

    public void Quarantine(string reason, string quarantinedBy, DateTimeOffset timestamp)
    {
        Status = "QUARANTINED";
        IsDispensationBlocked = true;
        QuarantinedAt = timestamp;
        QuarantinedBy = quarantinedBy;
        QuarantineReason = reason;
    }

    public void ReleaseQuarantine(string releasedBy)
    {
        Status = "AVAILABLE";
        IsDispensationBlocked = false;
        QuarantinedAt = null;
        QuarantinedBy = null;
        QuarantineReason = null;
    }

    public void Dispense(string subjectId, string dispensedBy, DateTimeOffset timestamp)
    {
        Status = "DISPENSED";
        AllocatedSubjectId = subjectId;
        DispensedAt = timestamp;
        DispensedBy = dispensedBy;
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return ExpirationDate.HasValue && ExpirationDate.Value <= now;
    }
}
