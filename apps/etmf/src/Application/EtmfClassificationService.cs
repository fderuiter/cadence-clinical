using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Services;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Storage;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Etmf.Application;

/// <summary>
/// Stateless eTMF DIA Reference Model taxonomy, classification heuristics, binary payload decoding,
/// and caller scope/reason helpers shared across eTMF application services.
/// </summary>
public static class EtmfClassificationService
{
    internal const int MaxStoredExcerptChars = 8192;

    public static Task<IResult> ClassifyDocumentAsync(ClassifyRequest request, IClock clock)
    {
        if (!TmfDocument.IsValidPathSegment(request.Filename))
        {
            return Task.FromResult<IResult>(TypedResults.BadRequest(new
            {
                error = "filename must be a single valid file name without path separators, control characters, or '.' / '..' segments."
            }));
        }

        var report = PerformClassification(
            docId: null,
            filename: request.Filename,
            mimeType: request.MimeType ?? "application/pdf",
            content: request.Content,
            hint: request.ContentHint,
            now: clock.UtcNow);

        return Task.FromResult<IResult>(TypedResults.Ok(report));
    }

    public static Task<IResult> GetTaxonomyAsync(string? version)
    {
        _ = version;
        var taxonomy = GetStandardTaxonomy();
        return Task.FromResult<IResult>(TypedResults.Ok(taxonomy));
    }

    internal static IResult? CheckStudyAndOptionalSiteScope(HttpContext httpContext, string? studyCode, string? siteCode) =>
        ClinicalScopeGuard.RequireStudyAndOptionalSite(
            httpContext,
            studyCode,
            siteCode,
            allowUnassignedSiteForScopedCaller: true,
            code: ClinicalScopeGuard.ForbiddenCode);

    internal static string? ResolveChangeReason(string? bodyReason, HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(bodyReason))
        {
            return bodyReason.Trim();
        }

        if (ChangeReasonHeader.Read(httpContext.Request) is { Length: > 0 } headerReason)
        {
            return headerReason.Trim();
        }

        if (CallerScope.Principal(httpContext)?.ChangeReason is { Length: > 0 } principalReason
            && !string.IsNullOrWhiteSpace(principalReason))
        {
            return principalReason.Trim();
        }

        return null;
    }

    internal static string SanitizeTextExcerpt(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string withoutNuls = raw.Replace('\0', ' ');
        return withoutNuls.Length <= MaxStoredExcerptChars
            ? withoutNuls
            : withoutNuls[..MaxStoredExcerptChars];
    }

    internal static bool TryDecodeDocumentPayload(
        string? contentBase64,
        string? contentText,
        out byte[] bytes,
        out string textExcerpt,
        out string? errorMessage)
    {
        bytes = [];
        textExcerpt = string.Empty;
        errorMessage = null;

        if (!string.IsNullOrWhiteSpace(contentBase64))
        {
            string normalized = contentBase64.Trim();
            int commaIndex = normalized.IndexOf(',', StringComparison.Ordinal);
            if (commaIndex >= 0 && normalized[..commaIndex].Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[(commaIndex + 1)..].Trim();
            }

            try
            {
                bytes = Convert.FromBase64String(normalized);
            }
            catch (FormatException)
            {
                errorMessage = "content_base64 is not valid Base64-encoded binary data.";
                return false;
            }

            if (bytes.Length == 0)
            {
                errorMessage = "Document binary payload cannot be empty.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(contentText))
            {
                textExcerpt = SanitizeTextExcerpt(contentText);
            }
            else
            {
                int sampleLen = Math.Min(bytes.Length, MaxStoredExcerptChars);
                textExcerpt = SanitizeTextExcerpt(Encoding.UTF8.GetString(bytes, 0, sampleLen));
            }

            return true;
        }

        if (!string.IsNullOrEmpty(contentText))
        {
            bytes = Encoding.UTF8.GetBytes(contentText);
            textExcerpt = SanitizeTextExcerpt(contentText);
            return true;
        }

        errorMessage = "Document binary payload or text content is required; empty uploads are not permitted.";
        return false;
    }

    internal static async Task<(string ObjectKey, string Sha256)> PersistBinaryPayloadAsync(
        IStoragePort<byte[]>? storage,
        string studyCode,
        string docId,
        string filename,
        string mimeType,
        byte[] bytes,
        CancellationToken ct)
    {
        string sha256 = ComputeSha256(bytes);
        string objectKey = EtmfExportPackager.BuildDocumentObjectKey(studyCode, docId, filename);
        if (storage is not null)
        {
            sha256 = await storage.PutObjectAsync(
                key: objectKey,
                data: bytes,
                contentType: mimeType,
                expectedSha256: sha256,
                ct: ct);
        }

        return (objectKey, sha256);
    }

    internal static DocumentIntelligenceReport PerformClassification(
        string? docId,
        string filename,
        string mimeType,
        string? content,
        string? hint,
        DateTimeOffset now,
        string? sha256Override = null)
    {
        var textToScan = $"{filename} {hint} {content}".ToLowerInvariant();
        var primary = PredictTaxonomy(textToScan);

        var sha256 = !string.IsNullOrWhiteSpace(sha256Override)
            ? sha256Override
            : ComputeSha256(content ?? filename);

        string tier = "LOW";
        if (primary.Confidence >= 0.85)
        {
            tier = "HIGH";
        }
        else if (primary.Confidence >= 0.70)
        {
            tier = "MEDIUM";
        }

        var recommendation = tier == "HIGH" ? "AUTO_CLASSIFY" : "CRA_QC_REQUIRED";

        var manifest = new Dictionary<string, object>
        {
            ["model"] = "Cadence-eTMF-Classifier-v3.2",
            ["prompt_hash"] = ComputeSha256(textToScan)[..16],
            ["confidence"] = primary.Confidence,
            ["confidence_tier"] = tier,
            ["timestamp"] = now.ToString("o")
        };

        var alternatives = new List<TaxonomyMatchCandidate>
        {
            new(1, "01.01", "01.01.01", "Clinical Trial Protocol", Math.Max(0.1, primary.Confidence - 0.2)),
            new(5, "05.02", "05.02.01", "FDA Form 1572", Math.Max(0.1, primary.Confidence - 0.3))
        };

        return new DocumentIntelligenceReport(
            DocumentId: docId,
            Filename: filename,
            MimeType: mimeType,
            Sha256Hash: sha256,
            Modality: mimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? "PDF_BINARY" : "TEXT",
            PrimaryClassification: primary,
            AlternativeCandidates: alternatives,
            ConfidenceTier: tier,
            QcRecommendation: recommendation,
            AiGenerationManifest: manifest
        );
    }

    internal static TaxonomyMatchCandidate PredictTaxonomy(string text)
    {
        if (text.Contains("protocol") || text.Contains("amendment"))
            return new TaxonomyMatchCandidate(1, "01.01", "01.01.01", "Clinical Trial Protocol", 0.96);

        if (text.Contains("1572") || text.Contains("fda 1572"))
            return new TaxonomyMatchCandidate(5, "05.02", "05.02.01", "FDA Form 1572", 0.98);

        if (text.Contains("financial") || text.Contains("disclosure"))
            return new TaxonomyMatchCandidate(5, "05.02", "05.02.02", "Financial Disclosure", 0.94);

        if (text.Contains("cv") || text.Contains("curriculum") || text.Contains("resume"))
            return new TaxonomyMatchCandidate(5, "05.02", "05.02.03", "Investigator CV", 0.95);

        if (text.Contains("delegation") || text.Contains("doa") || text.Contains("log"))
            return new TaxonomyMatchCandidate(5, "05.02", "05.02.04", "Delegation of Authority Log", 0.92);

        if (text.Contains("consent") || text.Contains("icf") || text.Contains("informed"))
            return new TaxonomyMatchCandidate(5, "05.02", "05.02.05", "Informed Consent Form", 0.97);

        if (text.Contains("brochure") || text.Contains("ib"))
            return new TaxonomyMatchCandidate(2, "02.01", "02.01.01", "Investigator's Brochure", 0.95);

        if (text.Contains("irb") || text.Contains("approval") || text.Contains("iec"))
            return new TaxonomyMatchCandidate(4, "04.02", "04.02.01", "IRB/IEC Approval Notification", 0.91);

        if (text.Contains("sae") || text.Contains("adverse") || text.Contains("safety"))
            return new TaxonomyMatchCandidate(7, "07.01", "07.01.01", "Serious Adverse Event Report", 0.93);

        if (text.Contains("lab") || text.Contains("certificate"))
            return new TaxonomyMatchCandidate(8, "08.01", "08.01.01", "Central Laboratory Certificate", 0.89);

        if (text.Contains("sap") || text.Contains("statistic"))
            return new TaxonomyMatchCandidate(11, "11.01", "11.01.01", "Statistical Analysis Plan", 0.92);

        return new TaxonomyMatchCandidate(1, "01.01", "01.01.01", "General Clinical Document", 0.65);
    }

    internal static TaxonomyMatchCandidate ResolveTaxonomyByCode(string artifactCode)
    {
        return artifactCode switch
        {
            "01.01.01" => new TaxonomyMatchCandidate(1, "01.01", "01.01.01", "Clinical Trial Protocol", 1.0),
            "01.01.02" => new TaxonomyMatchCandidate(1, "01.01", "01.01.02", "Clinical Trial Protocol Amendment", 1.0),
            "01.01.03" => new TaxonomyMatchCandidate(1, "01.01", "01.01.03", "Clinical Trial Protocol Signoff", 1.0),
            "02.01.01" => new TaxonomyMatchCandidate(2, "02.01", "02.01.01", "Investigator's Brochure", 1.0),
            "04.02.01" => new TaxonomyMatchCandidate(4, "04.02", "04.02.01", "IRB/IEC Approval Notification", 1.0),
            "05.02.01" => new TaxonomyMatchCandidate(5, "05.02", "05.02.01", "FDA Form 1572", 1.0),
            "05.02.02" => new TaxonomyMatchCandidate(5, "05.02", "05.02.02", "Financial Disclosure", 1.0),
            "05.02.03" => new TaxonomyMatchCandidate(5, "05.02", "05.02.03", "Investigator CV", 1.0),
            "05.02.04" => new TaxonomyMatchCandidate(5, "05.02", "05.02.04", "Delegation of Authority Log", 1.0),
            "05.02.05" => new TaxonomyMatchCandidate(5, "05.02", "05.02.05", "Informed Consent Form", 1.0),
            "07.01.01" => new TaxonomyMatchCandidate(7, "07.01", "07.01.01", "Serious Adverse Event Report", 1.0),
            "08.01.01" => new TaxonomyMatchCandidate(8, "08.01", "08.01.01", "Central Laboratory Certificate", 1.0),
            "11.01.01" => new TaxonomyMatchCandidate(11, "11.01", "11.01.01", "Statistical Analysis Plan", 1.0),
            _ => new TaxonomyMatchCandidate(1, "01.01", artifactCode, "Custom Artifact", 1.0)
        };
    }

    internal static string ComputeSha256(string content) =>
        ComputeSha256(Encoding.UTF8.GetBytes(content));

    internal static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static List<object> GetStandardTaxonomy()
    {
        return
        [
            new { zone = 1, zone_name = "Trial Management", section = "01.01", section_name = "Trial Design", artifact_code = "01.01.01", artifact_name = "Clinical Trial Protocol" },
            new { zone = 2, zone_name = "Central Trial Documents", section = "02.01", section_name = "Product Information", artifact_code = "02.01.01", artifact_name = "Investigator's Brochure" },
            new { zone = 4, zone_name = "IRB/IEC & other Approvals", section = "04.02", section_name = "IRB/IEC Approvals", artifact_code = "04.02.01", artifact_name = "IRB/IEC Approval Notification" },
            new { zone = 5, zone_name = "Site Management", section = "05.02", section_name = "Investigator Qualification", artifact_code = "05.02.01", artifact_name = "FDA Form 1572" },
            new { zone = 5, zone_name = "Site Management", section = "05.02", section_name = "Investigator Qualification", artifact_code = "05.02.02", artifact_name = "Financial Disclosure" },
            new { zone = 5, zone_name = "Site Management", section = "05.02", section_name = "Investigator Qualification", artifact_code = "05.02.03", artifact_name = "Investigator CV" },
            new { zone = 5, zone_name = "Site Management", section = "05.02", section_name = "Investigator Qualification", artifact_code = "05.02.04", artifact_name = "Delegation of Authority Log" },
            new { zone = 5, zone_name = "Site Management", section = "05.02", section_name = "Investigator Qualification", artifact_code = "05.02.05", artifact_name = "Informed Consent Form" },
            new { zone = 7, zone_name = "Safety Reporting", section = "07.01", section_name = "Safety Notifications", artifact_code = "07.01.01", artifact_name = "Serious Adverse Event Report" },
            new { zone = 8, zone_name = "Centralized & Local Testing", section = "08.01", section_name = "Lab Documentation", artifact_code = "08.01.01", artifact_name = "Central Laboratory Certificate" },
            new { zone = 11, zone_name = "Statistics", section = "11.01", section_name = "Statistical Analysis", artifact_code = "11.01.01", artifact_name = "Statistical Analysis Plan" }
        ];
    }
}
