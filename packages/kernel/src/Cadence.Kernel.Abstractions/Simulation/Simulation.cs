using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Simulation;

/// <summary>
/// Answers "what happens if I change this?" by evaluating a candidate version against its base, never by writing
/// (ADR-2480, spec §11.1). A run is a read: it needs <see cref="SimulationCapabilities.Run"/> at the scope, sees only
/// what the actor may read (K-SIM-4), and changes no clinical state (K-SIM-1).
/// </summary>
public interface ISimulationEngine
{
    /// <summary>
    /// Evaluates <paramref name="request"/> for the current actor. Fails with a <see cref="KernelError"/> when a
    /// version is unknown, the candidate is invalid or the actor may not run simulations at the scope.
    /// </summary>
    Task<CommandResult<SimulationResult>> SimulateAsync(TenantId tenant, SimulationRequest request, CancellationToken cancellationToken);
}

/// <summary>The capabilities simulation checks (ADR-2480 §4.3).</summary>
public static class SimulationCapabilities
{
    /// <summary>Running a simulation at a scope. It is a read, and it is never written to the audit chain.</summary>
    public static readonly Capability Run = new("simulation.run", VerbClass.Read);
}

/// <summary>What kind of versioned specification a candidate is (ADR-2480 §4.1).</summary>
public enum CandidateKind
{
    /// <summary>A protocol version (spec §6.2). Its scope is its study.</summary>
    ProtocolVersion,
}

/// <summary>
/// The proposed change: a candidate version of a versioned specification, compared with its base. Create one with
/// <see cref="StoredProtocolVersion"/> or <see cref="UnsavedProtocolVersion"/>.
/// </summary>
public sealed record SimulationCandidate
{
    private SimulationCandidate(CandidateKind kind, ProtocolVersionId? versionId, ProtocolVersionId? baseVersionId, string? content)
    {
        Kind = kind;
        VersionId = versionId;
        BaseVersionId = baseVersionId;
        Content = content;
    }

    /// <summary>The kind of specification.</summary>
    public CandidateKind Kind { get; }

    /// <summary>A stored candidate version; null for unsaved content.</summary>
    public ProtocolVersionId? VersionId { get; }

    /// <summary>The base of unsaved content; null for a stored candidate, whose base is its parent version.</summary>
    public ProtocolVersionId? BaseVersionId { get; }

    /// <summary>Unsaved candidate content, a JSON object; null for a stored candidate.</summary>
    public string? Content { get; }

    /// <summary>
    /// A stored protocol version, usually a draft, compared with the version it was forked from. A study's first
    /// version has no parent, so all of its elements are added.
    /// </summary>
    public static SimulationCandidate StoredProtocolVersion(ProtocolVersionId version)
    {
        if (version.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version to simulate.", nameof(version));
        }

        return new SimulationCandidate(CandidateKind.ProtocolVersion, version, null, null);
    }

    /// <summary>
    /// Protocol content that is not saved anywhere, compared with <paramref name="baseVersion"/>, which must have
    /// frozen content (in review or later) so the comparison is reproducible.
    /// </summary>
    public static SimulationCandidate UnsavedProtocolVersion(ProtocolVersionId baseVersion, string content)
    {
        if (baseVersion.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version the content is compared with.", nameof(baseVersion));
        }

        ArgumentNullException.ThrowIfNull(content);
        return new SimulationCandidate(CandidateKind.ProtocolVersion, null, baseVersion, content);
    }
}

/// <summary>A simulation to run.</summary>
/// <param name="Candidate">The proposed change.</param>
/// <param name="ValidAt">Valid time both evaluations read at; null for now.</param>
/// <param name="KnownAt">Knowledge time both evaluations read at; null for now.</param>
public sealed record SimulationRequest(SimulationCandidate Candidate, DateTimeOffset? ValidAt = null, DateTimeOffset? KnownAt = null);

/// <summary>The one point in time the baseline and the candidate are both evaluated at (K-SIM-2).</summary>
public sealed record SimulationAsOf(DateTimeOffset ValidAt, DateTimeOffset KnownAt);

/// <summary>The candidate a result describes.</summary>
/// <param name="Kind">The kind of specification.</param>
/// <param name="Version">The stored candidate, pinned to the version read; null for unsaved content or when masked.</param>
/// <param name="BaseVersion">The base, pinned to the version read; null when there is none or when masked.</param>
/// <param name="ContentHash">The candidate content's hash; null when masked.</param>
/// <param name="BaseContentHash">The base content's hash; null when there is no base or when masked.</param>
public sealed record CandidateSummary(
    CandidateKind Kind,
    NodeRef? Version,
    NodeRef? BaseVersion,
    string? ContentHash,
    string? BaseContentHash);

/// <summary>A pure function a result was computed with, and its version (K-SIM-2, K-SIM-3).</summary>
public sealed record EngineVersion(string Name, string Version);

/// <summary>How an element differs between the base and the candidate.</summary>
public enum ElementChangeKind
{
    /// <summary>Only the candidate has the element.</summary>
    Added,

    /// <summary>Only the base has the element.</summary>
    Removed,

    /// <summary>Both have it, with different content.</summary>
    Changed,
}

/// <summary>One protocol element that differs, identified by its stable element id (K-PROT-1).</summary>
/// <param name="ElementId">The element's <c>id</c>, stable across versions.</param>
/// <param name="InstanceType">Its <c>instanceType</c> (for example <c>StudyArm</c>), when the content names one.</param>
/// <param name="Change">Added, removed or changed.</param>
/// <param name="Properties">
/// For a changed element, the JSON Pointers of the differing values inside it, sorted. A nested element's own changes
/// are reported on that element; the parent reports only that its list of children changed. Empty otherwise.
/// </param>
public sealed record ElementChange(string ElementId, string? InstanceType, ElementChangeKind Change, IReadOnlyList<string> Properties);

/// <summary>A part of a result withheld from this actor, and why (Canvas D1, ADR-2471).</summary>
/// <param name="Part">The withheld section, for example <c>structural_diff</c>.</param>
/// <param name="Reason">Why it is withheld.</param>
public sealed record MaskedPart(string Part, MaskReason Reason);

/// <summary>
/// What a candidate would change (ADR-2480 §4.1). Sections are added as the pure functions behind them exist:
/// <see cref="EngineVersions"/> names the ones this result was computed with.
/// </summary>
/// <param name="Scope">Where the simulation ran: the study of a protocol candidate.</param>
/// <param name="AsOf">The pinned time both evaluations read at.</param>
/// <param name="Candidate">The candidate and its base.</param>
/// <param name="EngineVersions">The functions evaluated, sorted by name.</param>
/// <param name="InputHash">Hash of the inputs: the as-of, the base and candidate content hashes and the engine versions.</param>
/// <param name="ResultHash">Hash of this result without itself. The same inputs always give the same hash (K-SIM-3).</param>
/// <param name="StructuralDiff">Elements added, removed or changed, sorted by element id (K-PROT-1).</param>
/// <param name="Masked">Sections withheld from this actor (K-SIM-4).</param>
/// <param name="Dependents">
/// What depends on the base version, at <paramref name="AsOf"/>, through the one dependents read (Canvas D2); objects
/// the actor may not see are counted and masked. Null when the candidate changes nothing, has no base, or is masked.
/// Until protocol elements are nodes of their own (#4808) the changed object is the base version as a whole.
/// </param>
public sealed record SimulationResult(
    NodeRef Scope,
    SimulationAsOf AsOf,
    CandidateSummary Candidate,
    IReadOnlyList<EngineVersion> EngineVersions,
    string InputHash,
    string ResultHash,
    IReadOnlyList<ElementChange> StructuralDiff,
    IReadOnlyList<MaskedPart> Masked,
    DependentsReport? Dependents = null)
{
    /// <summary>True when the candidate changes nothing this actor may see.</summary>
    public bool IsEmpty => StructuralDiff.Count == 0 && Masked.Count == 0;
}

/// <summary>Errors raised by simulation (ADR-2480).</summary>
public static class SimulationErrors
{
    public static readonly KernelError VersionUnknown =
        new("K.Simulation.VersionUnknown", 404, "The protocol version does not exist.");

    public static readonly KernelError BaseNotFrozen =
        new("K.Simulation.BaseNotFrozen", 422, "The base version is still a draft; compare with a version whose content is frozen.");

    public static readonly KernelError ContentInvalid =
        new("K.Simulation.ContentInvalid", 422, "The candidate content must be a JSON object.");

    public static readonly KernelError ElementIdDuplicated =
        new("K.Simulation.ElementIdDuplicated", 422, "Two protocol elements share an id; element ids must be unique within a version (K-PROT-1).");
}
