using System;

namespace Cadence.Kernel.Execution;

/// <summary>
/// The enrollment state machine (spec §7.4, ported from the execution service's <c>SubjectTransitionGraph</c> without
/// <c>ReconsentRequired</c> and <c>Unblinded</c>):
/// <code>
/// Screening → ScreenFailed
/// Screening → Enrolled → Randomized → Active → Completed
/// Enrolled → Active                      (a study without randomization)
/// Screening | Enrolled | Randomized | Active → Withdrawn
/// </code>
/// A move to the current status is not a transition. ScreenFailed, Completed and Withdrawn are terminal.
/// </summary>
public static class EnrollmentTransitions
{
    /// <summary>True when an enrollment in <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool IsAllowed(EnrollmentStatus from, EnrollmentStatus to) => (from, to) switch
    {
        (EnrollmentStatus.Screening, EnrollmentStatus.ScreenFailed or EnrollmentStatus.Enrolled or EnrollmentStatus.Withdrawn) => true,
        (EnrollmentStatus.Enrolled, EnrollmentStatus.Randomized or EnrollmentStatus.Active or EnrollmentStatus.Withdrawn) => true,
        (EnrollmentStatus.Randomized, EnrollmentStatus.Active or EnrollmentStatus.Withdrawn) => true,
        (EnrollmentStatus.Active, EnrollmentStatus.Completed or EnrollmentStatus.Withdrawn) => true,
        _ => false,
    };

    /// <summary>True for a status no enrollment leaves: ScreenFailed, Completed and Withdrawn.</summary>
    public static bool IsTerminal(EnrollmentStatus status) =>
        status is EnrollmentStatus.ScreenFailed or EnrollmentStatus.Completed or EnrollmentStatus.Withdrawn;

    /// <summary>
    /// True for a status in which the participant is still in the study at the site. At most one enrollment of a
    /// participant at a study site is active at any valid instant.
    /// </summary>
    public static bool IsActive(EnrollmentStatus status) => Enum.IsDefined(status) && !IsTerminal(status);
}
