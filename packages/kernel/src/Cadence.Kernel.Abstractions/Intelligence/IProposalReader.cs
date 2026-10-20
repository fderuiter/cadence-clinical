using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Intelligence;

/// <summary>
/// Read-only access to a study's proposals (spec §11, ADR-2489) for hosts that show them, such as the Canvas review
/// screen and Needs Attention (ADR-2635). Reads run under the tenant's Row-Level Security, so another tenant's proposal
/// is never returned. The reader does not authorize the actor: the host masks what the caller may not see. Expiry is
/// derived at <c>now</c>, as the kernel derives it everywhere (an open proposal past its expiry reads as expired).
/// </summary>
public interface IProposalReader
{
    /// <summary>
    /// The study's proposals, newest first, at most <paramref name="limit"/>; only those with <paramref name="status"/>
    /// at <paramref name="now"/> when it is given.
    /// </summary>
    Task<IReadOnlyList<ProposalSummary>> ListForStudyAsync(
        TenantId tenant, StudyId study, ProposalStatus? status, DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>One proposal with its status at <paramref name="now"/>, or null when the tenant has no such proposal.</summary>
    Task<ProposalSummary?> GetAsync(TenantId tenant, ProposalId proposal, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>A proposal's content and decision, as a reviewer reads it.</summary>
/// <param name="Id">The proposal.</param>
/// <param name="StudyId">Its study.</param>
/// <param name="Intent">What it would achieve.</param>
/// <param name="Plan">The artifact version holding its plan and reasoning.</param>
/// <param name="Affected">The objects it would change or depends on, as NodeRef text, in the order proposed.</param>
/// <param name="SimulatedImpact">The result hash of the simulation over it, or null.</param>
/// <param name="RequestedCapabilities">The capabilities a person would use to execute it, in the order proposed.</param>
/// <param name="ProposedAt">When it was proposed.</param>
/// <param name="ExpiresAt">When it stops being approvable.</param>
/// <param name="ProposedById">The proposing actor's id.</param>
/// <param name="ProposedByKind">The proposing actor's kind.</param>
/// <param name="RequestedById">The person an automation agent proposed for (K-AI-6), or null.</param>
/// <param name="ModelId">The model that produced it, for an automation agent (K-PROV-3).</param>
/// <param name="ConfidenceScore">The confidence score the proposer gave, or null.</param>
/// <param name="ContentHash">The hash an approval signs (21 CFR 11.70).</param>
/// <param name="Status">Its status at the time of the read, with expiry derived.</param>
/// <param name="DecidedAt">When it was approved, rejected or withdrawn.</param>
/// <param name="DecidedById">Who approved, rejected or withdrew it.</param>
/// <param name="ApprovalAttestationId">The attestation an approval was signed with.</param>
/// <param name="DecisionReason">Why it was rejected or withdrawn.</param>
/// <param name="IsEscalated">True when it was escalated for review.</param>
/// <param name="EscalatedTo">Whom it was escalated to.</param>
/// <param name="EscalationReason">Why it was escalated.</param>
public sealed record ProposalSummary(
    ProposalId Id,
    StudyId StudyId,
    string Intent,
    ProposalPlan Plan,
    IReadOnlyList<string> Affected,
    string? SimulatedImpact,
    IReadOnlyList<string> RequestedCapabilities,
    DateTimeOffset ProposedAt,
    DateTimeOffset ExpiresAt,
    string ProposedById,
    ActorKind ProposedByKind,
    string? RequestedById,
    string? ModelId,
    double? ConfidenceScore,
    string ContentHash,
    ProposalStatus Status,
    DateTimeOffset? DecidedAt,
    string? DecidedById,
    AttestationId? ApprovalAttestationId,
    string? DecisionReason,
    bool IsEscalated,
    string? EscalatedTo,
    string? EscalationReason);

/// <summary>The artifact version that holds a proposal's plan; its bytes live in artifact storage, not in the kernel.</summary>
/// <param name="VersionId">The artifact version.</param>
/// <param name="ArtifactId">Its artifact.</param>
/// <param name="Title">The artifact's title.</param>
/// <param name="Number">The version's number in its artifact.</param>
/// <param name="MediaType">The media type of its bytes.</param>
/// <param name="ContentHash">The hash of its bytes.</param>
public sealed record ProposalPlan(
    ArtifactVersionId VersionId,
    ArtifactId ArtifactId,
    string Title,
    int Number,
    string MediaType,
    string ContentHash);
