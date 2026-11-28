using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Api;

/// <summary>Body of <c>POST /api/v1/kernel/locks</c>.</summary>
/// <param name="StudyRef">The study the scope belongs to, as <c>study:&lt;uuid&gt;</c>.</param>
/// <param name="ScopeRef">The scope to freeze or lock (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c> or <c>observation</c>).</param>
/// <param name="Kind">Either <c>Frozen</c> or <c>Locked</c>.</param>
/// <param name="Meaning">Signature meaning (<c>Approved</c> by default, or <c>Responsibility</c>).</param>
/// <param name="LockId">Optional lock assertion identifier; derived deterministically from the idempotency key when omitted.</param>
/// <param name="AttestationId">Optional attestation identifier; derived deterministically from the idempotency key when omitted.</param>
public sealed record KernelApplyLockRequest(
    string StudyRef,
    string ScopeRef,
    string Kind,
    string? Meaning = null,
    Guid? LockId = null,
    Guid? AttestationId = null);

/// <summary>Body of <c>POST /api/v1/kernel/locks/{lockRef}/release</c>.</summary>
/// <param name="StudyRef">Optional study NodeRef; resolved from the lock assertion when omitted.</param>
/// <param name="Meaning">Signature meaning (<c>Approved</c> by default, or <c>Responsibility</c>).</param>
/// <param name="AssertionId">Optional retracting assertion identifier; derived deterministically from the idempotency key when omitted.</param>
/// <param name="AttestationId">Optional attestation identifier; derived deterministically from the idempotency key when omitted.</param>
public sealed record KernelReleaseLockRequest(
    string? StudyRef = null,
    string? Meaning = null,
    Guid? AssertionId = null,
    Guid? AttestationId = null);

/// <summary>Body of <c>POST /api/v1/kernel/locks/in-force/batch</c>.</summary>
/// <param name="StudyCode">The study short code, UUID, or <c>study:&lt;uuid&gt;</c> NodeRef to evaluate.</param>
/// <param name="SiteCodes">Optional study site codes, UUIDs, or <c>study_site:&lt;uuid&gt;</c> NodeRefs to evaluate in batch.</param>
public sealed record KernelBatchScopeWriteLockRequest(
    string? StudyCode,
    IReadOnlyList<string>? SiteCodes = null);

/// <summary>Collapsed ADR-2590 indicator reporting whether records outside the caller's scope were omitted.</summary>
/// <param name="MoreExist">True when at least one matching item was omitted because of caller scope visibility.</param>
/// <param name="Reasons">Distinct sorted mask reasons explaining why items were omitted (never counts, kinds, or handles).</param>
public sealed record KernelHiddenContentResponse(
    bool MoreExist,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Canonical empty hidden-content indicator when zero records were omitted.</summary>
    public static KernelHiddenContentResponse None { get; } = new(false, []);

    /// <summary>Builds a collapsed hidden-content indicator from collected mask reasons.</summary>
    public static KernelHiddenContentResponse FromReasons(IEnumerable<MaskReason> reasons)
    {
        var ordered = reasons
            .Select(r => r.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
        return ordered.Count == 0 ? None : new KernelHiddenContentResponse(true, ordered);
    }
}

/// <summary>Locks recorded for a study at an evaluated bitemporal coordinate.</summary>
/// <param name="StudyRef">The study, as a NodeRef.</param>
/// <param name="StudyCode">The study's short code.</param>
/// <param name="ValidAt">The evaluated valid-time coordinate.</param>
/// <param name="KnownAt">The evaluated recorded-time coordinate.</param>
/// <param name="Locks">The locks within the caller's visible scope, with blinded locks masked in place.</param>
/// <param name="Hidden">Collapsed indicator when locks outside the caller's visible scope were omitted.</param>
public sealed record KernelStudyLocksResponse(
    string StudyRef,
    string StudyCode,
    DateTimeOffset ValidAt,
    DateTimeOffset KnownAt,
    IReadOnlyList<KernelLockResponse> Locks,
    KernelHiddenContentResponse Hidden);

/// <summary>A lock assertion or masked placeholder in a study lock list or mutation response.</summary>
/// <param name="Ref">The lock assertion NodeRef (<c>assertion:&lt;uuid&gt;</c>), or null when masked.</param>
/// <param name="StudyRef">The study NodeRef.</param>
/// <param name="ScopeRef">The locked scope NodeRef, or null when masked.</param>
/// <param name="ScopeKind">The kind of scope (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c>, <c>observation</c>), or null when masked.</param>
/// <param name="StudySiteRef">The governing study-site NodeRef when the lock covers a study site or narrower scope and is visible; null otherwise or when masked.</param>
/// <param name="EnrollmentRef">The governing enrollment NodeRef when the lock covers an enrollment or narrower scope and is visible; null otherwise or when masked.</param>
/// <param name="SubjectCode">The pseudonymous subject code when the lock covers an enrollment or narrower scope; null otherwise or when masked.</param>
/// <param name="Kind">Either <c>Frozen</c> or <c>Locked</c>, or null when masked.</param>
/// <param name="AttestationRef">The electronic signature NodeRef (<c>attestation:&lt;uuid&gt;</c>), or null when masked.</param>
/// <param name="AttestationMeaning">The recorded attestation meaning, or null when masked.</param>
/// <param name="LockedAt">When the lock was applied, or null when masked.</param>
/// <param name="LockedBy">Who applied the lock, or null when masked.</param>
/// <param name="ReasonForChange">The signed reason for applying the lock, or null when masked.</param>
/// <param name="Released">True when the lock has been released at the evaluated coordinate, or null when masked.</param>
/// <param name="ReleaseRef">The release assertion NodeRef when released; null otherwise or when masked.</param>
/// <param name="ReleasedAt">When the lock was released; null while active or when masked.</param>
/// <param name="ReleasedBy">Who released the lock; null while active or when masked.</param>
/// <param name="ReleaseReason">Why the lock was released; null while active or when masked.</param>
/// <param name="Masked">True when caller visibility or blinding hides this lock's details.</param>
/// <param name="Mask">Why the lock is masked (<c>Blinded</c>), or null when visible.</param>
public sealed record KernelLockResponse(
    string? Ref,
    string StudyRef,
    string? ScopeRef,
    string? ScopeKind,
    string? StudySiteRef,
    string? EnrollmentRef,
    string? SubjectCode,
    string? Kind,
    string? AttestationRef,
    string? AttestationMeaning,
    DateTimeOffset? LockedAt,
    string? LockedBy,
    string? ReasonForChange,
    bool? Released,
    string? ReleaseRef,
    DateTimeOffset? ReleasedAt,
    string? ReleasedBy,
    string? ReleaseReason,
    bool Masked,
    string? Mask)
{
    internal static KernelLockResponse FromVisible(StudyLockEntry entry) =>
        new(
            NodeRef.For(entry.LockId).ToString(),
            NodeRef.For(entry.StudyId).ToString(),
            entry.Scope.ToString(),
            entry.Scope.Kind,
            entry.StudySiteId is { } studySiteId ? NodeRef.For(studySiteId).ToString() : null,
            entry.EnrollmentId is { } enrollmentId ? NodeRef.For(enrollmentId).ToString() : null,
            entry.SubjectCode,
            entry.Kind.ToString(),
            entry.AttestationId.IsEmpty ? null : NodeRef.For(entry.AttestationId).ToString(),
            entry.AttestationMeaning?.ToString(),
            entry.LockedAt,
            entry.LockedBy,
            entry.ReasonForChange,
            entry.Released,
            entry.ReleaseAssertionId is { } rel ? NodeRef.For(rel).ToString() : null,
            entry.ReleasedAt,
            entry.ReleasedBy,
            entry.ReleaseReason,
            Masked: false,
            Mask: null);

    internal static KernelLockResponse FromMasked(StudyLockEntry entry, MaskReason reason) =>
        new(
            Ref: null,
            StudyRef: NodeRef.For(entry.StudyId).ToString(),
            ScopeRef: null,
            ScopeKind: null,
            StudySiteRef: null,
            EnrollmentRef: null,
            SubjectCode: null,
            Kind: null,
            AttestationRef: null,
            AttestationMeaning: null,
            LockedAt: null,
            LockedBy: null,
            ReasonForChange: null,
            Released: null,
            ReleaseRef: null,
            ReleasedAt: null,
            ReleasedBy: null,
            ReleaseReason: null,
            Masked: true,
            Mask: reason.ToString());

    internal static KernelLockResponse FromState(StudyId studyId, LockState state, AttestationMeaning meaning, string reason) =>
        new(
            NodeRef.For(state.LockId).ToString(),
            NodeRef.For(studyId).ToString(),
            state.Scope.ToString(),
            state.Scope.Kind,
            StudySiteRef: state.Scope.Kind == NodeKinds.StudySite ? state.Scope.ToString() : null,
            EnrollmentRef: state.Scope.Kind == NodeKinds.Enrollment ? state.Scope.ToString() : null,
            SubjectCode: null,
            state.Kind.ToString(),
            NodeRef.For(state.AttestationId).ToString(),
            meaning.ToString(),
            state.Released ? null : state.At,
            state.Released ? null : state.By,
            state.Released ? null : reason,
            state.Released,
            ReleaseRef: null,
            state.Released ? state.At : null,
            state.Released ? state.By : null,
            state.Released ? reason : null,
            Masked: false,
            Mask: null);
}

/// <summary>Live write-lock check response (<c>GET /api/v1/kernel/locks/in-force</c>).</summary>
public sealed record KernelScopeWriteLockResponse(
    bool StudyExists,
    bool Locked,
    string? LockKind,
    string? ScopeKind);

/// <summary>Readiness of a lock scope for a <c>Locked</c> assertion (<c>GET /studies/{studyRef}/locks/readiness</c>).</summary>
public sealed record KernelLockReadinessResponse(
    string StudyRef,
    string StudyCode,
    string ScopeRef,
    string ScopeKind,
    bool ReadyForLock,
    bool CanFreeze,
    int OpenQueryCount,
    int MissingRequiredItemCount,
    int OpenErrorFindingCount,
    int BlockerCount,
    IReadOnlyList<KernelLockBlockerResponse> Blockers,
    KernelExplanationResponse Explanation,
    int UnverifiedValueCount = 0,
    int UnsignedFormCount = 0,
    int UnopenedVisitCount = 0);

/// <summary>One unresolved blocker preventing a <c>Locked</c> assertion at a scope, masked in place under <c>K-AUTH-4</c>.</summary>
public sealed record KernelLockBlockerResponse(
    string Kind,
    string Owner,
    string? TargetRef,
    string TargetKind,
    string? SubjectCode,
    string? CollectionSpecId,
    string? CollectionTitle,
    string? ItemId,
    string? ItemLabel,
    string? QueryRef,
    string? QueryText,
    bool Answered,
    string NextAction,
    string NextCapability,
    bool Masked,
    string? Mask,
    string? FindingRef = null,
    string? RuleId = null,
    string? Detail = null,
    int? CurrentRevision = null,
    bool SignatureSuperseded = false,
    string? PlannedActivityId = null,
    string? PlannedActivityName = null,
    DateOnly? PlannedDate = null,
    DateOnly? WindowEnd = null)
{
    internal static KernelLockBlockerResponse FromVisible(LockReadinessBlocker blocker) =>
        new(
            blocker.Kind.ToString(),
            blocker.Owner,
            blocker.Target.ToString(),
            blocker.Target.Kind,
            blocker.SubjectCode,
            blocker.CollectionSpecId,
            blocker.CollectionTitle,
            blocker.ItemId,
            blocker.ItemLabel,
            blocker.QueryAssertionId is { } qid ? NodeRef.For(qid).ToString() : null,
            blocker.QueryText,
            blocker.Answered,
            blocker.NextAction,
            blocker.NextCapability,
            Masked: false,
            Mask: null,
            FindingRef: blocker.FindingId is { } fid ? NodeRef.For(fid).ToString() : null,
            RuleId: blocker.RuleId,
            Detail: blocker.FindingMessage
                ?? blocker.QueryText
                ?? (blocker.Kind == LockBlockerKind.UnverifiedValue && blocker.CurrentRevision is { } rev
                    ? $"Revision {rev} awaiting source verification"
                    : blocker.ItemLabel),
            CurrentRevision: blocker.CurrentRevision,
            SignatureSuperseded: blocker.SignatureSuperseded,
            PlannedActivityId: blocker.PlannedActivityId,
            PlannedActivityName: blocker.PlannedActivityName,
            PlannedDate: blocker.PlannedDate,
            WindowEnd: blocker.WindowEnd);

    internal static KernelLockBlockerResponse FromMasked(LockReadinessBlocker blocker, MaskReason reason)
    {
        var blockerLabel = blocker.Kind switch
        {
            LockBlockerKind.OpenQuery => "open query",
            LockBlockerKind.UnverifiedValue => "unverified observation revision",
            LockBlockerKind.UnsignedForm => blocker.SignatureSuperseded ? "superseded form signature" : "unsigned form",
            LockBlockerKind.UnopenedVisit => "unopened scheduled visit",
            LockBlockerKind.ErrorFinding => "Error validation finding",
            _ => "missing required item",
        };
        var maskedAction = reason == MaskReason.Blinded
            ? $"{blocker.Owner}: resolve blinded {blockerLabel} before locking."
            : $"{blocker.Owner}: resolve restricted {blockerLabel} in another scope before locking.";
        return new(
            blocker.Kind.ToString(),
            blocker.Owner,
            TargetRef: null,
            blocker.Target.Kind,
            SubjectCode: null,
            CollectionSpecId: null,
            CollectionTitle: null,
            ItemId: null,
            ItemLabel: null,
            QueryRef: null,
            QueryText: null,
            blocker.Answered,
            maskedAction,
            blocker.NextCapability,
            Masked: true,
            Mask: reason.ToString());
    }
}
