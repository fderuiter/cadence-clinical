using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cgt;

public sealed class CgtVialReturnReconciliation : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string SubjectId { get; private set; } = string.Empty;
    public string KitNumber { get; private set; } = string.Empty;
    public string VialIdentifier { get; private set; } = string.Empty;
    public int ReturnedQuantity { get; private set; }
    public int IntactVials { get; private set; }
    public int UsedVials { get; private set; }
    public double? ResidualVolumeMl { get; private set; }
    public double AdherencePercentage { get; private set; }
    public string Disposition { get; private set; } = "QUARANTINED_FOR_DESTRUCTION";
    public string LoggedBy { get; private set; } = string.Empty;
    public DateTimeOffset LoggedAt { get; private set; }
    public string? CertificateId { get; private set; }
    public string? Notes { get; private set; }

    private CgtVialReturnReconciliation() { } // EF Core

    public CgtVialReturnReconciliation(
        Guid id,
        string studyId,
        string siteId,
        string subjectId,
        string kitNumber,
        string vialIdentifier,
        int returnedQuantity,
        int intactVials,
        int usedVials,
        double? residualVolumeMl,
        double adherencePercentage,
        string disposition,
        string loggedBy,
        DateTimeOffset loggedAt,
        string? certificateId = null,
        string? notes = null,
        string createdBy = "system",
        string reasonForChange = "Vial return reconciliation logged")
        : base(id)
    {
        StudyId = studyId;
        SiteId = siteId;
        SubjectId = subjectId;
        KitNumber = kitNumber;
        VialIdentifier = vialIdentifier;
        ReturnedQuantity = returnedQuantity;
        IntactVials = intactVials;
        UsedVials = usedVials;
        ResidualVolumeMl = residualVolumeMl;
        AdherencePercentage = adherencePercentage;
        Disposition = disposition;
        LoggedBy = loggedBy;
        LoggedAt = loggedAt;
        CertificateId = certificateId;
        Notes = notes;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public void MarkDestroyed(string certificateId, string updatedBy, string reasonForChange)
    {
        CertificateId = certificateId;
        Disposition = "DESTROYED";
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }
}
