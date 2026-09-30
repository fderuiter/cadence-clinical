namespace Cadence.Compliance;

/// <summary>
/// Compliance-package entry point for audit digests. Delegates to
/// <see cref="Cadence.Security.Audit.AuditDigestCalculator"/> so the canonical payload and the HMAC key
/// (<see cref="Cadence.Security.Audit.AuditSecretKey"/>, no hardcoded fallback) are identical for every
/// producer and verifier.
/// @req:PRD-SYS-053
/// </summary>
public static class AuditDigestCalculator
{
    public static string ComputeAuditDigest(
        string eventId,
        string serviceName,
        string actionType,
        string entityName,
        string entityId,
        string userId,
        string tenantId,
        string reasonForChange,
        string timestamp,
        string previousDigest,
        string? secretKey = null) =>
        Cadence.Security.Audit.AuditDigestCalculator.ComputeAuditDigest(
            eventId,
            serviceName,
            actionType,
            entityName,
            entityId,
            userId,
            tenantId,
            reasonForChange,
            timestamp,
            previousDigest,
            secretKey);
}
