using System;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cgt;

public sealed class CgtDestructionCertificate : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string CertificateNumber { get; private set; } = string.Empty;
    public Guid? ChainOfCustodyId { get; private set; }
    public string? SubjectId { get; private set; }
    public string? Din { get; private set; }
    public string LotNumber { get; private set; } = string.Empty;
    public string KitNumbersJson { get; private set; } = "[]";
    public string VialIdentifiersJson { get; private set; } = "[]";
    public int QuantityDestroyed { get; private set; }
    public string UnitOfMeasure { get; private set; } = "VIALS";
    public double? ResidualVolumeMl { get; private set; }
    public string DestructionReason { get; private set; } = string.Empty;
    public string DestructionMethod { get; private set; } = string.Empty;
    public string DestructionFacility { get; private set; } = string.Empty;
    public DateTimeOffset DestructionTimestamp { get; private set; }
    public string Witness1Id { get; private set; } = string.Empty;
    public string? Witness1Name { get; private set; }
    public string? Witness1Role { get; private set; }
    public string Witness1SignatureToken { get; private set; } = string.Empty;
    public string Witness2Id { get; private set; } = string.Empty;
    public string? Witness2Name { get; private set; }
    public string? Witness2Role { get; private set; }
    public string Witness2SignatureToken { get; private set; } = string.Empty;
    public string CryptographicSeal { get; private set; } = string.Empty;
    public string Status { get; private set; } = "COMPLETED";
    public string? ReconciliationNotes { get; private set; }

    private CgtDestructionCertificate() { } // EF Core

    public CgtDestructionCertificate(
        Guid id,
        string studyId,
        string siteId,
        string certificateNumber,
        Guid? chainOfCustodyId,
        string? subjectId,
        string? din,
        string lotNumber,
        List<string> kitNumbers,
        List<string> vialIdentifiers,
        int quantityDestroyed,
        double? residualVolumeMl,
        string destructionReason,
        string destructionMethod,
        string destructionFacility,
        DateTimeOffset destructionTimestamp,
        string witness1Id,
        string? witness1Name,
        string? witness1Role,
        string witness1SignatureToken,
        string witness2Id,
        string? witness2Name,
        string? witness2Role,
        string witness2SignatureToken,
        string cryptographicSeal,
        string status = "COMPLETED",
        string? reconciliationNotes = null,
        string createdBy = "svc:execution",
        string reasonForChange = "Certificate of Destruction executed")
        : base(id)
    {
        StudyId = studyId;
        SiteId = siteId;
        CertificateNumber = certificateNumber;
        ChainOfCustodyId = chainOfCustodyId;
        SubjectId = subjectId;
        Din = din;
        LotNumber = lotNumber;
        KitNumbersJson = JsonSerializer.Serialize(kitNumbers ?? new());
        VialIdentifiersJson = JsonSerializer.Serialize(vialIdentifiers ?? new());
        QuantityDestroyed = quantityDestroyed;
        ResidualVolumeMl = residualVolumeMl;
        DestructionReason = destructionReason;
        DestructionMethod = destructionMethod;
        DestructionFacility = destructionFacility;
        DestructionTimestamp = destructionTimestamp;
        Witness1Id = witness1Id;
        Witness1Name = witness1Name;
        Witness1Role = witness1Role;
        Witness1SignatureToken = witness1SignatureToken;
        Witness2Id = witness2Id;
        Witness2Name = witness2Name;
        Witness2Role = witness2Role;
        Witness2SignatureToken = witness2SignatureToken;
        CryptographicSeal = cryptographicSeal;
        Status = status;
        ReconciliationNotes = reconciliationNotes;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public List<string> GetKitNumbers() =>
        JsonSerializer.Deserialize<List<string>>(KitNumbersJson) ?? new();

    public List<string> GetVialIdentifiers() =>
        JsonSerializer.Deserialize<List<string>>(VialIdentifiersJson) ?? new();
}
