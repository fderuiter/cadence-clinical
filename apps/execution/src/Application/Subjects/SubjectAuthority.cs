using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Who may change a participant's record in Execution (#4943). Adding a participant, recording consent, changing
/// their state and editing their demographics are site work, which a data manager may also do; breaking the blind
/// needs an unblinding authority. These are role checks until grants and qualifications from the Trust engine
/// replace them (#4781).
/// @req:PRD-SUB-001
/// </summary>
public static class SubjectAuthority
{
    /// <summary>
    /// Whether <paramref name="role"/> may add a participant, record consent, change state or edit demographics: an
    /// investigator (site, principal, sub or lead), a clinical research coordinator, or a data manager. Monitors,
    /// auditors and participants read but never change the record.
    /// </summary>
    public static bool CanManageParticipants(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "investigator" or "site_investigator" or "site investigator" or "site-investigator"
            or "principal_investigator" or "principal investigator" or "pi"
            or "sub_investigator" or "sub investigator" or "sub-investigator"
            or "lead_investigator" or "lead investigator"
            or "crc" or "site coordinator" or "site_coordinator" or "clinical research coordinator"
            or "sponsor_dm" or "data_manager" or "data manager" or "data-manager" or "dm";
    }

    /// <summary>
    /// Whether <paramref name="role"/> may move a participant to <see cref="SubjectState.Unblinded"/>: the roles the
    /// permission matrix grants <c>rtsm_unblind</c> (emergency unblinder, principal investigator, authorized ER
    /// physician, lead investigator).
    /// </summary>
    public static bool CanUnblind(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "emergency_unblinder" or "emergency unblinder"
            or "principal_investigator" or "principal investigator" or "pi"
            or "authorized_er_physician" or "authorized er physician"
            or "lead_investigator" or "lead investigator";
    }

    /// <summary>
    /// <see langword="null"/> when <paramref name="caller"/> may change a participant's record, otherwise the
    /// <c>403</c> to return.
    /// </summary>
    public static DomainError? AuthorizeParticipantChange(AuthenticatedCaller? caller) =>
        caller?.FirstRoleMatching(CanManageParticipants) is null
            ? SubjectErrors.UnauthorizedParticipantRole(Roles(caller))
            : null;

    /// <summary>
    /// <see langword="null"/> when <paramref name="caller"/> may move a participant to
    /// <paramref name="target"/>, otherwise the <c>403</c> to return. A target that does not parse is left to the
    /// application service, which answers it as an invalid transition.
    /// </summary>
    public static DomainError? AuthorizeStateChange(AuthenticatedCaller? caller, string? target)
    {
        if (SubjectStateCodec.TryParse(target, out var state) && state == SubjectState.Unblinded)
        {
            return caller?.FirstRoleMatching(CanUnblind) is null
                ? SubjectErrors.UnauthorizedUnblindRole(Roles(caller))
                : null;
        }

        return AuthorizeParticipantChange(caller);
    }

    private static string Roles(AuthenticatedCaller? caller) =>
        caller is null ? string.Empty : string.Join(",", caller.Roles);
}
