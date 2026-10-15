using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Security.Authentication;

using MassTransit;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Econsent.Endpoints;

public sealed record PublishTemplateRequest(
    [property: JsonPropertyName("study_id")] string? StudyId,
    [property: JsonPropertyName("protocol_version")] string? ProtocolVersion,
    [property: JsonPropertyName("change_summary")] string? ChangeSummary,
    [property: JsonPropertyName("change_reason")] string? ChangeReason,
    [property: JsonPropertyName("requires_reconsent")] bool? RequiresReconsent,
    [property: JsonPropertyName("channels")] string[]? Channels);

public sealed record PublishTemplateResponse(
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("is_published")] bool IsPublished,
    [property: JsonPropertyName("requires_reconsent")] bool RequiresReconsent,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);

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

        group.MapPost("/{template_id}/versions/{version_index}/publish", PublishTemplateVersionedAsync)
            .WithName("PublishTemplateVersioned")
            .Produces<PublishTemplateResponse>(StatusCodes.Status200OK);

        group.MapPost("/{template_id}/publish", PublishTemplateAsync)
            .WithName("PublishTemplate")
            .Produces<PublishTemplateResponse>(StatusCodes.Status200OK);

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

    public static async Task<IResult> ProcessCaptureConsentAsync(
        string templateId,
        int versionIndex,
        CaptureConsentRequest request,
        EconsentDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Request body is required."]
            });
        }

        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.SubjectPseudonym))
        {
            errors["subject_pseudonym"] = ["SubjectPseudonym is required."];
        }

        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            errors["study_id"] = ["StudyId is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SiteId))
        {
            errors["site_id"] = ["SiteId is required."];
        }

        if (string.IsNullOrWhiteSpace(request.DigestSha256))
        {
            errors["digest_sha256"] = ["DigestSha256 is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SignatureData))
        {
            errors["signature_data"] = ["SignatureData is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string subjectPseudonym = request.SubjectPseudonym!;
        string studyId = request.StudyId!;
        string siteId = request.SiteId!;
        string digestHash = request.DigestSha256!;
        string signatureData = request.SignatureData!;

        string signerName = !string.IsNullOrWhiteSpace(request.SignerName) ? request.SignerName : subjectPseudonym;
        string signerRole = !string.IsNullOrWhiteSpace(request.SignerRole) ? request.SignerRole : "Subject";

        string meaning;
        if (!string.IsNullOrWhiteSpace(request.Meaning))
        {
            meaning = request.Meaning;
        }
        else if (!string.IsNullOrWhiteSpace(request.SigningReason))
        {
            meaning = request.SigningReason;
        }
        else
        {
            meaning = "I agree to participate";
        }

        string protocolVersion = !string.IsNullOrWhiteSpace(request.ProtocolVersion) ? request.ProtocolVersion : "v1.0";

        var bindingSha256 = ConsentSignatureBinding.Compute(digestHash, signatureData);

        var manifestData = new
        {
            signer_name = signerName,
            signer_role = signerRole,
            meaning = meaning,
            digest = digestHash
        };
        string signatureManifestJson = JsonSerializer.Serialize(manifestData);

        var consentRecord = new SubjectConsent
        {
            Id = Guid.NewGuid().ToString(),
            SubjectPseudonym = subjectPseudonym,
            StudyId = studyId,
            SiteId = siteId,
            TemplateId = templateId,
            VersionIndex = versionIndex,
            ProtocolVersion = protocolVersion,
            SourceContentIdentity = digestHash,
            Status = "ACTIVE",
            ServerTimestamp = now,
            DeviceTimestamp = request.DeviceTimestamp ?? now,
            CreatedAt = now,
            CreatedBy = signerName,
            ReasonForChange = meaning,
            SignatureManifestJson = signatureManifestJson
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

    private static async Task<IResult> PublishTemplateVersionedAsync(
        string template_id,
        int version_index,
        PublishTemplateRequest? request,
        EconsentDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return await ProcessPublishTemplateAsync(template_id, version_index, request, dbContext, publishEndpoint, timeProvider, cancellationToken);
    }

    private static async Task<IResult> PublishTemplateAsync(
        string template_id,
        PublishTemplateRequest? request,
        EconsentDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        return await ProcessPublishTemplateAsync(template_id, 1, request, dbContext, publishEndpoint, timeProvider, cancellationToken);
    }

    public static async Task<IResult> ProcessPublishTemplateAsync(
        string templateId,
        int versionIndex,
        PublishTemplateRequest? request,
        EconsentDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        var template = await dbContext.ConsentTemplates
            .FirstOrDefaultAsync(t => t.TemplateId == templateId && t.VersionIndex == versionIndex, cancellationToken)
            ?? await dbContext.ConsentTemplates
                .FirstOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken);

        if (template is null)
        {
            template = new ConsentTemplate
            {
                Id = Guid.NewGuid().ToString(),
                TemplateId = templateId,
                StudyId = !string.IsNullOrWhiteSpace(request?.StudyId) ? request.StudyId : "STUDY-101",
                TemplateName = $"Consent Template {templateId}",
                ProtocolVersion = !string.IsNullOrWhiteSpace(request?.ProtocolVersion) ? request.ProtocolVersion : $"v{versionIndex}.0",
                IsPublished = true,
                RequiresReconsent = request?.RequiresReconsent ?? true,
                VersionIndex = versionIndex,
                CreatedAt = now,
                CreatedBy = "svc:econsent",
                ReasonForChange = request?.ChangeSummary ?? request?.ChangeReason ?? "Publishing template"
            };
            dbContext.ConsentTemplates.Add(template);
        }
        else
        {
            template.IsPublished = true;
            if (request?.RequiresReconsent.HasValue == true)
            {
                template.RequiresReconsent = request.RequiresReconsent.Value;
            }
            if (!string.IsNullOrWhiteSpace(request?.ProtocolVersion))
            {
                template.ProtocolVersion = request.ProtocolVersion;
            }
            if (!string.IsNullOrWhiteSpace(request?.StudyId))
            {
                template.StudyId = request.StudyId;
            }
            if (!string.IsNullOrWhiteSpace(request?.ChangeSummary) || !string.IsNullOrWhiteSpace(request?.ChangeReason))
            {
                template.ReasonForChange = request.ChangeSummary ?? request.ChangeReason!;
            }
        }

        if (template.RequiresReconsent)
        {
            string changeSummary;
            if (!string.IsNullOrWhiteSpace(request?.ChangeSummary))
            {
                changeSummary = request.ChangeSummary;
            }
            else if (!string.IsNullOrWhiteSpace(request?.ChangeReason))
            {
                changeSummary = request.ChangeReason;
            }
            else
            {
                changeSummary = "Updated informed consent form requires re-consent.";
            }

            var reconsentEvent = new EconsentReconsentRequired(
                StudyId: template.StudyId,
                TemplateId: template.TemplateId,
                VersionIndex: template.VersionIndex,
                ChangeSummary: changeSummary,
                Channels: request?.Channels ?? new[] { "SMS", "Mobile Push", "Email" },
                ProtocolVersion: template.ProtocolVersion,
                OccurredAt: now
            );

            await publishEndpoint.Publish(reconsentEvent, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new PublishTemplateResponse(
            TemplateId: template.TemplateId,
            VersionIndex: template.VersionIndex,
            IsPublished: template.IsPublished,
            RequiresReconsent: template.RequiresReconsent,
            PublishedAt: now);

        return TypedResults.Ok(response);
    }
}
