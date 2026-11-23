using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Handles <see cref="OpenCollectionInstance"/> (K-FACT-6, K-TIME-8): it resolves the protocol version governing the
/// enrollment's study site at the collection time, checks that its design has the CollectionSpec (and that the planned
/// activity collects it), and opens the form instance with <see cref="LineageEdgeKind.CollectedBy"/> edges from its
/// governing <c>protocol_element</c> spec and planned activity. A withdrawn participant gets no new form
/// (<see cref="CaptureConditions"/>).
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

        if (CollectionSpecs.Find(version, command.CollectionSpecId, command.PlannedActivityId, out var design) is { } problem)
        {
            return CollectionErrors.SpecUnknown.WithMessage(problem);
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

        var referenceStart = DateOnly.FromDateTime(enrolled.Period.Valid.Start.UtcDateTime);
        var actual = DateOnly.FromDateTime(collectedAt.UtcDateTime);
        var expected = ExpectedSchedule.Compute(design, referenceStart, actual)
            .FirstOrDefault(v => string.Equals(v.Visit.ActivityId, plannedActivityId, StringComparison.Ordinal));
        return expected is null ? null : ExpectedSchedule.Assess(expected, actual);
    }
}

/// <summary>
/// Handles <see cref="RecordCollectedValues"/> (ADR-2622): under the form instance's exclusive lock and shared locks on
/// every scope covering the form and its values, it checks every value against the CollectionSpec, then every expected
/// revision, and only then records and corrects the values together. Any failure changes nothing. After the participant's
/// withdrawal, a save with a value for an item that has none is refused whole (<see cref="CaptureConditions"/>).
/// </summary>
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

        if (await CheckValuesAsync(command.TenantId, values, spec, cancellationToken).ConfigureAwait(false) is { } invalid)
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

    /// <summary>
    /// The structural and controlled-terminology checks that need the CollectionSpec (ADR-2622 §4.4, spec §1037-1039):
    /// every value names an item of the spec, fits its shape, and when coded belongs to its governed codelist or code
    /// system version. <see cref="RecordCollectedValuesValidator"/> has already checked the save's shape.
    /// </summary>
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
    public static string? Find(ProtocolVersion version, string? specId, string? activityId) =>
        Find(version, specId, activityId, out _);

    /// <summary>Why <paramref name="version"/> cannot govern a form of <paramref name="specId"/> at <paramref name="activityId"/>, or null.</summary>
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

/// <summary>
/// Handles <see cref="SignCollectionInstance"/> (spec §9.5, #5463): under the form instance's advisory lock, so no save
/// interleaves, it checks that no study-scope <see cref="AssertionKind.Locked"/> assertion holds, hashes the instance's
/// current revisions and records the signature only when that is the hash the signer read. The attestation then names
/// exactly those revisions; a later correction leaves it as it is. A form with no values has nothing to sign. Child-scope
/// locks and <see cref="AssertionKind.Frozen"/> locks do not refuse a signature: signing a frozen or site/participant/form
/// locked form is how an investigator signs off prior to final study lock.
/// </summary>
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

        // The form instance's lock, without claiming the instance: a signature never changes the form, it adds one
        // Attestation (K-TX-1), and it reads the content no save can change until it commits.
        await work.LockAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false);
        if (await work.ExistsAsync<Attestation>(command.AttestationId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await work.ReadAsync<CollectionInstance>(command.CollectionInstanceId, cancellationToken).ConfigureAwait(false) is not { } instance
            || instance.EnrollmentId != command.EnrollmentId)
        {
            // A form of another enrollment is reported as missing: the caller was authorized for this one only.
            return KernelErrors.NotFound;
        }

        if (await LocksInForce.AnyAsync(
                work,
                [NodeRef.For(instance.StudyId)],
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
    /// <summary>The content hash of <paramref name="instance"/>'s current revisions (<see cref="CollectionInstance.ContentHashOf"/>).</summary>
    public static async Task<string> HashAsync(IKernelUnitOfWork work, CollectionInstance instance, CancellationToken cancellationToken) =>
        (await ReadAsync(work, instance, cancellationToken).ConfigureAwait(false)).Hash;

    /// <summary>Reads <paramref name="instance"/>'s member observations and computes their current content hash.</summary>
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

/// <summary>
/// Handles <see cref="RecordUnaskedCollectionItem"/>: ensures a blank item of a form instance holds a
/// <see cref="NullFlavor.NotAsked"/> observation at revision 1 so a missing-data query targets a kernel observation.
/// Refused under <see cref="LockKind.Locked"/> scopes while allowed under <see cref="LockKind.Frozen"/> scopes.
/// </summary>
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
            if (await LocksInForce.AnyAsync(
                work,
                LockScopes.Covering(instance.StudyId, instance.StudySiteId, instance.EnrollmentId, existing.Id, instance.Id),
                LocksInForce.DenyingAssertions,
                cancellationToken).ConfigureAwait(false))
            {
                return AssertionErrors.Locked;
            }

            return CommandResult<CollectedValueState>.Success(
                new CollectedValueState(command.ItemId, existing.Id, existing.Value, existing.Revision, Changed: false));
        }

        if (await LocksInForce.AnyAsync(
            work,
            LockScopes.Covering(instance.StudyId, instance.StudySiteId, instance.EnrollmentId, instance.Id, []),
            LocksInForce.DenyingAssertions,
            cancellationToken).ConfigureAwait(false))
        {
            return AssertionErrors.Locked;
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
