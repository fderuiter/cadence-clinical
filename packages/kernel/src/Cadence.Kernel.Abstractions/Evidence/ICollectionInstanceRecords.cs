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

    /// <summary>Every form instance opened for <paramref name="enrollment"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken);

    /// <summary>Every full form instance record opened for <paramref name="enrollment"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
        TenantId tenant,
        EnrollmentId enrollment,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([]);

    /// <summary>Every full form instance record opened for <paramref name="study"/> in <paramref name="tenant"/>, oldest first.</summary>
    Task<IReadOnlyList<CollectionInstanceRecord>> ListForStudyAsync(
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([]);
}

/// <summary>Summary of an opened form instance for an enrollment's form list.</summary>
/// <param name="CollectionInstanceId">The form instance's id.</param>
/// <param name="EnrollmentId">Its participant enrollment.</param>
/// <param name="ParticipantId">The enrollment's participant.</param>
/// <param name="StudyId">The study.</param>
/// <param name="StudySiteId">The study site.</param>
/// <param name="ProtocolVersionId">The protocol version that governs the form (K-TIME-8).</param>
/// <param name="CollectionSpecId">The CollectionSpec it collects.</param>
/// <param name="PlannedActivityId">The planned activity (visit) it belongs to, when scheduled.</param>
/// <param name="CollectedAt">When its values were collected in the trial world.</param>
/// <param name="OpenedBy">Who opened the form.</param>
/// <param name="OpenedAt">When it was opened.</param>
/// <param name="Version">1 when opened, one more per save that changed a value.</param>
/// <param name="Complete">Whether every required item in the governing CollectionSpec has a value.</param>
/// <param name="ItemCount">Total items on the form.</param>
/// <param name="RecordedCount">Items on the form that currently hold a recorded observation.</param>
/// <param name="CollectionTitle">The CollectionSpec's title in the governing protocol version, when present.</param>
/// <param name="MissingRequiredCount">Required items on the form that still lack a recorded observation.</param>
/// <param name="VerifiedCount">Recorded items whose current revision carries an active source verification assertion.</param>
/// <param name="UnverifiedCount">Recorded items whose current revision is not source-verified.</param>
/// <param name="Locked">Whether an active hard lock covers the form or any of its covering scopes.</param>
/// <param name="Frozen">Whether an active data freeze covers the form or any of its covering scopes.</param>
/// <param name="LockCount">Active locks and freezes covering the form or its values.</param>
/// <param name="Signed">Whether at least one signature covers the form's current content hash.</param>
/// <param name="SignatureSuperseded">Whether the form has signatures, all superseded by later value changes.</param>
/// <param name="SignatureCount">Total signatures recorded over the form instance.</param>
public sealed record CollectionInstanceSummary(
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
    bool Complete,
    int ItemCount,
    int RecordedCount,
    string? CollectionTitle = null,
    int MissingRequiredCount = 0,
    int VerifiedCount = 0,
    int UnverifiedCount = 0,
    bool Locked = false,
    bool Frozen = false,
    int LockCount = 0,
    bool Signed = false,
    bool SignatureSuperseded = false,
    int SignatureCount = 0);

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
/// <para><c>ContentHash</c>: The hash a signature over the form's current values would bind to (#5463); null while the
/// form has no values.</para>
/// <para><c>CollectionTitle</c>: The CollectionSpec's title in the governing protocol version, when present.</para>
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
    IReadOnlyList<FormAttestationView> Attestations,
    string? ContentHash,
    string? CollectionTitle = null);

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
/// <para><c>Superseded</c>: True once a correction or a new value changed the form after it was signed: the signature
/// stands for the revisions it signed, not for the form as it is now (#5463).</para>
/// <para><c>SignedValues</c>: The revisions it signed, ordered by item id, rebuilt from the values' history; null in the
/// rare case they cannot be rebuilt to the signed hash (a save recorded at the same instant as the signature).</para>
/// </remarks>
public sealed record FormAttestationView(
    AttestationId AttestationId,
    AttestationMeaning Meaning,
    string SignedBy,
    DateTimeOffset SignedAt,
    string ContentHash,
    string Reason,
    bool Superseded,
    IReadOnlyList<SignedValueView>? SignedValues);

/// <summary>One value as a signature signed it.</summary>
public sealed record SignedValueView(string ItemId, ObservationId ObservationId, int Revision, ObservationValue Value);
