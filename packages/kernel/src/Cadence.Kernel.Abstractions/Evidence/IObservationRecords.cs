using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;

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

/// <summary>An observation's current value, every revision, its lock and its kernel audit entries.</summary>
/// <remarks>
/// <para><c>Version</c>: The version a correction names as expected.</para>
/// <para><c>Revisions</c>: Oldest first. Each is recorded until the next one is recorded (K-FACT-1).</para>
/// <para><c>Audit</c>: The kernel audit-chain entries about the observation and its lock, oldest first. This is the
/// only audit of an observation.</para>
/// </remarks>
public sealed record ObservationRecord(
    ObservationId ObservationId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ConceptRef Concept,
    string Value,
    DateTimeOffset ObservedAt,
    long Version,
    ObservationLockView? Lock,
    IReadOnlyList<ObservationRevisionView> Revisions,
    IReadOnlyList<ObservationAuditView> Audit);

/// <summary>One revision of an observation's value.</summary>
/// <remarks>
/// <para><c>RecordedUntil</c>: When the next revision superseded it; null for the current one.</para>
/// </remarks>
public sealed record ObservationRevisionView(
    int Number,
    string Value,
    string ReasonForChange,
    string RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? RecordedUntil);

/// <summary>The lock on an observation.</summary>
public sealed record ObservationLockView(DateTimeOffset LockedAt, string LockedBy, string ReasonForChange);

/// <summary>One kernel audit-chain entry about an observation or its lock.</summary>
/// <remarks>
/// <para><c>Sequence</c>: The entry's position in the tenant's hash chain.</para>
/// <para><c>Subject</c>: <c>Observation</c> or <c>ObservationLock</c>.</para>
/// </remarks>
public sealed record ObservationAuditView(
    long Sequence,
    DateTimeOffset RecordedAt,
    string ActorId,
    string Action,
    string Subject,
    long? Version,
    string ReasonForChange);
