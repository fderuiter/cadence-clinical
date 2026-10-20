using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cadence.Core.Domain;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// The observations collected together for one participant under one CollectionSpec version (spec §8.1, K-FACT-6): the
/// kernel side of an eCRF form instance (ADR-2622). It names the participant's enrollment, the protocol version and
/// CollectionSpec that governed the collection (K-TIME-8), and optionally the planned activity (visit) it belongs to.
/// <list type="bullet">
/// <item>Membership is insert-only: an observation joins one instance, for one item of the CollectionSpec, and never
/// leaves it (<see cref="CollectionInstanceMember"/>).</item>
/// <item>It carries no layout, item order, workflow status or UI state; those stay with the application's form.</item>
/// <item>It is the form-instance lock scope (spec §10.5) and the target of form signatures (spec §9.5).</item>
/// <item>One command records and corrects its members together under its advisory lock (K-TX-1 as amended); each save
/// that changes a value counts in <see cref="Saves"/>, so the instance's version is one more than its saves.</item>
/// </list>
/// </summary>
public class CollectionInstance : AggregateRoot<CollectionInstanceId>
{
    /// <summary>The longest protocol element id (a CollectionSpec, item or planned activity id) the kernel stores.</summary>
    public const int ElementIdMaxLength = 128;

    // Values hash in the shape the API and events give them (ADR-2623), so an auditor can recompute a signed hash.
    private static readonly JsonSerializerOptions ValueJson = new(JsonSerializerDefaults.Web);

    private readonly List<CollectionInstanceMember> _members = [];

    protected CollectionInstance() { }

    private CollectionInstance(
        CollectionInstanceId id,
        Enrollment enrollment,
        ProtocolVersionId protocolVersionId,
        string collectionSpecId,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        string openedBy,
        DateTimeOffset openedAt)
        : base(id)
    {
        TenantId = enrollment.TenantId;
        StudyId = enrollment.StudyId;
        StudySiteId = enrollment.StudySiteId;
        EnrollmentId = enrollment.Id;
        ParticipantId = enrollment.ParticipantId;
        ProtocolVersionId = protocolVersionId;
        CollectionSpecId = collectionSpecId;
        PlannedActivityId = plannedActivityId;
        CollectedAt = KernelTimestamps.ToStoredPrecision(collectedAt);
        OpenedBy = openedBy;
        OpenedAt = KernelTimestamps.ToStoredPrecision(openedAt);
        LastSavedAt = OpenedAt;
    }

    public TenantId TenantId { get; private set; }

    public StudyId StudyId { get; private set; }

    /// <summary>The enrollment's study site, kept so the instance's covering lock scopes are read without a join.</summary>
    public StudySiteId StudySiteId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ParticipantId ParticipantId { get; private set; }

    /// <summary>The protocol version in force at the participant's study site when the values were collected (K-TIME-8).</summary>
    public ProtocolVersionId ProtocolVersionId { get; private set; }

    /// <summary>The id of the CollectionSpec in that version's design: what to collect.</summary>
    public string CollectionSpecId { get; private set; } = string.Empty;

    /// <summary>The id of the planned activity (visit) in that design, or null for an unscheduled or log form.</summary>
    public string? PlannedActivityId { get; private set; }

    /// <summary>When the values were collected in the trial world (valid time); a new member is observed then by default.</summary>
    public DateTimeOffset CollectedAt { get; private set; }

    public string OpenedBy { get; private set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>When the latest save committed.</summary>
    public DateTimeOffset LastSavedAt { get; private set; }

    /// <summary>
    /// How many saves changed a value. Counting them, rather than relying on <see cref="LastSavedAt"/>, makes every save a
    /// new instance version even when two commit at the same clock tick.
    /// </summary>
    public int Saves { get; private set; }

    /// <summary>The observations collected in this instance, one per CollectionSpec item.</summary>
    public IReadOnlyList<CollectionInstanceMember> Members => _members;

    /// <summary>
    /// Opens a form instance for <paramref name="enrollment"/> under the CollectionSpec <paramref name="collectionSpecId"/>
    /// of <paramref name="protocolVersionId"/>, which the caller resolved as governing at <paramref name="collectedAt"/>.
    /// </summary>
    public static CollectionInstance Open(
        CollectionInstanceId id,
        Enrollment enrollment,
        ProtocolVersionId protocolVersionId,
        string collectionSpecId,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        string openedBy,
        DateTimeOffset openedAt)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionSpecId);
        return new CollectionInstance(id, enrollment, protocolVersionId, collectionSpecId, plannedActivityId, collectedAt, openedBy, openedAt);
    }

    /// <summary>The member observation collected for <paramref name="itemId"/>, or null when the item has none yet.</summary>
    public CollectionInstanceMember? MemberFor(string itemId) =>
        _members.FirstOrDefault(m => string.Equals(m.ItemId, itemId, StringComparison.Ordinal));

    /// <summary>Whether <paramref name="observation"/> was collected in this instance.</summary>
    public bool Contains(ObservationId observation) => _members.Exists(m => m.ObservationId == observation);

    /// <summary>Adds <paramref name="observation"/> as the value collected for <paramref name="itemId"/>; an item has one member.</summary>
    public void Join(string itemId, ObservationId observation, DateTimeOffset joinedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (MemberFor(itemId) is not null || Contains(observation))
        {
            throw new InvalidOperationException($"Item {itemId} or observation {observation} is already a member of {Id}.");
        }

        _members.Add(new CollectionInstanceMember(this, itemId, observation, KernelTimestamps.ToStoredPrecision(joinedAt)));
    }

    /// <summary>
    /// The content hash a form signature binds to (spec §9.5, 21 CFR 11.70, #5463): the kernel content hash of
    /// <c>{"values": [{"observation_id", "revision", "value"}, …]}</c>, one entry per member ordered by observation id. A
    /// correction is a new revision, so it changes the hash, and a signature given before it no longer matches the form
    /// while still naming the revisions it signed.
    /// </summary>
    public static string ContentHashOf(IEnumerable<CollectionContentEntry> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var entries = new JsonArray();
        foreach (var value in values.OrderBy(v => v.ObservationId.ToString(), StringComparer.Ordinal))
        {
            entries.Add(new JsonObject
            {
                ["observation_id"] = value.ObservationId.ToString(),
                ["revision"] = value.Revision,
                ["value"] = JsonSerializer.SerializeToNode(value.Value, ValueJson),
            });
        }

        return Trust.ContentHash.Compute(new JsonObject { ["values"] = entries }.ToJsonString());
    }

    /// <summary>Records that a save committed at <paramref name="savedAt"/>.</summary>
    public void Saved(DateTimeOffset savedAt)
    {
        LastSavedAt = KernelTimestamps.ToStoredPrecision(savedAt);
        Saves++;
    }
}

/// <summary>
/// One observation collected in a <see cref="CollectionInstance"/>, for one item of its CollectionSpec. Insert-only in the
/// database: membership never changes (K-FACT-6).
/// </summary>
public class CollectionInstanceMember
{
    protected CollectionInstanceMember() { }

    internal CollectionInstanceMember(CollectionInstance instance, string itemId, ObservationId observationId, DateTimeOffset joinedAt)
    {
        Id = Guid.CreateVersion7(joinedAt);
        TenantId = instance.TenantId;
        CollectionInstanceId = instance.Id;
        ItemId = itemId;
        ObservationId = observationId;
        JoinedAt = joinedAt;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public CollectionInstanceId CollectionInstanceId { get; private set; }

    /// <summary>The CollectionSpec item the observation's value was collected for.</summary>
    public string ItemId { get; private set; } = string.Empty;

    public ObservationId ObservationId { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }
}

/// <summary>One value as a form signature hashes it: the observation, the revision signed and that revision's value.</summary>
public sealed record CollectionContentEntry(ObservationId ObservationId, int Revision, ObservationValue Value);
