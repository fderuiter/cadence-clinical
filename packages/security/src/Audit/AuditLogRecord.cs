using System;
using System.Diagnostics.CodeAnalysis;

namespace Cadence.Security.Audit;

/// <summary>
/// Immutable audit log record schema containing cryptographic Merkle chain digest metadata.
/// @req:PRD-SYS-053
/// </summary>
public record AuditLogRecord : AuditLogPayload
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
        Justification = "Public UPPER_SNAKE names ported from the Python reference (packages/security/rbac.py) and referenced across services; renaming them is a separate public API change.")]
    public const string GENESIS_BLOCK = "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000";

    public string EventId { get; init; } = Guid.NewGuid().ToString();
    /// <summary>When the event was logged, from the writer's clock (UTC). There is no default (K-TIME-3).</summary>
    public required DateTime Timestamp { get; init; }
    public string PreviousDigest { get; init; } = GENESIS_BLOCK;
    public string Sha256Digest { get; init; } = string.Empty;
}
