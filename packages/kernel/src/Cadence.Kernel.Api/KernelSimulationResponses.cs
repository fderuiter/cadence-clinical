using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Api;

/// <summary>The pinned evaluation window of a simulation run (K-SIM-2).</summary>
/// <param name="ValidAt">The valid time baseline and candidate were evaluated at.</param>
/// <param name="KnownAt">The transaction time baseline and candidate were evaluated at.</param>
public sealed record KernelSimulationAsOfResponse(DateTimeOffset ValidAt, DateTimeOffset KnownAt)
{
    internal static KernelSimulationAsOfResponse From(SimulationAsOf asOf) =>
        new(asOf.ValidAt, asOf.KnownAt);
}

/// <summary>The candidate specification a simulation result describes.</summary>
/// <param name="Kind">The candidate specification kind (<c>ProtocolVersion</c>).</param>
/// <param name="Version">The stored candidate's pinned NodeRef, or null for unsaved content or when masked.</param>
/// <param name="BaseVersion">The base version's pinned NodeRef, or null when there is none or when masked.</param>
/// <param name="ContentHash">The candidate content's hash, or null when masked.</param>
/// <param name="BaseContentHash">The base content's hash, or null when there is no base or when masked.</param>
public sealed record KernelCandidateSummaryResponse(
    string Kind,
    string? Version,
    string? BaseVersion,
    string? ContentHash,
    string? BaseContentHash)
{
    internal static KernelCandidateSummaryResponse From(CandidateSummary summary) =>
        new(
            summary.Kind.ToString(),
            summary.Version?.ToString(),
            summary.BaseVersion?.ToString(),
            summary.ContentHash,
            summary.BaseContentHash);
}

/// <summary>A pure evaluation function and version sealed into a simulation run (K-SIM-2, K-SIM-3).</summary>
/// <param name="Name">The function identifier, for example <c>protocol.structural_diff</c>.</param>
/// <param name="Version">The function's version string.</param>
public sealed record KernelEngineVersionResponse(string Name, string Version)
{
    internal static KernelEngineVersionResponse From(EngineVersion engine) =>
        new(engine.Name, engine.Version);
}

/// <summary>A planned visit on the study-day axis (K-TIME-7, ADR-2487).</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Name">The activity's name.</param>
/// <param name="EpochId">The epoch it belongs to.</param>
/// <param name="EpochOrder">The epoch's order in the design.</param>
/// <param name="StudyDay">The planned study day.</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
public sealed record KernelPlannedVisitResponse(
    string ActivityId,
    string Name,
    string EpochId,
    int EpochOrder,
    int StudyDay,
    int WindowBefore,
    int WindowAfter)
{
    internal static KernelPlannedVisitResponse? From(PlannedVisit? visit) =>
        visit is null
            ? null
            : new(
                visit.ActivityId,
                visit.Name,
                visit.EpochId,
                visit.EpochOrder,
                visit.StudyDay,
                visit.WindowBefore,
                visit.WindowAfter);
}

/// <summary>A timed planned activity whose expected schedule differs between the base and candidate (K-TIME-7).</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Change"><c>Added</c>, <c>Removed</c>, or <c>Changed</c>.</param>
/// <param name="Before">Its planned visit in the base, or null when added.</param>
/// <param name="After">Its planned visit in the candidate, or null when removed.</param>
public sealed record KernelScheduleChangeResponse(
    string ActivityId,
    string Change,
    KernelPlannedVisitResponse? Before,
    KernelPlannedVisitResponse? After)
{
    internal static KernelScheduleChangeResponse From(ScheduleChange change) =>
        new(
            change.ActivityId,
            change.Change.ToString(),
            KernelPlannedVisitResponse.From(change.Before),
            KernelPlannedVisitResponse.From(change.After));
}

/// <summary>A simulation section withheld from the caller, and why (K-SIM-4, ADR-2471).</summary>
/// <param name="Part">The withheld section name, for example <c>structural_diff</c> or <c>matrix_gaps</c>.</param>
/// <param name="Reason">Why it is withheld (<c>NotPermitted</c>, <c>Blinded</c>, <c>IdentityVault</c>).</param>
public sealed record KernelMaskedPartResponse(string Part, string Reason)
{
    internal static KernelMaskedPartResponse From(MaskedPart masked) =>
        new(masked.Part, masked.Reason.ToString());
}

/// <summary>A Schedule of Activities matrix gap or structural protocol finding.</summary>
/// <param name="ElementId">The stable element identifier associated with the finding.</param>
/// <param name="Kind">The category of gap detected.</param>
/// <param name="Severity"><c>Error</c>, <c>Warning</c>, or <c>Info</c>.</param>
/// <param name="JsonPointerPath">The JSON Pointer path to the element or property in the protocol design.</param>
/// <param name="Description">Human-readable description of the defect or gap.</param>
/// <param name="Recommendation">Actionable guidance on how to resolve the gap.</param>
public sealed record KernelMatrixGapFindingResponse(
    string ElementId,
    string Kind,
    string Severity,
    string JsonPointerPath,
    string Description,
    string? Recommendation)
{
    internal static KernelMatrixGapFindingResponse From(MatrixGapFinding finding) =>
        new(
            finding.ElementId,
            finding.Kind.ToString(),
            finding.Severity.ToString(),
            finding.JsonPointerPath,
            finding.Description,
            finding.Recommendation);
}

/// <summary>Evaluate-only simulation preview of what a candidate protocol version changes (ADR-2480).</summary>
/// <param name="Scope">The study NodeRef where the simulation ran.</param>
/// <param name="AsOf">The pinned time both evaluations read at.</param>
/// <param name="Candidate">The candidate and its base.</param>
/// <param name="EngineVersions">The pure functions evaluated, sorted by name.</param>
/// <param name="InputHash">Hash of the inputs (K-SIM-3).</param>
/// <param name="ResultHash">Hash of this result without itself (K-SIM-3).</param>
/// <param name="StructuralDiff">Protocol elements added, removed, or changed, sorted by element id.</param>
/// <param name="ScheduleChanges">Timed activities added, removed, or moved on the expected schedule.</param>
/// <param name="Masked">Sections withheld from this caller (K-SIM-4).</param>
/// <param name="Dependents">What depends on the base version's changed elements, or null when unchanged, unbased, or masked.</param>
/// <param name="MatrixGaps">Schedule matrix gaps and structural findings detected in the candidate design.</param>
/// <param name="IsEmpty">True when the candidate changes nothing this caller may see.</param>
public sealed record KernelSimulationResponse(
    string Scope,
    KernelSimulationAsOfResponse AsOf,
    KernelCandidateSummaryResponse Candidate,
    IReadOnlyList<KernelEngineVersionResponse> EngineVersions,
    string InputHash,
    string ResultHash,
    IReadOnlyList<KernelElementChangeResponse> StructuralDiff,
    IReadOnlyList<KernelScheduleChangeResponse> ScheduleChanges,
    IReadOnlyList<KernelMaskedPartResponse> Masked,
    KernelDependentsResponse? Dependents,
    IReadOnlyList<KernelMatrixGapFindingResponse> MatrixGaps,
    bool IsEmpty)
{
    internal static KernelSimulationResponse From(SimulationResult result) =>
        new(
            result.Scope.ToString(),
            KernelSimulationAsOfResponse.From(result.AsOf),
            KernelCandidateSummaryResponse.From(result.Candidate),
            [.. result.EngineVersions.Select(KernelEngineVersionResponse.From)],
            result.InputHash,
            result.ResultHash,
            [.. result.StructuralDiff.Select(c => new KernelElementChangeResponse(c.ElementId, c.InstanceType, c.Change.ToString(), c.Properties))],
            [.. result.ScheduleChanges.Select(KernelScheduleChangeResponse.From)],
            [.. result.Masked.Select(KernelMaskedPartResponse.From)],
            result.Dependents is { } dependents ? KernelDependentsResponse.From(dependents) : null,
            [.. (result.MatrixGaps ?? []).Select(KernelMatrixGapFindingResponse.From)],
            result.IsEmpty);
}
