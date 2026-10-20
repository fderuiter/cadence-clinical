using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// The aggregate root at the center of this slice: factory construction, stratification-factor/demographics
/// mutation with post-randomization locking (Rule 1), and the four state-changing operations (`TransitionTo`,
/// `Unblind`, `Withdraw`). Persistence mapping is out of scope here (MIG-SUBJ-011) — pure domain logic and
/// in-memory invariants only. Replaces `apps/execution/database/models/subject.py` (ORM model, `@validates`
/// guards) and `apps/execution/subject_lifecycle.py` (`unblind_subject_model`, `withdraw_subject_model`).
/// `IGxPAuditable` is inherited via `AggregateRoot&lt;SubjectId&gt; : AggregateRootBase : IGxPAuditable` — no
/// redeclaration needed here.
/// @req:PRD-SUB-001
/// </summary>
public sealed class ClinicalSubject : AggregateRoot<SubjectId>
{
    public SubjectCode SubjectCode { get; private set; } = default!;
    public StudyCode StudyCode { get; private set; } = default!;
    public SiteCode? SiteCode { get; private set; }
    public SubjectState Status { get; private set; }
    public string ActiveProtocolVersion { get; private set; } = "1.0.0";
    public string? EncryptedDemographics { get; private set; }
    public IReadOnlyDictionary<string, object?> StratFactors => _stratFactors;
    private readonly Dictionary<string, object?> _stratFactors = new();
    public int EnrollmentIndex { get; private set; }
    public bool IsUnblinded { get; private set; }
    public string? UnblindedBy { get; private set; }
    public string? UnblindedReason { get; private set; }
    public DateTimeOffset? UnblindedAt { get; private set; }
    public string? WithdrawalReason { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public Guid? RandomizationId { get; private set; }
    public string? KitReference { get; private set; }

    private ClinicalSubject() { } // EF Core

    public static ClinicalSubject Create(SubjectCode subjectCode, StudyCode studyCode, SiteCode? siteCode,
        int enrollmentIndex, string? encryptedDemographics, IReadOnlyDictionary<string, object?>? stratFactors)
    {
        var subject = new ClinicalSubject
        {
            Id = SubjectId.New(),
            SubjectCode = subjectCode,
            StudyCode = studyCode,
            SiteCode = siteCode,
            Status = SubjectState.Screening,
            EnrollmentIndex = enrollmentIndex,
            EncryptedDemographics = encryptedDemographics,
            // No arm at creation: an arm comes only from randomisation, and is held encrypted on the
            // RandomizationAllocation, never on the subject (#5607).
            RandomizationId = null,
            KitReference = null,
        };
        if (stratFactors is not null)
        {
            foreach (var (k, v) in stratFactors) subject._stratFactors[k] = v;
        }

        return subject;
    }

    /// <summary>Rule 1: pre-randomization only. Enforced here (domain invariant) — see §4.1 for the
    /// EF Core-level `IEntityTypeConfiguration` mirror of this same rule (defense in depth, matching
    /// the legacy system's dual ORM-`@validates` + router-level enforcement at raise sites #3/#21/#23).</summary>
    public Result<bool> UpdateStratFactors(IReadOnlyDictionary<string, object?> newFactors)
    {
        if (IsPostRandomization())
            return SubjectErrors.LockedFactorMutation();
        _stratFactors.Clear();
        foreach (var (k, v) in newFactors) _stratFactors[k] = v;
        return true;
    }

    public Result<bool> UpdateDemographics(string newEncryptedDemographics)
    {
        if (IsPostRandomization())
            return SubjectErrors.LockedFactorMutation();
        EncryptedDemographics = newEncryptedDemographics;
        return true;
    }

    public void SetActiveProtocolVersion(string protocolVersion)
    {
        if (!string.IsNullOrWhiteSpace(protocolVersion))
        {
            ActiveProtocolVersion = protocolVersion.Trim();
        }
    }

    /// <summary>Raise site #25 (`SOFT_DELETE_BLOCKED`, 403) — distinct error family from
    /// `LOCKED_FACTOR_MUTATION` even though both gate on the same `IsPostRandomization()` predicate.</summary>
    public Result<bool> ClearDemographics()
    {
        if (IsPostRandomization())
            return SubjectErrors.SoftDeleteBlocked();
        EncryptedDemographics = null;
        return true;
    }

    /// <summary>
    /// Records the randomisation (#5607): the stratification factors it used become locked, the subject moves to
    /// <see cref="SubjectState.Randomized"/>, and it names its allocation and kit. The arm is not held here.
    /// </summary>
    public Result<bool> Randomize(Guid randomizationId, string? kitReference, IReadOnlyDictionary<string, object?> stratFactors)
    {
        ArgumentNullException.ThrowIfNull(stratFactors);
        if (RandomizationId is not null)
            return RandomizationErrors.AlreadyRandomized();
        var transition = SubjectTransitionGraph.Guard(Status, SubjectState.Randomized);
        if (transition.IsFailure)
            return SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(Status), "RANDOMIZED");

        _stratFactors.Clear();
        foreach (var (k, v) in stratFactors) _stratFactors[k] = v;
        Status = SubjectState.Randomized;
        RandomizationId = randomizationId;
        KitReference = kitReference;
        return true;
    }

    public bool IsPostRandomization() => Status is SubjectState.Randomized or SubjectState.Active
        or SubjectState.Completed or SubjectState.Unblinded or SubjectState.Withdrawn;

    public Result<SubjectState> TransitionTo(SubjectState target)
    {
        var result = SubjectTransitionGraph.Guard(Status, target);
        if (result.IsFailure) return result;
        Status = result.Value;
        return result;
    }

    /// <summary>Rule 9 SCN-008: bypasses the transition-graph HTTP-layer error translation by design —
    /// this mirrors the legacy `unblind_subject_model`, which sets `.status` directly and lets an illegal
    /// call sequence surface as a raw domain exception to whatever caller invoked it, not a formatted
    /// HTTP response. .NET keeps that by returning <see cref="Result{TValue}"/> here too (callers of this
    /// method, not just HTTP endpoints, must still handle failure explicitly).</summary>
    public Result<bool> Unblind(string unblindedBy, string reason, IClock clock)
    {
        var transition = SubjectTransitionGraph.Guard(Status, SubjectState.Unblinded);
        if (transition.IsFailure) return SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(Status), "UNBLINDED");
        Status = SubjectState.Unblinded;
        IsUnblinded = true;
        UnblindedBy = unblindedBy;
        UnblindedReason = reason;
        UnblindedAt = clock.UtcNow; // DateTimeOffset.UtcNow — fixes the naive datetime.now() trap (subject_lifecycle.py:179)
        return true;
    }

    public Result<bool> Withdraw(string reason, IClock clock)
    {
        var transition = SubjectTransitionGraph.Guard(Status, SubjectState.Withdrawn);
        if (transition.IsFailure) return SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(Status), "WITHDRAWN");
        Status = SubjectState.Withdrawn;
        WithdrawalReason = reason;
        WithdrawnAt = clock.UtcNow; // DateTimeOffset.UtcNow — fixes the naive datetime.now() trap (subject_lifecycle.py:187)
        return true;
    }
}
