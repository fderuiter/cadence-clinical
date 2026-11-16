using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Site Startup (SSU) and Country-Level Multi-Site Greenlight Activation Matrix endpoints.
/// @req:PRD-SSU-001
/// @req:PRD-SSU-002
/// @req:PRD-SSU-003
/// </summary>
public static class StartupGreenlightEndpoints
{
    public const string CertifyPermission = "ctms_greenlight:certify";
    public const string ReadPermission = "ctms_greenlight:read";

    public static RouteGroupBuilder MapStartupGreenlightEndpoints(this RouteGroupBuilder group)
    {
        var startup = group.MapGroup("/startup");

        startup.MapGet("/sites/{siteId}/greenlight", GetSiteGreenlightAsync)
            .WithName("EvaluateSiteGreenlight")
            .RequirePermission(ReadPermission)
            .RequireSiteScope("siteId")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Evaluates 6-facet greenlight readiness for a single trial site.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK);

        startup.MapPost("/sites/{siteId}/greenlight/certify", CertifySiteGreenlightAsync)
            .WithName("CertifySiteGreenlight")
            .RequirePermission(CertifyPermission)
            .RequireSiteScope("siteId")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        startup.MapGet("/matrix", GetCountryMatrixAsync)
            .WithName("GetCountryGreenlightMatrix")
            .RequirePermission(ReadPermission)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country.")
            .Produces<List<SiteGreenlightGateResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        startup.MapPost("/matrix/batch-certify", BatchCertifyCountrySitesAsync)
            .WithName("BatchCertifyCountrySites")
            .RequirePermission(CertifyPermission)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Batch-certifies all selected 100% compliant sites in a target country with a single e-signature.")
            .Produces<BatchCertifyResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static string Signer(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted greenlight request has no signed caller.");

    // Greenlight tokens are sealed with the gateway secret; there is no fallback secret (ADR-2256).
    private static string TokenSecret(IConfiguration config) =>
        config["GATEWAY_SECRET"] is { Length: > 0 } secret
            ? secret
            : throw new InvalidOperationException("GATEWAY_SECRET is not configured; greenlight tokens cannot be sealed.");

    private static ProblemHttpResult OutsideStudy() =>
        TypedResults.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

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

    public static async Task<Results<Ok<SiteGreenlightGateResponseDto>, ProblemHttpResult>> GetSiteGreenlightAsync(
        string siteId,
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "country_code")] string? countryCode,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!CallerScope.CanAccessStudy(httpContext, studyId))
        {
            return OutsideStudy();
        }

        // A site without a recorded gate has no readiness evidence; nothing is invented for it (#5433).
        var gate = await FindGateAsync(db.CtmsSiteGreenlightGates.AsNoTracking(), studyId, siteId, countryCode, ct);
        return gate is null ? NoGate(siteId, studyId) : TypedResults.Ok(ToResponseDto(gate));
    }

    public static async Task<Results<Ok<SiteGreenlightGateResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> CertifySiteGreenlightAsync(
        string siteId,
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "country_code")] string? countryCode,
        [FromHeader(Name = "X-Change-Reason")] string? changeReasonHeader,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        IConfiguration config,
        CancellationToken ct)
    {
        if (!CallerScope.CanAccessStudy(httpContext, studyId))
        {
            return OutsideStudy();
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

        string signer = Signer(httpContext);
        string secret = TokenSecret(config);
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
            Details = $"Greenlight activation certified for site {gate.SiteId} in study {gate.StudyId} ({gate.CountryCode})"
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponseDto(gate));
    }

    public static async Task<Results<Ok<List<SiteGreenlightGateResponseDto>>, ProblemHttpResult>> GetCountryMatrixAsync(
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "country_code")] string countryCode,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return StudyRequired();
        }

        countryCode = (countryCode ?? "US").ToUpperInvariant();
        if (!CallerScope.CanAccessStudy(httpContext, studyId))
        {
            return OutsideStudy();
        }

        // Reads only: a study and country with no recorded gates is an empty matrix (#5433).
        var gates = await db.CtmsSiteGreenlightGates
            .AsNoTracking()
            .Where(g => g.StudyId == studyId && g.CountryCode == countryCode)
            .OrderBy(g => g.SiteId)
            .ToListAsync(ct);

        var responseDtos = gates.Select(ToResponseDto).ToList();
        return TypedResults.Ok(responseDtos);
    }

    public static async Task<Results<Ok<BatchCertifyResponseDto>, BadRequest<ProblemDetails>, ProblemHttpResult>> BatchCertifyCountrySitesAsync(
        [FromBody] BatchCertifyRequestDto request,
        [FromHeader(Name = "X-Change-Reason")] string? changeReasonHeader,
        CtmsDbContext db,
        HttpContext httpContext,
        [FromServices] IClock clock,
        IConfiguration config,
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
        if (!CallerScope.CanAccessStudy(httpContext, studyId))
        {
            return OutsideStudy();
        }

        var outsideSites = siteIds
            .Where(site => !SiteAccessEvaluator.CanAccessSite(CallerScope.Principal(httpContext), site))
            .ToList();
        if (outsideSites.Count > 0)
        {
            return TypedResults.Problem(
                title: "Outside your sites",
                detail: $"You are not assigned to: {string.Join(", ", outsideSites)}. Nothing was certified.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var gates = await db.CtmsSiteGreenlightGates
            .Where(g => g.StudyId == studyId && g.CountryCode == countryCode && siteIds.Contains(g.SiteId))
            .ToListAsync(ct);

        // Only sites with a recorded gate in this study and country can be certified; nothing is invented (#5433).
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

        // The signer is the signed caller, never a name or title in the request body (ADR-2516).
        string signer = Signer(httpContext);
        string role = CallerScope.Roles(httpContext);
        string secret = TokenSecret(config);
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
                Details = $"21 CFR Part 11 Batch Greenlight Certified for site {gate.SiteId} ({gate.SiteName}) in {gate.CountryCode} for study {gate.StudyId}."
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

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new BatchCertifyResponseDto(
            Success: true,
            CertifiedCount: certifiedItems.Count,
            StudyId: studyId,
            CountryCode: countryCode,
            CertifiedSites: certifiedItems,
            Message: $"Successfully batch-certified {certifiedItems.Count} sites in {countryCode} with single 21 CFR Part 11 electronic signature."
        ));
    }

    private static bool IsFullyCompliant(CtmsSiteGreenlightGate gate)
    {
        return gate.IsOverridden || (
               gate.FacetRegulatoryIrb &&
               gate.FacetCtaBudget &&
               gate.FacetEisfBaseline &&
               gate.FacetDoaLog &&
               gate.FacetStaffTraining &&
               gate.FacetIpRelease);
    }

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

public static class GreenlightTokenGenerator
{
    public static string GenerateToken(
        string siteId,
        string studyId,
        string countryCode,
        string certifiedBy,
        string secret,
        long issuedAt,
        long expiresAt,
        bool isOverridden = false)
    {
        var payloadObj = new Dictionary<string, object?>
        {
            ["certified_by"] = certifiedBy,
            ["country_code"] = countryCode.ToUpperInvariant(),
            ["expires_at"] = expiresAt,
            ["is_overridden"] = isOverridden,
            ["issued_at"] = issuedAt,
            ["site_id"] = siteId,
            ["study_id"] = studyId,
            ["token_type"] = "SITE_GREENLIGHT_ACTIVATION",
            ["token_version"] = "1"
        };

        string json = JsonSerializer.Serialize(payloadObj);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        string b64Payload = Convert.ToBase64String(jsonBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(b64Payload));
        string hexSig = Convert.ToHexStringLower(hashBytes);

        return $"{b64Payload}.{hexSig}";
    }
}

public record SiteGreenlightGateResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("site_name")] string SiteName,
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("overall_status")] string OverallStatus,
    [property: JsonPropertyName("readiness_percentage")] int ReadinessPercentage,
    [property: JsonPropertyName("facet_regulatory_irb")] bool FacetRegulatoryIrb,
    [property: JsonPropertyName("facet_cta_budget")] bool FacetCtaBudget,
    [property: JsonPropertyName("facet_eisf_baseline")] bool FacetEisfBaseline,
    [property: JsonPropertyName("facet_doa_log")] bool FacetDoaLog,
    [property: JsonPropertyName("facet_staff_training")] bool FacetStaffTraining,
    [property: JsonPropertyName("facet_ip_release")] bool FacetIpRelease,
    [property: JsonPropertyName("is_compliant")] bool IsCompliant,
    [property: JsonPropertyName("greenlight_token")] string? GreenlightToken,
    [property: JsonPropertyName("greenlight_certified_by")] string? GreenlightCertifiedBy,
    [property: JsonPropertyName("greenlight_certified_at")] string? GreenlightCertifiedAt,
    [property: JsonPropertyName("is_overridden")] bool IsOverridden,
    [property: JsonPropertyName("unfulfilled_reasons")] List<string>? UnfulfilledReasons
);

public record BatchCertifyRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("site_ids")] List<string> SiteIds,
    [property: JsonPropertyName("meaning")] string? Meaning
);

public record CertifiedSiteItemDto(
    [property: JsonPropertyName("site_id")] string SiteId,
    [property: JsonPropertyName("site_name")] string SiteName,
    [property: JsonPropertyName("greenlight_token")] string GreenlightToken,
    [property: JsonPropertyName("certified_at")] string CertifiedAt,
    [property: JsonPropertyName("certified_by")] string CertifiedBy,
    [property: JsonPropertyName("status")] string Status
);

public record BatchCertifyResponseDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("certified_count")] int CertifiedCount,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("country_code")] string CountryCode,
    [property: JsonPropertyName("certified_sites")] List<CertifiedSiteItemDto> CertifiedSites,
    [property: JsonPropertyName("message")] string Message
);
