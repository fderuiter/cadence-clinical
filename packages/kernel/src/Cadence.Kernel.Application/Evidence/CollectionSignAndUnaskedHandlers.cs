using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

using FluentValidation;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>Resolves the protocol version that governs an enrollment at a valid time (K-TIME-8).</summary>
internal static class GoverningVersion
{
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

    public static string? Find(ProtocolVersion version, string? specId, string? activityId) =>
        Find(version, specId, activityId, out _);

    public static string? Find(ProtocolVersion version, string? specId, string? activityId, out ProtocolDesign? design)
    {
        design = null;
        if (string.IsNullOrWhiteSpace(specId) || specId.Length > CollectionInstance.ElementIdMaxLength
            || (activityId is not null && (string.IsNullOrWhiteSpace(activityId) || activityId.Length > CollectionInstance.ElementIdMaxLength)))
        {
            return "Name the CollectionSpec, and the planned activity when there is one, by their ids in the protocol design.";
        }

        design = ProtocolDesignDocument.Read(version.Content).Design;
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

/// <summary>Handles <see cref="SignCollectionInstance"/> (spec §9.5, #5463).</summary>
internal sealed class SignCollectionInstanceHandler(IValidationFindings? findings = null) : ICommandHandler<SignCollectionInstance, AttestationId>
{
    public async Task<CommandResult<AttestationId>> HandleAsync(
        SignCollectionInstance command, ICommandContext context, CancellationToken cancellationToken)
    {
        var work = context.Work;
        if (command.Meaning is not (AttestationMeaning.Approved or AttestationMeaning.Responsibility))
        {
            return CollectionErrors.SignatureMeaningInvalid;
        }

        await work.LockAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false);
        if (await work.ExistsAsync<Attestation>(command.AttestationId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await work.ReadAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false) is not { } instance
            || instance.EnrollmentId != command.EnrollmentId)
        {
            return KernelErrors.NotFound;
        }

        var memberIds = instance.Members.Select(m => m.ObservationId).ToList();
        if (await LocksInForce.AnyAsync(
                work,
                LockScopes.Covering(instance.StudyId, instance.StudySiteId, instance.EnrollmentId, instance.Id, memberIds),
                LocksInForce.DenyingAssertions,
                cancellationToken).ConfigureAwait(false))
        {
            return AssertionErrors.Locked;
        }

        if (instance.Members.Count == 0)
        {
            return TrustErrors.TargetNotSignable;
        }

        var (current, observations) = await CollectionContent.ReadAsync(work, instance, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(current, command.ContentHash, StringComparison.Ordinal))
        {
            return TrustErrors.TargetContentChanged;
        }

        if (await EvidenceReadiness.CheckCollectionSignableAsync(
                command.TenantId,
                instance,
                observations,
                work,
                findings,
                cancellationToken).ConfigureAwait(false) is { } notReady)
        {
            return notReady;
        }

        var signed = Attestation.Sign(
            command.AttestationId,
            command.TenantId,
            context.Actor,
            command.Meaning,
            new AttestationTarget(NodeKinds.CollectionInstance, instance.Id.Value, current),
            command.AuthenticatedAt,
            command.Method,
            context.RecordedAt,
            command.ReasonForChange);
        if (signed.Value is not { } attestation)
        {
            return signed.Error!;
        }

        work.Add(attestation);
        var attestationTarget = NodeRef.For(attestation.Id);
        work.LinkInput(NodeRef.For(instance.Id, Math.Max(1L, instance.VersionIndex)), LineageEdgeKind.AttestedBy, target: attestationTarget);
        foreach (var observation in observations.OrderBy(o => o.Id))
        {
            work.LinkInput(NodeRef.For(observation.Id, Math.Max(1L, observation.VersionIndex)), LineageEdgeKind.AttestedBy, target: attestationTarget);
        }

        work.Raise(new AttestationRecorded(attestation.TenantId, attestation.Id, attestation.ActorId, attestation.Meaning, attestation.Target));
        return CommandResult<AttestationId>.Success(attestation.Id);
    }
}

/// <summary>The content a form signature binds to (#5463).</summary>
internal static class CollectionContent
{
    public static async Task<string> HashAsync(IKernelUnitOfWork work, CollectionInstance instance, CancellationToken cancellationToken) =>
        (await ReadAsync(work, instance, cancellationToken).ConfigureAwait(false)).Hash;

    public static async Task<(string Hash, IReadOnlyList<Observation> Observations)> ReadAsync(
        IKernelUnitOfWork work, CollectionInstance instance, CancellationToken cancellationToken)
    {
        var members = instance.Members.Select(m => m.ObservationId).ToList();
        var observations = await work.ReadAllAsync<Observation>(o => members.Contains(o.Id), cancellationToken).ConfigureAwait(false);
        var hash = CollectionInstance.ContentHashOf(observations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)));
        return (hash, observations);
    }
}

/// <summary>Links a form instance and its saved item observations to their governing <c>protocol_element</c> nodes (ADR-2474).</summary>
internal static class CollectionLineage
{
    public static void LinkInstance(
        IKernelUnitOfWork work, CollectionInstance instance, IReadOnlyDictionary<string, NodeRef> elements)
    {
        var target = NodeRef.For(instance.Id);
        work.LinkInput(elements[instance.CollectionSpecId], LineageEdgeKind.CollectedBy, instance.CollectedAt, target);
        if (instance.PlannedActivityId is { } activityId && elements.TryGetValue(activityId, out var activity))
        {
            work.LinkInput(activity, LineageEdgeKind.CollectedBy, instance.CollectedAt, target);
        }
    }

    public static void LinkItem(
        IKernelUnitOfWork work, Observation observation, string itemId, IReadOnlyDictionary<string, NodeRef> elements) =>
        work.LinkInput(elements[itemId], LineageEdgeKind.CollectedBy, observation.ObservedAt, NodeRef.For(observation.Id));
}

/// <summary>Shared steps of the form instance handlers.</summary>
internal static class CollectionStates
{
    public static CollectionInstanceState Of(CollectionInstance instance, long version, VisitTimingAssessment? visitTimingAssessment = null) =>
        new(
            instance.Id,
            instance.EnrollmentId,
            instance.ProtocolVersionId,
            instance.CollectionSpecId,
            instance.PlannedActivityId,
            instance.CollectedAt,
            version,
            visitTimingAssessment);

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

/// <summary>Handles <see cref="RecordUnaskedCollectionItem"/>.</summary>
internal sealed class RecordUnaskedCollectionItemHandler : ICommandHandler<RecordUnaskedCollectionItem, CollectedValueState>
{
    public async Task<CommandResult<CollectedValueState>> HandleAsync(
        RecordUnaskedCollectionItem command, ICommandContext context, CancellationToken cancellationToken)
    {
        var work = context.Work;
        await work.LockCollectionAsync(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false);
        if (await work.FindAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false) is not { } instance
            || instance.EnrollmentId != command.EnrollmentId)
        {
            return KernelErrors.NotFound;
        }

        var version = await work.ReadAsync<ProtocolVersion>(instance.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        var spec = version is null ? null : CollectionSpecs.Of(version, instance.CollectionSpecId);
        if (spec is null)
        {
            throw new InvalidOperationException($"Form instance {instance.Id} names CollectionSpec {instance.CollectionSpecId}, which its protocol version does not have.");
        }

        if (spec.Items.FirstOrDefault(i => string.Equals(i.Id, command.ItemId, StringComparison.Ordinal)) is not { } item)
        {
            return CollectionErrors.ValuesInvalid with
            {
                Errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [command.ItemId] = [$"CollectionSpec {spec.Id} has no item {command.ItemId}."],
                },
            };
        }

        if (instance.MemberFor(command.ItemId) is { } existingMember)
        {
            var existing = await work.FindAsync<Observation>(existingMember.ObservationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Member observation {existingMember.ObservationId} of {instance.Id} does not exist.");
            if (await LocksInForce.RefusalAsync(
                work,
                LockScopes.Covering(instance.StudyId, instance.StudySiteId, instance.EnrollmentId, existing.Id, instance.Id),
                LocksInForce.DenyingAssertions,
                AssertionErrors.Locked,
                nameof(RecordUnaskedCollectionItem),
                NodeRef.For(existing.Id, existing.VersionIndex),
                cancellationToken).ConfigureAwait(false) is { } existingLocked)
            {
                return existingLocked;
            }

            return CommandResult<CollectedValueState>.Success(
                new CollectedValueState(command.ItemId, existing.Id, existing.Value, existing.Revision, Changed: false));
        }

        if (await LocksInForce.RefusalAsync(
            work,
            LockScopes.Covering(instance.StudyId, instance.StudySiteId, instance.EnrollmentId, instance.Id, []),
            LocksInForce.DenyingAssertions,
            AssertionErrors.Locked,
            nameof(RecordUnaskedCollectionItem),
            NodeRef.For(instance.Id, instance.VersionIndex),
            cancellationToken).ConfigureAwait(false) is { } instanceLocked)
        {
            return instanceLocked;
        }

        if (command.ObservationId is { } chosen && await work.ExistsAsync<Observation>(chosen, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists.WithMessage($"Observation {chosen} already exists; choose another id for the value of {command.ItemId}.");
        }

        var recorded = Observation.Record(
            command.ObservationId ?? ObservationId.New(),
            instance.TenantId,
            instance.EnrollmentId,
            instance.ParticipantId,
            instance.StudyId,
            item.Concept,
            new MissingValue(NullFlavor.NotAsked),
            instance.CollectedAt,
            command.ReasonForChange,
            context.Actor.Id,
            context.RecordedAt);
        if (recorded.Value is not { } added)
        {
            return recorded.Error!;
        }

        var elements = await ProtocolElementLineage.ElementsAsync(work, version!, cancellationToken).ConfigureAwait(false);
        work.Add(added);
        instance.Join(command.ItemId, added.Id, context.RecordedAt);
        instance.Saved(context.RecordedAt);
        var instanceVersion = instance.VersionIndex + 1;
        CollectionLineage.LinkInstance(work, instance, elements);
        CollectionLineage.LinkItem(work, added, item.Id, elements);
        work.Raise(
            new ObservationRecorded(
                added.TenantId, added.Id, added.EnrollmentId, added.ParticipantId, added.StudyId, added.Concept, added.Value, added.ObservedAt),
            added.ObservedAt);
        work.Raise(new CollectedValuesSaved(
            instance.TenantId, instance.Id, instance.EnrollmentId, instance.StudyId, instance.ProtocolVersionId, instance.CollectionSpecId,
            instanceVersion, [new CollectedValueChange(command.ItemId, added.Id, 1, null)]));
        return CommandResult<CollectedValueState>.Success(
            new CollectedValueState(command.ItemId, added.Id, added.Value, 1, Changed: true));
    }
}

/// <summary>Checks the shape of <see cref="RecordUnaskedCollectionItem"/>.</summary>
internal sealed class RecordUnaskedCollectionItemValidator : AbstractValidator<RecordUnaskedCollectionItem>
{
    public RecordUnaskedCollectionItemValidator()
    {
        RuleFor(c => c.CollectionInstanceId).Must(id => !id.IsEmpty);
        RuleFor(c => c.EnrollmentId).Must(id => !id.IsEmpty);
        RuleFor(c => c.ItemId).NotEmpty().MaximumLength(CollectionInstance.ElementIdMaxLength);
    }
}
