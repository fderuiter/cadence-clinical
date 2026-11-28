using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.StudyImports;

internal static class KernelDirectoryEndpoints
{
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

        var idempotencyKey = KernelStudyDirectoryResolver.IdempotencyKey(context);
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
            : KernelStudyDirectoryResolver.NormalizeOrganizationCode(name);
        if (string.IsNullOrWhiteSpace(code))
        {
            return KernelErrorEndpoints.Problem(IdentityErrors.CodeRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(RegisterOrganization), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        var derivedOrgId = OrganizationId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "organization", idempotencyKey));
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

        var idempotencyKey = KernelStudyDirectoryResolver.IdempotencyKey(context);
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
        if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(RegisterSite), NodeRef.For(tenant), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        var institutionResult = await KernelStudyDirectoryResolver.ResolveOrRegisterOrganizationAsync(
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
        var derivedSiteId = SiteId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "site", $"{institution.OrganizationId}:{idempotencyKey}"));
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

        await KernelStudyDirectoryResolver.SynchronizeCallerSiteScopeAsync(context, tenant, persisted.SiteCode, cancellationToken).ConfigureAwait(false);

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
        var studyLookup = await KernelStudyDirectoryResolver.ResolveTargetStudyAsync(tenant, studyRef, directory, cancellationToken).ConfigureAwait(false);
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

        var idempotencyKey = KernelStudyDirectoryResolver.IdempotencyKey(context);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        var tenant = KernelRequestTenant.Get(context);
        var studyLookup = await KernelStudyDirectoryResolver.ResolveTargetStudyAsync(tenant, studyRef, directory, cancellationToken).ConfigureAwait(false);
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

        if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(LinkSiteToStudy), NodeRef.For(study.StudyId), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied);
        }

        var siteLookup = await KernelStudyDirectoryResolver.ResolveTargetSiteAsync(
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
            var inlineSite = await KernelStudyDirectoryResolver.RegisterInlineSiteAsync(
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
            : StudySiteId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "study-site", $"{study.StudyId}:{site.SiteId}:{idempotencyKey}"));
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

        await KernelStudyDirectoryResolver.SynchronizeCallerSiteScopeAsync(context, tenant, persisted.SiteCode, cancellationToken).ConfigureAwait(false);

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
}
