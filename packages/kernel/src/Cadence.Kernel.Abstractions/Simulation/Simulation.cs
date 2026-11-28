using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

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

    /// <summary>Saving an unmasked simulation result as an attestable <c>SimulationReport</c> artifact (K-SIM-5).</summary>
    public static readonly Capability SaveReport = new("simulation.save_report", VerbClass.Execute);
}

/// <summary>What kind of versioned specification a candidate is (ADR-2480 §4.1).</summary>
public enum CandidateKind
{
    /// <summary>A protocol version (spec §6.2). Its scope is its study.</summary>
    ProtocolVersion,

    /// <summary>A concept-to-variable mapping specification library item (ADR-2463). Evaluated within a study scope.</summary>
    MappingSpecification,

    /// <summary>A declarative validation rule pack library item (spec §10.1). Evaluated within a study scope.</summary>
    RulePack,
}

/// <summary>
/// The proposed change: a candidate version of a versioned specification, compared with its base. Create one with
/// <see cref="StoredProtocolVersion"/>, <see cref="UnsavedProtocolVersion"/>,
/// <see cref="StoredMappingSpecification"/>, <see cref="UnsavedMappingSpecification"/>,
/// <see cref="StoredRulePack"/>, or <see cref="UnsavedRulePack"/>.
/// </summary>
public sealed record SimulationCandidate
{
    private SimulationCandidate(
        CandidateKind kind,
        StudyId? studyId,
        ProtocolVersionId? versionId,
        long? version,
        ProtocolVersionId? baseVersionId,
        long? baseVersion,
        LibraryItemId? libraryItemId,
        LibraryItemId? baseLibraryItemId,
        StandardVersionId? standardVersionId,
        string? content)
    {
        Kind = kind;
        StudyId = studyId;
        VersionId = versionId;
        Version = version;
        BaseVersionId = baseVersionId;
        BaseVersion = baseVersion;
        LibraryItemId = libraryItemId;
        BaseLibraryItemId = baseLibraryItemId;
        StandardVersionId = standardVersionId;
        Content = content;
    }

    /// <summary>The kind of specification.</summary>
    public CandidateKind Kind { get; }

    /// <summary>The study scope for library-backed candidates (mapping specifications and rule packs); null when inferred from a protocol version.</summary>
    public StudyId? StudyId { get; }

    /// <summary>A stored candidate protocol version; null for unsaved content or library-item candidates.</summary>
    public ProtocolVersionId? VersionId { get; }

    /// <summary>Optional audit version pin for <see cref="VersionId"/>; null evaluates the latest stored version.</summary>
    public long? Version { get; }

    /// <summary>The base protocol version of unsaved protocol content; null for stored protocol candidates or library-item candidates.</summary>
    public ProtocolVersionId? BaseVersionId { get; }

    /// <summary>Optional audit version pin for <see cref="BaseVersionId"/>; null evaluates the latest stored base version.</summary>
    public long? BaseVersion { get; }

    /// <summary>A stored library-item candidate (mapping specification or rule pack); null for unsaved content or protocol candidates.</summary>
    public LibraryItemId? LibraryItemId { get; }

    /// <summary>Optional base library item to compare a mapping specification or rule pack candidate against.</summary>
    public LibraryItemId? BaseLibraryItemId { get; }

    /// <summary>Optional target standard version for an unsaved mapping specification candidate.</summary>
    public StandardVersionId? StandardVersionId { get; }

    /// <summary>Unsaved candidate JSON content; null for a stored candidate.</summary>
    public string? Content { get; }

    /// <summary>
    /// A stored protocol version, usually a draft, compared with the version it was forked from. A study's first
    /// version has no parent, so all of its elements are added.
    /// </summary>
    public static SimulationCandidate StoredProtocolVersion(ProtocolVersionId version, long? expectedVersion = null)
    {
        if (version.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version to simulate.", nameof(version));
        }

        if (expectedVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "A pinned version must be at least 1.");
        }

        return new SimulationCandidate(CandidateKind.ProtocolVersion, null, version, expectedVersion, null, null, null, null, null, null);
    }

    /// <summary>
    /// Protocol content that is not saved anywhere, compared with <paramref name="baseVersion"/>, which must have
    /// frozen content (in review or later) so the comparison is reproducible. The content must read as a protocol
    /// design, as a stored draft's must (ADR-2472).
    /// </summary>
    public static SimulationCandidate UnsavedProtocolVersion(ProtocolVersionId baseVersion, string content, long? expectedBaseVersion = null)
    {
        if (baseVersion.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version the content is compared with.", nameof(baseVersion));
        }

        if (expectedBaseVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedBaseVersion), expectedBaseVersion, "A pinned version must be at least 1.");
        }

        ArgumentNullException.ThrowIfNull(content);
        return new SimulationCandidate(CandidateKind.ProtocolVersion, null, null, null, baseVersion, expectedBaseVersion, null, null, null, content);
    }

    /// <summary>
    /// A stored mapping specification library item (draft or published) evaluated in <paramref name="studyId"/>
    /// against <paramref name="baseLibraryItem"/> (or its declared <c>extends</c> base when omitted).
    /// </summary>
    public static SimulationCandidate StoredMappingSpecification(
        StudyId studyId,
        LibraryItemId libraryItem,
        LibraryItemId? baseLibraryItem = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        if (libraryItem.IsEmpty)
        {
            throw new ArgumentException("Name the mapping specification library item to simulate.", nameof(libraryItem));
        }

        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        return new SimulationCandidate(CandidateKind.MappingSpecification, studyId, null, null, null, null, libraryItem, baseLibraryItem, null, null);
    }

    /// <summary>
    /// Unsaved mapping specification JSON content evaluated in <paramref name="studyId"/> against an optional frozen
    /// <paramref name="baseLibraryItem"/> (or its declared <c>extends</c> base).
    /// </summary>
    public static SimulationCandidate UnsavedMappingSpecification(
        StudyId studyId,
        string content,
        LibraryItemId? baseLibraryItem = null,
        StandardVersionId? standardVersion = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        ArgumentNullException.ThrowIfNull(content);
        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        if (standardVersion is { IsEmpty: true })
        {
            throw new ArgumentException("The standard version cannot be empty.", nameof(standardVersion));
        }

        return new SimulationCandidate(CandidateKind.MappingSpecification, studyId, null, null, null, null, null, baseLibraryItem, standardVersion, content);
    }

    /// <summary>
    /// A stored rule pack library item (draft or published) evaluated in <paramref name="studyId"/> against
    /// <paramref name="baseLibraryItem"/> (or the study's pinned rule packs when omitted).
    /// </summary>
    public static SimulationCandidate StoredRulePack(
        StudyId studyId,
        LibraryItemId libraryItem,
        LibraryItemId? baseLibraryItem = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        if (libraryItem.IsEmpty)
        {
            throw new ArgumentException("Name the rule pack library item to simulate.", nameof(libraryItem));
        }

        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        return new SimulationCandidate(CandidateKind.RulePack, studyId, null, null, null, null, libraryItem, baseLibraryItem, null, null);
    }

    /// <summary>
    /// Unsaved rule pack JSON content evaluated in <paramref name="studyId"/> against an optional frozen
    /// <paramref name="baseLibraryItem"/> (or the study's pinned rule packs when omitted).
    /// </summary>
    public static SimulationCandidate UnsavedRulePack(
        StudyId studyId,
        string content,
        LibraryItemId? baseLibraryItem = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        ArgumentNullException.ThrowIfNull(content);
        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        return new SimulationCandidate(CandidateKind.RulePack, studyId, null, null, null, null, null, baseLibraryItem, null, content);
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
