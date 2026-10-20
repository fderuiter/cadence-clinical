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

    /// <summary>The kit's treatment arm, encrypted for <see cref="ArmPurpose"/> (#5607); null for a kit no scheme can assign.</summary>
    public string? TreatmentArmCiphertext { get; private set; }

    /// <summary>The randomisation that assigned this kit, once assigned.</summary>
    public Guid? AssignedRandomizationId { get; private set; }
    public string? AssignedSubjectCode { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }

    /// <summary>The status of an unassigned kit at its site.</summary>
    public const string ActiveAtSiteStatus = "ACTIVE_AT_SITE";

    /// <summary>The status of a kit assigned to a randomised participant.</summary>
    public const string AssignedStatus = "ASSIGNED";

    /// <summary>The encryption purpose of a kit's arm, bound to the study and kit number.</summary>
    public static string ArmPurpose(string studyId, string kitNumber) => $"execution.ip_kit.arm:{studyId}:{kitNumber}";

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

    /// <summary>Sets the kit's encrypted treatment arm when it is registered.</summary>
    public void LabelTreatment(string armCiphertext) => TreatmentArmCiphertext = armCiphertext;

    /// <summary>Whether the kit can be assigned at randomisation: unassigned at its site, labelled, and unexpired at <paramref name="at"/>.</summary>
    public bool IsAssignable(DateTimeOffset at) =>
        Status == ActiveAtSiteStatus && AssignedRandomizationId is null && TreatmentArmCiphertext is not null && ExpirationDate > at;

    /// <summary>Assigns the kit to a randomised participant (#5607).</summary>
    public void AssignTo(Guid randomizationId, string subjectCode, DateTimeOffset assignedAt, string reasonForChange)
    {
        if (!IsAssignable(assignedAt))
            throw new InvalidOperationException($"Kit {KitNumber} is not assignable.");
        Status = AssignedStatus;
        AssignedRandomizationId = randomizationId;
        AssignedSubjectCode = subjectCode;
        AssignedAt = assignedAt;
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
