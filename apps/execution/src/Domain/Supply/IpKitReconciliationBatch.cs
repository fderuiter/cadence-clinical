using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Supply;

public sealed class IpKitReconciliationBatch : AggregateRoot<Guid>
{
    public string BatchNumber { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string ProcessedBy { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }
    public int TotalKits { get; private set; }
    public int QuarantinedKits { get; private set; }
    public string CertificateId { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string ItemsJson { get; private set; } = "[]";

    private IpKitReconciliationBatch() { } // EF Core

    public IpKitReconciliationBatch(
        Guid id,
        string batchNumber,
        string studyId,
        string siteId,
        string processedBy,
        DateTimeOffset processedAt,
        int totalKits,
        int quarantinedKits,
        string certificateId,
        string itemsJson,
        string? notes = null,
        string createdBy = "svc:execution",
        string reasonForChange = "IP kit batch reconciliation committed")
        : base(id)
    {
        BatchNumber = batchNumber;
        StudyId = studyId;
        SiteId = siteId;
        ProcessedBy = processedBy;
        ProcessedAt = processedAt;
        TotalKits = totalKits;
        QuarantinedKits = quarantinedKits;
        CertificateId = certificateId;
        ItemsJson = itemsJson;
        Notes = notes;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}
