namespace Cadence.Kernel.Execution;

/// <summary>
/// Status of an enrollment (spec §7.4). <c>ReconsentRequired</c> is not a status: it is a pending Applicability
/// transition (§7.5). <c>Unblinded</c> is not a status: unblinding is an attestation-bearing event and an RTSM assertion,
/// and blinding is an authorization condition (K-AUTH-4). Stored by name.
/// </summary>
public enum EnrollmentStatus
{
    /// <summary>Consented and being assessed for eligibility.</summary>
    Screening,

    /// <summary>Did not meet eligibility; terminal.</summary>
    ScreenFailed,

    /// <summary>Eligible and enrolled; the enrollment has its number.</summary>
    Enrolled,

    /// <summary>Assigned to an arm by randomization (the assignment itself is an RTSM assertion).</summary>
    Randomized,

    /// <summary>Receiving study treatment or follow-up.</summary>
    Active,

    /// <summary>Finished the study as planned; terminal.</summary>
    Completed,

    /// <summary>Left the study early, with a withdrawal kind and an attestation; terminal.</summary>
    Withdrawn,
}

/// <summary>Why a participant was withdrawn, from the CDISC disposition terms (NCI C66727).</summary>
public enum WithdrawalKind
{
    /// <summary>WITHDRAWAL BY SUBJECT: the participant chose to leave.</summary>
    WithdrawalBySubject,

    /// <summary>PHYSICIAN DECISION: the investigator withdrew the participant.</summary>
    PhysicianDecision,

    /// <summary>ADVERSE EVENT.</summary>
    AdverseEvent,

    /// <summary>LOST TO FOLLOW-UP.</summary>
    LostToFollowUp,

    /// <summary>DEATH.</summary>
    Death,

    /// <summary>PROTOCOL DEVIATION.</summary>
    ProtocolDeviation,

    /// <summary>STUDY TERMINATED BY SPONSOR.</summary>
    StudyTerminatedBySponsor,

    /// <summary>OTHER: the reason for change says why.</summary>
    Other,
}
