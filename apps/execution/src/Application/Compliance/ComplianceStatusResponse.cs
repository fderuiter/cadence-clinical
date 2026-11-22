using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// Wire shape for <c>GET /api/v1/execution/sites/{site_id}/compliance-status</c> (`01_spec.md` §1.2).
/// Always returned with HTTP 200 — a missing <see cref="Domain.Compliance.SiteComplianceCache"/> row is
/// modeled as <c>IsComplete: false</c> / <c>MissingDocuments: []</c>, never a 404.
/// Legacy Python source being replaced: `apps/execution/presentation/dtos.py`'s
/// <c>ComplianceStatusResponse</c> Pydantic model.
/// @req:PRD-SUB-001
/// </summary>
public sealed record ComplianceStatusResponse(
    StudyCode StudyId,
    SiteCode? SiteId,
    string Milestone,
    bool IsComplete,
    IReadOnlyList<string> MissingDocuments,
    bool IsActivated = false,
    DateTimeOffset? ActivatedAt = null,
    string? ActivatedBy = null);
