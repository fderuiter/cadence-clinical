using System;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>
/// One executed (or denied) command: who, when, why, on which aggregate (K-PROV-1). Insert-only.
/// </summary>
internal sealed class ProvenanceActivityRecord
{
    public Guid Id { get; init; }
    public TenantId TenantId { get; init; }
    public string CommandType { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public ActorKind ActorKind { get; init; }

    /// <summary>The person an automation agent acted for (K-AI-6); null for any other actor.</summary>
    public string? RequestedBy { get; init; }

    public DateTimeOffset RecordedAt { get; init; }
    public string ReasonForChange { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string? AggregateType { get; init; }
    public string? AggregateId { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }

    /// <summary>The authorization decision as JSON: its reason and every condition with its evidence (K-AUTH-2).</summary>
    public string? Authorization { get; init; }

    /// <summary>The approved proposal this command executed (K-AI-2), or null.</summary>
    public Guid? ProposalId { get; init; }
}

/// <summary>A raised domain event in its §5.2 envelope. Insert-only; the outbox carries it to subscribers.</summary>
internal sealed class DomainEventRecord
{
    public Guid EventId { get; init; }
    public TenantId TenantId { get; init; }
    public string Type { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public DateTimeOffset? OccurredValid { get; init; }
    public DateTimeOffset Recorded { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public Guid ActivityId { get; init; }
    public Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string Payload { get; init; } = "{}";
}

/// <summary>
/// One link of a tenant's audit hash chain (spec §5.3). <see cref="Hash"/> covers every other field plus
/// <see cref="PredecessorHash"/>; <see cref="Sequence"/> is gap-free per tenant. Insert-only.
/// </summary>
internal sealed class AuditEntryRecord
{
    public Guid Id { get; init; }
    public TenantId TenantId { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string? AggregateType { get; init; }
    public string? AggregateId { get; init; }
    public long? AggregateVersion { get; init; }
    public string ReasonForChange { get; init; } = string.Empty;
    public Guid ActivityId { get; init; }
    public string Changes { get; init; } = "{}";
    public string PredecessorHash { get; init; } = string.Empty;

    /// <summary>Set once, from <see cref="KernelAuditHash.Compute"/>, before the row is inserted.</summary>
    public string Hash { get; set; } = string.Empty;
}

/// <summary>The head of a tenant's audit chain, locked <c>FOR UPDATE</c> while a link is appended.</summary>
internal sealed class AuditChainHead
{
    public TenantId TenantId { get; init; }
    public long LastSequence { get; set; }
    public string LastHash { get; set; } = string.Empty;
}

/// <summary>An event waiting for the relay (at-least-once). Written in the command's transaction.</summary>
internal sealed class OutboxMessageRecord
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public TenantId TenantId { get; init; }
    public string StreamKey { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Envelope { get; init; } = "{}";
    public DateTimeOffset EnqueuedAt { get; init; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? QuarantinedAt { get; set; }
    public int Attempts { get; set; }

    /// <summary>Earliest time the relay retries a failed delivery; null until the first failure.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>The last delivery error, truncated, for operators.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Dead-letter quarantine record for an outbox event that failed with a terminal poison exception or exhausted
/// <see cref="Outbox.KernelOutboxRelay.MaxAttempts"/>. Isolates failure to its aggregate <see cref="StreamKey"/>
/// so unrelated streams in the same tenant continue dispatching.
/// </summary>
internal sealed class OutboxQuarantineMessageRecord
{
    public Guid Id { get; init; }
    public Guid OutboxMessageId { get; init; }
    public Guid EventId { get; init; }
    public TenantId TenantId { get; init; }
    public string StreamKey { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Envelope { get; init; } = "{}";
    public int Attempts { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? LastError { get; init; }
    public DateTimeOffset EnqueuedAt { get; init; }
    public DateTimeOffset QuarantinedAt { get; init; }
}

/// <summary>
/// Persisted asymmetrically signed study <c>DatabaseLockCertificate</c> minted atomically when a study-level
/// <c>Locked</c> assertion commits, and marked revoked in the same transaction when <c>ReleaseLock</c> commits.
/// </summary>
internal sealed class LockCertificateEntityRecord
{
    public Guid Id { get; init; }
    public string CertificateId { get; init; } = string.Empty;
    public TenantId TenantId { get; init; }
    public StudyId StudyId { get; init; }
    public string StudyCode { get; init; } = string.Empty;
    public AssertionId LockId { get; init; }
    public string Milestone { get; init; } = string.Empty;
    public AttestationId AttestationId { get; init; }
    public string SignedBy { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; }
    public string KeyId { get; init; } = string.Empty;
    public string Algorithm { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public string FingerprintSha256 { get; init; } = string.Empty;
    public string SignatureBase64 { get; init; } = string.Empty;
    public string CertificateJson { get; set; } = "{}";
    public bool Revoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}

/// <summary>
/// A seal over a tenant's audit chain (spec §5.3 "periodic root sealing"): the chain was verified intact up to
/// <see cref="Sequence"/>, whose hash was <see cref="Hash"/>. A later verification that finds the chain shorter, or a
/// different hash at a sealed sequence, proves tampering after the seal. Insert-only.
/// </summary>
internal sealed class AuditChainSeal
{
    public Guid Id { get; init; }
    public TenantId TenantId { get; init; }
    public long Sequence { get; init; }
    public string Hash { get; init; } = string.Empty;
    public DateTimeOffset SealedAt { get; init; }
    public string SealedBy { get; init; } = string.Empty;
}

/// <summary>The stored outcome of a command, keyed by tenant and idempotency key (K-TX-3). Insert-only.</summary>
internal sealed class IdempotencyRecord
{
    public TenantId TenantId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string CommandType { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string ResultJson { get; init; } = "null";
    public Guid ActivityId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// Immutable biostatistical export audit record persisted atomically alongside the Merkle <see cref="AuditEntryRecord"/>
/// for every study snapshot and participant casebook export request (PRD-CDR-006, K-PROV-1). Insert-only.
/// </summary>
internal sealed class BiostatExportAuditLog
{
    public Guid Id { get; init; }
    public TenantId TenantId { get; init; }
    public StudyId StudyId { get; init; }
    public EnrollmentId? EnrollmentId { get; init; }
    public ArtifactId ArtifactId { get; init; }
    public Guid ActivityId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string? LockFilter { get; init; }
    public DateTimeOffset ValidAt { get; init; }
    public DateTimeOffset KnownAt { get; init; }
    public bool IsUnblinded { get; init; }
    public string DetailsJson { get; init; } = "{}";
    public string Sha256Hash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
