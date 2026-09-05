using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Single factory for every <see cref="DomainError"/> this slice raises, unifying the 25 legacy
/// raise sites (`01_spec.md` §5.1) onto 7 stable, machine-readable codes. HTTP status mapping is
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
}
