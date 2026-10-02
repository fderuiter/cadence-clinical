using System;
using System.Collections.Generic;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A person or an automation agent proposed a change to a study (ADR-2489, Intelligence family).</summary>
[KernelEvent("ProposalCreated", 1)]
public sealed record ProposalCreated(
    TenantId TenantId,
    ProposalId ProposalId,
    StudyId StudyId,
    string ProposedBy,
    ActorKind ProposedByKind,
    string? ModelId,
    ArtifactVersionId PlanArtifactVersionId,
    IReadOnlyList<string> RequestedCapabilities,
    DateTimeOffset ExpiresAt,
    string ContentHash);

/// <summary>A person approved a proposal with an attestation over its content hash (ADR-2489, Intelligence family).</summary>
[KernelEvent("ProposalApproved", 1)]
public sealed record ProposalApproved(
    TenantId TenantId,
    ProposalId ProposalId,
    StudyId StudyId,
    string ContentHash,
    AttestationId AttestationId,
    string ApprovedBy);

/// <summary>A person rejected a proposal, for the reason given (ADR-2489, Intelligence family).</summary>
[KernelEvent("ProposalRejected", 1)]
public sealed record ProposalRejected(
    TenantId TenantId,
    ProposalId ProposalId,
    StudyId StudyId,
    string RejectedBy,
    string Reason);

/// <summary>A proposal's proposer withdrew it (ADR-2489, Intelligence family).</summary>
[KernelEvent("ProposalWithdrawn", 1)]
public sealed record ProposalWithdrawn(
    TenantId TenantId,
    ProposalId ProposalId,
    StudyId StudyId,
    string WithdrawnBy);
