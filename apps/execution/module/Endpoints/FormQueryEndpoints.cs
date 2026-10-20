using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// Data queries about eCRF values (#5461): raise a query on the current revision of a form's value, list a form's or an
/// enrollment's queries with whether the value changed since, and answer, reopen, close or cancel one. Every write is a
/// module command sent through <see cref="IKernelCommandSender"/>, so the kernel authorizes it against the caller's
/// grants and audits it; there are no batch routes, so a client acting on several queries sends one command each.
/// </summary>
internal static class FormQueryEndpoints
{
    public static IEndpointRouteBuilder MapFormQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(FormEndpoints.RoutePrefix)
            .RequireSignedInCaller("Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution queries");

        group.MapGet("/queries", ListForEnrollmentAsync)
            .WithName("ExecutionListEnrollmentQueries")
            .WithSummary("Lists the queries on every form of an enrollment, oldest first.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{formId:guid}/queries", ListForFormAsync)
            .WithName("ExecutionListFormQueries")
            .WithSummary("Lists a form's queries, oldest first, each with whether its value changed since it was raised.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{formId:guid}/queries", RaiseAsync)
            .WithName("ExecutionRaiseFormQuery")
            .WithSummary("Raises a query on the current revision of one of the form's values (execution.query.raise).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<RaiseFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/answer", AnswerAsync)
            .WithName("ExecutionAnswerFormQuery")
            .WithSummary("Answers an open query (execution.query.answer).")
            .ProducesKernelCommand<AnswerFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/reopen", ReopenAsync)
            .WithName("ExecutionReopenFormQuery")
            .WithSummary("Reopens an answered or closed query with why it is not settled (execution.query.manage).")
            .ProducesKernelCommand<ReopenFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/close", CloseAsync)
            .WithName("ExecutionCloseFormQuery")
            .WithSummary("Closes an open or answered query; the change reason is the closing note (execution.query.manage).")
            .ProducesKernelCommand<CloseFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/cancel", CancelAsync)
            .WithName("ExecutionCancelFormQuery")
            .WithSummary("Cancels an open or answered query raised in error (execution.query.manage).")
            .ProducesKernelCommand<CancelFormQuery>();

        return endpoints;
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListForFormAsync(
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
            return FormNotFound(formId);
        }

        // Queries are seen by whoever may see the form's enrollment, as its values are (ADR-2457).
        if (!await access.CanSeeAsync(tenant, NodeRef.For(record.EnrollmentId), cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"form {formId:D}");
        }

        var form = CollectionInstanceId.FromGuid(formId);
        var queries = await ReadAsync(contexts, tenant, q => q.FormId == form, cancellationToken).ConfigureAwait(false);
        var revisions = Revisions(record);
        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([.. queries.Select(q => FormQueryResponse.From(q, revisions))]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListForEnrollmentAsync(
        [FromQuery(Name = "enrollment_id")] Guid? enrollmentId,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        CancellationToken cancellationToken)
    {
        if (enrollmentId is not { } id || id == Guid.Empty)
        {
            return TypedResults.Problem(title: "Invalid request", detail: "Name the enrollment with enrollment_id.", statusCode: StatusCodes.Status400BadRequest);
        }

        var tenant = KernelRequestTenant.Get(context);
        var enrollment = EnrollmentId.FromGuid(id);
        if (!await access.CanSeeAsync(tenant, NodeRef.For(enrollment), cancellationToken).ConfigureAwait(false))
        {
            return NotVisible($"enrollment {id:D}");
        }

        var queries = await ReadAsync(contexts, tenant, q => q.EnrollmentId == enrollment, cancellationToken).ConfigureAwait(false);
        var revisions = new Dictionary<ObservationId, int>();
        foreach (var form in queries.Select(q => q.FormId).Distinct())
        {
            if (await forms.FindAsync(tenant, form, cancellationToken).ConfigureAwait(false) is { } record)
            {
                foreach (var (observation, revision) in Revisions(record))
                {
                    revisions[observation] = revision;
                }
            }
        }

        return TypedResults.Ok<IReadOnlyList<FormQueryResponse>>([.. queries.Select(q => FormQueryResponse.From(q, revisions))]);
    }

    internal static async Task<Results<Created<FormQueryResponse>, ProblemHttpResult>> RaiseAsync(
        Guid formId,
        RaiseFormQueryRequest request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var form = CollectionInstanceId.FromGuid(formId);

        // The query is authorized against the form's enrollment, which the client does not send.
        if (await forms.FindAsync(tenant, form, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return FormNotFound(formId);
        }

        var result = await sender.SendAsync(
            new RaiseFormQuery(
                tenant,
                request?.QueryId is { } given && given != Guid.Empty ? given : Guid.CreateVersion7(),
                form,
                record.EnrollmentId,
                ObservationId.FromGuid(request?.ObservationId ?? Guid.Empty),
                request?.Revision ?? 0,
                request?.Text ?? string.Empty,
                FormEndpoints.IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Value is not { } state)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var response = await RespondAsync(forms, tenant, state, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"{FormEndpoints.RoutePrefix}/{formId:D}/queries/{state.QueryId:D}", response);
    }

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> AnswerAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new AnswerFormQuery(tenant, queryId, enrollment, request?.Text ?? string.Empty, key, reason), cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> ReopenAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new ReopenFormQuery(tenant, queryId, enrollment, request?.Text ?? string.Empty, key, reason), cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CloseAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CloseFormQuery(tenant, queryId, enrollment, key, reason), cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CancelAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        StepAsync(formId, queryId, context, services, (tenant, enrollment, key, reason) =>
            new CancelFormQuery(tenant, queryId, enrollment, key, reason), cancellationToken);

    /// <summary>
    /// Finds the query on the form to authorize the step against its enrollment, sends the command built by
    /// <paramref name="command"/>, and answers with the query as it now stands.
    /// </summary>
    private static async Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> StepAsync(
        Guid formId,
        Guid queryId,
        HttpContext context,
        FormQueryCommandServices services,
        Func<TenantId, EnrollmentId, string, string, ICommand<FormQueryState>> command,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var form = CollectionInstanceId.FromGuid(formId);
        var query = (await ReadAsync(services.Contexts, tenant, q => q.Id == queryId && q.FormId == form, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault();
        if (query is null)
        {
            return TypedResults.Problem(
                title: "Not found", detail: $"Form {formId:D} has no query {queryId:D}.", statusCode: StatusCodes.Status404NotFound);
        }

        var result = await services.Sender.SendAsync(
            command(tenant, query.EnrollmentId, FormEndpoints.IdempotencyKey(context), services.ChangeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Ok(await RespondAsync(services.Forms, tenant, state, cancellationToken).ConfigureAwait(false))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    private static async Task<FormQueryResponse> RespondAsync(
        ICollectionInstanceRecords forms, TenantId tenant, FormQueryState state, CancellationToken cancellationToken)
    {
        var record = await forms.FindAsync(tenant, state.FormId, cancellationToken).ConfigureAwait(false);
        return FormQueryResponse.From(state, record is null ? new Dictionary<ObservationId, int>() : Revisions(record));
    }

    private static async Task<List<FormQuery>> ReadAsync(
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        TenantId tenant,
        System.Linq.Expressions.Expression<Func<FormQuery, bool>> filter,
        CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await db.FormQueries.Where(filter).OrderBy(q => q.CreatedAt).ThenBy(q => q.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<ObservationId, int> Revisions(CollectionInstanceRecord record) =>
        record.Values.ToDictionary(v => v.ObservationId, v => v.Revision);

    private static ProblemHttpResult FormNotFound(Guid formId) =>
        TypedResults.Problem(title: "Not found", detail: $"Form {formId:D} does not exist.", statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult NotVisible(string what) =>
        TypedResults.Problem(title: "Not visible", detail: $"You may not see {what}.", statusCode: StatusCodes.Status403Forbidden);
}

/// <summary>The services a query step needs, bound from DI as one parameter.</summary>
/// <param name="Forms">The kernel's form records, for the value's current revision.</param>
/// <param name="Contexts">The module's tenant-scoped reads.</param>
/// <param name="Sender">The kernel command path.</param>
/// <param name="ChangeReason">The signed change reason.</param>
internal sealed record FormQueryCommandServices(
    [FromServices] ICollectionInstanceRecords Forms,
    [FromServices] IModuleTenantContextFactory<ExecutionModuleDbContext> Contexts,
    [FromServices] IKernelCommandSender Sender,
    [FromServices] IKernelChangeReason ChangeReason);
