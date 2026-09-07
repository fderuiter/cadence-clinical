using System.Collections.Generic;

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
