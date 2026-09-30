using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Activity;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace Cadence.Kernel.Api;

/// <summary>
/// The reader-facing activity stream over HTTP (ADR-2485, Canvas PX-11, ADR-2540 §4.7): what happened in a scope,
/// newest first, through <see cref="IActivityStream"/>. A scope the reader may not see is refused, because its activity
/// would describe it; inside a visible scope an entry about an object the reader may not see keeps its place masked.
/// </summary>
internal static class KernelActivityEndpoints
{
    internal static void Map(RouteGroupBuilder reads)
    {
        reads.MapGet("/activity", ReadAsync)
            .WithName("KernelActivity")
            .WithSummary("What happened in a scope, newest first.")
            .WithDescription("scope is any NodeRef (the whole tenant when absent); family repeats (Knowledge, Execution, Evidence, Trust, Quality, Intelligence); actor filters by who did it; from is the earliest recorded time; before is the previous page's next cursor; limit 1 to 200 (default 50). Entries about objects you may not see are masked in place.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<Results<Ok<KernelActivityPageResponse>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? scope,
        [FromQuery] string[]? family,
        [FromQuery] string? actor,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] string? before,
        [FromQuery] int? limit,
        HttpContext context,
        IKernelNavigator navigator,
        INodeVisibility visibility,
        IActivityStream activity,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        NodeRef? scopeRef = null;
        if (scope is not null)
        {
            if (!NodeRef.TryParse(scope, out var parsed))
            {
                return KernelProblems.InvalidNodeRef(scope);
            }

            scopeRef = parsed;
        }

        var families = new HashSet<EventFamily>();
        foreach (var text in family ?? [])
        {
            if (!Enum.TryParse<EventFamily>(text, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(text, out _))
            {
                return KernelProblems.BadRequest(
                    "Invalid family", $"'{text}' is not an event family: expected one of {string.Join(", ", Enum.GetNames<EventFamily>())}.");
            }

            families.Add(parsed);
        }

        var pageSize = limit ?? 50;
        if (pageSize is < 1 or > ActivityQuery.MaxLimit)
        {
            return KernelProblems.BadRequest("Invalid limit", $"limit must be between 1 and {ActivityQuery.MaxLimit}.");
        }

        ActivityCursor? cursor = null;
        if (before is not null && !ActivityCursorToken.TryRead(before, out cursor))
        {
            return KernelProblems.BadRequest("Invalid cursor", "before must be the next cursor of an earlier page, as it was returned.");
        }

        if (currentActor.Actor is null)
        {
            return TypedResults.Problem(
                title: "No accountable reader",
                detail: "Activity is read by a named reader, and your sign-in names no accountable person.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var tenant = KernelRequestTenant.Get(context);
        NodeRef gate = NodeRef.For(tenant);
        if (scopeRef is not null)
        {
            if (await navigator.ResolveAsync(tenant, scopeRef, cancellationToken).ConfigureAwait(false) is not { } summary)
            {
                return KernelProblems.NotFound(scopeRef);
            }

            gate = summary.Ref;
        }

        // A scope's activity describes the scope, so a scope the reader may not see is refused, not masked (as for a
        // hidden study's protocol versions). The tenant is reference data any grant in effect shows (ADR-2457).
        var masks = await KernelReadMasking.MaskAsync(visibility, currentActor, tenant, [gate], cancellationToken).ConfigureAwait(false);
        if (masks.For(gate) is { } mask)
        {
            return KernelProblems.NotVisible(scopeRef is null
                ? $"You hold no grant in effect in this tenant ({mask}), so its activity is not shown."
                : $"You can't see {scopeRef} ({mask}), so its activity is not shown.");
        }

        var page = await activity.ReadAsync(
            tenant,
            new ActivityQuery(scopeRef, families.Count == 0 ? null : families, actor, from, cursor, pageSize),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelActivityPageResponse.From(page));
    }
}

/// <summary>
/// The opaque <c>next</c>/<c>before</c> cursor: the last entry's recorded time (UTC ticks) and event id, base64url.
/// Clients pass it back as they received it.
/// </summary>
internal static class ActivityCursorToken
{
    private const int Length = sizeof(long) + 16;

    public static string Write(ActivityCursor cursor)
    {
        Span<byte> bytes = stackalloc byte[Length];
        BinaryPrimitives.WriteInt64BigEndian(bytes, cursor.Recorded.UtcTicks);
        cursor.EventId.TryWriteBytes(bytes[sizeof(long)..], bigEndian: true, out _);
        return WebEncoders.Base64UrlEncode(bytes.ToArray());
    }

    public static bool TryRead(string text, out ActivityCursor? cursor)
    {
        cursor = null;
        byte[] bytes;
        try
        {
            bytes = WebEncoders.Base64UrlDecode(text);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != Length)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new ActivityCursor(new DateTimeOffset(ticks, TimeSpan.Zero), new Guid(bytes.AsSpan(sizeof(long)), bigEndian: true));
        return true;
    }
}

/// <summary>A page of activity; <c>next</c> is the cursor for the following page, or null on the last page.</summary>
public sealed record KernelActivityPageResponse(IReadOnlyList<KernelActivityEntryResponse> Entries, string? Next)
{
    internal static KernelActivityPageResponse From(ActivityPage page) =>
        new(
            [.. page.Entries.Select(KernelActivityEntryResponse.From)],
            page.Next is { } next ? ActivityCursorToken.Write(next) : null);
}

/// <summary>
/// One thing that happened. <c>subject</c> is the object's NodeRef at the version the event produced; it is null when
/// the event is about no object, or when the object is hidden from the reader, and <c>mask</c> then says why.
/// </summary>
public sealed record KernelActivityEntryResponse(
    Guid EventId,
    string Type,
    string Family,
    DateTimeOffset Recorded,
    DateTimeOffset? OccurredValid,
    string ActorId,
    string? SubjectKind,
    string? Subject,
    string? Mask,
    Guid CorrelationId)
{
    internal static KernelActivityEntryResponse From(ActivityEntry entry) =>
        new(
            entry.EventId,
            entry.Type,
            entry.Family.ToString(),
            entry.Recorded,
            entry.OccurredValid,
            entry.ActorId,
            entry.SubjectKind,
            entry.Subject?.ToString(),
            entry.Masked?.ToString(),
            entry.CorrelationId);
}
