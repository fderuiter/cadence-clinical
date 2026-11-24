using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

    private static readonly Capability ApproveLibraryItemCapability = new("library_item.approve", VerbClass.Approve);
    private static readonly Capability RegisterStandardCapability = new("standard.register", VerbClass.Execute);
    private static readonly Capability ApproveStandardCapability = new("standard.approve", VerbClass.Approve);
    private static readonly Capability AcceptFindingCapability = new("validation_finding.accept", VerbClass.Approve);
    private static readonly Capability ExplainFindingCapability = new("validation_finding.explain", VerbClass.Approve);
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);
    private static readonly FrozenSet<LineageEdgeKind> DerivedFromOnly = new[] { LineageEdgeKind.DerivedFrom }.ToFrozenSet();

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
            .WithSummary("Lists derived validation findings for a study, optionally filtered by stage, status, severity, rule pack or dataset.")
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

    internal static async Task<Results<Ok<KernelStudyRulePacksResponse>, ProblemHttpResult>> ListStudyRulePacksAsync(
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var studyId = resolved.StudyId;
        var registry = context.RequestServices.GetService<IStandardsRegistry>();
        if (registry is null)
        {
            return TypedResults.Ok(new KernelStudyRulePacksResponse(NodeRef.For(studyId).ToString(), null, [], []));
        }

        var profile = await registry.FindStudyStandardsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var pinnedIds = (profile?.LibraryPins ?? []).ToHashSet();
        var items = await registry.ListLibraryItemsAsync(tenant, LibraryItemKind.RulePack, cancellationToken).ConfigureAwait(false);
        var packs = items.Select(item => KernelRulePackResponse.From(item, pinnedIds.Contains(item.Id))).ToList();
        var pinnedRulePackRefs = items
            .Where(item => pinnedIds.Contains(item.Id))
            .Select(item => NodeRef.For(item.Id).ToString())
            .ToList();

        return TypedResults.Ok(new KernelStudyRulePacksResponse(
            NodeRef.For(studyId).ToString(),
            profile is not null ? NodeRef.For(profile.Id).ToString() : null,
            pinnedRulePackRefs,
            packs));
    }

    internal static async Task<Results<Ok<KernelRulePackPreviewResponse>, ProblemHttpResult>> PreviewStudyRulePackAsync(
        string studyRef,
        string itemRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryParseLibraryItem(itemRef, out var itemId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{itemRef}' is not a library_item NodeRef (library_item:<uuid>).");
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var registry = context.RequestServices.GetService<IStandardsRegistry>();
        var validation = context.RequestServices.GetService<ICollectionValidationService>();
        if (registry is null || validation is null)
        {
            return TypedResults.Ok(new KernelRulePackPreviewResponse(NodeRef.For(itemId).ToString(), 0, 0, 0, []));
        }

        var item = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.Kind != LibraryItemKind.RulePack)
        {
            return KernelProblems.NotFound(NodeRef.For(itemId));
        }

        IReadOnlySet<EnrollmentId>? unblindedEnrollments = null;
        var probe = context.RequestServices.GetService<IAuthorizationProbe>();
        var collections = context.RequestServices.GetService<ICollectionInstanceRecords>();
        if (probe is not null && collections is not null)
        {
            var studyForms = await collections.ListForStudyAsync(tenant, resolved.StudyId, cancellationToken).ConfigureAwait(false);
            var permitted = new HashSet<EnrollmentId>();
            foreach (var enrollmentId in studyForms.Select(f => f.EnrollmentId).Distinct())
            {
                var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, NodeRef.For(enrollmentId), cancellationToken).ConfigureAwait(false);
                if (decision.Permitted)
                {
                    permitted.Add(enrollmentId);
                }
            }

            unblindedEnrollments = permitted;
        }

        var preview = await validation.PreviewRulePackAsync(
            tenant,
            resolved.StudyId,
            itemId,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            actor.Actor,
            unblindedEnrollments,
            cancellationToken).ConfigureAwait(false);

        var hydratedViolations = await HydratePreviewViolationsAsync(
            tenant,
            resolved.StudyId,
            item,
            preview.Violations,
            context.RequestServices,
            visibility,
            actor,
            unblindedEnrollments,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new KernelRulePackPreviewResponse(
            NodeRef.For(itemId).ToString(),
            preview.FormsEvaluated,
            preview.RulesEvaluated,
            hydratedViolations.Count,
            hydratedViolations));
    }

    internal static async Task<Results<Ok<KernelRulePackResponse>, ProblemHttpResult>> ApproveLibraryItemAsync(
        string itemRef,
        KernelApproveLibraryItemRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this library item approval.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseLibraryItem(itemRef, out var itemId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{itemRef}' is not a library_item NodeRef (library_item:<uuid>).");
        }

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

        var tenant = KernelRequestTenant.Get(context);
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is { } entitlementReader)
        {
            var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
            var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
            if (entitlements.IsReadOnly)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
            }

            if (!entitlements.ProductFamilies.Contains(ProductFamily.Blueprint))
            {
                return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
            }
        }

        var existing = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(StandardsErrors.LibraryItemUnknown);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "approve-library-item-attest", idempotencyKey));

        var canApprove = await probe.CanIAsync(tenant, ApproveLibraryItemCapability, NodeRef.For(itemId), cancellationToken).ConfigureAwait(false);
        if (canApprove.Permitted)
        {
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.LibraryItem, itemId.Value, existing.ContentHash),
                    new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(attestResult.Error!);
            }
        }

        var approveResult = await sender.SendAsync(
            new ApproveLibraryItem(tenant, itemId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!approveResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(approveResult.Error!);
        }

        if (existing.Kind == LibraryItemKind.RulePack)
        {
            await EnsureBaselineStandardApprovedAsync(
                tenant,
                registry,
                sender,
                probe,
                new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                idempotencyKey,
                reason,
                cancellationToken).ConfigureAwait(false);
        }

        var updated = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false) ?? existing;
        return TypedResults.Ok(KernelRulePackResponse.From(updated, pinned: false));
    }

    internal static async Task<Results<Ok<KernelStudyRulePacksResponse>, ProblemHttpResult>> DeclareStudyRulePacksAsync(
        string studyRef,
        KernelDeclareStudyRulePacksRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request?.RulePackRefs is null)
        {
            return KernelProblems.BadRequest("Invalid standards declaration", "Provide rule_pack_refs as an array of library_item NodeRefs.");
        }

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

        var requestedRulePacks = new List<LibraryItemId>();
        foreach (var rawRef in request.RulePackRefs)
        {
            if (!TryParseLibraryItem(rawRef, out var itemId))
            {
                return KernelProblems.BadRequest("Invalid NodeRef", $"'{rawRef}' is not a library_item NodeRef (library_item:<uuid>).");
            }

            requestedRulePacks.Add(itemId);
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var studyId = resolved.StudyId;
        var currentProfile = await registry.FindStudyStandardsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var profileId = currentProfile?.Id
            ?? StudyStandardsProfileId.FromGuid(DeriveGuid(tenant, "study-standards-profile", studyId.Value.ToString("D")));

        var preservedPins = new List<LibraryItemId>();
        if (currentProfile is not null)
        {
            foreach (var pinId in currentProfile.LibraryPins)
            {
                var pinnedItem = await registry.FindLibraryItemAsync(tenant, pinId, cancellationToken).ConfigureAwait(false);
                if (pinnedItem is not null && pinnedItem.Kind != LibraryItemKind.RulePack)
                {
                    preservedPins.Add(pinId);
                }
            }
        }

        IReadOnlyList<StandardVersionId> standardVersions = currentProfile?.StandardVersions ?? [];
        if (standardVersions.Count == 0)
        {
            var allStandards = await registry.ListStandardVersionsAsync(tenant, cancellationToken).ConfigureAwait(false);
            standardVersions = [.. allStandards
                .Where(s => s.Status == GovernanceStatus.Approved)
                .GroupBy(s => s.StandardCode, StringComparer.Ordinal)
                .Select(g => g.Last().Id)];
        }

        var mergedPins = preservedPins.Concat(requestedRulePacks).Distinct().ToList();
        var declareResult = await sender.SendAsync(
            new DeclareStudyStandards(
                tenant,
                studyId,
                profileId,
                standardVersions,
                currentProfile?.ControlledTerminology ?? [],
                mergedPins,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!declareResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(declareResult.Error!);
        }

        return await ListStudyRulePacksAsync(studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingPageResponse>, ProblemHttpResult>> ListStudyFindingsAsync(
        string studyRef,
        string? stage,
        string? status,
        string? severity,
        string? dataset,
        string? pack_key,
        int? limit,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        ValidationStage? parsedStage = null;
        if (!string.IsNullOrWhiteSpace(stage))
        {
            if (!Enum.TryParse<ValidationStage>(stage, ignoreCase: true, out var s) || !Enum.IsDefined(s))
            {
                return KernelProblems.BadRequest("Invalid stage", $"'{stage}' is not a valid validation stage.");
            }

            parsedStage = s;
        }

        IReadOnlyCollection<FindingStatus>? parsedStatuses = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<FindingStatus>(status, ignoreCase: true, out var st) || !Enum.IsDefined(st))
            {
                return KernelProblems.BadRequest("Invalid status", $"'{status}' is not a valid finding status.");
            }

            parsedStatuses = [st];
        }

        ValidationSeverity? parsedSeverity = null;
        if (!string.IsNullOrWhiteSpace(severity))
        {
            if (!Enum.TryParse<ValidationSeverity>(severity, ignoreCase: true, out var sev) || !Enum.IsDefined(sev))
            {
                return KernelProblems.BadRequest("Invalid severity", $"'{severity}' is not a valid validation severity.");
            }

            parsedSeverity = sev;
        }

        if (limit is < 1 or > FindingQuery.MaxTake)
        {
            return KernelProblems.BadRequest("Invalid limit", $"Limit must be between 1 and {FindingQuery.MaxTake}.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        var findings = context.RequestServices.GetService<IValidationFindings>();
        if (findings is null)
        {
            return TypedResults.Ok(new KernelValidationFindingPageResponse(0, []));
        }

        var rawPage = await findings.ListAsync(
            tenant,
            new FindingQuery(resolved.StudyId)
            {
                Stage = parsedStage,
                Statuses = parsedStatuses,
                Severity = parsedSeverity,
                Dataset = string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim(),
                PackKey = string.IsNullOrWhiteSpace(pack_key) ? null : pack_key.Trim(),
                Take = FindingQuery.MaxTake,
            },
            cancellationToken).ConfigureAwait(false);

        if (rawPage.Findings.Count == 0)
        {
            return TypedResults.Ok(new KernelValidationFindingPageResponse(0, []));
        }

        var visibleFindings = await FilterVisibleFindingsAsync(
            tenant,
            resolved.StudyId,
            rawPage.Findings,
            context.RequestServices,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);
        var take = limit ?? 100;
        var paged = visibleFindings.Take(take).ToList();
        var hydrated = await HydrateFindingsAsync(
            tenant,
            resolved.StudyId,
            paged,
            context.RequestServices,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new KernelValidationFindingPageResponse(
            visibleFindings.Count,
            [.. hydrated.Select(h => h.Response)]));
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> GetFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(findingRef, out var node) || node.Kind != NodeKinds.ValidationFinding)
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var view = await findings.FindAsync(tenant, ValidationFindingId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return KernelProblems.NotFound(node);
        }

        var studyNode = NodeRef.For(view.Study);
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(studyNode) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You may not see findings for study {studyNode} ({studyMask}).");
        }

        var visibleFindings = await FilterVisibleFindingsAsync(
            tenant,
            view.Study,
            [view],
            context.RequestServices,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);
        if (visibleFindings.Count == 0)
        {
            return KernelProblems.NotVisible($"You may not see validation finding {node}.");
        }

        var hydratedList = await HydrateFindingsAsync(
            tenant,
            view.Study,
            visibleFindings,
            context.RequestServices,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);
        var hydrated = hydratedList[0];

        return TypedResults.Ok(new KernelValidationFindingDetailResponse(
            hydrated.Response,
            hydrated.View.Explain(hydrated.Response.TargetLabel, hydrated.ExtraEvidence)));
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> InvestigateFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (_, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new InvestigateFinding(tenant, findingId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ClassifyFindingAsync(
        string findingRef,
        KernelClassifyFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.Classification)
            || !Enum.TryParse<FindingClassification>(request.Classification.Trim(), ignoreCase: true, out var classification)
            || !Enum.IsDefined(classification))
        {
            return KernelProblems.BadRequest(
                "Invalid classification",
                "Classification must be DataIssue, MappingIssue, MetadataIssue, RuleIssue or NotAnIssue.");
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (_, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new ClassifyFinding(tenant, findingId, classification, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> AcceptFindingAsync(
        string findingRef,
        KernelAcceptFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to accepting this validation finding.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var rationale = (request?.Rationale ?? string.Empty).Trim();
        if (!FindingText.IsValid(rationale))
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.DecisionTextInvalid);
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (existing, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "accept-finding-attest", idempotencyKey));

        var findingNode = NodeRef.For(findingId);
        var canAccept = await probe.CanIAsync(tenant, AcceptFindingCapability, findingNode, cancellationToken).ConfigureAwait(false);
        if (canAccept.Permitted)
        {
            existing ??= await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
            }

            if (existing.Status is FindingStatus.Open or FindingStatus.Investigating)
            {
                var hash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Accepted, rationale);
                var attestResult = await sender.SendAsync(
                    new Attest(
                        tenant,
                        attestationId,
                        AttestationMeaning.Responsibility,
                        new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, hash),
                        new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                        KernelLockEndpoints.StepUpMethod,
                        $"{idempotencyKey}:attest",
                        reason,
                        findingNode),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
                {
                    return KernelErrorEndpoints.Problem(attestResult.Error!);
                }
            }
        }

        var result = await sender.SendAsync(
            new AcceptFinding(tenant, findingId, rationale, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ExplainFindingAsync(
        string findingRef,
        KernelExplainFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to explaining this validation finding.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var explanation = (request?.Explanation ?? string.Empty).Trim();
        if (!FindingText.IsValid(explanation))
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.DecisionTextInvalid);
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (existing, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "explain-finding-attest", idempotencyKey));

        var findingNode = NodeRef.For(findingId);
        var canExplain = await probe.CanIAsync(tenant, ExplainFindingCapability, findingNode, cancellationToken).ConfigureAwait(false);
        if (canExplain.Permitted)
        {
            existing ??= await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
            }

            if (existing.Status is FindingStatus.Open or FindingStatus.Investigating)
            {
                var hash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Explained, explanation);
                var attestResult = await sender.SendAsync(
                    new Attest(
                        tenant,
                        attestationId,
                        AttestationMeaning.Approved,
                        new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, hash),
                        new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                        KernelLockEndpoints.StepUpMethod,
                        $"{idempotencyKey}:attest",
                        reason,
                        findingNode),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
                {
                    return KernelErrorEndpoints.Problem(attestResult.Error!);
                }
            }
        }

        var result = await sender.SendAsync(
            new ExplainFinding(tenant, findingId, explanation, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ReopenFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason) is var (idempotencyKey, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckValidationDecisionEntitlementsAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var (_, visibilityProblem) = await AuthorizeDecisionVisibilityAsync(
            tenant, findingId, findings, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (visibilityProblem is not null)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new ReopenFinding(tenant, findingId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ToFindingDecisionResultAsync(result, tenant, findingId, findings, context.RequestServices, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static IValidationFindings? ResolveActiveValidationFindings(IServiceProvider services)
    {
        if (services.GetService<IValidationFindings>() is not { } findings)
        {
            return null;
        }

        var isPersistenceFindings = findings.GetType().Namespace?.StartsWith("Cadence.Kernel.Persistence", StringComparison.Ordinal) ?? false;
        if (isPersistenceFindings
            && services.GetService<IAuthorityStore>() is { } authorityStore
            && !(authorityStore.GetType().Namespace?.StartsWith("Cadence.Kernel.Persistence", StringComparison.Ordinal) ?? false))
        {
            return null;
        }

        return findings;
    }

    internal static async Task<bool> IsFindingVisibleAsync(
        TenantId tenant,
        ValidationFindingView finding,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var studyNode = NodeRef.For(finding.Study);
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(studyNode) is not null)
        {
            return false;
        }

        var visible = await FilterVisibleFindingsAsync(tenant, finding.Study, [finding], services, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
        return visible.Count > 0;
    }

    private static async Task<(ValidationFindingView? Existing, ProblemHttpResult? Problem)> AuthorizeDecisionVisibilityAsync(
        TenantId tenant,
        ValidationFindingId findingId,
        IValidationFindings findings,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (actor.Actor is null)
        {
            return (null, null);
        }

        var findingNode = NodeRef.For(findingId);
        if (ResolveActiveValidationFindings(context.RequestServices) is null)
        {
            var fallbackMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [findingNode], cancellationToken).ConfigureAwait(false);
            return fallbackMasks.For(findingNode) is not null
                ? (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied))
                : (null, null);
        }

        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return await IsFindingVisibleAsync(tenant, existing, context.RequestServices, visibility, actor, cancellationToken).ConfigureAwait(false)
                ? (existing, null)
                : (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [findingNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(findingNode) is not null)
        {
            return (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        if (context.RequestServices.GetService<IAuthorizationProbe>() is { } probe)
        {
            var canUnblindTenant = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, NodeRef.For(tenant), cancellationToken)
                .ConfigureAwait(false);
            if (!canUnblindTenant.Permitted)
            {
                return (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }
        }

        return (null, null);
    }

    private static async Task<ProblemHttpResult?> CheckValidationDecisionEntitlementsAsync(
        HttpContext context,
        TenantId tenant,
        CancellationToken cancellationToken)
    {
        if (context.RequestServices.GetService<ITenantEntitlementReader>() is not { } entitlementReader)
        {
            return null;
        }

        var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
        var entitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (entitlements.IsReadOnly)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.SubscriptionExpired);
        }

        if (!entitlements.ProductFamilies.Any(static f =>
                f is ProductFamily.Blueprint or ProductFamily.Capture or ProductFamily.Assure))
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
        }

        return null;
    }

    private static (string IdempotencyKey, string Reason, ProblemHttpResult? Problem) ReadCommandHeaders(
        HttpContext context, IKernelChangeReason changeReason)
    {
        var idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired));
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired));
        }

        return (idempotencyKey, reason, null);
    }

    private static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ToFindingDecisionResultAsync(
        CommandResult<ValidationFindingId> result,
        TenantId tenant,
        ValidationFindingId findingId,
        IValidationFindings findings,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
            return KernelProblems.NotFound(NodeRef.For(findingId));
        }

        if (!await IsFindingVisibleAsync(tenant, updated, services, visibility, actor, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        var hydrated = await HydrateFindingsAsync(
            tenant,
            updated.Study,
            [updated],
            services,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(hydrated[0].Response);
    }

    private static bool TryParseFinding(string text, out ValidationFindingId findingId)
    {
        findingId = default;
        if (!NodeRef.TryParse(text, out var node) || node.Kind != NodeKinds.ValidationFinding || node.Version is not null)
        {
            return false;
        }

        findingId = ValidationFindingId.FromGuid(node.Id);
        return !findingId.IsEmpty;
    }

    private static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveVisibleStudyAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        StudyId studyId;
        if (NodeRef.TryParse(studyRef, out var node))
        {
            if (node.Kind != NodeKinds.Study)
            {
                return (default, KernelProblems.WrongKind(node, NodeKinds.Study));
            }

            studyId = StudyId.FromGuid(node.Id);
            var navigator = context.RequestServices.GetService<IKernelNavigator>();
            var directory = context.RequestServices.GetService<IStudyDirectory>();
            var existsInNav = navigator is not null && await navigator.ResolveAsync(tenant, NodeRef.For(studyId), cancellationToken).ConfigureAwait(false) is not null;
            var existsInDir = !existsInNav && directory is not null && await directory.FindStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false) is not null;
            if (!existsInNav && !existsInDir)
            {
                return (default, KernelProblems.NotFound(NodeRef.For(studyId)));
            }
        }
        else
        {
            var directory = context.RequestServices.GetService<IStudyDirectory>();
            var byCode = directory is not null
                ? await directory.FindStudyByCodeAsync(tenant, studyRef.Trim(), cancellationToken).ConfigureAwait(false)
                : null;
            if (byCode is null)
            {
                return (default, TypedResults.Problem(
                    title: "Study not found",
                    detail: $"Study '{studyRef}' does not exist in your tenant.",
                    statusCode: StatusCodes.Status404NotFound));
            }

            studyId = byCode.StudyId;
        }

        var studyNode = NodeRef.For(studyId);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(studyNode) is { } mask)
        {
            return (default, KernelProblems.NotVisible($"You may not access study {studyNode} ({mask})."));
        }

        return (studyId, null);
    }

    private static bool TryParseLibraryItem(string text, out LibraryItemId itemId)
    {
        itemId = default;
        if (!NodeRef.TryParse(text, out var node) || node.Kind != NodeKinds.LibraryItem)
        {
            return false;
        }

        itemId = LibraryItemId.FromGuid(node.Id);
        return !itemId.IsEmpty;
    }

    private static async Task EnsureBaselineStandardApprovedAsync(
        TenantId tenant,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        DateTimeOffset authenticatedAt,
        string idempotencyKey,
        string reason,
        CancellationToken cancellationToken)
    {
        var existingStandards = await registry.ListStandardVersionsAsync(tenant, cancellationToken).ConfigureAwait(false);
        if (existingStandards.Any(s => s.Status == GovernanceStatus.Approved))
        {
            return;
        }

        var package = Cadence.Standards.StandardSeeds.Load(Cadence.Standards.StandardSeeds.Names[0]).Package;
        var summary = existingStandards.FirstOrDefault(s =>
            string.Equals(s.StandardCode, package.StandardCode, StringComparison.Ordinal)
            && string.Equals(s.Version, package.Version, StringComparison.Ordinal));

        if (summary is null)
        {
            var canRegister = await probe.CanIAsync(tenant, RegisterStandardCapability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);
            if (!canRegister.Permitted)
            {
                return;
            }

            var standardId = StandardVersionId.FromGuid(DeriveGuid(tenant, "baseline-standard", $"{package.StandardCode}:{package.Version}"));
            var regResult = await sender.SendAsync(
                new RegisterStandardVersion(tenant, standardId, package, null, $"{idempotencyKey}:std-register", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!regResult.IsSuccess && regResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return;
            }

            summary = await registry.FindStandardVersionAsync(tenant, package.StandardCode, package.Version, cancellationToken).ConfigureAwait(false);
        }

        if (summary is not null && summary.Status == GovernanceStatus.Draft)
        {
            var canApprove = await probe.CanIAsync(tenant, ApproveStandardCapability, NodeRef.For(summary.Id), cancellationToken).ConfigureAwait(false);
            if (!canApprove.Permitted)
            {
                return;
            }

            var attestId = AttestationId.FromGuid(DeriveGuid(tenant, "baseline-standard-attest", $"{idempotencyKey}:{summary.Id.Value:D}"));
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.StandardVersion, summary.Id.Value, summary.ContentHash),
                    authenticatedAt,
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:std-attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return;
            }

            await sender.SendAsync(
                new ApproveStandardVersion(tenant, summary.Id, attestId, $"{idempotencyKey}:std-approve", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<ValidationFindingView>> FilterVisibleFindingsAsync(
        TenantId tenant,
        StudyId studyId,
        IReadOnlyList<ValidationFindingView> candidates,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var collections = services.GetService<ICollectionInstanceRecords>();
        var protocols = services.GetService<IProtocolVersionReader>();
        var concepts = services.GetService<IConceptRegistry>();
        var probe = services.GetService<IAuthorizationProbe>();
        var classifier = services.GetService<IBlindedTreatmentClassifier>() ?? BlindedTreatmentClassifier.Instance;

        var formsByKey = new Dictionary<string, CollectionInstanceRecord>(StringComparer.Ordinal);
        var formsByEnrollment = new Dictionary<EnrollmentId, List<CollectionInstanceRecord>>();
        if (collections is not null && candidates.Any(f => f.Target.Kind == FindingTargetKind.DatasetRecord))
        {
            var studyForms = await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
            foreach (var form in studyForms)
            {
                formsByKey[form.CollectionInstanceId.Value.ToString("D")] = form;
                if (!formsByEnrollment.TryGetValue(form.EnrollmentId, out var list))
                {
                    formsByEnrollment[form.EnrollmentId] = list = [];
                }

                list.Add(form);
            }
        }

        var perFindingRefs = new Dictionary<ValidationFindingId, List<NodeRef>>(candidates.Count);
        var allNodesToCheck = new List<NodeRef>(candidates.Count * 2);
        foreach (var finding in candidates)
        {
            var refs = new List<NodeRef> { NodeRef.For(finding.Id) };
            if (finding.Target.Node is { } targetNode)
            {
                refs.Add(targetNode);
            }

            if (finding.Target.Kind == FindingTargetKind.DatasetRecord
                && formsByKey.TryGetValue(finding.Target.Key, out var targetForm))
            {
                refs.Add(NodeRef.For(targetForm.EnrollmentId));
                var siblings = formsByEnrollment.GetValueOrDefault(targetForm.EnrollmentId) ?? [targetForm];
                foreach (var obsId in ResolveReferencedObservationIds(finding, targetForm, siblings))
                {
                    refs.Add(NodeRef.For(obsId));
                }
            }

            perFindingRefs[finding.Id] = refs;
            allNodesToCheck.AddRange(refs);
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, allNodesToCheck, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProtocolDesign>? designs = null;
        Dictionary<ConceptId, BlindedConceptMetadata>? conceptCache = null;
        var unblindCache = new Dictionary<NodeRef, bool>();

        var visible = new List<ValidationFindingView>(candidates.Count);
        foreach (var finding in candidates)
        {
            var refs = perFindingRefs[finding.Id];
            if (refs.Any(r => masks.For(r) is not null))
            {
                continue;
            }

            if (probe is not null)
            {
                designs ??= await LoadStudyDesignsAsync(tenant, studyId, protocols, formsByKey.Values, cancellationToken).ConfigureAwait(false);
                conceptCache ??= await LoadStudyConceptsAsync(tenant, concepts, formsByKey.Values, designs, cancellationToken).ConfigureAwait(false);
                BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptCache.TryGetValue(id, out var m) ? m : null;

                var isBlinded = classifier.IsBlindedFinding(finding.Target, finding.Observed, designs, ResolveConcept);
                NodeRef unblindScope = NodeRef.For(studyId);
                if (finding.Target.Kind == FindingTargetKind.DatasetRecord
                    && formsByKey.TryGetValue(finding.Target.Key, out var targetForm))
                {
                    unblindScope = NodeRef.For(targetForm.EnrollmentId);
                    if (!isBlinded)
                    {
                        var siblings = formsByEnrollment.GetValueOrDefault(targetForm.EnrollmentId) ?? [targetForm];
                        isBlinded = ReferencesBlindedFormItem(finding, targetForm, siblings, classifier, designs, conceptCache);
                    }
                }

                if (isBlinded)
                {
                    if (!unblindCache.TryGetValue(unblindScope, out var canUnblind))
                    {
                        var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, unblindScope, cancellationToken).ConfigureAwait(false);
                        canUnblind = decision.Permitted;
                        unblindCache[unblindScope] = canUnblind;
                    }

                    if (!canUnblind)
                    {
                        continue;
                    }
                }
            }

            visible.Add(finding);
        }

        return visible;
    }

    private static IReadOnlyList<ObservationId> ResolveReferencedObservationIds(
        ValidationFindingView finding,
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblingForms)
    {
        var matched = MatchVariableObservations(targetForm.CollectionSpecId, finding.Target.Variable, targetForm, siblingForms)
            .Concat(finding.Observed.SelectMany(observed =>
                MatchVariableObservations(targetForm.CollectionSpecId, observed.Variable, targetForm, siblingForms)))
            .Distinct()
            .ToList();

        if (matched.Count == 0 && BlindedTreatmentClassifier.MatchesMarkers(targetForm.CollectionSpecId))
        {
            return [.. targetForm.Values.Select(val => val.ObservationId).Distinct()];
        }

        return matched;
    }

    private static IEnumerable<ObservationId> MatchVariableObservations(
        string defaultSpecId,
        string? variable,
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblingForms)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            yield break;
        }

        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            var specId = variable[..dot];
            var itemId = variable[(dot + 1)..];
            foreach (var form in siblingForms.Where(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal)))
            {
                foreach (var val in form.Values.Where(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal)))
                {
                    yield return val.ObservationId;
                }
            }

            yield break;
        }

        foreach (var val in targetForm.Values.Where(v => string.Equals(v.ItemId, variable, StringComparison.Ordinal)))
        {
            yield return val.ObservationId;
        }

        foreach (var form in siblingForms.Where(f => !string.Equals(f.CollectionSpecId, defaultSpecId, StringComparison.Ordinal)))
        {
            foreach (var val in form.Values.Where(v => string.Equals(v.ItemId, variable, StringComparison.Ordinal)))
            {
                yield return val.ObservationId;
            }
        }
    }

    private static bool ReferencesBlindedFormItem(
        ValidationFindingView finding,
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblingForms,
        IBlindedTreatmentClassifier classifier,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptCache)
    {
        bool IsFormBlindedItem(CollectionInstanceRecord form, string itemId)
        {
            var specItem = form.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
            var recorded = form.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal));
            BlindedConceptMetadata? meta = specItem is not null && conceptCache.TryGetValue(specItem.Concept.ConceptId, out var m)
                ? m
                : null;
            return classifier.IsBlindedCollectionItem(form.CollectionSpecId, form.CollectionTitle, specItem, recorded?.Value, meta, designs);
        }

        bool MatchesVariable(string? variable)
        {
            if (string.IsNullOrWhiteSpace(variable))
            {
                return false;
            }

            var dot = variable.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && dot < variable.Length - 1)
            {
                var specId = variable[..dot];
                var itemId = variable[(dot + 1)..];
                return siblingForms
                    .Where(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal))
                    .Any(f => IsFormBlindedItem(f, itemId));
            }

            return IsFormBlindedItem(targetForm, variable)
                || siblingForms.Any(f => IsFormBlindedItem(f, variable));
        }

        return classifier.IsBlindedCollectionSpec(targetForm.CollectionSpecId, targetForm.CollectionTitle, designs: designs)
            || MatchesVariable(finding.Target.Variable)
            || finding.Observed.Any(o => MatchesVariable(o.Variable));
    }

    private static async Task<IReadOnlyList<ProtocolDesign>> LoadStudyDesignsAsync(
        TenantId tenant,
        StudyId studyId,
        IProtocolVersionReader? protocols,
        IEnumerable<CollectionInstanceRecord> forms,
        CancellationToken cancellationToken)
    {
        if (protocols is null)
        {
            return [];
        }

        var designs = new List<ProtocolDesign>();
        var seen = new HashSet<ProtocolVersionId>();
        foreach (var versionId in forms.Select(f => f.ProtocolVersionId).Distinct())
        {
            if (seen.Add(versionId)
                && await protocols.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                designs.Add(design);
            }
        }

        if (designs.Count > 0)
        {
            return designs;
        }

        var summaries = await protocols.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        foreach (var summaryId in summaries.Select(summary => summary.Id).Where(seen.Add))
        {
            if (await protocols.GetDesignAsync(tenant, summaryId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                designs.Add(design);
            }
        }

        return designs;
    }

    private static async Task<Dictionary<ConceptId, BlindedConceptMetadata>> LoadStudyConceptsAsync(
        TenantId tenant,
        IConceptRegistry? concepts,
        IEnumerable<CollectionInstanceRecord> forms,
        IReadOnlyList<ProtocolDesign> designs,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<ConceptId, BlindedConceptMetadata>();
        if (concepts is null)
        {
            return resolved;
        }

        var refs = forms.SelectMany(f => f.Items).Select(i => i.Concept)
            .Concat(designs.SelectMany(d => d.CollectionSpecs).SelectMany(s => s.Items).Select(i => i.Concept))
            .Distinct();
        foreach (var conceptRef in refs)
        {
            var view = await concepts.ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false)
                ?? await concepts.GetCurrentAsync(tenant, conceptRef.ConceptId, cancellationToken).ConfigureAwait(false);
            if (view is not null)
            {
                resolved[conceptRef.ConceptId] = new BlindedConceptMetadata(view.ExternalCode, view.PreferredLabel, view.Definition);
            }
        }

        return resolved;
    }

    private sealed record HydratedFindingResult(
        ValidationFindingView View,
        KernelValidationFindingResponse Response,
        IReadOnlyList<NodeRef> ExtraEvidence);

    private sealed record ResolvedFormQueryLink(NodeRef? Ref, bool IsMasked);

    private static async Task<IReadOnlyList<HydratedFindingResult>> HydrateFindingsAsync(
        TenantId tenant,
        StudyId studyId,
        IReadOnlyList<ValidationFindingView> views,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (views.Count == 0)
        {
            return [];
        }

        var collections = services.GetService<ICollectionInstanceRecords>();
        var protocols = services.GetService<IProtocolVersionReader>();
        var navigator = services.GetService<IKernelNavigator>();
        var lineage = services.GetService<IKernelLineage>();
        var probe = services.GetService<IAuthorizationProbe>();

        var studyForms = collections is not null
            ? await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false)
            : [];
        var formsById = studyForms.ToDictionary(f => f.CollectionInstanceId);
        var formsByEnrollment = studyForms
            .GroupBy(f => f.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstanceRecord>)g.ToList());
        var formByObservation = new Dictionary<ObservationId, (CollectionInstanceRecord Form, CollectedValueView Value)>();
        foreach (var form in studyForms)
        {
            foreach (var val in form.Values)
            {
                formByObservation.TryAdd(val.ObservationId, (form, val));
            }
        }

        var designsByVersion = await LoadDesignsByVersionAsync(tenant, studyId, protocols, studyForms, cancellationToken).ConfigureAwait(false);
        var enrollmentDirectory = navigator is not null
            ? (await navigator.GetStudyEnrollmentsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false) ?? [])
                .ToDictionary(e => e.EnrollmentId)
            : new Dictionary<EnrollmentId, StudyEnrollmentNode>();

        var unblindByEnrollment = new Dictionary<EnrollmentId, bool>();
        async Task<bool> CanUnblindEnrollmentAsync(EnrollmentId enrollmentId)
        {
            if (probe is null)
            {
                return true;
            }

            if (unblindByEnrollment.TryGetValue(enrollmentId, out var cached))
            {
                return cached;
            }

            var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, NodeRef.For(enrollmentId), cancellationToken).ConfigureAwait(false);
            unblindByEnrollment[enrollmentId] = decision.Permitted;
            return decision.Permitted;
        }

        var perFindingContext = new Dictionary<ValidationFindingId, (
            ValidationFindingView EffectiveView,
            EnrollmentId? EnrollmentId,
            StudySiteId? SiteId,
            CollectionInstanceRecord? Form,
            string? ItemId,
            string? ItemLabel,
            ObservationId? PrimaryObsId,
            IReadOnlyList<ObservationId> AllObsIds,
            bool CanUnblind)>(views.Count);

        var nodesToResolve = new HashSet<NodeRef>();
        foreach (var rawView in views)
        {
            EnrollmentId? enrollmentId = rawView.Enrollment;
            CollectionInstanceRecord? rawForm = null;
            ObservationId? directObsId = null;

            if (rawView.Target.TryGetCollectionInstanceId(out var formId)
                && formsById.TryGetValue(formId, out var matchedForm))
            {
                rawForm = matchedForm;
                enrollmentId ??= matchedForm.EnrollmentId;
            }
            else if (rawView.Target.TryGetObservationId(out var obsId))
            {
                directObsId = obsId;
                if (formByObservation.TryGetValue(obsId, out var owner))
                {
                    rawForm = owner.Form;
                    enrollmentId ??= owner.Form.EnrollmentId;
                }
            }

            var canUnblind = enrollmentId is not { } eid || await CanUnblindEnrollmentAsync(eid).ConfigureAwait(false);
            var effectiveView = canUnblind ? rawView : rawView.MaskBlinded();
            var effectiveForm = rawForm is not null && !canUnblind ? rawForm.MaskBlinded() : rawForm;

            StudySiteId? siteId = effectiveForm?.StudySiteId;
            if (siteId is null && enrollmentId is { } enrolled && enrollmentDirectory.TryGetValue(enrolled, out var dirEntry))
            {
                siteId = dirEntry.StudySiteId;
            }

            string? itemId = null;
            string? itemLabel = null;
            ObservationId? primaryObsId = directObsId;
            var allObsIds = new List<ObservationId>();

            if (effectiveForm is not null)
            {
                var rawSiblings = formsByEnrollment.GetValueOrDefault(effectiveForm.EnrollmentId) ?? [effectiveForm];
                var siblings = canUnblind ? rawSiblings : [.. rawSiblings.Select(s => s.MaskBlinded())];
                var resolvedObs = ResolveReferencedObservationIds(effectiveView, effectiveForm, siblings);
                allObsIds.AddRange(resolvedObs);

                if (directObsId is { } dObs)
                {
                    if (!allObsIds.Contains(dObs))
                    {
                        allObsIds.Insert(0, dObs);
                    }

                    itemId = effectiveForm.Values.FirstOrDefault(v => v.ObservationId == dObs)?.ItemId ?? effectiveView.Target.Variable;
                }
                else
                {
                    itemId = ResolvePrimaryFindingItemId(effectiveView, effectiveForm, siblings);
                    if (primaryObsId is null && !string.IsNullOrWhiteSpace(itemId))
                    {
                        primaryObsId = MatchVariableObservations(effectiveForm.CollectionSpecId, itemId, effectiveForm, siblings)
                            .FirstOrDefault();
                        if (primaryObsId is { IsEmpty: true })
                        {
                            primaryObsId = null;
                        }
                    }

                    primaryObsId ??= resolvedObs.Count > 0 ? resolvedObs[0] : null;
                }

                itemLabel = ResolveCollectionItemLabel(effectiveForm, siblings, designsByVersion, itemId);
            }
            else if (directObsId is { } standaloneObs)
            {
                allObsIds.Add(standaloneObs);
                itemId = effectiveView.Target.Variable;
            }

            nodesToResolve.Add(NodeRef.For(effectiveView.RulePack));
            if (effectiveView.Target.Node is { } tNode)
            {
                nodesToResolve.Add(tNode.Unversioned());
            }

            if (siteId is { IsEmpty: false } sId)
            {
                nodesToResolve.Add(NodeRef.For(sId));
            }

            if (enrollmentId is { IsEmpty: false } eId)
            {
                nodesToResolve.Add(NodeRef.For(eId));
            }

            if (effectiveForm is not null)
            {
                nodesToResolve.Add(NodeRef.For(effectiveForm.CollectionInstanceId));
            }

            foreach (var oId in allObsIds.Where(o => !o.IsEmpty))
            {
                nodesToResolve.Add(NodeRef.For(oId));
            }

            perFindingContext[effectiveView.Id] = (
                effectiveView,
                enrollmentId,
                siteId,
                effectiveForm,
                NormalizeItemCode(itemId),
                itemLabel,
                primaryObsId,
                allObsIds,
                canUnblind);
        }

        var queryByFinding = new Dictionary<ValidationFindingId, ResolvedFormQueryLink>();
        if (lineage is not null && actor.Actor is { } reader)
        {
            var obsQueryCache = new Dictionary<ObservationId, ResolvedFormQueryLink?>();
            foreach (var rawView in views)
            {
                var ctx = perFindingContext[rawView.Id];
                var (fromFinding, findingLineageOk) = await TraceFormQueryAsync(lineage, tenant, reader, NodeRef.For(rawView.Id), cancellationToken).ConfigureAwait(false);
                if (!findingLineageOk)
                {
                    break;
                }

                if (fromFinding is null && ctx.PrimaryObsId is { IsEmpty: false } pObs)
                {
                    if (!obsQueryCache.TryGetValue(pObs, out var cachedObsQuery))
                    {
                        var (obsQuery, obsLineageOk) = await TraceFormQueryAsync(lineage, tenant, reader, NodeRef.For(pObs), cancellationToken).ConfigureAwait(false);
                        if (!obsLineageOk)
                        {
                            break;
                        }

                        cachedObsQuery = obsQuery;
                        obsQueryCache[pObs] = cachedObsQuery;
                    }

                    fromFinding = cachedObsQuery;
                }

                if (fromFinding is not null)
                {
                    queryByFinding[rawView.Id] = fromFinding;
                    if (fromFinding.Ref is { } qRef)
                    {
                        nodesToResolve.Add(qRef.Unversioned());
                    }
                }
            }
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [.. nodesToResolve], cancellationToken).ConfigureAwait(false);
        var summaries = navigator is not null && nodesToResolve.Count > 0
            ? await navigator.ResolveManyAsync(tenant, [.. nodesToResolve], cancellationToken).ConfigureAwait(false)
            : new Dictionary<NodeRef, NodeSummary>();

        var results = new List<HydratedFindingResult>(views.Count);
        foreach (var rawView in views)
        {
            var ctx = perFindingContext[rawView.Id];
            var view = ctx.EffectiveView;

            var packNode = NodeRef.For(view.RulePack);
            string rulePackLabel;
            if (masks.For(packNode) is not null)
            {
                rulePackLabel = ValidationFindingView.MaskedValue;
            }
            else if (summaries.TryGetValue(packNode, out var packSummary) && !string.IsNullOrWhiteSpace(packSummary.Title))
            {
                rulePackLabel = packSummary.Title;
            }
            else
            {
                rulePackLabel = view.PackKey;
            }

            string? siteRef = null;
            string? siteLabel = null;
            if (ctx.SiteId is { IsEmpty: false } sId)
            {
                var siteNode = NodeRef.For(sId);
                if (masks.For(siteNode) is not null)
                {
                    siteLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    siteRef = siteNode.ToString();
                    summaries.TryGetValue(siteNode, out var siteSummary);
                    siteLabel = FormatSiteLabel(
                        siteSummary,
                        ctx.EnrollmentId is { } eid && enrollmentDirectory.TryGetValue(eid, out var de) ? de.SiteCode : null);
                }
            }

            string? participantRef = null;
            string? participantId = null;
            string? participantLabel = null;
            if (ctx.EnrollmentId is { IsEmpty: false } eId)
            {
                var enrollmentNode = NodeRef.For(eId);
                if (masks.For(enrollmentNode) is not null)
                {
                    participantLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    participantRef = enrollmentNode.ToString();
                    if (ctx.Form is { ParticipantId.IsEmpty: false } formWithParticipant)
                    {
                        participantId = NodeRef.For(formWithParticipant.ParticipantId).ToString();
                    }

                    summaries.TryGetValue(enrollmentNode, out var enrollmentSummary);
                    participantLabel = enrollmentSummary?.Code
                        ?? enrollmentSummary?.Title
                        ?? (enrollmentDirectory.TryGetValue(eId, out var de) ? de.SubjectCode : null);
                }
            }

            string? plannedActivityId = null;
            string? visitLabel = null;
            string? formRef = null;
            string? formIdText = null;
            string? formLabel = null;
            string? collectionSpecId = null;
            if (ctx.Form is { } form)
            {
                var formNode = NodeRef.For(form.CollectionInstanceId);
                var design = designsByVersion.GetValueOrDefault(form.ProtocolVersionId);
                if (masks.For(formNode) is not null)
                {
                    formLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    formRef = formNode.ToString();
                    formIdText = form.CollectionInstanceId.Value.ToString("D");
                    collectionSpecId = form.CollectionSpecId;
                    plannedActivityId = form.PlannedActivityId;
                    visitLabel = ResolveVisitLabel(form.PlannedActivityId, design);
                    summaries.TryGetValue(formNode, out var formSummary);
                    formLabel = ResolveFormLabel(form, design, formSummary);
                }
            }

            string? observationRef = null;
            string? observationLabel = null;
            if (ctx.PrimaryObsId is { IsEmpty: false } pObsId)
            {
                var obsNode = NodeRef.For(pObsId);
                var obsIsBlinded = !ctx.CanUnblind
                    && ((ctx.Form is not null && ctx.ItemId is not null && ctx.Form.IsValueBlinded(ctx.ItemId, pObsId))
                        || (view.BlindedVariables is not null && ctx.ItemId is not null && view.BlindedVariables.Contains(ctx.ItemId)));
                if (masks.For(obsNode) is not null)
                {
                    observationLabel = ValidationFindingView.MaskedValue;
                }
                else if (obsIsBlinded)
                {
                    observationRef = obsNode.ToString();
                    observationLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    observationRef = obsNode.ToString();
                    summaries.TryGetValue(obsNode, out var obsSummary);
                    observationLabel = obsSummary?.Title ?? ctx.ItemLabel ?? ctx.ItemId;
                }
            }

            string? queryRef = null;
            Guid? queryId = null;
            string? queryLabel = null;
            if (queryByFinding.TryGetValue(rawView.Id, out var qLink))
            {
                var queryBlinded = !ctx.CanUnblind
                    && ctx.Form is not null
                    && ctx.ItemId is not null
                    && ctx.PrimaryObsId is { } qObs
                    && ctx.Form.IsQueryBlinded(ctx.ItemId, qObs);
                if (qLink.IsMasked || queryBlinded || qLink.Ref is null || masks.For(qLink.Ref.Unversioned()) is not null)
                {
                    queryLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    var unversionedQuery = qLink.Ref.Unversioned();
                    queryRef = unversionedQuery.ToString();
                    queryId = unversionedQuery.Id;
                    summaries.TryGetValue(unversionedQuery, out var querySummary);
                    queryLabel = !string.IsNullOrWhiteSpace(querySummary?.Title)
                        ? querySummary.Title
                        : $"Query ({view.RuleId})";
                }
            }

            var extraEvidence = new List<NodeRef>();
            if (formRef is not null && NodeRef.TryParse(formRef, out var visibleFormNode))
            {
                extraEvidence.Add(visibleFormNode);
            }

            foreach (var obsCandidate in ctx.AllObsIds.Where(o => !o.IsEmpty))
            {
                var candNode = NodeRef.For(obsCandidate);
                if (masks.For(candNode) is null)
                {
                    extraEvidence.Add(candNode);
                }
            }

            if (queryRef is not null && NodeRef.TryParse(queryRef, out var visibleQueryNode))
            {
                extraEvidence.Add(visibleQueryNode);
            }

            string? targetNodeRef = null;
            string? targetLabel = view.TargetLabel;
            if (view.Target.Kind == FindingTargetKind.DatasetRecord)
            {
                targetNodeRef = formRef ?? (view.Target.Node is { } tn && masks.For(tn) is null ? tn.ToString() : null);
                targetLabel = BuildTargetDisplayLabel(formLabel, visitLabel, ctx.ItemLabel ?? ctx.ItemId) ?? targetLabel;
            }
            else if (view.Target.Kind == FindingTargetKind.Observation)
            {
                targetNodeRef = observationRef ?? (view.Target.Node is { } tn && masks.For(tn) is null ? tn.ToString() : null);
                targetLabel = observationLabel == ValidationFindingView.MaskedValue
                    ? ValidationFindingView.MaskedValue
                    : (BuildTargetDisplayLabel(formLabel, visitLabel, ctx.ItemLabel ?? observationLabel) ?? observationLabel ?? targetLabel);
            }
            else if (view.Target.Kind == FindingTargetKind.DesignElement && view.Target.Node is { } designNode)
            {
                var unversionedDesign = designNode.Unversioned();
                if (masks.For(unversionedDesign) is not null)
                {
                    targetLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    targetNodeRef = unversionedDesign.ToString();
                    extraEvidence.Add(unversionedDesign);
                    if (summaries.TryGetValue(unversionedDesign, out var designSummary) && !string.IsNullOrWhiteSpace(designSummary.Title))
                    {
                        targetLabel = designSummary.Title;
                    }
                }
            }

            var baseResponse = KernelValidationFindingResponse.From(view);
            var enriched = baseResponse with
            {
                RulePackLabel = rulePackLabel,
                TargetNodeRef = targetNodeRef,
                TargetLabel = targetLabel,
                SiteRef = siteRef,
                SiteLabel = siteLabel,
                ParticipantRef = participantRef,
                ParticipantId = participantId,
                ParticipantLabel = participantLabel,
                PlannedActivityId = plannedActivityId,
                VisitLabel = visitLabel,
                FormRef = formRef,
                FormId = formIdText,
                FormLabel = formLabel,
                CollectionSpecId = collectionSpecId ?? view.Target.Dataset,
                ItemId = ctx.ItemId ?? view.Target.Variable,
                ItemLabel = ctx.ItemLabel,
                ObservationRef = observationRef,
                ObservationLabel = observationLabel,
                QueryRef = queryRef,
                QueryId = queryId,
                QueryLabel = queryLabel,
            };

            results.Add(new HydratedFindingResult(view, enriched, extraEvidence.Distinct().ToArray()));
        }

        return results;
    }

    private static async Task<IReadOnlyList<KernelRulePackPreviewViolationResponse>> HydratePreviewViolationsAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemView rulePack,
        IReadOnlyList<CollectionRulePackPreviewViolation> violations,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        IReadOnlySet<EnrollmentId>? unblindedEnrollments,
        CancellationToken cancellationToken)
    {
        if (violations.Count == 0)
        {
            return [];
        }

        var collections = services.GetService<ICollectionInstanceRecords>();
        var protocols = services.GetService<IProtocolVersionReader>();
        var navigator = services.GetService<IKernelNavigator>();
        var lineage = services.GetService<IKernelLineage>();

        var studyForms = collections is not null
            ? await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false)
            : [];
        var formsById = studyForms.ToDictionary(f => f.CollectionInstanceId);
        var formsByEnrollment = studyForms
            .GroupBy(f => f.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstanceRecord>)g.ToList());
        var designsByVersion = await LoadDesignsByVersionAsync(tenant, studyId, protocols, studyForms, cancellationToken).ConfigureAwait(false);
        var enrollmentDirectory = navigator is not null
            ? (await navigator.GetStudyEnrollmentsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false) ?? [])
                .ToDictionary(e => e.EnrollmentId)
            : new Dictionary<EnrollmentId, StudyEnrollmentNode>();

        var rulePackRef = NodeRef.For(rulePack.Id).ToString();
        var rulePackLabel = !string.IsNullOrWhiteSpace(rulePack.Title) ? rulePack.Title : rulePack.Key;

        var nodesToResolve = new HashSet<NodeRef>();
        var primaryObsByIndex = new ObservationId?[violations.Count];
        var itemLabelByIndex = new string?[violations.Count];
        var effectiveFormByIndex = new CollectionInstanceRecord?[violations.Count];
        var canUnblindByIndex = new bool[violations.Count];

        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            if (!formsById.TryGetValue(violation.FormId, out var rawForm))
            {
                continue;
            }

            var canUnblind = unblindedEnrollments is null || unblindedEnrollments.Contains(rawForm.EnrollmentId);
            canUnblindByIndex[i] = canUnblind;
            var form = canUnblind ? rawForm : rawForm.MaskBlinded();
            effectiveFormByIndex[i] = form;

            var rawSiblings = formsByEnrollment.GetValueOrDefault(form.EnrollmentId) ?? [form];
            var siblings = canUnblind ? rawSiblings : [.. rawSiblings.Select(s => s.MaskBlinded())];

            nodesToResolve.Add(NodeRef.For(form.CollectionInstanceId));
            nodesToResolve.Add(NodeRef.For(form.EnrollmentId));
            if (!form.StudySiteId.IsEmpty)
            {
                nodesToResolve.Add(NodeRef.For(form.StudySiteId));
            }

            var normalizedItem = NormalizeItemCode(violation.ItemId);
            itemLabelByIndex[i] = ResolveCollectionItemLabel(form, siblings, designsByVersion, violation.ItemId);
            if (!string.IsNullOrWhiteSpace(violation.ItemId))
            {
                var obsId = MatchVariableObservations(form.CollectionSpecId, violation.ItemId, form, siblings).FirstOrDefault();
                if (!obsId.IsEmpty)
                {
                    primaryObsByIndex[i] = obsId;
                    nodesToResolve.Add(NodeRef.For(obsId));
                }
            }
            else if (!string.IsNullOrWhiteSpace(normalizedItem))
            {
                var obsVal = form.Values.FirstOrDefault(v => string.Equals(v.ItemId, normalizedItem, StringComparison.Ordinal));
                if (obsVal is not null && !obsVal.ObservationId.IsEmpty)
                {
                    primaryObsByIndex[i] = obsVal.ObservationId;
                    nodesToResolve.Add(NodeRef.For(obsVal.ObservationId));
                }
            }
        }

        var obsQueryCache = new Dictionary<ObservationId, ResolvedFormQueryLink?>();
        if (lineage is not null && actor.Actor is { } reader)
        {
            foreach (var obsId in primaryObsByIndex.Where(o => o is { IsEmpty: false }).Select(o => o!.Value).Distinct())
            {
                var (qLink, lineageOk) = await TraceFormQueryAsync(lineage, tenant, reader, NodeRef.For(obsId), cancellationToken).ConfigureAwait(false);
                if (!lineageOk)
                {
                    break;
                }

                obsQueryCache[obsId] = qLink;
                if (qLink?.Ref is { } qRef)
                {
                    nodesToResolve.Add(qRef.Unversioned());
                }
            }
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [.. nodesToResolve], cancellationToken).ConfigureAwait(false);
        var summaries = navigator is not null && nodesToResolve.Count > 0
            ? await navigator.ResolveManyAsync(tenant, [.. nodesToResolve], cancellationToken).ConfigureAwait(false)
            : new Dictionary<NodeRef, NodeSummary>();

        var responses = new List<KernelRulePackPreviewViolationResponse>(violations.Count);
        for (var i = 0; i < violations.Count; i++)
        {
            var violation = violations[i];
            var baseResp = KernelRulePackPreviewViolationResponse.From(violation) with
            {
                RulePackRef = rulePackRef,
                RulePackLabel = rulePackLabel,
            };

            if (effectiveFormByIndex[i] is not { } form)
            {
                responses.Add(baseResp);
                continue;
            }

            var canUnblind = canUnblindByIndex[i];
            var design = designsByVersion.GetValueOrDefault(form.ProtocolVersionId);

            string? siteRef = null;
            string? siteLabel = null;
            if (!form.StudySiteId.IsEmpty)
            {
                var siteNode = NodeRef.For(form.StudySiteId);
                if (masks.For(siteNode) is not null)
                {
                    siteLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    siteRef = siteNode.ToString();
                    summaries.TryGetValue(siteNode, out var siteSummary);
                    siteLabel = FormatSiteLabel(
                        siteSummary,
                        enrollmentDirectory.TryGetValue(form.EnrollmentId, out var de) ? de.SiteCode : null);
                }
            }

            string? participantRef = null;
            string? participantId = null;
            string? participantLabel = null;
            var enrollmentNode = NodeRef.For(form.EnrollmentId);
            if (masks.For(enrollmentNode) is not null)
            {
                participantLabel = ValidationFindingView.MaskedValue;
            }
            else
            {
                participantRef = enrollmentNode.ToString();
                if (!form.ParticipantId.IsEmpty)
                {
                    participantId = NodeRef.For(form.ParticipantId).ToString();
                }

                summaries.TryGetValue(enrollmentNode, out var enrollmentSummary);
                participantLabel = enrollmentSummary?.Code
                    ?? enrollmentSummary?.Title
                    ?? (enrollmentDirectory.TryGetValue(form.EnrollmentId, out var de) ? de.SubjectCode : null);
            }

            var formNode = NodeRef.For(form.CollectionInstanceId);
            string? formRef = null;
            string? formLabel = null;
            string? plannedActivityId = null;
            string? visitLabel = null;
            if (masks.For(formNode) is not null)
            {
                formLabel = ValidationFindingView.MaskedValue;
            }
            else
            {
                formRef = formNode.ToString();
                plannedActivityId = form.PlannedActivityId;
                visitLabel = ResolveVisitLabel(form.PlannedActivityId, design);
                summaries.TryGetValue(formNode, out var formSummary);
                formLabel = ResolveFormLabel(form, design, formSummary);
            }

            var normalizedItem = NormalizeItemCode(violation.ItemId);
            string? observationRef = null;
            string? observationLabel = null;
            string? queryRef = null;
            Guid? queryId = null;
            string? queryLabel = null;

            if (primaryObsByIndex[i] is { IsEmpty: false } pObsId)
            {
                var obsNode = NodeRef.For(pObsId);
                var obsBlinded = !canUnblind && normalizedItem is not null && form.IsValueBlinded(normalizedItem, pObsId);
                if (masks.For(obsNode) is not null)
                {
                    observationLabel = ValidationFindingView.MaskedValue;
                }
                else if (obsBlinded)
                {
                    observationRef = obsNode.ToString();
                    observationLabel = ValidationFindingView.MaskedValue;
                }
                else
                {
                    observationRef = obsNode.ToString();
                    summaries.TryGetValue(obsNode, out var obsSummary);
                    observationLabel = obsSummary?.Title ?? itemLabelByIndex[i] ?? normalizedItem;
                }

                if (obsQueryCache.TryGetValue(pObsId, out var qLink) && qLink is not null)
                {
                    var qBlinded = !canUnblind && normalizedItem is not null && form.IsQueryBlinded(normalizedItem, pObsId);
                    if (qLink.IsMasked || qBlinded || qLink.Ref is null || masks.For(qLink.Ref.Unversioned()) is not null)
                    {
                        queryLabel = ValidationFindingView.MaskedValue;
                    }
                    else
                    {
                        var unversionedQuery = qLink.Ref.Unversioned();
                        queryRef = unversionedQuery.ToString();
                        queryId = unversionedQuery.Id;
                        summaries.TryGetValue(unversionedQuery, out var querySummary);
                        queryLabel = !string.IsNullOrWhiteSpace(querySummary?.Title)
                            ? querySummary.Title
                            : $"Query ({violation.RuleId})";
                    }
                }
            }

            responses.Add(baseResp with
            {
                ItemId = normalizedItem ?? violation.ItemId,
                SiteRef = siteRef,
                SiteLabel = siteLabel,
                ParticipantRef = participantRef,
                ParticipantId = participantId,
                ParticipantLabel = participantLabel,
                PlannedActivityId = plannedActivityId,
                VisitLabel = visitLabel,
                FormRef = formRef,
                FormLabel = formLabel,
                CollectionSpecId = form.CollectionSpecId,
                ItemLabel = itemLabelByIndex[i],
                ObservationRef = observationRef,
                ObservationLabel = observationLabel,
                QueryRef = queryRef,
                QueryId = queryId,
                QueryLabel = queryLabel,
            });
        }

        return responses;
    }

    private static async Task<(ResolvedFormQueryLink? Link, bool Available)> TraceFormQueryAsync(
        IKernelLineage lineage,
        TenantId tenant,
        Actor reader,
        NodeRef startNode,
        CancellationToken cancellationToken)
    {
        LineageGraph graph;
        try
        {
            graph = await lineage.TraceAsync(
                tenant,
                reader,
                new LineageQuery(startNode, LineageDirection.Forward, Depth: 1, EdgeKinds: DerivedFromOnly),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsTransientDatabaseException(ex))
        {
            return (null, false);
        }

        var queryNodes = graph.Nodes
            .Where(n => n.Distance > 0 && n.Kind == NodeKinds.FormQuery)
            .ToList();
        if (queryNodes.Count == 0)
        {
            return (null, true);
        }

        var visible = queryNodes.LastOrDefault(n => !n.IsMasked && n.Ref is not null);
        if (visible is not null)
        {
            return (new ResolvedFormQueryLink(visible.Ref!.Unversioned(), IsMasked: false), true);
        }

        return (new ResolvedFormQueryLink(null, IsMasked: true), true);
    }

    private static bool IsTransientDatabaseException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is System.Data.Common.DbException)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<Dictionary<ProtocolVersionId, ProtocolDesign>> LoadDesignsByVersionAsync(
        TenantId tenant,
        StudyId studyId,
        IProtocolVersionReader? protocols,
        IReadOnlyCollection<CollectionInstanceRecord> forms,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<ProtocolVersionId, ProtocolDesign>();
        if (protocols is null)
        {
            return map;
        }

        foreach (var versionId in forms.Select(f => f.ProtocolVersionId).Distinct())
        {
            if (await protocols.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                map[versionId] = design;
            }
        }

        if (map.Count == 0)
        {
            var summaries = await protocols.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
            foreach (var summary in summaries)
            {
                if (await protocols.GetDesignAsync(tenant, summary.Id, cancellationToken).ConfigureAwait(false) is { } design)
                {
                    map[summary.Id] = design;
                }
            }
        }

        return map;
    }

    private static string? ResolvePrimaryFindingItemId(
        ValidationFindingView view,
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblings)
    {
        if (!string.IsNullOrWhiteSpace(view.Target.Variable))
        {
            return view.Target.Variable;
        }

        foreach (var obs in view.Observed)
        {
            if (string.IsNullOrWhiteSpace(obs.Variable))
            {
                continue;
            }

            var normalized = NormalizeItemCode(obs.Variable);
            if (targetForm.Items.Any(i => string.Equals(i.Id, normalized, StringComparison.Ordinal))
                || targetForm.Values.Any(v => string.Equals(v.ItemId, normalized, StringComparison.Ordinal))
                || siblings.Any(f => f.Items.Any(i => string.Equals(i.Id, normalized, StringComparison.Ordinal))))
            {
                return obs.Variable;
            }
        }

        return view.Observed.FirstOrDefault()?.Variable;
    }

    private static string? NormalizeItemCode(string? variable)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            return null;
        }

        var trimmed = variable.Trim();
        var dot = trimmed.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && dot < trimmed.Length - 1 ? trimmed[(dot + 1)..] : trimmed;
    }

    private static string? ResolveCollectionItemLabel(
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblings,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign> designsByVersion,
        string? rawVariable)
    {
        if (string.IsNullOrWhiteSpace(rawVariable))
        {
            return null;
        }

        var specId = targetForm.CollectionSpecId;
        var itemId = rawVariable.Trim();
        var dot = itemId.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < itemId.Length - 1)
        {
            specId = itemId[..dot];
            itemId = itemId[(dot + 1)..];
        }

        var form = siblings.FirstOrDefault(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal)) ?? targetForm;
        var item = form.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal))
            ?? (designsByVersion.TryGetValue(form.ProtocolVersionId, out var design)
                ? design.CollectionSpecs
                    .FirstOrDefault(s => string.Equals(s.Id, form.CollectionSpecId, StringComparison.Ordinal))
                    ?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal))
                : null);

        return !string.IsNullOrWhiteSpace(item?.Prompt) ? item.Prompt.Trim() : itemId;
    }

    private static string? ResolveVisitLabel(string? plannedActivityId, ProtocolDesign? design)
    {
        if (string.IsNullOrWhiteSpace(plannedActivityId))
        {
            return null;
        }

        var name = design?.PlannedActivities
            .FirstOrDefault(a => string.Equals(a.Id, plannedActivityId, StringComparison.Ordinal))?.Name;
        return !string.IsNullOrWhiteSpace(name) ? name.Trim() : plannedActivityId.Trim();
    }

    private static string ResolveFormLabel(CollectionInstanceRecord form, ProtocolDesign? design, NodeSummary? summary)
    {
        if (!string.IsNullOrWhiteSpace(form.CollectionTitle))
        {
            return form.CollectionTitle.Trim();
        }

        var specName = design?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, form.CollectionSpecId, StringComparison.Ordinal))?.Name;
        if (!string.IsNullOrWhiteSpace(specName))
        {
            return specName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(summary?.Title))
        {
            return summary.Title.Trim();
        }

        return form.CollectionSpecId;
    }

    private static string? FormatSiteLabel(NodeSummary? summary, string? fallbackSiteCode)
    {
        var code = !string.IsNullOrWhiteSpace(summary?.Code) ? summary.Code.Trim() : fallbackSiteCode?.Trim();
        var title = !string.IsNullOrWhiteSpace(summary?.Title) ? summary.Title.Trim() : null;
        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(title) && !string.Equals(code, title, StringComparison.OrdinalIgnoreCase))
        {
            return $"{code} · {title}";
        }

        return code ?? title;
    }

    private static string? BuildTargetDisplayLabel(string? formLabel, string? visitLabel, string? itemLabel)
    {
        if (formLabel == ValidationFindingView.MaskedValue)
        {
            return ValidationFindingView.MaskedValue;
        }

        string? formWithVisit = null;
        if (!string.IsNullOrWhiteSpace(formLabel))
        {
            formWithVisit = !string.IsNullOrWhiteSpace(visitLabel) && !formLabel.Contains(visitLabel, StringComparison.Ordinal)
                ? $"{formLabel} ({visitLabel})"
                : formLabel;
        }

        if (!string.IsNullOrWhiteSpace(formWithVisit) && !string.IsNullOrWhiteSpace(itemLabel))
        {
            return $"{formWithVisit} · {itemLabel}";
        }

        return formWithVisit ?? itemLabel;
    }

    private static Guid DeriveGuid(TenantId tenant, string kind, string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-validation:{tenant}:{kind}:{seed}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
