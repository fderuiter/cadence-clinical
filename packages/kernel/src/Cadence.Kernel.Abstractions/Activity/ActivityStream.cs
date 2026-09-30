using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Activity;

/// <summary>The event families of spec §5.2, used to filter the activity stream.</summary>
public enum EventFamily
{
    Knowledge,
    Execution,
    Evidence,
    Trust,
    Quality,
    Intelligence,

    /// <summary>An event type not yet given a family. <c>KernelEventFamilies</c> tests keep this empty.</summary>
    Other,
}

/// <summary>The family of each kernel event type (spec §5.2). Every event type the kernel raises has one.</summary>
public static class KernelEventFamilies
{
    private static readonly FrozenDictionary<string, EventFamily> ByType = new Dictionary<string, EventFamily>(StringComparer.Ordinal)
    {
        ["TenantProvisioned"] = EventFamily.Execution,
        ["OrganizationRegistered"] = EventFamily.Execution,
        ["CodeSystemRegistered"] = EventFamily.Knowledge,
        ["CodeSystemVersionImported"] = EventFamily.Knowledge,
        ["ConceptDefined"] = EventFamily.Knowledge,
        ["ConceptRevised"] = EventFamily.Knowledge,
        ["ConceptDeprecated"] = EventFamily.Knowledge,
        ["ConceptMapped"] = EventFamily.Knowledge,
        ["ConceptMappingReviewed"] = EventFamily.Knowledge,
        ["StandardVersionRegistered"] = EventFamily.Knowledge,
        ["StandardVersionApproved"] = EventFamily.Knowledge,
        ["StandardVersionDeprecated"] = EventFamily.Knowledge,
        ["LibraryItemDrafted"] = EventFamily.Knowledge,
        ["LibraryItemApproved"] = EventFamily.Knowledge,
        ["LibraryItemDeprecated"] = EventFamily.Knowledge,
        ["StudyStandardsDeclared"] = EventFamily.Knowledge,
        ["ProtocolVersionDrafted"] = EventFamily.Knowledge,
        ["ProtocolVersionRevised"] = EventFamily.Knowledge,
        ["ProtocolVersionSubmittedForReview"] = EventFamily.Knowledge,
        ["ProtocolVersionReturnedToDraft"] = EventFamily.Knowledge,
        ["ProtocolVersionApproved"] = EventFamily.Knowledge,
        ["ProtocolVersionWithdrawn"] = EventFamily.Knowledge,
        ["ParticipantRegistered"] = EventFamily.Execution,
        ["ParticipantScreened"] = EventFamily.Execution,
        ["ParticipantEnrolled"] = EventFamily.Execution,
        ["EnrollmentStatusChanged"] = EventFamily.Execution,
        ["ParticipantWithdrawn"] = EventFamily.Execution,
        ["ArtifactRegistered"] = EventFamily.Evidence,
        ["ArtifactVersioned"] = EventFamily.Evidence,
        ["AttestationRecorded"] = EventFamily.Trust,
        ["DecisionDenied"] = EventFamily.Trust,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every classified event type.</summary>
    public static IReadOnlyCollection<string> Types => ByType.Keys;

    /// <summary>The family of <paramref name="eventType"/>, or <see cref="EventFamily.Other"/> when it has none yet.</summary>
    public static EventFamily Of(string eventType) => ByType.GetValueOrDefault(eventType, EventFamily.Other);
}

/// <summary>
/// The reader-facing activity stream (ADR-2485, Canvas D5, PX-11): what happened in a scope, newest first,
/// filtered by family, actor and time. It is a projection of the kernel's domain events for people, separate from the
/// integration events (K-EVT-2), and every entry is checked for the reader: an entry about an object the reader may
/// not see is returned masked, never dropped.
/// </summary>
public interface IActivityStream
{
    /// <summary>A page of activity in <paramref name="tenant"/> for the current actor.</summary>
    Task<ActivityPage> ReadAsync(TenantId tenant, ActivityQuery query, CancellationToken cancellationToken);
}

/// <summary>What to read.</summary>
/// <param name="Scope">Only activity on this object or anything under it in the zoom hierarchy (a study, a site); null for the whole tenant.</param>
/// <param name="Families">Only these families; null or empty for all.</param>
/// <param name="ActorId">Only activity by this actor id.</param>
/// <param name="From">Only activity recorded at or after this time.</param>
/// <param name="Before">Continue after this entry (the previous page's <see cref="ActivityPage.Next"/>).</param>
/// <param name="Limit">Page size, from 1 to <see cref="MaxLimit"/>.</param>
public sealed record ActivityQuery(
    NodeRef? Scope = null,
    IReadOnlyCollection<EventFamily>? Families = null,
    string? ActorId = null,
    DateTimeOffset? From = null,
    ActivityCursor? Before = null,
    int Limit = 50)
{
    public const int MaxLimit = 200;
}

/// <summary>A position in the stream: entries are ordered by recorded time, then event id, newest first.</summary>
public sealed record ActivityCursor(DateTimeOffset Recorded, Guid EventId);

/// <summary>One page of activity, and where the next page starts (null on the last page).</summary>
public sealed record ActivityPage(IReadOnlyList<ActivityEntry> Entries, ActivityCursor? Next);

/// <summary>
/// One thing that happened. When the reader may not see the object, <see cref="Masked"/> says why and
/// <see cref="Subject"/> is null; the entry keeps its time, type, family and <see cref="SubjectKind"/>, so the stream
/// never silently shortens (D1).
/// </summary>
/// <param name="EventId">The domain event it projects.</param>
/// <param name="Type">The event type, for example <c>ConceptRevised</c>.</param>
/// <param name="Family">The event's family.</param>
/// <param name="Recorded">When the kernel recorded it.</param>
/// <param name="OccurredValid">When it happened in the world, when the event says.</param>
/// <param name="ActorId">Who did it.</param>
/// <param name="SubjectKind">The kind of object it was about, or null for events about no object.</param>
/// <param name="Subject">The object, pinned to the version the event produced; null when masked or absent.</param>
/// <param name="Masked">Why the object is hidden from this reader, or null when it is visible.</param>
/// <param name="CorrelationId">Groups entries caused by one request.</param>
public sealed record ActivityEntry(
    Guid EventId,
    string Type,
    EventFamily Family,
    DateTimeOffset Recorded,
    DateTimeOffset? OccurredValid,
    string ActorId,
    string? SubjectKind,
    NodeRef? Subject,
    MaskReason? Masked,
    Guid CorrelationId);
