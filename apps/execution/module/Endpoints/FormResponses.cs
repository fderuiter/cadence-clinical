using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>Body of <c>POST /api/v1/execution/forms</c>.</summary>
/// <param name="FormId">The new form's id; the kernel assigns one when absent. Retrying with the same idempotency key must send the same id.</param>
/// <param name="EnrollmentId">The participant's enrollment.</param>
/// <param name="CollectionSpecId">The CollectionSpec the form collects, from the protocol design.</param>
/// <param name="PlannedActivityId">The visit the form belongs to; null for an unscheduled or log form.</param>
/// <param name="CollectedAt">When the values were collected in the trial world.</param>
public sealed record OpenFormRequest(Guid? FormId, Guid EnrollmentId, string CollectionSpecId, string? PlannedActivityId, DateTimeOffset CollectedAt);

/// <summary>Body of <c>PUT /api/v1/execution/forms/{formId}/values</c>: one page of values.</summary>
/// <param name="Values">At most one per item.</param>
public sealed record SaveFormValuesRequest(IReadOnlyList<FormValueRequest> Values);

/// <summary>One value of a page save.</summary>
/// <param name="ItemId">The item the value is for.</param>
/// <param name="Value">The value as entered, typed by its <c>type</c> member (ADR-2623); a reasoned missing value is a null flavor.</param>
/// <param name="ExpectedRevision">The revision the user edited: 0 for an item with no value yet.</param>
/// <param name="ObservationId">For a new value, the observation id the client chose; the kernel assigns one when absent.</param>
/// <param name="ObservedAt">When the value held, for a new value; the form's collection time when absent.</param>
public sealed record FormValueRequest(string ItemId, ObservationValue? Value, int ExpectedRevision, Guid? ObservationId, DateTimeOffset? ObservedAt);

/// <summary>A form after it was opened.</summary>
/// <param name="FormId">The form's id.</param>
/// <param name="EnrollmentId">Its enrollment.</param>
/// <param name="ProtocolVersionId">The protocol version that governs it (K-TIME-8).</param>
/// <param name="CollectionSpecId">What it collects.</param>
/// <param name="PlannedActivityId">Its visit, if any.</param>
/// <param name="CollectedAt">When its values were collected.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
public sealed record FormStateResponse(
    Guid FormId, Guid EnrollmentId, Guid ProtocolVersionId, string CollectionSpecId, string? PlannedActivityId, DateTimeOffset CollectedAt, long Version)
{
    internal static FormStateResponse From(CollectionInstanceState state) =>
        new(state.CollectionInstanceId.Value, state.EnrollmentId.Value, state.ProtocolVersionId.Value, state.CollectionSpecId,
            state.PlannedActivityId, state.CollectedAt, state.Version);
}

/// <summary>A form as a form screen shows it.</summary>
/// <param name="FormId">The form's id.</param>
/// <param name="EnrollmentId">Its enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs it (K-TIME-8).</param>
/// <param name="CollectionSpecId">What it collects.</param>
/// <param name="PlannedActivityId">Its visit, if any.</param>
/// <param name="CollectedAt">When its values were collected.</param>
/// <param name="OpenedBy">Who opened it.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="Complete">Whether every required item has a value (a reasoned missing value counts).</param>
/// <param name="MissingRequired">The required items without a value, in layout order.</param>
/// <param name="Items">Every item in layout order, with its value when it has one.</param>
/// <param name="Locks">The locks in force over the form, widest scope first (spec §10.5).</param>
/// <param name="Assertions">Verifications, queries and answers about its values, oldest first (spec §8.2).</param>
/// <param name="Signatures">The signatures over it, oldest first (spec §9.5).</param>
/// <param name="ContentHash">The hash a signature over the current values binds to: send it to sign them (#5463); null while
/// the form has no values.</param>
public sealed record FormResponse(
    Guid FormId,
    Guid EnrollmentId,
    Guid ParticipantId,
    Guid StudyId,
    Guid StudySiteId,
    Guid ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long Version,
    bool Complete,
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<FormItemResponse> Items,
    IReadOnlyList<FormLockResponse> Locks,
    IReadOnlyList<FormAssertionResponse> Assertions,
    IReadOnlyList<FormSignatureResponse> Signatures,
    string? ContentHash)
{
    /// <summary>
    /// Shapes the kernel's record for a form screen: items in <paramref name="layout"/> order (the specification's order
    /// when null), then any item the layout does not name, and completeness derived from the required items.
    /// </summary>
    internal static FormResponse From(CollectionInstanceRecord record, IReadOnlyList<string>? layout)
    {
        var specification = record.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var values = record.Values.ToDictionary(v => v.ItemId, StringComparer.Ordinal);
        var order = (layout ?? [.. record.Items.Select(i => i.Id)])
            .Concat(record.Items.Select(i => i.Id))
            .Concat(record.Values.Select(v => v.ItemId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var items = order
            .Select(id => FormItemResponse.From(id, specification.GetValueOrDefault(id), values.GetValueOrDefault(id)))
            .ToList();
        var missing = items
            .Where(i => i.Required && (i.Value is null || i.Value is MissingValue { Reason: NullFlavor.NotAsked }))
            .Select(i => i.ItemId)
            .ToList();
        return new(
            record.CollectionInstanceId.Value,
            record.EnrollmentId.Value,
            record.ParticipantId.Value,
            record.StudyId.Value,
            record.StudySiteId.Value,
            record.ProtocolVersionId.Value,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.CollectedAt,
            record.OpenedBy,
            record.OpenedAt,
            record.Version,
            missing.Count == 0,
            missing,
            items,
            [.. record.Locks.Select(l => new FormLockResponse(l.LockId.Value, l.Kind.ToString(), l.Scope.ToString(), l.LockedAt, l.LockedBy, l.ReasonForChange))],
            [.. record.Assertions.Select(a => new FormAssertionResponse(
                a.AssertionId.Value, a.Kind.ToString(), a.Target.ToString(), a.Revision, a.Text, a.AssertedBy, a.AssertedAt, a.ReasonForChange, a.RetractedAt))],
            [.. record.Attestations.Select(FormSignatureResponse.From)],
            record.ContentHash);
    }
}

/// <summary>One item of a form.</summary>
/// <param name="ItemId">The item's id in the CollectionSpec.</param>
/// <param name="Concept">What it records, as <c>&lt;concept id&gt;@&lt;version&gt;</c>; null when the governing version no longer has the item.</param>
/// <param name="Unit">The UCUM unit a quantity is collected in.</param>
/// <param name="ValueKind">The kind of value it takes, when fixed.</param>
/// <param name="Required">Whether it must have a value.</param>
/// <param name="Value">Its current value, or null when it has none.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Revision">The value's current revision, to send as the expected revision of its next edit; 0 when it has no value.</param>
/// <param name="RecordedBy">Who entered the current revision.</param>
/// <param name="RecordedAt">When the current revision was entered.</param>
public sealed record FormItemResponse(
    string ItemId,
    string? Concept,
    string? Unit,
    string? ValueKind,
    bool Required,
    ObservationValue? Value,
    Guid? ObservationId,
    int Revision,
    string? RecordedBy,
    DateTimeOffset? RecordedAt)
{
    internal static FormItemResponse From(string id, Cadence.Kernel.Protocol.Design.CollectionItem? item, CollectedValueView? value) =>
        new(
            id,
            item?.Concept.ToString(),
            item?.Unit,
            item?.ExpectedKind?.ToString(),
            item?.Required ?? false,
            value?.Value,
            value?.ObservationId.Value,
            value?.Revision ?? 0,
            value?.RecordedBy,
            value?.RecordedAt);
}

/// <summary>A lock in force over a form.</summary>
/// <param name="LockId">The lock.</param>
/// <param name="Kind">Locked or frozen.</param>
/// <param name="Scope">What it covers, as a NodeRef.</param>
/// <param name="LockedAt">When it took effect.</param>
/// <param name="LockedBy">Who locked it.</param>
/// <param name="Reason">Why.</param>
public sealed record FormLockResponse(Guid LockId, string Kind, string Scope, DateTimeOffset LockedAt, string LockedBy, string Reason);

/// <summary>A verification, query or answer about a form's value.</summary>
/// <param name="AssertionId">The assertion.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Target">The observation, or the query an answer answers, as a NodeRef.</param>
/// <param name="Revision">The revision a verification judged.</param>
/// <param name="Text">A query's or an answer's text.</param>
/// <param name="AssertedBy">Who made it.</param>
/// <param name="AssertedAt">When.</param>
/// <param name="Reason">The reason for change.</param>
/// <param name="RetractedAt">When it was retracted, if it was.</param>
public sealed record FormAssertionResponse(
    Guid AssertionId, string Kind, string Target, int? Revision, string? Text, string AssertedBy, DateTimeOffset AssertedAt, string Reason, DateTimeOffset? RetractedAt);

/// <summary>Body of <c>POST /api/v1/execution/forms/{formId}/signatures</c> (#5463).</summary>
/// <param name="Meaning"><c>approved</c> for an investigator's sign-off, or <c>responsibility</c>.</param>
/// <param name="ContentHash">The form's <c>content_hash</c> as the signer read it; a form changed since is refused with 409.</param>
/// <param name="SignatureId">The signature's id, when the client chooses it; otherwise the server does.</param>
public sealed record SignFormRequest(string Meaning, string ContentHash, Guid? SignatureId);

/// <summary>A signature over a form.</summary>
/// <param name="AttestationId">The signature.</param>
/// <param name="Meaning">What the signer attested.</param>
/// <param name="SignedBy">Who signed.</param>
/// <param name="SignedAt">When.</param>
/// <param name="Reason">The signer's reason.</param>
/// <param name="ContentHash">The hash of the revisions it signed.</param>
/// <param name="Superseded">True once the form changed after it was signed: it stands for the revisions it signed, and the
/// form needs a new signature for its current values.</param>
/// <param name="SignedValues">The revisions it signed, by item id, to show beside the current values; null when they cannot
/// be rebuilt from the values' history.</param>
public sealed record FormSignatureResponse(
    Guid AttestationId,
    string Meaning,
    string SignedBy,
    DateTimeOffset SignedAt,
    string Reason,
    string ContentHash,
    bool Superseded,
    IReadOnlyList<FormSignedValueResponse>? SignedValues)
{
    internal static FormSignatureResponse From(FormAttestationView signature) =>
        new(
            signature.AttestationId.Value,
            signature.Meaning.ToString(),
            signature.SignedBy,
            signature.SignedAt,
            signature.Reason,
            signature.ContentHash,
            signature.Superseded,
            signature.SignedValues?.Select(v => new FormSignedValueResponse(v.ItemId, v.ObservationId.Value, v.Revision, v.Value)).ToList());
}

/// <summary>One value as a signature signed it.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Revision">The revision signed.</param>
/// <param name="Value">That revision's value.</param>
public sealed record FormSignedValueResponse(string ItemId, Guid ObservationId, int Revision, ObservationValue Value);

/// <summary>A form after a page save: every value it holds, so the client marks values saved only from this answer.</summary>
/// <param name="FormId">The form.</param>
/// <param name="Version">Its version after the save.</param>
/// <param name="Values">Every value of the form, ordered by item id.</param>
public sealed record FormSavedResponse(Guid FormId, long Version, IReadOnlyList<FormSavedValueResponse> Values)
{
    internal static FormSavedResponse From(CollectedValuesState state) =>
        new(state.CollectionInstanceId.Value, state.Version,
            [.. state.Values.Select(v => new FormSavedValueResponse(v.ItemId, v.ObservationId.Value, v.Value, v.Revision, v.Changed))]);
}

/// <summary>One value after a save.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ObservationId">The observation holding it.</param>
/// <param name="Value">Its current value.</param>
/// <param name="Revision">Its current revision.</param>
/// <param name="Changed">Whether this save recorded or corrected it.</param>
public sealed record FormSavedValueResponse(string ItemId, Guid ObservationId, ObservationValue Value, int Revision, bool Changed);
