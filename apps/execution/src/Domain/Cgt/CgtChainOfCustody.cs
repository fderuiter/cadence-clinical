using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cgt;

public sealed class CgtChainOfCustody : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string SubjectId { get; private set; } = string.Empty;
    public string Din { get; private set; } = string.Empty;
    public string? BatchId { get; private set; }
    public string? CollectionSiteId { get; private set; }
    public string? ManufacturingFacilityId { get; private set; }
    public string Status { get; private set; } = "HARVESTED";
    public string CurrentStage { get; private set; } = "APHERESIS";
    public int CryoTempExcursionCount { get; private set; }
    public double? LowestTempCelsius { get; private set; }
    public string? UnlockToken { get; private set; }
    public DateTimeOffset? HarvestedAt { get; private set; }
    public DateTimeOffset? InfusedAt { get; private set; }
    public DateTimeOffset? ReconciledAt { get; private set; }

    private CgtChainOfCustody() { } // EF Core

    public CgtChainOfCustody(
        Guid id,
        string studyId,
        string siteId,
        string subjectId,
        string din,
        string? collectionSiteId = null,
        DateTimeOffset? harvestedAt = null,
        string createdBy = "system",
        string reasonForChange = "Apheresis harvest initialized")
        : base(id)
    {
        StudyId = studyId;
        SiteId = siteId;
        SubjectId = subjectId;
        Din = din;
        CollectionSiteId = collectionSiteId ?? siteId;
        HarvestedAt = harvestedAt;
        Status = "HARVESTED";
        CurrentStage = "APHERESIS";
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public void BindManufacturingBatch(
        string batchId,
        string manufacturingFacilityId,
        string updatedBy,
        string reasonForChange)
    {
        BatchId = batchId;
        ManufacturingFacilityId = manufacturingFacilityId;
        Status = "MANUFACTURING_BOUND";
        CurrentStage = "MANUFACTURING";
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }

    public void RecordMilestone(
        string stage,
        string milestoneType,
        double? tempCelsius,
        bool isExcursion,
        string updatedBy,
        string reasonForChange)
    {
        CurrentStage = stage;
        if (tempCelsius.HasValue && (!LowestTempCelsius.HasValue || tempCelsius.Value < LowestTempCelsius.Value))
        {
            LowestTempCelsius = tempCelsius.Value;
        }

        if (isExcursion)
        {
            CryoTempExcursionCount++;
            Status = "QUARANTINED";
        }
        else if (Status == "QUARANTINED" && CryoTempExcursionCount == 0)
        {
            Status = "IN_TRANSIT";
        }
        else if (Status != "QUARANTINED" && Status != "BEDSIDE_VERIFIED" && Status != "INFUSED")
        {
            Status = "IN_TRANSIT";
        }

        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }

    public void SetBedsideVerified(
        string unlockToken,
        string updatedBy,
        string reasonForChange)
    {
        UnlockToken = unlockToken;
        Status = "BEDSIDE_VERIFIED";
        CurrentStage = "BEDSIDE";
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }

    public void SetInfused(
        DateTimeOffset infusedAt,
        string updatedBy,
        string reasonForChange)
    {
        InfusedAt = infusedAt;
        Status = "INFUSED";
        CurrentStage = "INFUSION";
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }

    public void SetDestroyed(
        DateTimeOffset destroyedAt,
        string updatedBy,
        string reasonForChange)
    {
        ReconciledAt = destroyedAt;
        Status = "DESTROYED";
        CurrentStage = "DESTRUCTION";
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }
}
