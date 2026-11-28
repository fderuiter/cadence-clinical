using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Protocol.Design;

namespace Cadence.Kernel.Evidence;

/// <summary>Header metadata of an opened form instance without member values, assertions, codelists, or signatures.</summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="CollectedAt">When its values were collected in the trial world.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
public sealed record CollectionInstanceHeader(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    DateTimeOffset OpenedAt,
    long Version)
{
    /// <summary>Initializes a scalar header with study, site, enrollment, specification, and planned activity identifiers.</summary>
    public CollectionInstanceHeader(
        CollectionInstanceId collectionInstanceId,
        StudyId studyId,
        StudySiteId studySiteId,
        EnrollmentId enrollmentId,
        string collectionSpecId,
        string? plannedActivityId)
        : this(
            collectionInstanceId,
            enrollmentId,
            ParticipantId.Empty,
            studyId,
            studySiteId,
            ProtocolVersionId.Empty,
            collectionSpecId,
            plannedActivityId,
            default,
            default,
            1)
    {
    }

    /// <summary>Projects header metadata from <paramref name="record"/>.</summary>
    public static CollectionInstanceHeader From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.ParticipantId,
            record.StudyId,
            record.StudySiteId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.CollectedAt,
            record.OpenedAt,
            record.Version);
    }
}

/// <summary>
/// Current revision number and treatment-blinding flag for a kernel observation, read in batch without loading revision
/// history, assertions, or signatures.
/// </summary>
/// <param name="Revision">Current observation revision number.</param>
/// <param name="IsBlinded">True when the observation classifies as blinded treatment data (<c>K-AUTH-4</c>).</param>
public readonly record struct CollectionObservationProjection(int Revision, bool IsBlinded = false);

/// <summary>
/// Targeted projection of a form instance for query commands and blank-item checks: its scalar header, lineage versions,
/// whether an active <see cref="LockKind.Locked"/> assertion covers the form or target observation, and the matched
/// item/observation member's current revision.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="EnrollmentId">The participant enrollment.</param>
/// <param name="CollectionSpecId">The CollectionSpec identifier.</param>
/// <param name="PlannedActivityId">The planned activity (visit) identifier, when scheduled.</param>
/// <param name="Version">Current version index of the form instance.</param>
/// <param name="EnrollmentVersion">Current version index of the governing enrollment.</param>
/// <param name="IsLocked">True when an active <see cref="LockKind.Locked"/> assertion covers the form or target observation.</param>
/// <param name="ItemId">Item identifier of the matched member, when present.</param>
/// <param name="ObservationId">Observation identifier of the matched member, when present.</param>
/// <param name="Revision">Current revision number of the matched member, when present.</param>
/// <param name="IsUnaskedPlaceholder">True when the matched member's current value is <see cref="NullFlavor.NotAsked"/>.</param>
public sealed record CollectionQueryTarget(
    CollectionInstanceId CollectionInstanceId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    EnrollmentId EnrollmentId,
    string CollectionSpecId,
    string? PlannedActivityId,
    long Version,
    long EnrollmentVersion,
    bool IsLocked,
    string? ItemId = null,
    ObservationId? ObservationId = null,
    int? Revision = null,
    bool IsUnaskedPlaceholder = false);

/// <summary>
/// Narrow projection of a form instance for query worklists and query step responses: header metadata, current
/// observation and item revisions, and blinding classification without assertions, locks, codelists, or signatures.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="ObservationRevisions">Current revision number per unmasked observation on the form.</param>
/// <param name="ItemRevisions">Current revision number per item identifier on the form.</param>
/// <param name="BlindedItemIds">Items classified as blinded treatment data (K-AUTH-4).</param>
/// <param name="BlindedObservationIds">Observations classified as blinded treatment data (K-AUTH-4).</param>
public sealed record CollectionInstanceQueryTarget(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    IReadOnlyDictionary<ObservationId, int> ObservationRevisions,
    IReadOnlyDictionary<string, int> ItemRevisions,
    IReadOnlySet<string>? BlindedItemIds = null,
    IReadOnlySet<ObservationId>? BlindedObservationIds = null)
{
    /// <summary>True when a query over <paramref name="itemId"/> / <paramref name="observationId"/> targets blinded treatment data.</summary>
    public bool IsQueryBlinded(string itemId, ObservationId observationId) =>
        (BlindedItemIds?.Contains(itemId) ?? false)
        || (BlindedObservationIds?.Contains(observationId) ?? false);

    /// <summary>Projects query target metadata from <paramref name="record"/>.</summary>
    public static CollectionInstanceQueryTarget From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.StudyId,
            record.StudySiteId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.Values.Where(v => !v.ObservationId.IsEmpty).ToDictionary(v => v.ObservationId, v => v.Revision),
            record.Values.ToDictionary(v => v.ItemId, v => v.Revision, StringComparer.Ordinal),
            record.BlindedItemIds,
            record.BlindedObservationIds);
    }
}

/// <summary>One member observation and its current revision on a <see cref="CollectionInstanceScopeState"/>.</summary>
/// <param name="ItemId">The CollectionSpec item identifier.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Value">The observation's current typed value.</param>
/// <param name="Revision">The observation's current revision number.</param>
public sealed record CollectionScopeValue(
    string ItemId,
    ObservationId ObservationId,
    ObservationValue Value,
    int Revision);

/// <summary>
/// Narrow projection of a form instance for scope locking, lineage linking, and revision checks without protocol
/// design parsing, codelists, verification/query assertions, or signatures.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="EnrollmentVersion">Current version index of the governing enrollment for lineage linking.</param>
/// <param name="Values">Current member values and revisions, ordered by item id.</param>
/// <param name="Locks">Active locks and freezes covering the form or its values, widest scope first.</param>
public sealed record CollectionInstanceScopeState(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    long Version,
    long EnrollmentVersion,
    IReadOnlyList<CollectionScopeValue> Values,
    IReadOnlyList<LockView> Locks)
{
    /// <summary>Projects scope state from <paramref name="record"/>.</summary>
    public static CollectionInstanceScopeState From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.StudyId,
            record.StudySiteId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.PlannedActivityId,
            record.Version,
            record.EnrollmentVersion,
            [.. record.Values.Select(v => new CollectionScopeValue(v.ItemId, v.ObservationId, v.Value, v.Revision))],
            record.Locks);
    }
}

/// <summary>One member value of a <see cref="CollectionRuleInput"/> for edit-check evaluation.</summary>
/// <param name="ItemId">The CollectionSpec item identifier.</param>
/// <param name="ObservationId">The observation holding the value.</param>
/// <param name="Value">The observation's current typed value.</param>
public sealed record CollectionRuleValue(
    string ItemId,
    ObservationId ObservationId,
    ObservationValue Value);

/// <summary>
/// Narrow projection of a form instance for cross-form edit-check evaluation and observed-value comparison without
/// observation revision histories, assertions, locks, codelists, or signatures.
/// </summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="StudyId">The study.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="OpenedAt">When the form was opened.</param>
/// <param name="Items">The CollectionSpec's items in the governing protocol version.</param>
/// <param name="Values">Current member values, ordered by item id.</param>
/// <param name="CollectionTitle">The CollectionSpec's title in the governing protocol version, when present.</param>
public sealed record CollectionRuleInput(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    DateTimeOffset OpenedAt,
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<CollectionRuleValue> Values,
    string? CollectionTitle = null)
{
    /// <summary>Projects rule input from <paramref name="record"/>.</summary>
    public static CollectionRuleInput From(CollectionInstanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(
            record.CollectionInstanceId,
            record.EnrollmentId,
            record.StudyId,
            record.ProtocolVersionId,
            record.CollectionSpecId,
            record.OpenedAt,
            record.Items,
            [.. record.Values.Select(v => new CollectionRuleValue(v.ItemId, v.ObservationId, v.Value))],
            record.CollectionTitle);
    }
}
