using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// Reads study locks and their release history from canonical kernel assertions (spec §10.5, ADR-2621). Reads are
/// tenant-scoped by Row-Level Security and evaluate bitemporal cuts without mutating state.
/// </summary>
public interface ILockRecords
{
    /// <summary>
    /// Reads one lock assertion in <paramref name="tenant"/>, including its release when retracted, or null when no
    /// lock assertion has <paramref name="lockId"/>.
    /// </summary>
    Task<StudyLockEntry?> FindAsync(TenantId tenant, AssertionId lockId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists locks recorded in <paramref name="study"/> as of <c>(validAt, knownAt)</c>, ordered from widest scope to
    /// narrowest and then by assertion timestamp. Returns null when <paramref name="study"/> does not exist in
    /// <paramref name="tenant"/>.
    /// </summary>
    Task<StudyLockList?> ListByStudyAsync(
        TenantId tenant,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        bool activeOnly,
        CancellationToken cancellationToken);
}

/// <summary>The locks recorded for one study at a bitemporal cut.</summary>
/// <param name="StudyId">The study's canonical identifier.</param>
/// <param name="StudyCode">The study's short code.</param>
/// <param name="ValidAt">Valid-time coordinate evaluated for the snapshot.</param>
/// <param name="KnownAt">Recorded-time coordinate evaluated for the snapshot.</param>
/// <param name="Locks">Every lock assertion in the study matching the query.</param>
public sealed record StudyLockList(
    StudyId StudyId,
    string StudyCode,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<StudyLockEntry> Locks);

/// <summary>One lock assertion and any retracting release assertion recorded at the evaluated bitemporal cut.</summary>
/// <param name="LockId">The lock assertion's identifier.</param>
/// <param name="StudyId">The study the lock belongs to.</param>
/// <param name="EnrollmentId">The enrollment when the scope is at or below an enrollment; null for study or study site.</param>
/// <param name="StudySiteId">The study site when the scope is at or below a study site; null for a study lock.</param>
/// <param name="SubjectCode">The enrollment's pseudonymous subject code when applicable; null otherwise.</param>
/// <param name="Kind">Whether the scope is <see cref="LockKind.Frozen"/> or <see cref="LockKind.Locked"/>.</param>
/// <param name="Scope">The locked object (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c> or <c>observation</c>).</param>
/// <param name="BlindedScope">True when the locked observation records a blinded treatment or intervention concept.</param>
/// <param name="AttestationId">The electronic signature that attested applying the lock.</param>
/// <param name="AttestationMeaning">The signature meaning recorded when the lock was applied.</param>
/// <param name="LockedAt">When the lock was asserted.</param>
/// <param name="LockedBy">Who asserted the lock.</param>
/// <param name="ReasonForChange">Why the lock was applied.</param>
/// <param name="Released">True when a release assertion has retracted this lock as of the evaluated cut.</param>
/// <param name="ReleaseAssertionId">The retracting assertion's identifier when released; null while active.</param>
/// <param name="ReleaseAttestationId">The electronic signature that attested releasing the lock; null while active.</param>
/// <param name="ReleasedAt">When the lock was released; null while active.</param>
/// <param name="ReleasedBy">Who released the lock; null while active.</param>
/// <param name="ReleaseReason">Why the lock was released; null while active.</param>
public sealed record StudyLockEntry(
    AssertionId LockId,
    StudyId StudyId,
    EnrollmentId? EnrollmentId,
    StudySiteId? StudySiteId,
    string? SubjectCode,
    LockKind Kind,
    NodeRef Scope,
    bool BlindedScope,
    AttestationId AttestationId,
    AttestationMeaning? AttestationMeaning,
    DateTimeOffset LockedAt,
    string LockedBy,
    string ReasonForChange,
    bool Released,
    AssertionId? ReleaseAssertionId,
    AttestationId? ReleaseAttestationId,
    DateTimeOffset? ReleasedAt,
    string? ReleasedBy,
    string? ReleaseReason);
