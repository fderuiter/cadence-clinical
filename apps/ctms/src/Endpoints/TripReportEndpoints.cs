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
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Ctms.Endpoints;

/// <summary>
/// Structured visit questionnaire engine and pre-configured CRA trip report pipeline.
/// @req:PRD-CTMS-003
/// </summary>
public static class TripReportEndpoints
{
    public const string TripReportPermission = "ctms_monitoring_visit:create";

    /// <summary>
    /// Pre-configured Liquid templates registered in TemplateCatalog for PSV, SIV, IMV, and COV.
    /// </summary>
    public static TemplateCatalog Catalog { get; } = new(
        typeof(TripReportEndpoints).Assembly,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["psv_trip_report"] = "psv_trip_report.liquid",
            ["siv_trip_report"] = "siv_trip_report.liquid",
            ["imv_trip_report"] = "imv_trip_report.liquid",
            ["cov_trip_report"] = "cov_trip_report.liquid",
        });

    public static RouteGroupBuilder MapTripReportEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/trip-reports/{visitId}", GetOrInitializeFormAsync)
            .WithName("GetTripReportForm")
            .WithSummary("Retrieves or initializes a structured visit report questionnaire form for a monitoring visit.")
            .Produces<TripReportFormDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(TripReportPermission);

        group.MapPost("/trip-reports/save", SaveDraftAsync)
            .WithName("SaveTripReportDraft")
            .WithSummary("Saves section responses and verification indicators progressively for a draft trip report.")
            .Produces<TripReportFormDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(TripReportPermission);

        group.MapPost("/trip-reports/render", RenderTripReportAsync)
            .WithName("RenderTripReportHtml")
            .WithSummary("Renders a trip report HTML output via Liquid template.")
            .Produces<string>(StatusCodes.Status200OK, "text/plain")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(TripReportPermission);

        group.MapPost("/trip-reports/submit", SubmitAndSignAsync)
            .WithName("SubmitTripReport")
            .WithSummary("Submits and signs a trip report, compiling GxP PDF and filing to eTMF repository.")
            .Produces<TripReportCompilationResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(TripReportPermission);

        group.MapPost("/trip-reports/compile", CompilePdfAsync)
            .WithName("CompileTripReportPdf")
            .WithSummary("Compiles a GxP-compliant PDF trip report directly with Part 11 signature and eTMF filing.")
            .Produces<TripReportCompilationResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(TripReportPermission);

        group.MapGet("/trip-reports/{tripReportId}/pdf", GetTripReportPdfAsync)
            .WithName("GetTripReportPdf")
            .WithSummary("Downloads compiled GxP PDF for a trip report.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(TripReportPermission);

        group.MapPost("/trip-reports/findings/attach", AttachFindingAsync)
            .WithName("AttachFindingToSection")
            .WithSummary("Attaches a CtmsMonitoringVisitFinding directly to a questionnaire section or question item.")
            .Produces<SiteVisitFindingModel>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(TripReportPermission);

        return group;
    }

    /// <summary>
    /// Gets existing questionnaire or initializes a default questionnaire for the visit type.
    /// </summary>
    public static async Task<IResult> GetOrInitializeFormAsync(
        string visitId,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = httpContext.User?.Identity?.Name ?? "cra_fderuiter";
        var now = clock.UtcNow;

        var tripReport = await dbContext.CtmsTripReports
            .FirstOrDefaultAsync(tr => tr.VisitId == visitId, ct);

        var visit = await dbContext.CtmsMonitoringVisits
            .FirstOrDefaultAsync(v => v.Id == visitId, ct);

        string visitType = visit?.VisitType ?? "IMV";
        string studyId = visit?.StudyId ?? "STUDY-001";
        string siteId = visit?.SiteId ?? "Site-01";
        string craId = visit?.CraId ?? user;
        string templateName = ResolveTemplateName(visitType);

        if (tripReport is null)
        {
            tripReport = new CtmsTripReport
            {
                VisitId = visitId,
                StudyId = studyId,
                SiteId = siteId,
                CraId = craId,
                VisitType = visitType,
                TemplateName = templateName,
                Status = "DRAFT",
                CreatedAt = now.UtcDateTime,
                CreatedBy = user,
                VersionIndex = 1,
                ReasonForChange = "Initial trip report questionnaire generation."
            };
            dbContext.CtmsTripReports.Add(tripReport);

            // Populate default sections and questions
            var defaultSections = GetDefaultSectionsForVisitType(visitType);
            for (int i = 0; i < defaultSections.Count; i++)
            {
                var secDef = defaultSections[i];
                var secEntity = new CtmsTripReportSection
                {
                    TripReportId = tripReport.Id,
                    SectionKey = secDef.Key,
                    Title = secDef.Title,
                    IsVerified = false,
                    OrderIndex = i,
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = user,
                    VersionIndex = 1,
                    ReasonForChange = "Default section creation."
                };
                dbContext.CtmsTripReportSections.Add(secEntity);

                foreach (var q in secDef.Questions)
                {
                    var respEntity = new CtmsTripReportResponse
                    {
                        TripReportId = tripReport.Id,
                        SectionKey = secDef.Key,
                        QuestionKey = q.Key,
                        QuestionText = q.Text,
                        ResponseType = q.Type,
                        Value = q.DefaultValue,
                        Commentary = q.DefaultCommentary,
                        CreatedAt = now.UtcDateTime,
                        CreatedBy = user,
                        VersionIndex = 1,
                        ReasonForChange = "Default question response creation."
                    };
                    dbContext.CtmsTripReportResponses.Add(respEntity);
                }
            }

            await dbContext.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(await BuildFormDtoAsync(tripReport, dbContext, ct));
    }

    /// <summary>
    /// Saves progressive draft edits for questionnaire sections and responses.
    /// </summary>
    public static async Task<IResult> SaveDraftAsync(
        [FromBody] SaveTripReportRequest request,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = httpContext.User?.Identity?.Name ?? "cra_fderuiter";
        var now = clock.UtcNow;

        var tripReport = await dbContext.CtmsTripReports
            .FirstOrDefaultAsync(tr => tr.Id == request.TripReportId || tr.VisitId == request.TripReportId, ct);

        if (tripReport is null)
        {
            return TypedResults.NotFound(new { error = $"Trip report '{request.TripReportId}' not found." });
        }

        tripReport.Status = "DRAFT";
        tripReport.VersionIndex += 1;
        tripReport.ReasonForChange = request.ReasonForChange ?? "Progressive save of section responses.";

        var existingSections = await dbContext.CtmsTripReportSections
            .Where(s => s.TripReportId == tripReport.Id)
            .ToListAsync(ct);

        var existingResponses = await dbContext.CtmsTripReportResponses
            .Where(r => r.TripReportId == tripReport.Id)
            .ToListAsync(ct);

        foreach (var secReq in request.Sections)
        {
            var sec = existingSections.FirstOrDefault(s => s.SectionKey == secReq.SectionKey);
            if (sec is null)
            {
                sec = new CtmsTripReportSection
                {
                    TripReportId = tripReport.Id,
                    SectionKey = secReq.SectionKey,
                    Title = FormatSectionTitle(secReq.SectionKey),
                    IsVerified = secReq.IsVerified,
                    OrderIndex = existingSections.Count,
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = user,
                    VersionIndex = 1,
                    ReasonForChange = "Added section on save."
                };
                dbContext.CtmsTripReportSections.Add(sec);
            }
            else
            {
                sec.IsVerified = secReq.IsVerified;
                sec.VersionIndex += 1;
            }

            foreach (var respReq in secReq.Responses)
            {
                var resp = existingResponses.FirstOrDefault(r => r.SectionKey == secReq.SectionKey && r.QuestionKey == respReq.QuestionKey);
                if (resp is null)
                {
                    resp = new CtmsTripReportResponse
                    {
                        TripReportId = tripReport.Id,
                        SectionKey = secReq.SectionKey,
                        QuestionKey = respReq.QuestionKey,
                        QuestionText = respReq.QuestionText,
                        ResponseType = respReq.ResponseType,
                        Value = respReq.Value,
                        Commentary = respReq.Commentary,
                        FindingId = respReq.FindingId,
                        CreatedAt = now.UtcDateTime,
                        CreatedBy = user,
                        VersionIndex = 1,
                        ReasonForChange = "Added question response on save."
                    };
                    dbContext.CtmsTripReportResponses.Add(resp);
                }
                else
                {
                    resp.Value = respReq.Value;
                    resp.Commentary = respReq.Commentary;
                    resp.FindingId = respReq.FindingId ?? resp.FindingId;
                    resp.VersionIndex += 1;
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return TypedResults.Ok(await BuildFormDtoAsync(tripReport, dbContext, ct));
    }

    /// <summary>
    /// Renders Liquid HTML output for a trip report.
    /// </summary>
    public static async Task<IResult> RenderTripReportAsync(
        [FromQuery] string templateName,
        [FromBody] TripReportFormDto request,
        [FromServices] ITemplateRenderer renderer,
        [FromServices] IClock clock)
    {
        if (!Catalog.Contains(templateName))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown template",
                detail: $"templateName must be one of: {string.Join(", ", Catalog.Keys.Order(StringComparer.Ordinal))}.");
        }

        var modelDict = BuildTripReportModel(request, isSigned: false, signerName: null, signingTimestamp: null, signingPurpose: null, integrityHash: null, clock: clock);
        string rendered = await renderer.RenderAsync(Catalog, templateName, modelDict);
        return TypedResults.Content(rendered, "text/plain");
    }

    /// <summary>
    /// Submits and signs trip report, compiling GxP PDF and filing to eTMF.
    /// </summary>
    public static async Task<IResult> SubmitAndSignAsync(
        [FromBody] SubmitTripReportRequest request,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] ITemplateRenderer renderer,
        [FromServices] IClock clock,
        [FromServices] IServiceProvider serviceProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = httpContext.User?.Identity?.Name ?? "cra_fderuiter";
        var now = clock.UtcNow;

        var tripReport = await dbContext.CtmsTripReports
            .FirstOrDefaultAsync(tr => tr.Id == request.TripReportId || tr.VisitId == request.TripReportId, ct);

        if (tripReport is null)
        {
            return TypedResults.NotFound(new { error = $"Trip report '{request.TripReportId}' not found." });
        }

        var formDto = await BuildFormDtoAsync(tripReport, dbContext, ct);
        var pdfResult = await CompileTripReportPdfInternalAsync(
            tripReport.TemplateName,
            formDto,
            request.Finalize,
            request.SignerName ?? user,
            request.SigningPurpose ?? "21 CFR Part 11 Electronic Signature / CRA Monitoring Visit Report Approval",
            renderer,
            clock);

        tripReport.Status = request.Finalize ? "FINALIZED" : "SUBMITTED";
        tripReport.IsSigned = request.Finalize;
        tripReport.Signer = request.Finalize ? (request.SignerName ?? user) : null;
        tripReport.SigningTimestamp = request.Finalize ? now : null;
        tripReport.SignatureManifestation = pdfResult.SignatureManifestationJson;
        tripReport.SubmittedAt = now;
        tripReport.SubmittedBy = user;
        tripReport.PdfSha256Hash = pdfResult.Sha256Hash;
        tripReport.PdfObjectKey = $"trip_reports/{tripReport.StudyId}/{tripReport.SiteId}/{tripReport.Id}.pdf";
        tripReport.ReasonForChange = request.ReasonForChange ?? "Submitted and signed trip report.";

        // File to eTMF if finalized
        string? etmfDocId = null;
        if (request.Finalize)
        {
            var letterModel = new SiteVisitLetterModel(
                CraId: tripReport.CraId,
                CreatedAt: now.ToString("yyyy-MM-DD"),
                StudyId: tripReport.StudyId,
                SiteId: tripReport.SiteId,
                VisitId: tripReport.VisitId,
                ReasonForChange: "Automated eTMF filing for finalized CRA trip report."
            );

            var etmfDbType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext");
            var etmfDb = etmfDbType != null ? serviceProvider.GetService(etmfDbType) : null;
            etmfDocId = await EtmfFilingHelper.FileToEtmfAsync(letterModel, pdfResult, tripReport.TemplateName, tripReport.Id, etmfDb, clock, user, ct);
            tripReport.EtmfDocumentId = etmfDocId;
        }

        // Log CTMS Audit Log
        var audit = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = "CRA",
            Action = request.Finalize ? "FINALIZE_TRIP_REPORT" : "SUBMIT_TRIP_REPORT",
            Details = $"Trip report {tripReport.Id} for visit {tripReport.VisitId} submitted/finalized. SHA256: {pdfResult.Sha256Hash}. eTMF Doc: {etmfDocId ?? "N/A"}.",
            ReasonForChange = request.ReasonForChange ?? "CRA trip report submission audit entry."
        };
        dbContext.CtmsAuditLogs.Add(audit);

        await dbContext.SaveChangesAsync(ct);

        var resultDto = new TripReportCompilationResultDto(
            TripReportId: tripReport.Id,
            VisitId: tripReport.VisitId,
            TemplateName: tripReport.TemplateName,
            PdfSha256Hash: pdfResult.Sha256Hash,
            PdfObjectKey: tripReport.PdfObjectKey ?? "",
            IsSigned: tripReport.IsSigned,
            Status: tripReport.Status,
            SignatureManifestation: pdfResult.SignatureManifestationJson,
            EtmfDocumentId: etmfDocId,
            PdfAccessUrl: $"/api/v1/ctms/trip-reports/{tripReport.Id}/pdf",
            PdfBase64: Convert.ToBase64String(pdfResult.PdfBytes)
        );

        return TypedResults.Ok(resultDto);
    }

    /// <summary>
    /// Compiles GxP PDF trip report directly.
    /// </summary>
    public static async Task<IResult> CompilePdfAsync(
        [FromQuery] string templateName,
        [FromBody] TripReportFormDto request,
        [FromServices] ITemplateRenderer renderer,
        [FromServices] IClock clock,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] IServiceProvider serviceProvider,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = httpContext.User?.Identity?.Name ?? request.CraId;
        var pdfResult = await CompileTripReportPdfInternalAsync(
            templateName,
            request,
            isSigned: request.IsSigned,
            signerName: request.Signer ?? user,
            signingPurpose: "21 CFR Part 11 Electronic Signature / Trip Report Compilation",
            renderer,
            clock);

        return TypedResults.Ok(new TripReportCompilationResultDto(
            TripReportId: request.TripReportId,
            VisitId: request.VisitId,
            TemplateName: templateName,
            PdfSha256Hash: pdfResult.Sha256Hash,
            PdfObjectKey: $"trip_reports/{request.StudyId}/{request.SiteId}/{request.TripReportId}.pdf",
            IsSigned: pdfResult.IsSigned,
            Status: request.IsSigned ? "FINALIZED" : "DRAFT",
            SignatureManifestation: pdfResult.SignatureManifestationJson,
            EtmfDocumentId: request.EtmfDocumentId,
            PdfAccessUrl: $"/api/v1/ctms/trip-reports/{request.TripReportId}/pdf",
            PdfBase64: Convert.ToBase64String(pdfResult.PdfBytes)
        ));
    }

    /// <summary>
    /// Serves compiled GxP PDF binary stream for a trip report.
    /// </summary>
    public static async Task<IResult> GetTripReportPdfAsync(
        string tripReportId,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] ITemplateRenderer renderer,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        var tripReport = await dbContext.CtmsTripReports
            .FirstOrDefaultAsync(tr => tr.Id == tripReportId || tr.VisitId == tripReportId, ct);

        if (tripReport is null)
        {
            return TypedResults.NotFound(new { error = $"Trip report '{tripReportId}' not found." });
        }

        var formDto = await BuildFormDtoAsync(tripReport, dbContext, ct);
        var pdfResult = await CompileTripReportPdfInternalAsync(
            tripReport.TemplateName,
            formDto,
            isSigned: tripReport.IsSigned,
            signerName: tripReport.Signer ?? tripReport.CraId,
            signingPurpose: "21 CFR Part 11 Electronic Signature / CRA Monitoring Visit Report Approval",
            renderer,
            clock);

        return TypedResults.Bytes(pdfResult.PdfBytes, "application/pdf", $"{tripReport.TemplateName}_{tripReport.Id}.pdf");
    }

    /// <summary>
    /// Attaches a visit finding directly to a questionnaire section or question.
    /// </summary>
    public static async Task<IResult> AttachFindingAsync(
        [FromBody] AttachFindingRequest request,
        [FromServices] CtmsDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var user = httpContext.User?.Identity?.Name ?? "cra_fderuiter";
        var now = clock.UtcNow;

        var tripReport = await dbContext.CtmsTripReports
            .FirstOrDefaultAsync(tr => tr.VisitId == request.VisitId || tr.Id == request.VisitId, ct);

        var finding = new CtmsMonitoringVisitFinding
        {
            VisitId = request.VisitId,
            Text = request.Text,
            Severity = request.Severity,
            ResolutionStatus = request.ResolutionStatus,
            SectionKey = request.SectionKey,
            QuestionKey = request.QuestionKey,
            TripReportId = tripReport?.Id,
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
            ReasonForChange = "Attached finding to questionnaire section item."
        };

        dbContext.CtmsMonitoringVisitFindings.Add(finding);

        if (tripReport is not null && !string.IsNullOrWhiteSpace(request.QuestionKey))
        {
            var response = await dbContext.CtmsTripReportResponses
                .FirstOrDefaultAsync(r => r.TripReportId == tripReport.Id && r.SectionKey == request.SectionKey && r.QuestionKey == request.QuestionKey, ct);
            if (response is not null)
            {
                response.FindingId = finding.Id;
            }
        }

        await dbContext.SaveChangesAsync(ct);

        return TypedResults.Ok(new SiteVisitFindingModel(finding.Text, finding.Severity, finding.ResolutionStatus));
    }

    private static async Task<TripReportFormDto> BuildFormDtoAsync(CtmsTripReport tripReport, CtmsDbContext dbContext, CancellationToken ct)
    {
        var sections = await dbContext.CtmsTripReportSections
            .Where(s => s.TripReportId == tripReport.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync(ct);

        var responses = await dbContext.CtmsTripReportResponses
            .Where(r => r.TripReportId == tripReport.Id)
            .ToListAsync(ct);

        var findings = await dbContext.CtmsMonitoringVisitFindings
            .Where(f => f.VisitId == tripReport.VisitId || f.TripReportId == tripReport.Id)
            .ToListAsync(ct);

        var sectionDtos = sections.Select(sec =>
        {
            var secResponses = responses
                .Where(r => r.SectionKey == sec.SectionKey)
                .Select(r => new TripReportResponseDto(
                    ResponseId: r.Id,
                    SectionKey: r.SectionKey,
                    QuestionKey: r.QuestionKey,
                    QuestionText: r.QuestionText,
                    ResponseType: r.ResponseType,
                    Value: r.Value,
                    Commentary: r.Commentary,
                    FindingId: r.FindingId
                ))
                .ToList();

            var secFindings = findings
                .Where(f => string.Equals(f.SectionKey, sec.SectionKey, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(f.SectionKey))
                .Select(f => new SiteVisitFindingModel(f.Text, f.Severity, f.ResolutionStatus))
                .ToList();

            return new TripReportSectionDto(
                SectionKey: sec.SectionKey,
                Title: sec.Title,
                IsVerified: sec.IsVerified,
                OrderIndex: sec.OrderIndex,
                Responses: secResponses,
                Findings: secFindings
            );
        }).ToList();

        return new TripReportFormDto(
            TripReportId: tripReport.Id,
            VisitId: tripReport.VisitId,
            StudyId: tripReport.StudyId,
            SiteId: tripReport.SiteId,
            CraId: tripReport.CraId,
            VisitType: tripReport.VisitType,
            TemplateName: tripReport.TemplateName,
            Status: tripReport.Status,
            IsSigned: tripReport.IsSigned,
            Signer: tripReport.Signer,
            SigningTimestamp: tripReport.SigningTimestamp,
            SignatureManifestation: tripReport.SignatureManifestation,
            EtmfDocumentId: tripReport.EtmfDocumentId,
            PdfObjectKey: tripReport.PdfObjectKey,
            PdfSha256Hash: tripReport.PdfSha256Hash,
            Sections: sectionDtos
        );
    }

    private static async Task<GxpPdfResult> CompileTripReportPdfInternalAsync(
        string templateName,
        TripReportFormDto form,
        bool isSigned,
        string signerName,
        string signingPurpose,
        ITemplateRenderer renderer,
        IClock clock)
    {
        var now = clock.UtcNow;
        var signingTimestamp = now.ToString("o");

        var modelDict = BuildTripReportModel(
            form,
            isSigned: isSigned,
            signerName: isSigned ? signerName : null,
            signingTimestamp: isSigned ? signingTimestamp : null,
            signingPurpose: isSigned ? signingPurpose : null,
            integrityHash: null,
            clock: clock);

        string htmlContent = await renderer.RenderAsync(Catalog, templateName, modelDict);
        var bodyBytes = Encoding.UTF8.GetBytes(htmlContent);
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(bodyBytes));

        modelDict["integrity_hash"] = bodyHash;
        htmlContent = await renderer.RenderAsync(Catalog, templateName, modelDict);

        var pdfBuilder = new StringBuilder();
        pdfBuilder.AppendLine("%PDF-1.7");
        pdfBuilder.AppendLine($"% Cadence Clinical GxP Trip Report PDF - {templateName}");
        pdfBuilder.AppendLine($"% Study: {form.StudyId} | Site: {form.SiteId} | Visit: {form.VisitId}");
        if (!isSigned)
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

        if (isSigned)
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
            IsSigned: isSigned,
            SignatureManifestationJson: signatureManifestationJson,
            RenderedHtml: htmlContent
        );
    }

    private static Dictionary<string, object?> BuildTripReportModel(
        TripReportFormDto form,
        bool isSigned,
        string? signerName,
        string? signingTimestamp,
        string? signingPurpose,
        string? integrityHash,
        IClock clock)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["study_id"] = form.StudyId,
            ["site_id"] = form.SiteId,
            ["cra_id"] = form.CraId,
            ["visit_id"] = form.VisitId,
            ["visit_type"] = form.VisitType,
            ["status"] = form.Status,
            ["actual_date"] = clock.UtcNow.ToString("yyyy-MM-dd"),
            ["is_signed"] = isSigned,
            ["signer_name"] = signerName,
            ["signing_timestamp"] = signingTimestamp,
            ["signing_purpose"] = signingPurpose,
            ["integrity_hash"] = integrityHash,
            ["sections"] = form.Sections.Select(s => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = s.Title,
                ["section_key"] = s.SectionKey,
                ["is_verified"] = s.IsVerified,
                ["responses"] = s.Responses.Select(r => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["question_key"] = r.QuestionKey,
                    ["question_text"] = r.QuestionText,
                    ["value"] = r.Value,
                    ["response_type"] = r.ResponseType,
                    ["commentary"] = r.Commentary
                }).ToList(),
                ["findings"] = s.Findings.Select(f => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["text"] = f.Text,
                    ["severity"] = f.Severity,
                    ["resolution_status"] = f.ResolutionStatus
                }).ToList()
            }).ToList()
        };
    }

    private static string ResolveTemplateName(string visitType)
    {
        return visitType.ToUpperInvariant() switch
        {
            "PSV" or "PRE-STUDY" => "psv_trip_report",
            "SIV" or "INITIATION" => "siv_trip_report",
            "COV" or "CLOSE-OUT" => "cov_trip_report",
            _ => "imv_trip_report"
        };
    }

    private static string FormatSectionTitle(string sectionKey)
    {
        return sectionKey switch
        {
            "protocol_compliance" => "Protocol Compliance & IRB Approvals",
            "ip_accountability" => "Investigational Product (IP) Accountability",
            "regulatory_binder" => "Regulatory Binder & Essential Documents",
            "facilities" => "Facilities & Laboratory Equipment",
            _ => sectionKey.Replace('_', ' ').ToUpperInvariant()
        };
    }

    private static List<SectionDef> GetDefaultSectionsForVisitType(string visitType)
    {
        string upperType = visitType.ToUpperInvariant();
        string psvFocus = upperType.Contains("PSV") ? " (Pre-Study Qualification)" : "";
        string sivFocus = upperType.Contains("SIV") ? " (Initiation Readiness)" : "";
        string covFocus = upperType.Contains("COV") ? " (Final Close-Out)" : "";

        return new List<SectionDef>
        {
            new("protocol_compliance", "Protocol Compliance & IRB Approvals" + psvFocus + sivFocus + covFocus, new List<QuestionDef>
            {
                new("pc_irb_approval", "Is IRB/IEC approval documentation current and filed in eISF?", "boolean", "PASS", "IRB approval verified."),
                new("pc_protocol_dev", "Were any protocol deviations identified during this visit period?", "boolean", "NO", "No major protocol deviations."),
                new("pc_icf_version", "Are all active subjects consented under the approved ICF version?", "boolean", "PASS", "100% ICF version compliance verified.")
            }),
            new("ip_accountability", "Investigational Product (IP) Accountability", new List<QuestionDef>
            {
                new("ip_temp_logs", "Are IP storage temperature logs complete and within specified ranges?", "boolean", "PASS", "Temp logs continuous and compliant."),
                new("ip_reconciliation_count", "Number of IP units reconciled during visit counter", "numerical", "10", "10 kits reconciled."),
                new("ip_storage_sec", "Is IP stored securely with restricted authorization?", "boolean", "PASS", "Storage facility secure.")
            }),
            new("regulatory_binder", "Regulatory Binder & Essential Documents", new List<QuestionDef>
            {
                new("rb_doa_log", "Is Delegation of Authority (DOA) log signed and up to date?", "boolean", "PASS", "DOA log reviewed and complete."),
                new("rb_cv_gcp", "Are CVs and GCP training certificates filed for all active site staff?", "boolean", "PASS", "All credentials current."),
                new("rb_1572_fd", "Is FDA Form 1572 / Financial Disclosure current for all investigators?", "boolean", "PASS", "Form 1572 accurate.")
            }),
            new("facilities", "Facilities & Laboratory Equipment", new List<QuestionDef>
            {
                new("fac_lab_cert", "Are central/local lab accreditations and reference ranges current?", "boolean", "PASS", "Lab CAP/CLIA certs verified."),
                new("fac_equip_cal", "Is equipment calibration documentation verified?", "boolean", "PASS", "Centrifuge and freezer calibrated.")
            })
        };
    }

    private sealed record SectionDef(string Key, string Title, List<QuestionDef> Questions);
    private sealed record QuestionDef(string Key, string Text, string Type, string DefaultValue, string DefaultCommentary);
}

public record TripReportCompilationResultDto(
    string TripReportId,
    string VisitId,
    string TemplateName,
    string PdfSha256Hash,
    string PdfObjectKey,
    bool IsSigned,
    string Status,
    string SignatureManifestation,
    string? EtmfDocumentId,
    string PdfAccessUrl,
    string PdfBase64
);
