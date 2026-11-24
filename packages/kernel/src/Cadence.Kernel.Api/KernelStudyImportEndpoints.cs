using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cadence.Kernel.Api;

/// <summary>
/// Study browsing, self-service study registration, bundled eCRF template discovery, and synchronous ODM v1.3.2 XML /
/// USDM v4 JSON protocol draft import endpoints. Every mutating operation executes through
/// <see cref="IKernelCommandSender"/> with a signed reason for change (<c>X-Change-Reason</c>) and an
/// <c>Idempotency-Key</c>. Imported protocol definitions always enter <see cref="ProtocolVersionStatus.Draft"/>.
/// </summary>
public static class KernelStudyImportEndpoints
{
    private static readonly FormOptions MultipartLimits = new()
    {
        MultipartBodyLengthLimit = IProtocolDesignImporter.MaxPayloadBytes + 262_144,
        ValueLengthLimit = IProtocolDesignImporter.MaxPayloadBytes,
    };

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

    internal static async Task<Ok<IReadOnlyList<KernelStudySummaryResponse>>> ListStudiesAsync(
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelStudySummaryResponse>>([]);
        }

        var tenant = KernelRequestTenant.Get(context);
        var entries = await directory.ListStudiesAsync(tenant, cancellationToken).ConfigureAwait(false);
        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            entries.Select(e => NodeRef.For(e.StudyId)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<KernelStudySummaryResponse>>(
            [.. entries.Where(e => masks.For(NodeRef.For(e.StudyId)) is null).Select(KernelStudySummaryResponse.From)]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelOrganizationSummaryResponse>>, ProblemHttpResult>> ListOrganizationsAsync(
        string? kind,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        OrganizationKind? wantedKind = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<OrganizationKind>(kind, ignoreCase: true, out var parsedKind) || !Enum.IsDefined(parsedKind))
            {
                return KernelProblems.BadRequest("Invalid organization kind", $"'{kind}' is not a valid organization kind (Sponsor, Cro, SiteInstitution).");
            }

            wantedKind = parsedKind;
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelOrganizationSummaryResponse>>([]);
        }

        var tenant = KernelRequestTenant.Get(context);
        var entries = await directory.ListOrganizationsAsync(tenant, cancellationToken).ConfigureAwait(false);
        if (wantedKind is { } filter)
        {
            entries = [.. entries.Where(e => e.Kind == filter)];
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            entries.Select(e => NodeRef.For(e.OrganizationId)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<KernelOrganizationSummaryResponse>>(
            [.. entries.Where(e => masks.For(NodeRef.For(e.OrganizationId)) is null).Select(KernelOrganizationSummaryResponse.From)]);
    }

    internal static async Task<Results<Created<KernelOrganizationSummaryResponse>, ProblemHttpResult>> CreateOrganizationAsync(
        KernelCreateOrganizationRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid organization request", "Provide an organization name and optional code/kind.");
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        OrganizationKind parsedKind = OrganizationKind.SiteInstitution;
        if (!string.IsNullOrWhiteSpace(request.Kind)
            && (!Enum.TryParse(request.Kind.Trim(), ignoreCase: true, out parsedKind) || !Enum.IsDefined(parsedKind)))
        {
            return KernelProblems.BadRequest(
                "Invalid organization kind",
                $"'{request.Kind}' is not a valid organization kind (SiteInstitution, Sponsor, Cro, Vendor, Regulator, EthicsCommittee, Lab).");
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return KernelErrorEndpoints.Problem(IdentityErrors.NameRequired);
        }

        var code = !string.IsNullOrWhiteSpace(request.Code)
            ? request.Code.Trim().ToUpperInvariant()
            : NormalizeOrganizationCode(name);
        if (string.IsNullOrWhiteSpace(code))
        {
            return KernelErrorEndpoints.Problem(IdentityErrors.CodeRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (!await IsPermittedAsync(tenant, typeof(RegisterOrganization), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        var derivedOrgId = OrganizationId.FromGuid(DeriveGuid(tenant, "organization", idempotencyKey));
        var existingByCode = await directory.FindOrganizationByCodeAsync(tenant, code, cancellationToken).ConfigureAwait(false);
        if (existingByCode is not null && existingByCode.OrganizationId != derivedOrgId)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AlreadyExists);
        }

        var result = await sender.SendAsync(
            new RegisterOrganization(tenant, derivedOrgId, code, name, parsedKind, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value.IsEmpty)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var persisted = await directory.FindOrganizationAsync(tenant, result.Value, cancellationToken).ConfigureAwait(false)
            ?? new OrganizationDirectoryEntry(result.Value, 1, code, name, parsedKind);
        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(persisted.OrganizationId)}",
            KernelOrganizationSummaryResponse.From(persisted));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelSiteSummaryResponse>>, ProblemHttpResult>> ListSitesAsync(
        string? institutionRef,
        string? country,
        HttpContext context,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        OrganizationId? wantedInstitution = null;
        if (!string.IsNullOrWhiteSpace(institutionRef))
        {
            if (!NodeRef.TryParse(institutionRef.Trim(), out var orgNode))
            {
                return KernelProblems.InvalidNodeRef(institutionRef);
            }

            if (orgNode.Kind != NodeKinds.Organization)
            {
                return KernelProblems.WrongKind(orgNode, NodeKinds.Organization);
            }

            wantedInstitution = OrganizationId.FromGuid(orgNode.Id);
        }

        var directory = context.RequestServices.GetService<IStudyDirectory>();
        if (directory is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelSiteSummaryResponse>>([]);
        }

        var tenant = KernelRequestTenant.Get(context);
        var entries = await directory.ListSitesAsync(tenant, cancellationToken).ConfigureAwait(false);
        if (wantedInstitution is { } instFilter)
        {
            entries = [.. entries.Where(e => e.InstitutionId == instFilter)];
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            var normalizedCountry = country.Trim().ToUpperInvariant();
            entries = [.. entries.Where(e => string.Equals(e.Country, normalizedCountry, StringComparison.OrdinalIgnoreCase))];
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            entries.Select(e => NodeRef.For(e.SiteId)),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<KernelSiteSummaryResponse>>(
            [.. entries.Where(e => masks.For(NodeRef.For(e.SiteId)) is null).Select(KernelSiteSummaryResponse.From)]);
    }

    internal static async Task<Results<Created<KernelSiteSummaryResponse>, ProblemHttpResult>> CreateSiteAsync(
        KernelCreateSiteRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid site request", "Provide site_code, name, country, and institution details.");
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var siteCode = (request.SiteCode ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim();
        var country = (request.Country ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(siteCode))
        {
            return KernelErrorEndpoints.Problem(StudyErrors.CodeRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return KernelErrorEndpoints.Problem(StudyErrors.NameRequired);
        }

        if (country.Length != 2 || !char.IsAsciiLetterUpper(country[0]) || !char.IsAsciiLetterUpper(country[1]))
        {
            return KernelErrorEndpoints.Problem(StudyErrors.CountryInvalid);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (!await IsPermittedAsync(tenant, typeof(RegisterSite), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        var institutionResult = await ResolveOrRegisterOrganizationAsync(
            tenant,
            request.InstitutionRef,
            request.InstitutionCode,
            request.InstitutionName,
            OrganizationKind.SiteInstitution,
            idempotencyKey,
            reason,
            directory,
            sender,
            cancellationToken).ConfigureAwait(false);
        if (institutionResult.Problem is { } instProblem)
        {
            return instProblem;
        }

        var institution = institutionResult.Organization!;
        var derivedSiteId = SiteId.FromGuid(DeriveGuid(tenant, "site", $"{institution.OrganizationId}:{idempotencyKey}"));
        var existingByCode = await directory.FindSiteByCodeAsync(tenant, siteCode, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existingByCode is not null && existingByCode.SiteId != derivedSiteId)
        {
            return KernelErrorEndpoints.Problem(StudyErrors.SiteCodeTaken);
        }

        var registerResult = await sender.SendAsync(
            new RegisterSite(tenant, derivedSiteId, institution.OrganizationId, siteCode, name, country, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!registerResult.IsSuccess || registerResult.Value.IsEmpty)
        {
            return KernelErrorEndpoints.Problem(registerResult.Error!);
        }

        var persisted = await directory.FindSiteAsync(tenant, registerResult.Value, cancellationToken).ConfigureAwait(false)
            ?? new SiteDirectoryEntry(
                registerResult.Value,
                1,
                institution.OrganizationId,
                institution.VersionIndex,
                institution.Code,
                institution.Name,
                siteCode,
                name,
                country);

        await SynchronizeCallerSiteScopeAsync(context, tenant, persisted.SiteCode, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(persisted.SiteId)}",
            KernelSiteSummaryResponse.From(persisted));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelStudySiteSummaryResponse>>, ProblemHttpResult>> ListStudySitesAsync(
        string studyRef,
        HttpContext context,
        IStudyDirectory directory,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken,
        DateTimeOffset? valid_at = null,
        DateTimeOffset? known_at = null)
    {
        var tenant = KernelRequestTenant.Get(context);
        var studyLookup = await ResolveTargetStudyAsync(tenant, studyRef, directory, cancellationToken).ConfigureAwait(false);
        if (studyLookup.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var study = studyLookup.Study!;
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [NodeRef.For(study.StudyId)], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(NodeRef.For(study.StudyId)) is { } mask)
        {
            return KernelProblems.NotVisible($"You may not access study '{study.StudyCode}' ({mask}).");
        }

        var entries = await directory.ListStudySitesAsync(tenant, study.StudyId, cancellationToken).ConfigureAwait(false);
        var siteMasks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            entries.Select(e => NodeRef.For(e.StudySiteId)),
            cancellationToken).ConfigureAwait(false);

        Dictionary<StudySiteId, StudySiteGovernanceSummary>? governanceBySiteId = null;
        if (context.RequestServices.GetService<IStudyGovernanceReader>() is { } governanceReader)
        {
            var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
            var now = clock.UtcNow;
            var govRows = await governanceReader.ListStudySitesAsync(
                tenant,
                study.StudyId,
                valid_at ?? now,
                known_at ?? now,
                cancellationToken).ConfigureAwait(false);
            governanceBySiteId = govRows.ToDictionary(s => s.StudySiteId);
        }

        return TypedResults.Ok<IReadOnlyList<KernelStudySiteSummaryResponse>>(
            [
                .. entries
                    .Where(e => siteMasks.For(NodeRef.For(e.StudySiteId)) is null)
                    .Select(e => KernelStudySiteSummaryResponse.From(e, governanceBySiteId?.GetValueOrDefault(e.StudySiteId))),
            ]);
    }

    internal static async Task<Results<Created<KernelStudySiteSummaryResponse>, ProblemHttpResult>> LinkSiteToStudyAsync(
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
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid study-site request", "Provide site_ref or site_code to link to the study.");
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        var studyLookup = await ResolveTargetStudyAsync(tenant, studyRef, directory, cancellationToken).ConfigureAwait(false);
        if (studyLookup.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var study = studyLookup.Study!;
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [NodeRef.For(study.StudyId)], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(NodeRef.For(study.StudyId)) is { } mask)
        {
            return KernelProblems.NotVisible($"You may not access study '{study.StudyCode}' ({mask}).");
        }

        if (!await IsPermittedAsync(tenant, typeof(LinkSiteToStudy), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        var siteLookup = await ResolveTargetSiteAsync(
            tenant,
            request.SiteRef,
            request.SiteCode,
            request.InstitutionRef,
            request.InstitutionCode,
            directory,
            cancellationToken).ConfigureAwait(false);
        SiteDirectoryEntry site;
        if (siteLookup.Site is { } existingSite)
        {
            site = existingSite;
        }
        else if (string.IsNullOrWhiteSpace(request.SiteRef)
            && !string.IsNullOrWhiteSpace(request.SiteCode)
            && (!string.IsNullOrWhiteSpace(request.SiteName)
                || !string.IsNullOrWhiteSpace(request.Country)
                || !string.IsNullOrWhiteSpace(request.InstitutionRef)
                || request.SiteId is not null))
        {
            var inlineSite = await RegisterInlineSiteAsync(
                tenant,
                study,
                request,
                idempotencyKey,
                reason,
                directory,
                sender,
                probe,
                catalog,
                cancellationToken).ConfigureAwait(false);
            if (inlineSite.Problem is { } inlineProblem)
            {
                return inlineProblem;
            }

            site = inlineSite.Site!;
        }
        else
        {
            return siteLookup.Problem!;
        }

        var derivedStudySiteId = request.StudySiteId is { } givenStudySite && givenStudySite != Guid.Empty
            ? StudySiteId.FromGuid(givenStudySite)
            : StudySiteId.FromGuid(DeriveGuid(tenant, "study-site", $"{study.StudyId}:{site.SiteId}:{idempotencyKey}"));
        var linkResult = await sender.SendAsync(
            new LinkSiteToStudy(tenant, derivedStudySiteId, study.StudyId, site.SiteId, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!linkResult.IsSuccess || linkResult.Value.IsEmpty)
        {
            return KernelErrorEndpoints.Problem(linkResult.Error!);
        }

        var persisted = await directory.FindStudySiteAsync(tenant, linkResult.Value, cancellationToken).ConfigureAwait(false)
            ?? new StudySiteDirectoryEntry(
                linkResult.Value,
                1,
                study.StudyId,
                site.SiteId,
                site.VersionIndex,
                site.SiteCode,
                site.Name,
                site.Country,
                site.InstitutionId,
                site.InstitutionVersionIndex,
                site.InstitutionCode,
                site.InstitutionName,
                "Selected",
                study.StudyCode,
                study.VersionIndex,
                true);

        await SynchronizeCallerSiteScopeAsync(context, tenant, persisted.SiteCode, cancellationToken).ConfigureAwait(false);

        StudySiteGovernanceSummary? governance = null;
        if (context.RequestServices.GetService<IStudyGovernanceReader>() is { } governanceReader)
        {
            var clock = context.RequestServices.GetService<IClock>() ?? SystemClock.Instance;
            var now = clock.UtcNow;
            governance = await governanceReader.FindStudySiteAsync(tenant, linkResult.Value, now, now, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(persisted.StudySiteId)}",
            KernelStudySiteSummaryResponse.From(persisted, governance));
    }

    internal static async Task<Results<Created<KernelStudySummaryResponse>, ProblemHttpResult>> CreateStudyAsync(
        KernelCreateStudyRequest? request,
        HttpContext context,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelProblems.BadRequest("Invalid study request", "Provide study_code, title, phase, and sponsor details.");
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var studyCode = (request.StudyCode ?? string.Empty).Trim();
        var title = (request.Title ?? string.Empty).Trim();
        var phase = (request.Phase ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return KernelErrorEndpoints.Problem(StudyErrors.CodeRequired);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return KernelErrorEndpoints.Problem(StudyErrors.TitleRequired);
        }

        if (string.IsNullOrWhiteSpace(phase))
        {
            return TypedResults.Problem(
                title: "Phase required",
                detail: "A clinical phase (for example Phase 1, Phase 2, or Phase 3) is required.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (catalog.Find(typeof(RegisterStudy)) is { } registerEntry)
        {
            var canRegister = await probe.CanIAsync(tenant, registerEntry.Capability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false);
            if (!canRegister.Permitted)
            {
                return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
            }
        }

        var sponsorResult = await ResolveOrRegisterOrganizationAsync(
            tenant,
            request.SponsorRef,
            request.SponsorCode,
            request.SponsorName,
            OrganizationKind.Sponsor,
            idempotencyKey,
            reason,
            directory,
            sender,
            cancellationToken).ConfigureAwait(false);
        if (sponsorResult.Problem is { } sponsorProblem)
        {
            return sponsorProblem;
        }

        var sponsor = sponsorResult.Organization!;
        var derivedStudyId = StudyId.FromGuid(DeriveGuid(tenant, "study", idempotencyKey));
        var registerResult = await sender.SendAsync(
            new RegisterStudy(tenant, derivedStudyId, sponsor.OrganizationId, studyCode, title, phase, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!registerResult.IsSuccess || registerResult.Value is var createdId && createdId.IsEmpty)
        {
            return KernelErrorEndpoints.Problem(registerResult.Error!);
        }

        KernelStudySiteSummaryResponse? linkedSiteSummary = null;
        if (request.LinkSandboxSite
            || !string.IsNullOrWhiteSpace(request.InitialSiteRef)
            || !string.IsNullOrWhiteSpace(request.InitialSiteCode))
        {
            var initialLink = await LinkInitialSiteToStudyAsync(
                tenant,
                registerResult.Value,
                request,
                idempotencyKey,
                reason,
                directory,
                sender,
                probe,
                catalog,
                cancellationToken).ConfigureAwait(false);
            if (initialLink.Problem is { } initialLinkProblem)
            {
                return initialLinkProblem;
            }

            linkedSiteSummary = initialLink.LinkedSite;
        }

        var persisted = await directory.FindStudyAsync(tenant, registerResult.Value, cancellationToken).ConfigureAwait(false)
            ?? new StudyDirectoryEntry(
                registerResult.Value,
                1,
                studyCode,
                title,
                phase,
                sponsor.OrganizationId,
                sponsor.VersionIndex,
                sponsor.Code,
                sponsor.Name,
                linkedSiteSummary is not null ? 1 : 0);

        await SynchronizeCallerStudyScopeAsync(context, tenant, persisted.StudyCode, cancellationToken).ConfigureAwait(false);
        if (linkedSiteSummary is not null)
        {
            await SynchronizeCallerSiteScopeAsync(context, tenant, linkedSiteSummary.SiteCode, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(persisted.StudyId)}",
            KernelStudySummaryResponse.From(persisted, linkedSiteSummary));
    }

    internal static Ok<IReadOnlyList<KernelProtocolTemplateResponse>> ListProtocolTemplates(HttpContext context)
    {
        var importer = context.RequestServices.GetService<IProtocolDesignImporter>();
        if (importer is null)
        {
            return TypedResults.Ok<IReadOnlyList<KernelProtocolTemplateResponse>>([]);
        }

        return TypedResults.Ok<IReadOnlyList<KernelProtocolTemplateResponse>>(
            [.. importer.ListBundledTemplates().Select(KernelProtocolTemplateResponse.From)]);
    }

    internal static async Task<Results<Ok<KernelProtocolDraftSummaryResponse>, ProblemHttpResult>> GetProtocolDesignSummaryAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(versionRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var tenant = KernelRequestTenant.Get(context);
        var versionId = ProtocolVersionId.FromGuid(node.Id);
        if (await reader.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(node);
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [NodeRef.For(version.Id)], cancellationToken).ConfigureAwait(false);
        if (masks.For(NodeRef.For(version.Id)) is { } mask)
        {
            return KernelProblems.NotVisible($"You can't see this protocol version ({mask}), so its design summary is refused.");
        }

        var design = await reader.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        var amendment = await reader.GetAmendmentAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        var labels = await ResolveConceptLabelsAsync(
            tenant,
            design,
            context.RequestServices.GetService<IConceptRegistry>(),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(KernelProtocolDraftSummaryResponse.From(version, design, labels, amendment: amendment));
    }

    internal static async Task<Results<Created<KernelProtocolDraftSummaryResponse>, Ok<KernelProtocolDraftSummaryResponse>, ProblemHttpResult>> ImportProtocolDraftAsync(
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
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > (IProtocolDesignImporter.MaxPayloadBytes + 262_144))
        {
            return PayloadTooLargeProblem();
        }

        var reason = (changeReason.Read(context) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var idempotencyKey = IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        var studyLookup = await ResolveTargetStudyAsync(tenant, studyRef, directory, cancellationToken).ConfigureAwait(false);
        if (studyLookup.Problem is { } studyProblem)
        {
            return studyProblem;
        }

        var study = studyLookup.Study!;
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [NodeRef.For(study.StudyId)], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(NodeRef.For(study.StudyId)) is { } mask)
        {
            return KernelProblems.NotVisible($"You may not access study '{study.StudyCode}' ({mask}).");
        }

        var parsedPayload = await ReadImportInputAsync(context, cancellationToken).ConfigureAwait(false);
        if (parsedPayload.Problem is { } payloadProblem)
        {
            return payloadProblem;
        }

        var existingVersions = await versionReader.ListForStudyAsync(tenant, study.StudyId, cancellationToken).ConfigureAwait(false);
        var derivedVersionId = ProtocolVersionId.FromGuid(DeriveGuid(tenant, "protocol-version", $"{study.StudyId}:{idempotencyKey}"));
        var plan = await PlanDraftMutationAsync(
            tenant,
            study,
            existingVersions,
            derivedVersionId,
            versionReader,
            probe,
            catalog,
            cancellationToken).ConfigureAwait(false);
        if (plan.Problem is { } planProblem)
        {
            return planProblem;
        }

        var resolvedConceptsByKey = new Dictionary<string, ProtocolConceptResolution>(StringComparer.Ordinal);
        var stagedConcepts = new List<StagedProtocolConcept>();
        var stagedBiomedicalByCode = new Dictionary<string, ConceptRef>(StringComparer.Ordinal);
        var stagedByConceptId = new Dictionary<ConceptId, ConceptRef>();
        var stagedLabelsByRef = new Dictionary<ConceptRef, string>();
        var resolvedTerminologyByKey = new Dictionary<string, ProtocolTerminologyResolution>(StringComparer.Ordinal);
        var stagedCodeSystemsById = new Dictionary<CodeSystemId, StagedCodeSystem>();
        var stagedCodeSystemVersionsById = new Dictionary<CodeSystemVersionId, StagedCodeSystemVersion>();

        async ValueTask<ProtocolConceptResolution> ResolveOrDefineConceptAsync(
            ProtocolConceptCandidate candidate,
            CancellationToken ct)
        {
            if (resolvedConceptsByKey.TryGetValue(candidate.StableKey, out var cached))
            {
                return cached;
            }

            var label = string.IsNullOrWhiteSpace(candidate.PreferredLabel) ? "Protocol Item" : candidate.PreferredLabel.Trim();
            var definition = string.IsNullOrWhiteSpace(candidate.Definition) ? label : candidate.Definition.Trim();

            if (!string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode))
            {
                var bcCode = candidate.BiomedicalConceptCode.Trim();
                if (stagedBiomedicalByCode.TryGetValue(bcCode, out var stagedBcRef))
                {
                    var stagedHit = new ProtocolConceptResolution(stagedBcRef, UsedFallbackSponsorConcept: false);
                    resolvedConceptsByKey[candidate.StableKey] = stagedHit;
                    return stagedHit;
                }

                var foundBc = await concepts.FindBiomedicalConceptAsync(tenant, bcCode, ct).ConfigureAwait(false);
                if (foundBc is not null && foundBc.Status != ConceptStatus.Deprecated)
                {
                    var hit = new ProtocolConceptResolution(foundBc.Concept, UsedFallbackSponsorConcept: false);
                    resolvedConceptsByKey[candidate.StableKey] = hit;
                    return hit;
                }

                if (foundBc is null)
                {
                    var bcConceptId = ConceptId.FromGuid(DeriveGuid(tenant, "biomedical-concept", bcCode));
                    var existingByBcId = await concepts.GetCurrentAsync(tenant, bcConceptId, ct).ConfigureAwait(false);
                    if (existingByBcId is not null
                        && existingByBcId.Status != ConceptStatus.Deprecated
                        && existingByBcId.Origin == ConceptOrigin.CdiscBiomedicalConcept
                        && string.Equals(existingByBcId.ExternalCode, bcCode, StringComparison.Ordinal))
                    {
                        var reusedBc = new ProtocolConceptResolution(existingByBcId.Concept, UsedFallbackSponsorConcept: false);
                        resolvedConceptsByKey[candidate.StableKey] = reusedBc;
                        return reusedBc;
                    }

                    if (existingByBcId is null)
                    {
                        var newBcRef = new ConceptRef(bcConceptId, 1);
                        stagedConcepts.Add(new StagedProtocolConcept(
                            bcConceptId,
                            ConceptOrigin.CdiscBiomedicalConcept,
                            bcCode,
                            ConceptKind.BiomedicalConcept,
                            label,
                            definition));
                        stagedBiomedicalByCode[bcCode] = newBcRef;
                        stagedByConceptId[bcConceptId] = newBcRef;
                        stagedLabelsByRef[newBcRef] = label;

                        var stagedResolution = new ProtocolConceptResolution(newBcRef, UsedFallbackSponsorConcept: false);
                        resolvedConceptsByKey[candidate.StableKey] = stagedResolution;
                        return stagedResolution;
                    }
                }
            }

            var usedFallback = !string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode);
            var conceptId = ConceptId.FromGuid(DeriveGuid(tenant, "import-concept", candidate.StableKey));
            if (stagedByConceptId.TryGetValue(conceptId, out var alreadyStagedSponsor))
            {
                var cachedSponsor = new ProtocolConceptResolution(alreadyStagedSponsor, UsedFallbackSponsorConcept: usedFallback);
                resolvedConceptsByKey[candidate.StableKey] = cachedSponsor;
                return cachedSponsor;
            }

            var existingConcept = await concepts.GetCurrentAsync(tenant, conceptId, ct).ConfigureAwait(false);
            if (existingConcept is not null && existingConcept.Status != ConceptStatus.Deprecated)
            {
                var reused = new ProtocolConceptResolution(existingConcept.Concept, UsedFallbackSponsorConcept: usedFallback);
                resolvedConceptsByKey[candidate.StableKey] = reused;
                return reused;
            }

            var targetSponsorId = existingConcept is null
                ? conceptId
                : ConceptId.FromGuid(DeriveGuid(tenant, "import-concept-active", candidate.StableKey));
            var sponsorRef = new ConceptRef(targetSponsorId, 1);
            if (stagedByConceptId.TryAdd(targetSponsorId, sponsorRef))
            {
                stagedConcepts.Add(new StagedProtocolConcept(
                    targetSponsorId,
                    ConceptOrigin.Sponsor,
                    ExternalCode: null,
                    candidate.Kind,
                    label,
                    definition));
                stagedLabelsByRef[sponsorRef] = label;
            }

            var created = new ProtocolConceptResolution(sponsorRef, UsedFallbackSponsorConcept: usedFallback);
            resolvedConceptsByKey[candidate.StableKey] = created;
            return created;
        }

        async ValueTask<ProtocolTerminologyResolution> ResolveOrRegisterTerminologyAsync(
            ProtocolTerminologyCandidate candidate,
            CancellationToken ct)
        {
            var boundedGroup = BoundTerminologyToken(candidate.CodelistCode);
            var normalizedEntries = NormalizeTerminologyEntries(candidate.Entries, boundedGroup);

            if (!string.IsNullOrWhiteSpace(candidate.CodeSystemUri) && !string.IsNullOrWhiteSpace(candidate.CodeSystemVersion))
            {
                foreach (var candidateUri in CandidateCodeSystemUris(candidate.CodeSystemUri))
                {
                    var preExisting = await concepts.FindCodeSystemVersionAsync(tenant, candidateUri, candidate.CodeSystemVersion.Trim(), ct).ConfigureAwait(false);
                    if (preExisting is null)
                    {
                        continue;
                    }

                    if (normalizedEntries.Count == 0)
                    {
                        if (boundedGroup is not null)
                        {
                            var emptyCheckGroup = await concepts.GetGroupAsync(tenant, preExisting.Id, boundedGroup, ct).ConfigureAwait(false);
                            if (emptyCheckGroup.Count > 0)
                            {
                                return new ProtocolTerminologyResolution(new TerminologyBinding(preExisting.Id, boundedGroup));
                            }
                        }

                        return new ProtocolTerminologyResolution(new TerminologyBinding(preExisting.Id));
                    }

                    if (boundedGroup is not null)
                    {
                        var existingGroup = await concepts.GetGroupAsync(tenant, preExisting.Id, boundedGroup, ct).ConfigureAwait(false);
                        var groupCodes = existingGroup.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
                        if (existingGroup.Count == normalizedEntries.Count
                            && normalizedEntries.All(e => groupCodes.Contains(e.Code)))
                        {
                            return new ProtocolTerminologyResolution(new TerminologyBinding(preExisting.Id, boundedGroup));
                        }
                    }
                }
            }

            if (normalizedEntries.Count == 0)
            {
                return new ProtocolTerminologyResolution(null);
            }

            var effectiveGroup = boundedGroup ?? BoundTerminologyToken(candidate.SourceKey) ?? "codelist";
            var groupedEntries = normalizedEntries
                .Select(e => e.GroupCode == effectiveGroup ? e : e with { GroupCode = effectiveGroup })
                .ToList();

            var licence = InferLicenceClass(candidate.CodeSystemUri);
            var systemUri = licence switch
            {
                LicenseClass.Licensed => "urn:cadence:terminology:protocol-codelists:licensed",
                LicenseClass.AcceptedTerms => "urn:cadence:terminology:protocol-codelists:accepted-terms",
                _ => "urn:cadence:terminology:protocol-codelists",
            };

            var packageBytes = SerializeTerminologyEntries(groupedEntries);
            var digest = Convert.ToHexStringLower(SHA256.HashData(packageBytes));
            var versionPrefix = SanitizeVersionPrefix(effectiveGroup);
            var versionLabel = $"{versionPrefix}-{digest[..16]}";
            var cacheKey = $"{systemUri}|{versionLabel}|{effectiveGroup}";
            if (resolvedTerminologyByKey.TryGetValue(cacheKey, out var cachedTerminology))
            {
                return cachedTerminology;
            }

            var existingPinned = await concepts.FindCodeSystemVersionAsync(tenant, systemUri, versionLabel, ct).ConfigureAwait(false);
            if (existingPinned is not null)
            {
                var reusedBinding = new ProtocolTerminologyResolution(new TerminologyBinding(existingPinned.Id, effectiveGroup));
                resolvedTerminologyByKey[cacheKey] = reusedBinding;
                return reusedBinding;
            }

            var codeSystemId = CodeSystemId.FromGuid(DeriveGuid(tenant, "import-codesystem", systemUri));
            stagedCodeSystemsById.TryAdd(codeSystemId, new StagedCodeSystem(codeSystemId, systemUri, licence));

            var versionId = CodeSystemVersionId.FromGuid(DeriveGuid(tenant, "import-codesystem-version", $"{systemUri}:{versionLabel}"));
            var releasedOn = !string.IsNullOrWhiteSpace(candidate.CodeSystemVersion)
                && DateOnly.TryParseExact(candidate.CodeSystemVersion.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedRelease)
                    ? parsedRelease
                    : new DateOnly(2026, 1, 1);
            stagedCodeSystemVersionsById.TryAdd(
                versionId,
                new StagedCodeSystemVersion(
                    versionId,
                    codeSystemId,
                    systemUri,
                    versionLabel,
                    releasedOn,
                    new TerminologyPackage(
                        digest,
                        $"{versionPrefix}.json",
                        systemUri,
                        packageBytes.LongLength,
                        "application/vnd.cadence.codelist+json"),
                    groupedEntries));

            var createdBinding = new ProtocolTerminologyResolution(new TerminologyBinding(versionId, effectiveGroup));
            resolvedTerminologyByKey[cacheKey] = createdBinding;
            return createdBinding;
        }

        var importOutcome = await importer.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: parsedPayload.RawBytes,
                FileName: parsedPayload.FileName,
                Format: parsedPayload.Format,
                TemplateIds: parsedPayload.TemplateIds,
                BaseDesign: plan.BaseDesign,
                FallbackTitle: study.Title,
                ReplaceForms: parsedPayload.ReplaceForms),
            ResolveOrDefineConceptAsync,
            cancellationToken,
            ResolveOrRegisterTerminologyAsync).ConfigureAwait(false);

        if (importOutcome.PayloadTooLarge)
        {
            return PayloadTooLargeProblem(importOutcome.Problems.FirstOrDefault());
        }

        if (!importOutcome.IsSuccess || importOutcome.Design is null)
        {
            return ValidationFailedProblem(importOutcome.Problems);
        }

        if (stagedConcepts.Count > 0
            && !await IsPermittedAsync(tenant, typeof(DefineConcept), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        if (stagedCodeSystemsById.Count > 0
            && !await IsPermittedAsync(tenant, typeof(RegisterCodeSystem), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        foreach (var targetCodeSystemId in stagedCodeSystemVersionsById.Values.Select(v => v.CodeSystemId).Distinct())
        {
            if (!await IsPermittedAsync(tenant, typeof(ImportCodeSystemVersion), NodeRef.For(targetCodeSystemId), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
            }
        }

        if (importOutcome.RulePack is not null
            && !await IsPermittedAsync(tenant, typeof(DraftLibraryItem), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        foreach (var stagedCodeSystem in stagedCodeSystemsById.Values)
        {
            var registered = await sender.SendAsync(
                new RegisterCodeSystem(
                    tenant,
                    stagedCodeSystem.CodeSystemId,
                    stagedCodeSystem.SystemUri,
                    "Protocol Imported Codelists",
                    "Cadence Protocol Import",
                    stagedCodeSystem.License,
                    DeriveSubIdempotencyKey(idempotencyKey, "codesystem", stagedCodeSystem.CodeSystemId.Value),
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!registered.IsSuccess && !string.Equals(registered.Error?.Code, KernelErrors.AlreadyExists.Code, StringComparison.Ordinal))
            {
                return KernelErrorEndpoints.Problem(registered.Error!);
            }
        }

        foreach (var stagedVersion in stagedCodeSystemVersionsById.Values)
        {
            var imported = await sender.SendAsync(
                new ImportCodeSystemVersion(
                    tenant,
                    stagedVersion.VersionId,
                    stagedVersion.CodeSystemId,
                    stagedVersion.VersionLabel,
                    stagedVersion.ReleasedOn,
                    stagedVersion.Package,
                    stagedVersion.Entries,
                    DeriveSubIdempotencyKey(idempotencyKey, "codesystem-version", stagedVersion.VersionId.Value),
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!imported.IsSuccess && !string.Equals(imported.Error?.Code, KernelErrors.AlreadyExists.Code, StringComparison.Ordinal))
            {
                return KernelErrorEndpoints.Problem(imported.Error!);
            }
        }

        var serializedDesign = ProtocolDesignDocument.Write(importOutcome.Design);
        var amendment = BuildProtocolAmendment(
            plan,
            importOutcome.Design,
            serializedDesign,
            reason,
            parsedPayload.AmendmentClassification,
            parsedPayload.TransitionPolicy);

        var requestFingerprint = ComputeImportRequestFingerprint(study.StudyId, parsedPayload);
        var persistResult = await sender.SendAsync(
            new ImportProtocolDraft(
                tenant,
                study.StudyId,
                plan.Mode,
                plan.TargetVersionId,
                plan.ParentVersionId,
                serializedDesign,
                amendment,
                stagedConcepts,
                idempotencyKey,
                reason,
                requestFingerprint),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!persistResult.IsSuccess || persistResult.Value is null)
        {
            return KernelErrorEndpoints.Problem(persistResult.Error!);
        }

        var persistedState = persistResult.Value;
        var persistedSummary = await versionReader.GetAsync(tenant, persistedState.ProtocolVersionId, cancellationToken).ConfigureAwait(false)
            ?? new ProtocolVersionSummary(
                persistedState.ProtocolVersionId,
                persistedState.StudyId,
                persistedState.Number,
                plan.ParentVersionId,
                persistedState.Status,
                persistedState.ContentHash,
                amendment?.Intent ?? plan.AmendmentIntent,
                null,
                null);
        var effectiveDesign = persistResult.Replayed
            ? await versionReader.GetDesignAsync(tenant, persistedState.ProtocolVersionId, cancellationToken).ConfigureAwait(false) ?? importOutcome.Design
            : importOutcome.Design;
        var effectiveAmendment = persistResult.Replayed
            ? await versionReader.GetAmendmentAsync(tenant, persistedState.ProtocolVersionId, cancellationToken).ConfigureAwait(false) ?? amendment
            : amendment;

        KernelImportedRulePackSummaryResponse? draftedRulePack = null;
        if (importOutcome.RulePack is { } rulePack)
        {
            var libraryItemId = LibraryItemId.FromGuid(DeriveGuid(tenant, "import-rule-pack", $"{study.StudyId}:{idempotencyKey}"));
            var draftItemResult = await sender.SendAsync(
                new DraftLibraryItem(
                    tenant,
                    libraryItemId,
                    LibraryItemKind.RulePack,
                    rulePack.Key,
                    rulePack.Title,
                    rulePack.Content,
                    [],
                    $"{idempotencyKey}:rule-pack",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!draftItemResult.IsSuccess)
            {
                return KernelErrorEndpoints.Problem(draftItemResult.Error!);
            }

            draftedRulePack = KernelImportedRulePackSummaryResponse.From(draftItemResult.Value!, rulePack.Title, rulePack.RuleCount);
        }

        var conceptLabels = await ResolveConceptLabelsAsync(
            tenant,
            effectiveDesign,
            concepts,
            stagedLabelsByRef,
            cancellationToken).ConfigureAwait(false);
        var responseBody = KernelProtocolDraftSummaryResponse.From(
            persistedSummary,
            effectiveDesign,
            conceptLabels,
            draftedRulePack,
            effectiveAmendment);

        return plan.CreatedNewVersion
            ? TypedResults.Created(
                $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/protocol-versions/{NodeRef.For(persistedSummary.Id)}",
                responseBody)
            : TypedResults.Ok(responseBody);
    }

    private static async Task<(OrganizationDirectoryEntry? Organization, ProblemHttpResult? Problem)> ResolveOrRegisterOrganizationAsync(
        TenantId tenant,
        string? organizationRef,
        string? organizationCode,
        string? organizationName,
        OrganizationKind expectedKind,
        string idempotencyKey,
        string reason,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        CancellationToken cancellationToken)
    {
        bool isSiteInstitution = expectedKind == OrganizationKind.SiteInstitution;
        var unknownError = isSiteInstitution ? StudyErrors.InstitutionUnknown : StudyErrors.SponsorUnknown;

        if (!string.IsNullOrWhiteSpace(organizationRef))
        {
            if (!NodeRef.TryParse(organizationRef.Trim(), out var orgNode))
            {
                return (null, KernelProblems.InvalidNodeRef(organizationRef));
            }

            if (orgNode.Kind != NodeKinds.Organization)
            {
                return (null, KernelProblems.WrongKind(orgNode, NodeKinds.Organization));
            }

            var foundById = await directory.FindOrganizationAsync(tenant, OrganizationId.FromGuid(orgNode.Id), cancellationToken).ConfigureAwait(false);
            if (foundById is null)
            {
                return (null, KernelErrorEndpoints.Problem(unknownError));
            }

            if (foundById.Kind != expectedKind)
            {
                return (null, TypedResults.Problem(
                    title: isSiteInstitution ? "Invalid site institution" : "Invalid sponsor organization",
                    detail: $"Organization '{foundById.Code}' has kind '{foundById.Kind}', expected '{expectedKind}'.",
                    statusCode: StatusCodes.Status422UnprocessableEntity));
            }

            return (foundById, null);
        }

        var rawCode = (organizationCode ?? string.Empty).Trim();
        var rawName = (organizationName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawCode) && string.IsNullOrWhiteSpace(rawName))
        {
            return (null, TypedResults.Problem(
                title: isSiteInstitution ? "Site institution required" : "Sponsor required",
                detail: isSiteInstitution
                    ? "Select an existing site institution organization or provide an institution name/code."
                    : "Select an existing sponsor organization or provide a sponsor name/code.",
                statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        var defaultFallbackCode = isSiteInstitution ? "SITE-INSTITUTION" : "SPONSOR";
        var normalizedCode = !string.IsNullOrWhiteSpace(rawCode)
            ? NormalizeOrganizationCode(rawCode, defaultFallbackCode)
            : NormalizeOrganizationCode(rawName, defaultFallbackCode);
        var displayName = !string.IsNullOrWhiteSpace(rawName) ? rawName : rawCode;

        var existingByCode = await directory.FindOrganizationByCodeAsync(tenant, normalizedCode, cancellationToken).ConfigureAwait(false);
        if (existingByCode is not null)
        {
            if (existingByCode.Kind == expectedKind)
            {
                return (existingByCode, null);
            }

            if (!string.IsNullOrWhiteSpace(rawCode))
            {
                return (null, TypedResults.Problem(
                    title: "Organization kind mismatch",
                    detail: $"Organization code '{existingByCode.Code}' is already registered as '{existingByCode.Kind}', expected '{expectedKind}'.",
                    statusCode: StatusCodes.Status409Conflict));
            }

            // When only a display name was given (e.g. trial founder enters the same institution name as their sponsor),
            // disambiguate the derived code so the site institution is registered with OrganizationKind.SiteInstitution.
            var suffix = isSiteInstitution ? "-SITE" : "-SPONSOR";
            var maxPrefix = 48 - suffix.Length;
            normalizedCode = $"{normalizedCode[..Math.Min(normalizedCode.Length, maxPrefix)].TrimEnd('-')}{suffix}";
            var disambiguatedExisting = await directory.FindOrganizationByCodeAsync(tenant, normalizedCode, cancellationToken).ConfigureAwait(false);
            if (disambiguatedExisting is not null && disambiguatedExisting.Kind == expectedKind)
            {
                return (disambiguatedExisting, null);
            }
        }

        var deriveSalt = isSiteInstitution ? "site-institution-org" : "sponsor-org";
        var derivedOrgId = OrganizationId.FromGuid(DeriveGuid(tenant, deriveSalt, normalizedCode));
        var regOrg = await sender.SendAsync(
            new RegisterOrganization(
                tenant,
                derivedOrgId,
                normalizedCode,
                displayName,
                expectedKind,
                $"{idempotencyKey}:org:{expectedKind}:{normalizedCode}",
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!regOrg.IsSuccess)
        {
            var recheck = await directory.FindOrganizationByCodeAsync(tenant, normalizedCode, cancellationToken).ConfigureAwait(false);
            if (recheck is not null && recheck.Kind == expectedKind)
            {
                return (recheck, null);
            }

            return (null, KernelErrorEndpoints.Problem(regOrg.Error!));
        }

        var createdOrg = await directory.FindOrganizationAsync(tenant, regOrg.Value, cancellationToken).ConfigureAwait(false)
            ?? new OrganizationDirectoryEntry(regOrg.Value, 1, normalizedCode, displayName, expectedKind);
        return (createdOrg, null);
    }

    private static async Task<(SiteDirectoryEntry? Site, ProblemHttpResult? Problem)> ResolveTargetSiteAsync(
        TenantId tenant,
        string? siteRef,
        string? siteCode,
        string? institutionRef,
        string? institutionCode,
        IStudyDirectory directory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(siteRef))
        {
            var trimmedRef = siteRef.Trim();
            if (!NodeRef.TryParse(trimmedRef, out var siteNode))
            {
                return (null, KernelProblems.InvalidNodeRef(trimmedRef));
            }

            if (siteNode.Kind != NodeKinds.Site)
            {
                return (null, KernelProblems.WrongKind(siteNode, NodeKinds.Site));
            }

            var foundById = await directory.FindSiteAsync(tenant, SiteId.FromGuid(siteNode.Id), cancellationToken).ConfigureAwait(false);
            return foundById is not null
                ? (foundById, null)
                : (null, KernelErrorEndpoints.Problem(StudyErrors.SiteUnknown));
        }

        var rawSiteCode = (siteCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawSiteCode))
        {
            return (null, KernelProblems.BadRequest("Site required", "Provide site_ref or site_code to link a site to the study."));
        }

        OrganizationId? institutionFilter = null;
        if (!string.IsNullOrWhiteSpace(institutionRef))
        {
            if (!NodeRef.TryParse(institutionRef.Trim(), out var instNode))
            {
                return (null, KernelProblems.InvalidNodeRef(institutionRef));
            }

            if (instNode.Kind != NodeKinds.Organization)
            {
                return (null, KernelProblems.WrongKind(instNode, NodeKinds.Organization));
            }

            institutionFilter = OrganizationId.FromGuid(instNode.Id);
        }
        else if (!string.IsNullOrWhiteSpace(institutionCode))
        {
            var inst = await directory.FindOrganizationByCodeAsync(tenant, institutionCode.Trim(), cancellationToken).ConfigureAwait(false);
            if (inst is null)
            {
                return (null, KernelErrorEndpoints.Problem(StudyErrors.InstitutionUnknown));
            }

            institutionFilter = inst.OrganizationId;
        }

        var foundByCode = await directory.FindSiteByCodeAsync(tenant, rawSiteCode, institutionFilter, cancellationToken).ConfigureAwait(false);
        return foundByCode is not null
            ? (foundByCode, null)
            : (null, KernelErrorEndpoints.Problem(StudyErrors.SiteUnknown));
    }

    private static async Task<(SiteDirectoryEntry? Site, ProblemHttpResult? Problem)> RegisterInlineSiteAsync(
        TenantId tenant,
        StudyDirectoryEntry study,
        KernelLinkSiteToStudyRequest request,
        string idempotencyKey,
        string reason,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (!await IsPermittedAsync(tenant, typeof(RegisterSite), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        OrganizationDirectoryEntry institution;
        if (!string.IsNullOrWhiteSpace(request.InstitutionRef))
        {
            var rawOrg = request.InstitutionRef.Trim();
            OrganizationId orgId;
            if (NodeRef.TryParse(rawOrg, out var orgNode))
            {
                if (orgNode.Kind != NodeKinds.Organization)
                {
                    return (null, KernelProblems.WrongKind(orgNode, NodeKinds.Organization));
                }

                orgId = OrganizationId.FromGuid(orgNode.Id);
            }
            else if (Guid.TryParse(rawOrg, out var orgGuid) && orgGuid != Guid.Empty)
            {
                orgId = OrganizationId.FromGuid(orgGuid);
            }
            else
            {
                return (null, KernelProblems.InvalidNodeRef(rawOrg));
            }

            var foundOrg = await directory.FindOrganizationAsync(tenant, orgId, cancellationToken).ConfigureAwait(false);
            if (foundOrg is null)
            {
                return (null, KernelErrorEndpoints.Problem(StudyErrors.InstitutionUnknown));
            }

            institution = foundOrg;
        }
        else
        {
            institution = await directory.FindOrganizationAsync(tenant, study.SponsorId, cancellationToken).ConfigureAwait(false)
                ?? new OrganizationDirectoryEntry(study.SponsorId, study.SponsorVersionIndex, study.SponsorCode, study.SponsorName, OrganizationKind.Sponsor);
        }

        var siteCode = request.SiteCode!.Trim();
        var siteName = string.IsNullOrWhiteSpace(request.SiteName) ? siteCode : request.SiteName.Trim();
        var country = string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country.Trim().ToUpperInvariant();
        var siteId = request.SiteId is { } givenSite && givenSite != Guid.Empty
            ? SiteId.FromGuid(givenSite)
            : SiteId.FromGuid(DeriveGuid(tenant, "register-site", $"{siteCode.ToUpperInvariant()}:{idempotencyKey}"));

        var registerSiteResult = await sender.SendAsync(
            new RegisterSite(tenant, siteId, institution.OrganizationId, siteCode, siteName, country, $"{idempotencyKey}:site", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!registerSiteResult.IsSuccess && registerSiteResult.Error?.Code != KernelErrors.AlreadyExists.Code)
        {
            return (null, KernelErrorEndpoints.Problem(registerSiteResult.Error!));
        }

        var createdSite = await directory.FindSiteAsync(tenant, siteId, cancellationToken).ConfigureAwait(false)
            ?? new SiteDirectoryEntry(
                siteId,
                1,
                institution.OrganizationId,
                institution.VersionIndex,
                institution.Code,
                institution.Name,
                siteCode,
                siteName,
                country);
        return (createdSite, null);
    }

    private static async Task<(KernelStudySiteSummaryResponse? LinkedSite, ProblemHttpResult? Problem)> LinkInitialSiteToStudyAsync(
        TenantId tenant,
        StudyId studyId,
        KernelCreateStudyRequest request,
        string idempotencyKey,
        string reason,
        IStudyDirectory directory,
        IKernelCommandSender sender,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        SiteDirectoryEntry? targetSite;
        if (!string.IsNullOrWhiteSpace(request.InitialSiteRef) || !string.IsNullOrWhiteSpace(request.InitialSiteCode))
        {
            var lookup = await ResolveTargetSiteAsync(
                tenant,
                request.InitialSiteRef,
                request.InitialSiteCode,
                null,
                null,
                directory,
                cancellationToken).ConfigureAwait(false);
            if (lookup.Problem is { } lookupProblem)
            {
                return (null, lookupProblem);
            }

            targetSite = lookup.Site;
        }
        else
        {
            var sites = await directory.ListSitesAsync(tenant, cancellationToken).ConfigureAwait(false);
            targetSite = sites.FirstOrDefault();
            if (targetSite is null)
            {
                return (null, KernelErrorEndpoints.Problem(StudyErrors.SiteUnknown));
            }
        }

        if (!await IsPermittedAsync(tenant, typeof(LinkSiteToStudy), NodeRef.For(studyId), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        var derivedStudySiteId = StudySiteId.FromGuid(DeriveGuid(tenant, "study-site", $"{studyId}:{targetSite!.SiteId}:{idempotencyKey}"));
        var linkResult = await sender.SendAsync(
            new LinkSiteToStudy(tenant, derivedStudySiteId, studyId, targetSite.SiteId, $"{idempotencyKey}:study-site", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!linkResult.IsSuccess && linkResult.Error?.Code != StudyErrors.AlreadyLinked.Code && linkResult.Error?.Code != KernelErrors.AlreadyExists.Code)
        {
            return (null, KernelErrorEndpoints.Problem(linkResult.Error!));
        }

        var existingStudySites = await directory.ListStudySitesAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        var matched = existingStudySites.FirstOrDefault(ss => ss.SiteId == targetSite.SiteId)
            ?? new StudySiteDirectoryEntry(
                linkResult.IsSuccess ? linkResult.Value : derivedStudySiteId,
                1,
                studyId,
                targetSite.SiteId,
                targetSite.VersionIndex,
                targetSite.SiteCode,
                targetSite.Name,
                targetSite.Country,
                targetSite.InstitutionId,
                targetSite.InstitutionVersionIndex,
                targetSite.InstitutionCode,
                targetSite.InstitutionName,
                "Selected");

        return (KernelStudySiteSummaryResponse.From(matched), null);
    }

    private static async Task SynchronizeCallerStudyScopeAsync(
        HttpContext context,
        TenantId tenant,
        string studyCode,
        CancellationToken cancellationToken)
    {
        var synchronizer = context.RequestServices.GetService<IKernelIdentityScopeSynchronizer>();
        var currentActor = context.RequestServices.GetService<ICurrentActor>();
        if (synchronizer is null || currentActor?.Actor is not { Kind: ActorKind.Person, Id.Length: > 0 } actor)
        {
            return;
        }

        string? tenantCode = context.User.FindFirst("tenant_id")?.Value
            ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        await synchronizer.GrantStudyScopeAsync(actor, tenant, tenantCode, studyCode, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SynchronizeCallerSiteScopeAsync(
        HttpContext context,
        TenantId tenant,
        string siteCode,
        CancellationToken cancellationToken)
    {
        var synchronizer = context.RequestServices.GetService<IKernelIdentityScopeSynchronizer>();
        var currentActor = context.RequestServices.GetService<ICurrentActor>();
        if (synchronizer is null || currentActor?.Actor is not { Kind: ActorKind.Person, Id.Length: > 0 } actor)
        {
            return;
        }

        string? tenantCode = context.User.FindFirst("tenant_id")?.Value
            ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        await synchronizer.GrantSiteScopeAsync(actor, tenant, tenantCode, siteCode, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(StudyDirectoryEntry? Study, ProblemHttpResult? Problem)> ResolveTargetStudyAsync(
        TenantId tenant,
        string studyRef,
        IStudyDirectory directory,
        CancellationToken cancellationToken)
    {
        var trimmed = (studyRef ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (null, KernelProblems.BadRequest("Invalid study reference", "Provide a study NodeRef or study code."));
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (!NodeRef.TryParse(trimmed, out var node))
            {
                return (null, KernelProblems.InvalidNodeRef(trimmed));
            }

            if (node.Kind != NodeKinds.Study)
            {
                return (null, KernelProblems.WrongKind(node, NodeKinds.Study));
            }

            var byId = await directory.FindStudyAsync(tenant, StudyId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false);
            return byId is not null ? (byId, null) : (null, KernelProblems.NotFound(node));
        }

        var byCode = await directory.FindStudyByCodeAsync(tenant, trimmed, cancellationToken).ConfigureAwait(false);
        return byCode is not null
            ? (byCode, null)
            : (null, TypedResults.Problem(
                title: "Study not found",
                detail: $"Study '{trimmed}' does not exist in your tenant.",
                statusCode: StatusCodes.Status404NotFound));
    }

    private static async Task<ParsedImportInput> ReadImportInputAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.HasFormContentType)
        {
            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(MultipartLimits, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return ParsedImportInput.Fail(PayloadTooLargeProblem());
            }
            catch (IOException ex)
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid multipart upload", ex.Message));
            }

            byte[]? rawBytes = null;
            string? fileName = null;
            if (form.Files.Count > 0)
            {
                var file = form.Files[0];
                fileName = file.FileName;
                if (file.Length > IProtocolDesignImporter.MaxPayloadBytes)
                {
                    return ParsedImportInput.Fail(PayloadTooLargeProblem());
                }

                await using var stream = file.OpenReadStream();
                var (bytes, exceeded) = await ReadBoundedBytesAsync(stream, IProtocolDesignImporter.MaxPayloadBytes, cancellationToken).ConfigureAwait(false);
                if (exceeded)
                {
                    return ParsedImportInput.Fail(PayloadTooLargeProblem());
                }

                rawBytes = bytes;
            }

            if (!TryParseImportFormat(form["format"].ToString(), out var format))
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest(
                    "Invalid format",
                    "Supported format values are 'auto', 'odm_xml', and 'usdm_json'."));
            }

            var templateIds = ParseTemplateIds(form["template_ids"]);
            var replaceForms = bool.TryParse(form["replace_forms"].ToString(), out var parsedReplace) && parsedReplace;
            if (!TryParseAmendmentClassification(form["amendment_classification"].ToString(), out var formClassification))
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest(
                    "Invalid amendment classification",
                    "Supported amendment_classification values are 'Substantial', 'NonSubstantial', and 'UrgentSafetyMeasure'."));
            }

            if (!TryParseTransitionPolicy(form["transition_policy"].ToString(), out var formPolicy))
            {
                return ParsedImportInput.Fail(KernelProblems.BadRequest(
                    "Invalid transition policy",
                    "Supported transition_policy values are 'NewParticipantsOnly', 'RequiresReconsent', 'Automatic', and 'ByCohort'."));
            }

            return new ParsedImportInput(
                rawBytes,
                fileName,
                format,
                templateIds,
                replaceForms,
                formClassification,
                formPolicy,
                null);
        }

        var (bodyBytes, bodyExceeded) = await ReadBoundedBytesAsync(
            context.Request.Body,
            IProtocolDesignImporter.MaxPayloadBytes,
            cancellationToken).ConfigureAwait(false);
        if (bodyExceeded)
        {
            return ParsedImportInput.Fail(PayloadTooLargeProblem());
        }

        if (bodyBytes.Length == 0)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Empty import request",
                "Upload an ODM v1.3.2 XML or USDM v4 JSON file, or specify bundled template_ids."));
        }

        var jsonOptions = context.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions;
        KernelImportProtocolJsonRequest? jsonRequest;
        try
        {
            jsonRequest = JsonSerializer.Deserialize<KernelImportProtocolJsonRequest>(bodyBytes, jsonOptions);
        }
        catch (JsonException ex)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid JSON request", ex.Message));
        }

        if (jsonRequest is null)
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest("Invalid JSON request", "Request body could not be parsed."));
        }

        if (!TryParseImportFormat(jsonRequest.Format, out var jsonFormat))
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Invalid format",
                "Supported format values are 'auto', 'odm_xml', and 'usdm_json'."));
        }

        if (!TryParseAmendmentClassification(jsonRequest.AmendmentClassification, out var jsonClassification))
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Invalid amendment classification",
                "Supported amendment_classification values are 'Substantial', 'NonSubstantial', and 'UrgentSafetyMeasure'."));
        }

        if (!TryParseTransitionPolicy(jsonRequest.TransitionPolicy, out var jsonPolicy))
        {
            return ParsedImportInput.Fail(KernelProblems.BadRequest(
                "Invalid transition policy",
                "Supported transition_policy values are 'NewParticipantsOnly', 'RequiresReconsent', 'Automatic', and 'ByCohort'."));
        }

        byte[]? contentBytes = null;
        if (!string.IsNullOrEmpty(jsonRequest.Content))
        {
            contentBytes = Encoding.UTF8.GetBytes(jsonRequest.Content);
            if (contentBytes.Length > IProtocolDesignImporter.MaxPayloadBytes)
            {
                return ParsedImportInput.Fail(PayloadTooLargeProblem());
            }
        }

        return new ParsedImportInput(
            contentBytes,
            jsonRequest.FileName,
            jsonFormat,
            jsonRequest.TemplateIds,
            jsonRequest.ReplaceForms,
            jsonClassification,
            jsonPolicy,
            null);
    }

    private static async Task<DraftMutationPlan> PlanDraftMutationAsync(
        TenantId tenant,
        StudyDirectoryEntry study,
        IReadOnlyList<ProtocolVersionSummary> existingVersions,
        ProtocolVersionId derivedVersionId,
        IProtocolVersionReader versionReader,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        var ordered = existingVersions
            .OrderByDescending(v => v.Number)
            .ToList();

        // Check if a previous request with this exact Idempotency-Key already created derivedVersionId.
        var matchingReplayVersion = ordered.FirstOrDefault(v => v.Id == derivedVersionId);
        if (matchingReplayVersion is not null)
        {
            if (matchingReplayVersion.ParentVersionId is null)
            {
                if (!await IsPermittedAsync(tenant, typeof(CreateProtocolDraft), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
                {
                    return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
                }

                var priorSuperseded = ordered
                    .FirstOrDefault(v => v.Id != derivedVersionId && v.Status == ProtocolVersionStatus.Superseded);
                var replayRecoveryBase = priorSuperseded is not null
                    ? await versionReader.GetDesignAsync(tenant, priorSuperseded.Id, cancellationToken).ConfigureAwait(false)
                    : null;

                return new DraftMutationPlan(
                    ProtocolDraftImportMode.CreateDraft,
                    derivedVersionId,
                    ParentVersionId: null,
                    BaseDesign: replayRecoveryBase,
                    ParentContent: null,
                    ExistingAmendment: null,
                    CreatedNewVersion: true,
                    AmendmentIntent: null,
                    Problem: null);
            }

            var replayParentId = matchingReplayVersion.ParentVersionId.Value;
            if (!await IsPermittedAsync(tenant, typeof(ForkProtocolVersion), NodeRef.For(replayParentId), probe, catalog, cancellationToken).ConfigureAwait(false)
                || !await IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(replayParentId), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var (replayParentDesign, replayParentContent) = await LoadDesignAndContentAsync(
                tenant,
                replayParentId,
                versionReader,
                cancellationToken).ConfigureAwait(false);
            var replayAmendment = await versionReader.GetAmendmentAsync(tenant, matchingReplayVersion.Id, cancellationToken).ConfigureAwait(false);
            return new DraftMutationPlan(
                ProtocolDraftImportMode.ForkApproved,
                derivedVersionId,
                ParentVersionId: replayParentId,
                BaseDesign: replayParentDesign,
                ParentContent: replayParentContent,
                ExistingAmendment: replayAmendment,
                CreatedNewVersion: true,
                AmendmentIntent: matchingReplayVersion.AmendmentIntent,
                Problem: null);
        }

        if (ordered.Count == 0)
        {
            if (!await IsPermittedAsync(tenant, typeof(CreateProtocolDraft), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            return new DraftMutationPlan(
                ProtocolDraftImportMode.CreateDraft,
                derivedVersionId,
                ParentVersionId: null,
                BaseDesign: null,
                ParentContent: null,
                ExistingAmendment: null,
                CreatedNewVersion: true,
                AmendmentIntent: null,
                Problem: null);
        }

        if (ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.Draft) is { } openDraft)
        {
            if (!await IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(openDraft.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var currentDesign = await versionReader.GetDesignAsync(tenant, openDraft.Id, cancellationToken).ConfigureAwait(false);
            string? draftParentContent = null;
            ProtocolAmendment? existingAmendment = null;
            if (openDraft.ParentVersionId is { } draftParentId)
            {
                (_, draftParentContent) = await LoadDesignAndContentAsync(tenant, draftParentId, versionReader, cancellationToken).ConfigureAwait(false);
                existingAmendment = await versionReader.GetAmendmentAsync(tenant, openDraft.Id, cancellationToken).ConfigureAwait(false);
            }

            return new DraftMutationPlan(
                ProtocolDraftImportMode.UpdateExisting,
                openDraft.Id,
                ParentVersionId: openDraft.ParentVersionId,
                BaseDesign: currentDesign,
                ParentContent: draftParentContent,
                ExistingAmendment: existingAmendment,
                CreatedNewVersion: false,
                AmendmentIntent: openDraft.AmendmentIntent ?? existingAmendment?.Intent,
                Problem: null);
        }

        if (ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.InReview) is { } inReview)
        {
            if (!await IsPermittedAsync(tenant, typeof(ReturnProtocolVersionToDraft), NodeRef.For(inReview.Id), probe, catalog, cancellationToken).ConfigureAwait(false)
                || !await IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(inReview.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var reviewDesign = await versionReader.GetDesignAsync(tenant, inReview.Id, cancellationToken).ConfigureAwait(false);
            string? reviewParentContent = null;
            ProtocolAmendment? reviewAmendment = null;
            if (inReview.ParentVersionId is { } reviewParentId)
            {
                (_, reviewParentContent) = await LoadDesignAndContentAsync(tenant, reviewParentId, versionReader, cancellationToken).ConfigureAwait(false);
                reviewAmendment = await versionReader.GetAmendmentAsync(tenant, inReview.Id, cancellationToken).ConfigureAwait(false);
            }

            return new DraftMutationPlan(
                ProtocolDraftImportMode.ReopenInReview,
                inReview.Id,
                ParentVersionId: inReview.ParentVersionId,
                BaseDesign: reviewDesign,
                ParentContent: reviewParentContent,
                ExistingAmendment: reviewAmendment,
                CreatedNewVersion: false,
                AmendmentIntent: inReview.AmendmentIntent ?? reviewAmendment?.Intent,
                Problem: null);
        }

        if (ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.Approved) is { } approved)
        {
            if (!await IsPermittedAsync(tenant, typeof(ForkProtocolVersion), NodeRef.For(approved.Id), probe, catalog, cancellationToken).ConfigureAwait(false)
                || !await IsPermittedAsync(tenant, typeof(ReviseProtocolDraft), NodeRef.For(approved.Id), probe, catalog, cancellationToken).ConfigureAwait(false))
            {
                return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
            }

            var (approvedDesign, approvedContent) = await LoadDesignAndContentAsync(
                tenant,
                approved.Id,
                versionReader,
                cancellationToken).ConfigureAwait(false);
            return new DraftMutationPlan(
                ProtocolDraftImportMode.ForkApproved,
                derivedVersionId,
                ParentVersionId: approved.Id,
                BaseDesign: approvedDesign,
                ParentContent: approvedContent,
                ExistingAmendment: null,
                CreatedNewVersion: true,
                AmendmentIntent: "Protocol design import",
                Problem: null);
        }

        // All existing versions are terminal (Withdrawn and/or Superseded): create a recovery Draft at max(Number) + 1.
        if (!await IsPermittedAsync(tenant, typeof(CreateProtocolDraft), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return DraftMutationPlan.Fail(KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        var latestSuperseded = ordered.FirstOrDefault(v => v.Status == ProtocolVersionStatus.Superseded);
        var recoveryBaseDesign = latestSuperseded is not null
            ? await versionReader.GetDesignAsync(tenant, latestSuperseded.Id, cancellationToken).ConfigureAwait(false)
            : null;
        return new DraftMutationPlan(
            ProtocolDraftImportMode.CreateDraft,
            derivedVersionId,
            ParentVersionId: null,
            BaseDesign: recoveryBaseDesign,
            ParentContent: null,
            ExistingAmendment: null,
            CreatedNewVersion: true,
            AmendmentIntent: null,
            Problem: null);
    }

    private static async Task<(ProtocolDesign? Design, string? Content)> LoadDesignAndContentAsync(
        TenantId tenant,
        ProtocolVersionId versionId,
        IProtocolVersionReader versionReader,
        CancellationToken cancellationToken)
    {
        var rawContent = await versionReader.GetContentAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        if (rawContent is not null)
        {
            var parsed = ProtocolDesignDocument.Read(rawContent);
            if (parsed.IsValid)
            {
                return (parsed.Design, rawContent);
            }

            var fallbackDesign = await versionReader.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
            return (fallbackDesign, rawContent);
        }

        var design = await versionReader.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        return (design, design is not null ? ProtocolDesignDocument.Write(design) : null);
    }

    private static ProtocolAmendment? BuildProtocolAmendment(
        DraftMutationPlan plan,
        ProtocolDesign design,
        string serializedDesign,
        string reason,
        AmendmentClassification? requestedClassification,
        TransitionPolicy? requestedTransitionPolicy)
    {
        if (plan.ParentVersionId is null)
        {
            return null;
        }

        List<string> impacted = [];
        var diff = ProtocolStructuralDiff.Compare(plan.ParentContent, serializedDesign);
        if (diff.IsSuccess && diff.Value is { Count: > 0 } changes)
        {
            impacted =
            [
                .. changes
                    .Select(c => c.ElementId)
                    .Where(id => !string.Equals(id, ProtocolStructuralDiff.RootId, StringComparison.Ordinal)),
            ];
            if (impacted.Count == 0)
            {
                impacted = [.. changes.Select(c => c.ElementId)];
            }
        }

        ProtocolDesign? parentDesign = null;
        if (plan.ParentContent is not null
            && ProtocolDesignDocument.Read(plan.ParentContent) is { IsValid: true, Design: { } parsedParent })
        {
            parentDesign = parsedParent;
        }
        else if (plan.Mode == ProtocolDraftImportMode.ForkApproved)
        {
            parentDesign = plan.BaseDesign;
        }

        if (parentDesign is not null)
        {
            var soaImpacted = ScheduleOfActivitiesDiff.Compare(parentDesign, design).ImpactedElements;
            if (soaImpacted.Count > 0)
            {
                impacted =
                [
                    .. impacted
                        .Where(id => !string.Equals(id, ProtocolStructuralDiff.RootId, StringComparison.Ordinal))
                        .Concat(soaImpacted)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal),
                ];
            }
        }

        if (impacted.Count == 0)
        {
            impacted = plan.ExistingAmendment?.ImpactedElements is { Count: > 0 } existingImpacted
                ? [.. existingImpacted]
                : [.. design.CollectionSpecs.Select(s => s.Id).DefaultIfEmpty("protocol_design")];
        }

        var classification = requestedClassification
            ?? plan.ExistingAmendment?.Classification
            ?? AmendmentClassification.NonSubstantial;
        var transitionPolicy = requestedTransitionPolicy
            ?? plan.ExistingAmendment?.TransitionPolicy
            ?? TransitionPolicy.NewParticipantsOnly;
        var intent = plan.ExistingAmendment?.Intent
            ?? plan.AmendmentIntent
            ?? "Protocol design import";

        return new ProtocolAmendment(
            Intent: intent,
            Rationale: reason,
            Classification: classification,
            SummaryOfChanges: $"Imported {design.CollectionSpecs.Count} protocol form specification(s) ({impacted.Count} impacted element(s)).",
            ImpactedElements: impacted,
            TransitionPolicy: transitionPolicy);
    }

    private static Task<IReadOnlyDictionary<ConceptRef, string>> ResolveConceptLabelsAsync(
        TenantId tenant,
        ProtocolDesign? design,
        IConceptRegistry? concepts,
        CancellationToken cancellationToken) =>
        ResolveConceptLabelsAsync(tenant, design, concepts, stagedLabels: null, cancellationToken);

    private static async Task<IReadOnlyDictionary<ConceptRef, string>> ResolveConceptLabelsAsync(
        TenantId tenant,
        ProtocolDesign? design,
        IConceptRegistry? concepts,
        IReadOnlyDictionary<ConceptRef, string>? stagedLabels,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<ConceptRef, string>();
        if (design is null)
        {
            return map;
        }

        var needed = design.CollectionSpecs
            .SelectMany(s => s.Items)
            .Where(i => string.IsNullOrWhiteSpace(i.Prompt))
            .Select(i => i.Concept)
            .Distinct()
            .ToList();

        foreach (var conceptRef in needed)
        {
            if (stagedLabels is not null
                && stagedLabels.TryGetValue(conceptRef, out var stagedLabel)
                && !string.IsNullOrWhiteSpace(stagedLabel))
            {
                map[conceptRef] = stagedLabel;
                continue;
            }

            if (concepts is null)
            {
                continue;
            }

            var resolved = await concepts.ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false);
            if (resolved is not null && !string.IsNullOrWhiteSpace(resolved.PreferredLabel))
            {
                map[conceptRef] = resolved.PreferredLabel;
            }
        }

        return map;
    }

    private static async ValueTask<bool> IsPermittedAsync(
        TenantId tenant,
        Type commandType,
        NodeRef resource,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (catalog.Find(commandType) is not { } entry)
        {
            return false;
        }

        var decision = await probe.CanIAsync(tenant, entry.Capability, resource, cancellationToken).ConfigureAwait(false);
        return decision.Permitted;
    }

    private static async Task<(byte[] Bytes, bool ExceededLimit)> ReadBoundedBytesAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return ([], true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return (buffer.ToArray(), false);
    }

    private static bool TryParseImportFormat(string? raw, out ProtocolImportFormat format)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            format = ProtocolImportFormat.Auto;
            return true;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "odm":
            case "odm_xml":
            case "odmxml":
            case "xml":
                format = ProtocolImportFormat.OdmXml;
                return true;

            case "usdm":
            case "usdm_json":
            case "usdmjson":
            case "json":
                format = ProtocolImportFormat.UsdmJson;
                return true;

            default:
                format = ProtocolImportFormat.Auto;
                return false;
        }
    }

    private static List<string>? ParseTemplateIds(Microsoft.Extensions.Primitives.StringValues values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var entry in values)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var trimmed = entry.Trim();
            if (trimmed.StartsWith('['))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                    if (parsed is not null)
                    {
                        list.AddRange(parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // Fall through to comma-splitting.
                }
            }

            foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                list.Add(part);
            }
        }

        return list.Count > 0 ? list : null;
    }

    private static string NormalizeOrganizationCode(string input, string fallback = "SPONSOR")
    {
        var sb = new StringBuilder(input.Length);
        bool prevHyphen = false;
        foreach (char ch in input.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                prevHyphen = false;
            }
            else if (!prevHyphen && sb.Length > 0)
            {
                sb.Append('-');
                prevHyphen = true;
            }
        }

        var normalized = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(normalized) ? fallback : normalized[..Math.Min(normalized.Length, 48)];
    }

    private static ProblemHttpResult PayloadTooLargeProblem(string? detail = null) =>
        TypedResults.Problem(
            title: "Payload too large",
            detail: detail ?? $"Uploaded payload exceeds the 10 MB ({IProtocolDesignImporter.MaxPayloadBytes:N0} bytes) synchronous import limit.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    private static ProblemHttpResult ValidationFailedProblem(IReadOnlyList<string> problems)
    {
        var detail = problems.Count > 0
            ? string.Join(" ", problems)
            : "The uploaded protocol definition failed structural validation.";
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = ProtocolErrors.ContentInvalid.Code,
            ["errors"] = problems.ToArray(),
        };
        return TypedResults.Problem(
            title: "Protocol import validation failed",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: extensions);
    }

    private static string IdempotencyKey(HttpContext context) =>
        context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();

    private static Guid DeriveGuid(TenantId tenant, string kind, string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-study-import:{tenant}:{kind}:{key}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static string DeriveSubIdempotencyKey(string requestIdempotencyKey, string scope, Guid entityId)
    {
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{requestIdempotencyKey}:{scope}:{entityId:D}")));
        return $"imp-{scope}:{hash[..32]}";
    }

    private static IReadOnlyList<string> CandidateCodeSystemUris(string rawCodeSystem)
    {
        var trimmed = rawCodeSystem.Trim();
        var candidates = new List<string>();
        if (KnownCodeSystems.UriFor(trimmed) is { } canonical)
        {
            candidates.Add(canonical);
        }

        if (!candidates.Contains(trimmed, StringComparer.Ordinal))
        {
            candidates.Add(trimmed);
        }

        if (string.Equals(trimmed, "http://www.cdisc.org", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "https://www.cdisc.org", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "NCI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "CDISC", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var fallback in (string[])[KnownCodeSystems.NciThesaurus, KnownCodeSystems.SdtmCt, KnownCodeSystems.CdashCt, KnownCodeSystems.ProtocolCt])
            {
                if (!candidates.Contains(fallback, StringComparer.Ordinal))
                {
                    candidates.Add(fallback);
                }
            }
        }

        return candidates;
    }

    private static LicenseClass InferLicenceClass(string? rawCodeSystem)
    {
        if (string.IsNullOrWhiteSpace(rawCodeSystem))
        {
            return LicenseClass.Open;
        }

        var trimmed = rawCodeSystem.Trim();
        if (KnownCodeSystems.UriFor(trimmed) is { } canonical && KnownCodeSystems.RequiredLicence(canonical) is { } knownLicence)
        {
            return knownLicence;
        }

        if (trimmed.Contains("snomed", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("meddra", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("whodrug", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseClass.Licensed;
        }

        if (trimmed.Contains("loinc", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("rxnorm", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseClass.AcceptedTerms;
        }

        return LicenseClass.Open;
    }

    private static string? BoundTerminologyToken(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var trimmed = rawToken.Trim();
        if (trimmed.Length <= 64)
        {
            return trimmed;
        }

        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))[..8];
        return $"{trimmed[..55]}-{suffix}";
    }

    private static List<CodeSystemEntryData> NormalizeTerminologyEntries(
        IReadOnlyList<CodeSystemEntryData>? rawEntries,
        string? defaultGroup)
    {
        if (rawEntries is null || rawEntries.Count == 0)
        {
            return [];
        }

        var normalized = new List<CodeSystemEntryData>(rawEntries.Count);
        var seen = new HashSet<(string? Group, string Code)>();
        foreach (var entry in rawEntries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Code))
            {
                continue;
            }

            var rawCode = entry.Code.Trim();
            var boundedCode = BoundTerminologyToken(rawCode)!;
            var group = BoundTerminologyToken(entry.GroupCode) ?? defaultGroup;
            if (!seen.Add((group, boundedCode)))
            {
                continue;
            }

            var display = string.IsNullOrWhiteSpace(entry.Display) ? rawCode : entry.Display.Trim();
            if (display.Length > 1000)
            {
                display = display[..1000];
            }

            string? submission = null;
            if (!string.IsNullOrWhiteSpace(entry.SubmissionValue))
            {
                submission = entry.SubmissionValue.Trim();
            }
            else if (!string.Equals(boundedCode, rawCode, StringComparison.Ordinal))
            {
                submission = rawCode;
            }

            if (submission is { Length: > 255 })
            {
                submission = submission[..255];
            }

            normalized.Add(new CodeSystemEntryData(
                boundedCode,
                display,
                GroupCode: group,
                SubmissionValue: submission,
                Definition: string.IsNullOrWhiteSpace(entry.Definition) ? null : entry.Definition.Trim(),
                Synonyms: entry.Synonyms));
        }

        return normalized;
    }

    private static byte[] SerializeTerminologyEntries(IReadOnlyList<CodeSystemEntryData> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("code", entry.Code);
                writer.WriteString("display", entry.Display);
                if (entry.GroupCode is not null)
                {
                    writer.WriteString("group", entry.GroupCode);
                }

                if (entry.SubmissionValue is not null)
                {
                    writer.WriteString("submissionValue", entry.SubmissionValue);
                }

                if (entry.Definition is not null)
                {
                    writer.WriteString("definition", entry.Definition);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    private static string SanitizeVersionPrefix(string raw)
    {
        var sb = new StringBuilder();
        foreach (var ch in raw)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }

            if (sb.Length >= 32)
            {
                break;
            }
        }

        var prefix = sb.ToString().Trim('-', '.', '_');
        return string.IsNullOrEmpty(prefix) ? "codelist" : prefix;
    }

    private static string ComputeImportRequestFingerprint(StudyId studyId, ParsedImportInput input)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var normalizedTemplates = input.TemplateIds is { Count: > 0 }
            ? string.Join(",", input.TemplateIds.Select(t => t.Trim()))
            : string.Empty;
        var metadata = string.Join(
            "\n",
            studyId.ToString(),
            input.Format.ToString(),
            input.FileName ?? string.Empty,
            normalizedTemplates,
            input.ReplaceForms ? "replace:1" : "replace:0",
            input.AmendmentClassification?.ToString() ?? string.Empty,
            input.TransitionPolicy?.ToString() ?? string.Empty);
        sha.AppendData(Encoding.UTF8.GetBytes(metadata));
        sha.AppendData("\npayload:"u8);
        if (input.RawBytes is { Length: > 0 } raw)
        {
            sha.AppendData(raw);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static bool TryParseAmendmentClassification(string? raw, out AmendmentClassification? classification)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            classification = null;
            return true;
        }

        var normalized = raw.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<AmendmentClassification>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            classification = parsed;
            return true;
        }

        classification = null;
        return false;
    }

    private static bool TryParseTransitionPolicy(string? raw, out TransitionPolicy? policy)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            policy = null;
            return true;
        }

        var normalized = raw.Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<TransitionPolicy>(normalized, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            policy = parsed;
            return true;
        }

        policy = null;
        return false;
    }

    private sealed record ParsedImportInput(
        byte[]? RawBytes,
        string? FileName,
        ProtocolImportFormat Format,
        IReadOnlyList<string>? TemplateIds,
        bool ReplaceForms,
        AmendmentClassification? AmendmentClassification,
        TransitionPolicy? TransitionPolicy,
        ProblemHttpResult? Problem)
    {
        public static ParsedImportInput Fail(ProblemHttpResult problem) =>
            new(null, null, ProtocolImportFormat.Auto, null, false, null, null, problem);
    }

    private sealed record StagedCodeSystem(
        CodeSystemId CodeSystemId,
        string SystemUri,
        LicenseClass License);

    private sealed record StagedCodeSystemVersion(
        CodeSystemVersionId VersionId,
        CodeSystemId CodeSystemId,
        string SystemUri,
        string VersionLabel,
        DateOnly ReleasedOn,
        TerminologyPackage Package,
        IReadOnlyList<CodeSystemEntryData> Entries);

    private sealed record DraftMutationPlan(
        ProtocolDraftImportMode Mode,
        ProtocolVersionId TargetVersionId,
        ProtocolVersionId? ParentVersionId,
        ProtocolDesign? BaseDesign,
        string? ParentContent,
        ProtocolAmendment? ExistingAmendment,
        bool CreatedNewVersion,
        string? AmendmentIntent,
        ProblemHttpResult? Problem)
    {
        public static DraftMutationPlan Fail(ProblemHttpResult problem) =>
            new(ProtocolDraftImportMode.CreateDraft, default, null, null, null, null, false, null, problem);
    }
}
