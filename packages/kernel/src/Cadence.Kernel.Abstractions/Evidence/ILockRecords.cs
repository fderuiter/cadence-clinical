using System;
using System.Collections.Generic;
using System.Text.Json;
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
    /// assertion under <see cref="LockMilestone.InterimCut"/>.
    /// </summary>
    Task<LockReadinessSnapshot?> ReadReadinessAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        CancellationToken cancellationToken) =>
        Task.FromResult<LockReadinessSnapshot?>(null);

    /// <summary>
    /// Evaluates whether <paramref name="scope"/> in <paramref name="study"/> is ready for a <see cref="LockKind.Locked"/>
    /// assertion under <paramref name="milestone"/>. Returns null when <paramref name="study"/> or <paramref name="scope"/>
    /// does not exist in <paramref name="study"/>.
    /// </summary>
    Task<LockReadinessSnapshot?> ReadReadinessAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        LockMilestone milestone,
        CancellationToken cancellationToken) =>
        ReadReadinessAsync(tenant, study, scope, cancellationToken);

    /// <summary>
    /// Pre-checks whether <paramref name="scope"/> exists in <paramref name="study"/>, whether <paramref name="kind"/>
    /// is already held at <paramref name="scope"/>, and (for <see cref="LockKind.Locked"/>) whether <paramref name="scope"/>
    /// is ready under <see cref="LockMilestone.InterimCut"/>, before recording a signature attestation.
    /// </summary>
    Task<LockApplyPrecheck> PrecheckApplyAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        LockKind kind,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LockApplyPrecheck(ScopeExistsInStudy: true, AlreadyLocked: false, Readiness: null));

    /// <summary>
    /// Pre-checks whether <paramref name="scope"/> exists in <paramref name="study"/>, whether <paramref name="kind"/>
    /// is already held at <paramref name="scope"/> or covered by an active ancestor lock, and (for <see cref="LockKind.Locked"/>)
    /// whether <paramref name="scope"/> is ready under <paramref name="milestone"/>, before recording a signature attestation.
    /// </summary>
    Task<LockApplyPrecheck> PrecheckApplyAsync(
        TenantId tenant,
        StudyId study,
        NodeRef scope,
        LockKind kind,
        LockMilestone milestone,
        CancellationToken cancellationToken) =>
        PrecheckApplyAsync(tenant, study, scope, kind, cancellationToken);

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

    /// <summary>
    /// Checks in one pass whether any active write-denying lock (<see cref="LockKind.Frozen"/> or
    /// <see cref="LockKind.Locked"/>) covers <paramref name="studyCodeOrId"/> or any of
    /// <paramref name="siteCodesOrIds"/> within <paramref name="tenant"/>.
    /// </summary>
    async Task<BatchScopeWriteLockCheck> CheckBatchScopeWriteLocksAsync(
        TenantId tenant,
        string studyCodeOrId,
        IReadOnlyList<string> siteCodesOrIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(siteCodesOrIds);
        if (string.IsNullOrWhiteSpace(studyCodeOrId))
        {
            return new BatchScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        string trimmedStudy = studyCodeOrId.Trim();
        var distinctSites = new List<string>();
        var seenSites = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in siteCodesOrIds)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                string trimmed = raw.Trim();
                if (seenSites.Add(trimmed))
                {
                    distinctSites.Add(trimmed);
                }
            }
        }

        if (distinctSites.Count == 0)
        {
            var studyCheck = await CheckScopeWriteLockAsync(tenant, trimmedStudy, null, null, null, cancellationToken).ConfigureAwait(false);
            return new BatchScopeWriteLockCheck(
                studyCheck.StudyExists,
                studyCheck.Locked,
                studyCheck.LockKind,
                studyCheck.ScopeKind,
                studyCheck.StudyId,
                new Dictionary<string, StudySiteId>(StringComparer.Ordinal),
                studyCheck.CoveringLocks);
        }

        StudyId? resolvedStudyId = null;
        var resolvedSites = new Dictionary<string, StudySiteId>(StringComparer.Ordinal);
        var coveringLocks = new List<ScopeWriteLockCandidate>();
        var seenCandidates = new HashSet<(LockKind Kind, NodeRef Scope)>();
        ScopeWriteLockCandidate? highest = null;

        foreach (string site in distinctSites)
        {
            var siteCheck = await CheckScopeWriteLockAsync(tenant, trimmedStudy, site, null, null, cancellationToken).ConfigureAwait(false);
            if (!siteCheck.StudyExists)
            {
                return new BatchScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
            }

            resolvedStudyId ??= siteCheck.StudyId;
            if (siteCheck.StudySiteId is { } studySiteId)
            {
                resolvedSites[site] = studySiteId;
            }

            if (siteCheck.CoveringLocks is { Count: > 0 } candidates)
            {
                foreach (var candidate in candidates)
                {
                    var unversioned = candidate.Scope.Unversioned();
                    if (seenCandidates.Add((candidate.Kind, unversioned)))
                    {
                        var normalized = new ScopeWriteLockCandidate(candidate.Kind, unversioned);
                        coveringLocks.Add(normalized);
                        if (highest is null || ScopeWriteLockCheck.PrecedesCandidate(normalized, highest))
                        {
                            highest = normalized;
                        }
                    }
                }
            }
            else if (siteCheck is { Locked: true, LockKind: { } fallbackKind, ScopeKind: { } fallbackScopeKind })
            {
                NodeRef? fallbackNode = fallbackScopeKind switch
                {
                    NodeKinds.Study when siteCheck.StudyId is { } sid => NodeRef.For(sid),
                    NodeKinds.StudySite when siteCheck.StudySiteId is { } ssid => NodeRef.For(ssid),
                    _ => null,
                };
                if (fallbackNode is { } resolvedFallback && seenCandidates.Add((fallbackKind, resolvedFallback)))
                {
                    var normalized = new ScopeWriteLockCandidate(fallbackKind, resolvedFallback);
                    coveringLocks.Add(normalized);
                    if (highest is null || ScopeWriteLockCheck.PrecedesCandidate(normalized, highest))
                    {
                        highest = normalized;
                    }
                }
            }
        }

        return new BatchScopeWriteLockCheck(
            StudyExists: true,
            Locked: highest is not null,
            LockKind: highest?.Kind,
            ScopeKind: highest?.Scope.Kind,
            StudyId: resolvedStudyId,
            ResolvedStudySites: resolvedSites,
            CoveringLocks: coveringLocks);
    }
}

/// <summary>An active write-denying lock assertion candidate covering a resolved scope.</summary>
/// <param name="Kind">Whether the active assertion is <see cref="LockKind.Frozen"/> or <see cref="LockKind.Locked"/>.</param>
/// <param name="Scope">The unversioned locked target node (<c>study</c>, <c>study_site</c>, or <c>enrollment</c>).</param>
public sealed record ScopeWriteLockCandidate(
    LockKind Kind,
    NodeRef Scope);

/// <summary>Result of checking whether any active write-denying lock covers a study or a batch of study sites.</summary>
/// <param name="StudyExists">True when the requested study exists in the tenant.</param>
/// <param name="Locked">True when at least one active write-denying assertion covers the study or any evaluated site.</param>
/// <param name="LockKind">The highest-precedence active lock kind (<see cref="LockKind.Locked"/> before <see cref="LockKind.Frozen"/>), or null when unlocked.</param>
/// <param name="ScopeKind">The target scope kind of the highest-precedence active lock, or null when unlocked.</param>
/// <param name="StudyId">The resolved study identifier when <paramref name="StudyExists"/> is true; otherwise null.</param>
/// <param name="ResolvedStudySites">Mapping from each requested site code or identifier that matched in the study to its <see cref="StudySiteId"/>.</param>
/// <param name="CoveringLocks">All active covering lock candidates across the study and resolved sites.</param>
public sealed record BatchScopeWriteLockCheck(
    bool StudyExists,
    bool Locked,
    LockKind? LockKind,
    string? ScopeKind,
    StudyId? StudyId = null,
    IReadOnlyDictionary<string, StudySiteId>? ResolvedStudySites = null,
    IReadOnlyList<ScopeWriteLockCandidate>? CoveringLocks = null)
{
    /// <summary>
    /// Computes a <see cref="BatchScopeWriteLockCheck"/> over the subset of <see cref="CoveringLocks"/> whose unversioned
    /// target <see cref="NodeRef"/> belongs to <paramref name="allowedScopes"/>.
    /// </summary>
    public BatchScopeWriteLockCheck FilterScopes(IReadOnlySet<NodeRef> allowedScopes)
    {
        ArgumentNullException.ThrowIfNull(allowedScopes);
        if (!StudyExists)
        {
            return new BatchScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        if (CoveringLocks is not null)
        {
            ScopeWriteLockCandidate? active = null;
            foreach (var candidate in CoveringLocks)
            {
                if (!allowedScopes.Contains(candidate.Scope.Unversioned()))
                {
                    continue;
                }

                if (active is null || ScopeWriteLockCheck.PrecedesCandidate(candidate, active))
                {
                    active = candidate;
                }
            }

            return active is not null
                ? this with { Locked = true, LockKind = active.Kind, ScopeKind = active.Scope.Kind }
                : this with { Locked = false, LockKind = null, ScopeKind = null };
        }

        if (!Locked || LockKind is null || ScopeKind is null)
        {
            return this with { Locked = false, LockKind = null, ScopeKind = null };
        }

        return ScopeKind == NodeKinds.Study && StudyId is { } sid && allowedScopes.Contains(NodeRef.For(sid))
            ? this
            : this with { Locked = false, LockKind = null, ScopeKind = null };
    }
}

/// <summary>Result of checking whether any active write-denying lock covers a study, site, or enrollment scope.</summary>
/// <param name="StudyExists">True when the requested study exists in the tenant.</param>
/// <param name="Locked">True when at least one active write-denying assertion covers the evaluated scopes.</param>
/// <param name="LockKind">The highest-precedence active lock kind (<see cref="LockKind.Locked"/> before <see cref="LockKind.Frozen"/>), or null when unlocked.</param>
/// <param name="ScopeKind">The target scope kind of the highest-precedence active lock, or null when unlocked.</param>
/// <param name="StudyId">The resolved study identifier when <paramref name="StudyExists"/> is true; otherwise null.</param>
/// <param name="StudySiteId">The resolved study-site identifier (from explicit <c>site_code</c> when supplied, or inferred from the matched enrollment when <c>site_code</c> was omitted); otherwise null.</param>
/// <param name="EnrollmentId">The resolved enrollment identifier when <c>enrollment_id</c> or <c>subject_code</c> matched an enrollment in the study; otherwise null.</param>
/// <param name="CoveringLocks">All active covering lock candidates ordered by precedence (<see cref="LockKind.Locked"/> before <see cref="LockKind.Frozen"/>, then widest scope to narrowest).</param>
/// <param name="EnrollmentStudySiteId">The governing study-site identifier of <paramref name="EnrollmentId"/> when an enrollment matched; otherwise null.</param>
public sealed record ScopeWriteLockCheck(
    bool StudyExists,
    bool Locked,
    LockKind? LockKind,
    string? ScopeKind,
    StudyId? StudyId = null,
    StudySiteId? StudySiteId = null,
    EnrollmentId? EnrollmentId = null,
    IReadOnlyList<ScopeWriteLockCandidate>? CoveringLocks = null,
    StudySiteId? EnrollmentStudySiteId = null)
{
    /// <summary>
    /// Computes a <see cref="ScopeWriteLockCheck"/> over the subset of <see cref="CoveringLocks"/> whose unversioned
    /// target <see cref="NodeRef"/> belongs to <paramref name="allowedScopes"/>.
    /// </summary>
    public ScopeWriteLockCheck FilterScopes(IReadOnlySet<NodeRef> allowedScopes)
    {
        ArgumentNullException.ThrowIfNull(allowedScopes);
        if (!StudyExists)
        {
            return new ScopeWriteLockCheck(StudyExists: false, Locked: false, LockKind: null, ScopeKind: null);
        }

        if (CoveringLocks is not null)
        {
            ScopeWriteLockCandidate? active = null;
            foreach (var candidate in CoveringLocks)
            {
                if (!allowedScopes.Contains(candidate.Scope.Unversioned()))
                {
                    continue;
                }

                if (active is null || PrecedesCandidate(candidate, active))
                {
                    active = candidate;
                }
            }

            return active is not null
                ? this with { Locked = true, LockKind = active.Kind, ScopeKind = active.Scope.Kind }
                : this with { Locked = false, LockKind = null, ScopeKind = null };
        }

        if (!Locked || LockKind is null || ScopeKind is null)
        {
            return this with { Locked = false, LockKind = null, ScopeKind = null };
        }

        var fallbackTarget = ScopeKind switch
        {
            NodeKinds.Study when StudyId is { } sid => NodeRef.For(sid),
            NodeKinds.StudySite when StudySiteId is { } siteId => NodeRef.For(siteId),
            NodeKinds.Enrollment when EnrollmentId is { } enrollmentId => NodeRef.For(enrollmentId),
            _ => null,
        };
        return fallbackTarget is not null && allowedScopes.Contains(fallbackTarget)
            ? this
            : this with { Locked = false, LockKind = null, ScopeKind = null };
    }

    internal static bool PrecedesCandidate(ScopeWriteLockCandidate candidate, ScopeWriteLockCandidate current)
    {
        var candidateHard = candidate.Kind == Evidence.LockKind.Locked;
        var currentHard = current.Kind == Evidence.LockKind.Locked;
        if (candidateHard != currentHard)
        {
            return candidateHard;
        }

        return LockScopes.OrderOf(candidate.Scope.Kind) < LockScopes.OrderOf(current.Scope.Kind);
    }
}

/// <summary>Closeout milestone profile governing lock readiness evaluation.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<LockMilestone>))]
public enum LockMilestone
{
    /// <summary>Interim cut profile: evaluates opened due/closed visits, populated forms, SDV, open queries, required items, and Error findings.</summary>
    InterimCut = 0,

    /// <summary>Final closeout database lock profile: additionally blocks Screening enrollments, NotYetOpen scheduled visits on Enrolled subjects, unsigned NotAsked forms, and unreviewed observations.</summary>
    FinalDatabaseLock = 1,
}

/// <summary>Asymmetric database lock certificate minted for a study-wide <see cref="LockKind.Locked"/> assertion.</summary>
public sealed record DatabaseLockCertificateRecord(
    string CertificateId,
    string TenantId,
    string StudyId,
    string StudyCode,
    string LockId,
    LockMilestone Milestone,
    string AttestationId,
    string SignedBy,
    DateTimeOffset IssuedAt,
    string KeyId,
    string Algorithm,
    string PayloadSha256,
    string FingerprintSha256,
    string SignatureBase64,
    bool Revoked = false,
    DateTimeOffset? RevokedAt = null,
    string? RevokedBy = null);

/// <summary>Signs canonical <see cref="DatabaseLockCertificateRecord"/> payloads using the kernel's private asymmetric key.</summary>
public interface ILockCertificateSigner
{
    DatabaseLockCertificateRecord Sign(
        TenantId tenant,
        StudyId studyId,
        string studyCode,
        AssertionId lockId,
        LockMilestone milestone,
        AttestationId attestationId,
        string signedBy,
        DateTimeOffset issuedAt);
}

/// <summary>What blocks a <see cref="LockKind.Locked"/> assertion at a target scope.</summary>
public enum LockBlockerKind
{
    /// <summary>An unretracted <see cref="AssertionKind.Queried"/> assertion on an observation in scope.</summary>
    OpenQuery,

    /// <summary>A required <c>CollectionItem</c> on an opened <c>CollectionInstance</c> in scope with no recorded value.</summary>
    MissingRequiredItem,

    /// <summary>A recorded observation in scope whose current revision lacks an unretracted <see cref="AssertionKind.Verified"/> assertion.</summary>
    UnverifiedValue,

    /// <summary>A populated <c>CollectionInstance</c> in scope lacking a current <c>Approved</c> or <c>Responsibility</c> signature (including forms with only superseded signatures, or <c>NotAsked</c>-only forms at final lock).</summary>
    UnsignedForm,

    /// <summary>A due or closed scheduled visit (or <c>NotYetOpen</c> future scheduled visit at final database lock) on an active enrollment in scope with an unopened form.</summary>
    UnopenedVisit,

    /// <summary>An active (<see cref="FindingStatus.Open"/> or <see cref="FindingStatus.Investigating"/>) <see cref="ValidationSeverity.Error"/> validation finding in scope.</summary>
    ErrorFinding,

    /// <summary>An enrollment still in <c>Screening</c> status without final cohort disposition (<c>Enrolled</c> or <c>ScreenFailed</c>) at final database lock.</summary>
    ScreeningEnrollment,

    /// <summary>A recorded observation in scope whose current revision lacks an unretracted <see cref="AssertionKind.Reviewed"/> source data review assertion at final database lock.</summary>
    UnreviewedValue,
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
    int UnopenedVisitCount = 0,
    LockMilestone Milestone = LockMilestone.InterimCut,
    int ScreeningEnrollmentCount = 0,
    int PendingScheduledVisitCount = 0,
    int UnreviewedValueCount = 0);

/// <summary>Pre-check result evaluated before recording an electronic signature for <c>ApplyLock</c>.</summary>
public sealed record LockApplyPrecheck(
    bool ScopeExistsInStudy,
    bool AlreadyLocked,
    LockReadinessSnapshot? Readiness,
    NodeRef? CoveringLockScope = null,
    LockKind? CoveringLockKind = null);

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
    string? ReleaseReason,
    LockMilestone Milestone = LockMilestone.InterimCut,
    DatabaseLockCertificateRecord? Certificate = null);

/// <summary>JSON serializer helper for <see cref="DatabaseLockCertificateRecord"/> persisted on <c>kernel.assertions</c>.</summary>
public static class DatabaseLockCertificateJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(DatabaseLockCertificateRecord certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return JsonSerializer.Serialize(certificate, Options);
    }

    public static DatabaseLockCertificateRecord? Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<DatabaseLockCertificateRecord>(json, Options);
}
