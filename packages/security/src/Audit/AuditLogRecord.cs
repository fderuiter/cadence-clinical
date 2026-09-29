using System;

namespace Cadence.Security.Audit;

/// <summary>
/// Immutable audit log record schema containing cryptographic Merkle chain digest metadata.
/// @req:PRD-SYS-053
/// </summary>
public record AuditLogRecord : AuditLogPayload
{
    public const string GENESIS_BLOCK = "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000";

    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string PreviousDigest { get; init; } = GENESIS_BLOCK;
    public string Sha256Digest { get; init; } = string.Empty;
}
