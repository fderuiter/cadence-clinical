using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Cadence.Apps.Interop.Endpoints;

/// <summary>
/// Minimal API endpoint definitions for SMART on FHIR eSource polling and side-by-side reconciliation drawer workflows.
/// @req:PRD-SYS-001
/// @req:PRD-ESR-001
/// @req:PRD-ESR-002
/// @req:PRD-ESR-003
/// </summary>
public static class EsourceEndpoints
{
    public const string AcceptAction = "/api/v1/interop/esource/reconcile/accept";
    public const string AcceptSemanticAction = "interop.esource.accept";
    public const string BatchAcceptAction = "/api/v1/interop/esource/reconcile/batch-accept";
    public const string BatchAcceptSemanticAction = "interop.esource.batch_accept";
    public const string RejectAction = "/api/v1/interop/esource/reconcile/reject";
    public const string RejectSemanticAction = "interop.esource.reject";
    public const string BatchRejectAction = "/api/v1/interop/esource/reconcile/batch-reject";
    public const string BatchRejectSemanticAction = "interop.esource.batch_reject";

    private static SigTokenBinding? BatchAcceptBinding(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<EhrReconcileBatchAcceptRequest>().FirstOrDefault();
        return SigTokenBatchId.FromIds(request?.CandidateIds) is { } batchId
            ? new SigTokenBinding(BatchAcceptAction, BatchAcceptSemanticAction, batchId)
            : null;
    }

    private static SigTokenBinding? BatchRejectBinding(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<EhrReconcileBatchRejectRequest>().FirstOrDefault();
        return SigTokenBatchId.FromIds(request?.CandidateIds) is { } batchId
            ? new SigTokenBinding(BatchRejectAction, BatchRejectSemanticAction, batchId)
            : null;
    }

    public static IEndpointRouteBuilder MapEsourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/interop/esource")
            .WithTags("eSource Reconciliation & SMART on FHIR");

        // Hospital EHR Endpoint Configuration
        group.MapGet("/endpoints", ListEhrEndpointsAsync)
            .WithName("ListEhrEndpoints")
            .RequirePermission("esource_endpoint:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrEndpointListResponse>(StatusCodes.Status200OK);

        group.MapPost("/endpoints", RegisterEhrEndpointAsync)
            .WithName("RegisterEhrEndpoint")
            .RequirePermission("esource_endpoint:configure").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrEndpointRecord>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // SMART Backend Services Assertion Endpoints
        group.MapPost("/smart/assertion", GenerateClientAssertionAsync)
            .WithName("GenerateClientAssertion")
            .RequirePermission("esource_endpoint:configure").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<SmartAssertionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/smart/verify", VerifyClientAssertionAsync)
            .WithName("VerifyClientAssertion")
            .RequirePermission("esource_endpoint:configure").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<SmartVerifyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Polling & Ingestion
        group.MapPost("/poll", PollHospitalEhrAsync)
            .WithName("PollHospitalEhr")
            .RequirePermission("esource_candidate:ingest").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status501NotImplemented)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/ingest", IngestFhirBundleAsync)
            .WithName("IngestFhirBundle")
            .RequirePermission("esource_candidate:ingest").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<FhirBundleIngestResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Candidates for Drawer
        group.MapGet("/candidates", ListCandidatesAsync)
            .WithName("ListCandidates")
            .RequirePermission("esource_candidate:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrCandidateListResponse>(StatusCodes.Status200OK);

        group.MapGet("/candidates/{candidate_id}", GetCandidateByIdAsync)
            .WithName("GetCandidate")
            .RequirePermission("esource_candidate:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrCandidateItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Reconciliation 1-Click Actions
        group.MapPost("/reconcile/accept", AcceptCandidateAsync)
            .WithName("AcceptCandidate")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(AcceptSemanticAction, _ => AcceptAction)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/reconcile/batch-accept", BatchAcceptCandidatesAsync)
            .WithName("BatchAcceptCandidates")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(BatchAcceptBinding)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileBatchActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/reconcile/reject", RejectCandidateAsync)
            .WithName("RejectCandidate")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(RejectSemanticAction, _ => RejectAction)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/reconcile/batch-reject", BatchRejectCandidatesAsync)
            .WithName("BatchRejectCandidates")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(BatchRejectBinding)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileBatchActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/reconcile/query", RaiseClinicalQueryAsync)
            .WithName("RaiseClinicalQuery")
            .RequirePermission("esource_candidate:query").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Provenance & Audit Trail
        group.MapGet("/provenance/{candidate_id}", GetProvenanceAuditAsync)
            .WithName("GetProvenanceAudit")
            .RequirePermission("esource_candidate:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrProvenanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static async Task<IResult> ListEhrEndpointsAsync(
        bool active_only,
        InteropDbContext db,
        CancellationToken ct)
    {
        var query = db.EhrEndpointRecords.AsNoTracking();
        if (active_only)
        {
            query = query.Where(e => e.IsActive);
        }
        var list = await query.ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new EhrEndpointListResponse(list));
    }

    public static async Task<IResult> RegisterEhrEndpointAsync(
        EhrEndpointCreateRequest request,
        InteropDbContext db,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.FhirBaseUrl))
        {
            return Results.Problem("Endpoint name and FHIR Base URL are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.SigningKeyRef is not null && !ConfigurationSmartSigningKeys.IsValidReference(request.SigningKeyRef))
        {
            return Results.Problem(
                "SigningKeyRef names a server-side key: letters, digits and underscores, at most 64.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var caller = GetCallerUser(httpContext);
        var record = new EhrEndpointRecord
        {
            Name = request.Name,
            SystemType = request.SystemType ?? "SMART_FHIR_GENERIC",
            FhirBaseUrl = request.FhirBaseUrl,
            TokenUrl = request.TokenUrl ?? $"{request.FhirBaseUrl}/oauth/token",
            ClientId = request.ClientId ?? Guid.NewGuid().ToString("N"),
            PublicKeyJwksUrl = request.PublicKeyJwksUrl,
            SigningKeyRef = request.SigningKeyRef,
            SigningKeyId = request.SigningKeyId,
            ScopesJson = JsonSerializer.Serialize(request.Scopes ?? ["system/*.read", "user/*.read"]),
            PollingIntervalSec = request.PollingIntervalSec > 0 ? request.PollingIntervalSec : 300,
            IsActive = true,
            CreatedAt = clock.UtcNow,
            CreatedBy = caller,
            ReasonForChange = string.IsNullOrWhiteSpace(request.ReasonForChange) ? "Registered SMART on FHIR Endpoint" : request.ReasonForChange
        };

        db.EhrEndpointRecords.Add(record);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/v1/interop/esource/endpoints/{record.Id}", record);
    }

    /// <summary>The longest client assertion SMART Backend Services allows.</summary>
    public const int MaxAssertionLifetimeSeconds = 300;

    private static IResult SigningKeyUnavailable(string detail) =>
        Results.Problem(detail, statusCode: StatusCodes.Status422UnprocessableEntity, title: "SMART signing key unavailable");

    private static async Task<EhrEndpointRecord?> ActiveEndpointAsync(InteropDbContext db, string? endpointId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(endpointId)
            ? null
            : await db.EhrEndpointRecords.AsNoTracking().FirstOrDefaultAsync(e => e.Id == endpointId && e.IsActive, ct).ConfigureAwait(false);

    /// <summary>
    /// Signs a SMART Backend Services client assertion for a registered endpoint with that endpoint's server-side key
    /// (#5436). The issuer and subject are the endpoint's client id and the audience its token URL. A missing,
    /// unconfigured or invalid key answers 422 and signs nothing; there is no generated-key fallback.
    /// </summary>
    public static async Task<IResult> GenerateClientAssertionAsync(
        SmartAssertionRequest request,
        InteropDbContext db,
        ISmartSigningKeys signingKeys,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointId))
        {
            return Results.Problem("EndpointId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ExpirationSeconds is < 1 or > MaxAssertionLifetimeSeconds)
        {
            return Results.Problem(
                $"ExpirationSeconds must be between 1 and {MaxAssertionLifetimeSeconds}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var endpoint = await ActiveEndpointAsync(db, request.EndpointId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Results.Problem($"No active EHR endpoint {request.EndpointId}.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!signingKeys.TryGetPrivateKey(endpoint.SigningKeyRef, out var rsa, out var error))
        {
            return SigningKeyUnavailable(error);
        }

        using (rsa)
        {
            var securityKey = new RsaSecurityKey(rsa) { KeyId = endpoint.SigningKeyId };
            var now = clock.UtcNow.UtcDateTime;
            var expires = now.AddSeconds(request.ExpirationSeconds);

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = endpoint.ClientId,
                Subject = new ClaimsIdentity([new Claim("sub", endpoint.ClientId)]),
                Audience = endpoint.TokenUrl,
                Expires = expires,
                IssuedAt = now,
                NotBefore = now,
                Claims = new Dictionary<string, object>
                {
                    ["jti"] = Guid.NewGuid().ToString()
                },
                SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha384)
            };

            var tokenString = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
            return Results.Ok(new SmartAssertionResponse(
                ClientAssertionToken: tokenString,
                ClientAssertionType: "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ExpiresAt: new DateTimeOffset(expires)));
        }
    }

    /// <summary>
    /// Verifies a client assertion against a registered endpoint: its signature under the endpoint's key, issuer and
    /// subject equal to the client id, audience equal to the token URL, and lifetime on the injected clock (#5436).
    /// </summary>
    public static async Task<IResult> VerifyClientAssertionAsync(
        SmartVerifyRequest request,
        InteropDbContext db,
        ISmartSigningKeys signingKeys,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ClientAssertionToken) || string.IsNullOrWhiteSpace(request.EndpointId))
        {
            return Results.Problem("ClientAssertionToken and EndpointId are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var endpoint = await ActiveEndpointAsync(db, request.EndpointId, ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return Results.Problem($"No active EHR endpoint {request.EndpointId}.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!signingKeys.TryGetPrivateKey(endpoint.SigningKeyRef, out var rsa, out var error))
        {
            return SigningKeyUnavailable(error);
        }

        using (rsa)
        {
            using var publicKey = RSA.Create(rsa.ExportParameters(includePrivateParameters: false));
            var now = clock.UtcNow;
            var parameters = new TokenValidationParameters
            {
                ValidIssuer = endpoint.ClientId,
                ValidAudience = endpoint.TokenUrl,
                IssuerSigningKey = new RsaSecurityKey(publicKey),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha256],
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (notBefore, expires, _, _) =>
                    expires is { } exp && now.UtcDateTime < exp && (notBefore is not { } nbf || nbf <= now.UtcDateTime),
            };

            var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            try
            {
                tokenHandler.ValidateToken(request.ClientAssertionToken, parameters, out var validated);
                var jwt = (JwtSecurityToken)validated;
                bool subjectIsClient = jwt.Subject == endpoint.ClientId;
                return Results.Ok(new SmartVerifyResponse(
                    IsValid: subjectIsClient,
                    Subject: jwt.Subject ?? string.Empty,
                    Issuer: jwt.Issuer ?? string.Empty,
                    Audience: jwt.Audiences.FirstOrDefault() ?? string.Empty,
                    ExpiresAt: new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero),
                    ErrorMessage: subjectIsClient ? null : "The subject is not the endpoint's client id."));
            }
            catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
            {
                return Results.Ok(new SmartVerifyResponse(
                    IsValid: false,
                    Subject: string.Empty,
                    Issuer: string.Empty,
                    Audience: string.Empty,
                    ExpiresAt: now,
                    ErrorMessage: ex.Message));
            }
        }
    }

    /// <summary>
    /// Polls a registered EHR endpoint. No FHIR client is wired yet, so this answers 501 and writes nothing rather than
    /// inventing candidates (#5432). Bundles that a site exports are ingested through <see cref="IngestFhirBundleAsync"/>.
    /// </summary>
    public static IResult PollHospitalEhrAsync(
        EhrPollRequest request,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.Problem("StudyId and SubjectId are required for polling.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, request.StudyId, request.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return OutsideStudy();
        }

        return Results.Problem(
            "Polling an EHR is not available: no FHIR client is configured. Ingest an exported FHIR bundle instead.",
            statusCode: StatusCodes.Status501NotImplemented,
            title: "EHR polling not configured");
    }

    public static async Task<IResult> IngestFhirBundleAsync(
        FhirBundleIngestRequest request,
        InteropDbContext db,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.Problem("StudyId and SubjectId are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.SiteId) || string.IsNullOrWhiteSpace(request.EhrSystemId))
        {
            return Results.Problem("SiteId and EhrSystemId are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        string studyId = request.StudyId.Trim();
        string siteId = request.SiteId.Trim();
        string subjectId = request.SubjectId.Trim();
        string ehrSystemId = request.EhrSystemId.Trim();
        string subjectPseudonym = string.IsNullOrWhiteSpace(request.SubjectPseudonym)
            ? subjectId
            : request.SubjectPseudonym.Trim();

        if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, studyId, siteId) is not null)
        {
            return OutsideStudy();
        }

        var conflictingSites = await db.EhrReconciliationCandidateRecords
            .AsNoTracking()
            .Where(c => c.StudyId == studyId && c.SubjectId == subjectId && c.SiteId != siteId)
            .Select(c => c.SiteId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (conflictingSites.Exists(existingSite => ClinicalScopeGuard.RequireStudyAndSite(httpContext, studyId, existingSite) is not null))
        {
            return OutsideStudy();
        }

        if (conflictingSites.Count > 0)
        {
            return Results.Problem(
                title: "Conflicting subject site",
                detail: "Reconciliation candidates for this subject are already recorded under another site in this study.",
                statusCode: StatusCodes.Status409Conflict);
        }

        FhirObservationMapper.Result mapped;
        try
        {
            mapped = FhirObservationMapper.Map(
                request.RawFhirBundleJson ?? string.Empty,
                new FhirObservationMapper.Target(
                    subjectId,
                    subjectPseudonym,
                    ehrSystemId),
                clock.UtcNow);
        }
        catch (FormatException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (mapped.Candidates.Count == 0)
        {
            return Results.Problem(
                "The bundle holds no Observation that maps to a supported CDASH variable.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["skipped"] = mapped.Skipped });
        }

        var dtos = new List<EhrCandidateItemDto>();
        foreach (var candidate in mapped.Candidates.DistinctBy(c => (c.FhirResourceId, c.TargetVariable)))
        {
            candidate.StudyId = studyId;
            candidate.SiteId = siteId;

            // A resource already pending review for the same variable at this site is not filed twice.
            var existing = await db.EhrReconciliationCandidateRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.StudyId == candidate.StudyId
                        && c.SiteId == candidate.SiteId
                        && c.SubjectId == candidate.SubjectId
                        && c.FhirResourceId == candidate.FhirResourceId
                        && c.TargetVariable == candidate.TargetVariable,
                    ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                db.EhrReconciliationCandidateRecords.Add(candidate);
                dtos.Add(ToDto(candidate));
            }
            else
            {
                if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, existing.StudyId, existing.SiteId) is not null)
                {
                    return OutsideStudy();
                }

                dtos.Add(ToDto(existing));
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new FhirBundleIngestResponse(
            Success: true,
            CandidateCount: dtos.Count,
            Candidates: dtos,
            Message: mapped.Skipped.Count == 0
                ? $"Ingested {dtos.Count} Observation(s) for subject {subjectId}."
                : $"Ingested {dtos.Count} Observation(s) for subject {subjectId}; skipped {mapped.Skipped.Count}: {string.Join("; ", mapped.Skipped)}."));
    }

    public static async Task<IResult> ListCandidatesAsync(
        string? study_id,
        string? subject_id,
        string? site_id,
        string? domain,
        string? status,
        int skip,
        int limit,
        InteropDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        HashSet<string>? siteFilter = null;
        if (ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null
            && !ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, study_id, site_id, out _, out siteFilter, out var scopeDenied))
        {
            return scopeDenied!;
        }

        var query = InCallerStudies(db.EhrReconciliationCandidateRecords.AsNoTracking(), httpContext);
        if (siteFilter is not null)
        {
            var allowedSites = siteFilter.ToList();
            query = query.Where(c => c.SiteId != null && allowedSites.Contains(c.SiteId));
        }

        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(c => c.StudyId == study_id);
        }
        if (!string.IsNullOrWhiteSpace(subject_id))
        {
            query = query.Where(c => c.SubjectId == subject_id || c.SubjectPseudonym == subject_id);
        }
        if (!string.IsNullOrWhiteSpace(site_id))
        {
            query = query.Where(c => c.SiteId == site_id);
        }
        if (!string.IsNullOrWhiteSpace(domain) && domain != "ALL")
        {
            query = query.Where(c => c.TargetDomain == domain);
        }
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(c => c.Status == status);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip(skip > 0 ? skip : 0)
            .Take(limit > 0 ? limit : 50)
            .ToListAsync(ct)
            .ConfigureAwait(false);


        var dtos = items.Select(ToDto).ToList();
        return Results.Ok(new EhrCandidateListResponse(dtos, total));
    }

    public static async Task<IResult> GetCandidateByIdAsync(
        string candidate_id,
        InteropDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var item = await db.EhrReconciliationCandidateRecords.AsNoTracking().FirstOrDefaultAsync(c => c.Id == candidate_id, ct).ConfigureAwait(false);
        if (item == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return Results.Problem($"Candidate record '{candidate_id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(ToDto(item));
    }

    public static async Task<IResult> AcceptCandidateAsync(
        EhrReconcileAcceptRequest request,
        InteropDbContext db,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.EhrReconciliationCandidateRecords.FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct).ConfigureAwait(false);
        if (item == null)
        {
            return Results.Problem($"Candidate record '{request.CandidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return OutsideStudy();
        }

        var callerUser = GetCallerUser(httpContext);
        var callerRole = GetCallerRole(httpContext);
        var now = clock.UtcNow;

        item.Status = "ACCEPTED";
        item.ResolutionAction = "ACCEPT_COMMIT";
        item.ResolutionReason = string.IsNullOrWhiteSpace(request.CommitReason) ? "CRC accepted candidate data from hospital EHR" : request.CommitReason;
        item.ResolvedBy = callerUser;
        item.ResolvedAt = now;
        item.CurrentEcrfValueJson = item.CandidateValueJson;
        item.CurrentEcrfUpdatedAt = now;
        item.DiscrepancyType = "EXACT_MATCH";
        item.VersionIndex += 1;

        var eSig = new
        {
            signed_by = callerUser,
            role = callerRole,
            signed_at = now,
            meaning = "21 CFR Part 11 eSignature - Accept & Commit eSource Record",
            reason = request.ChangeReason ?? "CRC eSource Reconciliation Accept & Commit"
        };
        item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CANDIDATE_ACCEPTED",
            Details = $"Accepted candidate {item.Id} ({item.TargetVariable}) into eCRF with value {item.CandidateValueJson}",
            ChangeReason = request.ChangeReason ?? "CRC eSource Reconciliation Accept & Commit"
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: ToDto(item),
            ClinicalQueryId: null,
            Message: $"Candidate {item.TargetVariable} accepted and committed to eCRF."));
    }

    public static async Task<IResult> RejectCandidateAsync(
        EhrReconcileRejectRequest request,
        InteropDbContext db,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return Results.Problem("Mandatory clinical rationale is required when rejecting candidate EHR records under GxP rules.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.EhrReconciliationCandidateRecords.FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct).ConfigureAwait(false);
        if (item == null)
        {
            return Results.Problem($"Candidate record '{request.CandidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return OutsideStudy();
        }

        var callerUser = GetCallerUser(httpContext);
        var callerRole = GetCallerRole(httpContext);
        var now = clock.UtcNow;

        item.Status = "REJECTED";
        item.ResolutionAction = "REJECT";
        item.ResolutionReason = request.RejectionReason.Trim();
        item.ResolvedBy = callerUser;
        item.ResolvedAt = now;
        item.VersionIndex += 1;

        var eSig = new
        {
            signed_by = callerUser,
            role = callerRole,
            signed_at = now,
            meaning = "21 CFR Part 11 eSignature - Reject eSource Record",
            reason = request.RejectionReason
        };
        item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CANDIDATE_REJECTED",
            Details = $"Rejected candidate {item.Id} ({item.TargetVariable}). Clinical rationale: {request.RejectionReason}",
            ChangeReason = request.ChangeReason ?? "CRC eSource Reconciliation Reject"
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: ToDto(item),
            ClinicalQueryId: null,
            Message: $"Candidate {item.TargetVariable} rejected with recorded justification."));
    }

    public static async Task<IResult> BatchAcceptCandidatesAsync(
        EhrReconcileBatchAcceptRequest request,
        InteropDbContext db,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.CandidateIds == null || request.CandidateIds.Count == 0)
        {
            return Results.Problem("CandidateIds array cannot be empty for batch accept.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CandidateIds.Count > 100)
        {
            return Results.Problem("Batch size exceeds maximum limit of 100 candidate items.", statusCode: StatusCodes.Status400BadRequest);
        }

        var distinctIds = request.CandidateIds.Distinct().ToList();

        // One SaveChanges writes every candidate and audit entry atomically, and the context's retrying execution
        // strategy retries it as a whole. An explicit transaction here added nothing and failed under that strategy (#5383).
        var items = await db.EhrReconciliationCandidateRecords
            .Where(c => distinctIds.Contains(c.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (items.Count != distinctIds.Count)
        {
            var missingIds = distinctIds.Except(items.Select(i => i.Id)).ToList();
            return Results.Problem($"Batch operation aborted. Candidate record(s) not found: {string.Join(", ", missingIds)}", statusCode: StatusCodes.Status404NotFound);
        }

        if (items.Exists(i => ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, i.StudyId, i.SiteId) is not null))
        {
            return OutsideStudy();
        }

        var callerUser = GetCallerUser(httpContext);
        var callerRole = GetCallerRole(httpContext);
        var now = clock.UtcNow;
        var commitReason = string.IsNullOrWhiteSpace(request.CommitReason) ? "CRC batch accepted candidate data from hospital EHR" : request.CommitReason;

        foreach (var item in items)
        {
            item.Status = "ACCEPTED";
            item.ResolutionAction = "ACCEPT_COMMIT";
            item.ResolutionReason = commitReason;
            item.ResolvedBy = callerUser;
            item.ResolvedAt = now;
            item.CurrentEcrfValueJson = item.CandidateValueJson;
            item.CurrentEcrfUpdatedAt = now;
            item.DiscrepancyType = "EXACT_MATCH";
            item.VersionIndex += 1;

            var eSig = new
            {
                signed_by = callerUser,
                role = callerRole,
                signed_at = now,
                meaning = "21 CFR Part 11 eSignature - Batch Accept & Commit eSource Records",
                reason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Accept & Commit"
            };
            item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

            db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = callerUser,
                UserRole = callerRole,
                Action = "ESOURCE_CANDIDATE_BATCH_ACCEPTED",
                Details = $"Batch accepted candidate {item.Id} ({item.TargetVariable}) into eCRF with value {item.CandidateValueJson}",
                ChangeReason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Accept & Commit"
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var dtos = items.Select(ToDto).ToList();
        return Results.Ok(new EhrReconcileBatchActionResponse(
            Success: true,
            ActionCount: dtos.Count,
            Candidates: dtos,
            Message: $"Successfully batch accepted {dtos.Count} candidate items."));
    }

    public static async Task<IResult> BatchRejectCandidatesAsync(
        EhrReconcileBatchRejectRequest request,
        InteropDbContext db,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.CandidateIds == null || request.CandidateIds.Count == 0)
        {
            return Results.Problem("CandidateIds array cannot be empty for batch reject.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CandidateIds.Count > 100)
        {
            return Results.Problem("Batch size exceeds maximum limit of 100 candidate items.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return Results.Problem("Mandatory clinical rationale is required when batch rejecting candidate EHR records under GxP rules.", statusCode: StatusCodes.Status400BadRequest);
        }

        var distinctIds = request.CandidateIds.Distinct().ToList();

        // One SaveChanges writes every candidate and audit entry atomically, and the context's retrying execution
        // strategy retries it as a whole. An explicit transaction here added nothing and failed under that strategy (#5383).
        var items = await db.EhrReconciliationCandidateRecords
            .Where(c => distinctIds.Contains(c.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (items.Count != distinctIds.Count)
        {
            var missingIds = distinctIds.Except(items.Select(i => i.Id)).ToList();
            return Results.Problem($"Batch operation aborted. Candidate record(s) not found: {string.Join(", ", missingIds)}", statusCode: StatusCodes.Status404NotFound);
        }

        if (items.Exists(i => ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, i.StudyId, i.SiteId) is not null))
        {
            return OutsideStudy();
        }

        var callerUser = GetCallerUser(httpContext);
        var callerRole = GetCallerRole(httpContext);
        var now = clock.UtcNow;

        foreach (var item in items)
        {
            item.Status = "REJECTED";
            item.ResolutionAction = "REJECT";
            item.ResolutionReason = request.RejectionReason.Trim();
            item.ResolvedBy = callerUser;
            item.ResolvedAt = now;
            item.VersionIndex += 1;

            var eSig = new
            {
                signed_by = callerUser,
                role = callerRole,
                signed_at = now,
                meaning = "21 CFR Part 11 eSignature - Batch Reject eSource Records",
                reason = request.RejectionReason
            };
            item.ElectronicSignatureJson = JsonSerializer.Serialize(eSig);

            db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = callerUser,
                UserRole = callerRole,
                Action = "ESOURCE_CANDIDATE_BATCH_REJECTED",
                Details = $"Batch rejected candidate {item.Id} ({item.TargetVariable}). Clinical rationale: {request.RejectionReason}",
                ChangeReason = request.ChangeReason ?? "CRC eSource Batch Reconciliation Reject"
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var dtos = items.Select(ToDto).ToList();
        return Results.Ok(new EhrReconcileBatchActionResponse(
            Success: true,
            ActionCount: dtos.Count,
            Candidates: dtos,
            Message: $"Successfully batch rejected {dtos.Count} candidate items."));
    }

    public static async Task<IResult> RaiseClinicalQueryAsync(
        EhrReconcileQueryRequest request,
        InteropDbContext db,
        IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateId))
        {
            return Results.Problem("CandidateId is required.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (string.IsNullOrWhiteSpace(request.QueryExplanation))
        {
            return Results.Problem("Discrepancy explanation is required to raise a clinical query.", statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.EhrReconciliationCandidateRecords.FirstOrDefaultAsync(c => c.Id == request.CandidateId, ct).ConfigureAwait(false);
        if (item == null)
        {
            return Results.Problem($"Candidate record '{request.CandidateId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return OutsideStudy();
        }

        var callerUser = GetCallerUser(httpContext);
        var callerRole = GetCallerRole(httpContext);
        var now = clock.UtcNow;

        var query = new ClinicalQuery
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = item.StudyId,
            SubjectId = item.SubjectId,
            Domain = item.TargetDomain,
            TestCode = item.CdashTestcd ?? item.TargetVariable,
            Status = "OPEN",
            Explanation = request.QueryExplanation.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ClinicalQueries.Add(query);

        item.ClinicalQueryId = query.Id;
        item.Status = "QUERIED";
        item.ResolutionAction = "QUERY";
        item.ResolutionReason = request.QueryExplanation.Trim();
        item.ResolvedBy = callerUser;
        item.ResolvedAt = now;
        item.VersionIndex += 1;

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = callerUser,
            UserRole = callerRole,
            Action = "ESOURCE_CLINICAL_QUERY_RAISED",
            Details = $"Raised clinical query #{query.Id} for candidate {item.Id} ({item.TargetVariable}). Discrepancy: {request.QueryExplanation}",
            ChangeReason = request.ChangeReason ?? "CRC Raised eSource Discrepancy Query"
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new EhrReconcileActionResponse(
            Success: true,
            Candidate: ToDto(item),
            ClinicalQueryId: query.Id,
            Message: $"Clinical query #{query.Id} opened for record {item.TargetVariable}."));
    }

    public static async Task<IResult> GetProvenanceAuditAsync(
        string candidate_id,
        InteropDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var item = await db.EhrReconciliationCandidateRecords.AsNoTracking().FirstOrDefaultAsync(c => c.Id == candidate_id, ct).ConfigureAwait(false);
        if (item == null || ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, item.StudyId, item.SiteId) is not null)
        {
            return Results.Problem($"Candidate record '{candidate_id}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        string candidateMarker = $"candidate {item.Id}";
        string candidateSpaceMarker = $"candidate {item.Id} ";
        var auditLogs = (await db.InteropAuditLogs.AsNoTracking()
            .Where(a => a.Details.Contains(candidateMarker))
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .Where(a => a.Details.Contains(candidateSpaceMarker, StringComparison.Ordinal)
                || a.Details.EndsWith(candidateMarker, StringComparison.Ordinal))
            .ToList();

        object candVal = ParseJsonOrRaw(item.CandidateValueJson);
        object? ecrfVal = ParseJsonOrRaw(item.CurrentEcrfValueJson);
        object rawFhir = ParseJsonOrRaw(item.RawFhirResourceJson);
        object? eSig = ParseJsonOrRaw(item.ElectronicSignatureJson);

        return Results.Ok(new EhrProvenanceResponse(
            CandidateId: item.Id,
            StudyId: item.StudyId,
            SiteId: item.SiteId,
            SubjectId: item.SubjectId,
            SubjectPseudonym: item.SubjectPseudonym,
            EhrSystemId: item.EhrSystemId,
            FhirResourceType: item.FhirResourceType,
            FhirResourceId: item.FhirResourceId,
            SourceTimestamp: item.SourceTimestamp,
            TargetDomain: item.TargetDomain,
            TargetVariable: item.TargetVariable,
            CdashTestcd: item.CdashTestcd,
            CdashTest: item.CdashTest,
            CandidateValue: candVal,
            CandidateUnit: item.CandidateUnit,
            CurrentEcrfValue: ecrfVal,
            CurrentEcrfUpdatedAt: item.CurrentEcrfUpdatedAt,
            DiscrepancyType: item.DiscrepancyType,
            ConfidenceScore: item.ConfidenceScore,
            MappingProvenance: item.MappingProvenance,
            RawFhirResource: rawFhir,
            Status: item.Status,
            ResolutionAction: item.ResolutionAction,
            ResolutionReason: item.ResolutionReason,
            ResolvedBy: item.ResolvedBy,
            ResolvedAt: item.ResolvedAt,
            ElectronicSignature: eSig,
            ClinicalQueryId: item.ClinicalQueryId,
            CreatedAt: item.CreatedAt,
            VersionIndex: item.VersionIndex,
            AuditTrail: auditLogs));
    }

    private static EhrCandidateItemDto ToDto(EhrReconciliationCandidateRecord item)
    {
        object candVal = ParseJsonOrRaw(item.CandidateValueJson);
        object? ecrfVal = ParseJsonOrRaw(item.CurrentEcrfValueJson);
        object rawFhir = ParseJsonOrRaw(item.RawFhirResourceJson);
        object? eSig = ParseJsonOrRaw(item.ElectronicSignatureJson);

        return new EhrCandidateItemDto(
            Id: item.Id,
            StudyId: item.StudyId,
            SiteId: item.SiteId,
            SubjectId: item.SubjectId,
            SubjectPseudonym: string.IsNullOrWhiteSpace(item.SubjectPseudonym) ? item.SubjectId : item.SubjectPseudonym,
            EhrSystemId: item.EhrSystemId,
            FhirResourceType: item.FhirResourceType,
            FhirResourceId: item.FhirResourceId,
            SourceTimestamp: item.SourceTimestamp,
            TargetDomain: item.TargetDomain,
            TargetVariable: item.TargetVariable,
            CdashTestcd: item.CdashTestcd,
            CdashTest: item.CdashTest,
            CandidateValue: candVal,
            CandidateUnit: item.CandidateUnit,
            CurrentEcrfValue: ecrfVal,
            CurrentEcrfUpdatedAt: item.CurrentEcrfUpdatedAt,
            DiscrepancyType: item.DiscrepancyType,
            ConfidenceScore: item.ConfidenceScore,
            MappingProvenance: item.MappingProvenance,
            RawFhirResource: rawFhir,
            Status: item.Status,
            ResolutionAction: item.ResolutionAction,
            ResolutionReason: item.ResolutionReason,
            ResolvedBy: item.ResolvedBy,
            ResolvedAt: item.ResolvedAt,
            ElectronicSignature: eSig,
            ClinicalQueryId: item.ClinicalQueryId,
            CreatedAt: item.CreatedAt,
            VersionIndex: item.VersionIndex);
    }

    private static object ParseJsonOrRaw(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return json;
        }
    }

    // The signed caller (ADR-2516); the permission policy has already refused any request without one.
    private static string GetCallerUser(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted eSource request has no signed caller.");

    private static string GetCallerRole(HttpContext httpContext) => CallerScope.Roles(httpContext);

    private static IResult OutsideStudy() =>
        Results.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

    private static IQueryable<EhrReconciliationCandidateRecord> InCallerStudies(
        IQueryable<EhrReconciliationCandidateRecord> candidates,
        HttpContext httpContext)
    {
        var studies = CallerScope.StudyFilter(httpContext);
        return studies == null ? candidates : candidates.Where(c => studies.Contains(c.StudyId));
    }
}

// Request and Response DTOs
public record EhrEndpointCreateRequest(
    string Name,
    string? SystemType,
    string FhirBaseUrl,
    string? TokenUrl,
    string? ClientId,
    string? PublicKeyJwksUrl,
    List<string>? Scopes,
    int PollingIntervalSec,
    string? ReasonForChange,
    string? SigningKeyRef = null,
    string? SigningKeyId = null);

public record EhrEndpointListResponse(
    List<EhrEndpointRecord> Endpoints);

/// <summary>Asks for a client assertion for a registered endpoint; the key is the endpoint's, never the caller's.</summary>
public record SmartAssertionRequest(
    string EndpointId,
    int ExpirationSeconds = 300);

public record SmartAssertionResponse(
    string ClientAssertionToken,
    string ClientAssertionType,
    DateTimeOffset ExpiresAt);

public record SmartVerifyRequest(
    string ClientAssertionToken,
    string EndpointId);

public record SmartVerifyResponse(
    bool IsValid,
    string Subject,
    string Issuer,
    string Audience,
    DateTimeOffset ExpiresAt,
    string? ErrorMessage);

public record EhrPollRequest(
    string? EndpointId,
    string StudyId,
    string? SiteId,
    string SubjectId);

public record FhirBundleIngestRequest(
    string StudyId,
    string? SiteId,
    string SubjectId,
    string? SubjectPseudonym,
    string? EhrSystemId,
    string RawFhirBundleJson);

public record FhirBundleIngestResponse(
    bool Success,
    int CandidateCount,
    List<EhrCandidateItemDto> Candidates,
    string Message);

public record EhrCandidateListResponse(
    List<EhrCandidateItemDto> Candidates,
    int TotalCount);

public record EhrCandidateItemDto(
    string Id,
    string StudyId,
    string SiteId,
    string SubjectId,
    string SubjectPseudonym,
    string EhrSystemId,
    string FhirResourceType,
    string FhirResourceId,
    DateTimeOffset SourceTimestamp,
    string TargetDomain,
    string TargetVariable,
    string? CdashTestcd,
    string? CdashTest,
    object CandidateValue,
    string? CandidateUnit,
    object? CurrentEcrfValue,
    DateTimeOffset? CurrentEcrfUpdatedAt,
    string DiscrepancyType,
    double ConfidenceScore,
    string MappingProvenance,
    object RawFhirResource,
    string Status,
    string? ResolutionAction,
    string? ResolutionReason,
    string? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    object? ElectronicSignature,
    string? ClinicalQueryId,
    DateTimeOffset CreatedAt,
    int VersionIndex);

public record EhrReconcileAcceptRequest(
    string CandidateId,
    string? CommitReason,
    string? ChangeReason);

public record EhrReconcileBatchAcceptRequest(
    List<string> CandidateIds,
    string? CommitReason,
    string? ChangeReason);

public record EhrReconcileRejectRequest(
    string CandidateId,
    string RejectionReason,
    string? ChangeReason);

public record EhrReconcileBatchRejectRequest(
    List<string> CandidateIds,
    string RejectionReason,
    string? ChangeReason);

public record EhrReconcileQueryRequest(
    string CandidateId,
    string QueryExplanation,
    string? DiscrepancyType,
    string? ChangeReason);

public record EhrReconcileActionResponse(
    bool Success,
    EhrCandidateItemDto Candidate,
    string? ClinicalQueryId,
    string Message);

public record EhrReconcileBatchActionResponse(
    bool Success,
    int ActionCount,
    List<EhrCandidateItemDto> Candidates,
    string Message);

public record EhrProvenanceResponse(
    string CandidateId,
    string StudyId,
    string SiteId,
    string SubjectId,
    string SubjectPseudonym,
    string EhrSystemId,
    string FhirResourceType,
    string FhirResourceId,
    DateTimeOffset? SourceTimestamp,
    string TargetDomain,
    string TargetVariable,
    string? CdashTestcd,
    string? CdashTest,
    object CandidateValue,
    string? CandidateUnit,
    object? CurrentEcrfValue,
    DateTimeOffset? CurrentEcrfUpdatedAt,
    string DiscrepancyType,
    double ConfidenceScore,
    string MappingProvenance,
    object RawFhirResource,
    string Status,
    string? ResolutionAction,
    string? ResolutionReason,
    string? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    object? ElectronicSignature,
    string? ClinicalQueryId,
    DateTimeOffset CreatedAt,
    int VersionIndex,
    List<InteropAuditLog> AuditTrail);
