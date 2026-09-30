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
    /// <summary>
    /// When the audited change was saved, in UTC (see <see cref="NormalizeTimestamp"/>). The writer supplies it from
    /// its injected clock; there is no wall-clock default (K-TIME-3).
    /// </summary>
    public required DateTime Timestamp { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string ReasonForChange { get; init; } = string.Empty;
    public long VersionIndex { get; init; }
    public string? ChangesJson { get; init; }
    public string PredecessorHash { get; init; } = string.Empty;
    public string BlockHash { get; init; } = string.Empty;

    public AuditLogEntry() { }

    /// <summary>
    /// The form an audit timestamp is hashed and stored in: UTC, truncated to whole microseconds. PostgreSQL
    /// keeps only microseconds, so hashing the full 100 ns tick value would make every read-back entry fail
    /// verification. A timestamp read back without a kind is UTC, since audit timestamps are always written in UTC.
    /// </summary>
    public static DateTime NormalizeTimestamp(DateTime timestamp)
    {
        DateTime utc = timestamp.Kind switch
        {
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
            _ => timestamp,
        };
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }

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

