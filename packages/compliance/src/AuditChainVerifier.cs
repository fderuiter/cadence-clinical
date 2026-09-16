using System;
using System.Collections.Generic;

namespace Cadence.Compliance;

public record AuditChainVerificationResult(
    bool Valid,
    string Status,
    int TotalRecords,
    int VerifiedCount,
    int? BrokenAtIndex,
    string? Error
);

public static class AuditChainVerifier
{
    public static AuditChainVerificationResult VerifyAuditChain(
        IReadOnlyList<ComplianceAuditLogRecord> records,
        string? secretKey = null)
    {
        if (records == null || records.Count == 0)
        {
            return new AuditChainVerificationResult(
                Valid: true,
                Status: "INTACT",
                TotalRecords: 0,
                VerifiedCount: 0,
                BrokenAtIndex: null,
                Error: null
            );
        }

        var expectedPrev = "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000";

        for (var idx = 0; idx < records.Count; idx++)
        {
            var rec = records[idx];
            var recPrev = rec.PreviousDigest;
            var recDigest = rec.Sha256Digest ?? rec.CryptographicSeal;

            if (!string.IsNullOrEmpty(recPrev) && recPrev != expectedPrev)
            {
                return new AuditChainVerificationResult(
                    Valid: false,
                    Status: "TAMPERED",
                    TotalRecords: records.Count,
                    VerifiedCount: idx,
                    BrokenAtIndex: idx,
                    Error: $"Previous digest mismatch at index {idx}: expected '{expectedPrev}', got '{recPrev}'"
                );
            }

            var eventId = rec.EventId ?? rec.Id ?? $"evt-{idx}";
            var serviceName = rec.ServiceName ?? "cadence-service";
            var action = rec.Action ?? "UPDATE";
            var entityName = rec.TableName ?? "entity";
            var entityId = rec.RecordId ?? $"rec-{idx}";
            var userId = rec.UserId ?? "system";
            var tenantId = rec.TenantId ?? "tenant_default";
            var reason = rec.ChangeReason ?? "";
            var tsStr = rec.Timestamp.ToString("o");

            var computed = AuditDigestCalculator.ComputeAuditDigest(
                eventId: eventId,
                serviceName: serviceName,
                actionType: action,
                entityName: entityName,
                entityId: entityId,
                userId: userId,
                tenantId: tenantId,
                reasonForChange: reason,
                timestamp: tsStr,
                previousDigest: recPrev ?? expectedPrev,
                secretKey: secretKey
            );

            if (!string.IsNullOrEmpty(recDigest) && !recDigest.Equals(computed, StringComparison.OrdinalIgnoreCase))
            {
                return new AuditChainVerificationResult(
                    Valid: false,
                    Status: "TAMPERED",
                    TotalRecords: records.Count,
                    VerifiedCount: idx,
                    BrokenAtIndex: idx,
                    Error: $"HMAC SHA-256 digest verification failed at index {idx}: record digest '{recDigest}' does not match computed digest '{computed}'"
                );
            }

            expectedPrev = recDigest ?? computed;
        }

        return new AuditChainVerificationResult(
            Valid: true,
            Status: "INTACT",
            TotalRecords: records.Count,
            VerifiedCount: records.Count,
            BrokenAtIndex: null,
            Error: null
        );
    }
}
