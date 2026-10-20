using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Core.Domain;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// A suggested change to a study by a person or an automation agent (spec §11, ADR-2489). Its content (intent, plan,
/// affected objects, simulated impact, requested capabilities, expiry, proposer and model) never changes once proposed,
/// and <see cref="ContentHash"/> covers all of it, so an approval signs exactly what was proposed (21 CFR 11.70). Only
/// the decision changes: <c>Open → Approved | Rejected | Withdrawn</c>. An open proposal past <see cref="ExpiresAt"/> reads
/// as <see cref="ProposalStatus.Expired"/> (<see cref="StatusAt"/>); nothing stores expiry.
/// A proposal has no effect on kernel state: nothing reads it to decide anything else, and approving it does not run its
/// plan (K-SIM-5). Expected failures return a <see cref="KernelError"/>; nothing here throws for them.
/// </summary>
public class Proposal : AggregateRoot<ProposalId>
{
    protected Proposal() { }

    private Proposal(ProposalId id, TenantId tenantId, Propose command, Actor proposedBy, DateTimeOffset proposedAt)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = command.StudyId;
        Intent = command.Intent;
        PlanArtifactVersionId = command.PlanArtifactVersionId;
        Affected = [.. command.Affected.Select(a => a.ToString())];
        SimulatedImpact = command.SimulatedImpact;
        RequestedCapabilities = [.. command.RequestedCapabilities];
        ExpiresAt = KernelTimestamps.ToStoredPrecision(command.ExpiresAt);
        ProposedAt = KernelTimestamps.ToStoredPrecision(proposedAt);
        ProposedById = proposedBy.Id;
        ProposedByKind = proposedBy.Kind;
        RequestedById = proposedBy.RequestedBy?.Id;
        ModelId = command.ModelId;
        ConfidenceScore = command.ConfidenceScore;
        EscalationPolicy = command.EscalationPolicy;
        Status = ProposalStatus.Open;

        if (command.ConfidenceScore.HasValue && command.ConfidenceScore.Value < Propose.AutoEscalationThreshold)
        {
            IsEscalated = true;
            EscalatedAt = KernelTimestamps.ToStoredPrecision(proposedAt);
            EscalatedById = proposedBy.Id;
            EscalatedByKind = proposedBy.Kind;
            EscalatedTo = "Manual Review";
            EscalationReason = $"Automated escalation: Confidence score ({command.ConfidenceScore.Value:F2}) is below threshold ({Propose.AutoEscalationThreshold:F2}).";
            EscalationPolicy ??= "LowConfidenceAutoEscalation";
        }
        else if (!string.IsNullOrWhiteSpace(command.EscalationPolicy) && command.EscalationPolicy.Equals("ForceEscalation", StringComparison.OrdinalIgnoreCase))
        {
            IsEscalated = true;
            EscalatedAt = KernelTimestamps.ToStoredPrecision(proposedAt);
            EscalatedById = proposedBy.Id;
            EscalatedByKind = proposedBy.Kind;
            EscalatedTo = "Manual Review";
            EscalationReason = "Automated escalation: Policy explicitly requested escalation.";
        }

        ContentHash = HashOf(this);
    }

    public TenantId TenantId { get; private set; }

    /// <summary>The study the proposal is about.</summary>
    public StudyId StudyId { get; private set; }

    /// <summary>What the proposal would achieve.</summary>
    public string Intent { get; private set; } = string.Empty;

    /// <summary>The artifact version that holds the plan and its reasoning.</summary>
    public ArtifactVersionId PlanArtifactVersionId { get; private set; }

    /// <summary>The objects it would change or depends on, as NodeRef text, in the order proposed.</summary>
    public string[] Affected { get; private set; } = [];

    /// <summary>The result hash of the simulation run over it (ADR-2480), or null.</summary>
    public string? SimulatedImpact { get; private set; }

    /// <summary>The capability tokens a person would use to execute it, in the order proposed.</summary>
    public string[] RequestedCapabilities { get; private set; } = [];

    /// <summary>After this instant it can no longer be approved, rejected or withdrawn.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was proposed: the recorded time of the command.</summary>
    public DateTimeOffset ProposedAt { get; private set; }

    /// <summary>The proposing actor's id.</summary>
    public string ProposedById { get; private set; } = string.Empty;

    /// <summary>The proposing actor's kind.</summary>
    public ActorKind ProposedByKind { get; private set; }

    /// <summary>The person an automation agent proposed it for; set exactly when an agent proposed it (K-AI-6).</summary>
    public string? RequestedById { get; private set; }

    /// <summary>The model that produced it; set exactly when an automation agent proposed it (K-PROV-3).</summary>
    public string? ModelId { get; private set; }

    /// <summary>The AI confidence score (0.0 to 1.0) if present.</summary>
    public double? ConfidenceScore { get; private set; }

    /// <summary>Calculated confidence tier based on score: High (>= 0.85), Medium (0.70 - 0.84), Low (&lt; 0.70).</summary>
    public string? ConfidenceTier => ConfidenceScore switch
    {
        null => null,
        >= 0.85 => "High",
        >= 0.70 => "Medium",
        _ => "Low",
    };

    /// <summary>True when the proposal has been escalated for review.</summary>
    public bool IsEscalated { get; private set; }

    /// <summary>When the proposal was escalated.</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>The actor ID who escalated the proposal.</summary>
    public string? EscalatedById { get; private set; }

    /// <summary>The actor kind who escalated the proposal.</summary>
    public ActorKind? EscalatedByKind { get; private set; }

    /// <summary>The role, group, or person to whom the proposal was escalated.</summary>
    public string? EscalatedTo { get; private set; }

    /// <summary>The reason for escalation.</summary>
    public string? EscalationReason { get; private set; }

    /// <summary>The escalation policy rule name or ID, if applicable.</summary>
    public string? EscalationPolicy { get; private set; }

    /// <summary>The kernel content hash of every content field, which an approval signs.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>The stored status: <see cref="ProposalStatus.Open"/> until decided. Never <see cref="ProposalStatus.Expired"/>.</summary>
    public ProposalStatus Status { get; private set; }

    /// <summary>When it was approved, rejected or withdrawn.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>The id of the actor who approved, rejected or withdrew it.</summary>
    public string? DecidedById { get; private set; }

    /// <summary>The kind of the actor who approved, rejected or withdrew it.</summary>
    public ActorKind? DecidedByKind { get; private set; }

    /// <summary>The attestation an approval was signed with.</summary>
    public AttestationId? ApprovalAttestationId { get; private set; }

    /// <summary>Why it was rejected or withdrawn.</summary>
    public string? DecisionReason { get; private set; }

    /// <summary>The actor who proposed it; for an automation agent, acting for the person it proposed for.</summary>
    public Actor ProposedBy => new(ProposedByKind, ProposedById) { RequestedBy = RequestedById is { } person ? Actor.Person(person) : null };

    /// <summary>What an approval of this proposal signs.</summary>
    public AttestationTarget SignableTarget => new(NodeKinds.Proposal, Id.Value, ContentHash);

    /// <summary>The affected objects as references.</summary>
    public IReadOnlyList<NodeRef> AffectedRefs() => [.. Affected.Select(NodeRef.Parse)];

    /// <summary>
    /// Records <paramref name="command"/> as a proposal by <paramref name="proposedBy"/> at <paramref name="proposedAt"/>.
    /// <paramref name="isExecutableCapability"/> says whether a token is an Execute or Approve command of the kernel; the
    /// handler checks the study and the plan, which need reads.
    /// </summary>
    public static CommandResult<Proposal> Create(
        TenantId tenantId, Propose command, Actor proposedBy, DateTimeOffset proposedAt, Func<string, bool> isExecutableCapability)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(proposedBy);
        ArgumentNullException.ThrowIfNull(isExecutableCapability);
        if (Problem(command, proposedBy, proposedAt, isExecutableCapability) is { } problem)
        {
            return problem;
        }

        return CommandResult<Proposal>.Success(new Proposal(command.ProposalId, tenantId, command, proposedBy, proposedAt));
    }

    /// <summary>The status at <paramref name="now"/>: <see cref="ProposalStatus.Expired"/> for an open proposal past its expiry.</summary>
    public ProposalStatus StatusAt(DateTimeOffset now) =>
        Status == ProposalStatus.Open && ExpiresAt <= now ? ProposalStatus.Expired : Status;

    /// <summary>Why the proposal cannot be decided at <paramref name="now"/>, or null when it is open and unexpired.</summary>
    public KernelError? CheckOpen(DateTimeOffset now) => StatusAt(now) switch
    {
        ProposalStatus.Open => null,
        ProposalStatus.Expired => ProposalErrors.Expired,
        var decided => ProposalErrors.NotOpen.WithMessage($"The proposal is already {decided}."),
    };

    /// <summary>
    /// Why <paramref name="approver"/> cannot approve at <paramref name="now"/>, or null: the proposal is open and
    /// unexpired and the approver did not propose it. The handler then verifies the attestation.
    /// </summary>
    public KernelError? CheckApproval(Actor approver, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approver);
        return CheckOpen(now) ?? (IsProposer(approver) ? ProposalErrors.SelfApproval : null);
    }

    /// <summary>Approves the proposal with <paramref name="attestation"/>, which the handler has verified.</summary>
    public KernelError? Approve(Actor approver, AttestationId attestation, DateTimeOffset now)
    {
        if (CheckApproval(approver, now) is { } refused)
        {
            return refused;
        }

        Decide(ProposalStatus.Approved, approver, now, reason: null);
        ApprovalAttestationId = attestation;
        return null;
    }

    /// <summary>
    /// Why <paramref name="executor"/> cannot run a command needing <paramref name="capability"/> under this proposal at
    /// <paramref name="now"/>, or null (K-AI-2): it is approved and unexpired, the executor is the person who approved it,
    /// acting as themselves, and it requested the capability. The pipeline still authorizes the command as the executor,
    /// so a proposal never widens anyone's authority.
    /// </summary>
    public KernelError? CheckExecution(Actor executor, string capability, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(capability);
        if (Status != ProposalStatus.Approved)
        {
            return ProposalErrors.NotApproved.WithMessage($"The proposal is {StatusAt(now)}; only an approved proposal can be executed.");
        }

        if (ExpiresAt <= KernelTimestamps.ToStoredPrecision(now))
        {
            return ProposalErrors.ExecutionExpired;
        }

        if (executor.Kind != ActorKind.Person || DecidedByKind != ActorKind.Person || !string.Equals(executor.Id, DecidedById, StringComparison.Ordinal))
        {
            return ProposalErrors.NotApprover;
        }

        return RequestedCapabilities.Contains(capability, StringComparer.Ordinal)
            ? null
            : ProposalErrors.CapabilityNotRequested.WithMessage($"The proposal did not request {capability}.");
    }

    /// <summary>Rejects the proposal for <paramref name="reason"/>.</summary>
    public KernelError? Reject(Actor rejecter, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rejecter);
        if (CheckOpen(now) is { } refused)
        {
            return refused;
        }

        Decide(ProposalStatus.Rejected, rejecter, now, reason);
        return null;
    }

    /// <summary>Withdraws the proposal; only its proposer may.</summary>
    public KernelError? Withdraw(Actor actor, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (CheckOpen(now) is { } refused)
        {
            return refused;
        }

        if (!IsProposer(actor))
        {
            return ProposalErrors.NotProposer;
        }

        Decide(ProposalStatus.Withdrawn, actor, now, reason);
        return null;
    }

    /// <summary>Escalates the proposal for review by <paramref name="escalateTo"/>.</summary>
    public KernelError? Escalate(Actor actor, string escalateTo, string reason, DateTimeOffset now, string? policy = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(escalateTo))
        {
            return ProposalErrors.EscalationTargetRequired;
        }

        if (CheckOpen(now) is { } refused)
        {
            return refused;
        }

        if (IsEscalated)
        {
            return ProposalErrors.AlreadyEscalated;
        }

        IsEscalated = true;
        EscalatedAt = KernelTimestamps.ToStoredPrecision(now);
        EscalatedById = actor.Id;
        EscalatedByKind = actor.Kind;
        EscalatedTo = escalateTo;
        EscalationReason = reason;
        if (!string.IsNullOrWhiteSpace(policy))
        {
            EscalationPolicy = policy;
        }

        return null;
    }

    /// <summary>The proposal as a command result, with its status at <paramref name="now"/>.</summary>
    public ProposalState ToState(DateTimeOffset now) => new(
        Id, StudyId, StatusAt(now), ContentHash, ProposedById, ProposedByKind, ModelId, ExpiresAt, DecidedAt, DecidedById, ApprovalAttestationId,
        ConfidenceScore, ConfidenceTier, IsEscalated, EscalatedAt, EscalatedById, EscalatedByKind, EscalatedTo, EscalationReason, EscalationPolicy,
        RequestedById);

    /// <summary>
    /// The content hash of <paramref name="proposal"/>: the kernel content hash (<see cref="Trust.ContentHash"/>) of a JSON
    /// document holding its id, study and every content field, so any change to any of them changes the hash.
    /// </summary>
    public static string HashOf(Proposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var document = new JsonObject
        {
            ["id"] = proposal.Id.Value.ToString("D", CultureInfo.InvariantCulture),
            ["study"] = proposal.StudyId.Value.ToString("D", CultureInfo.InvariantCulture),
            ["intent"] = proposal.Intent,
            ["plan"] = proposal.PlanArtifactVersionId.Value.ToString("D", CultureInfo.InvariantCulture),
            ["affected"] = new JsonArray([.. proposal.Affected.Select(a => (JsonNode?)JsonValue.Create(a))]),
            ["simulatedImpact"] = proposal.SimulatedImpact,
            ["requestedCapabilities"] = new JsonArray([.. proposal.RequestedCapabilities.Select(c => (JsonNode?)JsonValue.Create(c))]),
            ["expiresAt"] = Instant(proposal.ExpiresAt),
            ["proposedAt"] = Instant(proposal.ProposedAt),
            ["proposedBy"] = new JsonObject
            {
                ["id"] = proposal.ProposedById,
                ["kind"] = proposal.ProposedByKind.ToString(),
                ["requestedBy"] = proposal.RequestedById,
            },
            ["modelId"] = proposal.ModelId,
            ["confidenceScore"] = proposal.ConfidenceScore,
        };
        return Trust.ContentHash.Compute(document.ToJsonString());
    }

    private static KernelError? Problem(Propose command, Actor proposedBy, DateTimeOffset proposedAt, Func<string, bool> isExecutableCapability)
    {
        if (command.ConfidenceScore.HasValue && (command.ConfidenceScore.Value < 0.0 || command.ConfidenceScore.Value > 1.0))
        {
            return ProposalErrors.InvalidConfidenceScore;
        }

        if (string.IsNullOrWhiteSpace(command.Intent) || command.Intent.Length > Propose.MaxIntentLength)
        {
            return ProposalErrors.Invalid.WithMessage($"The intent is 1 to {Propose.MaxIntentLength} characters.");
        }

        if (command.StudyId.IsEmpty || command.PlanArtifactVersionId.IsEmpty)
        {
            return ProposalErrors.Invalid.WithMessage("A proposal names its study and the artifact version holding its plan.");
        }

        if (command.Affected is null || command.Affected.Count > Propose.MaxAffected || command.Affected.Any(a => a is null))
        {
            return ProposalErrors.Invalid.WithMessage($"A proposal names 0 to {Propose.MaxAffected} affected objects.");
        }

        if (command.Affected.Distinct().Count() != command.Affected.Count)
        {
            return ProposalErrors.Invalid.WithMessage("Each affected object is named once.");
        }

        if (command.SimulatedImpact is not null && !Trust.ContentHash.IsWellFormed(command.SimulatedImpact))
        {
            return ProposalErrors.Invalid.WithMessage($"A simulated impact is a simulation result hash ({Trust.ContentHash.Prefix} and 64 lower-case hex digits).");
        }

        if (command.RequestedCapabilities is null
            || command.RequestedCapabilities.Count == 0
            || command.RequestedCapabilities.Count > Propose.MaxRequestedCapabilities)
        {
            return ProposalErrors.Invalid.WithMessage($"A proposal requests 1 to {Propose.MaxRequestedCapabilities} capabilities.");
        }

        if (command.RequestedCapabilities.Distinct(StringComparer.Ordinal).Count() != command.RequestedCapabilities.Count)
        {
            return ProposalErrors.Invalid.WithMessage("Each capability is requested once.");
        }

        foreach (var token in command.RequestedCapabilities)
        {
            if (!Capability.IsWellFormed(token) || !isExecutableCapability(token))
            {
                return ProposalErrors.CapabilityUnknown.WithMessage($"'{token}' is not an Execute or Approve command of the kernel.");
            }
        }

        var expiresAt = KernelTimestamps.ToStoredPrecision(command.ExpiresAt);
        var at = KernelTimestamps.ToStoredPrecision(proposedAt);
        if (expiresAt <= at || expiresAt > at + Propose.MaxLifetime)
        {
            return ProposalErrors.Invalid.WithMessage($"A proposal expires after it is proposed and at most {Propose.MaxLifetime.TotalDays:0} days later.");
        }

        if (proposedBy.Kind == ActorKind.AutomationAgent)
        {
            if (proposedBy.RequestedBy is null)
            {
                return ProposalErrors.RequesterRequired;
            }

            if (string.IsNullOrWhiteSpace(command.ModelId) || command.ModelId.Length > Propose.MaxModelIdLength)
            {
                return ProposalErrors.ModelRequired.WithMessage(
                    $"{ProposalErrors.ModelRequired.Message} The model id is 1 to {Propose.MaxModelIdLength} characters.");
            }
        }
        else if (command.ModelId is not null)
        {
            return ProposalErrors.ModelNotAllowed;
        }

        return null;
    }

    /// <summary>The same actor, and for an agent the same agent acting for the same person (K-AI-6).</summary>
    private bool IsProposer(Actor actor) =>
        actor.Kind == ProposedByKind
        && string.Equals(actor.Id, ProposedById, StringComparison.Ordinal)
        && string.Equals(actor.RequestedBy?.Id, RequestedById, StringComparison.Ordinal);

    private void Decide(ProposalStatus status, Actor actor, DateTimeOffset now, string? reason)
    {
        Status = status;
        DecidedAt = KernelTimestamps.ToStoredPrecision(now);
        DecidedById = actor.Id;
        DecidedByKind = actor.Kind;
        DecisionReason = reason;
    }

    private static string Instant(DateTimeOffset value) =>
        KernelTimestamps.ToStoredPrecision(value).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}
