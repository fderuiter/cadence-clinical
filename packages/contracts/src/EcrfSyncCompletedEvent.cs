using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event emitted when eCRF synchronization and UAT simulation complete for a protocol version.
/// </summary>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="StudyId">The study identifier.</param>
/// <param name="ProtocolVersionId">The protocol version identifier.</param>
/// <param name="VersionTag">The semantic version tag.</param>
/// <param name="VersionIndex">The version index number.</param>
/// <param name="FormDefinitionId">The compiled form definition identifier.</param>
/// <param name="Sha256EvidenceDigest">The SHA-256 evidence digest digest for 21 CFR Part 11.</param>
/// <param name="SyncedAt">The timestamp when synchronization completed.</param>
public sealed record EcrfSyncCompletedEvent(
    string TenantId,
    string StudyId,
    string ProtocolVersionId,
    string VersionTag,
    int VersionIndex,
    string FormDefinitionId,
    string Sha256EvidenceDigest,
    DateTimeOffset SyncedAt
);
