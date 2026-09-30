namespace Cadence.Kernel.Trust;

/// <summary>
/// What a signer asserts by an <c>Attestation</c> (spec §9.5, 21 CFR 11.50(a)(3)). Stored and sent by name.
/// </summary>
public enum AttestationMeaning
{
    Authored,
    Reviewed,
    Approved,
    Verified,
    Responsibility,
    Consented,

    /// <summary>Withdraws an approved record, such as a protocol version (spec §6.2).</summary>
    Withdrawn,
}
