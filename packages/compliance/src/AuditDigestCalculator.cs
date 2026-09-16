using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Compliance;

public static class AuditDigestCalculator
{
    public const string DefaultAuditSecretKey = "default-cadence-audit-secret-key";

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
        string? secretKey = null)
    {
        var key = !string.IsNullOrWhiteSpace(secretKey)
            ? secretKey.Trim()
            : (Environment.GetEnvironmentVariable("AUDIT_LOG_SECRET_KEY")?.Trim() ?? DefaultAuditSecretKey);

        var canonicalPayload = $"{eventId}|{serviceName}|{actionType}|{entityName}|{entityId}|{userId}|{tenantId}|{reasonForChange}|{timestamp}|{previousDigest}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload));
        return Convert.ToHexStringLower(hashBytes);
    }
}
