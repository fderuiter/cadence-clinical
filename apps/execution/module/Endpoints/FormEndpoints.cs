using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// eCRF forms (ADR-2622): open a form for a participant's visit, read it with its values, locks, assertions and
/// signatures, and save a page of values. Every write is a kernel command sent through
/// <see cref="IKernelCommandSender"/>, so the kernel authorizes it, audits it and keeps the values as observations; the
/// reason comes from the signed identity, the idempotency key from the <c>Idempotency-Key</c> header, and the tenant from
/// <see cref="KernelTenantFilter"/>.
/// </summary>
internal static class FormEndpoints
{
    /// <summary>The route prefix of the form endpoints, which the gateway sends to the kernel host.</summary>
    public const string RoutePrefix = "/api/v1/execution/forms";

    /// <summary>The header carrying the command's idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapFormEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(RoutePrefix)
            .RequireSignedInCaller("Every read checks the caller may see the form's enrollment; every write is a kernel command authorized against the caller's grants.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution forms");

        group.MapPost(string.Empty, OpenAsync)
            .WithName("ExecutionOpenForm")
            .WithSummary("Opens a form for a participant's visit under the protocol version in force (collection.open).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<OpenCollectionInstance>();

        group.MapGet("/{formId:guid}", GetAsync)
            .WithName("ExecutionGetForm")
            .WithSummary("Reads a form: its items in layout order with their values, completeness, locks, assertions and signatures.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{formId:guid}/values", SaveAsync)
            .WithName("ExecutionSaveFormValues")
            .WithSummary("Saves a page of values: all of them or, on one stale revision or invalid value, none (collection.record).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<RecordCollectedValues>();

        return endpoints;
    }

    internal static async Task<Results<Created<FormStateResponse>, ProblemHttpResult>> OpenAsync(
        OpenFormRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null || request.EnrollmentId == Guid.Empty || string.IsNullOrWhiteSpace(request.CollectionSpecId))
        {
            return BadRequest("Invalid form", "Name the enrollment and the CollectionSpec the form collects.");
        }

        var form = request.FormId is { } given && given != Guid.Empty ? CollectionInstanceId.FromGuid(given) : CollectionInstanceId.New();
        var result = await sender.SendAsync(
            new OpenCollectionInstance(
                KernelRequestTenant.Get(context),
                form,
                EnrollmentId.FromGuid(request.EnrollmentId),
                request.CollectionSpecId,
                request.PlannedActivityId,
                request.CollectedAt,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Created($"{RoutePrefix}/{state.CollectionInstanceId.Value:D}", FormStateResponse.From(state))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<FormResponse>, ProblemHttpResult>> GetAsync(
        Guid formId,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        if (await forms.FindAsync(tenant, CollectionInstanceId.FromGuid(formId), cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return NotFound(formId);
        }

        // A form is seen by whoever may see its enrollment (ADR-2457), as its values are.
        if (!await access.CanSeeAsync(tenant, NodeRef.For(record.EnrollmentId), cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Problem(
                title: "Not visible", detail: $"You may not see form {formId:D}.", statusCode: StatusCodes.Status403Forbidden);
        }

        // The layout the module snapshotted when the kernel opened the form; until that event is delivered, the
        // specification's own order, which is the same.
        string[]? layout;
        await using (var db = contexts.Create(tenant))
        {
            layout = await db.FormInstances
                .Where(f => f.Id == formId)
                .Select(f => EF.Property<string[]>(f, ExecutionModuleDbContext.ItemOrderField))
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok(FormResponse.From(record, layout));
    }

    internal static async Task<Results<Ok<FormSavedResponse>, ProblemHttpResult>> SaveAsync(
        Guid formId,
        SaveFormValuesRequest request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request?.Values is not { Count: > 0 } values
            || values.Any(v => v is null || string.IsNullOrWhiteSpace(v.ItemId) || v.Value is null || v.ExpectedRevision < 0))
        {
            return BadRequest("Invalid save", "Send at least one value, each naming its item, its value and the revision it was edited from.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var id = CollectionInstanceId.FromGuid(formId);

        // The save is authorized against the form's enrollment, which the client does not send.
        if (await forms.FindAsync(tenant, id, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return NotFound(formId);
        }

        var result = await sender.SendAsync(
            new RecordCollectedValues(
                tenant,
                id,
                record.EnrollmentId,
                [.. values.Select(v => new CollectedValue(
                    v.ItemId,
                    v.Value!,
                    v.ExpectedRevision,
                    v.ObservationId is { } observation ? ObservationId.FromGuid(observation) : null,
                    v.ObservedAt))],
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state ? TypedResults.Ok(FormSavedResponse.From(state)) : KernelErrorEndpoints.Problem(result.Error!);
    }

    private static string IdempotencyKey(HttpContext context) => context.Request.Headers[IdempotencyKeyHeader].ToString().Trim();

    private static ProblemHttpResult BadRequest(string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(Guid formId) =>
        TypedResults.Problem(title: "Not found", detail: $"Form {formId:D} does not exist.", statusCode: StatusCodes.Status404NotFound);
}
