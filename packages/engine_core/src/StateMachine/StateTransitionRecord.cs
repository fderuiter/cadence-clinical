using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Core.Domain;

/// <summary>
/// Immutable 21 CFR Part 11 compliant transition history audit record with SHA-256 Merkle hash chaining.
/// </summary>
public sealed record StateTransitionRecord(
    string Id,
    string FromState,
    string ToState,
    string Trigger,
    DateTimeOffset Timestamp,
    string UserId,
    string? UserRole,
    string ReasonForChange,
    string? PredecessorHash,
    string Sha256Hash)
{
    /// <summary>
    /// Computes monotonic cryptographic digest of the transition record and links it to the previous hash.
    /// </summary>
    public static StateTransitionRecord Create(
        string fromState,
        string toState,
        string trigger,
        DateTimeOffset timestamp,
        string userId,
        string? userRole,
        string reasonForChange,
        string? predecessorHash)
    {
        string id = Guid.NewGuid().ToString("N");
        string raw = $"{id}|{fromState}|{toState}|{trigger}|{timestamp:O}|{userId}|{userRole}|{reasonForChange}|{predecessorHash ?? string.Empty}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        string sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return new StateTransitionRecord(
            id,
            fromState,
            toState,
            trigger,
            timestamp,
            userId,
            userRole,
            reasonForChange,
            predecessorHash,
            sha256);
    }
}
