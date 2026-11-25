using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Templating;

namespace Cadence.Apps.Ctms.Application;

/// <summary>
/// Engine for compiling GxP PDF documents and applying 21 CFR Part 11 signatures.
/// </summary>
public static class GxpPdfCompiler
{
    public static Dictionary<string, object?> BuildTemplateModel(
        SiteVisitLetterModel request,
        bool isSigned,
        string? signerName,
        string? signingTimestamp,
        string? signingPurpose,
        string? integrityHash)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cra_id"] = request.CraId,
            ["created_at"] = request.CreatedAt,
            ["study_id"] = request.StudyId,
            ["site_id"] = request.SiteId,
            ["visit_type"] = request.VisitType,
            ["scheduled_date"] = request.ScheduledDate,
            ["actual_date"] = request.ActualDate,
            ["findings"] = request.Findings,
            ["is_signed"] = isSigned,
            ["signer_name"] = signerName,
            ["signing_timestamp"] = signingTimestamp,
            ["signing_purpose"] = signingPurpose,
            ["integrity_hash"] = integrityHash
        };
    }

    /// <summary>
    /// Compiles a GxP PDF document from Liquid template and model data.
    /// </summary>
    public static async Task<GxpPdfResult> CompileLetterPdfAsync(
        string templateName,
        SiteVisitLetterModel request,
        ITemplateRenderer renderer,
        TemplateCatalog catalog,
        IClock clock)
    {
        var now = clock.UtcNow;
        var visitId = request.VisitId ?? ("VISIT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant());
        var signerName = request.SignerName ?? request.CraId;
        var signingPurpose = request.SigningPurpose ?? "21 CFR Part 11 Electronic Signature / Visit Letter Approval";
        var signingTimestamp = now.ToString("o");

        var modelDict = BuildTemplateModel(
            request,
            isSigned: request.Finalize,
            signerName: request.Finalize ? signerName : null,
            signingTimestamp: request.Finalize ? signingTimestamp : null,
            signingPurpose: request.Finalize ? signingPurpose : null,
            integrityHash: null);

        string htmlContent = await renderer.RenderAsync(catalog, templateName, modelDict);

        var bodyBytes = Encoding.UTF8.GetBytes(htmlContent);
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(bodyBytes));

        modelDict["integrity_hash"] = bodyHash;
        htmlContent = await renderer.RenderAsync(catalog, templateName, modelDict);

        var pdfBuilder = new StringBuilder();
        pdfBuilder.AppendLine("%PDF-1.7");
        pdfBuilder.AppendLine($"% Cadence Clinical GxP PDF Document - {templateName}");
        pdfBuilder.AppendLine($"% Study: {request.StudyId} | Site: {request.SiteId} | Visit: {visitId}");
        if (!request.Finalize)
        {
            pdfBuilder.AppendLine("% Watermark: DRAFT - UNSIGNED WORKING COPY");
        }
        pdfBuilder.AppendLine($"% Content-SHA256: {bodyHash}");
        pdfBuilder.AppendLine();
        pdfBuilder.AppendLine(htmlContent);
        pdfBuilder.AppendLine();
        pdfBuilder.AppendLine("%%EOF");

        byte[] rawPdfBytes = Encoding.UTF8.GetBytes(pdfBuilder.ToString().TrimEnd());

        byte[] finalPdfBytes;
        string signatureManifestationJson = "{}";

        if (request.Finalize)
        {
            var (cert, key) = Pkcs7Signer.GenerateSigningCredentials(signerName, clock);
            var signer = new Pkcs7Signer(cert, key);
            finalPdfBytes = signer.SignPdf(rawPdfBytes);

            signatureManifestationJson = JsonSerializer.Serialize(new
            {
                signer = signerName,
                timestamp = signingTimestamp,
                purpose = signingPurpose,
                certificate_subject = cert.Subject,
                signature_algorithm = "SHA256withRSA-PSS"
            });
        }
        else
        {
            finalPdfBytes = rawPdfBytes;
        }

        var pdfHash = Convert.ToHexStringLower(SHA256.HashData(finalPdfBytes));

        return new GxpPdfResult(
            PdfBytes: finalPdfBytes,
            Sha256Hash: pdfHash,
            IsSigned: request.Finalize,
            SignatureManifestationJson: signatureManifestationJson,
            RenderedHtml: htmlContent
        );
    }
}
