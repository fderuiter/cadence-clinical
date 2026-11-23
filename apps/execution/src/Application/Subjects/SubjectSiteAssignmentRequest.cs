using System.Text.Json.Serialization;

using FluentValidation;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// <c>PATCH /api/v1/execution/subjects/{subjectId}/site</c> request body for assigning a clinical site to a
/// siteless participant and synchronizing kernel screening enrollment.
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectSiteAssignmentRequest([property: JsonPropertyName("site_id")] string? SiteCode);

/// <summary>
/// Validates <see cref="SubjectSiteAssignmentRequest"/> to ensure a non-whitespace <c>site_id</c> within domain
/// length bounds is supplied before handler execution.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectSiteAssignmentRequestValidator : AbstractValidator<SubjectSiteAssignmentRequest>
{
    public SubjectSiteAssignmentRequestValidator()
    {
        RuleFor(x => x.SiteCode)
            .NotEmpty()
            .MaximumLength(255)
            .OverridePropertyName("site_id");
    }
}
