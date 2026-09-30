namespace Cadence.Kernel.Protocol;

/// <summary>
/// How participants already on the previous protocol version move to an amended one (spec §6.2, §7.5). Applied by
/// Effectivity and Applicability (#4777). Stored by name.
/// </summary>
public enum TransitionPolicy
{
    /// <summary>Only participants enrolled after the amendment is effective follow it.</summary>
    NewParticipantsOnly,

    /// <summary>Current participants follow it once they re-consent.</summary>
    RequiresReconsent,

    /// <summary>Current participants follow it as soon as it is effective at their site.</summary>
    Automatic,

    /// <summary>The transition is set per cohort.</summary>
    ByCohort,
}
