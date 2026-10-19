using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>Handles <see cref="RecordObservation"/>: the participant and study come from the enrollment.</summary>
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
/// Handles <see cref="CorrectObservation"/> (ADR-2620): under the observation's lock-aggregate advisory lock (K-TX-4) it
/// reads the lock, then checks the expected version, then appends the revision. The version is also the commit-time
/// concurrency token, so a change between this read and the commit is refused.
/// </summary>
internal sealed class CorrectObservationHandler : ICommandHandler<CorrectObservation, ObservationState>
{
    public async Task<CommandResult<ObservationState>> HandleAsync(CorrectObservation command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ReasonForChange))
        {
            return ObservationErrors.ReasonForChangeRequired;
        }

        var lockId = ObservationLockId.For(command.ObservationId);
        await context.Work.LockAsync<ObservationLock>(lockId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.ReadAsync<ObservationLock>(lockId, cancellationToken).ConfigureAwait(false) is not null)
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

/// <summary>Handles <see cref="LockObservation"/> under the same advisory lock as corrections (K-TX-4).</summary>
internal sealed class LockObservationHandler : ICommandHandler<LockObservation, ObservationLockState>
{
    public async Task<CommandResult<ObservationLockState>> HandleAsync(LockObservation command, ICommandContext context, CancellationToken cancellationToken)
    {
        var lockId = ObservationLockId.For(command.ObservationId);
        await context.Work.LockAsync<ObservationLock>(lockId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.ExistsAsync<ObservationLock>(lockId, cancellationToken).ConfigureAwait(false))
        {
            return ObservationErrors.AlreadyLocked;
        }

        if (await context.Work.ReadAsync<Observation>(command.ObservationId, cancellationToken).ConfigureAwait(false) is not { } observation
            || observation.EnrollmentId != command.EnrollmentId)
        {
            return KernelErrors.NotFound;
        }

        var locked = new ObservationLock(observation.Id, observation.TenantId, observation.EnrollmentId, context.RecordedAt);
        context.Work.Add(locked);
        context.Work.Raise(new ObservationLocked(locked.TenantId, locked.Id, locked.ObservationId, locked.EnrollmentId));
        return CommandResult<ObservationLockState>.Success(new ObservationLockState(locked.Id, locked.ObservationId, locked.LockedAt, context.Actor.Id));
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
        services.AddTransient<ICommandHandler<LockObservation, ObservationLockState>, LockObservationHandler>();
        return services;
    }
}
