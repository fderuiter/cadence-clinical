using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Reads an observation with its correction history and its audit trail (spec §8.1, ADR-2620). Reads are tenant-scoped
/// by Row-Level Security and never change state. Kernel persistence implements it (<c>AddKernelPersistence</c>).
/// </summary>
public interface IObservationRecords
{
    /// <summary>The observation, or null when it does not exist in <paramref name="tenant"/>.</summary>
    Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken);
}

/// <summary>
/// An observation's current value, every revision, the locks that cover it, the assertions about it and its kernel
/// audit entries.
/// </summary>
/// <remarks>
/// <para><c>Version</c>: The version a correction names as expected.</para>
/// <para><c>Locks</c>: The locks in force at the observation's study, study site, enrollment, form instance or the
/// observation itself, widest scope first (spec §10.5). Empty when nothing covers it.</para>
/// <para><c>Revisions</c>: Oldest first. Each is recorded until the next one is recorded (K-FACT-1).</para>
/// <para><c>Assertions</c>: Verifications, queries and answers about the observation, retracted ones included, oldest
/// first (spec §8.2).</para>
/// <para><c>Audit</c>: The kernel audit-chain entries about the observation, oldest first. This is the only audit of
/// an observation's value.</para>
/// </remarks>
public sealed record ObservationRecord(
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ConceptRef Concept,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    long Version,
    IReadOnlyList<LockView> Locks,
    IReadOnlyList<ObservationRevisionView> Revisions,
    IReadOnlyList<AssertionView> Assertions,
    IReadOnlyList<ObservationAuditView> Audit,
    CollectionInstanceId? CollectionInstanceId = null,
    string? ItemId = null,
    ObservationValueKind? ExpectedValueKind = null,
    string? ExpectedUnit = null,
    CodeSystemVersionId? ExpectedCodeSystemVersionId = null);

/// <summary>One revision of an observation's value.</summary>
/// <remarks>
/// <para><c>RecordedUntil</c>: When the next revision superseded it; null for the current one.</para>
/// </remarks>
public sealed record ObservationRevisionView(
    int Number,
    ObservationValue Value,
    string ReasonForChange,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? RecordedUntil);

/// <summary>A lock in force (spec §10.5).</summary>
/// <remarks>
/// <para><c>Scope</c>: The study, study site, enrollment, form instance or observation it covers.</para>
/// <para><c>AttestationId</c>: The signature it was applied with.</para>
/// </remarks>
public sealed record LockView(
    AssertionId LockId,
    LockKind Kind,
    NodeRef Scope,
    AttestationId AttestationId,
    DateTimeOffset LockedAt,
    string LockedBy,
    string ReasonForChange);

/// <summary>One assertion about an observation, or about another assertion about it (spec §8.2).</summary>
/// <remarks>
/// <para><c>Target</c>: The observation, or the query an answer answers.</para>
/// <para><c>Revision</c>: The revision a verification judged; null for a query or an answer.</para>
/// <para><c>Text</c>: A query's or answer's text.</para>
/// <para><c>RetractedBy</c>: The assertion that retracted it, or null while it holds.</para>
/// </remarks>
public sealed record AssertionView(
    AssertionId AssertionId,
    AssertionKind Kind,
    NodeRef Target,
    int? Revision,
    string? Text,
    string AssertedBy,
    DateTimeOffset AssertedAt,
    string ReasonForChange,
    AssertionId? RetractedBy,
    DateTimeOffset? RetractedAt);

/// <summary>One kernel audit-chain entry about an observation.</summary>
/// <remarks>
/// <para><c>Sequence</c>: The entry's position in the tenant's hash chain.</para>
/// <para><c>Subject</c>: <c>Observation</c>.</para>
/// </remarks>
public sealed record ObservationAuditView(
    long Sequence,
    DateTimeOffset RecordedAt,
    string ActorId,
    string Action,
    string Subject,
    long? Version,
    string ReasonForChange);
