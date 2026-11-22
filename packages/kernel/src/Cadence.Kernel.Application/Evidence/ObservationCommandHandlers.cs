using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Handles <see cref="RecordObservation"/>: the participant and study come from the enrollment, no lock may cover
/// the enrollment, its study site or its study (spec §10.5), and the participant may not be withdrawn
/// (<see cref="CaptureConditions"/>).
/// </summary>
internal sealed class RecordObservationHandler : ICommandHandler<RecordObservation, ObservationState>
{
    public async Task<CommandResult<ObservationState>> HandleAsync(RecordObservation command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Observation>(command.ObservationId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return ObservationErrors.EnrollmentUnknown;
        }

        if (await LocksInForce.AnyAsync(
            context.Work, LockScopes.Covering(enrollment.StudyId, enrollment.StudySiteId, enrollment.Id), LocksInForce.DenyingWrites, cancellationToken)
            .ConfigureAwait(false))
        {
            return ObservationErrors.Locked;
        }

        if (await CaptureConditions.RefusalAsync(context.Work, enrollment.Id, nameof(RecordObservation), cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        var recorded = Observation.Record(
            command.ObservationId,
            command.TenantId,
            enrollment.Id,
            enrollment.ParticipantId,
            enrollment.StudyId,
            command.Concept,
            command.Value,
            command.ObservedAt,
            command.ReasonForChange,
            context.Actor.Id,
            context.RecordedAt);
        if (recorded.Value is not { } observation)
        {
            return recorded.Error!;
        }

        context.Work.Add(observation);
        context.Work.Raise(
            new ObservationRecorded(
                observation.TenantId, observation.Id, observation.EnrollmentId, observation.ParticipantId, observation.StudyId,
                observation.Concept, observation.Value, observation.ObservedAt),
            observation.ObservedAt);

        // A new aggregate is stamped at version 1 when the pipeline commits.
        return ObservationStates.Of(observation, version: 1);
    }
}

/// <summary>
/// Handles <see cref="CorrectObservation"/> (ADR-2620, ADR-2621): under the observation's advisory lock, so corrections
/// of one observation run one after another, and shared locks on the scopes that cover it, it reads the locks in force,
/// then checks the expected version, then appends the revision. The version is also the commit-time concurrency token.
/// A withdrawal does not refuse a correction: the observation was captured before it (<see cref="CaptureConditions"/>).
/// </summary>
internal sealed class CorrectObservationHandler : ICommandHandler<CorrectObservation, ObservationState>
{
    public async Task<CommandResult<ObservationState>> HandleAsync(CorrectObservation command, ICommandContext context, CancellationToken cancellationToken)
    {
        await context.Work.LockAsync<Observation>(command.ObservationId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return KernelErrors.NotFound;
        }

        var instance = await CollectionMembership.InstanceOfAsync(context.Work, command.ObservationId, cancellationToken).ConfigureAwait(false);
        if (await LocksInForce.AnyAsync(
            context.Work,
            LockScopes.Covering(enrollment.StudyId, enrollment.StudySiteId, enrollment.Id, command.ObservationId, instance),
            LocksInForce.DenyingWrites,
            cancellationToken).ConfigureAwait(false))
        {
            return ObservationErrors.Locked;
        }

        if (await context.Work.FindAsync<Observation>(command.ObservationId, cancellationToken).ConfigureAwait(false) is not { } observation
            || observation.EnrollmentId != command.EnrollmentId)
        {
            // An observation of another enrollment is reported as missing: the caller was authorized for this one only.
            return KernelErrors.NotFound;
        }

        if (observation.VersionIndex != command.ExpectedVersion)
        {
            return ObservationErrors.VersionStale.WithMessage(
                $"The observation is at version {observation.VersionIndex}, not {command.ExpectedVersion}; reload it and correct the current value.");
        }

        if (instance is { } instanceId
            && ObservationValue.ProblemOf(command.Value) is null
            && await context.Work.ReadAsync<CollectionInstance>(instanceId, cancellationToken).ConfigureAwait(false) is { } collectionInstance
            && collectionInstance.Members.FirstOrDefault(m => m.ObservationId == command.ObservationId) is { } member
            && await context.Work.ReadAsync<ProtocolVersion>(collectionInstance.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is { } version
            && CollectionSpecs.Of(version, collectionInstance.CollectionSpecId)
                ?.Items.FirstOrDefault(i => string.Equals(i.Id, member.ItemId, StringComparison.Ordinal)) is { } specItem
            && specItem.Admits(command.Value) is { } itemProblem)
        {
            return CollectionErrors.ValuesInvalid.WithMessage(itemProblem) with
            {
                Errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [member.ItemId] = [itemProblem],
                },
            };
        }

        var superseded = observation.Revision;
        if (observation.Correct(command.Value, command.ReasonForChange, context.Actor.Id, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(new ObservationCorrected(
            observation.TenantId, observation.Id, observation.EnrollmentId, observation.Revision, superseded, observation.Value, command.ReasonForChange));

        // The pipeline increments the version once when it commits the modified aggregate.
        return ObservationStates.Of(observation, version: observation.VersionIndex + 1);
    }
}

/// <summary>Shared steps of the observation handlers.</summary>
internal static class ObservationStates
{
    public static CommandResult<ObservationState> Of(Observation observation, long version) =>
        CommandResult<ObservationState>.Success(new ObservationState(
            observation.Id, observation.EnrollmentId, observation.ParticipantId, observation.StudyId, observation.Concept,
            observation.Value, version, observation.Revisions.Max(r => r.Number)));
}

/// <summary>Registers the observation command handlers (spec §8.1, ADR-2620).</summary>
public static class ObservationServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelObservationHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<RecordObservation, ObservationState>, RecordObservationHandler>();
        services.AddTransient<ICommandHandler<CorrectObservation, ObservationState>, CorrectObservationHandler>();
        return services;
    }
}
