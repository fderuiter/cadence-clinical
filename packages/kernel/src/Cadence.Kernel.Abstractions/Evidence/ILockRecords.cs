using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

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

    /// <summary>
    /// Evaluates whether <paramref name="scope"/> in <paramref name="study"/> is ready for a <see cref="LockKind.Locked"/>
    /// assertion across all six readiness gates: open queries, missing required collection items, unverified observation
    /// revisions, unsigned or superseded populated forms, unopened due/closed scheduled visits, and unresolved Error
    /// validation findings. Returns null when <paramref name="study"/> or <paramref name="scope"/> does not exist in
    /// <paramref name="study"/>.
    /// </summary>
    Task<LockReadinessSnapshot?> ReadReadinessAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken) =>
        Task.FromResult<LockReadinessSnapshot?>(null);

    /// <summary>
    /// Pre-checks whether <paramref name="scope"/> exists in <paramref name="study"/>, whether <paramref name="kind"/>
    /// is already held at <paramref name="scope"/>, and (for <see cref="LockKind.Locked"/>) whether <paramref name="scope"/>
    /// is ready, before recording a signature attestation.
    /// </summary>
    Task<LockApplyPrecheck> PrecheckApplyAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        LockKind kind,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LockApplyPrecheck(ScopeExistsInStudy: true, AlreadyLocked: false, Readiness: null));

    /// <summary>
    /// Checks whether any active write-denying lock (<see cref="LockKind.Frozen"/> or <see cref="LockKind.Locked"/>)
    /// covers <paramref name="studyCodeOrId"/> and any optional <paramref name="siteCodeOrId"/>,
    /// <paramref name="subjectCode"/>, or <paramref name="enrollmentId"/> within <paramref name="tenant"/>.
    /// </summary>
    Task<ScopeWriteLockCheck> CheckScopeWriteLockAsync(
        TenantId tenant,
        string studyCodeOrId,
        string? siteCodeOrId = null,
        string? subjectCode = null,
        Guid? enrollmentId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null));
}

/// <summary>Result of checking whether any active write-denying lock covers a study, site, or enrollment scope.</summary>
public sealed record ScopeWriteLockCheck(
    bool StudyExists,
    bool Locked,
    LockKind? LockKind,
    string? ScopeKind);

/// <summary>What blocks a <see cref="LockKind.Locked"/> assertion at a target scope.</summary>
public enum LockBlockerKind
{
    /// <summary>An unretracted <see cref="AssertionKind.Queried"/> assertion on an observation in scope.</summary>
    OpenQuery,

    /// <summary>A required <c>CollectionItem</c> on an opened <c>CollectionInstance</c> in scope with no recorded value.</summary>
    MissingRequiredItem,

    /// <summary>A recorded observation in scope whose current revision lacks an unretracted <see cref="AssertionKind.Verified"/> assertion.</summary>
    UnverifiedValue,

    /// <summary>A populated <c>CollectionInstance</c> in scope lacking a current <c>Approved</c> or <c>Responsibility</c> signature (including forms with only superseded signatures).</summary>
    UnsignedForm,

    /// <summary>A due or closed scheduled visit on an active enrollment in scope with an unopened form.</summary>
    UnopenedVisit,

    /// <summary>An active (<see cref="FindingStatus.Open"/> or <see cref="FindingStatus.Investigating"/>) <see cref="ValidationSeverity.Error"/> validation finding in scope.</summary>
    ErrorFinding,
}

/// <summary>One unresolved blocker preventing a <see cref="LockKind.Locked"/> assertion at a scope.</summary>
public sealed record LockReadinessBlocker(
    LockBlockerKind Kind,
    string Owner,
    NodeRef Target,
    StudySiteId? StudySiteId,
    EnrollmentId? EnrollmentId,
    CollectionInstanceId? CollectionInstanceId,
    ObservationId? ObservationId,
    AssertionId? QueryAssertionId,
    string? SubjectCode,
    string? CollectionSpecId,
    string? CollectionTitle,
    string? ItemId,
    string? ItemLabel,
    string? QueryText,
    bool Answered,
    bool Blinded,
    string NextAction,
    string NextCapability,
    NodeRef NextTarget,
    ValidationFindingId? FindingId = null,
    string? RuleId = null,
    string? FindingMessage = null,
    int? CurrentRevision = null,
    bool SignatureSuperseded = false,
    string? PlannedActivityId = null,
    string? PlannedActivityName = null,
    DateOnly? PlannedDate = null,
    DateOnly? WindowEnd = null);

/// <summary>Readiness snapshot of a lock scope in a study.</summary>
public sealed record LockReadinessSnapshot(
    StudyId StudyId,
    string StudyCode,
    NodeRef Scope,
    bool ReadyForLock,
    bool CanFreeze,
    int OpenQueryCount,
    int MissingRequiredItemCount,
    IReadOnlyList<LockReadinessBlocker> Blockers,
    Explanation Explanation,
    int OpenErrorFindingCount = 0,
    int UnverifiedValueCount = 0,
    int UnsignedFormCount = 0,
    int UnopenedVisitCount = 0);

/// <summary>Pre-check result evaluated before recording an electronic signature for <c>ApplyLock</c>.</summary>
public sealed record LockApplyPrecheck(
    bool ScopeExistsInStudy,
    bool AlreadyLocked,
    LockReadinessSnapshot? Readiness);

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
