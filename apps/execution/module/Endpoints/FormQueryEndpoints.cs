using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// Data queries about eCRF values (#5461, <c>PRD-QRY-001</c>, <c>PRD-QRY-002</c>): raise a query on the current revision
/// of a form's value or on a blank item, list a form's, an enrollment's or a study's visible queries (including point-in-time
/// <c>as_of</c> bitemporal reconstruction and owner/origin/priority/escalated filters), answer, reopen, close or cancel one,
/// run 14-day SLA escalations, and read de-identified daily aging digests.
/// </summary>
internal static class FormQueryEndpoints
{
    internal const int DefaultQueryLimit = 500;
    internal const int MaxQueryLimit = 1000;

    public static IEndpointRouteBuilder MapFormQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(FormEndpoints.RoutePrefix)
            .RequireSignedInCaller("Every read checks the caller may see the enrollment; every write is a module command authorized against the caller's grants.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution queries");

        group.MapGet("/queries", ListQueriesAsync)
            .WithName("ExecutionListEnrollmentQueries")
            .WithSummary("Lists the queries on every form of an enrollment or across visible enrollments of a study, oldest first.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/queries/digests", ListAgingDigestsAsync)
            .WithName("ExecutionListFormQueryAgingDigests")
            .WithSummary("Lists daily de-identified query aging digests for visible scopes of a study.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/queries/escalate", EscalateOverdueQueriesAsync)
            .WithName("ExecutionEscalateOverdueFormQueries")
            .WithSummary("Escalates open queries past their 14-day SLA on IClock and emits daily aging digests (execution.query.manage).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<EscalateFormQuery>();

        group.MapGet("/{formId:guid}/queries", ListForFormAsync)
            .WithName("ExecutionListFormQueries")
            .WithSummary("Lists a form's queries, oldest first, each with whether its value changed since it was raised.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{formId:guid}/queries", RaiseAsync)
            .WithName("ExecutionRaiseFormQuery")
            .WithSummary("Raises a query on the current revision of one of the form's values or on a blank item (execution.query.raise).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<RecordUnaskedCollectionItem>()
            .ProducesKernelCommand<RaiseFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/answer", AnswerAsync)
            .WithName("ExecutionAnswerFormQuery")
            .WithSummary("Answers an open query (execution.query.answer).")
            .ProducesKernelCommand<AnswerFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/reopen", ReopenAsync)
            .WithName("ExecutionReopenFormQuery")
            .WithSummary("Reopens an answered or closed query with why it is not settled (execution.query.manage).")
            .ProducesKernelCommand<ReopenFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/reassign", ReassignAsync)
            .WithName("ExecutionReassignFormQuery")
            .WithSummary("Reassigns an open or answered query's assignee or responsible role with audited bitemporal history (execution.query.manage).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ReassignFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/close", CloseAsync)
            .WithName("ExecutionCloseFormQuery")
            .WithSummary("Closes an answered manual query (separation of duties) or system query (execution.query.manage).")
            .Accepts<FormQueryNoteRequest>(isOptional: true, contentType: "application/json")
            .ProducesKernelCommand<CloseFormQuery>();

        group.MapPost("/{formId:guid}/queries/{queryId:guid}/cancel", CancelAsync)
            .WithName("ExecutionCancelFormQuery")
            .WithSummary("Cancels an open or answered query raised in error (execution.query.manage).")
            .Accepts<FormQueryNoteRequest>(isOptional: true, contentType: "application/json")
            .ProducesKernelCommand<CancelFormQuery>();

        return endpoints;
    }

    internal static Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListForFormAsync(
        Guid formId,
        [AsParameters] FormQueryTemporalFilter temporalFilter,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        IClock clock,
        CancellationToken cancellationToken) =>
        FormQueryReadEndpoints.ListForFormAsync(formId, temporalFilter, context, forms, contexts, access, navigator, clock, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<FormQueryResponse>>, ProblemHttpResult>> ListQueriesAsync(
        [AsParameters] FormQueryListFilter filter,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        IClock clock,
        CancellationToken cancellationToken) =>
        FormQueryReadEndpoints.ListQueriesAsync(filter, context, forms, contexts, access, navigator, clock, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<FormQueryAgingDigestResponse>>, ProblemHttpResult>> ListAgingDigestsAsync(
        [FromQuery(Name = "study_id")] Guid? studyId,
        [FromQuery(Name = "site")] string? site,
        [FromQuery(Name = "recipient_role")] string? recipientRole,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken) =>
        FormQueryReadEndpoints.ListAgingDigestsAsync(studyId, site, recipientRole, context, forms, contexts, access, navigator, cancellationToken);

    internal static Task<Results<Ok<FormQueryEscalationPassResponse>, ProblemHttpResult>> EscalateOverdueQueriesAsync(
        [FromQuery(Name = "study_id")] Guid? studyId,
        HttpContext context,
        IFormQueryEscalationService escalation,
        ICollectionInstanceRecords forms,
        IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
        IKernelReadAccess access,
        IKernelChangeReason changeReason,
        IKernelNavigator navigator,
        IClock clock,
        CancellationToken cancellationToken,
        [FromServices] ITenantEntitlementReader? entitlements = null) =>
        FormQueryMutationEndpoints.EscalateOverdueQueriesAsync(studyId, context, escalation, forms, contexts, access, changeReason, navigator, clock, cancellationToken, entitlements);

    internal static Task<Results<Created<FormQueryResponse>, ProblemHttpResult>> RaiseAsync(
        Guid formId,
        RaiseFormQueryRequest request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        CancellationToken cancellationToken) =>
        FormQueryMutationEndpoints.RaiseAsync(formId, request, context, forms, sender, changeReason, access, navigator, cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> AnswerAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        FormQueryMutationEndpoints.AnswerAsync(formId, queryId, request, context, services, cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> ReopenAsync(
        Guid formId, Guid queryId, FormQueryTextRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        FormQueryMutationEndpoints.ReopenAsync(formId, queryId, request, context, services, cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> ReassignAsync(
        Guid formId, Guid queryId, ReassignFormQueryRequest request, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        FormQueryMutationEndpoints.ReassignAsync(formId, queryId, request, context, services, cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CloseAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        FormQueryMutationEndpoints.CloseAsync(formId, queryId, context, services, cancellationToken);

    internal static Task<Results<Ok<FormQueryResponse>, ProblemHttpResult>> CancelAsync(
        Guid formId, Guid queryId, HttpContext context, [AsParameters] FormQueryCommandServices services, CancellationToken cancellationToken) =>
        FormQueryMutationEndpoints.CancelAsync(formId, queryId, context, services, cancellationToken);
}
