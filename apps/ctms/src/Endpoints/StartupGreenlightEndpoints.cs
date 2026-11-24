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
using Cadence.Apps.Ctms.Services;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
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
    public const string CertifySiteSemanticAction = "ctms.greenlight.certify";
    public const string BatchCertifySemanticAction = "ctms.greenlight.batch_certify";
    public const string BatchCertifyAction = "/api/v1/ctms/startup/matrix/batch-certify";

    public static string CertifySiteAction(string siteId) => $"/api/v1/ctms/startup/sites/{siteId}/greenlight/certify";

    public static RouteGroupBuilder MapStartupGreenlightEndpoints(this RouteGroupBuilder group)
    {
        var startup = group.MapGroup("/startup");

        startup.MapGet("/sites/{siteId}/greenlight", GetSiteGreenlightAsync)
            .WithName("EvaluateSiteGreenlight")
            .RequirePermission(ReadPermission)
            .RequireSiteScope("siteId")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Evaluates 6-facet greenlight readiness for a single trial site.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK);

        startup.MapPost("/sites/{siteId}/greenlight/certify", CertifySiteGreenlightAsync)
            .WithName("CertifySiteGreenlight")
            .RequirePermission(CertifyPermission)
            .RequireSiteScope("siteId")
            .AddEndpointFilter(PreCheckStudyQueryScopeFilter)
            .RequireElectronicSignature(
                CertifySiteSemanticAction,
                ctx => CertifySiteAction(ctx.Request.RouteValues["siteId"]?.ToString() ?? string.Empty))
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token.")
            .Produces<SiteGreenlightGateResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        startup.MapGet("/matrix", GetCountryMatrixAsync)
            .WithName("GetCountryGreenlightMatrix")
            .RequirePermission(ReadPermission).RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country.")
            .Produces<List<SiteGreenlightGateResponseDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        startup.MapPost("/matrix/batch-certify", BatchCertifyCountrySitesAsync)
            .WithName("BatchCertifyCountrySites")
            .RequirePermission(CertifyPermission).RequireCallerStudyAndSiteScope()
            .AddEndpointFilter(PreCheckBatchCertifyScopeFilter)
            .RequireElectronicSignature(BatchCertifySemanticAction, _ => BatchCertifyAction)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("Batch-certifies all selected 100% compliant sites in a target country with a single e-signature.")
            .Produces<BatchCertifyResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static ValueTask<object?> PreCheckStudyQueryScopeFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        string? studyId = context.HttpContext.Request.Query["study_id"].FirstOrDefault();
        string? siteId = context.HttpContext.Request.RouteValues.TryGetValue("siteId", out var rv) ? rv?.ToString() : null;
        if (!string.IsNullOrWhiteSpace(studyId)
            && CtmsScopeGuard.RequireStudyAndSite(context.HttpContext, studyId, siteId) is { } denied)
        {
            return ValueTask.FromResult<object?>(denied);
        }

        return next(context);
    }

    private static ValueTask<object?> PreCheckBatchCertifyScopeFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var req = context.Arguments.OfType<BatchCertifyRequestDto>().FirstOrDefault();
        if (req is not null && !string.IsNullOrWhiteSpace(req.StudyId))
        {
            if (!CtmsScopeGuard.CanAccessStudy(context.HttpContext, req.StudyId))
            {
                return ValueTask.FromResult<object?>(OutsideStudy());
            }

            if (req.SiteIds is { Count: > 0 })
            {
                var principal = CtmsScopeGuard.ResolvePrincipal(context.HttpContext);
                var outside = req.SiteIds.Where(s => !SiteAccessEvaluator.CanAccessSite(principal, s)).ToList();
                if (outside.Count > 0)
                {
                    return ValueTask.FromResult<object?>(CtmsScopeGuard.Forbidden(
                        $"You are not assigned to: {string.Join(", ", outside)}. Nothing was certified.",
                        "Outside your sites"));
                }
            }
        }

        return next(context);
    }

    private static string Signer(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted greenlight request has no signed caller.");

    // Greenlight tokens are sealed with the gateway secret; there is no fallback secret (ADR-2256).
    private static string TokenSecret(IConfiguration config) =>
        config["GATEWAY_SECRET"] is { Length: > 0 } secret
            ? secret
            : throw new InvalidOperationException("GATEWAY_SECRET is not configured; greenlight tokens cannot be sealed.");

    private static ProblemHttpResult OutsideStudy() =>
        CtmsScopeGuard.Forbidden("You are not assigned to this study.", "Outside your studies");

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
        CancellationToken ct,
        [FromServices] ICtmsGreenlightReadinessEvaluator? evaluator = null,
        [FromServices] IClock? clock = null)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return StudyRequired();
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        string trimmedStudy = studyId.Trim();
        string trimmedSite = siteId.Trim();
        string effectiveCountry = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.Trim().ToUpperInvariant();

        var gate = await FindGateAsync(db.CtmsSiteGreenlightGates, trimmedStudy, trimmedSite, countryCode, ct);
        var live = evaluator is not null
            ? await evaluator.EvaluateSiteAsync(db, gate, trimmedStudy, trimmedSite, ct)
            : await EvaluateLocalReadinessAsync(db, gate, trimmedStudy, trimmedSite, ct);

        bool shouldPersist = false;
        if (gate is null)
        {
            var now = (clock ?? SystemClock.Instance).UtcNow;
            string actor = CallerScope.UserId(httpContext) ?? "system";
            gate = new CtmsSiteGreenlightGate
            {
                StudyId = trimmedStudy,
                SiteId = trimmedSite,
                SiteName = $"Site {trimmedSite}",
                CountryCode = effectiveCountry,
                CreatedAt = now.UtcDateTime,
                CreatedBy = actor,
                ReasonForChange = "Initialized site startup greenlight readiness gate from live checks",
                VersionIndex = 1,
            };
            gate.UpdateLiveFacets(
                live.FacetRegulatoryIrb,
                live.FacetCtaBudget,
                live.FacetEisfBaseline,
                live.FacetDoaLog,
                live.FacetStaffTraining,
                live.FacetIpRelease,
                live.ReadinessPercentage,
                live.BlockingReasons.Count > 0 ? string.Join("; ", live.BlockingReasons) : null);

            db.CtmsSiteGreenlightGates.Add(gate);
            shouldPersist = true;
        }
        else if (gate.OverallStatus == CtmsGreenlightStatus.Pending && gate.GreenlightCertifiedAt is null)
        {
            bool changed = gate.FacetRegulatoryIrb != live.FacetRegulatoryIrb
                || gate.FacetCtaBudget != live.FacetCtaBudget
                || gate.FacetEisfBaseline != live.FacetEisfBaseline
                || gate.FacetDoaLog != live.FacetDoaLog
                || gate.FacetStaffTraining != live.FacetStaffTraining
                || gate.FacetIpRelease != live.FacetIpRelease
                || gate.ReadinessPercentage != live.ReadinessPercentage;

            if (changed)
            {
                gate.UpdateLiveFacets(
                    live.FacetRegulatoryIrb,
                    live.FacetCtaBudget,
                    live.FacetEisfBaseline,
                    live.FacetDoaLog,
                    live.FacetStaffTraining,
                    live.FacetIpRelease,
                    live.ReadinessPercentage,
                    live.BlockingReasons.Count > 0 ? string.Join("; ", live.BlockingReasons) : null);
                shouldPersist = true;
            }
        }

        if (shouldPersist)
        {
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(ToResponseDto(gate, live));
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
        CancellationToken ct,
        [FromServices] ICtmsGreenlightReadinessEvaluator? evaluator = null)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return StudyRequired();
        }

        if (CtmsScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is { } scopeDenied)
        {
            return scopeDenied;
        }

        string trimmedStudy = studyId.Trim();
        string trimmedSite = siteId.Trim();

        var gate = await FindGateAsync(db.CtmsSiteGreenlightGates, trimmedStudy, trimmedSite, countryCode, ct);
        if (gate is null)
        {
            return NoGate(trimmedSite, trimmedStudy);
        }

        GreenlightLiveEvaluation? live = null;
        if (gate.OverallStatus == CtmsGreenlightStatus.Pending && gate.GreenlightCertifiedAt is null)
        {
            live = evaluator is not null
                ? await evaluator.EvaluateSiteAsync(db, gate, trimmedStudy, trimmedSite, ct)
                : await EvaluateLocalReadinessAsync(db, gate, trimmedStudy, trimmedSite, ct);
            gate.UpdateLiveFacets(
                live.FacetRegulatoryIrb,
                live.FacetCtaBudget,
                live.FacetEisfBaseline,
                live.FacetDoaLog,
                live.FacetStaffTraining,
                live.FacetIpRelease,
                live.ReadinessPercentage,
                live.BlockingReasons.Count > 0 ? string.Join("; ", live.BlockingReasons) : null);
        }

        if (!IsFullyCompliant(gate))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Site Non-Compliant",
                Detail = $"Site {trimmedSite} does not fulfill all 6 greenlight facets and cannot be certified.",
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
            Details = $"Greenlight activation certified for site {gate.SiteId} in study {gate.StudyId} ({gate.CountryCode})",
            StudyId = gate.StudyId,
            SiteId = gate.SiteId,
        };
        db.CtmsAuditLogs.Add(auditEntry);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponseDto(gate, live));
    }

    public static async Task<Results<Ok<List<SiteGreenlightGateResponseDto>>, ProblemHttpResult>> GetCountryMatrixAsync(
        [FromQuery(Name = "study_id")] string studyId,
        [FromQuery(Name = "country_code")] string countryCode,
        CtmsDbContext db,
        HttpContext httpContext,
        CancellationToken ct,
        [FromServices] ICtmsGreenlightReadinessEvaluator? evaluator = null)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return StudyRequired();
        }

        string trimmedStudy = studyId.Trim();
        countryCode = (countryCode ?? "US").ToUpperInvariant();
        if (!CtmsScopeGuard.CanAccessStudy(httpContext, trimmedStudy))
        {
            return OutsideStudy();
        }

        if (CtmsScopeGuard.ResolveSiteFilter(httpContext, siteId: null, out var siteFilter) is { } siteDenied)
        {
            return siteDenied;
        }

        var gates = await db.CtmsSiteGreenlightGates
            .AsNoTracking()
            .Where(g => g.StudyId == trimmedStudy && g.CountryCode == countryCode)
            .OrderBy(g => g.SiteId)
            .ToListAsync(ct);

        var filteredGates = CtmsScopeGuard.ApplyFilters(gates, studyFilter: null, g => g.StudyId, siteFilter, g => g.SiteId);
        var responseDtos = new List<SiteGreenlightGateResponseDto>(filteredGates.Count);
        foreach (var gate in filteredGates)
        {
            GreenlightLiveEvaluation? live = null;
            if (evaluator is not null && gate.OverallStatus == CtmsGreenlightStatus.Pending && gate.GreenlightCertifiedAt is null)
            {
                live = await evaluator.EvaluateSiteAsync(db, gate, gate.StudyId, gate.SiteId, ct);
                gate.UpdateLiveFacets(
                    live.FacetRegulatoryIrb,
                    live.FacetCtaBudget,
                    live.FacetEisfBaseline,
                    live.FacetDoaLog,
                    live.FacetStaffTraining,
                    live.FacetIpRelease,
                    live.ReadinessPercentage,
                    live.BlockingReasons.Count > 0 ? string.Join("; ", live.BlockingReasons) : null);
            }

            responseDtos.Add(ToResponseDto(gate, live));
        }

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
        string studyId = request.StudyId.Trim();
        var siteIds = request.SiteIds.Distinct(StringComparer.Ordinal).ToList();
        if (!CtmsScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return OutsideStudy();
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

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new BatchCertifyResponseDto(
            Success: true,
            CertifiedCount: certifiedItems.Count,
            StudyId: studyId,
            CountryCode: countryCode,
            CertifiedSites: certifiedItems,
            Message: $"Successfully batch-certified {certifiedItems.Count} sites in {countryCode} with single 21 CFR Part 11 electronic signature.",
            TotalCertified: certifiedItems.Count
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

    private static async Task<GreenlightLiveEvaluation> EvaluateLocalReadinessAsync(
        CtmsDbContext db,
        CtmsSiteGreenlightGate? existingGate,
        string studyCode,
        string siteCode,
        CancellationToken ct)
    {
        var milestones = await db.CtmsSiteMilestones.AsNoTracking()
            .Where(m => m.StudyId == studyCode && m.SiteId == siteCode)
            .ToListAsync(ct);
        var delegations = await db.CtmsDelegations.AsNoTracking()
            .Where(d => d.StudyId == studyCode && d.SiteId == siteCode && d.IsActive)
            .ToListAsync(ct);
        var visits = await db.CtmsMonitoringVisits.AsNoTracking()
            .Where(v => v.StudyId == studyCode && v.SiteId == siteCode)
            .ToListAsync(ct);

        static bool IsDone(CtmsSiteMilestone m) =>
            m.Status is CtmsSiteMilestoneStatus.Completed or CtmsSiteMilestoneStatus.Achieved || m.ActualDate.HasValue;
        static bool HasToken(string? src, params string[] tokens) =>
            !string.IsNullOrWhiteSpace(src) && tokens.Any(t => src.Contains(t, StringComparison.OrdinalIgnoreCase));

        bool reg = (existingGate?.FacetRegulatoryIrb ?? false)
            || milestones.Any(m => IsDone(m) && HasToken(m.MilestoneType, "IRB", "REGULATORY", "ETHICS", "EC_APPROVAL"));
        bool cta = (existingGate?.FacetCtaBudget ?? false)
            || milestones.Any(m => IsDone(m) && HasToken(m.MilestoneType, "CTA", "CONTRACT", "BUDGET", "SITE_AGREEMENT"));
        bool eisf = (existingGate?.FacetEisfBaseline ?? false)
            || milestones.Any(m => IsDone(m) && HasToken(m.MilestoneType, "EISF", "ISF", "SIV_READY", "BASELINE_DOCS"))
            || visits.Any(v => string.Equals(v.VisitType, "SIV", StringComparison.OrdinalIgnoreCase)
                && v.Status is CtmsMonitoringVisitStatus.Completed or CtmsMonitoringVisitStatus.Signed_Off);
        bool doa = delegations.Count > 0
            ? delegations.All(d => d.SignedOff)
            : (existingGate?.FacetDoaLog ?? false);
        bool training = (existingGate?.FacetStaffTraining ?? false)
            || milestones.Any(m => IsDone(m) && HasToken(m.MilestoneType, "TRAINING", "QUALIFICATION"));
        bool ip = (existingGate?.FacetIpRelease ?? false)
            || milestones.Any(m => IsDone(m) && HasToken(m.MilestoneType, "IP_RELEASE", "DRUG_RELEASE", "SUPPLY_READY", "DEPOT_RELEASE", "IP_SHIPPED"));

        var facets = BuildFacets(reg, cta, eisf, doa, training, ip, delegations);
        var blocking = BuildUnfulfilledReasons(existingGate?.IsOverridden ?? false, reg, cta, eisf, doa, training, ip);
        int passed = facets.Count(f => f.IsSatisfied);
        int percentage = (existingGate?.IsOverridden ?? false) ? 100 : (int)Math.Round(passed / 6.0 * 100);
        bool compliant = blocking.Count == 0 || (existingGate?.IsOverridden ?? false);
        string status;
        if (existingGate?.OverallStatus is CtmsGreenlightStatus.Approved or CtmsGreenlightStatus.Overridden)
        {
            status = existingGate.OverallStatus.ToString().ToUpperInvariant();
        }
        else
        {
            status = compliant ? "READY" : "PENDING";
        }

        return new GreenlightLiveEvaluation(reg, cta, eisf, doa, training, ip, facets, blocking, percentage, compliant, status);
    }

    private static List<GreenlightFacetItemDto> BuildFacets(
        bool reg,
        bool cta,
        bool eisf,
        bool doa,
        bool training,
        bool ip,
        IReadOnlyList<CtmsDelegation>? delegations = null)
    {
        string doaSummary;
        if (!doa)
        {
            doaSummary = "Pending Principal Investigator-signed delegation log";
        }
        else if (delegations is { Count: > 0 })
        {
            doaSummary = $"{delegations.Count} active delegation(s) signed by Principal Investigator";
        }
        else
        {
            doaSummary = "Principal Investigator DoA log verified";
        }

        return
        [
            new("REGULATORY_IRB", "Regulatory & IRB Approval", reg, reg ? "Regulatory & IRB approval verified" : "Pending IRB/EC and national regulatory approval"),
            new("CTA_BUDGET", "Clinical Trial Agreement & Budget", cta, cta ? "Executed CTA & site agreement verified" : "Pending Clinical Trial Agreement & Budget execution"),
            new("EISF_BASELINE", "eISF Baseline Package", eisf, eisf ? "eISF baseline package verified" : "Pending eISF baseline document package"),
            new("DOA_LOG", "Delegation of Authority Log", doa, doaSummary),
            new("STAFF_TRAINING", "Protocol Staff Training", training, training ? "Protocol staff training verified" : "Pending protocol training completion for site staff"),
            new("IP_RELEASE", "Investigational Product Release", ip, ip ? "Investigational product depot release verified" : "Pending Investigational Product depot release"),
        ];
    }

    private static List<string> BuildUnfulfilledReasons(
        bool isOverridden,
        bool reg,
        bool cta,
        bool eisf,
        bool doa,
        bool training,
        bool ip)
    {
        var unfulfilled = new List<string>();
        if (!isOverridden)
        {
            if (!reg) unfulfilled.Add("Pending IRB/Regulatory Approval");
            if (!cta) unfulfilled.Add("Missing Executed CTA & Budget");
            if (!eisf) unfulfilled.Add("Incomplete eISF Baseline");
            if (!doa) unfulfilled.Add("Pending Delegation of Authority Log");
            if (!training) unfulfilled.Add("Incomplete Staff Protocol Training");
            if (!ip) unfulfilled.Add("Pending Investigational Product Depot Release");
        }

        return unfulfilled;
    }

    private static SiteGreenlightGateResponseDto ToResponseDto(
        CtmsSiteGreenlightGate gate,
        GreenlightLiveEvaluation? live = null)
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
        var unfulfilled = BuildUnfulfilledReasons(
            gate.IsOverridden,
            gate.FacetRegulatoryIrb,
            gate.FacetCtaBudget,
            gate.FacetEisfBaseline,
            gate.FacetDoaLog,
            gate.FacetStaffTraining,
            gate.FacetIpRelease);

        var facets = live?.Facets.ToList() ?? BuildFacets(
            gate.FacetRegulatoryIrb,
            gate.FacetCtaBudget,
            gate.FacetEisfBaseline,
            gate.FacetDoaLog,
            gate.FacetStaffTraining,
            gate.FacetIpRelease);

        string? certifiedAtIso = gate.GreenlightCertifiedAt?.ToString("o");
        bool isCertified = gate.OverallStatus == CtmsGreenlightStatus.Approved || gate.GreenlightCertifiedAt is not null;
        bool isReadyForCertification = compliant && !isCertified;

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
            GreenlightCertifiedAt: certifiedAtIso,
            IsOverridden: gate.IsOverridden,
            UnfulfilledReasons: unfulfilled,
            Facets: facets,
            BlockingReasons: unfulfilled,
            IsReadyForCertification: isReadyForCertification,
            CertifiedBy: gate.GreenlightCertifiedBy,
            CertifiedAt: certifiedAtIso
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

public record GreenlightFacetItemDto(
    [property: JsonPropertyName("facet_code")] string FacetCode,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("is_satisfied")] bool IsSatisfied,
    [property: JsonPropertyName("evidence_summary")] string EvidenceSummary
);

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
    [property: JsonPropertyName("unfulfilled_reasons")] List<string>? UnfulfilledReasons,
    [property: JsonPropertyName("facets")] List<GreenlightFacetItemDto>? Facets = null,
    [property: JsonPropertyName("blocking_reasons")] List<string>? BlockingReasons = null,
    [property: JsonPropertyName("is_ready_for_certification")] bool IsReadyForCertification = false,
    [property: JsonPropertyName("certified_by")] string? CertifiedBy = null,
    [property: JsonPropertyName("certified_at")] string? CertifiedAt = null
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
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("total_certified")] int TotalCertified = 0
);
