namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// <c>POST /api/v1/execution/sites/{siteId}/activate</c> request body. Needs no dedicated
/// FluentValidation validator per <c>02_technical_design.md</c> §2.7: <see cref="StudyId"/> is its only
/// field, and ASP.NET Core's model binding already rejects a request body missing a required
/// non-nullable <c>string</c> property before any handler or validator would see it.
/// Legacy Python source being replaced: <c>apps/execution/presentation/dtos.py</c> (Pydantic request models).
/// @req:PRD-SUB-001
/// </summary>
public sealed record SiteActivationRequest(
    string StudyId,
    string? QaDirectorSigToken = null,
    string? MedicalMonitorSigToken = null,
    string? Justification = null,
    string? QaDirectorUserId = null,
    string? MedicalMonitorUserId = null,
    string? CountryCode = null);
