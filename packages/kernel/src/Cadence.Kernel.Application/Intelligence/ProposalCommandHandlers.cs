using System;
using System.Collections.Frozen;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Intelligence;

/// <summary>
/// The capabilities a proposal may request (ADR-2489 §4.1): the tokens of the kernel's own Execute and Approve commands
/// in <see cref="KernelCommandCatalog.Kernel"/>.
/// </summary>
public static class ProposalCapabilities
{
    private static readonly FrozenSet<string> Executable = KernelCommandCatalog.Kernel.Entries
        .Where(e => e.Capability.VerbClass is VerbClass.Execute or VerbClass.Approve)
        .Select(e => e.Capability.Token)
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True when <paramref name="token"/> is the capability of a kernel Execute or Approve command.</summary>
    public static bool IsExecutable(string token) => token is not null && Executable.Contains(token);
}

/// <summary>
/// Handles <see cref="Propose"/> (ADR-2489 §4.1). The study must exist and the plan must be a version of an artifact of
/// the same study or of the tenant. The acting actor is the proposer: an automation agent names its model, anyone else
/// names none. The plan and every affected object must be ones the proposer may see (K-AI-3), so a proposal never links into scope
/// its proposer cannot read. The proposal derives from its plan and is defined by each affected object version in
/// lineage (§4.4).
/// </summary>
internal sealed class ProposeHandler(INodeVisibility visibility) : ICommandHandler<Propose, ProposalState>
{
    public async Task<CommandResult<ProposalState>> HandleAsync(Propose command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Proposal>(command.ProposalId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var created = Proposal.Create(command.TenantId, command, context.Actor, context.RecordedAt, ProposalCapabilities.IsExecutable);
        if (created.Value is not { } proposal)
        {
            return created.Error!;
        }

        if (command.Affected.Any(a => a.Kind == NodeKinds.Proposal && a.Id == command.ProposalId.Value))
        {
            return ProposalErrors.Invalid.WithMessage("A proposal cannot name itself as affected.");
        }

        if (await context.Work.ReadAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ProposalErrors.StudyUnknown;
        }

        var planId = command.PlanArtifactVersionId;
        var artifacts = await context.Work.ReadAllAsync<Artifact>(a => a.Versions.Any(v => v.Id == planId), cancellationToken).ConfigureAwait(false);
        if (artifacts.SingleOrDefault() is not { } plan || (plan.StudyId is { } planStudy && planStudy != command.StudyId))
        {
            return ProposalErrors.PlanInvalid.WithMessage(
                $"Artifact version {planId} does not exist or belongs to another study; the plan is an artifact version of the proposal's study or of the tenant.");
        }

        var hidden = await visibility
            .MaskAsync(command.TenantId, context.Actor, [NodeRef.For(planId, 1), .. command.Affected], cancellationToken)
            .ConfigureAwait(false);
        if (hidden.Count > 0)
        {
            return ProposalErrors.NotVisible;
        }

        context.Work.Add(proposal);

        // An artifact version never changes, so its lineage version is always 1. Affected objects are linked where the
        // reference names a version; lineage joins versions, so an unversioned reference is kept on the proposal only.
        context.Work.LinkInput(NodeRef.For(planId, 1), LineageEdgeKind.DerivedFrom);
        foreach (var affected in command.Affected.Where(a => a.Version is not null))
        {
            context.Work.LinkInput(affected, LineageEdgeKind.DefinedBy);
        }

        context.Work.Raise(new ProposalCreated(
            proposal.TenantId,
            proposal.Id,
            proposal.StudyId,
            proposal.ProposedById,
            proposal.ProposedByKind,
            proposal.ModelId,
            proposal.PlanArtifactVersionId,
            [.. proposal.RequestedCapabilities],
            proposal.ExpiresAt,
            proposal.ContentHash,
            proposal.ConfidenceScore,
            proposal.EscalationPolicy,
            proposal.IsEscalated));

        if (proposal.IsEscalated && proposal.EscalatedAt.HasValue)
        {
            context.Work.Raise(new ProposalEscalated(
                proposal.TenantId,
                proposal.Id,
                proposal.StudyId,
                proposal.EscalatedById ?? proposal.ProposedById,
                proposal.EscalatedByKind ?? proposal.ProposedByKind,
                proposal.EscalatedTo ?? "Manual Review",
                proposal.EscalationReason ?? "Automated escalation",
                proposal.EscalatedAt.Value,
                proposal.EscalationPolicy));
        }

        return CommandResult<ProposalState>.Success(proposal.ToState(context.RecordedAt));
    }
}

/// <summary>Handles <see cref="EscalateProposal"/>: escalates an open proposal for human review.</summary>
internal sealed class EscalateProposalHandler : ICommandHandler<EscalateProposal, ProposalState>
{
    public async Task<CommandResult<ProposalState>> HandleAsync(EscalateProposal command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Proposal>(command.ProposalId, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return KernelErrors.NotFound;
        }

        if (proposal.Escalate(context.Actor, command.EscalateTo, command.ReasonForChange, context.RecordedAt, command.EscalationPolicy) is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProposalEscalated(
            proposal.TenantId,
            proposal.Id,
            proposal.StudyId,
            context.Actor.Id,
            context.Actor.Kind,
            command.EscalateTo,
            command.ReasonForChange,
            context.RecordedAt,
            command.EscalationPolicy ?? proposal.EscalationPolicy));

        return CommandResult<ProposalState>.Success(proposal.ToState(context.RecordedAt));
    }
}

/// <summary>
/// Handles <see cref="ApproveProposal"/> (ADR-2489 §4.2): the proposal is open and unexpired, the approver did not
/// propose it, and the named attestation means <c>Approved</c> and was signed by the acting person over the proposal's
/// content hash (21 CFR 11.70). Approval does not run the plan (K-SIM-5).
/// </summary>
internal sealed class ApproveProposalHandler(IAttestationVerifier verifier) : ICommandHandler<ApproveProposal, ProposalState>
{
    public async Task<CommandResult<ProposalState>> HandleAsync(ApproveProposal command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Proposal>(command.ProposalId, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return KernelErrors.NotFound;
        }

        if (proposal.CheckApproval(context.Actor, context.RecordedAt) is { } refused)
        {
            return refused;
        }

        var verification = await verifier
            .VerifyAsync(
                new AttestationRequirement(context.TenantId, command.AttestationId, AttestationMeaning.Approved, proposal.SignableTarget, context.Actor),
                cancellationToken)
            .ConfigureAwait(false);
        if (!verification.Satisfied)
        {
            return ProposalErrors.AttestationRejected.WithMessage(verification.Reason ?? ProposalErrors.AttestationRejected.Message);
        }

        if (proposal.Approve(context.Actor, command.AttestationId, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProposalApproved(
            proposal.TenantId, proposal.Id, proposal.StudyId, proposal.ContentHash, command.AttestationId, context.Actor.Id));
        return CommandResult<ProposalState>.Success(proposal.ToState(context.RecordedAt));
    }
}

/// <summary>Handles <see cref="RejectProposal"/>: an open, unexpired proposal is rejected for the command's reason.</summary>
internal sealed class RejectProposalHandler : ICommandHandler<RejectProposal, ProposalState>
{
    public async Task<CommandResult<ProposalState>> HandleAsync(RejectProposal command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Proposal>(command.ProposalId, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return KernelErrors.NotFound;
        }

        if (proposal.Reject(context.Actor, command.ReasonForChange, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProposalRejected(proposal.TenantId, proposal.Id, proposal.StudyId, context.Actor.Id, command.ReasonForChange));
        return CommandResult<ProposalState>.Success(proposal.ToState(context.RecordedAt));
    }
}

/// <summary>Handles <see cref="WithdrawProposal"/>: only the proposer withdraws an open, unexpired proposal.</summary>
internal sealed class WithdrawProposalHandler : ICommandHandler<WithdrawProposal, ProposalState>
{
    public async Task<CommandResult<ProposalState>> HandleAsync(WithdrawProposal command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Proposal>(command.ProposalId, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return KernelErrors.NotFound;
        }

        if (proposal.Withdraw(context.Actor, command.ReasonForChange, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProposalWithdrawn(proposal.TenantId, proposal.Id, proposal.StudyId, context.Actor.Id));
        return CommandResult<ProposalState>.Success(proposal.ToState(context.RecordedAt));
    }
}

/// <summary>
/// Lets a person sign a proposal (ADR-2489 §4.2): the proposal must exist and the attested hash must be its content hash,
/// which never changes. Whether it is still open is checked when it is approved.
/// </summary>
internal sealed class ProposalAttestationCheck : IAttestationTargetCheck
{
    public string Kind => NodeKinds.Proposal;

    public async Task<KernelError?> CheckAsync(AttestationTarget target, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);
        var proposal = await context.Work.ReadAsync<Proposal>(ProposalId.FromGuid(target.Id), cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return TrustErrors.TargetUnknown;
        }

        return string.Equals(proposal.ContentHash, target.ContentHash, StringComparison.Ordinal) ? null : TrustErrors.TargetContentChanged;
    }
}

/// <summary>Registers the proposal handlers and the proposal attestation check (ADR-2489).</summary>
public static class ProposalServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelProposalHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<Propose, ProposalState>, ProposeHandler>();
        services.AddTransient<ICommandHandler<ApproveProposal, ProposalState>, ApproveProposalHandler>();
        services.AddTransient<ICommandHandler<RejectProposal, ProposalState>, RejectProposalHandler>();
        services.AddTransient<ICommandHandler<WithdrawProposal, ProposalState>, WithdrawProposalHandler>();
        services.AddTransient<ICommandHandler<EscalateProposal, ProposalState>, EscalateProposalHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IAttestationTargetCheck, ProposalAttestationCheck>());
        return services;
    }
}
