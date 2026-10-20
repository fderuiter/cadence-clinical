using FluentValidation;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Optional demographics sub-payload shared by <see cref="SubjectCreateRequest"/> and
/// <c>SubjectDemographicsUpdateRequest</c>. Fields stay free-form strings at this DTO layer — the legacy
/// Pydantic <c>DemographicsPayload</c> shape performed no field-level validation either; normalization
/// and encryption are downstream concerns (<c>IDemographicsNormalizationService</c>,
/// <c>IDemographicsEncryptionService</c>), not this ticket's responsibility.
/// @req:PRD-SUB-001
/// </summary>
public sealed record DemographicsPayload(string? Name, string? Birthdate, string? Gender, string? Race);

/// <summary>
/// <c>POST /api/v1/execution/subjects</c> request body. <see cref="SiteId"/> is legitimately optional at enrollment
/// time. There is no arm: a caller cannot choose one, because an arm comes only from randomisation (#5607).
/// Per <c>01_spec.md</c> §3's truthiness-trap table, an explicitly-empty <see cref="SiteId"/> string is a
/// <i>valid</i> value for this DTO's own shape rules (see <see cref="SubjectCreateRequestValidator"/>); the
/// empty-string-as-absent ambiguity is resolved downstream at the domain layer by <c>SiteCode</c>'s
/// constructor (<c>MIG-SUBJ-001</c>), which throws once a non-null <c>SiteCode</c> is actually constructed
/// from an empty/whitespace value.
/// Legacy Python source being replaced: <c>apps/execution/presentation/dtos.py</c> (Pydantic request models).
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectCreateRequest(
    string SubjectId,
    string StudyId,
    string? SiteId,
    DemographicsPayload? Demographics);

/// <summary>
/// Per <c>02_technical_design.md</c> §2.7. Deliberately does <b>not</b> apply a blanket
/// <c>NotEmpty()</c> to <see cref="SubjectCreateRequest.SiteId"/> — it is legitimately optional, so only
/// a non-null value is length-checked.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectCreateRequestValidator : AbstractValidator<SubjectCreateRequest>
{
    public SubjectCreateRequestValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty().MaximumLength(255);
        RuleFor(x => x.StudyId).NotEmpty().MaximumLength(255);
        RuleFor(x => x.SiteId).MaximumLength(255).When(x => x.SiteId is not null);
    }
}
