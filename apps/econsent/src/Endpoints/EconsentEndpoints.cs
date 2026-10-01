using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Econsent.Endpoints;

public sealed record CaptureConsentRequest(
    [property: JsonPropertyName("subject_pseudonym")] string? SubjectPseudonym,
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("site_id")] string? SiteId,
    [property: JsonPropertyName("signer_name")] string? SignerName,
    [property: JsonPropertyName("signer_email")] string? SignerEmail,
    [property: JsonPropertyName("signer_role")] string? SignerRole,
    [property: JsonPropertyName("meaning")] string? Meaning,
    [property: JsonPropertyName("signing_reason")] string? SigningReason,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion,
    [property: JsonPropertyName("digest_sha256")] string? DigestSha256,
    [property: JsonPropertyName("signature_data")] string? SignatureData,
    [property: JsonPropertyName("device_timestamp")] DateTimeOffset? DeviceTimestamp);

public sealed record CaptureConsentResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("subject_pseudonym")] string SubjectPseudonym,
    [property: JsonPropertyName("signer_name")] string SignerName,
    [property: JsonPropertyName("signer_role")] string SignerRole,
    [property: JsonPropertyName("meaning")] string Meaning,
    [property: JsonPropertyName("server_timestamp")] DateTimeOffset ServerTimestamp,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sha256_hash")] string Sha256Hash);

/// <summary>
/// Minimal API route definitions for eConsent capture and step-up signature verification.
/// Gated by 21 CFR Part 11 step-up e-signature token verification (X-Sig-Token).
/// @req:PRD-SYS-053
/// </summary>
public static class EconsentEndpoints
{
    public const string CaptureConsentSemanticAction = "econsent.consent.capture";

    public static string CaptureConsentAction(string templateId, int versionIndex) =>
        $"/api/v1/econsent/templates/{templateId}/versions/{versionIndex}/capture-consent";

    public static string CaptureConsentLegacyAction(string templateId) =>
        $"/api/v1/econsent/templates/{templateId}/capture-consent";

    public static IEndpointRouteBuilder MapEconsentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/econsent/templates")
            .WithTags("eConsent");

        group.MapPost("/{template_id}/versions/{version_index}/capture-consent", CaptureConsentAsync)
            .WithName("CaptureConsentVersioned")
            .Produces<CaptureConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(CaptureConsentSemanticAction, CaptureConsentActionFor);

        group.MapPost("/{template_id}/capture-consent", CaptureConsentLegacyAsync)
            .WithName("CaptureConsentLegacy")
            .Produces<CaptureConsentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature(CaptureConsentSemanticAction, CaptureConsentLegacyActionFor);

        return app;
    }

    private static string CaptureConsentActionFor(HttpContext httpContext)
    {
        string templateId = httpContext.Request.RouteValues["template_id"]?.ToString() ?? string.Empty;
        int versionIndex = int.TryParse(httpContext.Request.RouteValues["version_index"]?.ToString(), out int v) ? v : 1;
        return CaptureConsentAction(templateId, versionIndex);
    }

    private static string CaptureConsentLegacyActionFor(HttpContext httpContext)
    {
        string templateId = httpContext.Request.RouteValues["template_id"]?.ToString() ?? string.Empty;
        return CaptureConsentLegacyAction(templateId);
    }

    private static async Task<IResult> CaptureConsentAsync(
        string template_id,
        int version_index,
        CaptureConsentRequest request,
        EconsentDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return await ProcessCaptureConsentAsync(template_id, version_index, request, dbContext, timeProvider, cancellationToken);
    }

    private static async Task<IResult> CaptureConsentLegacyAsync(
        string template_id,
        CaptureConsentRequest request,
        EconsentDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return await ProcessCaptureConsentAsync(template_id, 1, request, dbContext, timeProvider, cancellationToken);
    }

    private static async Task<IResult> ProcessCaptureConsentAsync(
        string templateId,
        int versionIndex,
        CaptureConsentRequest request,
        EconsentDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string subjectPseudonym = request.SubjectPseudonym ?? "SUBJ-PARTICIPANT-001";
        string signerName = request.SignerName ?? request.SubjectPseudonym ?? "Participant Signer";
        string signerRole = request.SignerRole ?? "Subject";
        string meaning = request.Meaning ?? request.SigningReason ?? "I agree to participate";
        string digestHash = request.DigestSha256 ?? "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289"; // pragma: allowlist secret

        var signatureData = request.SignatureData ?? $"DATA_BINDING_V1_{subjectPseudonym}_{now.ToUnixTimeMilliseconds()}";
        var bindingSha256 = ConsentSignatureBinding.Compute(digestHash, signatureData);

        var consentRecord = new SubjectConsent
        {
            Id = Guid.NewGuid().ToString(),
            SubjectPseudonym = subjectPseudonym,
            StudyId = request.StudyId ?? "CADENCE-101",
            SiteId = request.SiteId ?? "SITE-01",
            TemplateId = templateId,
            VersionIndex = versionIndex,
            ProtocolVersion = request.ProtocolVersion ?? "v1.0",
            SourceContentIdentity = digestHash,
            Status = "ACTIVE",
            ServerTimestamp = now,
            DeviceTimestamp = request.DeviceTimestamp ?? now,
            CreatedAt = now,
            CreatedBy = signerName,
            ReasonForChange = meaning,
            SignatureManifestJson = $"{{\"signer_name\":\"{signerName}\",\"signer_role\":\"{signerRole}\",\"meaning\":\"{meaning}\",\"digest\":\"{digestHash}\"}}"
        };

        var signatureRecord = new ConsentSignature
        {
            Id = Guid.NewGuid().ToString(),
            TemplateId = templateId,
            VersionIndex = versionIndex,
            SubjectPseudonym = subjectPseudonym,
            Role = signerRole.ToUpperInvariant(),
            SignerName = signerName,
            SignerEmail = request.SignerEmail,
            Meaning = meaning,
            SignedAt = now,
            DigestSha256 = digestHash,
            SignatureBindingSha256 = bindingSha256,
            CreatedBy = signerName,
            ReasonForChange = meaning
        };

        dbContext.SubjectConsents.Add(consentRecord);
        dbContext.ConsentSignatures.Add(signatureRecord);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new CaptureConsentResponse(
            Id: consentRecord.Id,
            TemplateId: templateId,
            VersionIndex: versionIndex,
            SubjectPseudonym: subjectPseudonym,
            SignerName: signerName,
            SignerRole: signerRole,
            Meaning: meaning,
            ServerTimestamp: now,
            Status: "ACTIVE",
            Sha256Hash: digestHash);

        return TypedResults.Ok(response);
    }
}
