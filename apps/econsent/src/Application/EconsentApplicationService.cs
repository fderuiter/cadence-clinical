using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Econsent.Application;

public sealed class EconsentApplicationService(EconsentUnitOfWork unitOfWork, IClock clock)
{
    private EconsentDbContext DbContext => unitOfWork.DbContext;

    public async Task<IResult> CaptureConsentAsync(
        string templateId,
        int versionIndex,
        CaptureConsentRequest request,
        HttpContext httpContext,
        IOptions<BiometricLivenessOptions> livenessOptions,
        ISigTokenUseStore sigTokenUseStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(livenessOptions);
        ArgumentNullException.ThrowIfNull(sigTokenUseStore);

        if (request is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Request body is required."]
            });
        }

        if (ClinicalScopeGuard.RequireAnyStudyAndSiteScope(httpContext) is { } callerScopeDenied)
        {
            return callerScopeDenied;
        }

        if (!string.IsNullOrWhiteSpace(request.StudyId)
            && ClinicalScopeGuard.RequireStudy(httpContext, request.StudyId) is { } studyDenied)
        {
            return studyDenied;
        }

        if (!string.IsNullOrWhiteSpace(request.SiteId)
            && ClinicalScopeGuard.RequireSite(httpContext, request.SiteId) is { } siteDenied)
        {
            return siteDenied;
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

        DateTimeOffset now = clock.UtcNow;
        byte[] livenessSecret = livenessOptions.Value.DecodeKey();

        BiometricLivenessTokenData? livenessData = null;
        if (string.IsNullOrWhiteSpace(request.LivenessToken))
        {
            errors["liveness_token"] = ["Valid liveness token is required."];
        }
        else if (!BiometricLivenessTokenService.VerifyToken(
            request.LivenessToken,
            request.SubjectPseudonym,
            out livenessData,
            secret: livenessSecret,
            now: now))
        {
            errors["liveness_token"] = ["Liveness token is invalid or expired."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (ClinicalScopeGuard.RequireStudyAndSite(httpContext, request.StudyId, request.SiteId) is { } denied)
        {
            return denied;
        }

        DateTimeOffset retainUntil = DateTimeOffset.FromUnixTimeSeconds(livenessData!.ExpiresAtUnix) + BiometricLivenessTokenService.ClockSkew;
        string useStoreKey = $"{BiometricLivenessTokenService.UseStoreKeyPrefix}{livenessData.Jti}";
        if (!await sigTokenUseStore.TryRecordFirstUseAsync(useStoreKey, retainUntil, cancellationToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["liveness_token"] = ["Liveness token has already been used."]
            });
        }

        // The participant or representative signs; the record is attributed to the signed-in staff member who captured it.
        string capturedBy = CallerScope.RequiredUserId(httpContext, "An admitted eConsent request has no signed caller.");

        string subjectPseudonym = request.SubjectPseudonym!;
        string studyId = request.StudyId!;
        string siteId = request.SiteId!;
        string digestHash = request.DigestSha256!;
        string signatureData = request.SignatureData!;
        string livenessToken = request.LivenessToken!;
        string livenessTokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(livenessToken)));

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

        var bindingSha256 = ConsentSignatureBinding.Compute(digestHash, signatureData, livenessTokenHash);

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
            CreatedBy = capturedBy,
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
            CreatedBy = capturedBy,
            ReasonForChange = meaning
        };

        var livenessRecord = new BiometricLivenessVerification
        {
            Id = Guid.NewGuid().ToString(),
            ConsentId = consentRecord.Id,
            SubjectPseudonym = subjectPseudonym,
            LivenessToken = livenessToken,
            LivenessTokenHash = livenessTokenHash,
            AntiSpoofScore = livenessData?.AntiSpoofScore ?? 0.98m,
            IsLive = livenessData?.IsLive ?? true,
            Status = "VERIFIED",
            VerifiedAt = livenessData is not null ? DateTimeOffset.FromUnixTimeSeconds(livenessData.IssuedAtUnix) : now,
            CreatedAt = now,
            CreatedBy = capturedBy,
            ReasonForChange = "Facial liveness verification completed"
        };

        var auditRecord = new ConsentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = capturedBy,
            ActorRole = signerRole,
            Action = "BIOMETRIC_LIVENESS_VERIFIED",
            DocumentId = consentRecord.Id,
            Details = $"Biometric liveness verified with anti-spoof score {livenessRecord.AntiSpoofScore} for subject {subjectPseudonym}",
            ReasonForChange = "Liveness verification for consent capture",
            TenantId = ClinicalScopeGuard.ResolvePrincipal(httpContext)?.TenantId ?? CallerScope.TenantId(httpContext) ?? string.Empty,
            Sha256Digest = livenessTokenHash
        };

        DbContext.SubjectConsents.Add(consentRecord);
        DbContext.ConsentSignatures.Add(signatureRecord);
        DbContext.BiometricLivenessVerifications.Add(livenessRecord);
        DbContext.ConsentAuditLogs.Add(auditRecord);
        await unitOfWork.CommitAsync(cancellationToken);

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

    public async Task<IResult> PublishTemplateAsync(
        string templateId,
        int versionIndex,
        PublishTemplateRequest? request,
        IPublishEndpoint publishEndpoint,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        var template = await DbContext.ConsentTemplates
            .FirstOrDefaultAsync(t => t.TemplateId == templateId && t.VersionIndex == versionIndex, cancellationToken)
            ?? await DbContext.ConsentTemplates
                .FirstOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken);

        // A new template names its study; an existing one may only be published, or moved, within the caller's studies.
        string? targetStudy = !string.IsNullOrWhiteSpace(request?.StudyId) ? request.StudyId : template?.StudyId;
        if (string.IsNullOrWhiteSpace(targetStudy))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["study_id"] = ["StudyId is required to publish a new template."]
            });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, targetStudy)
            || (template is not null && !ClinicalScopeGuard.CanAccessStudy(httpContext, template.StudyId)))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        if (template is null)
        {
            template = new ConsentTemplate
            {
                Id = Guid.NewGuid().ToString(),
                TemplateId = templateId,
                StudyId = targetStudy,
                TemplateName = $"Consent Template {templateId}",
                ProtocolVersion = !string.IsNullOrWhiteSpace(request?.ProtocolVersion) ? request.ProtocolVersion : $"v{versionIndex}.0",
                IsPublished = true,
                RequiresReconsent = request?.RequiresReconsent ?? true,
                VersionIndex = versionIndex,
                CreatedAt = now,
                CreatedBy = CallerScope.RequiredUserId(httpContext, "An admitted eConsent request has no signed caller."),
                ReasonForChange = request?.ChangeSummary ?? request?.ChangeReason ?? "Publishing template"
            };
            DbContext.ConsentTemplates.Add(template);
        }
        else
        {
            template.IsPublished = true;
            if (request?.RequiresReconsent is bool requiresReconsent)
            {
                template.RequiresReconsent = requiresReconsent;
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
                Channels: request?.Channels ?? ["SMS", "Mobile Push", "Email"],
                ProtocolVersion: template.ProtocolVersion,
                OccurredAt: now);

            await publishEndpoint.Publish(reconsentEvent, cancellationToken);
        }

        await unitOfWork.CommitAsync(cancellationToken);

        var response = new PublishTemplateResponse(
            TemplateId: template.TemplateId,
            VersionIndex: template.VersionIndex,
            IsPublished: template.IsPublished,
            RequiresReconsent: template.RequiresReconsent,
            PublishedAt: now);

        return TypedResults.Ok(response);
    }
}
