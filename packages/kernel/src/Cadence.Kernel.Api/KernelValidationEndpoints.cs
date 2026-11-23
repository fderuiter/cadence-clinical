using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
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
/// Study rule-pack governance, in-memory rule-pack preview, validation finding inspection, and finding decision routes
/// (spec §6.3, §9.3, §10.3, K-RULE-3, K-FIND-1, K-FIND-2, K-AUTH-4). Every mutating action dispatches through
/// <see cref="IKernelCommandSender"/> with an <c>Idempotency-Key</c> and a signed <c>X-Change-Reason</c>.
/// </summary>
public static class KernelValidationEndpoints
{
    /// <summary>The semantic action a library-item approval step-up token must carry.</summary>
    public const string ApproveLibraryItemSemanticAction = "kernel.library_item.approve";

    /// <summary>The semantic action a finding-acceptance step-up token must carry.</summary>
    public const string AcceptFindingSemanticAction = "kernel.validation_finding.accept";

    /// <summary>The semantic action a finding-explanation step-up token must carry.</summary>
    public const string ExplainFindingSemanticAction = "kernel.validation_finding.explain";

    private const string RedactedBlindedValue = "masked";

    private static readonly Capability ApproveLibraryItemCapability = new("library_item.approve", VerbClass.Approve);
    private static readonly Capability RegisterStandardCapability = new("standard.register", VerbClass.Execute);
    private static readonly Capability ApproveStandardCapability = new("standard.approve", VerbClass.Approve);
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);
    private static readonly Capability AcceptFindingCapability = new("validation_finding.accept", VerbClass.Approve);
    private static readonly Capability ExplainFindingCapability = new("validation_finding.explain", VerbClass.Approve);

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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<InvestigateFinding>();

        group.MapPost("/validation/findings/{findingRef}/classify", ClassifyFindingAsync)
            .WithName("KernelClassifyValidationFinding")
            .WithSummary("Records the root-cause classification of a validation finding (validation_finding.classify).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<ClassifyFinding>();

        group.MapPost("/validation/findings/{findingRef}/accept", AcceptFindingAsync)
            .WithName("KernelAcceptValidationFinding")
            .WithSummary("Accepts an open or investigating validation finding with a Responsibility attestation (validation_finding.accept).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                .. RaisesKernelErrorsAttribute.Of(typeof(AcceptFinding))!,
                KernelErrors.AlreadyExists,
            ])
            .RequireElectronicSignature(
                AcceptFindingSemanticAction,
                ctx => AcceptFindingAction(ctx.Request.RouteValues["findingRef"]?.ToString() ?? string.Empty));

        group.MapPost("/validation/findings/{findingRef}/explain", ExplainFindingAsync)
            .WithName("KernelExplainValidationFinding")
            .WithSummary("Explains an open or investigating validation finding for the reviewer guide with an Approved attestation (validation_finding.explain).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelErrors([
                .. KernelErrors.CommandPipeline,
                .. RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                .. RaisesKernelErrorsAttribute.Of(typeof(ExplainFinding))!,
                KernelErrors.AlreadyExists,
            ])
            .RequireElectronicSignature(
                ExplainFindingSemanticAction,
                ctx => ExplainFindingAction(ctx.Request.RouteValues["findingRef"]?.ToString() ?? string.Empty));

        group.MapPost("/validation/findings/{findingRef}/reopen", ReopenFindingAsync)
            .WithName("KernelReopenValidationFinding")
            .WithSummary("Withdraws an active investigation, acceptance, or explanation on a validation finding (validation_finding.reopen).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
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

        var access = await ValidationAccessProjector.CreateForStudyAsync(
            tenant,
            resolved.StudyId,
            preview.Violations.Select(v => v.FormId),
            findings: [],
            context,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);

        var ruleVariablesById = ReadRuleVariablesById(item.Content);
        var visibleViolations = new List<KernelRulePackPreviewViolationResponse>(preview.Violations.Count);
        foreach (var violation in preview.Violations)
        {
            var form = access.FindForm(violation.FormId);
            if (!access.IsFormVisible(form))
            {
                continue;
            }

            ruleVariablesById.TryGetValue(violation.RuleId, out var referencedVariables);
            var projected = await access.ProjectPreviewViolationAsync(violation, form, referencedVariables, cancellationToken)
                .ConfigureAwait(false);
            if (projected is not null)
            {
                visibleViolations.Add(projected);
            }
        }

        var formsEvaluated = access.HasLoadedStudyForms
            ? access.CountVisibleGovernedForms(item.GoverningProtocolVersions)
            : preview.FormsEvaluated;

        return TypedResults.Ok(new KernelRulePackPreviewResponse(
            NodeRef.For(itemId).ToString(),
            formsEvaluated,
            preview.RulesEvaluated,
            visibleViolations.Count,
            visibleViolations));
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

        var allMatching = new List<ValidationFindingView>();
        var skip = 0;
        var trimmedDataset = string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim();
        var trimmedPackKey = string.IsNullOrWhiteSpace(pack_key) ? null : pack_key.Trim();
        while (true)
        {
            var page = await findings.ListAsync(
                tenant,
                new FindingQuery(resolved.StudyId)
                {
                    Stage = parsedStage,
                    Statuses = parsedStatuses,
                    Severity = parsedSeverity,
                    Dataset = trimmedDataset,
                    PackKey = trimmedPackKey,
                    Skip = skip,
                    Take = FindingQuery.MaxTake,
                },
                cancellationToken).ConfigureAwait(false);
            allMatching.AddRange(page.Findings);
            if (page.Findings.Count == 0 || allMatching.Count >= page.Total)
            {
                break;
            }

            skip += page.Findings.Count;
        }

        if (allMatching.Count == 0)
        {
            return TypedResults.Ok(new KernelValidationFindingPageResponse(0, []));
        }

        var targetFormIds = allMatching
            .Select(TryGetCollectionFormId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var access = await ValidationAccessProjector.CreateForStudyAsync(
            tenant,
            resolved.StudyId,
            targetFormIds,
            allMatching,
            context,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);

        var visibleViews = new List<(ValidationFindingView View, CollectionInstanceRecord? Form)>(allMatching.Count);
        foreach (var view in allMatching)
        {
            var form = TryGetCollectionFormId(view) is { } fid ? access.FindForm(fid) : null;
            if (!await access.IsVisibleFindingAsync(view, form, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            visibleViews.Add((view, form));
        }

        var effectiveTake = limit ?? 100;
        var responses = new List<KernelValidationFindingResponse>(Math.Min(visibleViews.Count, effectiveTake));
        foreach (var (view, form) in visibleViews.Take(effectiveTake))
        {
            var redacted = await access.RedactFindingAsync(view, form, cancellationToken).ConfigureAwait(false);
            var (siteCode, subjectCode) = access.GetCodes(form);
            responses.Add(KernelValidationFindingResponse.From(redacted, siteCode, subjectCode));
        }

        return TypedResults.Ok(new KernelValidationFindingPageResponse(visibleViews.Count, responses));
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> GetFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var view = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return KernelProblems.NotFound(NodeRef.For(findingId));
        }

        var (Access, Form, Problem) = await CheckFindingVisibilityAsync(tenant, view, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (Problem is { } problem)
        {
            return problem;
        }

        var redacted = await Access.RedactFindingAsync(view, Form, cancellationToken).ConfigureAwait(false);
        var (siteCode, subjectCode) = Access.GetCodes(Form);
        return TypedResults.Ok(new KernelValidationFindingDetailResponse(
            KernelValidationFindingResponse.From(redacted, siteCode, subjectCode),
            redacted.Explain()));
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> InvestigateFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadMutationHeaders(context, changeReason) is var (key, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        var (Access, Form, Problem) = await CheckFindingVisibilityAsync(tenant, existing, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (Problem is { } visibilityProblem)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new InvestigateFinding(tenant, findingId, key, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await BuildUpdatedFindingResponseAsync(tenant, findingId, existing, Access, Form, findings, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ClassifyFindingAsync(
        string findingRef,
        KernelClassifyFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
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
                $"'{request?.Classification}' is not a valid FindingClassification (DataIssue, MappingIssue, MetadataIssue, RuleIssue, NotAnIssue).");
        }

        if (ReadMutationHeaders(context, changeReason) is var (key, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        var (Access, Form, Problem) = await CheckFindingVisibilityAsync(tenant, existing, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (Problem is { } visibilityProblem)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new ClassifyFinding(tenant, findingId, classification, key, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await BuildUpdatedFindingResponseAsync(tenant, findingId, existing, Access, Form, findings, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> AcceptFindingAsync(
        string findingRef,
        KernelAcceptFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this finding acceptance.",
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

        if (ReadMutationHeaders(context, changeReason) is var (key, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await EnsureWorkspaceWritableForFindingDecisionAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        var (Access, Form, Problem) = await CheckFindingVisibilityAsync(tenant, existing, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (Problem is { } visibilityProblem)
        {
            return visibilityProblem;
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "accept-finding-attest", key));

        var canAccept = await probe.CanIAsync(tenant, AcceptFindingCapability, NodeRef.For(findingId), cancellationToken).ConfigureAwait(false);
        if (canAccept.Permitted)
        {
            var contentHash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Accepted, rationale);
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Responsibility,
                    new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, contentHash),
                    new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                    KernelLockEndpoints.StepUpMethod,
                    $"{key}:attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(attestResult.Error!);
            }
        }

        var acceptResult = await sender.SendAsync(
            new AcceptFinding(tenant, findingId, rationale, attestationId, key, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!acceptResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(acceptResult.Error!);
        }

        return await BuildUpdatedFindingResponseAsync(tenant, findingId, existing, Access, Form, findings, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ExplainFindingAsync(
        string findingRef,
        KernelExplainFindingRequest? request,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (context.VerifiedSigToken() is not { } sigToken)
        {
            return TypedResults.Problem(
                title: "Electronic signature required",
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this finding explanation.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        var explanationText = (request?.Explanation ?? string.Empty).Trim();
        if (!FindingText.IsValid(explanationText))
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.DecisionTextInvalid);
        }

        if (ReadMutationHeaders(context, changeReason) is var (key, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await EnsureWorkspaceWritableForFindingDecisionAsync(context, tenant, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        var (Access, Form, Problem) = await CheckFindingVisibilityAsync(tenant, existing, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (Problem is { } visibilityProblem)
        {
            return visibilityProblem;
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "explain-finding-attest", key));

        var canExplain = await probe.CanIAsync(tenant, ExplainFindingCapability, NodeRef.For(findingId), cancellationToken).ConfigureAwait(false);
        if (canExplain.Permitted)
        {
            var contentHash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Explained, explanationText);
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, contentHash),
                    new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero),
                    KernelLockEndpoints.StepUpMethod,
                    $"{key}:attest",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return KernelErrorEndpoints.Problem(attestResult.Error!);
            }
        }

        var explainResult = await sender.SendAsync(
            new ExplainFinding(tenant, findingId, explanationText, attestationId, key, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!explainResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(explainResult.Error!);
        }

        return await BuildUpdatedFindingResponseAsync(tenant, findingId, existing, Access, Form, findings, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingResponse>, ProblemHttpResult>> ReopenFindingAsync(
        string findingRef,
        HttpContext context,
        IValidationFindings findings,
        IKernelCommandSender sender,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadMutationHeaders(context, changeReason) is var (key, reason, headerProblem) && headerProblem is not null)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        var (Access, Form, Problem) = await CheckFindingVisibilityAsync(tenant, existing, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (Problem is { } visibilityProblem)
        {
            return visibilityProblem;
        }

        var result = await sender.SendAsync(
            new ReopenFinding(tenant, findingId, key, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await BuildUpdatedFindingResponseAsync(tenant, findingId, existing, Access, Form, findings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static (string Key, string Reason, ProblemHttpResult? Problem) ReadMutationHeaders(
        HttpContext context,
        IKernelChangeReason changeReason)
    {
        var key = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired));
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (string.Empty, string.Empty, KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired));
        }

        return (key, reason, null);
    }

    private static async Task<ProblemHttpResult?> EnsureWorkspaceWritableForFindingDecisionAsync(
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

        if (entitlements.ProductFamilies.Count == 0)
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
        }

        return null;
    }

    private static async Task<(ValidationAccessProjector Access, CollectionInstanceRecord? Form, ProblemHttpResult? Problem)> CheckFindingVisibilityAsync(
        TenantId tenant,
        ValidationFindingView view,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var studyNode = NodeRef.For(view.Study);
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(studyNode) is { } studyMask)
        {
            var emptyAccess = await ValidationAccessProjector.CreateForStudyAsync(
                tenant,
                view.Study,
                formIds: [],
                findings: [],
                context,
                visibility,
                actor,
                cancellationToken).ConfigureAwait(false);
            return (emptyAccess, null, KernelProblems.NotVisible($"You may not see findings for study {studyNode} ({studyMask})."));
        }

        var formId = TryGetCollectionFormId(view);
        var access = await ValidationAccessProjector.CreateForStudyAsync(
            tenant,
            view.Study,
            formId is { } fid ? [fid] : [],
            [view],
            context,
            visibility,
            actor,
            cancellationToken).ConfigureAwait(false);
        var form = formId is { } resolvedFid ? access.FindForm(resolvedFid) : null;
        if (!await access.IsVisibleFindingAsync(view, form, cancellationToken).ConfigureAwait(false))
        {
            return (access, form, KernelProblems.NotVisible($"You may not see validation finding {NodeRef.For(view.Id)}."));
        }

        return (access, form, null);
    }

    private static async Task<Ok<KernelValidationFindingResponse>> BuildUpdatedFindingResponseAsync(
        TenantId tenant,
        ValidationFindingId findingId,
        ValidationFindingView fallback,
        ValidationAccessProjector access,
        CollectionInstanceRecord? form,
        IValidationFindings findings,
        CancellationToken cancellationToken)
    {
        var updated = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false) ?? fallback;
        var redacted = await access.RedactFindingAsync(updated, form, cancellationToken).ConfigureAwait(false);
        var (siteCode, subjectCode) = access.GetCodes(form);
        return TypedResults.Ok(KernelValidationFindingResponse.From(redacted, siteCode, subjectCode));
    }

    private static CollectionInstanceId? TryGetCollectionFormId(ValidationFindingView view) =>
        view.Stage == ValidationStage.Collection
        && view.Target.Kind == FindingTargetKind.DatasetRecord
        && Guid.TryParse(view.Target.Key, out var guid)
        && guid != Guid.Empty
            ? CollectionInstanceId.FromGuid(guid)
            : null;

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
            var existsInDir = directory is not null && await directory.FindStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false) is not null;
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

    private static bool TryParseFinding(string text, out ValidationFindingId findingId)
    {
        findingId = default;
        if (!NodeRef.TryParse(text, out var node) || node.Kind != NodeKinds.ValidationFinding)
        {
            return false;
        }

        findingId = ValidationFindingId.FromGuid(node.Id);
        return !findingId.IsEmpty;
    }

    private static Dictionary<string, HashSet<string>> ReadRuleVariablesById(JsonElement content)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("rules", out var rulesProp)
            || rulesProp.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in rulesProp.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var idProp)
                || idProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                continue;
            }

            var vars = new HashSet<string>(StringComparer.Ordinal);
            if (entry.TryGetProperty("condition", out var condProp))
            {
                CollectConditionVariables(condProp, vars);
            }

            result[idProp.GetString()!] = vars;
        }

        return result;
    }

    private static void CollectConditionVariables(JsonElement element, HashSet<string> variables)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "var", StringComparison.Ordinal) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var name = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            variables.Add(name);
                        }
                    }
                    else
                    {
                        CollectConditionVariables(prop.Value, variables);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectConditionVariables(item, variables);
                }

                break;
        }
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

    private static Guid DeriveGuid(TenantId tenant, string kind, string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-validation:{tenant}:{kind}:{seed}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private sealed class ValidationAccessProjector(
        TenantId tenant,
        StudyId studyId,
        bool hasLoadedStudyForms,
        IReadOnlyList<CollectionInstanceRecord> allStudyForms,
        Dictionary<CollectionInstanceId, CollectionInstanceRecord> formsById,
        Dictionary<EnrollmentId, List<CollectionInstanceRecord>> formsByEnrollment,
        Dictionary<StudySiteId, string?> siteCodes,
        Dictionary<EnrollmentId, string?> subjectCodes,
        Dictionary<ValidationFindingId, List<NodeRef>> perFindingRefs,
        IReadOnlyDictionary<NodeRef, MaskReason> masks,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolVersionSummary>? studyVersions,
        Dictionary<ConceptId, BlindedConceptMetadata> conceptMetadata,
        IConceptRegistry? conceptRegistry,
        IAuthorizationProbe? probe,
        IBlindedTreatmentClassifier classifier)
    {
        private readonly Dictionary<NodeRef, bool> _unblindByScope = [];
        private readonly Dictionary<ConceptRef, BlindedConceptMetadata?> _conceptsByRef = [];

        public bool HasLoadedStudyForms => hasLoadedStudyForms;

        public static async Task<ValidationAccessProjector> CreateForStudyAsync(
            TenantId tenant,
            StudyId studyId,
            IEnumerable<CollectionInstanceId> formIds,
            IReadOnlyList<ValidationFindingView> findings,
            HttpContext context,
            INodeVisibility visibility,
            ICurrentActor actor,
            CancellationToken cancellationToken)
        {
            var collections = context.RequestServices.GetService<ICollectionInstanceRecords>();
            var navigator = context.RequestServices.GetService<IKernelNavigator>();
            var enrollmentDirectory = context.RequestServices.GetService<IEnrollmentDirectory>();
            var protocolReader = context.RequestServices.GetService<IProtocolVersionReader>();
            var conceptRegistry = context.RequestServices.GetService<IConceptRegistry>();
            var probe = context.RequestServices.GetService<IAuthorizationProbe>();
            var classifier = context.RequestServices.GetService<IBlindedTreatmentClassifier>() ?? BlindedTreatmentClassifier.Instance;

            var formsById = new Dictionary<CollectionInstanceId, CollectionInstanceRecord>();
            IReadOnlyList<CollectionInstanceRecord> studyForms = [];
            var hasLoadedStudyForms = false;
            if (collections is not null)
            {
                studyForms = await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
                if (studyForms.Count > 0)
                {
                    hasLoadedStudyForms = true;
                    foreach (var record in studyForms)
                    {
                        formsById[record.CollectionInstanceId] = record;
                    }
                }

                foreach (var fid in formIds.Distinct())
                {
                    if (!formsById.ContainsKey(fid)
                        && await collections.FindAsync(tenant, fid, cancellationToken).ConfigureAwait(false) is { } single)
                    {
                        formsById[fid] = single;
                    }
                }
            }

            var allKnownForms = hasLoadedStudyForms ? studyForms : [.. formsById.Values];
            var formsByEnrollment = allKnownForms
                .GroupBy(f => f.EnrollmentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var siteCodes = new Dictionary<StudySiteId, string?>();
            var subjectCodes = new Dictionary<EnrollmentId, string?>();
            foreach (var siteId in formsById.Values.Select(f => f.StudySiteId).Distinct())
            {
                var summary = navigator is not null
                    ? await navigator.ResolveAsync(tenant, NodeRef.For(siteId), cancellationToken).ConfigureAwait(false)
                    : null;
                siteCodes[siteId] = summary?.Code;
            }

            foreach (var enrollmentId in formsById.Values.Select(f => f.EnrollmentId).Distinct())
            {
                string? code = null;
                if (navigator is not null)
                {
                    var summary = await navigator.ResolveAsync(tenant, NodeRef.For(enrollmentId), cancellationToken).ConfigureAwait(false);
                    code = summary?.Code;
                }

                if (string.IsNullOrWhiteSpace(code) && enrollmentDirectory is not null)
                {
                    var state = await enrollmentDirectory.FindAsync(tenant, enrollmentId, cancellationToken).ConfigureAwait(false);
                    code = state?.SubjectCode.Value;
                }

                subjectCodes[enrollmentId] = code;
            }

            var perFindingRefs = new Dictionary<ValidationFindingId, List<NodeRef>>(findings.Count);
            var nodesToCheck = new List<NodeRef>();
            nodesToCheck.AddRange(formsById.Values.Select(f => NodeRef.For(f.EnrollmentId)));
            nodesToCheck.AddRange(formsById.Values.Select(f => NodeRef.For(f.StudySiteId)));
            foreach (var finding in findings)
            {
                var refs = new List<NodeRef> { NodeRef.For(finding.Id) };
                if (finding.Target.Node is { } targetNode)
                {
                    refs.Add(targetNode);
                }

                if (TryGetCollectionFormId(finding) is { } fid && formsById.TryGetValue(fid, out var targetForm))
                {
                    refs.Add(NodeRef.For(targetForm.EnrollmentId));
                    refs.Add(NodeRef.For(targetForm.StudySiteId));
                    var siblings = formsByEnrollment.GetValueOrDefault(targetForm.EnrollmentId) ?? [targetForm];
                    foreach (var obsId in ResolveReferencedObservationIds(finding, targetForm, siblings))
                    {
                        refs.Add(NodeRef.For(obsId));
                    }
                }

                perFindingRefs[finding.Id] = refs;
                nodesToCheck.AddRange(refs);
            }

            IReadOnlyDictionary<NodeRef, MaskReason> masks = nodesToCheck.Count == 0
                ? new Dictionary<NodeRef, MaskReason>()
                : await KernelReadMasking.MaskAsync(visibility, actor, tenant, [.. nodesToCheck.Distinct()], cancellationToken)
                    .ConfigureAwait(false);

            var designs = new List<ProtocolDesign>();
            IReadOnlyDictionary<ProtocolVersionId, ProtocolVersionSummary>? studyVersions = null;
            if (protocolReader is not null)
            {
                var seenVersions = new HashSet<ProtocolVersionId>();
                foreach (var pvId in formsById.Values.Select(f => f.ProtocolVersionId).Distinct())
                {
                    if (seenVersions.Add(pvId)
                        && await protocolReader.GetDesignAsync(tenant, pvId, cancellationToken).ConfigureAwait(false) is { } design)
                    {
                        designs.Add(design);
                    }
                }

                var summaries = await protocolReader.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
                if (summaries.Count > 0)
                {
                    studyVersions = summaries.ToDictionary(s => s.Id);
                    if (designs.Count == 0)
                    {
                        foreach (var summaryId in summaries.Select(s => s.Id).Where(seenVersions.Add))
                        {
                            if (await protocolReader.GetDesignAsync(tenant, summaryId, cancellationToken).ConfigureAwait(false) is { } design)
                            {
                                designs.Add(design);
                            }
                        }
                    }
                }
            }

            var conceptMetadata = new Dictionary<ConceptId, BlindedConceptMetadata>();
            if (conceptRegistry is not null)
            {
                var conceptRefs = formsById.Values.SelectMany(f => f.Items).Select(i => i.Concept)
                    .Concat(designs.SelectMany(d => d.CollectionSpecs).SelectMany(s => s.Items).Select(i => i.Concept))
                    .Distinct();
                foreach (var cref in conceptRefs)
                {
                    var view = await conceptRegistry.ResolveAsync(tenant, cref, cancellationToken).ConfigureAwait(false)
                        ?? await conceptRegistry.GetCurrentAsync(tenant, cref.ConceptId, cancellationToken).ConfigureAwait(false);
                    if (view is not null)
                    {
                        conceptMetadata[cref.ConceptId] = new BlindedConceptMetadata(view.ExternalCode, view.PreferredLabel, view.Definition);
                    }
                }
            }

            return new ValidationAccessProjector(
                tenant,
                studyId,
                hasLoadedStudyForms,
                allKnownForms,
                formsById,
                formsByEnrollment,
                siteCodes,
                subjectCodes,
                perFindingRefs,
                masks,
                designs,
                studyVersions,
                conceptMetadata,
                conceptRegistry,
                probe,
                classifier);
        }

        public CollectionInstanceRecord? FindForm(CollectionInstanceId formId) =>
            formsById.GetValueOrDefault(formId);

        public (string? SiteCode, string? SubjectCode) GetCodes(CollectionInstanceRecord? form) =>
            form is null
                ? (null, null)
                : (siteCodes.GetValueOrDefault(form.StudySiteId), subjectCodes.GetValueOrDefault(form.EnrollmentId));

        public bool IsFormVisible(CollectionInstanceRecord? form) =>
            form is null
            || (masks.For(NodeRef.For(form.EnrollmentId)) is null
                && masks.For(NodeRef.For(form.StudySiteId)) is null);

        public async Task<bool> IsVisibleFindingAsync(
            ValidationFindingView view,
            CollectionInstanceRecord? form,
            CancellationToken cancellationToken)
        {
            if (perFindingRefs.TryGetValue(view.Id, out var refs))
            {
                if (refs.Any(r => masks.For(r) is not null))
                {
                    return false;
                }
            }
            else if (masks.For(NodeRef.For(view.Id)) is not null || !IsFormVisible(form))
            {
                return false;
            }

            if (probe is null)
            {
                return true;
            }

            BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMetadata.TryGetValue(id, out var meta) ? meta : null;
            var isBlinded = classifier.IsBlindedFinding(view.Target, view.Observed, designs, ResolveConcept)
                || await IsVariableOrTargetBlindedAsync(form, view.Target.Dataset, view.Target.Variable, cancellationToken).ConfigureAwait(false);
            if (!isBlinded)
            {
                foreach (var obs in view.Observed)
                {
                    if (await IsVariableOrTargetBlindedAsync(form, view.Target.Dataset, obs.Variable, cancellationToken).ConfigureAwait(false))
                    {
                        isBlinded = true;
                        break;
                    }
                }
            }

            if (!isBlinded)
            {
                return true;
            }

            var unblindScope = form is not null ? NodeRef.For(form.EnrollmentId) : NodeRef.For(studyId);
            return await CanUnblindScopeAsync(unblindScope, cancellationToken).ConfigureAwait(false);
        }

        public int CountVisibleGovernedForms(IReadOnlyList<ProtocolVersionId>? governingVersions) =>
            allStudyForms.Count(f =>
                IsFormVisible(f)
                && AppliesToInstance(governingVersions, f, studyVersions));

        public async Task<KernelRulePackPreviewViolationResponse?> ProjectPreviewViolationAsync(
            CollectionRulePackPreviewViolation violation,
            CollectionInstanceRecord? form,
            IReadOnlyCollection<string>? referencedVariables,
            CancellationToken cancellationToken)
        {
            var (siteCode, subjectCode) = GetCodes(form);
            var touchesBlinded = await IsVariableOrTargetBlindedAsync(form, violation.Dataset, violation.ItemId, cancellationToken)
                .ConfigureAwait(false);
            if (!touchesBlinded && referencedVariables is { Count: > 0 })
            {
                foreach (var variable in referencedVariables)
                {
                    if (await IsVariableOrTargetBlindedAsync(form, violation.Dataset, variable, cancellationToken).ConfigureAwait(false))
                    {
                        touchesBlinded = true;
                        break;
                    }
                }
            }

            if (touchesBlinded)
            {
                var unblindScope = form is not null ? NodeRef.For(form.EnrollmentId) : NodeRef.For(studyId);
                if (!await CanUnblindScopeAsync(unblindScope, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }
            }

            return KernelRulePackPreviewViolationResponse.From(violation, siteCode, subjectCode);
        }

        public async Task<ValidationFindingView> RedactFindingAsync(
            ValidationFindingView view,
            CollectionInstanceRecord? form,
            CancellationToken cancellationToken)
        {
            var unblindScope = form is not null ? NodeRef.For(form.EnrollmentId) : NodeRef.For(studyId);
            if (await CanUnblindScopeAsync(unblindScope, cancellationToken).ConfigureAwait(false))
            {
                return view;
            }

            var redactedSecrets = new HashSet<string>(StringComparer.Ordinal);
            if (form is not null)
            {
                foreach (var sibling in formsByEnrollment.GetValueOrDefault(form.EnrollmentId, [form]))
                {
                    foreach (var val in sibling.Values)
                    {
                        var concept = await ResolveConceptMetadataAsync(val.Concept, cancellationToken).ConfigureAwait(false);
                        if (classifier.IsBlindedObservation(
                                val.Concept.ConceptId,
                                val.Value,
                                concept,
                                sibling.CollectionSpecId,
                                val.ItemId,
                                designs)
                            && FormatObservationSecret(val.Value) is { Length: > 0 } secret)
                        {
                            redactedSecrets.Add(secret);
                        }
                    }
                }
            }

            if (redactedSecrets.Count == 0)
            {
                return view;
            }

            var message = RedactSecrets(view.Message, redactedSecrets);
            var expected = view.Expected is not null ? RedactSecrets(view.Expected, redactedSecrets) : null;
            var decision = view.Decision is { Text: { } decisionText }
                ? view.Decision with { Text = RedactSecrets(decisionText, redactedSecrets) }
                : view.Decision;

            return view with
            {
                Message = message,
                Expected = expected,
                Decision = decision,
            };
        }

        private async Task<bool> IsVariableOrTargetBlindedAsync(
            CollectionInstanceRecord? form,
            string? defaultSpecId,
            string? rawVariable,
            CancellationToken cancellationToken)
        {
            BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptMetadata.TryGetValue(id, out var m) ? m : null;
            if (classifier.IsBlindedCollectionSpec(defaultSpecId, designs: designs)
                || classifier.IsBlindedVariable(defaultSpecId, rawVariable, designs, ResolveConcept))
            {
                return true;
            }

            var (specId, itemId) = NormalizeSpecAndItem(form, defaultSpecId, rawVariable);
            CollectedValueView? matchedValue = null;
            CollectionItem? matchedItem = null;
            string? matchedFormTitle = null;

            if (form is not null)
            {
                var candidates = formsByEnrollment.GetValueOrDefault(form.EnrollmentId, [form]);
                foreach (var candidate in candidates)
                {
                    if (specId is not null && !string.Equals(candidate.CollectionSpecId, specId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    matchedFormTitle ??= candidate.CollectionTitle;
                    matchedItem ??= FindMatchingItem(candidate.Items, itemId);
                    matchedValue ??= FindMatchingValue(candidate.Values, itemId);
                }
            }

            ConceptRef? conceptRef = matchedValue?.Concept ?? matchedItem?.Concept;
            var conceptMeta = conceptRef is { } cref
                ? await ResolveConceptMetadataAsync(cref, cancellationToken).ConfigureAwait(false)
                : null;

            if ((matchedItem is not null || matchedValue is not null || !string.IsNullOrWhiteSpace(specId))
                && classifier.IsBlindedCollectionItem(specId, matchedFormTitle, matchedItem, matchedValue?.Value, conceptMeta, designs))
            {
                return true;
            }

            return conceptRef is { } resolvedRef
                && classifier.IsBlindedObservation(
                    resolvedRef.ConceptId,
                    matchedValue?.Value,
                    conceptMeta,
                    specId,
                    matchedItem?.Id ?? itemId,
                    designs);
        }

        private (string? SpecId, string? ItemId) NormalizeSpecAndItem(
            CollectionInstanceRecord? form,
            string? defaultSpecId,
            string? rawVariable)
        {
            var effectiveDefaultSpec = !string.IsNullOrWhiteSpace(defaultSpecId) ? defaultSpecId : form?.CollectionSpecId;
            if (string.IsNullOrWhiteSpace(rawVariable))
            {
                return (effectiveDefaultSpec, null);
            }

            var variable = rawVariable.StartsWith("prev.", StringComparison.Ordinal) && rawVariable.Length > "prev.".Length
                ? rawVariable["prev.".Length..]
                : rawVariable;

            var knownSpecs = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(effectiveDefaultSpec))
            {
                knownSpecs.Add(effectiveDefaultSpec);
            }

            if (form is not null)
            {
                foreach (var sibling in formsByEnrollment.GetValueOrDefault(form.EnrollmentId, [form]))
                {
                    knownSpecs.Add(sibling.CollectionSpecId);
                }
            }

            foreach (var design in designs)
            {
                foreach (var spec in design.CollectionSpecs)
                {
                    knownSpecs.Add(spec.Id);
                }
            }

            foreach (var spec in knownSpecs.OrderByDescending(s => s.Length))
            {
                var prefix = $"{spec}.";
                if (variable.StartsWith(prefix, StringComparison.Ordinal) && variable.Length > prefix.Length)
                {
                    return (spec, variable[prefix.Length..]);
                }
            }

            if (!variable.StartsWith("item.", StringComparison.Ordinal))
            {
                var dot = variable.IndexOf('.', StringComparison.Ordinal);
                if (dot > 0 && dot < variable.Length - 1)
                {
                    return (variable[..dot], variable[(dot + 1)..]);
                }
            }

            return (effectiveDefaultSpec, variable);
        }

        private static bool AppliesToInstance(
            IReadOnlyList<ProtocolVersionId>? governingVersions,
            CollectionInstanceRecord instance,
            IReadOnlyDictionary<ProtocolVersionId, ProtocolVersionSummary>? studyVersions) =>
            RulePackGovernance.AppliesToProtocolVersion(governingVersions, instance.ProtocolVersionId, studyVersions);

        private static CollectionItem? FindMatchingItem(IReadOnlyList<CollectionItem> items, string? itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return items.FirstOrDefault(i =>
                string.Equals(i.Id, itemId, StringComparison.Ordinal)
                || i.Id.EndsWith($".{itemId}", StringComparison.Ordinal));
        }

        private static CollectedValueView? FindMatchingValue(IReadOnlyList<CollectedValueView> values, string? itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            return values.FirstOrDefault(v =>
                string.Equals(v.ItemId, itemId, StringComparison.Ordinal)
                || v.ItemId.EndsWith($".{itemId}", StringComparison.Ordinal));
        }

        private async Task<BlindedConceptMetadata?> ResolveConceptMetadataAsync(ConceptRef conceptRef, CancellationToken cancellationToken)
        {
            if (conceptMetadata.TryGetValue(conceptRef.ConceptId, out var preloaded))
            {
                return preloaded;
            }

            if (_conceptsByRef.TryGetValue(conceptRef, out var cached))
            {
                return cached;
            }

            var resolved = conceptRegistry is not null
                ? await conceptRegistry.ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false)
                    ?? await conceptRegistry.GetCurrentAsync(tenant, conceptRef.ConceptId, cancellationToken).ConfigureAwait(false)
                : null;
            BlindedConceptMetadata? meta = resolved is not null
                ? new BlindedConceptMetadata(resolved.ExternalCode, resolved.PreferredLabel, resolved.Definition)
                : null;
            _conceptsByRef[conceptRef] = meta;
            return meta;
        }

        private async Task<bool> CanUnblindScopeAsync(NodeRef scopeNode, CancellationToken cancellationToken)
        {
            if (_unblindByScope.TryGetValue(scopeNode, out var cached))
            {
                return cached;
            }

            if (probe is null)
            {
                _unblindByScope[scopeNode] = false;
                return false;
            }

            var decision = await probe.CanIAsync(tenant, EnrollmentUnblindCapability, scopeNode, cancellationToken)
                .ConfigureAwait(false);
            _unblindByScope[scopeNode] = decision.Permitted;
            return decision.Permitted;
        }

        private static string? FormatObservationSecret(ObservationValue value) =>
            value switch
            {
                MissingValue => null,
                QuantityValue q => q.Value.ToString(CultureInfo.InvariantCulture),
                CodedValue c => c.Code,
                TextValue t => t.Value,
                BooleanValue b => b.Value ? "true" : "false",
                DateTimeValue dt => dt.Value,
                _ => value.ToDisplayString(),
            };

        private static string RedactSecrets(string text, HashSet<string> secrets)
        {
            var result = text;
            foreach (var secret in secrets.Where(s => !string.IsNullOrWhiteSpace(s)).OrderByDescending(s => s.Length))
            {
                result = result.Replace(secret, RedactedBlindedValue, StringComparison.Ordinal);
            }

            return result;
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

            var normalized = variable.StartsWith("prev.", StringComparison.Ordinal) && variable.Length > "prev.".Length
                ? variable["prev.".Length..]
                : variable;

            foreach (var siblingSpecId in siblingForms
                         .Select(f => f.CollectionSpecId)
                         .Distinct(StringComparer.Ordinal)
                         .OrderByDescending(s => s.Length))
            {
                var prefix = $"{siblingSpecId}.";
                if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length)
                {
                    var itemId = normalized[prefix.Length..];
                    foreach (var form in siblingForms.Where(f => string.Equals(f.CollectionSpecId, siblingSpecId, StringComparison.Ordinal)))
                    {
                        foreach (var val in form.Values.Where(v => MatchesFormItemId(v.ItemId, itemId)))
                        {
                            yield return val.ObservationId;
                        }
                    }

                    yield break;
                }
            }

            if (!normalized.StartsWith("item.", StringComparison.Ordinal))
            {
                var dot = normalized.IndexOf('.', StringComparison.Ordinal);
                if (dot > 0 && dot < normalized.Length - 1)
                {
                    var specId = normalized[..dot];
                    var itemId = normalized[(dot + 1)..];
                    foreach (var form in siblingForms.Where(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal)))
                    {
                        foreach (var val in form.Values.Where(v => MatchesFormItemId(v.ItemId, itemId)))
                        {
                            yield return val.ObservationId;
                        }
                    }

                    yield break;
                }
            }

            foreach (var val in targetForm.Values.Where(v => MatchesFormItemId(v.ItemId, normalized)))
            {
                yield return val.ObservationId;
            }

            foreach (var form in siblingForms.Where(f => !string.Equals(f.CollectionSpecId, defaultSpecId, StringComparison.Ordinal)))
            {
                foreach (var val in form.Values.Where(v => MatchesFormItemId(v.ItemId, normalized)))
                {
                    yield return val.ObservationId;
                }
            }
        }

        private static bool MatchesFormItemId(string recordedItemId, string candidateItemId) =>
            string.Equals(recordedItemId, candidateItemId, StringComparison.Ordinal)
            || (recordedItemId.StartsWith("item.", StringComparison.Ordinal)
                && string.Equals(recordedItemId["item.".Length..], candidateItemId, StringComparison.Ordinal));
    }
}
