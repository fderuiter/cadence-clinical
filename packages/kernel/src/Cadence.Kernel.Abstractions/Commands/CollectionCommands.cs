using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Opens a form instance for a participant (spec §8.1, K-FACT-6, ADR-2622): a <c>CollectionInstance</c> that will hold
/// the values collected under one CollectionSpec. The governing protocol version is resolved by the kernel, not named
/// by the caller: the highest-numbered version in force at the enrollment's study site at <c>CollectedAt</c> (K-TIME-8),
/// whose design must contain the CollectionSpec. Refused when a lock covers the enrollment (spec §10.5) and when the
/// participant is withdrawn (spec §7.4, <see cref="ObservationErrors.ParticipantWithdrawn"/>).
/// </summary>
/// <remarks>
/// <para><c>CollectionSpecId</c>: The id of the CollectionSpec in the protocol design: what the form collects.</para>
/// <para><c>PlannedActivityId</c>: The planned activity (visit) the form belongs to, which must list the CollectionSpec;
/// null for an unscheduled or log form.</para>
/// <para><c>CollectedAt</c>: When the values were collected in the trial world; not after the command is recorded.</para>
/// </remarks>
[KernelCapability("collection.open", VerbClass.Execute, Resource = nameof(OpenCollectionInstance.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(CollectionErrors),
    nameof(CollectionErrors.EnrollmentUnknown),
    nameof(CollectionErrors.CollectedAtInvalid),
    nameof(CollectionErrors.NoGoverningVersion),
    nameof(CollectionErrors.SpecUnknown))]
[RaisesKernelErrors(typeof(ObservationErrors), nameof(ObservationErrors.Locked), nameof(ObservationErrors.ParticipantWithdrawn))]
public sealed record OpenCollectionInstance(
    TenantId TenantId,
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<CollectionInstanceState>;

/// <summary>
/// Saves a form page (ADR-2622, K-TX-1 as amended): records the values of items that have none yet and corrects the
/// values of items that have one, all in one transaction under the form instance's advisory lock. Each value names the
/// revision it was edited from; one stale revision refuses the whole save (409), so a stale browser never overwrites a
/// value it did not see. A value equal to the current one is left as it is. Structural checks (the item exists, the
/// value's kind, UCUM unit and code-system version) refuse the save with 422 and an error per item; edit checks run after
/// commit. Refused when a lock covers the form or any of its values (spec §10.5). After the participant's withdrawal a
/// save that only corrects values captured before it is accepted, and one with a new value is refused whole
/// (<see cref="ObservationErrors.ParticipantWithdrawn"/>).
/// </summary>
/// <remarks>
/// <para><c>EnrollmentId</c>: The form instance's enrollment; the command is authorized against it.</para>
/// <para><c>Values</c>: At most one per CollectionSpec item.</para>
/// </remarks>
[KernelCapability("collection.record", VerbClass.Execute, Resource = nameof(RecordCollectedValues.EnrollmentId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.NotFound), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(typeof(CollectionErrors), nameof(CollectionErrors.ValuesInvalid), nameof(CollectionErrors.RevisionStale))]
[RaisesKernelErrors(
    typeof(ObservationErrors), nameof(ObservationErrors.Locked), nameof(ObservationErrors.ObservedAtInvalid), nameof(ObservationErrors.ParticipantWithdrawn))]
public sealed record RecordCollectedValues(
    TenantId TenantId,
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    IReadOnlyList<CollectedValue> Values,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<CollectedValuesState>;

/// <summary>One value of a form page save.</summary>
/// <param name="ItemId">The CollectionSpec item the value is collected for; the observation records the item's concept.</param>
/// <param name="Value">The value as entered (ADR-2623).</param>
/// <param name="ExpectedRevision">The revision the user edited: 0 for an item with no value yet, otherwise its current revision.</param>
/// <param name="ObservationId">For a new value, the observation id the client chose (the kernel assigns one when null);
/// for an existing one, null or the item's observation.</param>
/// <param name="ObservedAt">When the value held, for a new value; the form's <c>CollectedAt</c> when null.</param>
public sealed record CollectedValue(
    string ItemId,
    ObservationValue Value,
    int ExpectedRevision,
    ObservationId? ObservationId = null,
    DateTimeOffset? ObservedAt = null);

/// <summary>A form instance after a command.</summary>
/// <remarks>
/// <para><c>ProtocolVersionId</c>: The protocol version that governs the form (K-TIME-8).</para>
/// <para><c>Version</c>: 1 when opened, one more per save.</para>
/// </remarks>
public sealed record CollectionInstanceState(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    long Version);

/// <summary>
/// A form page after a save: every value of the form instance, ordered by item id, so the client marks values saved only
/// from it.
/// </summary>
public sealed record CollectedValuesState(CollectionInstanceId CollectionInstanceId, long Version, IReadOnlyList<CollectedValueState> Values);

/// <summary>One value of a form instance after a save.</summary>
/// <remarks>
/// <para><c>Revision</c>: The value's current revision, the expected revision of its next edit.</para>
/// <para><c>Changed</c>: Whether this save recorded or corrected it.</para>
/// </remarks>
public sealed record CollectedValueState(string ItemId, ObservationId ObservationId, ObservationValue Value, int Revision, bool Changed);

/// <summary>Errors raised by the form instance commands (spec §8.1, ADR-2622).</summary>
public static class CollectionErrors
{
    public static readonly KernelError EnrollmentUnknown =
        new("K.Collection.EnrollmentUnknown", 404, "The enrollment does not exist.");

    public static readonly KernelError CollectedAtInvalid =
        new("K.Collection.CollectedAtInvalid", 422, "Values cannot be collected after the form is opened.");

    public static readonly KernelError NoGoverningVersion =
        new("K.Collection.NoGoverningVersion", 409, "No protocol version is in force at the participant's study site at that time.");

    public static readonly KernelError SpecUnknown =
        new("K.Collection.SpecUnknown", 422, "The governing protocol version has no such CollectionSpec, or the planned activity does not collect it.");

    public static readonly KernelError ValuesInvalid =
        new("K.Collection.ValuesInvalid", 422, "One or more values do not fit the form's CollectionSpec.");

    public static readonly KernelError RevisionStale =
        new("K.Collection.RevisionStale", 409, "The form changed since you opened it; reload it and edit the current values.");

    /// <summary>Every form instance error, for the API's documented problem set.</summary>
    public static IReadOnlyList<KernelError> All { get; } =
        [EnrollmentUnknown, CollectedAtInvalid, NoGoverningVersion, SpecUnknown, ValuesInvalid, RevisionStale];
}
