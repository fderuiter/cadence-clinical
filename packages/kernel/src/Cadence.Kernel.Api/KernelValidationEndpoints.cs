using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Api.Validation;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Study rule-pack governance, in-memory rule-pack preview, and validation finding inspection routes (spec §6.3, §10.3,
/// K-RULE-3, K-FIND-1). Every mutating action dispatches through <see cref="IKernelCommandSender"/> with an
/// <c>Idempotency-Key</c> and a signed <c>X-Change-Reason</c>.
/// </summary>
public static class KernelValidationEndpoints
{
    /// <summary>The semantic action a library-item approval step-up token must carry.</summary>
    public const string ApproveLibraryItemSemanticAction = "kernel.library_item.approve";

    /// <summary>The semantic action a validation-finding acceptance step-up token must carry.</summary>
    public const string AcceptFindingSemanticAction = "kernel.validation_finding.accept";

    /// <summary>The semantic action a validation-finding explanation step-up token must carry.</summary>
    public const string ExplainFindingSemanticAction = "kernel.validation_finding.explain";

    /// <summary>The canonical concrete action path for approving a library item reference.</summary>
    public static string ApproveLibraryItemAction(string itemRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/library-items/{itemRef}/approve";

    /// <summary>The canonical concrete action path for accepting a validation finding reference.</summary>
    public static string AcceptFindingAction(string findingRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/validation/findings/{findingRef}/accept";

    /// <summary>The canonical concrete action path for explaining a validation finding reference.</summary>
    public static string ExplainFindingAction(string findingRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/validation/findings/{findingRef}/explain";

    /// <summary>Maps study rule-pack and validation finding routes on the kernel group.</summary>
    public static RouteGroupBuilder MapKernelValidation(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel validation");

        group.MapGet("/studies/{studyRef}/rule-packs", ListStudyRulePacksAsync)
            .WithName("KernelListStudyRulePacks")
            .WithSummary("Lists rule-pack library items in the tenant and indicates which versions are pinned on the study.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/studies/{studyRef}/rule-packs/{itemRef}/preview", PreviewStudyRulePackAsync)
            .WithName("KernelPreviewStudyRulePack")
            .WithSummary("Evaluates a draft or approved rule pack in memory over the study's current collection records without mutating state.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
                KernelErrors.ActorUnresolved,
                KernelErrors.AuthorizationDenied,
                KernelErrors.NotFound,
                StandardsErrors.LibraryItemUnknown);

        group.MapPost("/library-items/{itemRef}/approve", ApproveLibraryItemAsync)
            .WithName("KernelApproveLibraryItem")
            .WithSummary("Approves a draft library item version with an Approved attestation (library_item.approve).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                KernelErrors.AlreadyExists,
                StandardsErrors.LibraryItemUnknown,
                StandardsErrors.InvalidTransition,
                StandardsErrors.AttestationRejected,
            ])
            .RequireElectronicSignature(
                ApproveLibraryItemSemanticAction,
                ctx => ApproveLibraryItemAction(ctx.Request.RouteValues["itemRef"]?.ToString() ?? string.Empty));

        group.MapPut("/studies/{studyRef}/standards", DeclareStudyRulePacksAsync)
            .WithName("KernelDeclareStudyRulePacks")
            .WithSummary("Declares or updates the approved rule-pack library items pinned on a study (study.declare_standards).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                StandardsErrors.StudyUnknown,
                StandardsErrors.LibraryItemUnknown,
                StandardsErrors.NotApproved,
                StandardsErrors.DeclarationInvalid,
            ]);

        group.MapGet("/studies/{studyRef}/validation/findings", ListStudyFindingsAsync)
            .WithName("KernelListStudyValidationFindings")
            .WithSummary("Lists derived validation findings for a study, optionally filtered by stage, repeatable status, severity, rule pack or dataset.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/studies/{studyRef}/attention", GetStudyAttentionAsync)
            .WithName("KernelGetStudyAttention")
            .WithSummary("Lists a study's open proposals and active (Open or Investigating) validation findings in one attention read.")
            .WithDescription("studyRef accepts a study NodeRef or study code. Returns the pinned study NodeRef, open proposals masked in place, and visible Open or Investigating findings up to limit (default 20).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/validation/findings/{findingRef}", GetFindingAsync)
            .WithName("KernelGetValidationFinding")
            .WithSummary("Returns one derived validation finding and its structured provenance explanation.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/validation/findings/{findingRef}/investigate", InvestigateFindingAsync)
            .WithName("KernelInvestigateValidationFinding")
            .WithSummary("Marks an open validation finding as under investigation (validation_finding.investigate).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<InvestigateFinding>();

        group.MapPost("/validation/findings/{findingRef}/classify", ClassifyFindingAsync)
            .WithName("KernelClassifyValidationFinding")
            .WithSummary("Classifies a detected validation finding (validation_finding.classify).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ClassifyFinding>();

        group.MapPost("/validation/findings/{findingRef}/accept", AcceptFindingAsync)
            .WithName("KernelAcceptValidationFinding")
            .WithSummary("Accepts an open or investigating validation finding with a Responsibility electronic signature (validation_finding.accept).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                .. RaisesKernelErrorsAttribute.Of(typeof(AcceptFinding))!,
            ])
            .RequireElectronicSignature(
                AcceptFindingSemanticAction,
                ctx => AcceptFindingAction(ctx.Request.RouteValues["findingRef"]?.ToString() ?? string.Empty));

        group.MapPost("/validation/findings/{findingRef}/explain", ExplainFindingAsync)
            .WithName("KernelExplainValidationFinding")
            .WithSummary("Explains an open or investigating validation finding with an Approved electronic signature (validation_finding.explain).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                .. RaisesKernelErrorsAttribute.Of(typeof(ExplainFinding))!,
            ])
            .RequireElectronicSignature(
                ExplainFindingSemanticAction,
                ctx => ExplainFindingAction(ctx.Request.RouteValues["findingRef"]?.ToString() ?? string.Empty));

        group.MapPost("/validation/findings/{findingRef}/reopen", ReopenFindingAsync)
            .WithName("KernelReopenValidationFinding")
            .WithSummary("Withdraws an investigation, acceptance or explanation on a validation finding (validation_finding.reopen).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ReopenFinding>();

        return kernel;
    }

    internal static Task<Results<Ok<KernelStudyRulePacksResponse>, ProblemHttpResult>> ListStudyRulePacksAsync(
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelRulePackGovernanceEndpoints.ListStudyRulePacksAsync(studyRef, context, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelRulePackPreviewResponse>, ProblemHttpResult>> PreviewStudyRulePackAsync(
        string studyRef,
        string itemRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken) =>
        KernelRulePackGovernanceEndpoints.PreviewStudyRulePackAsync(studyRef, itemRef, context, visibility, actor, clock, cancellationToken);

    internal static Task<Results<Ok<KernelRulePackResponse>, ProblemHttpResult>> ApproveLibraryItemAsync(
        string itemRef,
        KernelApproveLibraryItemRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelRulePackGovernanceEndpoints.ApproveLibraryItemAsync(itemRef, request, context, registry, sender, probe, changeReason, cancellationToken);

    internal static Task<Results<Ok<KernelStudyRulePacksResponse>, ProblemHttpResult>> DeclareStudyRulePacksAsync(
        string studyRef,
        KernelDeclareStudyRulePacksRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelRulePackGovernanceEndpoints.DeclareStudyRulePacksAsync(studyRef, request, context, registry, sender, visibility, actor, changeReason, cancellationToken);

    internal static Task<Results<Ok<KernelValidationFindingPageResponse>, ProblemHttpResult>> ListStudyFindingsAsync(
        string studyRef,
        string? stage,
        [FromQuery(Name = "status")] string[]? status,
        string? severity,
        string? dataset,
        string? pack_key,
        int? limit,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingReadEndpoints.ListStudyFindingsAsync(
            studyRef, stage, status, severity, dataset, pack_key, limit, context, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelStudyAttentionResponse>, ProblemHttpResult>> GetStudyAttentionAsync(
        string studyRef,
        [FromQuery] int? limit,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken) =>
        KernelValidationFindingReadEndpoints.GetStudyAttentionAsync(studyRef, limit, context, visibility, actor, clock, cancellationToken);

    internal static Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> GetFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingReadEndpoints.GetFindingAsync(findingRef, context, findings, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> InvestigateFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingDecisionEndpoints.InvestigateFindingAsync(
            findingRef, context, findings, sender, changeReason, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ClassifyFindingAsync(
        string findingRef,
        KernelClassifyFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingDecisionEndpoints.ClassifyFindingAsync(
            findingRef, request, context, findings, sender, changeReason, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> AcceptFindingAsync(
        string findingRef,
        KernelAcceptFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingDecisionEndpoints.AcceptFindingAsync(
            findingRef, request, context, findings, sender, probe, changeReason, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ExplainFindingAsync(
        string findingRef,
        KernelExplainFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingDecisionEndpoints.ExplainFindingAsync(
            findingRef, request, context, findings, sender, probe, changeReason, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ReopenFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingDecisionEndpoints.ReopenFindingAsync(
            findingRef, context, findings, sender, changeReason, visibility, actor, cancellationToken);

    internal static IValidationFindings? ResolveActiveValidationFindings(IServiceProvider services) =>
        KernelValidationFindingVisibility.ResolveActiveValidationFindings(services);

    internal static Task<bool> IsFindingVisibleAsync(
        TenantId tenant,
        ValidationFindingView finding,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelValidationFindingVisibility.IsFindingVisibleAsync(tenant, finding, services, visibility, actor, cancellationToken);
}
