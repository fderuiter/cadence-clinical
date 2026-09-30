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
    /// <summary>When the event was logged, from the writer's clock (UTC). There is no default (K-TIME-3).</summary>
    public required DateTime Timestamp { get; init; }
    public string PreviousDigest { get; init; } = GENESIS_BLOCK;
    public string Sha256Digest { get; init; } = string.Empty;
}
