using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
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
/// Capability-keyed work-queue reads and definitions over HTTP (Canvas D10, PX-15, ADR-2486). Each queue is evaluated at
/// read time through <see cref="IWorkQueues"/> and <c>IAuthorizationProbe.CanIAsync</c> (never role names), enforcing
/// the 200-item candidate cap with <c>truncated</c> and rendering restricted (<see cref="MaskReason.NotPermitted"/>) or
/// blinded (<see cref="MaskReason.Blinded"/>) items as masked placeholders in place.
/// </summary>
public static class KernelWorkQueueEndpoints
{
    /// <summary>Maps <c>/work-queues</c> on the kernel route group (<see cref="KernelApiEndpointRouteBuilderExtensions.MapKernelApi"/>).</summary>
    public static RouteGroupBuilder MapKernelWorkQueues(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup("/work-queues").AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel work queues");

        group.MapGet(string.Empty, ReadAsync)
            .WithName("KernelReadWorkQueues")
            .WithSummary("Reads the caller's capability-keyed work queues in the current tenant or within a scope.")
            .WithDescription("within is an optional NodeRef (for example study:<uuid> or study_site:<uuid>); code repeats to filter queue codes; limit is 1 to 200 (default 50). Restricted or blinded items remain in place with masked set to NotPermitted or Blinded.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelErrors(KernelErrors.ActorUnresolved, WorkQueueErrors.QueryInvalid);

        group.MapPost(string.Empty, DefineAsync)
            .WithName("KernelDefineWorkQueue")
            .WithSummary("Defines the next version of a capability-keyed work queue in the caller's tenant (work_queue.define).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<DefineWorkQueue>();

        return kernel;
    }

    internal static async Task<Results<Ok<KernelWorkQueuesResponse>, ProblemHttpResult>> ReadAsync(
        [FromQuery] string? within,
        [FromQuery(Name = "code")] string[]? codes,
        [FromQuery] int? limit,
        HttpContext context,
        IKernelNavigator navigator,
        INodeVisibility visibility,
        ICurrentActor currentActor,
        IWorkQueues workQueues,
        IWorkItemTargetResolver targetResolver,
        CancellationToken cancellationToken)
    {
        NodeRef? withinRef = null;
        if (!string.IsNullOrWhiteSpace(within))
        {
            if (!NodeRef.TryParse(within, out var parsed))
            {
                return KernelProblems.InvalidNodeRef(within);
            }

            withinRef = parsed;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (withinRef is not null)
        {
            if (await navigator.ResolveAsync(tenant, withinRef, cancellationToken).ConfigureAwait(false) is not { } resolved)
            {
                return KernelProblems.NotFound(withinRef);
            }

            if (withinRef.Kind != NodeKinds.Tenant)
            {
                var masks = await KernelReadMasking.MaskAsync(visibility, currentActor, tenant, [resolved.Ref], cancellationToken)
                    .ConfigureAwait(false);
                if (masks.For(resolved.Ref) is { } mask)
                {
                    return KernelProblems.NotVisible($"You may not see {withinRef} ({mask}), so work queues within it are not shown.");
                }
            }
        }

        var pageLimit = limit ?? 50;
        var result = await workQueues.ReadAsync(
            tenant,
            new WorkQueueQuery(withinRef, codes is { Length: > 0 } ? codes : null, pageLimit),
            cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return KernelErrorEndpoints.Problem(error);
        }

        var queues = result.Value!;
        var visibleSubjects = queues
            .SelectMany(q => q.Items)
            .Where(i => i.Masked is null && i.Subject is not null)
            .Select(i => i.Subject!)
            .Distinct()
            .ToList();
        var targets = visibleSubjects.Count == 0
            ? new Dictionary<NodeRef, WorkItemTargetContext>()
            : await targetResolver.ResolveAsync(tenant, visibleSubjects, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(KernelWorkQueuesResponse.From(queues, targets));
    }

    internal static async Task<Results<Created<KernelWorkQueueDefinitionResponse>, ProblemHttpResult>> DefineAsync(
        KernelDefineWorkQueueRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Request body is required.");
        }

        if (!Enum.TryParse<VerbClass>(request.VerbClass, ignoreCase: true, out var verbClass)
            || !Enum.IsDefined(verbClass)
            || int.TryParse(request.VerbClass, out _))
        {
            return KernelProblems.BadRequest(
                "Invalid verb_class",
                $"'{request.VerbClass}' is not a valid verb class: expected one of {string.Join(", ", Enum.GetNames<VerbClass>())}.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var id = request.WorkQueueDefinitionId is { } guid && guid != Guid.Empty
            ? WorkQueueDefinitionId.FromGuid(guid)
            : WorkQueueDefinitionId.New();

        var result = await sender.SendAsync(
            new DefineWorkQueue(
                tenant,
                id,
                request.Code ?? string.Empty,
                request.Title ?? string.Empty,
                request.Capability ?? string.Empty,
                verbClass,
                request.Source ?? string.Empty,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return KernelErrorEndpoints.Problem(error);
        }

        var state = result.Value!;
        var response = KernelWorkQueueDefinitionResponse.From(state);
        return TypedResults.Created($"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/work-queues?code={Uri.EscapeDataString(state.Code)}", response);
    }
}

/// <summary>Request body for defining a tenant work queue (<see cref="DefineWorkQueue"/>).</summary>
/// <param name="Code">Lower snake_case queue code, for example <c>pending_queries</c>.</param>
/// <param name="Title">Human-readable queue title.</param>
/// <param name="Capability">Capability token (resource.action), never a role name.</param>
/// <param name="VerbClass">Capability verb class (<c>Read</c>, <c>Propose</c>, <c>Execute</c>, <c>Approve</c>, <c>Administer</c>).</param>
/// <param name="Source">Registered work-item source name.</param>
/// <param name="WorkQueueDefinitionId">Optional client-chosen definition identifier.</param>
public sealed record KernelDefineWorkQueueRequest(
    string? Code,
    string? Title,
    string? Capability,
    string? VerbClass,
    string? Source,
    Guid? WorkQueueDefinitionId = null);

/// <summary>Response returned after defining a work-queue version.</summary>
/// <param name="Definition">Unversioned NodeRef of the work-queue definition version.</param>
/// <param name="Code">Stable queue code.</param>
/// <param name="Version">Definition version number.</param>
/// <param name="Title">Queue title.</param>
/// <param name="Capability">Capability token required to see items.</param>
/// <param name="VerbClass">Capability verb class.</param>
/// <param name="Source">Registered work-item source name.</param>
public sealed record KernelWorkQueueDefinitionResponse(
    string Definition,
    string Code,
    int Version,
    string Title,
    string Capability,
    string VerbClass,
    string Source)
{
    internal static KernelWorkQueueDefinitionResponse From(WorkQueueDefinitionState state) =>
        new(
            NodeRef.For(state.WorkQueueDefinitionId).ToString(),
            state.Code,
            state.Version,
            state.Title,
            state.Capability,
            state.VerbClass.ToString(),
            state.Source);
}

/// <summary>Envelope of capability-keyed work queues returned to the signed-in caller.</summary>
/// <param name="Queues">The caller's work queues in code order.</param>
public sealed record KernelWorkQueuesResponse(IReadOnlyList<KernelWorkQueueResponse> Queues)
{
    internal static KernelWorkQueuesResponse From(
        IReadOnlyList<WorkQueue> queues,
        IReadOnlyDictionary<NodeRef, WorkItemTargetContext> targets) =>
        new([.. queues.Select(q => KernelWorkQueueResponse.From(q, targets))]);
}

/// <summary>One capability-keyed work queue for the signed-in caller.</summary>
/// <param name="Definition">Pinned NodeRef of the definition version computed from.</param>
/// <param name="Code">Queue code.</param>
/// <param name="Version">Definition version.</param>
/// <param name="Title">Queue title.</param>
/// <param name="Capability">Capability token required on each item.</param>
/// <param name="Items">Permitted items, oldest first, with restricted or blinded items masked in place.</param>
/// <param name="Truncated">True when more items or more than 200 source candidates exist beyond this read.</param>
public sealed record KernelWorkQueueResponse(
    string Definition,
    string Code,
    int Version,
    string Title,
    string Capability,
    IReadOnlyList<KernelWorkItemResponse> Items,
    bool Truncated)
{
    internal static KernelWorkQueueResponse From(
        WorkQueue queue,
        IReadOnlyDictionary<NodeRef, WorkItemTargetContext> targets) =>
        new(
            queue.Definition.ToString(),
            queue.Code,
            queue.Version,
            queue.Title,
            queue.Capability,
            [.. queue.Items.Select(i => KernelWorkItemResponse.From(i, targets))],
            queue.Truncated);
}

/// <summary>One item in a work queue; when <see cref="Masked"/> is non-null, <see cref="Subject"/>, <see cref="Title"/>, and <see cref="Target"/> are null.</summary>
/// <param name="Subject">Pinned NodeRef of the object due, or null when masked.</param>
/// <param name="SubjectKind">Object kind, preserved even when masked.</param>
/// <param name="Title">Human-readable description of what is due, or null when masked.</param>
/// <param name="Since">Timestamp when the item became due.</param>
/// <param name="Masked">Mask reason (<c>NotPermitted</c> or <c>Blinded</c>), or null when visible.</param>
/// <param name="Target">Deep-link navigation metadata for unmasked items, or null when masked.</param>
public sealed record KernelWorkItemResponse(
    string? Subject,
    string SubjectKind,
    string? Title,
    DateTimeOffset Since,
    string? Masked,
    KernelWorkItemTargetResponse? Target)
{
    internal static KernelWorkItemResponse From(
        WorkItem item,
        IReadOnlyDictionary<NodeRef, WorkItemTargetContext> targets)
    {
        if (item.Masked is not null || item.Subject is null)
        {
            return new KernelWorkItemResponse(
                null,
                item.SubjectKind,
                null,
                item.Since,
                item.Masked?.ToString(),
                null);
        }

        var target = targets.TryGetValue(item.Subject, out var ctx)
            ? KernelWorkItemTargetResponse.From(ctx)
            : null;
        return new KernelWorkItemResponse(
            item.Subject.ToString(),
            item.SubjectKind,
            item.Title,
            item.Since,
            null,
            target);
    }
}

/// <summary>Navigation target coordinates for an unmasked work item.</summary>
/// <param name="StudyRef">Owning study NodeRef.</param>
/// <param name="StudyId">Owning study UUID.</param>
/// <param name="EnrollmentId">Enrollment UUID when applicable.</param>
/// <param name="FormId">Collection instance (form) UUID when applicable.</param>
/// <param name="ObservationRef">Observation NodeRef when applicable.</param>
/// <param name="FindingRef">Validation finding NodeRef when applicable.</param>
/// <param name="ProtocolVersionRef">Protocol version NodeRef when applicable.</param>
/// <param name="ProposalRef">Proposal NodeRef when applicable.</param>
public sealed record KernelWorkItemTargetResponse(
    string? StudyRef,
    Guid? StudyId,
    Guid? EnrollmentId,
    Guid? FormId,
    string? ObservationRef,
    string? FindingRef,
    string? ProtocolVersionRef,
    string? ProposalRef = null)
{
    internal static KernelWorkItemTargetResponse From(WorkItemTargetContext context) =>
        new(
            context.StudyRef,
            context.StudyId,
            context.EnrollmentId,
            context.FormId,
            context.ObservationRef,
            context.FindingRef,
            context.ProtocolVersionRef,
            context.ProposalRef);
}
