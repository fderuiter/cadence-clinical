using System;

namespace Cadence.Kernel.Lineage;

/// <summary>
/// How a downstream object depends on an upstream one (spec §9.6, ADR-2255). An edge runs from the upstream object
/// (<c>from</c>) to the downstream object (<c>to</c>) and reads "<c>to</c> is &lt;kind&gt; <c>from</c>": a
/// PlannedActivity <see cref="RequiredBy"/> an Estimand, an SDTM record <see cref="DerivedFrom"/> an Observation.
/// </summary>
public enum LineageEdgeKind
{
    /// <summary>The downstream object exists because the upstream one requires it (Objective → Endpoint → Estimand → activity).</summary>
    RequiredBy,

    /// <summary>The downstream object's meaning is defined by the upstream one (a concept version, a code-system version).</summary>
    DefinedBy,

    /// <summary>The downstream fact was collected under the upstream specification (a CollectionSpec item, a CRF version).</summary>
    CollectedBy,

    /// <summary>The downstream value was computed from the upstream one.</summary>
    DerivedFrom,

    /// <summary>The downstream record was produced by the upstream mapping specification.</summary>
    MappedBy,

    /// <summary>The downstream record was produced by the upstream transformation.</summary>
    TransformedBy,

    /// <summary>The downstream object is the result of validating the upstream one (a ValidationFinding).</summary>
    ValidatedBy,

    /// <summary>The downstream object is an attestation over the upstream one.</summary>
    AttestedBy,

    /// <summary>The downstream object was written under the upstream protocol version.</summary>
    GovernedBy,

    /// <summary>
    /// The downstream object version was written by a command that executed the upstream approved proposal (K-AI-2,
    /// ADR-2632). The pipeline writes it; handlers never declare it.
    /// </summary>
    ExecutedUnder,

    /// <summary>The downstream object is the next version of the upstream one. The pipeline writes it for every update.</summary>
    Supersedes,
}

/// <summary>Which way a lineage read walks from its starting object.</summary>
public enum LineageDirection
{
    /// <summary>Toward inputs: from a submission value back to its source (<c>to</c> → <c>from</c>).</summary>
    Backward,

    /// <summary>Toward dependents: from an Objective forward to the datasets that serve it (<c>from</c> → <c>to</c>).</summary>
    Forward,
}

/// <summary>
/// Marks an aggregate whose every version is derived from other objects (spec §9.6, K-LIN-1). The command pipeline
/// refuses to commit such an aggregate unless the handler declared at least one input with
/// <c>IKernelUnitOfWork.LinkInput</c>, and the scheduled lineage check reports stored versions without an inbound edge.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DerivedObjectAttribute : Attribute;
