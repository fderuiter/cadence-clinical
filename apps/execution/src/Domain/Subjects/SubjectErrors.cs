using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Single factory for every <see cref="DomainError"/> this slice raises, unifying the 25 legacy
/// raise sites (`01_spec.md` §5.1) onto 7 stable, machine-readable codes, plus codes with no legacy raise
/// site (<c>ICF_SIGNED_DATE_REQUIRED</c>, #4824; <c>UNAUTHORIZED_SUBJECT_ROLE</c> and <c>UNAUTHORIZED_UNBLIND_ROLE</c>, #4943). HTTP status mapping is
/// MIG-SUBJ-010's concern; aggregate-level call sites are MIG-SUBJ-004's.
/// @req:PRD-SUB-001
/// </summary>
public static class SubjectErrors
{
    public static DomainError NotFound(string subjectIdOrCode) =>
        DomainError.NotFound("SUBJECT_NOT_FOUND", "Subject not found");

    public static DomainError InvalidStateTransition(string from, string to) =>
        DomainError.Validation("INVALID_STATE_TRANSITION", $"Transition from {from} to {to} is forbidden.");

    public static DomainError LockedFactorMutation() =>
        DomainError.Validation("LOCKED_FACTOR_MUTATION", "Stratification factors and demographics are locked post-randomization.")
            with
        { Type = ErrorType.Conflict };

    public static DomainError SoftDeleteBlocked() =>
        DomainError.Forbidden("SOFT_DELETE_BLOCKED", "Demographics cannot be deleted post-randomization.");

    public static DomainError BlockedEnrollment(SiteCode? siteId) =>
        DomainError.Validation("BLOCKED_ENROLLMENT", $"Subject enrollment blocked: Site {siteId} is not compliant.");

    public static DomainError BlockedActivation(string siteId) =>
        DomainError.Validation("BLOCKED_ACTIVATION", $"Site activation blocked: Site {siteId} is not compliant in eTMF. Missing expected documents.");

    public static DomainError ReconsentRequired(string versionTag, string subjectId, string visitId) =>
        DomainError.Conflict("RECONSENT_REQUIRED",
            $"Protocol Amendment {versionTag} is active. Subject {subjectId} must execute re-consent before data entry on visit {visitId}.")
            with
        { Type = ErrorType.Forbidden };

    /// <summary>The caller holds no role that may change a participant's record (#4943).</summary>
    public static DomainError UnauthorizedParticipantRole(string roles) =>
        DomainError.Forbidden("UNAUTHORIZED_SUBJECT_ROLE",
            $"Role '{roles}' is not authorized to change a participant's record. Permitted roles: investigator, clinical research coordinator.");

    /// <summary>The caller holds no unblinding authority, so cannot move a participant to UNBLINDED (#4943).</summary>
    public static DomainError UnauthorizedUnblindRole(string roles) =>
        DomainError.Forbidden("UNAUTHORIZED_UNBLIND_ROLE",
            $"Role '{roles}' is not authorized to unblind a participant. Permitted roles: emergency unblinder, principal investigator, authorized ER physician, lead investigator.");

    /// <summary>A signed consent was submitted without <c>icf_signed_date</c> and no date is on file for that
    /// consent version. The legacy system defaulted the date to the time of the request; this port rejects it
    /// instead (#4824).</summary>
    public static DomainError SignedConsentDateRequired() =>
        DomainError.Validation("ICF_SIGNED_DATE_REQUIRED",
            "icf_signed_date is required when icf_signed is true and no signed date is on file for this consent version.");
}
