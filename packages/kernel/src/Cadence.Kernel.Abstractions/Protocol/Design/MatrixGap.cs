namespace Cadence.Kernel.Protocol.Design;

/// <summary>The kind of schedule matrix gap or structural defect detected.</summary>
public enum MatrixGapKind
{
    /// <summary>An assessment or procedure lacks a link to a study objective or endpoint.</summary>
    UnlinkedEndpoint,

    /// <summary>An encounter or planned activity is not connected to an epoch, collection spec, or study arm.</summary>
    OrphanEncounter,

    /// <summary>A safety or discontinuation rule is defined but lacks a scheduled planned activity timing.</summary>
    UnscheduledSafetyProcedure,

    /// <summary>An epoch sequence or transition forms a cycle or invalid order progression.</summary>
    CyclicEpochTransition,

    /// <summary>A structural requirement of the ICH M11 standard protocol template is missing or invalid.</summary>
    IchM11Violation,
}

/// <summary>The severity of a matrix gap finding.</summary>
public enum MatrixGapSeverity
{
    Error,
    Warning,
    Info,
}

/// <summary>
/// Represents a schedule of activities matrix gap or structural protocol finding.
/// </summary>
/// <param name="ElementId">The stable element identifier associated with the gap.</param>
/// <param name="Kind">The category of gap detected.</param>
/// <param name="Severity">The severity level (Error, Warning, Info).</param>
/// <param name="JsonPointerPath">The exact JSON Pointer path to the element or property in the USDM graph.</param>
/// <param name="Description">Human-readable description of the defect or gap.</param>
/// <param name="Recommendation">Actionable guidance on how to resolve the gap.</param>
public sealed record MatrixGapFinding(
    string ElementId,
    MatrixGapKind Kind,
    MatrixGapSeverity Severity,
    string JsonPointerPath,
    string Description,
    string? Recommendation = null);
