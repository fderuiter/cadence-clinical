using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Proposes a change to a study (spec §11, ADR-2489). A proposal records what it would achieve, the artifact version
/// holding the plan, the objects it would change or depends on, and the capabilities a person would use to execute it.
/// It has no effect on kernel state: approving it does not run its plan (K-SIM-5). An automation agent names the model
/// that produced it (K-PROV-3); any other actor names none.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="ProposalId">The new proposal's id.</param>
/// <param name="StudyId">The study the proposal is about; it is authorized against this study.</param>
/// <param name="Intent">What the proposal would achieve, 1 to <see cref="MaxIntentLength"/> characters.</param>
/// <param name="PlanArtifactVersionId">The artifact version that holds the plan, of the same study or tenant-wide.</param>
/// <param name="Affected">The objects it would change or depends on, 0 to <see cref="MaxAffected"/> distinct references.</param>
/// <param name="SimulatedImpact">The result hash of a simulation over it (ADR-2480), or null.</param>
/// <param name="RequestedCapabilities">
/// 1 to <see cref="MaxRequestedCapabilities"/> distinct capability tokens a person would use to execute it, each an
/// Execute or Approve command of the kernel command catalogue.
/// </param>
/// <param name="ExpiresAt">After this it can no longer be approved: after it is proposed and at most <see cref="MaxLifetime"/> later.</param>
/// <param name="ModelId">The model that produced it: required when an automation agent proposes, null otherwise.</param>
/// <param name="IdempotencyKey">The client's idempotency key.</param>
/// <param name="ReasonForChange">Why it is proposed.</param>
[KernelCapability("proposal.create", VerbClass.Propose, Resource = nameof(StudyId))]
public sealed record Propose(
    TenantId TenantId,
    ProposalId ProposalId,
    StudyId StudyId,
    string Intent,
    ArtifactVersionId PlanArtifactVersionId,
    IReadOnlyList<NodeRef> Affected,
    string? SimulatedImpact,
    IReadOnlyList<string> RequestedCapabilities,
    DateTimeOffset ExpiresAt,
    string? ModelId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProposalState>
{
    /// <summary>The longest intent.</summary>
    public const int MaxIntentLength = 2000;

    /// <summary>The most objects a proposal may name as affected.</summary>
    public const int MaxAffected = 200;

    /// <summary>The most capabilities a proposal may request.</summary>
    public const int MaxRequestedCapabilities = 20;

    /// <summary>The longest model identifier.</summary>
    public const int MaxModelIdLength = 200;

    /// <summary>How long after it is proposed a proposal may expire at the latest.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(90);
}

/// <summary>
/// Approves an open, unexpired proposal (ADR-2489 §4.2). <c>AttestationId</c> names an <c>Approved</c> attestation by
/// the acting person over the proposal's content hash (21 CFR 11.70). The approver may not be the proposer. Approval
/// does not execute the proposal (K-SIM-5).
/// </summary>
[KernelCapability("proposal.approve", VerbClass.Approve, Resource = nameof(ProposalId))]
public sealed record ApproveProposal(
    TenantId TenantId,
    ProposalId ProposalId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProposalState>;

/// <summary>Rejects an open, unexpired proposal; the reason for change is the rejection reason (ADR-2489 §4.2).</summary>
[KernelCapability("proposal.reject", VerbClass.Approve, Resource = nameof(ProposalId))]
public sealed record RejectProposal(
    TenantId TenantId,
    ProposalId ProposalId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProposalState>;

/// <summary>Withdraws an open, unexpired proposal. Only its proposer may (ADR-2489 §4.2).</summary>
[KernelCapability("proposal.withdraw", VerbClass.Propose, Resource = nameof(ProposalId))]
public sealed record WithdrawProposal(
    TenantId TenantId,
    ProposalId ProposalId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProposalState>;

/// <summary>
/// Where a proposal is in its lifecycle (ADR-2489 §4.2): <c>Open → Approved | Rejected | Withdrawn</c>. An open proposal
/// past its expiry reads as <see cref="Expired"/>; expiry is derived on read and never stored.
/// </summary>
public enum ProposalStatus
{
    /// <summary>Waiting for a decision.</summary>
    Open,

    /// <summary>A person approved it with an attestation over its content.</summary>
    Approved,

    /// <summary>A person rejected it.</summary>
    Rejected,

    /// <summary>Its proposer withdrew it.</summary>
    Withdrawn,

    /// <summary>It was still open when it expired. Derived on read only.</summary>
    Expired,
}

/// <summary>A proposal after a command, with its status as of the command's recorded time.</summary>
/// <param name="ProposalId">The proposal.</param>
/// <param name="StudyId">Its study.</param>
/// <param name="Status">Its status, with expiry derived.</param>
/// <param name="ContentHash">The hash an approval signs.</param>
/// <param name="ProposedBy">The proposing actor's id.</param>
/// <param name="ProposedByKind">The proposing actor's kind.</param>
/// <param name="ModelId">The model that produced it, for an automation agent.</param>
/// <param name="ExpiresAt">When it stops being approvable.</param>
/// <param name="DecidedAt">When it was approved, rejected or withdrawn.</param>
/// <param name="DecidedBy">Who approved, rejected or withdrew it.</param>
/// <param name="ApprovalAttestationId">The attestation an approval was signed with.</param>
public sealed record ProposalState(
    ProposalId ProposalId,
    StudyId StudyId,
    ProposalStatus Status,
    string ContentHash,
    string ProposedBy,
    ActorKind ProposedByKind,
    string? ModelId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    AttestationId? ApprovalAttestationId);

/// <summary>Errors raised by the proposal commands (ADR-2489).</summary>
public static class ProposalErrors
{
    /// <summary>The plan or an affected object is one the proposer may not see (K-AI-3); the error does not say which.</summary>
    public static readonly KernelError NotVisible =
        new("K.Proposal.NotVisible", 422, "A proposal names only objects its proposer may see.");

    public static readonly KernelError Invalid =
        new("K.Proposal.Invalid", 422, "The proposal is not valid.");

    public static readonly KernelError StudyUnknown =
        new("K.Proposal.StudyUnknown", 404, "The study does not exist.");

    public static readonly KernelError PlanInvalid =
        new("K.Proposal.PlanInvalid", 422, "The plan must be an artifact version of the proposal's study or of the tenant.");

    public static readonly KernelError CapabilityUnknown =
        new("K.Proposal.CapabilityUnknown", 422, "Each requested capability must be an Execute or Approve command of the kernel.");

    public static readonly KernelError ModelRequired =
        new("K.Proposal.ModelRequired", 422, "An automation agent names the model that produced its proposal (K-PROV-3).");

    public static readonly KernelError ModelNotAllowed =
        new("K.Proposal.ModelNotAllowed", 422, "Only an automation agent's proposal names a model.");

    public static readonly KernelError NotOpen =
        new("K.Proposal.NotOpen", 409, "The proposal has already been decided.");

    public static readonly KernelError Expired =
        new("K.Proposal.Expired", 409, "The proposal has expired.");

    public static readonly KernelError SelfApproval =
        new("K.Proposal.SelfApproval", 403, "The proposer cannot approve their own proposal.");

    public static readonly KernelError NotProposer =
        new("K.Proposal.NotProposer", 403, "Only the proposer can withdraw a proposal.");

    public static readonly KernelError AttestationRejected =
        new("K.Proposal.AttestationRejected", 422, "The attestation does not approve this proposal's content.");
}
