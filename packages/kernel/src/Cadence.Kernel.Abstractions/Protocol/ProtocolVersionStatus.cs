namespace Cadence.Kernel.Protocol;

/// <summary>
/// Lifecycle of a protocol version (spec §6.2): <c>Draft → InReview → Approved → Superseded | Withdrawn</c>. A version
/// in review can return to draft, and a version in review or approved can be withdrawn. Superseded and Withdrawn are
/// terminal. Stored by name.
/// </summary>
public enum ProtocolVersionStatus
{
    /// <summary>Being written; the content can change.</summary>
    Draft,

    /// <summary>Submitted for review; the content is frozen and its hash is set.</summary>
    InReview,

    /// <summary>Approved by an attestation bound to the content hash. Approved versions coexist.</summary>
    Approved,

    /// <summary>A later version is effective everywhere this one will ever be (set by Effectivity, #4777).</summary>
    Superseded,

    /// <summary>Withdrawn with a reason and an attestation; terminal.</summary>
    Withdrawn,
}
