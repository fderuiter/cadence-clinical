using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Cadence.Apps.Interop.Application.Services;

public interface IEsourceIngestionService
{
    Task<IResult> ListEhrEndpointsAsync(bool activeOnly, CancellationToken ct);
    Task<IResult> RegisterEhrEndpointAsync(EhrEndpointCreateRequest request, HttpContext httpContext, CancellationToken ct);
    Task<IResult> GenerateClientAssertionAsync(SmartAssertionRequest request, CancellationToken ct);
    Task<IResult> VerifyClientAssertionAsync(SmartVerifyRequest request, CancellationToken ct);
    Task<IResult> IngestFhirBundleAsync(FhirBundleIngestRequest request, HttpContext httpContext, CancellationToken ct);
}

public sealed class EsourceIngestionService(
    InteropDbContext db,
    IInteropUnitOfWork unitOfWork,
    ISmartSigningKeys signingKeys,
    IClock clock) : IEsourceIngestionService
{
    public const int MaxAssertionLifetimeSeconds = 300;

    public async Task<IResult> ListEhrEndpointsAsync(bool activeOnly, CancellationToken ct)
    {
        var query = db.EhrEndpointRecords.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(e => e.IsActive);
        }

        var list = await query.ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new EhrEndpointListResponse(list));
    }

    public async Task<IResult> RegisterEhrEndpointAsync(
        EhrEndpointCreateRequest request,
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

        var caller = CallerScope.RequiredUserId(httpContext, "An admitted eSource request has no signed caller.");
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
        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/v1/interop/esource/endpoints/{record.Id}", record);
    }

    public async Task<IResult> GenerateClientAssertionAsync(SmartAssertionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EndpointId))
        {
            return Results.Problem("EndpointId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ExpirationSeconds is < 1 or > MaxAssertionLifetimeSeconds)
        {
            return Results.Problem(
                $"ExpirationSeconds must be between 1 and {MaxAssertionLifetimeSeconds}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var endpoint = await ActiveEndpointAsync(request.EndpointId, ct).ConfigureAwait(false);
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

    public async Task<IResult> VerifyClientAssertionAsync(SmartVerifyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ClientAssertionToken) || string.IsNullOrWhiteSpace(request.EndpointId))
        {
            return Results.Problem("ClientAssertionToken and EndpointId are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var endpoint = await ActiveEndpointAsync(request.EndpointId, ct).ConfigureAwait(false);
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

    public async Task<IResult> IngestFhirBundleAsync(
        FhirBundleIngestRequest request,
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
            return ClinicalScopeGuard.OutsideStudy();
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
            return ClinicalScopeGuard.OutsideStudy();
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
                dtos.Add(EsourceReconciliationService.ToDto(candidate));
            }
            else
            {
                if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, existing.StudyId, existing.SiteId) is not null)
                {
                    return ClinicalScopeGuard.OutsideStudy();
                }

                dtos.Add(EsourceReconciliationService.ToDto(existing));
            }
        }

        await unitOfWork.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new FhirBundleIngestResponse(
            Success: true,
            CandidateCount: dtos.Count,
            Candidates: dtos,
            Message: mapped.Skipped.Count == 0
                ? $"Ingested {dtos.Count} Observation(s) for subject {subjectId}."
                : $"Ingested {dtos.Count} Observation(s) for subject {subjectId}; skipped {mapped.Skipped.Count}: {string.Join("; ", mapped.Skipped)}."));
    }

    private static IResult SigningKeyUnavailable(string detail) =>
        Results.Problem(detail, statusCode: StatusCodes.Status422UnprocessableEntity, title: "SMART signing key unavailable");

    private async Task<EhrEndpointRecord?> ActiveEndpointAsync(string? endpointId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(endpointId)
            ? null
            : await db.EhrEndpointRecords.AsNoTracking().FirstOrDefaultAsync(e => e.Id == endpointId && e.IsActive, ct).ConfigureAwait(false);
}
