using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// The kernel's lineage reads for the Canvas Trace and Impact verbs (ADR-2540): a lineage walk, an object's
/// dependents, and a protocol amendment's impact, all through <see cref="IKernelLineage"/> (ADR-2471) so every screen
/// counts the same way (Canvas D2). Objects the reader may not see are masked in place and still counted. Each read
/// names its reader, so a request without an accountable person is refused.
/// </summary>
internal static class KernelLineageEndpoints
{
    internal static void Map(RouteGroupBuilder reads)
    {
        reads.MapGet("/nodes/{nodeRef}/lineage", TraceAsync)
            .WithName("KernelTrace")
            .WithSummary("Walks an object's lineage backward to its inputs or forward to its dependents.")
            .WithDescription("direction is Backward (default) or Forward; depth 1 to 50 (default 10); edge_kind repeats (for example DerivedFrom); valid_at and known_at read the graph as it was.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/nodes/{nodeRef}/dependents", DependentsAsync)
            .WithName("KernelDependents")
            .WithSummary("What depends on an object, grouped by kind with counts.")
            .WithDescription("Masked objects are counted like any other, so a count never depends on the reader. depth 1 to 50 (default 10); edge_kind repeats; valid_at and known_at read the graph as it was.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/protocol-versions/{versionRef}/impact", AmendmentImpactAsync)
            .WithName("KernelAmendmentImpact")
            .WithSummary("What depends on the version a protocol amendment changes (K-PROT-2).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<Results<Ok<KernelLineageResponse>, ProblemHttpResult>> TraceAsync(
        string nodeRef,
        [FromQuery] string? direction,
        [FromQuery] int? depth,
        [FromQuery(Name = "edge_kind")] string[]? edgeKind,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        IKernelLineage lineage,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
        }

        var walk = LineageDirection.Backward;
        if (direction is not null && !TryParseName(direction, out walk))
        {
            return KernelProblems.BadRequest("Invalid direction", $"direction must be {LineageDirection.Backward} or {LineageDirection.Forward}.");
        }

        var (steps, kinds, problem) = ReadWindow(depth, edgeKind);
        if (problem is not null)
        {
            return problem;
        }

        if (actor.Actor is not { } reader)
        {
            return NoReader();
        }

        var graph = await lineage.TraceAsync(
            KernelRequestTenant.Get(context), reader, new LineageQuery(node, walk, steps, kinds, validAt, knownAt), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(KernelLineageResponse.From(graph));
    }

    internal static async Task<Results<Ok<KernelDependentsResponse>, ProblemHttpResult>> DependentsAsync(
        string nodeRef,
        [FromQuery] int? depth,
        [FromQuery(Name = "edge_kind")] string[]? edgeKind,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        IKernelLineage lineage,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
        }

        var (steps, kinds, problem) = ReadWindow(depth, edgeKind);
        if (problem is not null)
        {
            return problem;
        }

        if (actor.Actor is not { } reader)
        {
            return NoReader();
        }

        var report = await lineage.DependentsAsync(
            KernelRequestTenant.Get(context), reader, new DependentsQuery(node, steps, kinds, validAt, knownAt), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(KernelDependentsResponse.From(report));
    }

    internal static async Task<Results<Ok<KernelAmendmentImpactResponse>, ProblemHttpResult>> AmendmentImpactAsync(
        string versionRef,
        [FromQuery] int? depth,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        HttpContext context,
        IProtocolVersionReader versions,
        IAmendmentImpact impact,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(versionRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var (steps, _, problem) = ReadWindow(depth, null);
        if (problem is not null)
        {
            return problem;
        }

        if (actor.Actor is not { } reader)
        {
            return NoReader();
        }

        var tenant = KernelRequestTenant.Get(context);
        var amended = ProtocolVersionId.FromGuid(node.Id);
        if (await versions.GetAsync(tenant, amended, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(node);
        }

        if (version.ParentVersionId is null)
        {
            return KernelProblems.BadRequest("Not an amendment", $"{node} is a study's first protocol version, which amends nothing.");
        }

        if (await impact.ComputeAsync(tenant, reader, amended, steps, validAt, knownAt, cancellationToken).ConfigureAwait(false)
            is not { } computed)
        {
            return KernelProblems.NotFound(node);
        }

        return TypedResults.Ok(KernelAmendmentImpactResponse.From(computed));
    }

    private static (int Depth, IReadOnlySet<LineageEdgeKind>? Kinds, ProblemHttpResult? Problem) ReadWindow(int? depth, string[]? edgeKinds)
    {
        var steps = depth ?? LineageQuery.DefaultDepth;
        if (steps is < 1 or > LineageQuery.MaxDepth)
        {
            return (steps, null, KernelProblems.BadRequest("Invalid depth", $"depth must be between 1 and {LineageQuery.MaxDepth}."));
        }

        if (edgeKinds is not { Length: > 0 })
        {
            return (steps, null, null);
        }

        var kinds = new HashSet<LineageEdgeKind>();
        foreach (var text in edgeKinds)
        {
            if (!TryParseName<LineageEdgeKind>(text, out var kind))
            {
                return (steps, null, KernelProblems.BadRequest(
                    "Invalid edge kind", $"'{text}' is not a lineage edge kind: expected one of {string.Join(", ", Enum.GetNames<LineageEdgeKind>())}."));
            }

            kinds.Add(kind);
        }

        return (steps, kinds, null);
    }

    /// <summary>Parses an enum member by name, ignoring case; numbers are not names.</summary>
    private static bool TryParseName<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum =>
        Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value) && !int.TryParse(text, out _);

    private static ProblemHttpResult NoReader() =>
        TypedResults.Problem(
            title: "No accountable reader",
            detail: "Lineage reads name their reader, and your sign-in names no accountable person.",
            statusCode: StatusCodes.Status403Forbidden);
}
