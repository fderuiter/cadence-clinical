using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Represents a single-use, 30-day BYOD activation token issued for a <see cref="ClinicalSubject"/>.
/// Raw activation codes and participant identity claims are never persisted; only keyed HMAC-SHA256
/// lookup digests computed via <c>ILookupHasher</c> are stored at rest (<c>K-IDN-1</c>).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectActivationToken
{
    public const int DefaultValidityDays = 30;
    public const string TokenHashPurpose = "execution.subject-activation-code.v1";
    public const string PrincipalHashPurpose = "execution.subject-activation-principal.v1";

    private SubjectActivationToken()
    {
    }

    public Guid Id { get; private set; }

    public SubjectCode SubjectCode { get; private set; }

    public StudyCode StudyCode { get; private set; }

    public SiteCode? SiteCode { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public string IssuedBy { get; private set; } = string.Empty;

    public string ReasonForChange { get; private set; } = string.Empty;

    public DateTimeOffset? RedeemedAt { get; private set; }

    public string? RedeemedByPrincipalHash { get; private set; }

    public bool IsRevoked { get; private set; }

    public static SubjectActivationToken Issue(
        SubjectCode subjectCode,
        StudyCode studyCode,
        SiteCode? siteCode,
        string tokenHash,
        DateTimeOffset issuedAt,
        string issuedBy,
        string reasonForChange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonForChange);

        return new SubjectActivationToken
        {
            Id = Guid.NewGuid(),
            SubjectCode = subjectCode,
            StudyCode = studyCode,
            SiteCode = siteCode,
            TokenHash = tokenHash,
            IssuedAt = issuedAt,
            ExpiresAt = issuedAt.AddDays(DefaultValidityDays),
            IssuedBy = issuedBy,
            ReasonForChange = reasonForChange,
            IsRevoked = false,
        };
    }

    public void Revoke() => IsRevoked = true;

    public Result<bool> Redeem(string redeemedByPrincipalHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redeemedByPrincipalHash);

        if (IsRevoked || RedeemedAt is not null || now > ExpiresAt)
        {
            return SubjectErrors.ActivationCodeInvalidOrExpired();
        }

        RedeemedAt = now;
        RedeemedByPrincipalHash = redeemedByPrincipalHash;
        return true;
    }
}
