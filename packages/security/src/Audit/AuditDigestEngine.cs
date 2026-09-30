using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Security.Audit;

/// <summary>
/// 21 CFR Part 11 Immutable Audit Logger and SHA-256 Merkle Digest Chain Engine.
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
        string? secretKey = null)
    {
        string key = AuditSecretKey.Resolve(secretKey);

        string canonicalPayload = $"{eventId}|{serviceName}|{actionType}|{entityName}|{entityId}|{userId}|{tenantId}|{reasonForChange}|{timestamp}|{previousDigest}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload));
        return Convert.ToHexStringLower(hash);
    }
}

public class AuditDigestEngine
{
    private readonly IAuditLogWriter _writer;
    private readonly string _secretKey;

    public AuditDigestEngine(IAuditLogWriter? writer = null, string? secretKey = null)
    {
        _writer = writer ?? new InMemoryAuditStore();
        _secretKey = AuditSecretKey.Resolve(secretKey);
    }

    public string GetLastDigest()
    {
        var lastRec = _writer.GetLastRecord();
        return lastRec?.Sha256Digest ?? AuditLogRecord.GENESIS_BLOCK;
    }

    public AuditLogRecord LogEvent(AuditLogPayload payload)
    {
        string eventId = Guid.NewGuid().ToString();
        DateTime timestamp = DateTime.UtcNow;
        string timestampStr = timestamp.ToString("o");
        string prevDigest = GetLastDigest();

        string digest = AuditDigestCalculator.ComputeAuditDigest(
            eventId: eventId,
            serviceName: payload.ServiceName,
            actionType: payload.ActionType,
            entityName: payload.EntityName,
            entityId: payload.EntityId,
            userId: payload.UserId,
            tenantId: payload.TenantId,
            reasonForChange: payload.ReasonForChange,
            timestamp: timestampStr,
            previousDigest: prevDigest,
            secretKey: _secretKey
        );

        var record = new AuditLogRecord
        {
            EventId = eventId,
            ServiceName = payload.ServiceName,
            ActionType = payload.ActionType,
            EntityName = payload.EntityName,
            EntityId = payload.EntityId,
            UserId = payload.UserId,
            TenantId = payload.TenantId,
            ReasonForChange = payload.ReasonForChange,
            Details = payload.Details,
            Timestamp = timestamp,
            PreviousDigest = prevDigest,
            Sha256Digest = digest
        };

        _writer.WriteRecord(record);
        return record;
    }

    public bool VerifyChainIntegrity()
    {
        string expectedPrev = AuditLogRecord.GENESIS_BLOCK;
        var records = _writer.FetchAllRecords();

        foreach (var record in records)
        {
            if (!string.Equals(record.PreviousDigest, expectedPrev, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string recalculated = AuditDigestCalculator.ComputeAuditDigest(
                eventId: record.EventId,
                serviceName: record.ServiceName,
                actionType: record.ActionType,
                entityName: record.EntityName,
                entityId: record.EntityId,
                userId: record.UserId,
                tenantId: record.TenantId,
                reasonForChange: record.ReasonForChange,
                timestamp: record.Timestamp.ToString("o"),
                previousDigest: record.PreviousDigest,
                secretKey: _secretKey
            );

            if (!string.Equals(record.Sha256Digest, recalculated, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            expectedPrev = record.Sha256Digest;
        }

        return true;
    }
}
