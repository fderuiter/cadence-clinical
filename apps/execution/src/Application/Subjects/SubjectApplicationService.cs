using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Direct typed implementation of <see cref="ISubjectApplicationService"/> (ADR-2234 §3.2, no MediatR),
/// per <c>02_technical_design.md</c> §3.1/§3.3. `MIG-SUBJ-018` (this ticket) implements only
/// <see cref="CreateAsync"/> and <see cref="RecordConsentAsync"/> — the two "creation-shaped" use cases
/// (SCN-001/SCN-002). The remaining 5 interface members
/// (<see cref="AdvanceStateAsync"/>/<see cref="UpdateDemographicsAsync"/>/<see cref="DeleteDemographicsAsync"/>/
/// <see cref="GetDetailAsync"/>/<see cref="EvaluateAndTransitionScreeningAsync"/>) are `MIG-SUBJ-019`'s
/// responsibility, added to this same file as a later, in-place continuation (not a second physical file —
/// this class is not declared <c>partial</c>, since MIG-SUBJ-019's own Target Files entry is this exact
/// path). Because a non-abstract class implementing an interface must provide every member a body to
/// compile, each of those 5 methods is stubbed below with <see cref="NotImplementedException"/> for now —
/// implementing their real logic here would duplicate MIG-SUBJ-019's own scope, not merely stand this file
/// up standalone.
///
/// <para>
/// <see cref="ResolveSubjectAsync"/> is the shared Rule 9 dual-lookup helper (try the surrogate id, then the
/// domain code) both <see cref="RecordConsentAsync"/> (this ticket) and MIG-SUBJ-019's
/// <c>AdvanceStateAsync</c> use — kept as a private instance method (not duplicated per call site) so the
/// two-step lookup sequencing lives in exactly one place, per <c>02_technical_design.md</c> §3.2.
/// </para>
/// Legacy Python source being replaced: <c>apps/execution/application/services.py</c>
/// (<c>SubjectLifecycleService.enroll_subject</c>, <c>.record_consent</c>).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectApplicationService(
    ISubjectRepository subjects,
    IConsentRepository consents,
    ISiteComplianceRepository compliance,
    IDemographicsEncryptionService demographicsEncryption,
    IAuditLogWriter auditLog,
    IClock clock,
    ICurrentAuditContextAccessor auditContext) : ISubjectApplicationService
{
    // ---- MIG-SUBJ-018: CreateAsync & RecordConsentAsync ----------------------------------------

    /// <summary>
    /// Enrolls a new <see cref="ClinicalSubject"/> (SCN-001): assigns the next sequential
    /// <c>enrollment_index</c> via the shared, transaction-scoped, advisory-locked
    /// <see cref="ISubjectRepository.EnrollNextIndexAsync"/> (MIG-SUBJ-013) rather than a second
    /// index-assignment query, encrypts a supplied <see cref="DemographicsPayload"/> before it ever
    /// reaches the aggregate, writes an <c>ENROLL_SUBJECT</c> audit entry, and returns
    /// <see cref="SubjectResponse.FromAggregate"/> — the <b>one</b> call site in this slice that uses
    /// <c>FromAggregate</c> rather than <c>Underpopulated</c> (§2.6). <see cref="SubjectCreateRequest.SiteId"/>
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

        int enrollmentIndex = await subjects.EnrollNextIndexAsync(studyCode, ct).ConfigureAwait(false);

        var subject = ClinicalSubject.Create(subjectCode, studyCode, siteCode, enrollmentIndex,
            encryptedDemographics, stratFactors: null);
        // MIG-SUBJ-017: the .NET replacement for the legacy's own `current_change_reason.get() or "..."`
        // per-endpoint idiom is this one, uniformly-defaulting accessor — see ICurrentAuditContextAccessor's
        // own doc comment ("closing the legacy's two-idiom split... giving every reader exactly one
        // resolution path"). This deliberately does not reproduce the router's distinct literal fallback
        // text ("Initial Subject Creation"); MIG-SUBJ-017 already unified every such fallback onto one
        // constant ("system_operation").
        subject.ReasonForChange = auditContext.ChangeReason;

        await subjects.AddAsync(subject, ct).ConfigureAwait(false);
        await auditLog.WriteAsync("clinical_subjects", subject.Id.ToString(), "ENROLL_SUBJECT",
            auditContext.UserId, subject.ReasonForChange, oldValues: null, newValues: null, ct).ConfigureAwait(false);
        await subjects.SaveChangesAsync(ct).ConfigureAwait(false);

        return SubjectResponse.FromAggregate(subject);
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

        SubjectConsent consent;
        if (existing is not null)
        {
            // Rule 9 field-merge-on-resubmission (MIG-SUBJ-005) — never a full overwrite.
            // `incomingProtocolVersion` stays null: SubjectConsentRequest carries no wire field distinct
            // from ProtocolVersion.VersionTag (already passed as `versionTag` below) that could supply it.
            existing.MergeResubmission(request.ProtocolVersion.VersionTag, request.IcfSigned, request.IcfSignedDate,
                request.RequiresReconsent, request.IsPaperOverride, incomingProtocolVersion: null, clock);
            consent = existing;
        }
        else
        {
            var version = new ProtocolVersionRef(studyCode, request.ProtocolVersion.VersionTag, versionIndex);
            consent = SubjectConsent.Create(subject.SubjectCode, version, request.IcfSigned, request.IcfSignedDate,
                request.RequiresReconsent, request.IsPaperOverride, clock);
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

        await auditLog.WriteAsync("subject_consents", consent.Id.ToString(), "RECORD_CONSENT",
            auditContext.UserId, consent.ReasonForChange, oldValues: null, newValues: null, ct).ConfigureAwait(false);
        await consents.SaveChangesAsync(ct).ConfigureAwait(false);

        return new SubjectConsentResponse(consent.Id, consent.SubjectCode, consent.ProtocolVersion.StudyCode,
            consent.ProtocolVersion.VersionTag, consent.ProtocolVersion.VersionIndex, consent.IcfSigned,
            consent.IcfSignedDate, consent.RequiresReconsent, consent.IsPaperOverride, consent.VersionIndex);
    }

    // ---- MIG-SUBJ-019: remaining 5 methods (stubbed here only so this file compiles standalone) --------
    // Each throws NotImplementedException rather than carrying real logic: implementing that logic is
    // MIG-SUBJ-019's scope (dependent on this ticket), and duplicating it here would pre-empt that ticket's
    // own work rather than merely keeping SubjectApplicationService buildable in the meantime.

    /// <summary>Implemented by MIG-SUBJ-019.</summary>
    public Task<Result<SubjectResponse>> AdvanceStateAsync(
        string subjectIdOrCode, SubjectStateUpdateRequest request, string changeReason, CancellationToken ct)
    {
        // `compliance` (Rule 2's site-activation gate, per 02_technical_design.md §3.2) is consumed by this
        // method once MIG-SUBJ-019 implements it. Referenced here only so the shared primary-constructor
        // parameter is not flagged as unread (CS9113) while every method that touches it is still a stub.
        _ = compliance;
        throw new NotImplementedException("AdvanceStateAsync is implemented by MIG-SUBJ-019.");
    }

    /// <summary>Implemented by MIG-SUBJ-019.</summary>
    public Task<Result<SubjectResponse>> UpdateDemographicsAsync(
        string subjectIdOrCode, SubjectDemographicsUpdateRequest request, string changeReason, CancellationToken ct) =>
        throw new NotImplementedException("UpdateDemographicsAsync is implemented by MIG-SUBJ-019.");

    /// <summary>Implemented by MIG-SUBJ-019.</summary>
    public Task<Result<SubjectResponse>> DeleteDemographicsAsync(
        string subjectIdOrCode, string changeReason, CancellationToken ct) =>
        throw new NotImplementedException("DeleteDemographicsAsync is implemented by MIG-SUBJ-019.");

    /// <summary>Implemented by MIG-SUBJ-019.</summary>
    public Task<Result<SubjectDetailResponse>> GetDetailAsync(
        string subjectId, bool isBlindedCaller, CancellationToken ct) =>
        throw new NotImplementedException("GetDetailAsync is implemented by MIG-SUBJ-019.");

    /// <summary>Implemented by MIG-SUBJ-019.</summary>
    public Task<Result<SubjectResponse>> EvaluateAndTransitionScreeningAsync(string subjectId, CancellationToken ct) =>
        throw new NotImplementedException("EvaluateAndTransitionScreeningAsync is implemented by MIG-SUBJ-019.");

    // ---- Shared helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Rule 9 dual-lookup: try the surrogate PK first, then the domain code — shared by
    /// <see cref="RecordConsentAsync"/> (MIG-SUBJ-018) and MIG-SUBJ-019's <c>AdvanceStateAsync</c>, so the
    /// two-step lookup sequencing lives in exactly one place rather than being duplicated per call site.
    /// Returns <see langword="null"/> when neither lookup matches; callers are responsible for mapping that
    /// to <see cref="SubjectErrors.NotFound"/>.
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
    private static IReadOnlyDictionary<string, object?> ToDictionary(DemographicsPayload demographics) =>
        new Dictionary<string, object?>
        {
            ["name"] = demographics.Name,
            ["birthdate"] = demographics.Birthdate,
            ["gender"] = demographics.Gender,
            ["race"] = demographics.Race,
        };
}
