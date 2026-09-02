using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain.Audit;

/// <summary>
/// Immutable 21 CFR Part 11 regulatory audit trail entry with Merkle block chaining.
/// @req:PRD-SYS-053
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EntityName { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string UserId { get; init; } = string.Empty;
    public string ReasonForChange { get; init; } = string.Empty;
    public long VersionIndex { get; init; }
    public string? ChangesJson { get; init; }
    public string PredecessorHash { get; init; } = string.Empty;
    public string BlockHash { get; init; } = string.Empty;

    public AuditLogEntry() { }

    public static string ComputeBlockHash(
        string entityName,
        string entityId,
        string action,
        DateTime timestamp,
        string userId,
        long versionIndex,
        string? changesJson,
        string predecessorHash)
    {
        string rawBlock = $"{entityName}|{entityId}|{action}|{timestamp:O}|{userId}|{versionIndex}|{changesJson ?? ""}|{predecessorHash}";
        byte[] bytes = Encoding.UTF8.GetBytes(rawBlock);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
