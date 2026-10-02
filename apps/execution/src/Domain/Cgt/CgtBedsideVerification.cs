using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cgt;

public sealed class CgtBedsideVerification : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string SubjectId { get; private set; } = string.Empty;
    public string Din { get; private set; } = string.Empty;
    public string? BatchId { get; private set; }
    public string PatientWristbandId { get; private set; } = string.Empty;
    public string PrimaryVerifierId { get; private set; } = string.Empty;
    public string? PrimaryVerifierName { get; private set; }
    public string? PrimaryVerifierRole { get; private set; }
    public string PrimarySignatureToken { get; private set; } = string.Empty;
    public string SecondaryVerifierId { get; private set; } = string.Empty;
    public string? SecondaryVerifierName { get; private set; }
    public string? SecondaryVerifierRole { get; private set; }
    public string SecondarySignatureToken { get; private set; } = string.Empty;
    public string UnlockToken { get; private set; } = string.Empty;
    public DateTimeOffset VerifiedAt { get; private set; }
    public bool IsMatch { get; private set; } = true;
    public string? Notes { get; private set; }

    private CgtBedsideVerification() { } // EF Core

    public CgtBedsideVerification(
        Guid id,
        string studyId,
        string siteId,
        string subjectId,
        string din,
        string? batchId,
        string patientWristbandId,
        string primaryVerifierId,
        string? primaryVerifierName,
        string? primaryVerifierRole,
        string primarySignatureToken,
        string secondaryVerifierId,
        string? secondaryVerifierName,
        string? secondaryVerifierRole,
        string secondarySignatureToken,
        string unlockToken,
        DateTimeOffset verifiedAt,
        bool isMatch = true,
        string? notes = null,
        string createdBy = "system",
        string reasonForChange = "Bedside dual verification completed")
        : base(id)
    {
        StudyId = studyId;
        SiteId = siteId;
        SubjectId = subjectId;
        Din = din;
        BatchId = batchId;
        PatientWristbandId = patientWristbandId;
        PrimaryVerifierId = primaryVerifierId;
        PrimaryVerifierName = primaryVerifierName;
        PrimaryVerifierRole = primaryVerifierRole;
        PrimarySignatureToken = primarySignatureToken;
        SecondaryVerifierId = secondaryVerifierId;
        SecondaryVerifierName = secondaryVerifierName;
        SecondaryVerifierRole = secondaryVerifierRole;
        SecondarySignatureToken = secondarySignatureToken;
        UnlockToken = unlockToken;
        VerifiedAt = verifiedAt;
        IsMatch = isMatch;
        Notes = notes;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}
