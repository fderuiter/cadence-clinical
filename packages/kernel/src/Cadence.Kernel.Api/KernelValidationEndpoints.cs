using System;
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
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
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

    private static readonly Capability ApproveLibraryItemCapability = new("library_item.approve", VerbClass.Approve);
    private static readonly Capability RegisterStandardCapability = new("standard.register", VerbClass.Execute);
    private static readonly Capability ApproveStandardCapability = new("standard.approve", VerbClass.Approve);
    private static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    /// <summary>The canonical concrete action path for approving a library item reference.</summary>
    public static string ApproveLibraryItemAction(string itemRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/library-items/{itemRef}/approve";

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

        return TypedResults.Ok(new KernelRulePackPreviewResponse(
            NodeRef.For(itemId).ToString(),
            preview.FormsEvaluated,
            preview.RulesEvaluated,
            preview.Violations.Count,
            [.. preview.Violations.Select(KernelRulePackPreviewViolationResponse.From)]));
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

        return TypedResults.Ok(new KernelValidationFindingPageResponse(
            visibleFindings.Count,
            [.. visibleFindings.Take(take).Select(KernelValidationFindingResponse.From)]));
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

        return TypedResults.Ok(new KernelValidationFindingDetailResponse(
            KernelValidationFindingResponse.From(view),
            view.Explain()));
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
