using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Randomization;

/// <summary>
/// The errors randomisation and kit assignment raise (#5607). None of them names an arm, so a refusal cannot reveal
/// an allocation to a blinded caller.
/// @req:PRD-SUB-001
/// </summary>
public static class RandomizationErrors
{
    public static DomainError InvalidScheme(string description) =>
        DomainError.Validation("RANDOMIZATION_SCHEME_INVALID", description);

    public static DomainError SchemeNotFound() =>
        DomainError.NotFound("RANDOMIZATION_SCHEME_NOT_FOUND", "Randomisation scheme not found.");

    public static DomainError SchemeNotDraft(int version, RandomizationSchemeStatus status) =>
        DomainError.Conflict("RANDOMIZATION_SCHEME_NOT_DRAFT", $"Scheme version {version} is {status}; only a draft can be approved.");

    public static DomainError SelfApproval() =>
        DomainError.Forbidden("RANDOMIZATION_SCHEME_SELF_APPROVAL",
            "A randomisation scheme must be approved by someone other than the person who drafted it.");

    public static DomainError ConfigurationTampered(int version) =>
        DomainError.Conflict("RANDOMIZATION_SCHEME_TAMPERED",
            $"Scheme version {version} no longer matches the configuration hash recorded when it was drafted.");

    public static DomainError NoApprovedScheme(string studyCode) =>
        DomainError.Conflict("RANDOMIZATION_NOT_LIVE", $"Study {studyCode} has no approved randomisation scheme.");

    public static DomainError MissingFactor(string factor) =>
        DomainError.Validation("STRATIFICATION_FACTOR_MISSING", $"Stratification factor '{factor}' needs a value.");

    public static DomainError UnknownFactor(string factor) =>
        DomainError.Validation("STRATIFICATION_FACTOR_UNKNOWN", $"The scheme does not stratify by '{factor}'.");

    public static DomainError UnknownLevel(string factor, string level) =>
        DomainError.Validation("STRATIFICATION_LEVEL_UNKNOWN", $"'{level}' is not a declared level of stratification factor '{factor}'.");

    public static DomainError ListExhausted(string stratum) =>
        DomainError.Conflict("RANDOMIZATION_LIST_EXHAUSTED",
            $"The randomisation list for stratum '{stratum}' has no free slot; approve a new scheme version with a longer list.");

    public static DomainError AlreadyRandomized() =>
        DomainError.Conflict("SUBJECT_ALREADY_RANDOMIZED", "The participant is already randomised.");

    public static DomainError NotEnrolled(string status) =>
        DomainError.Conflict("RANDOMIZATION_REQUIRES_ENROLLED", $"Only an enrolled participant can be randomised; this one is {status}.");

    public static DomainError ConsentRequired() =>
        DomainError.Forbidden("RANDOMIZATION_REQUIRES_CONSENT",
            "The participant has no current signed informed consent (or a re-consent is pending), so they cannot be randomised.");

    public static DomainError SiteNotActivated(string? siteCode) =>
        DomainError.Forbidden("RANDOMIZATION_BLOCKED_SITE",
            $"Randomisation is blocked: site {siteCode} has not been activated (greenlit) for this study.");

    public static DomainError KitUnavailable(string? siteCode) =>
        DomainError.Conflict("KIT_UNAVAILABLE",
            $"Site {siteCode} has no unexpired kit available for this allocation; resupply the site before randomising.");

    public static DomainError RandomizeThroughAllocation() =>
        DomainError.Validation("RANDOMIZATION_REQUIRES_ALLOCATION",
            "A participant is randomised through POST /api/v1/execution/subjects/{subjectId}/randomize, not a state change.");
}
