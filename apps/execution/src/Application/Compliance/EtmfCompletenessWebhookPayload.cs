using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Subjects;

using FluentValidation;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// eTMF-completeness webhook payload — Rule 2's compliance-gate data source (see
/// <c>SiteComplianceCache</c>, <c>MIG-SUBJ-007</c>). <see cref="SiteId"/> is nullable: study-level
/// milestones are a legitimate case and carry no site. <see cref="MissingArtifacts"/> is optional since a
/// fully-complete milestone need not enumerate any missing items.
/// Legacy Python source being replaced: <c>apps/execution/presentation/dtos.py</c> (Pydantic request models).
/// @req:PRD-SUB-001
/// </summary>
public sealed record EtmfCompletenessWebhookPayload(
    string StudyId,
    string? SiteId,
    string Milestone,
    bool IsComplete,
    IReadOnlyList<string>? MissingArtifacts);

/// <summary>
/// Per <c>02_technical_design.md</c> §2.7. <see cref="EtmfCompletenessWebhookPayload.StudyId"/> and
/// <see cref="EtmfCompletenessWebhookPayload.Milestone"/> are the only two required fields;
/// FluentValidation's <c>NotEmpty()</c> already treats a whitespace-only string as empty.
/// @req:PRD-SUB-001
/// </summary>
public sealed class EtmfCompletenessWebhookPayloadValidator : AbstractValidator<EtmfCompletenessWebhookPayload>
{
    public EtmfCompletenessWebhookPayloadValidator()
    {
        RuleFor(x => x.StudyId).NotEmpty();
        RuleFor(x => x.Milestone).NotEmpty();
    }
}

/// <summary>
/// Per-site completeness snapshot entry inside <see cref="EtmfCompletenessBatchWebhookPayload"/>.
/// @req:PRD-SUB-001
/// </summary>
public sealed record EtmfSiteCompletenessItem(
    SiteCode SiteId,
    bool IsComplete,
    IReadOnlyList<string>? MissingArtifacts);

/// <summary>
/// Study-wide batch eTMF-completeness webhook payload posted to
/// <c>POST /api/v1/execution/webhooks/etmf/batch</c> when a central study startup document changes readiness
/// across multiple sites.
/// @req:PRD-SUB-001
/// </summary>
public sealed record EtmfCompletenessBatchWebhookPayload(
    StudyCode StudyId,
    string Milestone,
    IReadOnlyList<EtmfSiteCompletenessItem> Sites);

/// <summary>
/// Validates <see cref="EtmfCompletenessBatchWebhookPayload"/> before scope and lock guards run.
/// @req:PRD-SUB-001
/// </summary>
public sealed class EtmfCompletenessBatchWebhookPayloadValidator : AbstractValidator<EtmfCompletenessBatchWebhookPayload>
{
    public EtmfCompletenessBatchWebhookPayloadValidator()
    {
        RuleFor(x => x.StudyId.Value).NotEmpty().MaximumLength(255).WithName("study_id");
        RuleFor(x => x.Milestone).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Sites).NotNull();
        RuleForEach(x => x.Sites).ChildRules(site =>
        {
            site.RuleFor(s => s.SiteId.Value).NotEmpty().MaximumLength(255).WithName("site_id");
        });
    }
}
