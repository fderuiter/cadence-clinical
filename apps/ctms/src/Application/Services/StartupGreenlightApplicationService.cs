using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Ctms.Application.Services;

public interface IStartupGreenlightApplicationService
{
    Task<Results<Ok<SiteGreenlightGateResponseDto>, ProblemHttpResult>> GetSiteGreenlightAsync(
        string siteId,
        string studyId,
        string? countryCode,
        HttpContext httpContext,
        CancellationToken ct);

    Task<Results<Ok<SiteGreenlightGateResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> CertifySiteGreenlightAsync(
        string siteId,
        string studyId,
        string? countryCode,
        string? changeReasonHeader,
        HttpContext httpContext,
        CancellationToken ct);

    Task<Results<Ok<List<SiteGreenlightGateResponseDto>>, ProblemHttpResult>> GetCountryMatrixAsync(
        string studyId,
        string countryCode,
        HttpContext httpContext,
        CancellationToken ct);

    Task<Results<Ok<BatchCertifyResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> BatchCertifyCountrySitesAsync(
        BatchCertifyRequestDto request,
        string? changeReasonHeader,
        HttpContext httpContext,
        CancellationToken ct);
}

public sealed class StartupGreenlightApplicationService(
    CtmsDbContext db,
    ICtmsUnitOfWork unitOfWork,
    IClock clock,
    IOptions<GatewayAuthenticationOptions> gatewayAuthentication) : IStartupGreenlightApplicationService
{
    public async Task<Results<Ok<SiteGreenlightGateResponseDto>, ProblemHttpResult>> GetSiteGreenlightAsync(
        string siteId,
        string studyId,
        string? countryCode,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        var gate = await FindGateAsync(db.CtmsSiteGreenlightGates.AsNoTracking(), studyId, siteId, countryCode, ct);
        return gate is null ? NoGate(siteId, studyId) : TypedResults.Ok(ToResponseDto(gate));
    }

    public async Task<Results<Ok<SiteGreenlightGateResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> CertifySiteGreenlightAsync(
        string siteId,
        string studyId,
        string? countryCode,
        string? changeReasonHeader,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        var gate = await FindGateAsync(db.CtmsSiteGreenlightGates, studyId, siteId, countryCode, ct);
        if (gate is null)
        {
            return NoGate(siteId, studyId);
        }

        if (!IsFullyCompliant(gate))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Site Non-Compliant",
                Detail = $"Site {siteId} does not fulfill all 6 greenlight facets and cannot be certified.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string signer = CallerScope.RequiredUserId(httpContext, "An admitted greenlight request has no signed caller.");
        string secret = TokenSecret(gatewayAuthentication);
        DateTimeOffset now = clock.UtcNow;
        long issuedAt = now.ToUnixTimeSeconds();
        long expiresAt = issuedAt + (86400 * 365);

        string token = GreenlightTokenGenerator.GenerateToken(
            gate.SiteId, gate.StudyId, gate.CountryCode, signer, secret, issuedAt, expiresAt, gate.IsOverridden);

        gate.Approve(signer, now, token);
        gate.ReasonForChange = changeReasonHeader ?? "Greenlight activation certified";

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = signer,
            UserRole = CallerScope.Roles(httpContext),
            Action = "GREENLIGHT_CERTIFIED",
            Details = $"Greenlight activation certified for site {gate.SiteId} in study {gate.StudyId} ({gate.CountryCode})",
            StudyId = gate.StudyId,
            SiteId = gate.SiteId,
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(ToResponseDto(gate));
    }

    public async Task<Results<Ok<List<SiteGreenlightGateResponseDto>>, ProblemHttpResult>> GetCountryMatrixAsync(
        string studyId,
        string countryCode,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return StudyRequired();
        }

        countryCode = (countryCode ?? "US").ToUpperInvariant();
        if (!CtmsScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return ClinicalScopeGuard.OutsideStudy(code: ClinicalScopeGuard.ForbiddenCode);
        }

        if (CtmsScopeGuard.ResolveSiteFilter(httpContext, siteId: null, out var siteFilter) is { } siteDenied)
        {
            return siteDenied;
        }

        var gates = await db.CtmsSiteGreenlightGates
            .AsNoTracking()
            .Where(g => g.StudyId == studyId && g.CountryCode == countryCode)
            .OrderBy(g => g.SiteId)
            .ToListAsync(ct);

        var filteredGates = CtmsScopeGuard.ApplyFilters(gates, studyFilter: null, g => g.StudyId, siteFilter, g => g.SiteId);
        var responseDtos = filteredGates.Select(ToResponseDto).ToList();
        return TypedResults.Ok(responseDtos);
    }

    public async Task<Results<Ok<BatchCertifyResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> BatchCertifyCountrySitesAsync(
        BatchCertifyRequestDto request,
        string? changeReasonHeader,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.SiteIds == null || request.SiteIds.Count == 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid Batch Request",
                Detail = "No site IDs provided for batch certification.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return StudyRequired();
        }

        string countryCode = (request.CountryCode ?? "US").ToUpperInvariant();
        string studyId = request.StudyId;
        var siteIds = request.SiteIds.Distinct(StringComparer.Ordinal).ToList();
        if (!CtmsScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return ClinicalScopeGuard.OutsideStudy(code: ClinicalScopeGuard.ForbiddenCode);
        }

        var principal = CtmsScopeGuard.ResolvePrincipal(httpContext);
        var outsideSites = siteIds
            .Where(site => !SiteAccessEvaluator.CanAccessSite(principal, site))
            .ToList();
        if (outsideSites.Count > 0)
        {
            return CtmsScopeGuard.Forbidden(
                $"You are not assigned to: {string.Join(", ", outsideSites)}. Nothing was certified.",
                "Outside your sites");
        }

        var gates = await db.CtmsSiteGreenlightGates
            .Where(g => g.StudyId == studyId && g.CountryCode == countryCode && siteIds.Contains(g.SiteId))
            .ToListAsync(ct);

        var unknownSites = siteIds.Except(gates.Select(g => g.SiteId), StringComparer.Ordinal).ToList();
        if (unknownSites.Count > 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Unknown Sites",
                Detail = $"No greenlight gate is recorded in study {studyId} ({countryCode}) for: {string.Join(", ", unknownSites)}. Nothing was certified.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var nonCompliantSites = gates.Where(g => !IsFullyCompliant(g)).Select(g => g.SiteId).ToList();
        if (nonCompliantSites.Count > 0)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Batch Certification Rejected",
                Detail = $"The following requested sites are not 100% compliant: {string.Join(", ", nonCompliantSites)}. Only fully compliant sites can be batch-certified.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string signer = CallerScope.RequiredUserId(httpContext, "An admitted greenlight request has no signed caller.");
        string role = CallerScope.Roles(httpContext);
        string secret = TokenSecret(gatewayAuthentication);
        DateTimeOffset now = clock.UtcNow;
        long issuedAt = now.ToUnixTimeSeconds();
        long expiresAt = issuedAt + (86400 * 365);

        var certifiedItems = new List<CertifiedSiteItemDto>();

        foreach (var gate in gates)
        {
            string token = GreenlightTokenGenerator.GenerateToken(
                gate.SiteId, gate.StudyId, gate.CountryCode, signer, secret, issuedAt, expiresAt, gate.IsOverridden);

            gate.Approve(signer, now, token);
            gate.ReasonForChange = changeReasonHeader ?? request.Meaning ?? "21 CFR Part 11 Country-Level Batch Certification";

            var auditEntry = new CtmsAuditLog
            {
                Timestamp = now,
                UserId = signer,
                UserRole = role,
                Action = "BATCH_GREENLIGHT_CERTIFIED",
                Details = $"21 CFR Part 11 Batch Greenlight Certified for site {gate.SiteId} ({gate.SiteName}) in {gate.CountryCode} for study {gate.StudyId}.",
                StudyId = gate.StudyId,
                SiteId = gate.SiteId,
            };
            db.CtmsAuditLogs.Add(auditEntry);

            certifiedItems.Add(new CertifiedSiteItemDto(
                gate.SiteId,
                gate.SiteName,
                token,
                now.ToString("o"),
                signer,
                "APPROVED"
            ));
        }

        await unitOfWork.CommitAsync(ct);

        return TypedResults.Ok(new BatchCertifyResponseDto(
            Success: true,
            CertifiedCount: certifiedItems.Count,
            StudyId: studyId,
            CountryCode: countryCode,
            CertifiedSites: certifiedItems,
            Message: $"Successfully batch-certified {certifiedItems.Count} sites in {countryCode} with single 21 CFR Part 11 electronic signature."
        ));
    }

    public static string TokenSecret(IOptions<GatewayAuthenticationOptions> gatewayAuthentication)
    {
        ArgumentNullException.ThrowIfNull(gatewayAuthentication);
        return gatewayAuthentication.Value.GatewaySecret is { Length: >= GatewayAuthenticationOptions.MinimumSecretLength } secret
            ? secret
            : throw new InvalidOperationException(
                $"GATEWAY_SECRET must be set to a secret of at least {GatewayAuthenticationOptions.MinimumSecretLength} characters; greenlight tokens cannot be sealed.");
    }

    private static ProblemHttpResult StudyRequired() =>
        TypedResults.Problem(
            title: "Study required",
            detail: "Name the study (study_id); there is no default study.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NoGate(string siteId, string studyId) =>
        TypedResults.Problem(
            title: "No greenlight gate",
            detail: $"No greenlight gate is recorded for site {siteId} in study {studyId}.",
            statusCode: StatusCodes.Status404NotFound);

    private static bool IsFullyCompliant(CtmsSiteGreenlightGate gate) =>
        gate.IsOverridden || (
            gate.FacetRegulatoryIrb &&
            gate.FacetCtaBudget &&
            gate.FacetEisfBaseline &&
            gate.FacetDoaLog &&
            gate.FacetStaffTraining &&
            gate.FacetIpRelease);

    private static SiteGreenlightGateResponseDto ToResponseDto(CtmsSiteGreenlightGate gate)
    {
        bool compliant = IsFullyCompliant(gate);
        int passed = 0;
        if (gate.FacetRegulatoryIrb) passed++;
        if (gate.FacetCtaBudget) passed++;
        if (gate.FacetEisfBaseline) passed++;
        if (gate.FacetDoaLog) passed++;
        if (gate.FacetStaffTraining) passed++;
        if (gate.FacetIpRelease) passed++;

        int percentage = gate.IsOverridden ? 100 : (int)Math.Round((passed / 6.0) * 100);

        var unfulfilled = new List<string>();
        if (!gate.IsOverridden)
        {
            if (!gate.FacetRegulatoryIrb) unfulfilled.Add("Pending IRB/Regulatory Approval");
            if (!gate.FacetCtaBudget) unfulfilled.Add("Missing Executed CTA & Budget");
            if (!gate.FacetEisfBaseline) unfulfilled.Add("Incomplete eISF Baseline");
            if (!gate.FacetDoaLog) unfulfilled.Add("Pending Delegation of Authority Log");
            if (!gate.FacetStaffTraining) unfulfilled.Add("Incomplete Staff Protocol Training");
            if (!gate.FacetIpRelease) unfulfilled.Add("Pending Investigational Product Depot Release");
        }

        return new SiteGreenlightGateResponseDto(
            Id: gate.Id,
            StudyId: gate.StudyId,
            SiteId: gate.SiteId,
            SiteName: string.IsNullOrWhiteSpace(gate.SiteName) ? $"Site {gate.SiteId}" : gate.SiteName,
            CountryCode: gate.CountryCode,
            OverallStatus: gate.OverallStatus.ToString().ToUpperInvariant(),
            ReadinessPercentage: percentage,
            FacetRegulatoryIrb: gate.FacetRegulatoryIrb,
            FacetCtaBudget: gate.FacetCtaBudget,
            FacetEisfBaseline: gate.FacetEisfBaseline,
            FacetDoaLog: gate.FacetDoaLog,
            FacetStaffTraining: gate.FacetStaffTraining,
            FacetIpRelease: gate.FacetIpRelease,
            IsCompliant: compliant,
            GreenlightToken: gate.GreenlightToken,
            GreenlightCertifiedBy: gate.GreenlightCertifiedBy,
            GreenlightCertifiedAt: gate.GreenlightCertifiedAt?.ToString("o"),
            IsOverridden: gate.IsOverridden,
            UnfulfilledReasons: unfulfilled
        );
    }

    private static Task<CtmsSiteGreenlightGate?> FindGateAsync(
        IQueryable<CtmsSiteGreenlightGate> gates, string studyId, string siteId, string? countryCode, CancellationToken ct)
    {
        var query = gates.Where(g => g.StudyId == studyId && g.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            string country = countryCode.ToUpperInvariant();
            query = query.Where(g => g.CountryCode == country);
        }

        return query.FirstOrDefaultAsync(ct);
    }
}
