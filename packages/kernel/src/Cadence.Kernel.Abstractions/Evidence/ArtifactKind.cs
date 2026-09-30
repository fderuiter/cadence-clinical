namespace Cadence.Kernel.Evidence;

/// <summary>
/// What an artifact's content is, declared when it is registered (spec §8.3). The kind is fixed for the artifact's
/// life and is what kernel rules check (for example K-TIME-5: an effectivity needs an ethics or regulatory approval).
/// Filing classifications such as the TMF artifact type are Assertions, so filing can change without touching this.
/// </summary>
public enum ArtifactKind
{
    /// <summary>A kind the kernel does not reason about.</summary>
    Other,

    /// <summary>An ethics committee or IRB approval of a protocol version for a scope.</summary>
    EthicsApproval,

    /// <summary>A competent authority approval or clinical trial authorisation.</summary>
    RegulatoryApproval,

    /// <summary>A competent authority's acknowledgement of a clinical trial application or notification.</summary>
    CtaAcknowledgement,

    /// <summary>A blank informed consent form version, as approved.</summary>
    InformedConsentForm,

    /// <summary>A consent form signed by or for a participant.</summary>
    SignedConsent,

    /// <summary>A source document, such as a paper record or its scan.</summary>
    SourceDocument,

    /// <summary>A laboratory report or a laboratory reference range set.</summary>
    LabReport,

    /// <summary>A generated dataset, such as an SDTM or ADaM file.</summary>
    Dataset,

    /// <summary>A rendering of a protocol version, such as its PDF.</summary>
    ProtocolRendering,
}
