using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Reads <paramref name="instances"/> in one batch, keyed by <see cref="CollectionInstanceId"/>; missing forms are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
        TenantId tenant,
        IReadOnlyCollection<CollectionInstanceId> instances,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instances);
        var found = new Dictionary<CollectionInstanceId, CollectionInstanceRecord>();
        foreach (var instance in instances.Where(id => !id.IsEmpty).Distinct())
        {
            if (await FindAsync(tenant, instance, cancellationToken).ConfigureAwait(false) is { } record)
            {
                found[instance] = record;
            }
        }

        return found;
    }

    /// <summary>
    /// Reads the current revision numbers for <paramref name="observations"/> in one batch; missing observations are omitted.
    /// </summary>
    async Task<IReadOnlyDictionary<ObservationId, int>> FindObservationRevisionsAsync(
        TenantId tenant,
        IReadOnlyCollection<ObservationId> observations,
        IReadOnlyCollection<CollectionInstanceId>? instances = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0 || instances is null || instances.Count == 0)
        {
            return new Dictionary<ObservationId, int>();
        }

        var wanted = observations.Where(id => !id.IsEmpty).ToHashSet();
        var records = await FindManyAsync(tenant, instances, cancellationToken).ConfigureAwait(false);
        var revisions = new Dictionary<ObservationId, int>();
        foreach (var value in records.Values.SelectMany(r => r.Values).Where(v => wanted.Contains(v.ObservationId)))
        {
            revisions[value.ObservationId] = value.Revision;
        }

        return revisions;
    }

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
/// <param name="OpenQueryCount">Un-retracted clinical queries on the form that still await an answer.</param>
/// <param name="AnsweredQueryCount">Un-retracted clinical queries on the form that have been answered and await closure.</param>
/// <param name="OpenErrorFindingCount">Active Open or Investigating Error validation findings targeting the form or its values.</param>
/// <param name="ReadinessBlockers">Diagnostic blockers preventing completeness, signature, or scope lock when non-empty.</param>
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
    int SignatureCount = 0,
    int OpenQueryCount = 0,
    int AnsweredQueryCount = 0,
    int OpenErrorFindingCount = 0,
    IReadOnlyList<string>? ReadinessBlockers = null);

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
    string? CollectionTitle = null,
    long EnrollmentVersion = 1,
    bool Complete = true,
    IReadOnlyList<string>? MissingRequired = null,
    int OpenQueryCount = 0,
    int AnsweredQueryCount = 0,
    int OpenErrorFindingCount = 0,
    IReadOnlyList<string>? ReadinessBlockers = null);

/// <summary>Shared completeness and signature/lock readiness rules for form instances.</summary>
public static class CollectionReadiness
{
    /// <summary>True only when required items are recorded, no active queries remain, and no active Error findings remain.</summary>
    public static bool IsComplete(
        int missingRequiredCount,
        int openQueryCount,
        int answeredQueryCount,
        int openErrorFindingCount) =>
        missingRequiredCount == 0
        && openQueryCount == 0
        && answeredQueryCount == 0
        && openErrorFindingCount == 0;

    /// <summary>Builds a scope readiness summary with diagnostic blockers for freeze and lock gates.</summary>
    public static ScopeReadinessSummary BuildScopeSummary(
        References.NodeRef scope,
        int incompleteFormCount,
        int openQueryCount,
        int answeredQueryCount,
        int openErrorFindingCount)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var blockers = new List<string>(4);
        if (incompleteFormCount > 0)
        {
            if (scope.Kind == References.NodeKinds.Observation)
            {
                blockers.Add("The observation holds an unrecorded placeholder value.");
            }
            else
            {
                blockers.Add(incompleteFormCount == 1
                    ? "1 covered form has missing required values."
                    : $"{incompleteFormCount} covered forms have missing required values.");
            }
        }

        if (openQueryCount > 0)
        {
            blockers.Add(openQueryCount == 1
                ? "1 open query remains in scope."
                : $"{openQueryCount} open queries remain in scope.");
        }

        if (answeredQueryCount > 0)
        {
            blockers.Add(answeredQueryCount == 1
                ? "1 answered query is awaiting monitor closure in scope."
                : $"{answeredQueryCount} answered queries are awaiting monitor closure in scope.");
        }

        if (openErrorFindingCount > 0)
        {
            blockers.Add(openErrorFindingCount == 1
                ? "1 unresolved Error validation finding remains in scope."
                : $"{openErrorFindingCount} unresolved Error validation findings remain in scope.");
        }

        return new ScopeReadinessSummary(
            scope,
            blockers.Count == 0,
            incompleteFormCount,
            openQueryCount,
            answeredQueryCount,
            openErrorFindingCount,
            blockers);
    }

    /// <summary>Formats diagnostic readiness blockers for a form instance.</summary>
    public static IReadOnlyList<string> BuildBlockers(
        IReadOnlyList<string>? missingRequired,
        int missingRequiredCount,
        int openQueryCount,
        int answeredQueryCount,
        int openErrorFindingCount)
    {
        var blockers = new List<string>(4);
        if (missingRequired is { Count: > 0 })
        {
            blockers.Add(missingRequired.Count == 1
                ? $"Required item '{missingRequired[0]}' has no recorded value."
                : $"{missingRequired.Count} required items have no recorded value: {string.Join(", ", missingRequired)}.");
        }
        else if (missingRequiredCount > 0)
        {
            blockers.Add(missingRequiredCount == 1
                ? "1 required item has no recorded value."
                : $"{missingRequiredCount} required items have no recorded value.");
        }

        if (openQueryCount > 0)
        {
            blockers.Add(openQueryCount == 1
                ? "1 open query must be answered and closed."
                : $"{openQueryCount} open queries must be answered and closed.");
        }

        if (answeredQueryCount > 0)
        {
            blockers.Add(answeredQueryCount == 1
                ? "1 answered query is awaiting monitor closure."
                : $"{answeredQueryCount} answered queries are awaiting monitor closure.");
        }

        if (openErrorFindingCount > 0)
        {
            blockers.Add(openErrorFindingCount == 1
                ? "1 active Error validation finding must be resolved, accepted, or explained."
                : $"{openErrorFindingCount} active Error validation findings must be resolved, accepted, or explained.");
        }

        return blockers;
    }
}

/// <summary>Evaluates whether a lock scope is ready to be frozen or locked.</summary>
public interface IScopeReadiness
{
    /// <summary>Evaluates readiness of <paramref name="scope"/> within <paramref name="study"/>, or null when the scope does not exist in the study.</summary>
    Task<ScopeReadinessSummary?> EvaluateAsync(
        TenantId tenant,
        StudyId study,
        Cadence.Kernel.References.NodeRef scope,
        CancellationToken cancellationToken);
}

/// <summary>Readiness diagnostics for a candidate freeze or lock scope.</summary>
public sealed record ScopeReadinessSummary(
    Cadence.Kernel.References.NodeRef Scope,
    bool Ready,
    int IncompleteFormCount,
    int OpenQueryCount,
    int AnsweredQueryCount,
    int OpenErrorFindingCount,
    IReadOnlyList<string> Blockers);

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
