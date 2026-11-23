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
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
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
/// Standards Registry, Metadata Registry library-item governance, in-memory rule-pack preview, validation run recording,
/// and validation finding triage routes (spec §6.3, §10.3, K-STD-1, K-RULE-3, K-FIND-1, K-FIND-2). Every mutating action
/// dispatches through <see cref="IKernelCommandSender"/> with an <c>Idempotency-Key</c> and a signed
/// <c>X-Change-Reason</c>.
/// </summary>
public static class KernelValidationEndpoints
{
    /// <summary>The semantic action a standard-version approval step-up token must carry.</summary>
    public const string ApproveStandardVersionSemanticAction = "kernel.standard.approve";

    /// <summary>The semantic action a standard-version deprecation step-up token must carry.</summary>
    public const string DeprecateStandardVersionSemanticAction = "kernel.standard.deprecate";

    /// <summary>The semantic action a library-item approval step-up token must carry.</summary>
    public const string ApproveLibraryItemSemanticAction = "kernel.library_item.approve";

    /// <summary>The semantic action a library-item deprecation step-up token must carry.</summary>
    public const string DeprecateLibraryItemSemanticAction = "kernel.library_item.deprecate";

    /// <summary>The semantic action a validation-finding acceptance step-up token must carry.</summary>
    public const string AcceptFindingSemanticAction = "kernel.validation_finding.accept";

    /// <summary>The semantic action a validation-finding explanation step-up token must carry.</summary>
    public const string ExplainFindingSemanticAction = "kernel.validation_finding.explain";

    private static readonly Capability RegisterStandardCapability = new("standard.register", VerbClass.Execute);
    private static readonly Capability ApproveStandardCapability = new("standard.approve", VerbClass.Approve);
    private static readonly Capability DeprecateStandardCapability = new("standard.deprecate", VerbClass.Approve);
    private static readonly Capability ApproveLibraryItemCapability = new("library_item.approve", VerbClass.Approve);
    private static readonly Capability DeprecateLibraryItemCapability = new("library_item.deprecate", VerbClass.Approve);
    private static readonly Capability RecordValidationRunCapability = new("validation_run.record", VerbClass.Execute);
    private static readonly Capability AcceptFindingCapability = new("validation_finding.accept", VerbClass.Approve);
    private static readonly Capability ExplainFindingCapability = new("validation_finding.explain", VerbClass.Approve);

    /// <summary>The canonical concrete action path for approving a standard version reference.</summary>
    public static string ApproveStandardVersionAction(string standardRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/standards/{standardRef}/approve";

    /// <summary>The canonical concrete action path for deprecating a standard version reference.</summary>
    public static string DeprecateStandardVersionAction(string standardRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/standards/{standardRef}/deprecate";

    /// <summary>The canonical concrete action path for approving a library item reference.</summary>
    public static string ApproveLibraryItemAction(string itemRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/library-items/{itemRef}/approve";

    /// <summary>The canonical concrete action path for deprecating a library item reference.</summary>
    public static string DeprecateLibraryItemAction(string itemRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/library-items/{itemRef}/deprecate";

    /// <summary>The canonical concrete action path for accepting a validation finding reference.</summary>
    public static string AcceptFindingAction(string findingRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/validation/findings/{findingRef}/accept";

    /// <summary>The canonical concrete action path for explaining a validation finding reference.</summary>
    public static string ExplainFindingAction(string findingRef) =>
        $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/validation/findings/{findingRef}/explain";

    /// <summary>Maps standards registry, library governance, validation run, and finding triage routes on the kernel group.</summary>
    public static RouteGroupBuilder MapKernelValidation(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel validation");

        group.MapGet("/standards", ListStandardsAsync)
            .WithName("KernelListStandards")
            .WithSummary("Lists registered standard versions in the tenant alongside bundled public CDISC standard seeds.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/standards", RegisterStandardVersionAsync)
            .WithName("KernelRegisterStandardVersion")
            .WithSummary("Registers a data standard version in Draft status from an explicit package or bundled seed (standard.register).")
            .Produces<KernelStandardVersionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RegisterStandardVersion>();

        group.MapPost("/standards/{standardRef}/approve", ApproveStandardVersionAsync)
            .WithName("KernelApproveStandardVersion")
            .WithSummary("Approves a draft standard version with a step-up Approved attestation (standard.approve).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(ApproveStandardVersion))!)])
            .RequireElectronicSignature(
                ApproveStandardVersionSemanticAction,
                ctx => ApproveStandardVersionAction(ctx.Request.RouteValues["standardRef"]?.ToString() ?? string.Empty));

        group.MapPost("/standards/{standardRef}/deprecate", DeprecateStandardVersionAsync)
            .WithName("KernelDeprecateStandardVersion")
            .WithSummary("Deprecates a standard version with a step-up Responsibility attestation (standard.deprecate).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(DeprecateStandardVersion))!)])
            .RequireElectronicSignature(
                DeprecateStandardVersionSemanticAction,
                ctx => DeprecateStandardVersionAction(ctx.Request.RouteValues["standardRef"]?.ToString() ?? string.Empty));

        group.MapGet("/library-items", ListLibraryItemsAsync)
            .WithName("KernelListLibraryItems")
            .WithSummary("Lists Metadata Registry library items in the tenant, optionally filtered by kind or annotated with study pins.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/library-items", DraftLibraryItemAsync)
            .WithName("KernelDraftLibraryItem")
            .WithSummary("Drafts the next version of a Metadata Registry library item across all five kinds (library_item.draft).")
            .Produces<KernelLibraryItemResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(RegisterStandardVersion))!,
                RaisesKernelErrorsAttribute.Of(typeof(DraftLibraryItem))!)]);

        group.MapPost("/library-items/{itemRef}/approve", ApproveLibraryItemAsync)
            .WithName("KernelApproveLibraryItem")
            .WithSummary("Approves a draft library item version with an Approved attestation (library_item.approve).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(ApproveLibraryItem))!)])
            .RequireElectronicSignature(
                ApproveLibraryItemSemanticAction,
                ctx => ApproveLibraryItemAction(ctx.Request.RouteValues["itemRef"]?.ToString() ?? string.Empty));

        group.MapPost("/library-items/{itemRef}/deprecate", DeprecateLibraryItemAsync)
            .WithName("KernelDeprecateLibraryItem")
            .WithSummary("Deprecates a library item version with a Responsibility attestation (library_item.deprecate).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(DeprecateLibraryItem))!)])
            .RequireElectronicSignature(
                DeprecateLibraryItemSemanticAction,
                ctx => DeprecateLibraryItemAction(ctx.Request.RouteValues["itemRef"]?.ToString() ?? string.Empty));

        group.MapGet("/studies/{studyRef}/standards", GetStudyStandardsAsync)
            .WithName("KernelGetStudyStandards")
            .WithSummary("Returns the declared standards, controlled terminology bindings, and pinned library items of a study.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

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

        group.MapPut("/studies/{studyRef}/standards", DeclareStudyRulePacksAsync)
            .WithName("KernelDeclareStudyRulePacks")
            .WithSummary("Declares or updates the approved standards, terminology bindings, and library items pinned on a study (study.declare_standards).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<DeclareStudyStandards>();

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

        group.MapPost("/studies/{studyRef}/validation/runs", RunStudyValidationAsync)
            .WithName("KernelRunStudyValidation")
            .WithSummary("Evaluates study collection rules on demand or records an explicit validation run (validation_run.record).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RecordValidationRun>();

        group.MapPost("/validation/findings/{findingRef}/investigate", InvestigateFindingAsync)
            .WithName("KernelInvestigateFinding")
            .WithSummary("Marks an open validation finding as Investigating (validation_finding.investigate).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<InvestigateFinding>();

        group.MapPost("/validation/findings/{findingRef}/classify", ClassifyFindingAsync)
            .WithName("KernelClassifyFinding")
            .WithSummary("Classifies a detected validation finding (validation_finding.classify).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ClassifyFinding>();

        group.MapPost("/validation/findings/{findingRef}/accept", AcceptFindingAsync)
            .WithName("KernelAcceptFinding")
            .WithSummary("Accepts an open or investigating validation finding with a step-up Responsibility attestation (validation_finding.accept).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(AcceptFinding))!)])
            .RequireElectronicSignature(
                AcceptFindingSemanticAction,
                ctx => AcceptFindingAction(ctx.Request.RouteValues["findingRef"]?.ToString() ?? string.Empty));

        group.MapPost("/validation/findings/{findingRef}/explain", ExplainFindingAsync)
            .WithName("KernelExplainFinding")
            .WithSummary("Explains an open or investigating validation finding for the reviewer guide with a step-up Approved attestation (validation_finding.explain).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(Attest))!,
                RaisesKernelErrorsAttribute.Of(typeof(ExplainFinding))!)])
            .RequireElectronicSignature(
                ExplainFindingSemanticAction,
                ctx => ExplainFindingAction(ctx.Request.RouteValues["findingRef"]?.ToString() ?? string.Empty));

        group.MapPost("/validation/findings/{findingRef}/reopen", ReopenFindingAsync)
            .WithName("KernelReopenFinding")
            .WithSummary("Reopens an investigating, accepted, or explained validation finding (validation_finding.reopen).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ReopenFinding>();

        return kernel;
    }

    internal static async Task<Results<Ok<KernelStandardsCatalogResponse>, ProblemHttpResult>> ListStandardsAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var registry = context.RequestServices.GetService<IStandardsRegistry>();
        var registered = new List<KernelStandardVersionResponse>();
        if (registry is not null)
        {
            var summaries = await registry.ListStandardVersionsAsync(tenant, cancellationToken).ConfigureAwait(false);
            foreach (var summary in summaries)
            {
                var package = await registry.GetStandardPackageAsync(tenant, summary.Id, cancellationToken).ConfigureAwait(false);
                registered.Add(KernelStandardVersionResponse.From(summary, package));
            }
        }

        var seeds = Cadence.Standards.StandardSeeds.LoadAll()
            .Select(s => new KernelBundledStandardSeedResponse(
                s.File,
                s.Package.StandardCode,
                s.Package.Version,
                s.Package.Name,
                s.Package.Publisher,
                [.. s.Package.Datasets.Select(KernelStandardDatasetSummaryResponse.From)]))
            .ToList();

        return TypedResults.Ok(new KernelStandardsCatalogResponse(registered, seeds));
    }

    internal static async Task<Results<Created<KernelStandardVersionResponse>, ProblemHttpResult>> RegisterStandardVersionAsync(
        KernelRegisterStandardVersionRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null || (string.IsNullOrWhiteSpace(request.SeedFile) && request.Package is null))
        {
            return KernelProblems.BadRequest("Invalid request", "Provide seed_file or package in the request body.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        StandardPackage package;
        if (!string.IsNullOrWhiteSpace(request.SeedFile))
        {
            var seedName = request.SeedFile.Trim();
            if (!Cadence.Standards.StandardSeeds.Names.Contains(seedName, StringComparer.Ordinal))
            {
                return KernelProblems.BadRequest(
                    "Unknown seed file",
                    $"No bundled standard seed named '{seedName}'. Available: {string.Join(", ", Cadence.Standards.StandardSeeds.Names)}.");
            }

            package = Cadence.Standards.StandardSeeds.Load(seedName).Package;
        }
        else
        {
            package = request.Package!;
        }

        var tenant = KernelRequestTenant.Get(context);
        var standardId = request.StandardVersionId is { } given && given != Guid.Empty
            ? StandardVersionId.FromGuid(given)
            : StandardVersionId.FromGuid(DeriveGuid(tenant, "standard-version", idempotencyKey));

        var result = await sender.SendAsync(
            new RegisterStandardVersion(tenant, standardId, package, request.ExpectedContentHash, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var summary = await registry.FindStandardVersionAsync(tenant, result.Value, cancellationToken).ConfigureAwait(false)
            ?? new StandardVersionSummary(
                result.Value,
                package.StandardCode,
                package.Version,
                package.Name,
                package.Publisher,
                package.Source,
                package.EffectiveDate,
                string.Empty,
                GovernanceStatus.Draft,
                null,
                null,
                package.ControlledTerminology);

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/standards/{NodeRef.For(result.Value)}",
            KernelStandardVersionResponse.From(summary, package));
    }

    internal static async Task<Results<Ok<KernelStandardVersionResponse>, ProblemHttpResult>> ApproveStandardVersionAsync(
        string standardRef,
        KernelApproveStandardVersionRequest? request,
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
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this standard version approval.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseStandardVersion(standardRef, out var standardId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{standardRef}' is not a standard_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckTenantEntitlementAsync(tenant, context, ProductFamily.Blueprint, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var existing = await registry.FindStandardVersionAsync(tenant, standardId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(StandardsErrors.StandardVersionUnknown);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "approve-standard-attest", idempotencyKey));

        var canApprove = await probe.CanIAsync(tenant, ApproveStandardCapability, NodeRef.For(standardId), cancellationToken).ConfigureAwait(false);
        if (canApprove.Permitted)
        {
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.StandardVersion, standardId.Value, existing.ContentHash),
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

        var result = await sender.SendAsync(
            new ApproveStandardVersion(tenant, standardId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await registry.FindStandardVersionAsync(tenant, standardId, cancellationToken).ConfigureAwait(false) ?? existing;
        var package = await registry.GetStandardPackageAsync(tenant, standardId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelStandardVersionResponse.From(updated, package));
    }

    internal static async Task<Results<Ok<KernelStandardVersionResponse>, ProblemHttpResult>> DeprecateStandardVersionAsync(
        string standardRef,
        KernelDeprecateStandardVersionRequest? request,
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
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this standard version deprecation.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseStandardVersion(standardRef, out var standardId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{standardRef}' is not a standard_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckTenantEntitlementAsync(tenant, context, ProductFamily.Blueprint, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var existing = await registry.FindStandardVersionAsync(tenant, standardId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(StandardsErrors.StandardVersionUnknown);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "deprecate-standard-attest", idempotencyKey));

        var canDeprecate = await probe.CanIAsync(tenant, DeprecateStandardCapability, NodeRef.For(standardId), cancellationToken).ConfigureAwait(false);
        if (canDeprecate.Permitted)
        {
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Responsibility,
                    new AttestationTarget(NodeKinds.StandardVersion, standardId.Value, existing.ContentHash),
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

        var result = await sender.SendAsync(
            new DeprecateStandardVersion(tenant, standardId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await registry.FindStandardVersionAsync(tenant, standardId, cancellationToken).ConfigureAwait(false) ?? existing;
        var package = await registry.GetStandardPackageAsync(tenant, standardId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelStandardVersionResponse.From(updated, package));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelLibraryItemResponse>>, ProblemHttpResult>> ListLibraryItemsAsync(
        string? kind,
        string? study_ref,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        LibraryItemKind? parsedKind = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<LibraryItemKind>(kind.Trim(), ignoreCase: true, out var k) || !Enum.IsDefined(k))
            {
                return KernelProblems.BadRequest("Invalid kind", $"'{kind}' is not a valid library item kind.");
            }

            parsedKind = k;
        }

        var tenant = KernelRequestTenant.Get(context);
        var registry = context.RequestServices.GetService<IStandardsRegistry>();
        if (registry is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelLibraryItemResponse>>([]);
        }

        var pinnedIds = new HashSet<LibraryItemId>();
        if (!string.IsNullOrWhiteSpace(study_ref))
        {
            var resolved = await ResolveVisibleStudyAsync(tenant, study_ref.Trim(), context, visibility, actor, cancellationToken).ConfigureAwait(false);
            if (resolved.Problem is { } problem)
            {
                return problem;
            }

            var profile = await registry.FindStudyStandardsAsync(tenant, resolved.StudyId, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
            {
                pinnedIds = [.. profile.LibraryPins];
            }
        }

        var items = await registry.ListLibraryItemsAsync(tenant, parsedKind, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelLibraryItemResponse>>(
            [.. items.Select(i => KernelLibraryItemResponse.From(i, pinnedIds.Contains(i.Id)))]);
    }

    internal static async Task<Results<Created<KernelLibraryItemResponse>, ProblemHttpResult>> DraftLibraryItemAsync(
        KernelDraftLibraryItemRequest? request,
        HttpContext context,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Kind))
        {
            return KernelProblems.BadRequest("Invalid request", "Provide kind, key, title, and content in the request body.");
        }

        if (!Enum.TryParse<LibraryItemKind>(request.Kind.Trim(), ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return KernelErrorEndpoints.Problem(StandardsErrors.LibraryItemInvalid);
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var standardVersions = new List<StandardVersionId>();
        if (request.StandardVersionRefs is not null)
        {
            foreach (var rawStd in request.StandardVersionRefs)
            {
                if (!TryParseStandardVersion(rawStd, out var stdId))
                {
                    return KernelProblems.BadRequest("Invalid NodeRef", $"'{rawStd}' is not a standard_version NodeRef.");
                }

                standardVersions.Add(stdId);
            }
        }

        if (standardVersions.Count == 0 && !string.IsNullOrWhiteSpace(request.SeedStandardFile))
        {
            var seedFile = request.SeedStandardFile.Trim();
            if (!Cadence.Standards.StandardSeeds.Names.Contains(seedFile, StringComparer.Ordinal))
            {
                return KernelProblems.BadRequest("Unknown seed file", $"No bundled standard seed named '{seedFile}'.");
            }

            var seedPackage = Cadence.Standards.StandardSeeds.Load(seedFile).Package;
            var existingStd = await registry.FindStandardVersionAsync(tenant, seedPackage.StandardCode, seedPackage.Version, cancellationToken).ConfigureAwait(false);
            if (existingStd is null)
            {
                var canRegister = await probe.CanIAsync(tenant, RegisterStandardCapability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);
                if (!canRegister.Permitted)
                {
                    return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
                }

                var stdId = StandardVersionId.FromGuid(DeriveGuid(tenant, "seed-standard", $"{seedPackage.StandardCode}:{seedPackage.Version}"));
                var regResult = await sender.SendAsync(
                    new RegisterStandardVersion(tenant, stdId, seedPackage, null, $"{idempotencyKey}:seed-std", reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!regResult.IsSuccess && regResult.Error?.Code != KernelErrors.AlreadyExists.Code)
                {
                    return KernelErrorEndpoints.Problem(regResult.Error!);
                }

                existingStd = await registry.FindStandardVersionAsync(tenant, seedPackage.StandardCode, seedPackage.Version, cancellationToken).ConfigureAwait(false);
            }

            if (existingStd is not null)
            {
                standardVersions.Add(existingStd.Id);
            }
        }

        var itemId = request.LibraryItemId is { } given && given != Guid.Empty
            ? LibraryItemId.FromGuid(given)
            : LibraryItemId.FromGuid(DeriveGuid(tenant, "library-item", idempotencyKey));

        var result = await sender.SendAsync(
            new DraftLibraryItem(
                tenant,
                itemId,
                kind,
                request.Key ?? string.Empty,
                request.Title ?? string.Empty,
                request.Content,
                standardVersions,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var drafted = result.Value!;
        var view = await registry.FindLibraryItemAsync(tenant, drafted.Id, cancellationToken).ConfigureAwait(false)
            ?? new LibraryItemView(
                drafted.Id,
                drafted.Kind,
                drafted.Key,
                drafted.Version,
                request.Title ?? string.Empty,
                request.Content,
                drafted.ContentHash,
                GovernanceStatus.Draft,
                standardVersions,
                null,
                null);

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/library-items/{NodeRef.For(drafted.Id)}",
            KernelLibraryItemResponse.From(view, pinned: false));
    }

    internal static async Task<Results<Ok<KernelLibraryItemResponse>, ProblemHttpResult>> DeprecateLibraryItemAsync(
        string itemRef,
        KernelDeprecateLibraryItemRequest? request,
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
                detail: "Supply a fresh, single-use X-Sig-Token step-up header bound to this library item deprecation.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!TryParseLibraryItem(itemRef, out var itemId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{itemRef}' is not a library_item NodeRef (library_item:<uuid>).");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckTenantEntitlementAsync(tenant, context, ProductFamily.Blueprint, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var existing = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(StandardsErrors.LibraryItemUnknown);
        }

        var attestationId = request?.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "deprecate-library-item-attest", idempotencyKey));

        var canDeprecate = await probe.CanIAsync(tenant, DeprecateLibraryItemCapability, NodeRef.For(itemId), cancellationToken).ConfigureAwait(false);
        if (canDeprecate.Permitted)
        {
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Responsibility,
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

        var result = await sender.SendAsync(
            new DeprecateLibraryItem(tenant, itemId, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var updated = await registry.FindLibraryItemAsync(tenant, itemId, cancellationToken).ConfigureAwait(false) ?? existing;
        return TypedResults.Ok(KernelLibraryItemResponse.From(updated, pinned: false));
    }

    internal static async Task<Results<Ok<KernelStudyStandardsProfileResponse>, ProblemHttpResult>> GetStudyStandardsAsync(
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
            return TypedResults.Ok(new KernelStudyStandardsProfileResponse(NodeRef.For(studyId).ToString(), null, 0, [], [], []));
        }

        var profile = await registry.FindStudyStandardsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return TypedResults.Ok(new KernelStudyStandardsProfileResponse(NodeRef.For(studyId).ToString(), null, 0, [], [], []));
        }

        var standards = new List<KernelStandardVersionResponse>(profile.StandardVersions.Count);
        foreach (var stdId in profile.StandardVersions)
        {
            var std = await registry.FindStandardVersionAsync(tenant, stdId, cancellationToken).ConfigureAwait(false);
            if (std is not null)
            {
                var pkg = await registry.GetStandardPackageAsync(tenant, stdId, cancellationToken).ConfigureAwait(false);
                standards.Add(KernelStandardVersionResponse.From(std, pkg));
            }
        }

        var pins = new List<KernelLibraryItemResponse>(profile.LibraryPins.Count);
        foreach (var pinId in profile.LibraryPins)
        {
            var item = await registry.FindLibraryItemAsync(tenant, pinId, cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                pins.Add(KernelLibraryItemResponse.From(item, pinned: true));
            }
        }

        return TypedResults.Ok(new KernelStudyStandardsProfileResponse(
            NodeRef.For(studyId).ToString(),
            NodeRef.For(profile.Id).ToString(),
            profile.Revision,
            standards,
            [.. profile.ControlledTerminology.Select(KernelCodeSystemVersionRefDto.From)],
            pins));
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
            return TypedResults.Ok(new KernelStudyRulePacksResponse(NodeRef.For(studyId).ToString(), null, [], [], [], []));
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
            packs,
            profile is not null ? [.. profile.StandardVersions.Select(id => NodeRef.For(id).ToString())] : [],
            profile is not null ? [.. profile.LibraryPins.Select(id => NodeRef.For(id).ToString())] : []));
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

        var preview = await validation.PreviewRulePackAsync(
            tenant,
            resolved.StudyId,
            itemId,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
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

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckTenantEntitlementAsync(tenant, context, ProductFamily.Blueprint, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
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

        var authenticatedAt = new DateTimeOffset(sigToken.IssuedAt, TimeSpan.Zero);
        if (existing.StandardVersions.Count > 0)
        {
            await ApproveDraftStandardVersionsAsync(
                tenant,
                existing.StandardVersions,
                registry,
                sender,
                probe,
                authenticatedAt,
                idempotencyKey,
                reason,
                cancellationToken).ConfigureAwait(false);
        }
        else if (existing.Kind == LibraryItemKind.RulePack)
        {
            await EnsureBaselineStandardApprovedAsync(
                tenant,
                registry,
                sender,
                probe,
                authenticatedAt,
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
        if (request is null
            || (request.RulePackRefs is null
                && request.MappingSpecRefs is null
                && request.LibraryPinRefs is null
                && request.StandardVersionRefs is null
                && request.ControlledTerminology is null))
        {
            return KernelProblems.BadRequest(
                "Invalid standards declaration",
                "Provide rule_pack_refs, mapping_spec_refs, library_pin_refs, standard_version_refs, or controlled_terminology.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
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

        List<LibraryItemId> mergedPins;
        if (request.LibraryPinRefs is not null)
        {
            if (!TryParseLibraryItemList(request.LibraryPinRefs, out mergedPins, out var pinProblem))
            {
                return pinProblem!;
            }
        }
        else
        {
            var replacedKinds = new HashSet<LibraryItemKind>();
            var additions = new List<LibraryItemId>();
            if (request.RulePackRefs is not null)
            {
                replacedKinds.Add(LibraryItemKind.RulePack);
                if (!TryParseLibraryItemList(request.RulePackRefs, out var rulePacks, out var rpProblem))
                {
                    return rpProblem!;
                }

                additions.AddRange(rulePacks);
            }

            if (request.MappingSpecRefs is not null)
            {
                replacedKinds.Add(LibraryItemKind.MappingSpecification);
                if (!TryParseLibraryItemList(request.MappingSpecRefs, out var mappingSpecs, out var msProblem))
                {
                    return msProblem!;
                }

                additions.AddRange(mappingSpecs);
            }

            var preservedPins = new List<LibraryItemId>();
            if (currentProfile is not null)
            {
                foreach (var pinId in currentProfile.LibraryPins)
                {
                    var pinnedItem = await registry.FindLibraryItemAsync(tenant, pinId, cancellationToken).ConfigureAwait(false);
                    if (pinnedItem is not null && !replacedKinds.Contains(pinnedItem.Kind))
                    {
                        preservedPins.Add(pinId);
                    }
                }
            }

            mergedPins = [.. preservedPins.Concat(additions).Distinct()];
        }

        List<StandardVersionId> standardVersions;
        if (request.StandardVersionRefs is not null)
        {
            standardVersions = new List<StandardVersionId>(request.StandardVersionRefs.Count);
            foreach (var rawStd in request.StandardVersionRefs)
            {
                if (!TryParseStandardVersion(rawStd, out var stdId))
                {
                    return KernelProblems.BadRequest("Invalid NodeRef", $"'{rawStd}' is not a standard_version NodeRef.");
                }

                standardVersions.Add(stdId);
            }
        }
        else
        {
            standardVersions = [.. (currentProfile?.StandardVersions ?? [])];
            foreach (var pinId in mergedPins)
            {
                var pinnedItem = await registry.FindLibraryItemAsync(tenant, pinId, cancellationToken).ConfigureAwait(false);
                if (pinnedItem is not null)
                {
                    foreach (var stdId in pinnedItem.StandardVersions)
                    {
                        if (!standardVersions.Contains(stdId))
                        {
                            standardVersions.Add(stdId);
                        }
                    }
                }
            }

            if (standardVersions.Count == 0)
            {
                var allStandards = await registry.ListStandardVersionsAsync(tenant, cancellationToken).ConfigureAwait(false);
                standardVersions = [.. allStandards
                    .Where(s => s.Status == GovernanceStatus.Approved)
                    .GroupBy(s => s.StandardCode, StringComparer.Ordinal)
                    .Select(g => g.Last().Id)];
            }
        }

        IReadOnlyList<CodeSystemVersionRef> controlledTerminology = request.ControlledTerminology is not null
            ? [.. request.ControlledTerminology.Select(ct => ct.ToDomain())]
            : currentProfile?.ControlledTerminology ?? [];

        var declareResult = await sender.SendAsync(
            new DeclareStudyStandards(
                tenant,
                studyId,
                profileId,
                standardVersions,
                controlledTerminology,
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

    internal static async Task<Results<Ok<KernelValidationRunResponse>, ProblemHttpResult>> RunStudyValidationAsync(
        string studyRef,
        KernelRunStudyValidationRequest? request,
        HttpContext context,
        IAuthorizationProbe probe,
        IKernelCommandSender sender,
        IValidationFindings findings,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var (studyId, studyProblem) = await ResolveStudyForCommandAsync(tenant, studyRef, context, ValidationErrors.StudyUnknown, cancellationToken).ConfigureAwait(false);
        if (studyProblem is not null)
        {
            return studyProblem;
        }

        var now = clock.UtcNow;
        var asOf = request?.AsOf ?? DateOnly.FromDateTime(now.UtcDateTime);

        if (request?.RulePackRefs is null && request?.Detections is null)
        {
            var canRecord = await probe.CanIAsync(tenant, RecordValidationRunCapability, NodeRef.For(studyId), cancellationToken).ConfigureAwait(false);
            if (!canRecord.Permitted)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
            }

            var validation = context.RequestServices.GetService<ICollectionValidationService>();
            if (validation is null)
            {
                return TypedResults.Ok(new KernelValidationRunResponse(null, 0, 0, 0, []));
            }

            var evalResult = await validation.ValidateStudyCollectionsAsync(
                tenant,
                studyId,
                asOf,
                now,
                idempotencyKey,
                reason,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!evalResult.IsSuccess)
            {
                return KernelErrorEndpoints.Problem(evalResult.Error!);
            }

            var evaluation = evalResult.Value!;
            var outcome = evaluation.RunOutcome;
            return TypedResults.Ok(new KernelValidationRunResponse(
                outcome is not null ? NodeRef.For(outcome.RunId).ToString() : null,
                outcome?.Detected ?? 0,
                outcome?.New ?? 0,
                outcome?.Resolved ?? 0,
                [.. evaluation.Findings.Select(KernelValidationFindingResponse.From)]));
        }

        ValidationStage stage = ValidationStage.Collection;
        if (!string.IsNullOrWhiteSpace(request.Stage)
            && (!Enum.TryParse<ValidationStage>(request.Stage.Trim(), ignoreCase: true, out stage) || !Enum.IsDefined(stage)))
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.RunInvalid);
        }

        if (!TryParseLibraryItemList(request.RulePackRefs ?? [], out var rulePacks, out var rpProblem))
        {
            return rpProblem!;
        }

        var inputs = new List<NodeRef>();
        if (request.InputRefs is not null)
        {
            foreach (var rawInput in request.InputRefs)
            {
                if (!NodeRef.TryParse(rawInput.Trim(), out var inputNode))
                {
                    return KernelProblems.BadRequest("Invalid NodeRef", $"'{rawInput}' is not a valid NodeRef.");
                }

                inputs.Add(inputNode);
            }
        }

        var detections = new List<ValidationDetection>();
        if (request.Detections is not null)
        {
            foreach (var det in request.Detections)
            {
                if (!Enum.TryParse<ValidationSeverity>(det.Severity, ignoreCase: true, out var sev) || !Enum.IsDefined(sev)
                    || !Enum.TryParse<FindingTargetKind>(det.TargetKind, ignoreCase: true, out var targetKind) || !Enum.IsDefined(targetKind))
                {
                    return KernelErrorEndpoints.Problem(ValidationErrors.RunInvalid);
                }

                detections.Add(new ValidationDetection(
                    det.PackKey ?? string.Empty,
                    det.RuleId ?? string.Empty,
                    det.RuleVersion,
                    sev,
                    new FindingTarget(targetKind, det.Dataset ?? string.Empty, det.RecordKey ?? string.Empty, det.Variable),
                    det.Message ?? string.Empty,
                    det.Expected,
                    [.. (det.Observed ?? []).Select(o => new ObservedVariable(o.Variable ?? string.Empty, o.Value))]));
            }
        }

        var runId = request.RunId is { } givenRun && givenRun != Guid.Empty
            ? ValidationRunId.FromGuid(givenRun)
            : ValidationRunId.FromGuid(DeriveGuid(tenant, "validation-run", idempotencyKey));

        var cmdResult = await sender.SendAsync(
            new RecordValidationRun(
                tenant,
                runId,
                studyId,
                stage,
                rulePacks,
                asOf,
                request.Standard,
                request.StandardVersion,
                request.Agency,
                inputs,
                request.Datasets ?? [],
                now,
                now,
                detections,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!cmdResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(cmdResult.Error!);
        }

        var page = await findings.ListAsync(
            tenant,
            new FindingQuery(studyId) { Stage = stage, Take = 100 },
            cancellationToken).ConfigureAwait(false);

        var runOutcome = cmdResult.Value!;
        return TypedResults.Ok(new KernelValidationRunResponse(
            NodeRef.For(runOutcome.RunId).ToString(),
            runOutcome.Detected,
            runOutcome.New,
            runOutcome.Resolved,
            [.. page.Findings.Select(KernelValidationFindingResponse.From)]));
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> InvestigateFindingAsync(
        string findingRef,
        HttpContext context,
        IKernelCommandSender sender,
        IValidationFindings findings,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new InvestigateFinding(tenant, findingId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await LoadFindingDetailAsync(tenant, findingId, findings, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> ClassifyFindingAsync(
        string findingRef,
        KernelClassifyFindingRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IValidationFindings findings,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Classification))
        {
            return KernelProblems.BadRequest("Invalid request", "Provide classification in the request body.");
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (!Enum.TryParse<FindingClassification>(request.Classification.Trim(), ignoreCase: true, out var classification)
            || !Enum.IsDefined(classification))
        {
            return KernelProblems.BadRequest("Invalid classification", $"'{request.Classification}' is not a valid finding classification.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new ClassifyFinding(tenant, findingId, classification, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await LoadFindingDetailAsync(tenant, findingId, findings, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> AcceptFindingAsync(
        string findingRef,
        KernelAcceptFindingRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IValidationFindings findings,
        IAuthorizationProbe probe,
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

        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide rationale in the request body.");
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var rationale = (request.Rationale ?? string.Empty).Trim();
        if (rationale.Length is 0 or > FindingText.MaxLength)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.DecisionTextInvalid);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckTenantEntitlementAsync(tenant, context, requiredFamily: null, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        var attestationId = request.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "accept-finding-attest", idempotencyKey));

        var canAccept = await probe.CanIAsync(tenant, AcceptFindingCapability, NodeRef.For(findingId), cancellationToken).ConfigureAwait(false);
        if (canAccept.Permitted)
        {
            var decisionHash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Accepted, rationale);
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Responsibility,
                    new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, decisionHash),
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

        var result = await sender.SendAsync(
            new AcceptFinding(tenant, findingId, rationale, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await LoadFindingDetailAsync(tenant, findingId, findings, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> ExplainFindingAsync(
        string findingRef,
        KernelExplainFindingRequest? request,
        HttpContext context,
        IKernelCommandSender sender,
        IValidationFindings findings,
        IAuthorizationProbe probe,
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

        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide explanation in the request body.");
        }

        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var explanation = (request.Explanation ?? string.Empty).Trim();
        if (explanation.Length is 0 or > FindingText.MaxLength)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.DecisionTextInvalid);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await CheckTenantEntitlementAsync(tenant, context, requiredFamily: null, cancellationToken).ConfigureAwait(false) is { } entitlementProblem)
        {
            return entitlementProblem;
        }

        var existing = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        var attestationId = request.AttestationId is { } given && given != Guid.Empty
            ? AttestationId.FromGuid(given)
            : AttestationId.FromGuid(DeriveGuid(tenant, "explain-finding-attest", idempotencyKey));

        var canExplain = await probe.CanIAsync(tenant, ExplainFindingCapability, NodeRef.For(findingId), cancellationToken).ConfigureAwait(false);
        if (canExplain.Permitted)
        {
            var decisionHash = FindingDecisionContent.Hash(findingId, existing.LastDetectedRun, FindingStatus.Explained, explanation);
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestationId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, decisionHash),
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

        var result = await sender.SendAsync(
            new ExplainFinding(tenant, findingId, explanation, attestationId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await LoadFindingDetailAsync(tenant, findingId, findings, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> ReopenFindingAsync(
        string findingRef,
        HttpContext context,
        IKernelCommandSender sender,
        IValidationFindings findings,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!TryParseFinding(findingRef, out var findingId))
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{findingRef}' is not a validation_finding NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await sender.SendAsync(
            new ReopenFinding(tenant, findingId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        return await LoadFindingDetailAsync(tenant, findingId, findings, cancellationToken).ConfigureAwait(false);
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

        var page = await findings.ListAsync(
            tenant,
            new FindingQuery(resolved.StudyId)
            {
                Stage = parsedStage,
                Statuses = parsedStatuses,
                Severity = parsedSeverity,
                Dataset = string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim(),
                PackKey = string.IsNullOrWhiteSpace(pack_key) ? null : pack_key.Trim(),
                Take = limit ?? 100,
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new KernelValidationFindingPageResponse(
            page.Total,
            [.. page.Findings.Select(KernelValidationFindingResponse.From)]));
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
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(studyNode) is { } mask)
        {
            return KernelProblems.NotVisible($"You may not see findings for study {studyNode} ({mask}).");
        }

        return TypedResults.Ok(new KernelValidationFindingDetailResponse(
            KernelValidationFindingResponse.From(view),
            view.Explain()));
    }

    private static async Task<Results<Ok<KernelValidationFindingDetailResponse>, ProblemHttpResult>> LoadFindingDetailAsync(
        TenantId tenant,
        ValidationFindingId findingId,
        IValidationFindings findings,
        CancellationToken cancellationToken)
    {
        var view = await findings.FindAsync(tenant, findingId, cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return KernelErrorEndpoints.Problem(ValidationErrors.FindingUnknown);
        }

        return TypedResults.Ok(new KernelValidationFindingDetailResponse(
            KernelValidationFindingResponse.From(view),
            view.Explain()));
    }

    internal static ProblemHttpResult? ReadCommandHeaders(
        HttpContext context,
        IKernelChangeReason changeReason,
        out string idempotencyKey,
        out string reason)
    {
        idempotencyKey = context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        return null;
    }

    internal static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveStudyForCommandAsync(
        TenantId tenant,
        string studyRef,
        HttpContext context,
        KernelError notFoundError,
        CancellationToken cancellationToken)
    {
        if (NodeRef.TryParse(studyRef.Trim(), out var node))
        {
            if (node.Kind != NodeKinds.Study)
            {
                return (default, KernelProblems.WrongKind(node, NodeKinds.Study));
            }

            return (StudyId.FromGuid(node.Id), null);
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        var byCode = directory is not null
            ? await directory.FindStudyByCodeAsync(tenant, studyRef.Trim(), cancellationToken).ConfigureAwait(false)
            : null;
        if (byCode is null)
        {
            return (default, KernelErrorEndpoints.Problem(notFoundError));
        }

        return (byCode.StudyId, null);
    }

    internal static async Task<(StudyId StudyId, ProblemHttpResult? Problem)> ResolveVisibleStudyAsync(
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

    private static bool TryParseStandardVersion(string text, out StandardVersionId standardId)
    {
        standardId = default;
        if (!NodeRef.TryParse(text.Trim(), out var node) || node.Kind != NodeKinds.StandardVersion)
        {
            return false;
        }

        standardId = StandardVersionId.FromGuid(node.Id);
        return !standardId.IsEmpty;
    }

    private static bool TryParseLibraryItem(string text, out LibraryItemId itemId)
    {
        itemId = default;
        if (!NodeRef.TryParse(text.Trim(), out var node) || node.Kind != NodeKinds.LibraryItem)
        {
            return false;
        }

        itemId = LibraryItemId.FromGuid(node.Id);
        return !itemId.IsEmpty;
    }

    private static bool TryParseLibraryItemList(
        IReadOnlyList<string> rawRefs,
        out List<LibraryItemId> ids,
        out ProblemHttpResult? problem)
    {
        ids = new List<LibraryItemId>(rawRefs.Count);
        problem = null;
        foreach (var rawRef in rawRefs)
        {
            if (!TryParseLibraryItem(rawRef, out var itemId))
            {
                problem = KernelProblems.BadRequest("Invalid NodeRef", $"'{rawRef}' is not a library_item NodeRef (library_item:<uuid>).");
                return false;
            }

            ids.Add(itemId);
        }

        return true;
    }

    private static bool TryParseFinding(string text, out ValidationFindingId findingId)
    {
        findingId = default;
        if (!NodeRef.TryParse(text.Trim(), out var node) || node.Kind != NodeKinds.ValidationFinding)
        {
            return false;
        }

        findingId = ValidationFindingId.FromGuid(node.Id);
        return !findingId.IsEmpty;
    }

    private static async Task ApproveDraftStandardVersionsAsync(
        TenantId tenant,
        IReadOnlyList<StandardVersionId> standardIds,
        IStandardsRegistry registry,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        DateTimeOffset authenticatedAt,
        string idempotencyKey,
        string reason,
        CancellationToken cancellationToken)
    {
        foreach (var stdId in standardIds)
        {
            var summary = await registry.FindStandardVersionAsync(tenant, stdId, cancellationToken).ConfigureAwait(false);
            if (summary is not { Status: GovernanceStatus.Draft })
            {
                continue;
            }

            var canApprove = await probe.CanIAsync(tenant, ApproveStandardCapability, NodeRef.For(summary.Id), cancellationToken).ConfigureAwait(false);
            if (!canApprove.Permitted)
            {
                continue;
            }

            var attestId = AttestationId.FromGuid(DeriveGuid(tenant, "linked-standard-attest", $"{idempotencyKey}:{summary.Id.Value:D}"));
            var attestResult = await sender.SendAsync(
                new Attest(
                    tenant,
                    attestId,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.StandardVersion, summary.Id.Value, summary.ContentHash),
                    authenticatedAt,
                    KernelLockEndpoints.StepUpMethod,
                    $"{idempotencyKey}:std-attest:{summary.Id.Value:N}",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!attestResult.IsSuccess && attestResult.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                continue;
            }

            await sender.SendAsync(
                new ApproveStandardVersion(tenant, summary.Id, attestId, $"{idempotencyKey}:std-approve:{summary.Id.Value:N}", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
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
            await ApproveDraftStandardVersionsAsync(
                tenant,
                [summary.Id],
                registry,
                sender,
                probe,
                authenticatedAt,
                idempotencyKey,
                reason,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<ProblemHttpResult?> CheckTenantEntitlementAsync(
        TenantId tenant,
        HttpContext context,
        ProductFamily? requiredFamily,
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

        bool isEntitled = requiredFamily is { } family
            ? entitlements.ProductFamilies.Contains(family)
            : entitlements.ProductFamilies.Count > 0;
        return isEntitled
            ? null
            : KernelErrorEndpoints.Problem(CommercialErrors.ProductFamilyNotEntitled);
    }

    internal static KernelError[] DistinctErrors(params IEnumerable<KernelError>[] groups) =>
        [.. groups.SelectMany(g => g).GroupBy(e => e.Code, StringComparer.Ordinal).Select(g => g.First())];

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
