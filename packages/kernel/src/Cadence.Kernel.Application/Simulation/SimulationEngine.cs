using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>A protocol version as simulation reads it.</summary>
/// <param name="Id">The version.</param>
/// <param name="StudyId">Its study.</param>
/// <param name="Number">Its number in the study.</param>
/// <param name="Status">Its lifecycle status.</param>
/// <param name="ParentVersionId">The version it was forked from, or null for the first version.</param>
/// <param name="Content">Its content, a JSON object.</param>
/// <param name="ContentHash">Its frozen content hash, or null while it is a draft.</param>
/// <param name="VersionIndex">Its audit version, which pins references to it.</param>
public sealed record ProtocolVersionSnapshot(
    ProtocolVersionId Id,
    StudyId StudyId,
    int Number,
    ProtocolVersionStatus Status,
    ProtocolVersionId? ParentVersionId,
    string Content,
    string? ContentHash,
    long VersionIndex);

/// <summary>
/// The reads simulation makes. Implemented by kernel persistence with no-tracking queries under the tenant's Row-Level
/// Security; it has no way to write (K-SIM-1).
/// </summary>
public interface ISimulationInputs
{
    /// <summary>The protocol version, or null when the tenant has none with that id.</summary>
    Task<ProtocolVersionSnapshot?> FindProtocolVersionAsync(TenantId tenant, ProtocolVersionId id, CancellationToken cancellationToken);

    /// <summary>
    /// What depends on any version of <paramref name="baseVersion"/> at <paramref name="asOf"/>, through the kernel's one
    /// dependents read, without walking through <paramref name="candidate"/> (a stored fork depends on its parent).
    /// From the base version the read enters only the design elements in <paramref name="impactedElements"/>
    /// (<see cref="ProtocolElementImpact"/>, ADR-2474). Objects <paramref name="reader"/> may not see are
    /// counted and masked.
    /// </summary>
    Task<DependentsReport> DependentsAsync(
        TenantId tenant,
        Actor reader,
        ProtocolVersionId baseVersion,
        ProtocolVersionId? candidate,
        IReadOnlySet<NodeRef> impactedElements,
        SimulationAsOf asOf,
        CancellationToken cancellationToken);
}

/// <summary>
/// The simulation engine (ADR-2480). It reads the candidate and its base, checks <c>simulation.run</c> at the
/// candidate's study, masks what the actor may not see through <see cref="INodeVisibility"/>, evaluates the pure
/// functions and seals the result with its input and result hashes. It depends on no command sender, unit of work
/// or persistence type, and an architecture test keeps it that way (K-SIM-1).
/// </summary>
public sealed class SimulationEngine(
    ISimulationInputs inputs,
    IAuthorizationProbe probe,
    INodeVisibility visibility,
    ICurrentActor currentActor,
    IClock clock) : ISimulationEngine
{
    /// <summary>The section the structural diff is reported in, and masked as.</summary>
    public const string StructuralDiffPart = "structural_diff";

    /// <summary>The section the dependents are reported in, and masked as.</summary>
    public const string DependentsPart = "dependents";

    /// <summary>The section the schedule changes are reported in, and masked as.</summary>
    public const string ScheduleChangesPart = "schedule_changes";

    /// <summary>The engine version recorded for matrix gap analysis; bump it when its output changes.</summary>
    public static readonly EngineVersion MatrixGapEngine = new("protocol.matrix_gap", "1");

    /// <summary>The section the matrix gaps are reported in, and masked as.</summary>
    public const string MatrixGapsPart = "matrix_gaps";

    /// <summary>The engine version recorded for the expected schedule (K-TIME-7); bump it when its output changes.</summary>
    public static readonly EngineVersion ScheduleEngine = new("protocol.expected_schedule", "1");

    /// <summary>How many design problems an invalid-content error lists.</summary>
    private const int MaxReportedProblems = 10;

    /// <summary>The engine version recorded when a result carries dependents.</summary>
    public static readonly EngineVersion DependentsEngine = new("lineage.dependents", "2");

    public async Task<CommandResult<SimulationResult>> SimulateAsync(
        TenantId tenant, SimulationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("Simulation runs within a tenant.", nameof(tenant));
        }

        if (currentActor.Actor is not { } actor)
        {
            return KernelErrors.ActorUnresolved;
        }

        var now = clock.UtcNow;
        var asOf = new SimulationAsOf(request.ValidAt ?? now, request.KnownAt ?? now);

        var loaded = await LoadAsync(tenant, request.Candidate, cancellationToken).ConfigureAwait(false);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var (candidate, baseVersion) = loaded.Value;
        var scope = NodeRef.For(candidate.StudyId);
        var decision = await probe.CanIAsync(tenant, SimulationCapabilities.Run, scope, cancellationToken).ConfigureAwait(false);
        if (!decision.Permitted)
        {
            return KernelErrors.AuthorizationDenied.WithMessage(decision.Reason ?? KernelErrors.AuthorizationDenied.Message)
                with
            { Explanation = decision.Explain(SimulationCapabilities.Run.Token, scope) };
        }

        var versionRef = candidate.Id is { } storedId ? NodeRef.For(storedId, candidate.VersionIndex) : null;
        var baseRef = baseVersion is null ? null : NodeRef.For(baseVersion.Id, baseVersion.VersionIndex);
        var hidden = await visibility.MaskAsync(
            tenant, actor, [.. new[] { scope, versionRef, baseRef }.OfType<NodeRef>()], cancellationToken).ConfigureAwait(false);
        var mask = new[] { scope, versionRef, baseRef }
            .OfType<NodeRef>()
            .Select(node => hidden.TryGetValue(node, out var reason) ? reason : (MaskReason?)null)
            .FirstOrDefault(reason => reason is not null);

        var engines = new List<EngineVersion> { ProtocolStructuralDiff.Engine, ScheduleEngine };
        CandidateSummary summary;
        IReadOnlyList<ElementChange> diff;
        IReadOnlyList<ScheduleChange> schedule;
        IReadOnlyList<MatrixGapFinding> matrixGaps;
        IReadOnlyList<MaskedPart> masked;
        DependentsReport? dependents = null;
        if (mask is { } reason)
        {
            // Nothing derived from content the actor may not read is computed, hashed or shown (K-SIM-4).
            summary = new CandidateSummary(CandidateKind.ProtocolVersion, null, null, null, null);
            diff = [];
            schedule = [];
            matrixGaps = [];
            masked =
            [
                new MaskedPart(DependentsPart, reason),
                new MaskedPart(ScheduleChangesPart, reason),
                new MaskedPart(StructuralDiffPart, reason),
            ];
        }
        else
        {
            var compared = ProtocolStructuralDiff.Compare(baseVersion?.Content, candidate.Content);
            if (compared.Error is { } diffError)
            {
                return diffError;
            }

            summary = new CandidateSummary(
                CandidateKind.ProtocolVersion, versionRef, baseRef, candidate.ContentHash, baseVersion?.ContentHash);
            diff = compared.Value!;
            schedule = ScheduleChanges(baseVersion?.Content, candidate.Content);
            matrixGaps = EvaluateMatrixGaps(candidate.Content);
            if (matrixGaps.Any(g => g.Kind != MatrixGapKind.IchM11Violation))
            {
                engines.Add(MatrixGapEngine);
            }
            masked = [];
            if (diff.Count > 0 && baseVersion is not null)
            {
                var impacted = ProtocolElementImpact.Impacted(baseVersion.StudyId, diff);
                dependents = await inputs.DependentsAsync(tenant, actor, baseVersion.Id, candidate.Id, impacted, asOf, cancellationToken)
                    .ConfigureAwait(false);
                engines.Add(DependentsEngine);
            }
        }

        engines.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        var inputHash = ContentHash.Compute(InputDocument(asOf, summary, engines, masked).ToJsonString());
        var resultHash = ContentHash.Compute(
            ResultDocument(scope, asOf, summary, engines, inputHash, diff, schedule, matrixGaps, masked, dependents).ToJsonString());
        return CommandResult<SimulationResult>.Success(
            new SimulationResult(scope, asOf, summary, engines, inputHash, resultHash, diff, schedule, masked, dependents, matrixGaps));
    }

    /// <summary>
    /// The timed activities added to, removed from or moved on the expected schedule (K-TIME-7), on the study-day axis.
    /// Both contents are protocol designs: a stored version's by the protocol commands, unsaved content by
    /// <see cref="LoadAsync"/>.
    /// </summary>
    private static List<ScheduleChange> ScheduleChanges(string? baseContent, string candidateContent)
    {
        var before = baseContent is null ? [] : ExpectedSchedule.Plan(DesignOf(baseContent));
        var after = ExpectedSchedule.Plan(DesignOf(candidateContent));
        var beforeById = before.ToDictionary(v => v.ActivityId, StringComparer.Ordinal);
        var afterById = after.ToDictionary(v => v.ActivityId, StringComparer.Ordinal);
        var changes = new List<ScheduleChange>();
        foreach (var id in beforeById.Keys.Union(afterById.Keys).Order(StringComparer.Ordinal))
        {
            var old = beforeById.GetValueOrDefault(id);
            var @new = afterById.GetValueOrDefault(id);
            if (old is null)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Added, null, @new));
            }
            else if (@new is null)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Removed, old, null));
            }
            else if (old.EpochId != @new.EpochId || old.StudyDay != @new.StudyDay
                || old.WindowBefore != @new.WindowBefore || old.WindowAfter != @new.WindowAfter)
            {
                changes.Add(new ScheduleChange(id, ElementChangeKind.Changed, old, @new));
            }
        }

        return changes;

        static ProtocolDesign DesignOf(string content) =>
            ProtocolDesignDocument.Read(content).Design
            ?? throw new InvalidOperationException("Simulation compared content that is not a protocol design.");
    }

    private async Task<CommandResult<(Candidate Candidate, ProtocolVersionSnapshot? Base)>> LoadAsync(
        TenantId tenant, SimulationCandidate request, CancellationToken cancellationToken)
    {
        if (request.VersionId is { } versionId)
        {
            var stored = await inputs.FindProtocolVersionAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                return SimulationErrors.VersionUnknown;
            }

            ProtocolVersionSnapshot? parent = null;
            if (stored.ParentVersionId is { } parentId)
            {
                parent = await inputs.FindProtocolVersionAsync(tenant, parentId, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    return SimulationErrors.VersionUnknown.WithMessage("The protocol version's parent does not exist.");
                }
            }

            var hash = stored.ContentHash ?? ContentHash.Compute(stored.Content);
            return CommandResult<(Candidate, ProtocolVersionSnapshot?)>.Success(
                (new Candidate(stored.Id, stored.StudyId, stored.Content, hash, stored.VersionIndex), parent));
        }

        var baseId = request.BaseVersionId!.Value;
        var baseVersion = await inputs.FindProtocolVersionAsync(tenant, baseId, cancellationToken).ConfigureAwait(false);
        if (baseVersion is null)
        {
            return SimulationErrors.VersionUnknown;
        }

        if (baseVersion.ContentHash is null)
        {
            return SimulationErrors.BaseNotFrozen;
        }

        // Unsaved content is held to the same rule as a stored draft: it must read as a protocol design (ADR-2472).
        var read = ProtocolDesignDocument.Read(request.Content!);
        if (!read.IsValid)
        {
            var more = read.Problems.Count > MaxReportedProblems ? $" (and {read.Problems.Count - MaxReportedProblems} more)" : string.Empty;
            return SimulationErrors.ContentInvalid.WithMessage(
                $"{SimulationErrors.ContentInvalid.Message} {string.Join(" ", read.Problems.Take(MaxReportedProblems))}{more}");
        }

        return CommandResult<(Candidate, ProtocolVersionSnapshot?)>.Success(
            (new Candidate(null, baseVersion.StudyId, request.Content!, ContentHash.Compute(request.Content!), 0), baseVersion));
    }

    /// <summary>What the result depends on (K-SIM-3). A masked input is represented by its mask, never its hash.</summary>
    private static JsonObject InputDocument(
        SimulationAsOf asOf, CandidateSummary candidate, IReadOnlyList<EngineVersion> engines, IReadOnlyList<MaskedPart> masked) => new()
        {
            ["as_of"] = AsOfNode(asOf),
            ["candidate"] = CandidateNode(candidate),
            ["engines"] = new JsonArray([.. engines.Select(e => (JsonNode)new JsonObject { ["name"] = e.Name, ["version"] = e.Version })]),
            ["masked"] = MaskedNode(masked),
        };

    private static IReadOnlyList<MatrixGapFinding> EvaluateMatrixGaps(string content)
    {
        var read = ProtocolDesignDocument.Read(content);
        return read.Design is { } design ? MatrixGapAnalyzer.Analyze(design) : [];
    }

    private static JsonObject ResultDocument(
        NodeRef scope,
        SimulationAsOf asOf,
        CandidateSummary candidate,
        IReadOnlyList<EngineVersion> engines,
        string inputHash,
        IReadOnlyList<ElementChange> diff,
        IReadOnlyList<ScheduleChange> schedule,
        IReadOnlyList<MatrixGapFinding> matrixGaps,
        IReadOnlyList<MaskedPart> masked,
        DependentsReport? dependents)
    {
        var document = InputDocument(asOf, candidate, engines, masked);
        document["scope"] = scope.ToString();
        document["input_hash"] = inputHash;
        document["structural_diff"] = new JsonArray([.. diff.Select(change => (JsonNode)new JsonObject
        {
            ["element_id"] = change.ElementId,
            ["instance_type"] = change.InstanceType,
            ["change"] = change.Change.ToString(),
            ["properties"] = new JsonArray([.. change.Properties.Select(p => (JsonNode?)p)]),
        })]);
        document["schedule_changes"] = new JsonArray([.. schedule.Select(change => (JsonNode)new JsonObject
        {
            ["activity_id"] = change.ActivityId,
            ["change"] = change.Change.ToString(),
            ["before"] = PlannedVisitNode(change.Before),
            ["after"] = PlannedVisitNode(change.After),
        })]);
        document["matrix_gaps"] = new JsonArray([.. matrixGaps.Select(gap => (JsonNode)new JsonObject
        {
            ["element_id"] = gap.ElementId,
            ["kind"] = gap.Kind.ToString(),
            ["severity"] = gap.Severity.ToString(),
            ["json_pointer_path"] = gap.JsonPointerPath,
            ["description"] = gap.Description,
            ["recommendation"] = gap.Recommendation,
        })]);
        document["dependents"] = dependents is null ? null : new JsonObject
        {
            ["start"] = dependents.Start,
            ["depth"] = dependents.Depth,
            ["total"] = dependents.Total,
            ["truncated"] = dependents.Truncated,
            ["groups"] = new JsonArray([.. dependents.Groups.Select(group => (JsonNode)new JsonObject
            {
                ["kind"] = group.Kind,
                ["count"] = group.Count,
                ["objects"] = new JsonArray([.. group.Objects.Select(o => (JsonNode)new JsonObject
                {
                    ["handle"] = o.Handle,
                    ["mask"] = o.Mask?.ToString(),
                    ["distance"] = o.Distance,
                })]),
            })]),
        };
        return document;
    }

    private static JsonObject? PlannedVisitNode(PlannedVisit? visit) => visit is null ? null : new JsonObject
    {
        ["epoch_id"] = visit.EpochId,
        ["study_day"] = visit.StudyDay,
        ["window_before"] = visit.WindowBefore,
        ["window_after"] = visit.WindowAfter,
    };

    private static JsonObject AsOfNode(SimulationAsOf asOf) => new()
    {
        ["valid_at"] = asOf.ValidAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        ["known_at"] = asOf.KnownAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
    };

    private static JsonObject CandidateNode(CandidateSummary candidate) => new()
    {
        ["kind"] = candidate.Kind.ToString(),
        ["version"] = candidate.Version?.ToString(),
        ["base_version"] = candidate.BaseVersion?.ToString(),
        ["content_hash"] = candidate.ContentHash,
        ["base_content_hash"] = candidate.BaseContentHash,
    };

    private static JsonArray MaskedNode(IReadOnlyList<MaskedPart> masked) =>
        new([.. masked.Select(m => (JsonNode)new JsonObject { ["part"] = m.Part, ["reason"] = m.Reason.ToString() })]);

    private sealed record Candidate(ProtocolVersionId? Id, StudyId StudyId, string Content, string ContentHash, long VersionIndex);
}

/// <summary>Registers the simulation engine.</summary>
public static class SimulationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISimulationEngine"/>. The host also registers kernel persistence's inputs
    /// (<c>AddKernelSimulationInputs()</c>), <see cref="IAuthorizationProbe"/> (<c>AddKernelAuthorization()</c>) and an
    /// <see cref="INodeVisibility"/>.
    /// </summary>
    public static IServiceCollection AddKernelSimulation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ISimulationEngine, SimulationEngine>();
        return services;
    }
}
