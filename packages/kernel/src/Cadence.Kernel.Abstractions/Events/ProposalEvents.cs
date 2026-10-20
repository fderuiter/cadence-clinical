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
    string ContentHash,
    double? ConfidenceScore = null,
    string? EscalationPolicy = null,
    bool IsEscalated = false,
    string? RequestedBy = null);

/// <summary>A proposal was escalated for human review (ADR-2489, Intelligence family).</summary>
[KernelEvent("ProposalEscalated", 1)]
public sealed record ProposalEscalated(
    TenantId TenantId,
    ProposalId ProposalId,
    StudyId StudyId,
    string EscalatedBy,
    ActorKind EscalatedByKind,
    string EscalateTo,
    string Reason,
    DateTimeOffset EscalatedAt,
    string? EscalationPolicy = null);

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

/// <summary>
/// A person executed a command under a proposal they approved (K-AI-2, ADR-2632, Intelligence family). The event's
/// activity is the executed command's provenance activity, so the chain reads proposal → approval attestation → activity
/// → the events that activity raised. One approved proposal may be executed by several commands until it expires.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="ProposalId">The executed proposal.</param>
/// <param name="StudyId">Its study.</param>
/// <param name="Command">The executed command type, for example <c>RaiseQuery</c>.</param>
/// <param name="Capability">The capability the command needed, one the proposal requested.</param>
/// <param name="Resource">The object the command was authorized against, as NodeRef text.</param>
/// <param name="ExecutedBy">The person who executed it: the proposal's approver.</param>
/// <param name="ApprovalAttestationId">The attestation the proposal was approved with.</param>
/// <param name="ProposedBy">The proposing actor's id.</param>
/// <param name="ProposedByKind">The proposing actor's kind.</param>
/// <param name="ModelId">The model that produced the proposal, for an automation agent (K-PROV-3).</param>
/// <param name="RequestedBy">The person the agent proposed for (K-AI-6), for an automation agent.</param>
[KernelEvent("ProposalExecuted", 1)]
public sealed record ProposalExecuted(
    TenantId TenantId,
    ProposalId ProposalId,
    StudyId StudyId,
    string Command,
    string Capability,
    string Resource,
    string ExecutedBy,
    AttestationId ApprovalAttestationId,
    string ProposedBy,
    ActorKind ProposedByKind,
    string? ModelId,
    string? RequestedBy);
