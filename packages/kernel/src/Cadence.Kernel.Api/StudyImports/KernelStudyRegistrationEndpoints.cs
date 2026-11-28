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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.StudyImports;

internal static class KernelStudyRegistrationEndpoints
{
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

        var idempotencyKey = KernelStudyDirectoryResolver.IdempotencyKey(context);
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

        var sponsorResult = await KernelStudyDirectoryResolver.ResolveOrRegisterOrganizationAsync(
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
        var derivedStudyId = StudyId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "study", idempotencyKey));
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

        await KernelStudyDirectoryResolver.SynchronizeCallerStudyScopeAsync(context, tenant, persisted.StudyCode, cancellationToken).ConfigureAwait(false);
        if (linkedSiteSummary is not null)
        {
            await KernelStudyDirectoryResolver.SynchronizeCallerSiteScopeAsync(context, tenant, linkedSiteSummary.SiteCode, cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Created(
            $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/nodes/{NodeRef.For(persisted.StudyId)}",
            KernelStudySummaryResponse.From(persisted, linkedSiteSummary));
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
            var lookup = await KernelStudyDirectoryResolver.ResolveTargetSiteAsync(
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

        if (!await KernelStudyDirectoryResolver.IsPermittedAsync(tenant, typeof(LinkSiteToStudy), NodeRef.For(studyId), probe, catalog, cancellationToken).ConfigureAwait(false))
        {
            return (null, KernelErrorEndpoints.Problem(KernelErrors.AuthorizationDenied));
        }

        var derivedStudySiteId = StudySiteId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "study-site", $"{studyId}:{targetSite!.SiteId}:{idempotencyKey}"));
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
}
