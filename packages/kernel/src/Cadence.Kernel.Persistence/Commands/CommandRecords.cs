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
    public string Type { get; init; } = string.Empty;
    public string Envelope { get; init; } = "{}";
    public DateTimeOffset EnqueuedAt { get; init; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public int Attempts { get; set; }

    /// <summary>Earliest time the relay retries a failed delivery; null until the first failure.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>The last delivery error, truncated, for operators.</summary>
    public string? LastError { get; set; }
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
