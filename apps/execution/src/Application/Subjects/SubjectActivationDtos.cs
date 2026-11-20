using System;

using Cadence.Apps.Execution.Domain.Subjects;

using FluentValidation;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Wire payload returned once when a site investigator or coordinator issues a 30-day BYOD activation code.
/// The raw <paramref name="ActivationCode"/> is never persisted at rest.
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectActivationCodeIssueResponse(
    SubjectCode SubjectId,
    StudyCode StudyId,
    SiteCode? SiteId,
    string ActivationCode,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int ExpiresInDays);

/// <summary>
/// Wire request submitted by an authenticated participant to redeem a 30-day BYOD activation code.
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectActivationCodeRedeemRequest(string ActivationCode);

/// <summary>
/// Wire response confirming redemption or bound-account resolution of a participant's <see cref="SubjectCode"/>.
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectActivationCodeRedeemResponse(
    SubjectCode SubjectId,
    StudyCode StudyId,
    SiteCode? SiteId,
    DateTimeOffset RedeemedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Validates <see cref="SubjectActivationCodeRedeemRequest"/> before redemption executes.
/// </summary>
public sealed class SubjectActivationCodeRedeemRequestValidator : AbstractValidator<SubjectActivationCodeRedeemRequest>
{
    public SubjectActivationCodeRedeemRequestValidator()
    {
        RuleFor(x => x.ActivationCode)
            .NotEmpty()
            .MaximumLength(128);
    }
}
