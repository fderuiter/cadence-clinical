using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Handles <see cref="OpenCollectionInstance"/> (K-FACT-6, K-TIME-8): resolves the protocol version governing the
/// enrollment's study site at the collection time, checks that its design has the CollectionSpec, and opens the form instance.
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

        await context.Work.LockAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (await context.Work.ReadAsync<Enrollment>(command.EnrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return CollectionErrors.EnrollmentUnknown;
        }

        var collectedAt = KernelTimestamps.ToStoredPrecision(command.CollectedAt);
        if (collectedAt > context.RecordedAt)
        {
            return CollectionErrors.CollectedAtInvalid;
        }

        if (await LocksInForce.RefusalAsync(
            context.Work,
            LockScopes.Covering(enrollment.StudyId, enrollment.StudySiteId, enrollment.Id),
            LocksInForce.DenyingWrites,
            ObservationErrors.Locked,
            nameof(OpenCollectionInstance),
            NodeRef.For(enrollment.Id, enrollment.VersionIndex),
            cancellationToken).ConfigureAwait(false) is { } locked)
        {
            return locked;
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

        if (CollectionSpecs.Find(version, command.CollectionSpecId, command.PlannedActivityId, out var design) is { } problem)
        {
            return CollectionErrors.SpecUnknown.WithMessage(problem);
        }

        if (command.PlannedActivityId is not null)
        {
            var spec = design!.CollectionSpecs.First(s => string.Equals(s.Id, command.CollectionSpecId, StringComparison.Ordinal));
            var activity = design.PlannedActivities.First(a => string.Equals(a.Id, command.PlannedActivityId, StringComparison.Ordinal));
            if (!spec.Repeating && !activity.Repeating)
            {
                var existingForCell = await context.Work.ReadAllAsync<CollectionInstance>(
                    c => c.EnrollmentId == enrollment.Id
                        && c.CollectionSpecId == command.CollectionSpecId
                        && c.PlannedActivityId == command.PlannedActivityId,
                    cancellationToken).ConfigureAwait(false);
                if (existingForCell.Any(c => c.Id != command.CollectionInstanceId))
                {
                    return CollectionErrors.DuplicateSingletonInstance;
                }
            }
        }

        var elements = await ProtocolElementLineage.ElementsAsync(context.Work, version, cancellationToken).ConfigureAwait(false);
        var instance = CollectionInstance.Open(
            command.CollectionInstanceId, enrollment, version.Id, command.CollectionSpecId, command.PlannedActivityId, collectedAt,
            context.Actor.Id, context.RecordedAt);
        context.Work.Add(instance);
        CollectionLineage.LinkInstance(context.Work, instance, elements);
        context.Work.Raise(
            new CollectionInstanceOpened(
                instance.TenantId, instance.Id, instance.EnrollmentId, instance.ParticipantId, instance.StudyId, version.Id,
                instance.CollectionSpecId, instance.PlannedActivityId, instance.CollectedAt),
            instance.CollectedAt);
        var assessment = AssessVisit(design!, enrollment, command.PlannedActivityId, collectedAt, context.RecordedAt);
        return CommandResult<CollectionInstanceState>.Success(CollectionStates.Of(instance, version: 1, assessment));
    }

    private static VisitTimingAssessment? AssessVisit(
        ProtocolDesign design,
        Enrollment enrollment,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        DateTimeOffset knownAt)
    {
        if (plannedActivityId is null)
        {
            return null;
        }

        if (enrollment.Periods
            .Where(p => p.Status == EnrollmentStatus.Enrolled && p.Period.Recorded.Contains(knownAt))
            .OrderBy(p => p.Period.Valid.Start)
            .FirstOrDefault() is not { } enrolled)
        {
            return null;
        }

        var enrolledAt = enrolled.Period.Valid.Start;
        var referenceStart = DateOnly.FromDateTime(enrolledAt.DateTime);
        var actual = DateOnly.FromDateTime(collectedAt.ToOffset(enrolledAt.Offset).DateTime);
        var expected = ExpectedSchedule.Compute(design, referenceStart, actual)
            .FirstOrDefault(v => string.Equals(v.Visit.ActivityId, plannedActivityId, StringComparison.Ordinal));
        return expected is null ? null : ExpectedSchedule.Assess(expected, actual);
    }
}

/// <summary>Handles <see cref="RecordCollectedValues"/> (ADR-2622).</summary>
internal sealed class RecordCollectedValuesHandler(IConceptRegistry? concepts = null)
    : ICommandHandler<RecordCollectedValues, CollectedValuesState>
{
    public async Task<CommandResult<CollectedValuesState>> HandleAsync(
        RecordCollectedValues command, ICommandContext context, CancellationToken cancellationToken)
    {
        var work = context.Work;
        await work.LockCollectionAsync(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false);
        if (await work.FindAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false) is not { } instance
            || instance.EnrollmentId != command.EnrollmentId)
        {
            return KernelErrors.NotFound;
        }

        var values = command.Values;
        var version = await work.ReadAsync<ProtocolVersion>(instance.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        var spec = version is null ? null : CollectionSpecs.Of(version, instance.CollectionSpecId);
        if (spec is null)
        {
            throw new InvalidOperationException($"Form instance {instance.Id} names CollectionSpec {instance.CollectionSpecId}, which its protocol version does not have.");
        }

        if (await CheckValuesAsync(command.TenantId, values, spec, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var members = values.Select(v => instance.MemberFor(v.ItemId)?.ObservationId).OfType<ObservationId>().ToList();
        if (await LocksInForce.RefusalAsync(
            work,
            LockScopes.Covering(instance.StudyId, instance.StudySiteId, instance.EnrollmentId, instance.Id, members),
            LocksInForce.DenyingWrites,
            ObservationErrors.Locked,
            nameof(RecordCollectedValues),
            NodeRef.For(instance.Id, instance.VersionIndex),
            cancellationToken).ConfigureAwait(false) is { } locked)
        {
            return locked;
        }

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
            var elements = await ProtocolElementLineage.ElementsAsync(work, version!, cancellationToken).ConfigureAwait(false);
            instance.Saved(context.RecordedAt);
            instanceVersion++;
            CollectionLineage.LinkInstance(work, instance, elements);
            foreach (var change in changes)
            {
                CollectionLineage.LinkItem(work, current[change.ItemId], change.ItemId, elements);
            }

            work.Raise(new CollectedValuesSaved(
                instance.TenantId, instance.Id, instance.EnrollmentId, instance.StudyId, instance.ProtocolVersionId, instance.CollectionSpecId,
                instanceVersion, changes));
        }

        return await CollectionStates.ValuesAsync(work, instance, instanceVersion, current, changes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<KernelError?> CheckValuesAsync(
        TenantId tenant,
        IReadOnlyList<CollectedValue> values,
        CollectionSpec spec,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Dictionary<(CodeSystemVersionId Version, string GroupCode), IReadOnlyList<CodeSystemEntryView>>? groupCache = null;

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
            else if (concepts is not null && item.Terminology is { } terminology && value.Value is CodedValue coded)
            {
                if (!string.IsNullOrWhiteSpace(terminology.Codelist))
                {
                    groupCache ??= [];
                    var key = (terminology.CodeSystemVersionId, terminology.Codelist);
                    if (!groupCache.TryGetValue(key, out var groupEntries))
                    {
                        groupEntries = await concepts
                            .GetGroupAsync(tenant, terminology.CodeSystemVersionId, terminology.Codelist, cancellationToken)
                            .ConfigureAwait(false);
                        groupCache[key] = groupEntries;
                    }

                    if (!groupEntries.Any(e => string.Equals(e.Code, coded.Code, StringComparison.Ordinal)))
                    {
                        errors[value.ItemId] =
                        [
                            $"Item {item.Id} rejects code '{coded.Code}': it is not in codelist '{terminology.Codelist}' of code system version {terminology.CodeSystemVersionId}.",
                        ];
                    }
                }
                else
                {
                    var foundEntries = await concepts
                        .LookupAsync(tenant, new Coding(terminology.CodeSystemVersionId, coded.Code), cancellationToken)
                        .ConfigureAwait(false);
                    if (foundEntries.Count == 0)
                    {
                        errors[value.ItemId] =
                        [
                            $"Item {item.Id} rejects code '{coded.Code}': it is not in code system version {terminology.CodeSystemVersionId}.",
                        ];
                    }
                }
            }
        }

        return errors.Count > 0 ? CollectionErrors.ValuesInvalid with { Errors = errors } : null;
    }
}

/// <summary>Checks the shape of <see cref="OpenCollectionInstance"/>.</summary>
internal sealed class OpenCollectionInstanceValidator : AbstractValidator<OpenCollectionInstance>
{
    public OpenCollectionInstanceValidator()
    {
        RuleFor(c => c.CollectionSpecId).NotEmpty().MaximumLength(CollectionInstance.ElementIdMaxLength);
        RuleFor(c => c.PlannedActivityId).NotEmpty().MaximumLength(CollectionInstance.ElementIdMaxLength).When(c => c.PlannedActivityId is not null);
    }
}

/// <summary>Checks the shape of <see cref="RecordCollectedValues"/>.</summary>
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

/// <summary>Registers the form instance command handlers (K-FACT-6, ADR-2622).</summary>
public static class CollectionServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelCollectionHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<OpenCollectionInstance, CollectionInstanceState>, OpenCollectionInstanceHandler>();
        services.AddTransient<ICommandHandler<RecordCollectedValues, CollectedValuesState>, RecordCollectedValuesHandler>();
        services.AddTransient<ICommandHandler<RecordUnaskedCollectionItem, CollectedValueState>, RecordUnaskedCollectionItemHandler>();
        services.AddTransient<ICommandHandler<SignCollectionInstance, AttestationId>, SignCollectionInstanceHandler>();
        services.AddCommandValidator<OpenCollectionInstance, OpenCollectionInstanceValidator>();
        services.AddCommandValidator<RecordCollectedValues, RecordCollectedValuesValidator>();
        services.AddCommandValidator<RecordUnaskedCollectionItem, RecordUnaskedCollectionItemValidator>();
        return services;
    }
}
