using Cadence.Kernel.Commands;
using Cadence.Kernel.Protocol;

namespace Cadence.Kernel.Events;

/// <summary>A protocol draft was created, as a study's first version or as a fork (spec §6.2, Knowledge family).</summary>
[KernelEvent("ProtocolVersionDrafted", 1)]
public sealed record ProtocolVersionDrafted(
    TenantId TenantId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    int Number,
    ProtocolVersionId? ParentVersionId,
    ProtocolAmendment? Amendment);

/// <summary>A protocol draft's content or amendment was replaced.</summary>
[KernelEvent("ProtocolVersionRevised", 1)]
public sealed record ProtocolVersionRevised(TenantId TenantId, StudyId StudyId, ProtocolVersionId ProtocolVersionId);

/// <summary>A protocol version was frozen for review with this content hash.</summary>
[KernelEvent("ProtocolVersionSubmittedForReview", 1)]
public sealed record ProtocolVersionSubmittedForReview(
    TenantId TenantId, StudyId StudyId, ProtocolVersionId ProtocolVersionId, string ContentHash);

/// <summary>A protocol version in review was returned to draft.</summary>
[KernelEvent("ProtocolVersionReturnedToDraft", 1)]
public sealed record ProtocolVersionReturnedToDraft(TenantId TenantId, StudyId StudyId, ProtocolVersionId ProtocolVersionId);

/// <summary>A protocol version was approved by the named attestation.</summary>
[KernelEvent("ProtocolVersionApproved", 1)]
public sealed record ProtocolVersionApproved(
    TenantId TenantId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    int Number,
    string ContentHash,
    AttestationId AttestationId);

/// <summary>A protocol version was withdrawn by the named attestation.</summary>
[KernelEvent("ProtocolVersionWithdrawn", 1)]
public sealed record ProtocolVersionWithdrawn(
    TenantId TenantId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    AttestationId AttestationId,
    string Reason);
