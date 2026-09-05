using System;

using FluentValidation;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Identifies the protocol version a consent record is executed against.
/// @req:PRD-SUB-001
/// </summary>
public sealed record ProtocolVersionPayload(string StudyId, string VersionTag, int VersionIndex);

/// <summary>
/// <c>POST /api/v1/execution/subjects/{id}/consent</c> request body.
/// Legacy Python source being replaced: <c>apps/execution/presentation/dtos.py</c> (Pydantic request models).
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectConsentRequest(
    ProtocolVersionPayload ProtocolVersion,
    bool IcfSigned,
    DateTimeOffset? IcfSignedDate,
    bool RequiresReconsent = false,
    bool IsPaperOverride = false);

/// <summary>
/// Per <c>02_technical_design.md</c> §2.7. The nested <see cref="ProtocolVersionPayload.StudyId"/>/
/// <see cref="ProtocolVersionPayload.VersionTag"/> rules are guarded by
/// <c>.When(x => x.ProtocolVersion is not null)</c> so a missing <see cref="SubjectConsentRequest.ProtocolVersion"/>
/// surfaces exactly one failure (this validator's own <c>NotNull()</c> rule) rather than a
/// <see cref="NullReferenceException"/> — FluentValidation evaluates a rule's <c>When</c> condition against the
/// root instance before invoking that rule's own property accessor, so the nested-property rules below are
/// never evaluated when <c>ProtocolVersion</c> is null.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectConsentRequestValidator : AbstractValidator<SubjectConsentRequest>
{
    public SubjectConsentRequestValidator()
    {
        RuleFor(x => x.ProtocolVersion).NotNull();
        RuleFor(x => x.ProtocolVersion.StudyId).NotEmpty().When(x => x.ProtocolVersion is not null);
        RuleFor(x => x.ProtocolVersion.VersionTag).NotEmpty().When(x => x.ProtocolVersion is not null);
    }
}
