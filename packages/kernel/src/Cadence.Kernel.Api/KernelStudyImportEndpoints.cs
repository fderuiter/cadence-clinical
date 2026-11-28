using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Api.StudyImports;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Study browsing, self-service study registration, bundled eCRF template discovery, and synchronous ODM v1.3.2 XML /
/// USDM v4 JSON protocol draft import endpoints. Every mutating operation executes through
/// <see cref="IKernelCommandSender"/> with a signed reason for change (<c>X-Change-Reason</c>) and an
/// <c>Idempotency-Key</c>. Imported protocol definitions always enter <see cref="ProtocolVersionStatus.Draft"/>.
/// </summary>
public static class KernelStudyImportEndpoints
{
    /// <summary>Maps study directory, study creation, template listing, and synchronous protocol draft import routes.</summary>
    public static RouteGroupBuilder MapKernelStudyImports(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>();

        group.MapGet("/studies", ListStudiesAsync)
            .WithName("KernelListStudies")
            .WithSummary("Lists visible studies in the caller's tenant.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/organizations", ListOrganizationsAsync)
            .WithName("KernelListOrganizations")
            .WithSummary("Lists visible organizations in the caller's tenant.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/organizations", CreateOrganizationAsync)
            .WithName("KernelCreateOrganization")
            .WithSummary("Registers an organization such as a site institution or sponsor in the caller's tenant (organization.register).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesKernelCommand<RegisterOrganization>();

        group.MapGet("/sites", ListSitesAsync)
            .WithName("KernelListSites")
            .WithSummary("Lists visible investigational sites in the caller's tenant.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/sites", CreateSiteAsync)
            .WithName("KernelCreateSite")
            .WithSummary("Registers an investigational site under a site institution in the caller's tenant (site.register).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                IdentityErrors.CodeRequired,
                IdentityErrors.NameRequired,
                IdentityErrors.TenantUnknown,
                StudyErrors.CodeRequired,
                StudyErrors.NameRequired,
                StudyErrors.CountryInvalid,
                StudyErrors.InstitutionUnknown,
                StudyErrors.SiteCodeTaken,
            ]);

        group.MapPost("/studies", CreateStudyAsync)
            .WithName("KernelCreateStudy")
            .WithSummary("Registers a new study in the caller's tenant with a signed reason for change (study.register) and optional one-step site link.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                IdentityErrors.CodeRequired,
                IdentityErrors.NameRequired,
                IdentityErrors.TenantUnknown,
                StudyErrors.CodeRequired,
                StudyErrors.TitleRequired,
                StudyErrors.SponsorUnknown,
                StudyErrors.StudyCodeTaken,
                StudyErrors.StudyUnknown,
                StudyErrors.SiteUnknown,
                StudyErrors.AlreadyLinked,
            ]);

        group.MapGet("/studies/{studyRef}/sites", ListStudySitesAsync)
            .WithName("KernelListStudySites")
            .WithSummary("Lists visible investigational sites linked to a study together with their activation status and currently effective protocol versions.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/studies/{studyRef}/sites", LinkSiteToStudyAsync)
            .WithName("KernelLinkSiteToStudy")
            .WithSummary("Links an investigational site (registering it inline when site details are supplied) to a study in the caller's tenant (site.register, study_site.link).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                StudyErrors.CodeRequired,
                StudyErrors.NameRequired,
                StudyErrors.CountryInvalid,
                StudyErrors.InstitutionUnknown,
                StudyErrors.SiteCodeTaken,
                StudyErrors.StudyUnknown,
                StudyErrors.SiteUnknown,
                StudyErrors.AlreadyLinked,
            ]);

        group.MapGet("/protocol-templates", ListProtocolTemplates)
            .WithName("KernelListProtocolTemplates")
            .WithSummary("Lists bundled standard CDISC Library eCRF templates available for protocol draft import.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/protocol-versions/{versionRef}/design-summary", GetProtocolDesignSummaryAsync)
            .WithName("KernelGetProtocolDesignSummary")
            .WithSummary("Returns a viewable summary of a protocol version's forms, ordered items, and import notes.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/studies/{studyRef}/protocol-imports", ImportProtocolDraftAsync)
            .WithName("KernelImportProtocolDraft")
            .WithSummary("Synchronously imports ODM v1.3.2 XML, USDM v4 JSON, or bundled eCRF templates into a protocol draft.")
            .Accepts<KernelImportProtocolJsonRequest>("application/json", "multipart/form-data")
            .Produces<KernelProtocolDraftSummaryResponse>(StatusCodes.Status200OK)
            .Produces<KernelProtocolDraftSummaryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                KernelErrors.AlreadyExists,
                KernelErrors.NotFound,
                IdentityErrors.NameRequired,
                IdentityErrors.TenantUnknown,
                ProtocolErrors.StudyUnknown,
                ProtocolErrors.FirstVersionExists,
                ProtocolErrors.InvalidTransition,
                ProtocolErrors.ContentRequired,
                ProtocolErrors.ContentInvalid,
                ProtocolErrors.AmendmentInvalid,
                ProtocolErrors.AmendmentNotAllowed,
                KnowledgeErrors.CodeSystemUnknown,
                KnowledgeErrors.CodeSystemUriInvalid,
                KnowledgeErrors.ConceptLabelRequired,
                KnowledgeErrors.DuplicateEntry,
                KnowledgeErrors.EntriesInvalid,
                KnowledgeErrors.ExternalCodeMismatch,
                KnowledgeErrors.LicenseMismatch,
                KnowledgeErrors.PackageInvalid,
                KnowledgeErrors.VersionRequired,
                StandardsErrors.LibraryItemInvalid,
                StandardsErrors.StandardVersionUnknown,
            ]);

        return kernel;
    }

    internal static Task<Ok<IReadOnlyList<KernelStudySummaryResponse>>> ListStudiesAsync(
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelStudyRegistrationEndpoints.ListStudiesAsync(context, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelOrganizationSummaryResponse>>, ProblemHttpResult>> ListOrganizationsAsync(
        string? kind,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelDirectoryEndpoints.ListOrganizationsAsync(kind, context, visibility, actor, cancellationToken);

    internal static Task<Results<Created<KernelOrganizationSummaryResponse>, ProblemHttpResult>> CreateOrganizationAsync(
        KernelCreateOrganizationRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelDirectoryEndpoints.CreateOrganizationAsync(request, context, directory, sender, probe, catalog, changeReason, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelSiteSummaryResponse>>, ProblemHttpResult>> ListSitesAsync(
        string? institutionRef,
        string? country,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelDirectoryEndpoints.ListSitesAsync(institutionRef, country, context, visibility, actor, cancellationToken);

    internal static Task<Results<Created<KernelSiteSummaryResponse>, ProblemHttpResult>> CreateSiteAsync(
        KernelCreateSiteRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelDirectoryEndpoints.CreateSiteAsync(request, context, directory, sender, probe, catalog, changeReason, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelStudySiteSummaryResponse>>, ProblemHttpResult>> ListStudySitesAsync(
        string studyRef,
        HttpContext context,
        IStudyDirectory directory,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken,
        DateTimeOffset? valid_at = null,
        DateTimeOffset? known_at = null) =>
        KernelDirectoryEndpoints.ListStudySitesAsync(studyRef, context, directory, visibility, actor, cancellationToken, valid_at, known_at);

    internal static Task<Results<Created<KernelStudySiteSummaryResponse>, ProblemHttpResult>> LinkSiteToStudyAsync(
        string studyRef,
        KernelLinkSiteToStudyRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelDirectoryEndpoints.LinkSiteToStudyAsync(studyRef, request, context, directory, sender, probe, catalog, visibility, actor, changeReason, cancellationToken);

    internal static Task<Results<Created<KernelStudySummaryResponse>, ProblemHttpResult>> CreateStudyAsync(
        KernelCreateStudyRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelStudyRegistrationEndpoints.CreateStudyAsync(request, context, directory, sender, probe, catalog, changeReason, cancellationToken);

    internal static Ok<IReadOnlyList<KernelProtocolTemplateResponse>> ListProtocolTemplates(HttpContext context) =>
        KernelProtocolTemplateEndpoints.ListProtocolTemplates(context);

    internal static Task<Results<Ok<KernelProtocolDraftSummaryResponse>, ProblemHttpResult>> GetProtocolDesignSummaryAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken) =>
        KernelProtocolTemplateEndpoints.GetProtocolDesignSummaryAsync(versionRef, context, reader, visibility, actor, cancellationToken);

    internal static Task<Results<Created<KernelProtocolDraftSummaryResponse>, Ok<KernelProtocolDraftSummaryResponse>, ProblemHttpResult>> ImportProtocolDraftAsync(
        string studyRef,
        HttpContext context,
        IStudyDirectory directory,
        IProtocolVersionReader versionReader,
        IProtocolDesignImporter importer,
        IConceptRegistry concepts,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        INodeVisibility visibility,
        ICurrentActor actor,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken) =>
        KernelProtocolDraftImportEndpoints.ImportProtocolDraftAsync(
            studyRef,
            context,
            directory,
            versionReader,
            importer,
            concepts,
            sender,
            probe,
            catalog,
            visibility,
            actor,
            changeReason,
            cancellationToken);
}
