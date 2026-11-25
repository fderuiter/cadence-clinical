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

public sealed class EsourceConfigurationService(
    InteropUnitOfWork uow,
    IClock clock,
    ISmartSigningKeys? signingKeys = null)
{
    /// <summary>The longest client assertion SMART Backend Services allows.</summary>
    public const int MaxAssertionLifetimeSeconds = 300;

    public async Task<IResult> ListEhrEndpointsAsync(bool activeOnly, CancellationToken ct)
    {
        var query = uow.Db.EhrEndpointRecords.AsNoTracking();
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

        uow.Db.EhrEndpointRecords.Add(record);
        await uow.CommitAsync(ct).ConfigureAwait(false);

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

        if (signingKeys is null)
        {
            return SigningKeyUnavailable("SMART signing keys are not configured.");
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

        if (signingKeys is null)
        {
            return SigningKeyUnavailable("SMART signing keys are not configured.");
        }

        if (!signingKeys.TryGetPrivateKey(endpoint.SigningKeyRef, out var rsa2, out var verifyError))
        {
            return SigningKeyUnavailable(verifyError);
        }

        using (rsa2)
        {
            using var publicKey = RSA.Create(rsa2.ExportParameters(includePrivateParameters: false));
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

    public static IResult PollHospitalEhr(EhrPollRequest request, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.Problem("StudyId and SubjectId are required for polling.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ClinicalScopeGuard.RequireStudyAndOptionalSite(httpContext, request.StudyId, request.SiteId, allowUnassignedSiteForScopedCaller: true) is not null)
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        return Results.Problem(
            "Polling an EHR is not available: no FHIR client is configured. Ingest an exported FHIR bundle instead.",
            statusCode: StatusCodes.Status501NotImplemented,
            title: "EHR polling not configured");
    }

    private static IResult SigningKeyUnavailable(string detail) =>
        Results.Problem(detail, statusCode: StatusCodes.Status422UnprocessableEntity, title: "SMART signing key unavailable");

    private async Task<EhrEndpointRecord?> ActiveEndpointAsync(string? endpointId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(endpointId)
            ? null
            : await uow.Db.EhrEndpointRecords.AsNoTracking().FirstOrDefaultAsync(e => e.Id == endpointId && e.IsActive, ct).ConfigureAwait(false);
}
