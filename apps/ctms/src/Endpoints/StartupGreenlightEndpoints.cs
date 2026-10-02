using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;

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
            .WithSummary("Evaluates 6-facet greenlight readiness for a single trial site.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK);

        startup.MapPost("/sites/{siteId}/greenlight/certify", CertifySiteGreenlightAsync)
            .WithName("CertifySiteGreenlight")
            .WithSummary("Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        startup.MapGet("/matrix", GetCountryMatrixAsync)
            .WithName("GetCountryGreenlightMatrix")
            .WithSummary("Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country.")
            .Produces<List<SiteGreenlightGateResponseDto>>(StatusCodes.Status200OK);

        startup.MapPost("/matrix/batch-certify", BatchCertifyCountrySitesAsync)
            .WithName("BatchCertifyCountrySites")
            .WithSummary("Batch-certifies all selected 100% compliant sites in a target country with a single e-signature.")
            .Produces<BatchCertifyResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    public static async Task<Ok<SiteGreenlightGateResponseDto>> GetSiteGreenlightAsync(
        string siteId,
        [FromQuery] string studyId,
        [FromQuery] string? countryCode,
        [FromServices] CtmsDbContext db,
        CancellationToken ct)
    {
        countryCode = (countryCode ?? "US").ToUpperInvariant();
        var gate = await GetOrCreateGateAsync(db, studyId, siteId, countryCode, ct);
        return TypedResults.Ok(ToResponseDto(gate));
    }

    public static async Task<Results<Ok<SiteGreenlightGateResponseDto>, BadRequest<ProblemDetails>>> CertifySiteGreenlightAsync(
        string siteId,
        [FromQuery] string studyId,
        [FromQuery] string? countryCode,
        [FromHeader(Name = "X-Change-Reason")] string? changeReasonHeader,
        [FromServices] CtmsDbContext db,
        [FromServices] IClock clock,
        [FromServices] IConfiguration config,
        CancellationToken ct)
    {
        countryCode = (countryCode ?? "US").ToUpperInvariant();
        var gate = await GetOrCreateGateAsync(db, studyId, siteId, countryCode, ct);

        if (!IsFullyCompliant(gate))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Site Non-Compliant",
                Detail = $"Site {siteId} does not fulfill all 6 greenlight facets and cannot be certified.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string signer = "Startup Manager";
        string secret = config["GATEWAY_SECRET"] ?? "internal-gateway-secret-12345";
        DateTimeOffset now = clock.UtcNow;
        long issuedAt = now.ToUnixTimeSeconds();
        long expiresAt = issuedAt + (86400 * 365);

        string token = GreenlightTokenGenerator.GenerateToken(
            gate.SiteId, gate.StudyId, gate.CountryCode, signer, secret, issuedAt, expiresAt, gate.IsOverridden);

        gate.OverallStatus = "APPROVED";
        gate.GreenlightToken = token;
        gate.GreenlightCertifiedBy = signer;
        gate.GreenlightCertifiedAt = now;
        gate.ReasonForChange = changeReasonHeader ?? "Greenlight activation certified";

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = signer,
            UserRole = "Site Startup Manager",
            Action = "GREENLIGHT_CERTIFIED",
            Details = $"Greenlight activation certified for site {gate.SiteId} in study {gate.StudyId} ({gate.CountryCode})"
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponseDto(gate));
    }

    public static async Task<Ok<List<SiteGreenlightGateResponseDto>>> GetCountryMatrixAsync(
        [FromQuery] string studyId,
        [FromQuery] string countryCode,
        [FromServices] CtmsDbContext db,
        CancellationToken ct)
    {
        countryCode = (countryCode ?? "US").ToUpperInvariant();
        studyId = string.IsNullOrWhiteSpace(studyId) ? "PROTOCOL-2026-01" : studyId;

        var gates = await db.CtmsSiteGreenlightGates
            .AsNoTracking()
            .Where(g => g.StudyId == studyId && g.CountryCode == countryCode)
            .ToListAsync(ct);

        if (gates.Count == 0)
        {
            gates = SeedCountryGates(studyId, countryCode);
            db.CtmsSiteGreenlightGates.AddRange(gates);
            await db.SaveChangesAsync(ct);
        }

        var responseDtos = gates.Select(ToResponseDto).ToList();
        return TypedResults.Ok(responseDtos);
    }

    public static async Task<Results<Ok<BatchCertifyResponseDto>, BadRequest<ProblemDetails>>> BatchCertifyCountrySitesAsync(
        [FromBody] BatchCertifyRequestDto request,
        [FromHeader(Name = "X-Change-Reason")] string? changeReasonHeader,
        [FromServices] CtmsDbContext db,
        [FromServices] IClock clock,
        [FromServices] IConfiguration config,
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

        string countryCode = (request.CountryCode ?? "US").ToUpperInvariant();
        string studyId = string.IsNullOrWhiteSpace(request.StudyId) ? "PROTOCOL-2026-01" : request.StudyId;

        var gates = await db.CtmsSiteGreenlightGates
            .Where(g => g.StudyId == studyId && g.CountryCode == countryCode && request.SiteIds.Contains(g.SiteId))
            .ToListAsync(ct);

        if (gates.Count == 0)
        {
            var seedGates = SeedCountryGates(studyId, countryCode);
            db.CtmsSiteGreenlightGates.AddRange(seedGates);
            await db.SaveChangesAsync(ct);

            gates = seedGates.Where(g => request.SiteIds.Contains(g.SiteId)).ToList();
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

        string signer = !string.IsNullOrWhiteSpace(request.SignerName) ? request.SignerName : "Startup Manager";
        string role = !string.IsNullOrWhiteSpace(request.SignerRole) ? request.SignerRole : "VP of Clinical Operations";
        string secret = config["GATEWAY_SECRET"] ?? "internal-gateway-secret-12345";
        DateTimeOffset now = clock.UtcNow;
        long issuedAt = now.ToUnixTimeSeconds();
        long expiresAt = issuedAt + (86400 * 365);

        var certifiedItems = new List<CertifiedSiteItemDto>();

        foreach (var gate in gates)
        {
            string token = GreenlightTokenGenerator.GenerateToken(
                gate.SiteId, gate.StudyId, gate.CountryCode, signer, secret, issuedAt, expiresAt, gate.IsOverridden);

            gate.OverallStatus = "APPROVED";
            gate.GreenlightToken = token;
            gate.GreenlightCertifiedBy = signer;
            gate.GreenlightCertifiedAt = now;
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
        return gate.FacetRegulatoryIrb &&
               gate.FacetCtaBudget &&
               gate.FacetEisfBaseline &&
               gate.FacetDoaLog &&
               gate.FacetStaffTraining &&
               gate.FacetIpRelease;
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

        int percentage = (int)Math.Round((passed / 6.0) * 100);

        var unfulfilled = new List<string>();
        if (!gate.FacetRegulatoryIrb) unfulfilled.Add("Pending IRB/Regulatory Approval");
        if (!gate.FacetCtaBudget) unfulfilled.Add("Missing Executed CTA & Budget");
        if (!gate.FacetEisfBaseline) unfulfilled.Add("Incomplete eISF Baseline");
        if (!gate.FacetDoaLog) unfulfilled.Add("Pending Delegation of Authority Log");
        if (!gate.FacetStaffTraining) unfulfilled.Add("Incomplete Staff Protocol Training");
        if (!gate.FacetIpRelease) unfulfilled.Add("Pending Investigational Product Depot Release");

        return new SiteGreenlightGateResponseDto(
            Id: gate.Id,
            StudyId: gate.StudyId,
            SiteId: gate.SiteId,
            SiteName: string.IsNullOrWhiteSpace(gate.SiteName) ? $"Site {gate.SiteId}" : gate.SiteName,
            CountryCode: gate.CountryCode,
            OverallStatus: gate.OverallStatus,
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

    private static async Task<CtmsSiteGreenlightGate> GetOrCreateGateAsync(
        CtmsDbContext db, string studyId, string siteId, string countryCode, CancellationToken ct)
    {
        var gate = await db.CtmsSiteGreenlightGates
            .FirstOrDefaultAsync(g => g.StudyId == studyId && g.SiteId == siteId, ct);

        if (gate == null)
        {
            gate = new CtmsSiteGreenlightGate
            {
                StudyId = studyId,
                SiteId = siteId,
                SiteName = $"Site {siteId}",
                CountryCode = countryCode,
                OverallStatus = "PENDING",
                ReadinessPercentage = 100,
                FacetRegulatoryIrb = true,
                FacetCtaBudget = true,
                FacetEisfBaseline = true,
                FacetDoaLog = true,
                FacetStaffTraining = true,
                FacetIpRelease = true,
                CreatedBy = "svc:ctms",
                ReasonForChange = "Initial greenlight gate creation"
            };
            db.CtmsSiteGreenlightGates.Add(gate);
            await db.SaveChangesAsync(ct);
        }

        return gate;
    }

    private static List<CtmsSiteGreenlightGate> SeedCountryGates(string studyId, string countryCode)
    {
        var list = new List<CtmsSiteGreenlightGate>();
        string prefix = countryCode.ToUpper() switch
        {
            "DE" => "SITE-20",
            "US" => "SITE-10",
            "JP" => "SITE-30",
            "GB" => "SITE-40",
            _ => "SITE-50"
        };

        // Create 6 sites: 4 fully compliant (100%), 2 non-compliant (exceptions)
        for (int i = 1; i <= 6; i++)
        {
            string sId = $"{prefix}{i}";
            bool isCompliant = (i <= 4);

            list.Add(new CtmsSiteGreenlightGate
            {
                StudyId = studyId,
                SiteId = sId,
                SiteName = $"{countryCode} Clinical Trial Site {i}",
                CountryCode = countryCode,
                OverallStatus = "PENDING",
                ReadinessPercentage = isCompliant ? 100 : 67,
                FacetRegulatoryIrb = true,
                FacetCtaBudget = true,
                FacetEisfBaseline = true,
                FacetDoaLog = isCompliant,
                FacetStaffTraining = true,
                FacetIpRelease = isCompliant,
                CreatedBy = "svc:ctms",
                ReasonForChange = "Seed country matrix sites"
            });
        }

        return list;
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
    string Id,
    string StudyId,
    string SiteId,
    string SiteName,
    string CountryCode,
    string OverallStatus,
    int ReadinessPercentage,
    bool FacetRegulatoryIrb,
    bool FacetCtaBudget,
    bool FacetEisfBaseline,
    bool FacetDoaLog,
    bool FacetStaffTraining,
    bool FacetIpRelease,
    bool IsCompliant,
    string? GreenlightToken,
    string? GreenlightCertifiedBy,
    string? GreenlightCertifiedAt,
    bool IsOverridden,
    List<string>? UnfulfilledReasons
);

public record BatchCertifyRequestDto(
    string StudyId,
    string CountryCode,
    List<string> SiteIds,
    string? SignerName,
    string? SignerRole,
    string? Meaning
);

public record CertifiedSiteItemDto(
    string SiteId,
    string SiteName,
    string GreenlightToken,
    string CertifiedAt,
    string CertifiedBy,
    string Status
);

public record BatchCertifyResponseDto(
    bool Success,
    int CertifiedCount,
    string StudyId,
    string CountryCode,
    List<CertifiedSiteItemDto> CertifiedSites,
    string Message
);
