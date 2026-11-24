using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Audit;

/// <summary>
/// Bitemporal filter and page cap for on-demand object history reads (<c>GET /api/v1/kernel/nodes/{nodeRef}/history</c>).
/// </summary>
public sealed record NodeHistoryWindow(
    DateTimeOffset? ValidAt = null,
    DateTimeOffset? KnownAt = null,
    int Limit = 100);

/// <summary>
/// One structured property change extracted from an audit entry or clinical revision.
/// </summary>
public sealed record KernelHistoryFieldChange(
    string Field,
    string? Before,
    string? After);

/// <summary>
/// Electronic signature / attestation metadata bound to an audit event (21 CFR Part 11 §11.50 / §11.70).
/// </summary>
public sealed record KernelHistoryAttestation(
    NodeRef AttestationRef,
    string ActorId,
    NodeRef? ActorRef,
    string Meaning,
    string Method,
    DateTimeOffset AuthenticatedAt,
    DateTimeOffset RecordedAt,
    string TargetContentHash,
    string? Reason);

/// <summary>
/// One chronological history entry for a kernel object, or an in-place masked placeholder when the caller lacks scope or
/// <c>enrollment.unblind</c> (<c>K-AUTH-4</c>, ADR-2471).
/// </summary>
public sealed record KernelHistoryEntry(
    string EntryId,
    NodeRef? AuditEntryRef,
    string EventKind,
    string Action,
    string? CommandType,
    string? Outcome,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ValidAt,
    string? ActorId,
    string? ActorKind,
    NodeRef? ActorRef,
    string TargetKind,
    NodeRef? TargetRef,
    long? Version,
    string? ReasonForChange,
    string? Summary,
    IReadOnlyList<KernelHistoryFieldChange> FieldChanges,
    KernelHistoryAttestation? Attestation,
    long? Sequence,
    string? Hash,
    string? PredecessorHash,
    MaskReason? Mask = null);

/// <summary>
/// On-demand history timeline of one kernel object across audit entries, provenance activities, revisions, assertions,
/// and electronic signatures.
/// </summary>
public sealed record KernelNodeHistory(
    NodeRef Node,
    MaskReason? NodeMask,
    DateTimeOffset? ValidAt,
    DateTimeOffset? KnownAt,
    IReadOnlyList<KernelHistoryEntry> Entries);

/// <summary>
/// Single tamper-evident audit entry detail (<c>GET /api/v1/kernel/audit-entries/{auditRef}</c>), or an in-place masked
/// placeholder under <c>K-AUTH-4</c> / ADR-2471.
/// </summary>
public sealed record KernelAuditEntryDetail(
    NodeRef? AuditEntryRef,
    string TargetKind,
    NodeRef? TargetRef,
    string? Action,
    string? CommandType,
    string? Outcome,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ValidAt,
    string? ActorId,
    string? ActorKind,
    NodeRef? ActorRef,
    long? Version,
    string? ReasonForChange,
    string? Summary,
    IReadOnlyList<KernelHistoryFieldChange> FieldChanges,
    KernelHistoryAttestation? Attestation,
    Guid? ActivityId,
    Guid? CorrelationId,
    long? Sequence,
    string? Hash,
    string? PredecessorHash,
    MaskReason? Mask = null);

/// <summary>
/// Reads on-demand node history and individual audit entries directly from existing kernel audit, provenance,
/// lineage, revision, assertion, and attestation tables while enforcing <see cref="INodeVisibility"/> (<c>K-AUTH-4</c>).
/// </summary>
public interface IKernelHistoryReader
{
    Task<KernelNodeHistory?> ReadNodeHistoryAsync(
        TenantId tenant,
        Actor? reader,
        NodeRef node,
        NodeHistoryWindow window,
        CancellationToken cancellationToken = default);

    Task<KernelAuditEntryDetail?> FindAuditEntryAsync(
        TenantId tenant,
        Actor? reader,
        AuditEntryId auditEntryId,
        CancellationToken cancellationToken = default);
}
