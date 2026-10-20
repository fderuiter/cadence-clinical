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
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Handles <see cref="OpenCollectionInstance"/> (K-FACT-6, K-TIME-8): it resolves the protocol version governing the
/// enrollment's study site at the collection time, checks that its design has the CollectionSpec (and that the planned
/// activity collects it), and opens the form instance with a <see cref="LineageEdgeKind.GovernedBy"/> edge from that
/// version. A withdrawn participant gets no new form (<see cref="CaptureConditions"/>).
/// </summary>
internal sealed class OpenCollectionInstanceHandler : ICommandHandler<OpenCollectionInstance, CollectionInstanceState>
{
    public async Task<CommandResult<CollectionInstanceState>> HandleAsync(
        OpenCollectionInstance command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return CollectionErrors.EnrollmentUnknown;
        }

        var collectedAt = KernelTimestamps.ToStoredPrecision(command.CollectedAt);
        if (collectedAt > context.RecordedAt)
        {
            return CollectionErrors.CollectedAtInvalid;
        }

        if (await LocksInForce.AnyAsync(
            context.Work, LockScopes.Covering(enrollment.StudyId, enrollment.StudySiteId, enrollment.Id), LocksInForce.DenyingWrites, cancellationToken)
            .ConfigureAwait(false))
        {
            return ObservationErrors.Locked;
        }

        if (await CaptureConditions.RefusalAsync(context.Work, enrollment.Id, nameof(OpenCollectionInstance), cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        if (await GoverningVersion.ResolveAsync(context.Work, enrollment, collectedAt, context.RecordedAt, cancellationToken).ConfigureAwait(false)
            is not { } version)
        {
            return CollectionErrors.NoGoverningVersion;
        }

        if (CollectionSpecs.Find(version, command.CollectionSpecId, command.PlannedActivityId) is { } problem)
        {
            return CollectionErrors.SpecUnknown.WithMessage(problem);
        }

        var instance = CollectionInstance.Open(
            command.CollectionInstanceId, enrollment, version.Id, command.CollectionSpecId, command.PlannedActivityId, collectedAt,
            context.Actor.Id, context.RecordedAt);
        context.Work.Add(instance);
        context.Work.LinkInput(NodeRef.For(version.Id, version.VersionIndex), LineageEdgeKind.GovernedBy, collectedAt);
        context.Work.Raise(
            new CollectionInstanceOpened(
                instance.TenantId, instance.Id, instance.EnrollmentId, instance.ParticipantId, instance.StudyId, version.Id,
                instance.CollectionSpecId, instance.PlannedActivityId, instance.CollectedAt),
            instance.CollectedAt);
        return CommandResult<CollectionInstanceState>.Success(CollectionStates.Of(instance, version: 1));
    }
}

/// <summary>
/// Handles <see cref="RecordCollectedValues"/> (ADR-2622): under the form instance's exclusive lock and shared locks on
/// every scope covering the form and its values, it checks every value against the CollectionSpec, then every expected
/// revision, and only then records and corrects the values together. Any failure changes nothing. After the participant's
/// withdrawal, a save with a value for an item that has none is refused whole (<see cref="CaptureConditions"/>).
/// </summary>
internal sealed class RecordCollectedValuesHandler : ICommandHandler<RecordCollectedValues, CollectedValuesState>
{
    public async Task<CommandResult<CollectedValuesState>> HandleAsync(
        RecordCollectedValues command, ICommandContext context, CancellationToken cancellationToken)
    {
        var work = context.Work;
        await work.LockCollectionAsync(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false);
        if (await work.FindAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false) is not { } instance
            || instance.EnrollmentId != command.EnrollmentId)
        {
            // A form of another enrollment is reported as missing: the caller was authorized for this one only.
            return KernelErrors.NotFound;
        }

        var values = command.Values;
        var version = await work.ReadAsync<ProtocolVersion>(instance.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        var spec = version is null ? null : CollectionSpecs.Of(version, instance.CollectionSpecId);
        if (spec is null)
        {
            throw new InvalidOperationException($"Form instance {instance.Id} names CollectionSpec {instance.CollectionSpecId}, which its protocol version does not have.");
        }

        if (CheckValues(values, spec) is { } invalid)
        {
            return invalid;
        }

        var members = values.Select(v => instance.MemberFor(v.ItemId)?.ObservationId).OfType<ObservationId>().ToList();
        if (await LocksInForce.AnyAsync(
            work,
            LockScopes.Covering(instance.StudyId, instance.StudySiteId, instance.EnrollmentId, instance.Id, members),
            LocksInForce.DenyingWrites,
            cancellationToken).ConfigureAwait(false))
        {
            return ObservationErrors.Locked;
        }

        // A save that only corrects values captured before a withdrawal goes on; one with a new value is new capture.
        if (values.Any(v => instance.MemberFor(v.ItemId) is null)
            && await CaptureConditions.RefusalAsync(work, instance.EnrollmentId, nameof(RecordCollectedValues), cancellationToken).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        var current = new Dictionary<string, Observation>(StringComparer.Ordinal);
        var stale = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (instance.MemberFor(value.ItemId) is not { } member)
            {
                if (value.ExpectedRevision != 0)
                {
                    stale[value.ItemId] = [$"Item {value.ItemId} has no value; expected revision 0, not {value.ExpectedRevision}."];
                }
                else if (value.ObservationId is { } chosen && await work.ExistsAsync<Observation>(chosen, cancellationToken).ConfigureAwait(false))
                {
                    return KernelErrors.AlreadyExists.WithMessage($"Observation {chosen} already exists; choose another id for the value of {value.ItemId}.");
                }

                continue;
            }

            var observation = await work.FindAsync<Observation>(member.ObservationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Member observation {member.ObservationId} of {instance.Id} does not exist.");
            current[value.ItemId] = observation;
            if (value.ObservationId is { } named && named != observation.Id)
            {
                stale[value.ItemId] = [$"Item {value.ItemId} holds observation {observation.Id}, not {named}."];
            }
            else if (observation.Revision != value.ExpectedRevision)
            {
                stale[value.ItemId] = [$"Item {value.ItemId} is at revision {observation.Revision}, not {value.ExpectedRevision}."];
            }
        }

        if (stale.Count > 0)
        {
            return CollectionErrors.RevisionStale with { Errors = stale };
        }

        var changes = new List<CollectedValueChange>();
        foreach (var value in values)
        {
            var item = spec.Items.Single(i => string.Equals(i.Id, value.ItemId, StringComparison.Ordinal));
            if (current.TryGetValue(value.ItemId, out var observation))
            {
                if (observation.Value == value.Value)
                {
                    continue;
                }

                var superseded = observation.Revision;
                if (observation.Correct(value.Value, command.ReasonForChange, context.Actor.Id, context.RecordedAt) is { } error)
                {
                    return error;
                }

                work.Raise(new ObservationCorrected(
                    observation.TenantId, observation.Id, observation.EnrollmentId, observation.Revision, superseded, observation.Value, command.ReasonForChange));
                changes.Add(new CollectedValueChange(value.ItemId, observation.Id, observation.Revision, superseded));
                continue;
            }

            var recorded = Observation.Record(
                value.ObservationId ?? ObservationId.New(), instance.TenantId, instance.EnrollmentId, instance.ParticipantId, instance.StudyId,
                item.Concept, value.Value, value.ObservedAt ?? instance.CollectedAt, command.ReasonForChange, context.Actor.Id, context.RecordedAt);
            if (recorded.Value is not { } added)
            {
                return recorded.Error!;
            }

            work.Add(added);
            instance.Join(value.ItemId, added.Id, context.RecordedAt);
            current[value.ItemId] = added;
            work.Raise(
                new ObservationRecorded(
                    added.TenantId, added.Id, added.EnrollmentId, added.ParticipantId, added.StudyId, added.Concept, added.Value, added.ObservedAt),
                added.ObservedAt);
            changes.Add(new CollectedValueChange(value.ItemId, added.Id, 1, null));
        }

        var instanceVersion = instance.VersionIndex;
        if (changes.Count > 0)
        {
            instance.Saved(context.RecordedAt);
            instanceVersion++;
            work.LinkInput(NodeRef.For(version!.Id, version.VersionIndex), LineageEdgeKind.GovernedBy, instance.CollectedAt);
            work.Raise(new CollectedValuesSaved(
                instance.TenantId, instance.Id, instance.EnrollmentId, instance.StudyId, instance.ProtocolVersionId, instance.CollectionSpecId,
                instanceVersion, changes));
        }

        return await CollectionStates.ValuesAsync(work, instance, instanceVersion, current, changes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The structural checks that need the CollectionSpec (ADR-2622 §4.4): every value names an item of the spec and fits
    /// it. <see cref="RecordCollectedValuesValidator"/> has already checked the save's shape.
    /// </summary>
    private static KernelError? CheckValues(IReadOnlyList<CollectedValue> values, CollectionSpec spec)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (spec.Items.FirstOrDefault(i => string.Equals(i.Id, value.ItemId, StringComparison.Ordinal)) is not { } item)
            {
                errors[value.ItemId] = [$"CollectionSpec {spec.Id} has no item {value.ItemId}."];
            }
            else if (item.Admits(value.Value) is { } problem)
            {
                errors[value.ItemId] = [problem];
            }
        }

        return errors.Count > 0 ? CollectionErrors.ValuesInvalid with { Errors = errors } : null;
    }
}

/// <summary>Checks the shape of <see cref="OpenCollectionInstance"/>: protocol element ids the kernel can store.</summary>
internal sealed class OpenCollectionInstanceValidator : AbstractValidator<OpenCollectionInstance>
{
    public OpenCollectionInstanceValidator()
    {
        RuleFor(c => c.CollectionSpecId).NotEmpty().MaximumLength(CollectionInstance.ElementIdMaxLength);
        RuleFor(c => c.PlannedActivityId).NotEmpty().MaximumLength(CollectionInstance.ElementIdMaxLength).When(c => c.PlannedActivityId is not null);
    }
}

/// <summary>
/// Checks the shape of <see cref="RecordCollectedValues"/>: at least one value, each naming a distinct item with a value
/// and an expected revision of 0 or more, and no observation id chosen twice.
/// </summary>
internal sealed class RecordCollectedValuesValidator : AbstractValidator<RecordCollectedValues>
{
    public RecordCollectedValuesValidator()
    {
        RuleFor(c => c.Values).NotEmpty();
        RuleForEach(c => c.Values).ChildRules(value =>
        {
            value.RuleFor(v => v.ItemId).NotEmpty().MaximumLength(CollectionInstance.ElementIdMaxLength);
            value.RuleFor(v => v.Value).NotNull();
            value.RuleFor(v => v.ExpectedRevision).GreaterThanOrEqualTo(0);
        }).When(c => c.Values is not null);
        RuleFor(c => c.Values)
            .Must(values => values.Where(v => v?.ItemId is not null).GroupBy(v => v.ItemId, StringComparer.Ordinal).All(g => g.Count() == 1))
            .WithMessage("Each item is named at most once.")
            .When(c => c.Values is not null);
        RuleFor(c => c.Values)
            .Must(values => values.Where(v => v?.ObservationId is not null).GroupBy(v => v.ObservationId).All(g => g.Count() == 1))
            .WithMessage("Each observation id is chosen at most once.")
            .When(c => c.Values is not null);
    }
}

/// <summary>Resolves the protocol version that governs an enrollment at a valid time (K-TIME-8).</summary>
internal static class GoverningVersion
{
    /// <summary>
    /// The highest-numbered protocol version in force (<see cref="EffectivityResolution.EffectiveAt"/>) at the enrollment's
    /// study site at <paramref name="validAt"/>, as known at <paramref name="knownAt"/>, or null when none is.
    /// </summary>
    public static async Task<ProtocolVersion?> ResolveAsync(
        IKernelUnitOfWork work, Enrollment enrollment, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken)
    {
        if (await work.ReadAsync<StudySite>(enrollment.StudySiteId, cancellationToken).ConfigureAwait(false) is not { } studySite
            || await work.ReadAsync<Site>(studySite.SiteId, cancellationToken).ConfigureAwait(false) is not { } site)
        {
            return null;
        }

        var study = enrollment.StudyId;
        var effectivities = await work.ReadAllAsync<Effectivity>(e => e.StudyId == study, cancellationToken).ConfigureAwait(false);
        var effective = EffectivityResolution.EffectiveAt(effectivities, studySite.Id, site.Country, validAt, knownAt)
            .Select(e => e.ProtocolVersionId)
            .ToList();
        if (effective.Count == 0)
        {
            return null;
        }

        var versions = await work.ReadAllAsync<ProtocolVersion>(v => effective.Contains(v.Id), cancellationToken).ConfigureAwait(false);
        return versions.MaxBy(v => v.Number);
    }
}

/// <summary>Finds the form instance an observation was collected in.</summary>
internal static class CollectionMembership
{
    /// <summary>
    /// The form instance <paramref name="observation"/> is a member of, or null for an observation recorded on its own:
    /// the instance is one of the lock scopes that cover the observation (spec §10.5).
    /// </summary>
    public static async Task<CollectionInstanceId?> InstanceOfAsync(IKernelUnitOfWork work, ObservationId observation, CancellationToken cancellationToken)
    {
        var instances = await work.ReadAllAsync<CollectionInstance>(c => c.Members.Any(m => m.ObservationId == observation), cancellationToken)
            .ConfigureAwait(false);
        return instances.Count == 0 ? null : instances[0].Id;
    }
}

/// <summary>Finds a CollectionSpec in a protocol version's design.</summary>
internal static class CollectionSpecs
{
    public static CollectionSpec? Of(ProtocolVersion version, string specId) =>
        ProtocolDesignDocument.Read(version.Content).Design?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, specId, StringComparison.Ordinal));

    /// <summary>Why <paramref name="version"/> cannot govern a form of <paramref name="specId"/> at <paramref name="activityId"/>, or null.</summary>
    public static string? Find(ProtocolVersion version, string? specId, string? activityId)
    {
        if (string.IsNullOrWhiteSpace(specId) || specId.Length > CollectionInstance.ElementIdMaxLength
            || (activityId is not null && (string.IsNullOrWhiteSpace(activityId) || activityId.Length > CollectionInstance.ElementIdMaxLength)))
        {
            return "Name the CollectionSpec, and the planned activity when there is one, by their ids in the protocol design.";
        }

        var design = ProtocolDesignDocument.Read(version.Content).Design;
        if (design?.CollectionSpecs.Any(s => string.Equals(s.Id, specId, StringComparison.Ordinal)) != true)
        {
            return $"Protocol version {version.Number} has no CollectionSpec {specId}.";
        }

        if (activityId is not null
            && !design.PlannedActivities.Any(a => string.Equals(a.Id, activityId, StringComparison.Ordinal) && a.CollectionSpecIds.Contains(specId, StringComparer.Ordinal)))
        {
            return $"Protocol version {version.Number} has no planned activity {activityId} that collects {specId}.";
        }

        return null;
    }
}

/// <summary>Shared steps of the form instance handlers.</summary>
internal static class CollectionStates
{
    public static CollectionInstanceState Of(CollectionInstance instance, long version) =>
        new(instance.Id, instance.EnrollmentId, instance.ProtocolVersionId, instance.CollectionSpecId, instance.PlannedActivityId, instance.CollectedAt, version);

    /// <summary>Every value of <paramref name="instance"/> after a save, reading the members the save did not load.</summary>
    public static async Task<CommandResult<CollectedValuesState>> ValuesAsync(
        IKernelUnitOfWork work,
        CollectionInstance instance,
        long version,
        IReadOnlyDictionary<string, Observation> loaded,
        IReadOnlyList<CollectedValueChange> changes,
        CancellationToken cancellationToken)
    {
        var unloaded = instance.Members.Where(m => !loaded.ContainsKey(m.ItemId)).Select(m => m.ObservationId).ToList();
        var others = unloaded.Count == 0
            ? []
            : (await work.ReadAllAsync<Observation>(o => unloaded.Contains(o.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(o => o.Id);
        var changed = changes.Select(c => c.ItemId).ToHashSet(StringComparer.Ordinal);
        var values = instance.Members
            .OrderBy(m => m.ItemId, StringComparer.Ordinal)
            .Select(m => (Member: m, Observation: loaded.TryGetValue(m.ItemId, out var observation) ? observation : others[m.ObservationId]))
            .Select(x => new CollectedValueState(x.Member.ItemId, x.Observation.Id, x.Observation.Value, x.Observation.Revision, changed.Contains(x.Member.ItemId)))
            .ToList();
        return CommandResult<CollectedValuesState>.Success(new CollectedValuesState(instance.Id, version, values));
    }
}

/// <summary>Registers the form instance command handlers (K-FACT-6, ADR-2622).</summary>
public static class CollectionServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelCollectionHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<OpenCollectionInstance, CollectionInstanceState>, OpenCollectionInstanceHandler>();
        services.AddTransient<ICommandHandler<RecordCollectedValues, CollectedValuesState>, RecordCollectedValuesHandler>();
        services.AddCommandValidator<OpenCollectionInstance, OpenCollectionInstanceValidator>();
        services.AddCommandValidator<RecordCollectedValues, RecordCollectedValuesValidator>();
        return services;
    }
}
