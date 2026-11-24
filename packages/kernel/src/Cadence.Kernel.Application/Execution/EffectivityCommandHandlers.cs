using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Execution;

/// <summary>
/// Handles <see cref="DeclareEffectivity"/>. The protocol version must exist and not be withdrawn, a study-site scope
/// must belong to its study, and the version may have only one effectivity per scope.
/// </summary>
internal sealed class DeclareEffectivityHandler : ICommandHandler<DeclareEffectivity, EffectivityState>
{
    public async Task<CommandResult<EffectivityState>> HandleAsync(DeclareEffectivity command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (command.Scope?.Problem() is { } problem)
        {
            return EffectivityErrors.ScopeInvalid.WithMessage(problem);
        }

        if (await context.Work.ExistsAsync<Effectivity>(command.EffectivityId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return EffectivityErrors.ProtocolVersionUnknown;
        }

        if (version.Status == ProtocolVersionStatus.Withdrawn)
        {
            return EffectivityErrors.ProtocolVersionWithdrawn;
        }

        var scope = command.Scope!;
        if (scope.StudySiteId is { } studySiteId
            && (await context.Work.ReadAsync<StudySite>(studySiteId, cancellationToken).ConfigureAwait(false))?.StudyId != version.StudyId)
        {
            return EffectivityErrors.StudySiteNotInStudy;
        }

        var coveringScopes = scope.StudySiteId is { } targetStudySite
            ? LockScopes.Covering(version.StudyId, targetStudySite)
            : [NodeRef.For(version.StudyId)];
        if (await LocksInForce.AnyAsync(context.Work, coveringScopes, LocksInForce.DenyingWrites, cancellationToken).ConfigureAwait(false))
        {
            return ObservationErrors.Locked;
        }

        var versionId = command.ProtocolVersionId;
        var declared = await context.Work.ReadAllAsync<Effectivity>(e => e.ProtocolVersionId == versionId, cancellationToken).ConfigureAwait(false);
        if (declared.Any(e => e.Scope == scope))
        {
            return EffectivityErrors.AlreadyDeclared;
        }

        var created = Effectivity.Declare(
            command.EffectivityId, command.TenantId, version.StudyId, versionId, scope, command.DeclaredAt, context.RecordedAt);
        if (created.Value is not { } effectivity)
        {
            return created.Error!;
        }

        context.Work.Add(effectivity);
        context.Work.Raise(
            new EffectivityDeclared(effectivity.TenantId, effectivity.Id, versionId, version.StudyId, scope, effectivity.StatusSince),
            effectivity.StatusSince);
        return EffectivityStates.Of(effectivity);
    }
}

/// <summary>
/// Handles <see cref="ChangeEffectivityStatus"/>. Each evidence item must be a version of an artifact of an accepted kind
/// that is tenant-wide or belongs to the effectivity's study; becoming effective needs one (K-TIME-5) and an approved
/// protocol version.
/// </summary>
internal sealed class ChangeEffectivityStatusHandler : ICommandHandler<ChangeEffectivityStatus, EffectivityState>
{
    public async Task<CommandResult<EffectivityState>> HandleAsync(ChangeEffectivityStatus command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Effectivity>(command.EffectivityId, cancellationToken).ConfigureAwait(false) is not { } effectivity)
        {
            return KernelErrors.NotFound;
        }

        var coveringScopes = effectivity.ScopeStudySiteId is { } targetStudySite
            ? LockScopes.Covering(effectivity.StudyId, targetStudySite)
            : [NodeRef.For(effectivity.StudyId)];
        if (await LocksInForce.AnyAsync(context.Work, coveringScopes, LocksInForce.DenyingWrites, cancellationToken).ConfigureAwait(false))
        {
            return ObservationErrors.Locked;
        }

        var evidence = command.Evidence ?? [];
        if (effectivity.CheckTransition(command.To, command.EffectiveAt, context.RecordedAt) is { } invalid)
        {
            return invalid;
        }

        if (evidence.Count > Effectivity.MaxEvidence)
        {
            return EffectivityErrors.EvidenceInvalid.WithMessage($"Name at most {Effectivity.MaxEvidence} distinct artifact versions.");
        }

        if (await EvidenceProblemAsync(effectivity, command.To, evidence, context, cancellationToken).ConfigureAwait(false) is { } evidenceProblem)
        {
            return evidenceProblem;
        }

        if (command.To == EffectivityStatus.Effective
            && (await context.Work.ReadAsync<ProtocolVersion>(effectivity.ProtocolVersionId, cancellationToken).ConfigureAwait(false))?.Status
                is not (ProtocolVersionStatus.Approved or ProtocolVersionStatus.Superseded))
        {
            return EffectivityErrors.ProtocolVersionNotApproved;
        }

        var from = effectivity.Status;
        if (effectivity.ChangeStatus(command.To, evidence, command.EffectiveAt, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(
            new EffectivityStatusChanged(
                effectivity.TenantId, effectivity.Id, effectivity.ProtocolVersionId, from, effectivity.Status, effectivity.StatusSince, [.. evidence]),
            effectivity.StatusSince);
        return EffectivityStates.Of(effectivity);
    }

    private static async Task<KernelError?> EvidenceProblemAsync(
        Effectivity effectivity, EffectivityStatus to, IReadOnlyList<ArtifactVersionId> evidence, ICommandContext context, CancellationToken cancellationToken)
    {
        var accepted = EffectivityTransitions.EvidenceKinds(to);
        if (evidence.Count > 0 && accepted.Count == 0)
        {
            return EffectivityErrors.EvidenceInvalid.WithMessage($"A move to {to} takes no evidence.");
        }

        if (evidence.Distinct().Count() != evidence.Count)
        {
            return EffectivityErrors.EvidenceInvalid.WithMessage("Each artifact version is named once.");
        }

        foreach (var versionId in evidence)
        {
            var id = versionId;
            var artifacts = await context.Work.ReadAllAsync<Artifact>(a => a.Versions.Any(v => v.Id == id), cancellationToken).ConfigureAwait(false);
            if (artifacts.SingleOrDefault() is not { } artifact
                || !accepted.Contains(artifact.Kind)
                || (artifact.StudyId is { } study && study != effectivity.StudyId))
            {
                return EffectivityErrors.EvidenceInvalid.WithMessage(
                    $"Artifact version {versionId} does not exist, belongs to another study, or is not one of: {string.Join(", ", accepted.Order())}.");
            }
        }

        return null;
    }
}

/// <summary>
/// Handles <see cref="ActivateStudySite"/>: the study site becomes active only when a protocol version is in force there
/// at the command's recorded time (K-TIME-4).
/// </summary>
internal sealed class ActivateStudySiteHandler : ICommandHandler<ActivateStudySite, StudySiteState>
{
    /// <summary>The status an active study site has.</summary>
    public const string ActiveStatus = "Active";

    public async Task<CommandResult<StudySiteState>> HandleAsync(ActivateStudySite command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<StudySite>(command.StudySiteId, cancellationToken).ConfigureAwait(false) is not { } studySite)
        {
            return EffectivityErrors.StudySiteUnknown;
        }

        if (await LocksInForce.AnyAsync(
                context.Work,
                LockScopes.Covering(studySite.StudyId, studySite.Id),
                LocksInForce.DenyingWrites,
                cancellationToken).ConfigureAwait(false))
        {
            return ObservationErrors.Locked;
        }

        if (string.Equals(studySite.Status, ActiveStatus, StringComparison.Ordinal))
        {
            return EffectivityErrors.StudySiteAlreadyActive;
        }

        var site = await context.Work.ReadAsync<Site>(studySite.SiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Study site {studySite.Id} names site {studySite.SiteId}, which does not exist.");
        var studyId = studySite.StudyId;
        var effectivities = await context.Work.ReadAllAsync<Effectivity>(e => e.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        var effective = await EffectiveVersionsAsync(
            EffectivityResolution.EffectiveAt(effectivities, studySite.Id, site.Country, context.RecordedAt, context.RecordedAt), context, cancellationToken)
            .ConfigureAwait(false);
        if (effective.Count == 0)
        {
            return EffectivityErrors.NoEffectiveVersion;
        }

        studySite.Activate(command.ReasonForChange);
        context.Work.Raise(new StudySiteActivated(studySite.TenantId, studySite.Id, studyId, [.. effective.Select(v => v.ProtocolVersionId)]));
        return CommandResult<StudySiteState>.Success(new StudySiteState(studySite.Id, studySite.Status, effective));
    }

    private static async Task<IReadOnlyList<EffectiveVersion>> EffectiveVersionsAsync(
        IReadOnlyList<Effectivity> deciding, ICommandContext context, CancellationToken cancellationToken)
    {
        var versions = new List<EffectiveVersion>(deciding.Count);
        foreach (var effectivity in deciding)
        {
            var version = await context.Work.ReadAsync<ProtocolVersion>(effectivity.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
            versions.Add(new EffectiveVersion(effectivity.ProtocolVersionId, version?.Number ?? 0, effectivity.Id, effectivity.ScopeLevel));
        }

        return [.. versions.OrderByDescending(v => v.Number)];
    }
}

/// <summary>Builds <see cref="EffectivityState"/> results.</summary>
internal static class EffectivityStates
{
    public static CommandResult<EffectivityState> Of(Effectivity effectivity) =>
        CommandResult<EffectivityState>.Success(new EffectivityState(
            effectivity.Id, effectivity.ProtocolVersionId, effectivity.Scope, effectivity.Status, effectivity.StatusSince, effectivity.CurrentEvidence()));
}

/// <summary>Registers the effectivity and study-site activation handlers (spec §7.1).</summary>
public static class EffectivityServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelEffectivityHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<DeclareEffectivity, EffectivityState>, DeclareEffectivityHandler>();
        services.AddTransient<ICommandHandler<ChangeEffectivityStatus, EffectivityState>, ChangeEffectivityStatusHandler>();
        services.AddTransient<ICommandHandler<ActivateStudySite, StudySiteState>, ActivateStudySiteHandler>();
        return services;
    }
}
