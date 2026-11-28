using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

internal static class KernelStudyDirectoryResolver
{
    internal static async Task<(OrganizationDirectoryEntry? Organization, ProblemHttpResult? Problem)> ResolveOrRegisterOrganizationAsync(
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

    internal static async Task<(SiteDirectoryEntry? Site, ProblemHttpResult? Problem)> ResolveTargetSiteAsync(
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

    internal static async Task<(SiteDirectoryEntry? Site, ProblemHttpResult? Problem)> RegisterInlineSiteAsync(
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

    internal static async Task SynchronizeCallerStudyScopeAsync(
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

    internal static async Task SynchronizeCallerSiteScopeAsync(
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

    internal static async Task<(StudyDirectoryEntry? Study, ProblemHttpResult? Problem)> ResolveTargetStudyAsync(
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

    internal static async ValueTask<bool> IsPermittedAsync(
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

    internal static string NormalizeOrganizationCode(string input, string fallback = "SPONSOR")
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

    internal static string IdempotencyKey(HttpContext context) =>
        context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim();

    internal static Guid DeriveGuid(TenantId tenant, string kind, string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-kernel-study-import:{tenant}:{kind}:{key}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    internal static string DeriveSubIdempotencyKey(string requestIdempotencyKey, string scope, Guid entityId)
    {
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{requestIdempotencyKey}:{scope}:{entityId:D}")));
        return $"imp-{scope}:{hash[..32]}";
    }
}
