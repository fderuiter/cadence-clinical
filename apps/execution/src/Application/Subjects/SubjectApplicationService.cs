using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Direct typed implementation of <see cref="ISubjectApplicationService"/> (ADR-2428 §3.2, no MediatR),
/// per <c>02_technical_design.md</c> §3.1/§3.3. `MIG-SUBJ-018` implemented <see cref="CreateAsync"/> and
/// <see cref="RecordConsentAsync"/> — the two "creation-shaped" use cases (SCN-001/SCN-002). `MIG-SUBJ-019`
/// (this ticket) adds the remaining 5 interface members
/// (<see cref="AdvanceStateAsync"/>/<see cref="UpdateDemographicsAsync"/>/<see cref="DeleteDemographicsAsync"/>/
/// <see cref="GetDetailAsync"/>/<see cref="EvaluateAndTransitionScreeningAsync"/>) as a later, in-place
/// continuation of this same file (not a second physical file — this class is not declared <c>partial</c>,
/// since MIG-SUBJ-019's own Target Files entry is this exact path), covering 16 of the 25 raise sites and
/// SCN-003 through SCN-007.
///
/// <para>
/// <see cref="ResolveSubjectAsync"/> is the shared Rule 9 dual-lookup helper (try the surrogate id, then the
/// domain code) <see cref="RecordConsentAsync"/>, <see cref="AdvanceStateAsync"/>, and
/// <see cref="UpdateDemographicsAsync"/>/<see cref="DeleteDemographicsAsync"/> all use — kept as a private
/// instance method (not duplicated per call site) so the two-step lookup sequencing lives in exactly one
/// place, per <c>02_technical_design.md</c> §3.2. <see cref="GetDetailAsync"/> and
/// <see cref="EvaluateAndTransitionScreeningAsync"/> deliberately do <b>not</b> use it — see their own doc
/// comments for why a single, code-only lookup is the legacy-faithful choice for those two. Likewise,
/// <see cref="TransitionSubjectAsync"/> is the shared Rule 2 gate + transition + audit + response helper
/// <see cref="AdvanceStateAsync"/> and <see cref="EvaluateAndTransitionScreeningAsync"/> both call, so a
/// transition into <see cref="SubjectState.Enrolled"/> is gated, and audited on the blocked path, identically
/// through either entry point (SCN-005).
/// </para>
/// <para>
/// <paramref name="screeningEligibility"/> is <see cref="EvaluateAndTransitionScreeningAsync"/>'s injected
/// seam onto the out-of-slice eligibility rule engine (<c>01_spec.md</c>'s explicit boundary around
/// <c>apps/execution/eligibility_service.py</c>) — per this ticket's own text, "accept it as an injected
/// <c>Func&lt;...&gt;</c>/port for this ticket's purposes," since the rule engine itself does not exist in
/// this slice. Optional (defaulting to <see langword="null"/>) purely so this primary constructor's
/// pre-existing 7-argument call sites (MIG-SUBJ-018's own <c>SubjectApplicationServiceCreateTests</c>/
/// <c>SubjectApplicationServiceConsentTests</c>, which never exercise screening) keep compiling unmodified;
/// <see cref="EvaluateAndTransitionScreeningAsync"/> itself fails loudly (not a silently-guessed verdict) if
/// actually invoked without one configured.
/// </para>
/// Legacy Python source being replaced: <c>apps/execution/application/services.py</c>
/// (<c>SubjectLifecycleService.enroll_subject</c>, <c>.record_consent</c>, <c>.advance_subject_state</c>,
/// <c>.update_demographics</c>) and the business-logic portions of
/// <c>apps/execution/presentation/routers/subjects.py</c> (<c>get_subject_detail</c>,
/// <c>evaluate_and_transition_screening</c>, <c>update_subject_demographics_endpoint</c>,
/// <c>delete_subject_demographics_endpoint</c>, the RTSM-blinding filter).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectApplicationService(
    ISubjectRepository subjects,
    IConsentRepository consents,
    ISiteComplianceRepository compliance,
    IDemographicsEncryptionService demographicsEncryption,
    IAuditLogWriter auditLog,
    ICurrentAuditContextAccessor auditContext,
    Func<ClinicalSubject, CancellationToken, Task<bool>>? screeningEligibility = null,
    IClock? clock = null) : ISubjectApplicationService
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    // ---- MIG-SUBJ-018: CreateAsync & RecordConsentAsync ----------------------------------------

    /// <summary>
    /// Enrolls a new <see cref="ClinicalSubject"/> (SCN-001): assigns the next sequential
    /// <c>enrollment_index</c> via the shared, transaction-scoped, advisory-locked
    /// <see cref="ISubjectRepository.EnrollNextIndexAsync"/> (MIG-SUBJ-013) rather than a second
    /// index-assignment query, encrypts a supplied <see cref="DemographicsPayload"/> before it ever
    /// reaches the aggregate, writes an <c>ENROLL_SUBJECT</c> audit entry, and returns
    /// <see cref="SubjectResponse.FromAggregate"/>. <see cref="SubjectCreateRequest.SiteId"/>
    /// is passed to <see cref="SiteCode"/>'s constructor unconditionally whenever it is non-null (never
    /// pre-filtered on length): per <see cref="SubjectCreateRequest"/>'s own doc comment, an explicitly-empty
    /// site id is valid at the DTO's own shape rules and the empty-string-as-absent ambiguity is deliberately
    /// resolved downstream by <see cref="SiteCode"/>'s constructor throwing, not silently treated as "no site"
    /// here.
    /// </summary>
    public async Task<Result<SubjectResponse>> CreateAsync(SubjectCreateRequest request, CancellationToken ct)
    {
        string? encryptedDemographics = request.Demographics is { } demographics
            ? demographicsEncryption.Encrypt(ToDictionary(demographics))
            : null;

        var subjectCode = new SubjectCode(request.SubjectId);
        var studyCode = new StudyCode(request.StudyId);
        SiteCode? siteCode = request.SiteId is not null ? new SiteCode(request.SiteId) : null;

        // The index read, the insert and its audit entry are one unit of work under the per-study lock; a transient
        // failure re-runs all three (#5383).
        var enrolled = await subjects.ExecuteInTransactionAsync(async token =>
        {
            int enrollmentIndex = await subjects.EnrollNextIndexAsync(studyCode, token).ConfigureAwait(false);

            var subject = ClinicalSubject.Create(subjectCode, studyCode, siteCode, enrollmentIndex,
                encryptedDemographics, stratFactors: null);
            // MIG-SUBJ-017: the .NET replacement for the legacy's own `current_change_reason.get() or "..."`
            // per-endpoint idiom is this one, uniformly-defaulting accessor — see ICurrentAuditContextAccessor's
            // own doc comment ("closing the legacy's two-idiom split... giving every reader exactly one
            // resolution path"). This deliberately does not reproduce the router's distinct literal fallback
            // text ("Initial Subject Creation"); MIG-SUBJ-017 already unified every such fallback onto one
            // constant ("system_operation").
            subject.ReasonForChange = auditContext.ChangeReason;

            await subjects.AddAsync(subject, token).ConfigureAwait(false);
            await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "ENROLL_SUBJECT",
                auditContext.UserId, subject.ReasonForChange, oldValues: null, newValues: null, token).ConfigureAwait(false);
            await subjects.SaveChangesAsync(token).ConfigureAwait(false);
            return subject;
        }, ct).ConfigureAwait(false);

        return SubjectResponse.FromAggregate(enrolled);
    }

    /// <summary>
    /// Records or merges a <see cref="SubjectConsent"/> (SCN-002). Resolves the subject via the shared
    /// <see cref="ResolveSubjectAsync"/> dual-lookup; looks up an existing consent row for the exact
    /// <c>(subjectCode, studyCode, versionIndex)</c> triple and, if found, delegates to
    /// <see cref="SubjectConsent.MergeResubmission"/> (MIG-SUBJ-005's field-merge semantics) rather than
    /// creating a duplicate row. When the incoming consent no longer requires reconsent and was actually
    /// signed (<c>IcfSigned</c>) or paper-overridden, every <i>other</i> consent row for the same subject
    /// still requiring reconsent is cleared in the same unit of work (Rule 3's cascade, SCN-002).
    /// </summary>
    public async Task<Result<SubjectConsentResponse>> RecordConsentAsync(
        string subjectIdOrCode, SubjectConsentRequest request, CancellationToken ct)
    {
        var subject = await ResolveSubjectAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return SubjectErrors.NotFound(subjectIdOrCode);
        }

        var studyCode = new StudyCode(request.ProtocolVersion.StudyId);
        int versionIndex = request.ProtocolVersion.VersionIndex;

        var existing = await consents.GetAsync(subject.SubjectCode, studyCode, versionIndex, ct).ConfigureAwait(false);

        // A signed consent needs the date it was signed: from this request, or already on file for this
        // consent version. It is never defaulted to the current time (#4824).
        if (request.IcfSigned && (request.IcfSignedDate ?? existing?.IcfSignedDate) is null)
        {
            return SubjectErrors.SignedConsentDateRequired();
        }

        SubjectConsent consent;
        if (existing is not null)
        {
            // Rule 9 field-merge-on-resubmission (MIG-SUBJ-005) — never a full overwrite.
            // `incomingProtocolVersion` stays null: SubjectConsentRequest carries no wire field distinct
            // from ProtocolVersion.VersionTag (already passed as `versionTag` below) that could supply it.
            existing.MergeResubmission(request.ProtocolVersion.VersionTag, request.IcfSigned, request.IcfSignedDate,
                request.RequiresReconsent, request.IsPaperOverride, incomingProtocolVersion: null);
            consent = existing;
        }
        else
        {
            var version = new ProtocolVersionRef(studyCode, request.ProtocolVersion.VersionTag, versionIndex);
            consent = SubjectConsent.Create(subject.SubjectCode, version, request.IcfSigned, request.IcfSignedDate,
                request.RequiresReconsent, request.IsPaperOverride);
            await consents.AddAsync(consent, ct).ConfigureAwait(false);
        }

        consent.ReasonForChange = auditContext.ChangeReason;

        if (!request.RequiresReconsent && (request.IcfSigned || request.IsPaperOverride))
        {
            var others = await consents.GetOtherRequiringReconsentAsync(subject.SubjectCode, studyCode, versionIndex, ct)
                .ConfigureAwait(false);
            foreach (var other in others)
            {
                other.ClearReconsentRequirement();
            }
        }

        await auditLog.WriteAsync(nameof(SubjectConsent), consent.Id.ToString(), "RECORD_CONSENT",
            auditContext.UserId, consent.ReasonForChange, oldValues: null, newValues: null, ct).ConfigureAwait(false);
        await consents.SaveChangesAsync(ct).ConfigureAwait(false);

        return new SubjectConsentResponse(consent.Id, consent.SubjectCode, consent.ProtocolVersion.StudyCode,
            consent.ProtocolVersion.VersionTag, consent.ProtocolVersion.VersionIndex, consent.IcfSigned,
            consent.IcfSignedDate, consent.RequiresReconsent, consent.IsPaperOverride, consent.VersionIndex);
    }

    // ---- MIG-SUBJ-019: AdvanceState/UpdateDemographics/Delete/Detail/Screening -------------------------

    /// <summary>
    /// Transitions a subject's lifecycle state (SCN-003/SCN-005), folding legacy raise sites #1/#4-#6/#14-#19.
    /// Resolves the subject via the shared <see cref="ResolveSubjectAsync"/> dual-lookup; an unparseable
    /// <see cref="SubjectStateUpdateRequest.Target"/> maps to the same <c>INVALID_STATE_TRANSITION</c> code a
    /// well-formed-but-forbidden transition would (closing raise site #6 onto #4/#5's family, per Rule 9 —
    /// callers cannot distinguish a typo'd state name from a legal-but-forbidden one from the response alone).
    /// The Rule 2 site-compliance gate, the actual transition, the audit write, and the response
    /// (built from the aggregate, D8) are then all delegated to <see cref="TransitionSubjectAsync"/> — see that method's own doc
    /// comment for why it is shared with <see cref="EvaluateAndTransitionScreeningAsync"/>.
    /// </summary>
    public async Task<Result<SubjectResponse>> AdvanceStateAsync(
        string subjectIdOrCode, SubjectStateUpdateRequest request, string changeReason, CancellationToken ct)
    {
        var subject = await ResolveSubjectAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return SubjectErrors.NotFound(subjectIdOrCode);
        }

        if (!SubjectStateCodec.TryParse(request.Target, out var target))
        {
            return SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(subject.Status), request.Target ?? "");
        }

        return await TransitionSubjectAsync(subject, target, changeReason, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-encrypts demographics and/or replaces stratification factors (SCN-004 failure path/SCN-006 success
    /// path), subject to <see cref="ClinicalSubject"/>'s post-randomization lock. Dual-lookup uniformly
    /// (closing raise site #2: the legacy router's own inline lookup bypassed
    /// <c>SubjectLifecycleService.update_demographics</c>'s dual-lookup entirely — this port uses the
    /// service-layer dual-lookup for every call site instead, a deliberate, documented closing of that gap).
    /// Strat factors are checked before demographics, mirroring the legacy router's own check ordering
    /// (<c>subjects.py:690-692</c> then <c>:705-707</c>, raise sites #21 then #23) — a strat-factors failure
    /// short-circuits before any demographics re-encryption is attempted, and neither mutation is persisted
    /// (no <c>SaveChangesAsync</c> has run yet) if either fails.
    /// </summary>
    public async Task<Result<SubjectResponse>> UpdateDemographicsAsync(
        string subjectIdOrCode, SubjectDemographicsUpdateRequest request, string changeReason, CancellationToken ct)
    {
        var subject = await ResolveSubjectAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return SubjectErrors.NotFound(subjectIdOrCode);
        }

        if (request.StratFactors is { } stratFactors)
        {
            var stratResult = subject.UpdateStratFactors(stratFactors);
            if (stratResult.IsFailure)
            {
                // `!`: Result<T>.Error is non-null exactly when IsFailure is true (Result<T>'s own
                // constructor invariant, packages/engine_core/src/Result.cs) -- re-wrapped into
                // Result<SubjectResponse> since stratResult's TValue (bool) differs from this method's.
                return Result<SubjectResponse>.Failure(stratResult.Error!);
            }
        }

        if (request.Demographics is { } demographics)
        {
            // Raise site #22's fix. The legacy lock check decrypts the *existing* stored ciphertext to
            // compare "did this update actually change demographics" and, on a decrypt failure, silently
            // treats it as current_demo={} -- which can spuriously look like a change and misreport
            // LOCKED_FACTOR_MUTATION for a subject whose demographics were never actually mutated, merely
            // corrupted-at-rest. ClinicalSubject.UpdateDemographics (MIG-SUBJ-004) already collapses raise
            // sites #3/#21/#23 onto one unconditional post-randomization guard (02_technical_design.md
            // §2.3/§4.1), so the decrypted value itself is never consulted for a verdict here -- it is
            // decrypted, and the result deliberately discarded, purely so a corrupted existing ciphertext
            // fails loudly before that unconditional guard would otherwise mask the real problem behind
            // LOCKED_FACTOR_MUTATION.
            if (subject.IsPostRandomization() && subject.EncryptedDemographics is { } existingCiphertext)
            {
                _ = demographicsEncryption.Decrypt(existingCiphertext);
            }

            string newEncryptedDemographics = demographicsEncryption.Encrypt(ToDictionary(demographics));
            var demographicsResult = subject.UpdateDemographics(newEncryptedDemographics);
            if (demographicsResult.IsFailure)
            {
                // `!`: same Result<T>.Error non-null-when-IsFailure invariant as above.
                return Result<SubjectResponse>.Failure(demographicsResult.Error!);
            }
        }

        subject.ReasonForChange = changeReason;
        // Unlike the legacy router (which bypassed SubjectLifecycleService.update_demographics entirely for
        // every HTTP call -- raise site #2), this port always routes through the one method above, so a
        // strat-factors-only update now also gets an UPDATE_DEMOGRAPHICS audit entry the legacy *router*
        // path never wrote for that case (only the legacy *service* method -- exercised solely by SCN-006's
        // direct unit test -- ever audited this action; fixtures/output_expected_nominal.json's SCN-004-A,
        // which replays the router's own strat-factors-only path, has no audit_assertions for exactly this
        // reason). This is the same kind of deliberate, documented consolidation as the dual-lookup fix
        // above, not a hidden behavior change.
        await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "UPDATE_DEMOGRAPHICS",
            auditContext.UserId, changeReason, oldValues: null, newValues: null, ct).ConfigureAwait(false);
        await subjects.SaveChangesAsync(ct).ConfigureAwait(false);

        return SubjectResponse.FromAggregate(subject);
    }

    /// <summary>
    /// Soft-deletes (clears) demographics (raise site #25) — a distinct error family
    /// (<see cref="SubjectErrors.SoftDeleteBlocked"/>) from <see cref="UpdateDemographicsAsync"/>'s
    /// <see cref="SubjectErrors.LockedFactorMutation"/> despite both gating on the same
    /// <see cref="ClinicalSubject.IsPostRandomization"/> predicate. Writes a <c>DELETE_DEMOGRAPHICS</c> audit
    /// entry in the same transaction (#4767; the legacy endpoint wrote none).
    /// </summary>
    public async Task<Result<SubjectResponse>> DeleteDemographicsAsync(
        string subjectIdOrCode, string changeReason, CancellationToken ct)
    {
        var subject = await ResolveSubjectAsync(subjectIdOrCode, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return SubjectErrors.NotFound(subjectIdOrCode);
        }

        var clearResult = subject.ClearDemographics();
        if (clearResult.IsFailure)
        {
            // `!`: same Result<T>.Error non-null-when-IsFailure invariant as UpdateDemographicsAsync above.
            return Result<SubjectResponse>.Failure(clearResult.Error!);
        }

        subject.ReasonForChange = changeReason;
        await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "DELETE_DEMOGRAPHICS",
            auditContext.UserId, changeReason, oldValues: null, newValues: null, ct).ConfigureAwait(false);
        await subjects.SaveChangesAsync(ct).ConfigureAwait(false);

        return SubjectResponse.FromAggregate(subject);
    }

    /// <summary>
    /// Full subject detail (raise site #13), single-lookup by domain code only — matching
    /// <c>get_subject_detail</c>'s legacy contract (<c>01_spec.md</c> §1.2) — deliberately not the
    /// <see cref="ResolveSubjectAsync"/> dual-lookup <see cref="AdvanceStateAsync"/>/
    /// <see cref="UpdateDemographicsAsync"/>/<see cref="DeleteDemographicsAsync"/> share: every fixture route
    /// exercising this method (e.g. <c>GET /api/v1/execution/subjects/SUBJ-DOES-NOT-EXIST</c>) passes a
    /// domain subject code, never a surrogate GUID, and <c>01_spec.md</c> Rule 9 lists only
    /// <c>advance_subject_state</c>/<c>update_demographics</c> as dual-lookup call sites. The treatment group,
    /// randomisation seed and investigational product id are always null here: the arm lives only encrypted on the
    /// randomisation allocation, and the endpoint asks <c>RandomizationApplicationService.RevealAsync</c> for it only
    /// when the caller is unblinded (#5607).
    /// </summary>
    public async Task<Result<SubjectDetailResponse>> GetDetailAsync(string subjectId, CancellationToken ct)
    {
        var subject = await subjects.GetByCodeAsync(subjectId, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return SubjectErrors.NotFound(subjectId);
        }

        return new SubjectDetailResponse(subject.SubjectCode, subject.StudyCode,
            SubjectStateCodec.ToCode(subject.Status), subject.SiteCode, TreatmentGroup: null,
            RandomizationSeed: null, InvestigationalProductId: null);
    }

    /// <summary>Default page size for <see cref="ListAsync"/>.</summary>
    public const int DefaultListLimit = 50;

    /// <summary>Largest page <see cref="ListAsync"/> returns, whatever the caller asks for.</summary>
    public const int MaxListLimit = 200;

    /// <summary>
    /// Lists one page of a study's subjects in enrollment order. A missing study, a malformed study or site
    /// code, an unknown status, a limit outside 1..<see cref="MaxListLimit"/> or a negative offset is a
    /// validation error rather than a silently widened or emptied list.
    /// </summary>
    public async Task<Result<SubjectListResponse>> ListAsync(string? studyId, string? siteId, string? status, int? limit,
        int? offset, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return DomainError.Validation("STUDY_ID_REQUIRED", "study_id is required to list subjects.");
        }

        StudyCode studyCode;
        try
        {
            studyCode = new StudyCode(studyId);
        }
        catch (ArgumentException ex)
        {
            return DomainError.Validation("INVALID_STUDY_CODE", ex.Message);
        }

        SiteCode? siteCode = null;
        if (!string.IsNullOrWhiteSpace(siteId))
        {
            try
            {
                siteCode = new SiteCode(siteId);
            }
            catch (ArgumentException ex)
            {
                return DomainError.Validation("INVALID_SITE_CODE", ex.Message);
            }
        }

        SubjectState? state = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!SubjectStateCodec.TryParse(status, out var parsed))
            {
                return DomainError.Validation("INVALID_SUBJECT_STATUS", $"'{status}' is not a subject status.");
            }

            state = parsed;
        }

        int pageSize = limit ?? DefaultListLimit;
        if (pageSize is < 1 or > MaxListLimit)
        {
            return DomainError.Validation("INVALID_LIMIT", $"limit must be between 1 and {MaxListLimit}.");
        }

        int skip = offset ?? 0;
        if (skip < 0)
        {
            return DomainError.Validation("INVALID_OFFSET", "offset cannot be negative.");
        }

        var page = await subjects.ListAsync(new SubjectListCriteria(studyCode, siteCode, state, pageSize, skip), ct)
            .ConfigureAwait(false);

        return new SubjectListResponse(page.Items.Select(SubjectSummaryResponse.FromAggregate).ToList(), page.Total,
            pageSize, skip);
    }

    /// <summary>
    /// Evaluates screening eligibility and transitions to <see cref="SubjectState.ScreenFailed"/> or
    /// <see cref="SubjectState.Enrolled"/> (SCN-005/SCN-007), single-lookup by domain code only — see
    /// <see cref="GetDetailAsync"/>'s remarks on why this is not the shared dual-lookup helper; raise site
    /// #10's not-found is standardized onto <see cref="SubjectErrors.NotFound"/> rather than reproducing the
    /// legacy's distinct "Clinical subject not found." string. Resolves the acting user/reason from
    /// <see cref="ICurrentAuditContextAccessor"/> (MIG-SUBJ-017 — the one call site Rule 9 singles out for the
    /// legacy's <c>LookupError</c>-catch-vs-truthy-<c>or</c> idiom split, now unified onto this one accessor)
    /// rather than a caller-supplied <c>changeReason</c>, matching this method's interface signature (no such
    /// parameter exists on it, unlike <see cref="AdvanceStateAsync"/>/<see cref="UpdateDemographicsAsync"/>/
    /// <see cref="DeleteDemographicsAsync"/>). Delegates the actual eligibility verdict to the injected
    /// <c>screeningEligibility</c> constructor seam — the eligibility rule engine itself is out of this
    /// slice's scope per <c>01_spec.md</c>'s explicit boundary — then reuses <see cref="TransitionSubjectAsync"/> so a
    /// transition to <see cref="SubjectState.Enrolled"/> is gated by the exact same Rule 2 site-compliance
    /// check <see cref="AdvanceStateAsync"/> uses (SCN-005).
    /// </summary>
    public async Task<Result<SubjectResponse>> EvaluateAndTransitionScreeningAsync(string subjectId, CancellationToken ct)
    {
        var subject = await subjects.GetByCodeAsync(subjectId, ct).ConfigureAwait(false);
        if (subject is null)
        {
            return SubjectErrors.NotFound(subjectId);
        }

        var evaluateEligibility = screeningEligibility ?? throw new InvalidOperationException(
            "SubjectApplicationService was constructed without a screening-eligibility evaluator -- the " +
            "out-of-slice 'Func<...>/port' seam 03_tickets.md's MIG-SUBJ-019 describes. " +
            "EvaluateAndTransitionScreeningAsync cannot resolve an eligibility verdict without one.");
        bool eligible = await evaluateEligibility(subject, ct).ConfigureAwait(false);
        var target = eligible ? SubjectState.Enrolled : SubjectState.ScreenFailed;

        return await TransitionSubjectAsync(subject, target, auditContext.ChangeReason, ct).ConfigureAwait(false);
    }

    // ---- Shared helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Rule 2 sovereign-site-activation gate (enforced only when <paramref name="target"/> is
    /// <see cref="SubjectState.Enrolled"/>) + <see cref="ClinicalSubject.TransitionTo"/> + the semantic
    /// <c>TRANSITION_&lt;STATE&gt;</c> audit write + the response built from the aggregate — shared by
    /// <see cref="AdvanceStateAsync"/> and <see cref="EvaluateAndTransitionScreeningAsync"/> so a subject
    /// targeting <see cref="SubjectState.Enrolled"/> at a non-compliant site is blocked, and a
    /// <c>BLOCKED_ENROLLMENT</c> audit entry written for that blocked attempt itself (not only for successes,
    /// matching the legacy's own behavior), identically through either entry point (SCN-005). Always uses
    /// <see cref="ICurrentAuditContextAccessor.UserId"/> for the audit <c>userId</c> — every caller resolves
    /// its own acting user the same way, so that argument is not threaded through as a parameter.
    /// </summary>
    private async Task<Result<SubjectResponse>> TransitionSubjectAsync(
        ClinicalSubject subject, SubjectState target, string changeReason, CancellationToken ct)
    {
        if (target == SubjectState.Enrolled)
        {
            var gate = await compliance.GetAsync(subject.StudyCode, subject.SiteCode, "SITE_ACTIVATION", ct).ConfigureAwait(false);
            if (gate is not { IsComplete: true })
            {
                // MIG-SUBJ-026 fix: this audit write was never committed. IAuditLogWriter.WriteAsync only
                // stages the entry on the shared DbContext's ChangeTracker (see its own doc comment: "commits
                // inside the same SaveChangesAsync call") -- without an explicit SaveChangesAsync before
                // returning, the pending entry was silently discarded the moment this scope's DbContext was
                // disposed, so no BLOCKED_ENROLLMENT row was ever actually persisted. Confirmed independently
                // two ways: (1) MIG-SUBJ-026's golden-master replay of INV-014/INV-015 (both exercise this
                // exact blocked-enrollment branch, one via a direct service call, one via a real HTTP PATCH)
                // found zero matching audit rows in either case; (2) SiteComplianceApplicationService
                // .ActivateSiteAsync's own analogous blocked-activation branch (MIG-SUBJ-020) already calls
                // `compliance.SaveChangesAsync(ct)` right after its own audit write, matching this method's
                // own doc comment's claim that the blocked path is audited "before returning the failure
                // result" -- this one call site just never actually added the save that claim depends on.
                await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "BLOCKED_ENROLLMENT",
                    auditContext.UserId, changeReason, oldValues: null, newValues: null, ct).ConfigureAwait(false);
                await subjects.SaveChangesAsync(ct).ConfigureAwait(false);
                return SubjectErrors.BlockedEnrollment(subject.SiteCode);
            }
        }

        // Randomisation allocates an arm and a kit, so it is its own command, never a bare state change (#5607).
        if (target == SubjectState.Randomized)
        {
            return RandomizationErrors.RandomizeThroughAllocation();
        }

        // An emergency unblinding needs a clinical justification of at least 50 characters (PRD-SUB-005, #4982).
        if (target == SubjectState.Unblinded
            && changeReason.Trim().Length < SubjectAuthority.MinimumUnblindingReasonLength)
        {
            return SubjectErrors.UnblindingReasonTooShort(SubjectAuthority.MinimumUnblindingReasonLength);
        }

        // Unblinding records who broke the blind, why and when on the subject itself, not only in the audit
        // entry (#4951); every other target is a plain transition.
        DomainError? transitionError = target == SubjectState.Unblinded
            ? subject.Unblind(auditContext.UserId, changeReason, _clock).Error
            : subject.TransitionTo(target).Error;
        if (transitionError is not null)
        {
            return Result<SubjectResponse>.Failure(transitionError);
        }

        subject.ReasonForChange = changeReason;
        await auditLog.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), $"TRANSITION_{SubjectStateCodec.ToCode(target)}",
            auditContext.UserId, changeReason, oldValues: null, newValues: null, ct).ConfigureAwait(false);
        await subjects.SaveChangesAsync(ct).ConfigureAwait(false);

        return SubjectResponse.FromAggregate(subject);
    }

    /// <summary>
    /// Rule 9 dual-lookup: try the surrogate PK first, then the domain code — shared by
    /// <see cref="RecordConsentAsync"/> (MIG-SUBJ-018) and MIG-SUBJ-019's <see cref="AdvanceStateAsync"/>/
    /// <see cref="UpdateDemographicsAsync"/>/<see cref="DeleteDemographicsAsync"/>, so the two-step lookup
    /// sequencing lives in exactly one place rather than being duplicated per call site. Returns
    /// <see langword="null"/> when neither lookup matches; callers are responsible for mapping that to
    /// <see cref="SubjectErrors.NotFound"/>.
    /// </summary>
    private async Task<ClinicalSubject?> ResolveSubjectAsync(string subjectIdOrCode, CancellationToken ct) =>
        await subjects.GetByIdAsync(subjectIdOrCode, ct).ConfigureAwait(false)
            ?? await subjects.GetByCodeAsync(subjectIdOrCode, ct).ConfigureAwait(false);

    /// <summary>
    /// Flattens the structured <see cref="DemographicsPayload"/> DTO into the free-form dictionary shape
    /// <see cref="IDemographicsEncryptionService.Encrypt"/> expects, using the same lowercase key names the
    /// legacy <c>demographics.py</c>/<c>eligibility_context.py</c> dictionaries use (<c>name</c>,
    /// <c>birthdate</c>, <c>gender</c>, <c>race</c>) so a decrypted round-trip matches the legacy shape byte
    /// for byte.
    /// </summary>
    private static Dictionary<string, object?> ToDictionary(DemographicsPayload demographics) =>
        new Dictionary<string, object?>
        {
            ["name"] = demographics.Name,
            ["birthdate"] = demographics.Birthdate,
            ["gender"] = demographics.Gender,
            ["race"] = demographics.Race,
        };
}
