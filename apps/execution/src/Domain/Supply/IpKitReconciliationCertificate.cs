using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Supply;

public sealed class IpKitReconciliationCertificate : AggregateRoot<Guid>
{
    public string CertificateId { get; private set; } = string.Empty;
    public Guid BatchId { get; private set; }
    public string BatchNumber { get; private set; } = string.Empty;
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string IssuedBy { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public string KitNumbersJson { get; private set; } = "[]";
    public string SummaryJson { get; private set; } = "{}";
    public string DigitalSignature { get; private set; } = string.Empty;

    private IpKitReconciliationCertificate() { } // EF Core

    public IpKitReconciliationCertificate(
        Guid id,
        string certificateId,
        Guid batchId,
        string batchNumber,
        string studyId,
        string siteId,
        string issuedBy,
        DateTimeOffset issuedAt,
        string kitNumbersJson,
        string summaryJson,
        string digitalSignature,
        string createdBy = "svc:execution",
        string reasonForChange = "Digital IP kit reconciliation certificate issued")
        : base(id)
    {
        CertificateId = certificateId;
        BatchId = batchId;
        BatchNumber = batchNumber;
        StudyId = studyId;
        SiteId = siteId;
        IssuedBy = issuedBy;
        IssuedAt = issuedAt;
        KitNumbersJson = kitNumbersJson;
        SummaryJson = summaryJson;
        DigitalSignature = digitalSignature;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}
