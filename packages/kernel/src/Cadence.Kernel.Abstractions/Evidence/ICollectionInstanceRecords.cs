using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Reads a form instance in one call (spec §8.1, ADR-2622): its values with their revisions, the assertions about them,
/// the signatures over the form and the locks in force. Reads are tenant-scoped by Row-Level Security and never change
/// state. Kernel persistence implements it (<c>AddKernelPersistence</c>).
/// </summary>
public interface ICollectionInstanceRecords
{
    /// <summary>The form instance, or null when it does not exist in <paramref name="tenant"/>.</summary>
    Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken);
}

/// <summary>A form instance and everything a form screen shows about it.</summary>
/// <remarks>
/// <para><c>ProtocolVersionId</c>: The protocol version that governs the form (K-TIME-8).</para>
/// <para><c>Version</c>: 1 when opened, one more per save that changed a value.</para>
/// <para><c>Items</c>: The CollectionSpec's items in the governing version, in the specification's order: what the form
/// collects, which items are required and the kind of value each takes. Empty when that version no longer has the
/// specification.</para>
/// <para><c>Values</c>: One per CollectionSpec item that has a value, ordered by item id.</para>
/// <para><c>Locks</c>: The locks in force at the form's study, study site, enrollment, the form itself or one of its
/// values, widest scope first (spec §10.5).</para>
/// <para><c>Assertions</c>: Verifications, queries and answers about the form's values, retracted ones included,
/// oldest first (spec §8.2).</para>
/// <para><c>Attestations</c>: The signatures over the form instance, oldest first (spec §9.5).</para>
/// </remarks>
public sealed record CollectionInstanceRecord(
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt,
    long Version,
    IReadOnlyList<CollectionItem> Items,
    IReadOnlyList<CollectedValueView> Values,
    IReadOnlyList<LockView> Locks,
    IReadOnlyList<AssertionView> Assertions,
    IReadOnlyList<FormAttestationView> Attestations);

/// <summary>One value of a form instance.</summary>
/// <remarks>
/// <para><c>Revision</c>: The value's current revision, the expected revision of its next edit.</para>
/// <para><c>RecordedBy</c> and <c>RecordedAt</c>: Who entered the current revision, and when.</para>
/// </remarks>
public sealed record CollectedValueView(
    string ItemId,
    ObservationId ObservationId,
    ConceptRef Concept,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    int Revision,
    string RecordedBy,
    DateTimeOffset RecordedAt);

/// <summary>A signature over a form instance (spec §9.5).</summary>
/// <remarks>
/// <para><c>ContentHash</c>: The hash of the member revisions it signed; a later correction leaves it bound to them.</para>
/// </remarks>
public sealed record FormAttestationView(
    AttestationId AttestationId,
    AttestationMeaning Meaning,
    string SignedBy,
    DateTimeOffset SignedAt,
    string ContentHash,
    string Reason);
