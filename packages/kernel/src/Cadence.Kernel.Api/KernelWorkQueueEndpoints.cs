using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.WorkQueues;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// The signed-in person's work queues over HTTP (ADR-2486, Canvas D10 and PX-15, ADR-2540 §4.8): one queue per current
/// definition, holding only the items the person may act on now, through <see cref="IWorkQueues"/>. A <c>within</c>
/// scope the person may not see is refused, as for the activity stream; an item whose object the person may not see
/// keeps its place masked.
/// </summary>
internal static class KernelWorkQueueEndpoints
{
    internal static void Map(RouteGroupBuilder reads)
    {
        reads.MapGet("/work-queues", ReadAsync)
            .WithName("KernelWorkQueues")
            .WithSummary("The caller's work queues: what is due that the caller may act on now.")
            .WithDescription("within is any NodeRef (a study, a site; the whole tenant when absent); code repeats to pick queues; limit is the items per queue, 1 to 200 (default 50). Each item was permitted for the caller by the same evaluator as CanI (ADR-2483).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelWorkQueueResponse>>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? within,
        [FromQuery] string[]? code,
        [FromQuery] int? limit,
        HttpContext context,
        IKernelNavigator navigator,
        INodeVisibility visibility,
        IWorkQueues queues,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        NodeRef? scope = null;
        if (within is not null)
        {
            if (!NodeRef.TryParse(within, out var parsed))
            {
                return KernelProblems.InvalidNodeRef(within);
            }

            scope = parsed;
        }

        var pageSize = limit ?? 50;
        if (pageSize is < 1 or > WorkQueueQuery.MaxLimit)
        {
            return KernelProblems.BadRequest("Invalid limit", $"limit must be between 1 and {WorkQueueQuery.MaxLimit}.");
        }

        if (currentActor.Actor is null)
        {
            return TypedResults.Problem(
                title: "No accountable reader",
                detail: "Work queues are read for a named person, and your sign-in names no accountable person.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (scope is not null)
        {
            if (await navigator.ResolveAsync(tenant, scope, cancellationToken).ConfigureAwait(false) is not { } summary)
            {
                return KernelProblems.NotFound(scope);
            }

            var masks = await KernelReadMasking.MaskAsync(visibility, currentActor, tenant, [summary.Ref], cancellationToken).ConfigureAwait(false);
            if (masks.For(summary.Ref) is { } mask)
            {
                return KernelProblems.NotVisible($"You can't see {scope} ({mask}), so its work queues are not read.");
            }
        }

        var codes = code is { Length: > 0 } ? code.Distinct(StringComparer.Ordinal).ToList() : null;
        var result = await queues.ReadAsync(tenant, new WorkQueueQuery(scope, codes, pageSize), cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return TypedResults.Problem(title: "Work queues unavailable", detail: error.Message, statusCode: error.HttpStatus);
        }

        return TypedResults.Ok<IReadOnlyList<KernelWorkQueueResponse>>([.. result.Value!.Select(KernelWorkQueueResponse.From)]);
    }
}

/// <summary>
/// One of the caller's queues: the definition version it was computed from, its code, title and capability, and the
/// items due, oldest first. <c>truncated</c> says there may be more than were returned.
/// </summary>
public sealed record KernelWorkQueueResponse(
    string Definition,
    string Code,
    int Version,
    string Title,
    string Capability,
    IReadOnlyList<KernelWorkItemResponse> Items,
    bool Truncated)
{
    internal static KernelWorkQueueResponse From(WorkQueue queue) =>
        new(
            queue.Definition.ToString(),
            queue.Code,
            queue.Version,
            queue.Title,
            queue.Capability,
            [.. queue.Items.Select(KernelWorkItemResponse.From)],
            queue.Truncated);
}

/// <summary>
/// One item due. <c>subject</c> and <c>title</c> are null when the caller may not see the object, and <c>mask</c> then
/// says why; the kind and the time stay.
/// </summary>
public sealed record KernelWorkItemResponse(string? Subject, string SubjectKind, string? Title, DateTimeOffset Since, string? Mask)
{
    internal static KernelWorkItemResponse From(WorkItem item) =>
        new(item.Subject?.ToString(), item.SubjectKind, item.Title, item.Since, item.Masked?.ToString());
}
