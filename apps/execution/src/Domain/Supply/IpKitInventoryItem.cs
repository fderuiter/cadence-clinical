using System;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Supply;

public sealed class IpKitInventoryItem : AggregateRoot<Guid>
{
    public string KitNumber { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string LotNumber { get; private set; } = string.Empty;
    public DateTimeOffset ExpirationDate { get; private set; }
    public string Status { get; private set; } = "ACTIVE_AT_SITE";
    public double? ResidualVolumeMl { get; private set; }
    public double? ResidualVolumePercentage { get; private set; }
    public string? DispositionReason { get; private set; }
    public string? LastReconciledBatchId { get; private set; }
    public DateTimeOffset? LastReconciledAt { get; private set; }
    public string? LastReconciledBy { get; private set; }

    private IpKitInventoryItem() { } // EF Core

    public IpKitInventoryItem(
        Guid id,
        string kitNumber,
        string studyId,
        string siteId,
        string lotNumber,
        DateTimeOffset expirationDate,
        string status = "ACTIVE_AT_SITE",
        double? residualVolumeMl = null,
        double? residualVolumePercentage = null,
        string createdBy = "svc:execution",
        string reasonForChange = "IP Kit registered in inventory")
        : base(id)
    {
        KitNumber = kitNumber;
        StudyId = studyId;
        SiteId = siteId;
        LotNumber = lotNumber;
        ExpirationDate = expirationDate;
        Status = status;
        ResidualVolumeMl = residualVolumeMl;
        ResidualVolumePercentage = residualVolumePercentage;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public void ReconcileStatus(
        string newStatus,
        string? dispositionReason,
        double? residualVolumeMl,
        double? residualVolumePercentage,
        string batchId,
        string reconciledBy,
        DateTimeOffset reconciledAt,
        string reasonForChange)
    {
        Status = newStatus;
        DispositionReason = dispositionReason;
        ResidualVolumeMl = residualVolumeMl ?? ResidualVolumeMl;
        ResidualVolumePercentage = residualVolumePercentage ?? ResidualVolumePercentage;
        LastReconciledBatchId = batchId;
        LastReconciledAt = reconciledAt;
        LastReconciledBy = reconciledBy;
        CreatedBy = reconciledBy;
        ReasonForChange = reasonForChange;
    }
}
