namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// Site activation response payload containing the activation status and signed HMAC GreenlightToken.
/// @req:PRD-SSU-001, PRD-SSU-003
/// </summary>
public sealed record SiteActivationResponse(
    string SiteId,
    string StudyId,
    bool Activated,
    string GreenlightToken,
    bool IsOverridden = false);
