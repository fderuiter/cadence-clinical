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
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using static Cadence.Kernel.Api.KernelValidationEndpoints;

namespace Cadence.Kernel.Api;

/// <summary>
/// Site registration, study-site linking and activation (<c>K-TIME-4</c>), evidence artifact registration
/// (<c>K-TIME-5</c>), and protocol effectivity governance routes (spec §3.1, §7.1, §8.3). Every mutating action
/// dispatches through <see cref="IKernelCommandSender"/> with an <c>Idempotency-Key</c> and a signed
/// <c>X-Change-Reason</c>.
/// </summary>
public static class KernelGovernanceEndpoints
{
    private static readonly Capability RegisterOrgCapability = new("organization.register", VerbClass.Execute);
    private static readonly Capability ActivateStudySiteCapability = new("study_site.activate", VerbClass.Approve);
    private static readonly IReadOnlyList<KernelError> RegisterSiteInstitutionErrors =
    [
        IdentityErrors.CodeRequired,
        IdentityErrors.NameRequired,
        IdentityErrors.TenantUnknown,
    ];

    /// <summary>Maps site, evidence artifact, protocol effectivity, and study-site activation routes on the kernel group.</summary>
    public static RouteGroupBuilder MapKernelGovernance(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel governance");

        group.MapGet("/sites", ListSitesAsync)
            .WithName("KernelListSites")
            .WithSummary("Lists registered clinical sites in the caller's tenant.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/sites", RegisterSiteAsync)
            .WithName("KernelRegisterSite")
            .WithSummary("Registers a durable clinical site under an institution in the caller's tenant (site.register).")
            .Produces<KernelSiteSummaryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors([.. DistinctErrors(
                KernelErrors.CommandPipeline,
                RaisesKernelErrorsAttribute.Of(typeof(RegisterSite))!,
                RegisterSiteInstitutionErrors)]);

        group.MapGet("/studies/{studyRef}/sites", ListStudySitesAsync)
            .WithName("KernelListStudySites")
            .WithSummary("Lists clinical sites linked to a study together with their activation status and effective protocol versions.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/studies/{studyRef}/sites", LinkSiteToStudyAsync)
            .WithName("KernelLinkSiteToStudy")
            .WithSummary("Links a registered clinical site to a study in Selected status (study_site.link).")
            .Produces<KernelStudySiteSummaryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<LinkSiteToStudy>();

        group.MapGet("/studies/{studyRef}/artifacts", ListStudyArtifactsAsync)
            .WithName("KernelListStudyArtifacts")
            .WithSummary("Lists evidence artifacts registered for a study together with their immutable versions.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/artifacts", RegisterArtifactAsync)
            .WithName("KernelRegisterArtifact")
            .WithSummary("Registers an evidence artifact with its initial hash-addressed immutable version (artifact.register).")
            .Produces<KernelArtifactSummaryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RegisterArtifact>();

        group.MapPost("/artifacts/{artifactRef}/versions", AddArtifactVersionAsync)
            .WithName("KernelAddArtifactVersion")
            .WithSummary("Appends a new immutable version to an existing evidence artifact (artifact.add_version).")
            .Produces<KernelArtifactSummaryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<AddArtifactVersion>();

        group.MapGet("/studies/{studyRef}/effectivities", ListStudyEffectivitiesAsync)
            .WithName("KernelListStudyEffectivities")
            .WithSummary("Lists protocol effectivity declarations across Study, Country, and StudySite scopes for a study.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/effectivities", DeclareEffectivityAsync)
            .WithName("KernelDeclareEffectivity")
            .WithSummary("Declares a protocol version Pending in a Study, Country, or StudySite scope (effectivity.declare).")
            .Produces<KernelEffectivitySummaryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<DeclareEffectivity>();

        group.MapPost("/effectivities/{effectivityRef}/status", ChangeEffectivityStatusAsync)
            .WithName("KernelChangeEffectivityStatus")
            .WithSummary("Transitions a protocol effectivity status (e.g. Pending to Effective with ethics/regulatory approval evidence, K-TIME-5).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ChangeEffectivityStatus>();

        group.MapPost("/study-sites/{studySiteRef}/activate", ActivateStudySiteAsync)
            .WithName("KernelActivateStudySite")
            .WithSummary("Activates a study site once at least one approved protocol version is effective there (study_site.activate, K-TIME-4).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ActivateStudySite>();

        group.MapPost("/study-sites/activate-by-code", ActivateStudySiteByCodeAsync)
            .WithName("KernelActivateStudySiteByCode")
            .WithSummary("Idempotently activates a study site by study code and site code once a protocol version is effective there (K-TIME-4).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<ActivateStudySite>();

        return kernel;
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelSiteSummaryResponse>>, ProblemHttpResult>> ListSitesAsync(
        HttpContext context,
        IStudyGovernanceReader governance,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var entries = await governance.ListSitesAsync(tenant, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelSiteSummaryResponse>>([.. entries.Select(KernelSiteSummaryResponse.From)]);
    }

    internal static async Task<Results<Created<KernelSiteSummaryResponse>, ProblemHttpResult>> RegisterSiteAsync(
        KernelRegisterSiteRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IStudyGovernanceReader governance,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide site registration fields in the request body.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var (institutionId, instProblem) = await ResolveOrCreateInstitutionAsync(
            tenant,
            request,
            directory,
            sender,
            probe,
            idempotencyKey,
            reason,
            cancellationToken).ConfigureAwait(false);
        if (instProblem is not null)
        {
            return instProblem;
        }

        var siteId = request.SiteId is { } given && given != Guid.Empty
            ? SiteId.FromGuid(given)
            : SiteId.FromGuid(DeriveGuid(tenant, "site", idempotencyKey));

        var result = await sender.SendAsync(
            new RegisterSite(
                tenant,
                siteId,
                institutionId,
                request.SiteCode?.Trim() ?? string.Empty,
                request.Name?.Trim() ?? string.Empty,
                request.Country?.Trim().ToUpperInvariant() ?? string.Empty,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var created = await governance.FindSiteAsync(tenant, result.Value, cancellationToken).ConfigureAwait(false)
            ?? new SiteDirectoryEntry(
                result.Value,
                1,
                institutionId,
                request.InstitutionCode ?? string.Empty,
                request.InstitutionName ?? string.Empty,
                request.SiteCode?.Trim() ?? string.Empty,
                request.Name?.Trim() ?? string.Empty,
                request.Country?.Trim().ToUpperInvariant() ?? string.Empty);

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/sites/{NodeRef.For(result.Value)}",
            KernelSiteSummaryResponse.From(created));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelStudySiteSummaryResponse>>, ProblemHttpResult>> ListStudySitesAsync(
        string studyRef,
        HttpContext context,
        IStudyGovernanceReader governance,
        IEffectivityReader effectivities,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var (studyId, problem) = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return problem;
        }

        var entries = await governance.ListStudySitesAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var responses = new List<KernelStudySiteSummaryResponse>(entries.Count);
        foreach (var entry in entries)
        {
            var effective = await effectivities.EffectiveAtAsync(tenant, entry.StudySiteId, now, now, cancellationToken).ConfigureAwait(false);
            responses.Add(KernelStudySiteSummaryResponse.From(entry, effective));
        }

        return TypedResults.Ok<IReadOnlyList<KernelStudySiteSummaryResponse>>(responses);
    }

    internal static async Task<Results<Created<KernelStudySiteSummaryResponse>, ProblemHttpResult>> LinkSiteToStudyAsync(
        string studyRef,
        KernelLinkSiteToStudyRequest? request,
        HttpContext context,
        IStudyGovernanceReader governance,
        IEffectivityReader effectivities,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SiteRef))
        {
            return KernelProblems.BadRequest("Invalid request", "Provide site_ref in the request body.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var (studyId, studyProblem) = await ResolveStudyForCommandAsync(tenant, studyRef, context, StudyErrors.StudyUnknown, cancellationToken).ConfigureAwait(false);
        if (studyProblem is not null)
        {
            return studyProblem;
        }

        SiteId siteId;
        if (NodeRef.TryParse(request.SiteRef.Trim(), out var siteNode))
        {
            if (siteNode.Kind != NodeKinds.Site)
            {
                return KernelProblems.WrongKind(siteNode, NodeKinds.Site);
            }

            siteId = SiteId.FromGuid(siteNode.Id);
        }
        else
        {
            var byCode = await governance.FindSiteByCodeAsync(tenant, request.SiteRef.Trim(), cancellationToken).ConfigureAwait(false);
            if (byCode is null)
            {
                return KernelErrorEndpoints.Problem(StudyErrors.SiteUnknown);
            }

            siteId = byCode.SiteId;
        }

        var studySiteId = request.StudySiteId is { } given && given != Guid.Empty
            ? StudySiteId.FromGuid(given)
            : StudySiteId.FromGuid(DeriveGuid(tenant, "study-site", idempotencyKey));

        var result = await sender.SendAsync(
            new LinkSiteToStudy(tenant, studySiteId, studyId, siteId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var now = clock.UtcNow;
        var effective = await effectivities.EffectiveAtAsync(tenant, result.Value, now, now, cancellationToken).ConfigureAwait(false);
        var created = await governance.FindStudySiteAsync(tenant, result.Value, cancellationToken).ConfigureAwait(false)
            ?? new StudySiteDirectoryEntry(result.Value, 1, studyId, string.Empty, siteId, string.Empty, string.Empty, string.Empty, default, string.Empty, "Selected");

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/study-sites/{NodeRef.For(result.Value)}",
            KernelStudySiteSummaryResponse.From(created, effective));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelArtifactSummaryResponse>>, ProblemHttpResult>> ListStudyArtifactsAsync(
        string studyRef,
        HttpContext context,
        IStudyGovernanceReader governance,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var (studyId, problem) = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return problem;
        }

        var entries = await governance.ListStudyArtifactsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelArtifactSummaryResponse>>([.. entries.Select(KernelArtifactSummaryResponse.From)]);
    }

    internal static async Task<Results<Created<KernelArtifactSummaryResponse>, ProblemHttpResult>> RegisterArtifactAsync(
        KernelRegisterArtifactRequest? request,
        HttpContext context,
        IStudyGovernanceReader governance,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide artifact registration fields in the request body.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        if (!Enum.TryParse<ArtifactKind>(request.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return KernelErrorEndpoints.Problem(EvidenceErrors.KindInvalid);
        }

        var tenant = KernelRequestTenant.Get(context);
        StudyId? studyId = null;
        if (!string.IsNullOrWhiteSpace(request.StudyRef))
        {
            var (resolvedStudyId, studyProblem) = await ResolveStudyForCommandAsync(
                tenant,
                request.StudyRef,
                context,
                EvidenceErrors.StudyUnknown,
                cancellationToken).ConfigureAwait(false);
            if (studyProblem is not null)
            {
                return studyProblem;
            }

            studyId = resolvedStudyId;
        }

        var artifactId = request.ArtifactId is { } given && given != Guid.Empty
            ? ArtifactId.FromGuid(given)
            : ArtifactId.FromGuid(DeriveGuid(tenant, "artifact", idempotencyKey));
        var versionId = request.ArtifactVersionId is { } givenVer && givenVer != Guid.Empty
            ? ArtifactVersionId.FromGuid(givenVer)
            : ArtifactVersionId.FromGuid(DeriveGuid(tenant, "artifact-version", idempotencyKey));

        var content = BuildArtifactContent(
            request.ContentHash,
            request.SizeBytes,
            request.MediaType,
            request.StorageRef,
            request.DocumentText,
            artifactId,
            versionId);

        var result = await sender.SendAsync(
            new RegisterArtifact(tenant, artifactId, versionId, kind, studyId, request.Title ?? string.Empty, content, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var registered = result.Value!;
        var entry = await governance.FindArtifactAsync(tenant, registered.ArtifactId, cancellationToken).ConfigureAwait(false)
            ?? FallbackArtifactEntry(registered, content, clock.UtcNow);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/artifacts/{NodeRef.For(registered.ArtifactId)}",
            KernelArtifactSummaryResponse.From(entry));
    }

    internal static async Task<Results<Created<KernelArtifactSummaryResponse>, ProblemHttpResult>> AddArtifactVersionAsync(
        string artifactRef,
        KernelAddArtifactVersionRequest? request,
        HttpContext context,
        IStudyGovernanceReader governance,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid request", "Provide artifact version content fields in the request body.");
        }

        if (!NodeRef.TryParse(artifactRef, out var node) || node.Kind != NodeKinds.Artifact)
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{artifactRef}' is not an artifact NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var artifactId = ArtifactId.FromGuid(node.Id);
        var versionId = request.ArtifactVersionId is { } givenVer && givenVer != Guid.Empty
            ? ArtifactVersionId.FromGuid(givenVer)
            : ArtifactVersionId.FromGuid(DeriveGuid(tenant, "artifact-version", idempotencyKey));

        var content = BuildArtifactContent(
            request.ContentHash,
            request.SizeBytes,
            request.MediaType,
            request.StorageRef,
            request.DocumentText,
            artifactId,
            versionId);

        var result = await sender.SendAsync(
            new AddArtifactVersion(tenant, artifactId, versionId, content, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var added = result.Value!;
        var entry = await governance.FindArtifactAsync(tenant, added.ArtifactId, cancellationToken).ConfigureAwait(false)
            ?? FallbackArtifactEntry(added, content, clock.UtcNow);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/artifacts/{NodeRef.For(added.ArtifactId)}",
            KernelArtifactSummaryResponse.From(entry));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelEffectivitySummaryResponse>>, ProblemHttpResult>> ListStudyEffectivitiesAsync(
        string studyRef,
        HttpContext context,
        IStudyGovernanceReader governance,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var tenant = KernelRequestTenant.Get(context);
        var (studyId, problem) = await ResolveVisibleStudyAsync(tenant, studyRef, context, visibility, actor, cancellationToken).ConfigureAwait(false);
        if (problem is not null)
        {
            return problem;
        }

        var entries = await governance.ListStudyEffectivitiesAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelEffectivitySummaryResponse>>([.. entries.Select(KernelEffectivitySummaryResponse.From)]);
    }

    internal static async Task<Results<Created<KernelEffectivitySummaryResponse>, ProblemHttpResult>> DeclareEffectivityAsync(
        KernelDeclareEffectivityRequest? request,
        HttpContext context,
        IStudyGovernanceReader governance,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProtocolVersionRef))
        {
            return KernelProblems.BadRequest("Invalid request", "Provide protocol_version_ref and scope_level in the request body.");
        }

        if (!NodeRef.TryParse(request.ProtocolVersionRef.Trim(), out var pvNode) || pvNode.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{request.ProtocolVersionRef}' is not a protocol_version NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        if (!TryBuildScope(request.ScopeLevel, request.Country, request.StudySiteRef, out var scope, out var scopeProblem))
        {
            return scopeProblem!;
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectivityId = request.EffectivityId is { } given && given != Guid.Empty
            ? EffectivityId.FromGuid(given)
            : EffectivityId.FromGuid(DeriveGuid(tenant, "effectivity", idempotencyKey));

        // Default DeclaredAt slightly earlier than UtcNow so an immediate follow-up ChangeEffectivityStatus at UtcNow
        // satisfies effectiveAt > StatusSince while remaining <= recordedAt.
        var declaredAt = request.DeclaredAt ?? clock.UtcNow.AddMinutes(-1);

        var result = await sender.SendAsync(
            new DeclareEffectivity(
                tenant,
                effectivityId,
                ProtocolVersionId.FromGuid(pvNode.Id),
                scope,
                declaredAt,
                idempotencyKey,
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var declared = result.Value!;
        var entry = await governance.FindEffectivityAsync(tenant, declared.EffectivityId, cancellationToken).ConfigureAwait(false)
            ?? FallbackEffectivityEntry(declared);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/effectivities/{NodeRef.For(declared.EffectivityId)}",
            KernelEffectivitySummaryResponse.From(entry));
    }

    internal static async Task<Results<Ok<KernelEffectivitySummaryResponse>, ProblemHttpResult>> ChangeEffectivityStatusAsync(
        string effectivityRef,
        KernelChangeEffectivityStatusRequest? request,
        HttpContext context,
        IStudyGovernanceReader governance,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Status))
        {
            return KernelProblems.BadRequest("Invalid request", "Provide status in the request body.");
        }

        if (!NodeRef.TryParse(effectivityRef, out var node) || node.Kind != NodeKinds.Effectivity)
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{effectivityRef}' is not an effectivity NodeRef.");
        }

        if (!Enum.TryParse<EffectivityStatus>(request.Status.Trim(), ignoreCase: true, out var targetStatus) || !Enum.IsDefined(targetStatus))
        {
            return KernelErrorEndpoints.Problem(EffectivityErrors.InvalidTransition);
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var evidence = new List<ArtifactVersionId>();
        if (request.EvidenceVersionRefs is not null)
        {
            foreach (var rawRef in request.EvidenceVersionRefs)
            {
                if (!NodeRef.TryParse(rawRef.Trim(), out var evNode) || evNode.Kind != NodeKinds.ArtifactVersion)
                {
                    return KernelProblems.BadRequest("Invalid NodeRef", $"'{rawRef}' is not an artifact_version NodeRef.");
                }

                evidence.Add(ArtifactVersionId.FromGuid(evNode.Id));
            }
        }

        var tenant = KernelRequestTenant.Get(context);
        var effectivityId = EffectivityId.FromGuid(node.Id);
        var effectiveAt = request.EffectiveAt ?? clock.UtcNow;

        var result = await sender.SendAsync(
            new ChangeEffectivityStatus(tenant, effectivityId, targetStatus, effectiveAt, evidence, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var changed = result.Value!;
        var entry = await governance.FindEffectivityAsync(tenant, changed.EffectivityId, cancellationToken).ConfigureAwait(false)
            ?? FallbackEffectivityEntry(changed);
        return TypedResults.Ok(KernelEffectivitySummaryResponse.From(entry));
    }

    internal static async Task<Results<Ok<KernelStudySiteSummaryResponse>, ProblemHttpResult>> ActivateStudySiteAsync(
        string studySiteRef,
        HttpContext context,
        IStudyGovernanceReader governance,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(studySiteRef, out var node) || node.Kind != NodeKinds.StudySite)
        {
            return KernelProblems.BadRequest("Invalid NodeRef", $"'{studySiteRef}' is not a study_site NodeRef.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var studySiteId = StudySiteId.FromGuid(node.Id);
        var result = await sender.SendAsync(
            new ActivateStudySite(tenant, studySiteId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var activated = result.Value!;
        var entry = await governance.FindStudySiteAsync(tenant, activated.StudySiteId, cancellationToken).ConfigureAwait(false)
            ?? new StudySiteDirectoryEntry(activated.StudySiteId, 1, default, string.Empty, default, string.Empty, string.Empty, string.Empty, default, string.Empty, activated.Status);
        return TypedResults.Ok(KernelStudySiteSummaryResponse.From(entry, activated.EffectiveVersions));
    }

    internal static async Task<Results<Ok<KernelStudySiteSummaryResponse>, ProblemHttpResult>> ActivateStudySiteByCodeAsync(
        KernelActivateStudySiteByCodeRequest? request,
        HttpContext context,
        IStudyGovernanceReader governance,
        IEffectivityReader effectivities,
        IAuthorizationProbe probe,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.StudyCode) || string.IsNullOrWhiteSpace(request.SiteCode))
        {
            return KernelProblems.BadRequest("Invalid request", "Provide study_code and site_code in the request body.");
        }

        if (ReadCommandHeaders(context, changeReason, out var idempotencyKey, out var reason) is { } headerProblem)
        {
            return headerProblem;
        }

        var tenant = KernelRequestTenant.Get(context);
        var studySite = await governance.FindStudySiteByCodeAsync(tenant, request.StudyCode, request.SiteCode, cancellationToken).ConfigureAwait(false);
        if (studySite is null)
        {
            return KernelErrorEndpoints.Problem(EffectivityErrors.StudySiteUnknown);
        }

        if (string.Equals(studySite.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var canActivate = await probe.CanIAsync(tenant, ActivateStudySiteCapability, NodeRef.For(studySite.StudySiteId), cancellationToken).ConfigureAwait(false);
            if (!canActivate.Permitted)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
            }

            var now = clock.UtcNow;
            var effective = await effectivities.EffectiveAtAsync(tenant, studySite.StudySiteId, now, now, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(KernelStudySiteSummaryResponse.From(studySite, effective));
        }

        var result = await sender.SendAsync(
            new ActivateStudySite(tenant, studySite.StudySiteId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var activatedByCode = result.Value!;
        var updated = await governance.FindStudySiteAsync(tenant, activatedByCode.StudySiteId, cancellationToken).ConfigureAwait(false) ?? studySite;
        return TypedResults.Ok(KernelStudySiteSummaryResponse.From(updated, activatedByCode.EffectiveVersions));
    }

    private static async Task<(OrganizationId InstitutionId, ProblemHttpResult? Problem)> ResolveOrCreateInstitutionAsync(
        TenantId tenant,
        KernelRegisterSiteRequest request,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        string idempotencyKey,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.InstitutionRef))
        {
            if (!NodeRef.TryParse(request.InstitutionRef.Trim(), out var orgNode) || orgNode.Kind != NodeKinds.Organization)
            {
                return (default, KernelProblems.BadRequest("Invalid NodeRef", $"'{request.InstitutionRef}' is not an organization NodeRef."));
            }

            return (OrganizationId.FromGuid(orgNode.Id), null);
        }

        if (!string.IsNullOrWhiteSpace(request.InstitutionCode))
        {
            var byCode = await directory.FindOrganizationByCodeAsync(tenant, request.InstitutionCode.Trim(), cancellationToken).ConfigureAwait(false);
            if (byCode is not null)
            {
                return (byCode.OrganizationId, null);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.InstitutionName) || !string.IsNullOrWhiteSpace(request.InstitutionCode))
        {
            var canRegisterOrg = await probe.CanIAsync(tenant, RegisterOrgCapability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);
            if (canRegisterOrg.Permitted)
            {
                var rawCode = !string.IsNullOrWhiteSpace(request.InstitutionCode)
                    ? request.InstitutionCode.Trim().ToUpperInvariant()
                    : $"INST-{(request.SiteCode ?? "SITE").Trim().ToUpperInvariant()}";
                var rawName = !string.IsNullOrWhiteSpace(request.InstitutionName)
                    ? request.InstitutionName.Trim()
                    : $"{request.Name?.Trim() ?? rawCode} Institution";
                var orgId = OrganizationId.FromGuid(DeriveGuid(tenant, "site-institution", $"{idempotencyKey}:{rawCode}"));
                var orgResult = await sender.SendAsync(
                    new RegisterOrganization(tenant, orgId, rawCode, rawName, OrganizationKind.SiteInstitution, $"{idempotencyKey}:institution", reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (orgResult.IsSuccess)
                {
                    return (orgResult.Value, null);
                }

                if (orgResult.Error?.Code != KernelErrors.AlreadyExists.Code)
                {
                    return (default, KernelErrorEndpoints.Problem(orgResult.Error!));
                }

                var existingByCode = await directory.FindOrganizationByCodeAsync(tenant, rawCode, cancellationToken).ConfigureAwait(false);
                if (existingByCode is not null)
                {
                    return (existingByCode.OrganizationId, null);
                }
            }
        }

        var organizations = await directory.ListOrganizationsAsync(tenant, cancellationToken).ConfigureAwait(false);
        var defaultInst = organizations.FirstOrDefault(o => o.Kind == OrganizationKind.SiteInstitution)
            ?? organizations.FirstOrDefault();
        if (defaultInst is not null)
        {
            return (defaultInst.OrganizationId, null);
        }

        return (default, KernelErrorEndpoints.Problem(StudyErrors.InstitutionUnknown));
    }

    private static ArtifactContent BuildArtifactContent(
        string? contentHash,
        long? sizeBytes,
        string? mediaType,
        string? storageRef,
        string? documentText,
        ArtifactId artifactId,
        ArtifactVersionId versionId)
    {
        var effectiveHash = contentHash?.Trim();
        long effectiveSize = sizeBytes ?? 0;
        if (string.IsNullOrWhiteSpace(effectiveHash) && !string.IsNullOrEmpty(documentText))
        {
            var bytes = Encoding.UTF8.GetBytes(documentText);
            effectiveHash = ContentHash.OfBytes(bytes);
            if (sizeBytes is null)
            {
                effectiveSize = bytes.LongLength;
            }
        }

        var fallbackMediaType = !string.IsNullOrEmpty(documentText) ? "text/plain" : "application/pdf";
        var effectiveMediaType = string.IsNullOrWhiteSpace(mediaType)
            ? fallbackMediaType
            : mediaType.Trim();
        var effectiveStorageRef = string.IsNullOrWhiteSpace(storageRef)
            ? $"s3://cadence-evidence/{artifactId.Value:D}/{versionId.Value:D}"
            : storageRef.Trim();

        return new ArtifactContent(effectiveHash ?? string.Empty, effectiveSize, effectiveMediaType, effectiveStorageRef);
    }

    private static bool TryBuildScope(
        string? scopeLevel,
        string? country,
        string? studySiteRef,
        out EffectivityScope scope,
        out ProblemHttpResult? problem)
    {
        scope = default!;
        problem = null;
        if (!Enum.TryParse<EffectivityLevel>(scopeLevel?.Trim(), ignoreCase: true, out var level) || !Enum.IsDefined(level))
        {
            problem = KernelErrorEndpoints.Problem(EffectivityErrors.ScopeInvalid);
            return false;
        }

        switch (level)
        {
            case EffectivityLevel.Study:
                scope = EffectivityScope.Study();
                return true;
            case EffectivityLevel.Country:
                scope = EffectivityScope.InCountry(country?.Trim().ToUpperInvariant() ?? string.Empty);
                return true;
            case EffectivityLevel.StudySite:
                if (string.IsNullOrWhiteSpace(studySiteRef) || !NodeRef.TryParse(studySiteRef.Trim(), out var ssNode) || ssNode.Kind != NodeKinds.StudySite)
                {
                    problem = KernelProblems.BadRequest("Invalid NodeRef", $"'{studySiteRef}' is not a study_site NodeRef.");
                    return false;
                }

                scope = EffectivityScope.At(StudySiteId.FromGuid(ssNode.Id));
                return true;
            default:
                problem = KernelErrorEndpoints.Problem(EffectivityErrors.ScopeInvalid);
                return false;
        }
    }

    private static ArtifactDirectoryEntry FallbackArtifactEntry(
        ArtifactState state,
        ArtifactContent content,
        DateTimeOffset recordedAt)
    {
        var ver = new ArtifactVersionDirectoryEntry(
            state.LatestVersionId,
            state.LatestVersionNumber,
            state.LatestContentHash,
            content.SizeBytes,
            content.MediaType,
            content.StorageRef,
            recordedAt);
        return new ArtifactDirectoryEntry(state.ArtifactId, 1, state.Kind, state.StudyId, state.Title, ver, [ver]);
    }

    private static EffectivityDirectoryEntry FallbackEffectivityEntry(EffectivityState state) =>
        new(
            state.EffectivityId,
            1,
            default,
            state.ProtocolVersionId,
            1,
            state.Scope,
            state.Status,
            state.StatusSince,
            state.Evidence);

    private static Guid DeriveGuid(TenantId tenant, string kind, string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-governance:{tenant}:{kind}:{seed}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
