using System;
using System.Collections.Generic;

namespace Cadence.Compliance;

public record ComplianceAuditLogRecord(
    string RecordId,
    string Action,
    string UserId,
    string ChangeReason,
    string? Id = null,
    string? EventId = null,
    string ServiceName = "cadence-service",
    string TableName = "audited_entity",
    string? UserRole = null,
    string TenantId = "tenant_default",
    string? IpAddress = null,
    DateTimeOffset? Timestamp = null,
    IReadOnlyDictionary<string, object?>? OldValues = null,
    IReadOnlyDictionary<string, object?>? NewValues = null,
    IReadOnlyDictionary<string, object?>? Details = null,
    int VersionIndex = 1,
    string PreviousDigest = "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000",
    string? Sha256Digest = null,
    string? CryptographicSeal = null,
    IReadOnlyDictionary<string, object?>? AiMetadata = null
)
{
    public string Id { get; init; } = Id ?? Guid.NewGuid().ToString();
    public string EventId { get; init; } = EventId ?? Guid.NewGuid().ToString();
    public DateTimeOffset Timestamp { get; init; } = Timestamp ?? DateTimeOffset.UtcNow;
}
