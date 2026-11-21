using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Etmf.Endpoints;

public static class EtmfIntelligenceEndpoints
{
    public static void MapEtmfEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/etmf");

        // Every route names its permission (ADR-2510, ADR-2516). A study id in a body or on a stored document is checked
        // against the caller's signed study assignments, and lists are narrowed to them (#5361).
        group.MapPost("/intelligence/classify", ClassifyDocument).RequirePermission("etmf_document:tag");
        group.MapPost("/intelligence/stage-qc", StageQcDocument).RequirePermission("etmf_document:create");
        group.MapGet("/intelligence/qc-queue", GetQcQueue).RequirePermission("etmf_document:read");
        group.MapPost("/intelligence/qc-review/{doc_id}", ReviewQcDocument).RequirePermission("etmf_document:transition_clinical_qc");

        group.MapGet("/documents", GetDocuments).RequirePermission("etmf_document:read");
        group.MapGet("/documents/{document_id}", GetDocumentById).RequirePermission("etmf_document:read");
        group.MapPost("/documents/export", ExportDocuments).RequirePermission("etmf_document:export");
        group.MapGet("/export-jobs/{job_id}", GetExportJob).RequirePermission("etmf_document:export");
        group.MapGet("/export-jobs/{job_id}/download", DownloadExportJob).RequirePermission("etmf_document:export");
        group.MapGet("/export-jobs", ListExportJobs).RequirePermission("etmf_document:export");

        group.MapPost("/ingest", IngestDocument).RequirePermission("etmf_document:create");
        group.MapGet("/taxonomy", GetTaxonomy).RequirePermission("etmf_taxonomy:read");
    }

    /// <summary>The signed caller's user id; the gateway scheme always sets one on an admitted request.</summary>
    private static string Caller(HttpContext httpContext) =>
        CallerScope.UserId(httpContext) ?? throw new InvalidOperationException("An admitted eTMF request has no signed caller.");

    /// <summary>Narrows documents to the studies the caller is assigned to.</summary>
    private static IQueryable<TmfDocument> InCallerStudies(IQueryable<TmfDocument> documents, HttpContext httpContext)
    {
        var studies = CallerScope.StudyFilter(httpContext);
        return studies == null ? documents : documents.Where(d => studies.Contains(d.StudyId));
    }

    private static IResult OutsideStudy() =>
        TypedResults.Problem(
            title: "Outside your studies",
            detail: "You are not assigned to this study.",
            statusCode: StatusCodes.Status403Forbidden);

    public static Task<IResult> ClassifyDocument(
        [FromBody] ClassifyRequest request,
        [FromServices] IClock clock)
    {
        var report = PerformClassification(
            docId: null,
            filename: request.Filename,
            mimeType: request.MimeType ?? "application/pdf",
            content: request.Content,
            hint: request.ContentHint,
            now: clock.UtcNow);

        return Task.FromResult<IResult>(TypedResults.Ok(report));
    }

    public static async Task<IResult> StageQcDocument(
        [FromBody] StageDocumentQCRequest request,
        [FromServices] EtmfDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct,
        [FromServices] IEtmfCompletenessWebhookPublisher? webhookPublisher = null)
    {
        if (string.IsNullOrWhiteSpace(request.ReasonForChange) || request.ReasonForChange.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        if (!CallerScope.CanAccessStudy(httpContext, request.StudyId))
        {
            return OutsideStudy();
        }

        var now = clock.UtcNow;
        var docId = "DOC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var report = PerformClassification(
            docId: docId,
            filename: request.Filename,
            mimeType: request.MimeType ?? "application/pdf",
            content: request.Content,
            hint: request.ArtifactHint,
            now: now);

        var user = Caller(httpContext);
        var roles = CallerScope.Roles(httpContext);

        var doc = TmfDocument.CreateForTechnicalQc(
            id: docId,
            studyId: request.StudyId,
            filename: request.Filename,
            createdBy: user,
            actorRole: roles,
            timestamp: now,
            reasonForChange: request.ReasonForChange,
            siteId: request.SiteId,
            zone: report.PrimaryClassification.Zone,
            section: report.PrimaryClassification.Section,
            artifactCode: report.PrimaryClassification.ArtifactCode,
            artifactType: report.PrimaryClassification.ArtifactType,
            content: request.Content,
            mimeType: request.MimeType ?? "application/pdf",
            taxonomyVersion: request.TaxonomyVersion ?? "v3.2.0-complete",
            metadataJson: JsonSerializer.Serialize(new
            {
                document_intelligence = report,
                assigned_cra = request.AssignedCra
            })
        );

        dbContext.TmfDocuments.Add(doc);
        foreach (var tr in doc.Transitions)
        {
            dbContext.TmfDocumentQcTransitions.Add(tr);
        }

        var audit = new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = "STAGE_QC",
            DocumentId = docId,
            Details = $"Staged document {request.Filename} for CRA QC in study {request.StudyId}.",
            ReasonForChange = request.ReasonForChange
        };
        dbContext.TmfAuditLogs.Add(audit);

        await dbContext.SaveChangesAsync(ct);

        if (webhookPublisher is not null && !string.IsNullOrWhiteSpace(request.SiteId))
        {
            string? tenantId = httpContext.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
            await webhookPublisher.PublishSiteActivationCompletenessAsync(
                dbContext,
                request.StudyId,
                request.SiteId,
                tenantId,
                request.ReasonForChange,
                ct);
        }

        var stagingItem = new CraqcStagingItem(
            DocumentId: docId,
            StudyId: request.StudyId,
            SiteId: request.SiteId,
            Filename: request.Filename,
            Status: "TECHNICAL_QC",
            IntelligenceReport: report,
            StagedAt: now,
            StagedBy: user,
            QcAssignedTo: request.AssignedCra,
            DiscrepancyNotes: report.ConfidenceTier == "LOW" ? ["Low classification confidence score"] : []
        );

        return TypedResults.Created($"/api/v1/etmf/documents/{docId}", stagingItem);
    }

    public static async Task<IResult> GetQcQueue(
        [FromQuery] string? study_id,
        [FromServices] EtmfDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var query = InCallerStudies(dbContext.TmfDocuments.AsNoTracking(), httpContext).Where(d => d.Status == "TECHNICAL_QC");
        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(d => d.StudyId == study_id);
        }

        var docs = await query.ToListAsync(ct);
        var stagingItems = docs.Select(doc =>
        {
            DocumentIntelligenceReport report;
            try
            {
                if (!string.IsNullOrEmpty(doc.MetadataJson))
                {
                    using var jsonDoc = JsonDocument.Parse(doc.MetadataJson);
                    if (jsonDoc.RootElement.TryGetProperty("document_intelligence", out var intelElem))
                    {
                        report = JsonSerializer.Deserialize<DocumentIntelligenceReport>(intelElem.GetRawText())!;
                    }
                    else
                    {
                        report = PerformClassification(doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow);
                    }
                }
                else
                {
                    report = PerformClassification(doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow);
                }
            }
            catch
            {
                report = PerformClassification(doc.Id, doc.Filename, doc.MimeType, doc.Content, doc.ArtifactType, clock.UtcNow);
            }

            return new CraqcStagingItem(
                DocumentId: doc.Id,
                StudyId: doc.StudyId,
                SiteId: doc.SiteId,
                Filename: doc.Filename,
                Status: doc.Status,
                IntelligenceReport: report,
                StagedAt: new DateTimeOffset(doc.CreatedAt, TimeSpan.Zero),
                StagedBy: doc.CreatedBy,
                QcAssignedTo: null,
                DiscrepancyNotes: report.ConfidenceTier == "LOW" ? ["Low classification confidence score"] : []
            );
        }).ToList();

        return TypedResults.Ok(stagingItems);
    }

    public static async Task<IResult> ReviewQcDocument(
        [FromRoute] string doc_id,
        [FromBody] CraqcReviewRequest request,
        [FromServices] EtmfDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct,
        [FromServices] IEtmfCompletenessWebhookPublisher? webhookPublisher = null)
    {
        if (string.IsNullOrWhiteSpace(request.ReasonForChange) || request.ReasonForChange.Length < 10)
        {
            return TypedResults.BadRequest(new { error = "21 CFR Part 11 requires a reason_for_change of at least 10 characters." });
        }

        var doc = await dbContext.TmfDocuments.FirstOrDefaultAsync(d => d.Id == doc_id, ct);
        if (doc == null)
        {
            return TypedResults.NotFound(new { error = $"Document with ID '{doc_id}' not found." });
        }

        if (!CallerScope.CanAccessStudy(httpContext, doc.StudyId))
        {
            return OutsideStudy();
        }

        var decision = request.Decision.ToUpperInvariant();
        if (decision != "ACCEPT" && decision != "OVERRIDE" && decision != "REJECT")
        {
            return TypedResults.BadRequest(new { error = $"Invalid decision '{request.Decision}'. Allowed values: ACCEPT, OVERRIDE, REJECT." });
        }

        if (!string.Equals(doc.Status, TmfDocumentLifecycleState.TechnicalQc, StringComparison.OrdinalIgnoreCase))
        {
            var allowed = TmfDocument.GetAllowedTransitions(doc.Status);
            var allowedStr = allowed.Count > 0 ? string.Join(", ", allowed) : "None";
            return TypedResults.BadRequest(new
            {
                error = $"Cannot review document with status '{doc.Status}'. Only documents in 'TECHNICAL_QC' status can be reviewed. Allowed transitions from '{doc.Status}': {allowedStr}."
            });
        }

        var now = clock.UtcNow;
        var user = Caller(httpContext);
        var roles = CallerScope.Roles(httpContext);

        if (decision == "OVERRIDE" && !string.IsNullOrWhiteSpace(request.OverrideArtifactCode))
        {
            var tax = ResolveTaxonomyByCode(request.OverrideArtifactCode);
            doc.OverrideTaxonomy(tax.Zone, tax.Section, tax.ArtifactType, request.OverrideArtifactCode);
        }

        var currentSeq = await dbContext.TmfDocumentQcTransitions
            .Where(t => t.DocumentId == doc_id)
            .MaxAsync(t => (int?)t.TransitionSequence, ct) ?? 0;
        var nextSeq = currentSeq + 1;

        TmfDocumentQcTransition transition;
        try
        {
            transition = decision == "REJECT"
                ? doc.Reject(user, roles, request.ReasonForChange, now, nextSeq)
                : doc.Approve(user, roles, request.ReasonForChange, now, nextSeq);
        }
        catch (InvalidStateTransitionException ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(new { error = ex.Message });
        }

        dbContext.TmfDocumentQcTransitions.Add(transition);

        var audit = new TmfAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = roles,
            Action = $"QC_REVIEW_{decision}",
            DocumentId = doc_id,
            Details = $"CRA QC Review decision: {decision}. Reason: {request.ReasonForChange}",
            ReasonForChange = request.ReasonForChange
        };
        dbContext.TmfAuditLogs.Add(audit);

        await dbContext.SaveChangesAsync(ct);

        if (webhookPublisher is not null)
        {
            string? tenantId = httpContext.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
            await webhookPublisher.PublishSiteActivationCompletenessAsync(
                dbContext,
                doc.StudyId,
                doc.SiteId,
                tenantId,
                request.ReasonForChange,
                ct);
        }

        return TypedResults.Ok(doc);
    }

    public static async Task<IResult> GetDocuments(
        [FromQuery] string? study_id,
        [FromQuery] int? zone,
        [FromServices] EtmfDbContext dbContext,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var query = InCallerStudies(dbContext.TmfDocuments.AsNoTracking(), httpContext);
        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(d => d.StudyId == study_id);
        }
        if (zone.HasValue)
        {
            query = query.Where(d => d.Zone == zone.Value);
        }

        var docs = await query.ToListAsync(ct);
        return TypedResults.Ok(docs);
    }

    public static async Task<IResult> GetDocumentById(
        [FromRoute] string document_id,
        [FromServices] EtmfDbContext dbContext,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var doc = await dbContext.TmfDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == document_id, ct);
        // A document in a study the caller is not assigned to is answered as missing, so its id reveals nothing.
        if (doc == null || !CallerScope.CanAccessStudy(httpContext, doc.StudyId))
        {
            return TypedResults.NotFound(new { error = $"Document '{document_id}' not found." });
        }
        return TypedResults.Ok(doc);
    }

    public static async Task<IResult> IngestDocument(
        [FromBody] IngestDocumentRequest request,
        [FromServices] EtmfDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return TypedResults.BadRequest(new { error = "study_id is required: every document is filed in a study." });
        }

        if (!CallerScope.CanAccessStudy(httpContext, request.StudyId))
        {
            return OutsideStudy();
        }

        var now = clock.UtcNow;
        var user = Caller(httpContext);
        var docId = "DOC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var doc = TmfDocument.CreateDraft(
            id: docId,
            studyId: request.StudyId,
            filename: request.Filename,
            createdBy: user,
            createdAt: now.UtcDateTime,
            reasonForChange: request.ReasonForChange ?? "Initial filing",
            siteId: request.SiteId,
            zone: request.Zone > 0 ? request.Zone : 1,
            section: request.Section ?? "01.01",
            artifactCode: request.ArtifactCode ?? "01.01.01",
            artifactType: request.ArtifactType ?? "Clinical Trial Protocol",
            content: request.Content ?? string.Empty,
            mimeType: request.MimeType ?? "application/pdf",
            taxonomyVersion: request.TaxonomyVersion ?? "v3.2.0-complete"
        );

        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/etmf/documents/{docId}", doc);
    }

    public static Task<IResult> GetTaxonomy([FromQuery] string? version)
    {
        var taxonomy = GetStandardTaxonomy();
        return Task.FromResult<IResult>(TypedResults.Ok(taxonomy));
    }

    private static DocumentIntelligenceReport PerformClassification(
        string? docId,
        string filename,
        string mimeType,
        string? content,
        string? hint,
        DateTimeOffset now)
    {
        var textToScan = $"{filename} {hint} {content}".ToLowerInvariant();
        var primary = PredictTaxonomy(textToScan);

        var sha256 = ComputeSha256(content ?? filename);

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

    private static TaxonomyMatchCandidate PredictTaxonomy(string text)
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

    private static TaxonomyMatchCandidate ResolveTaxonomyByCode(string artifactCode)
    {
        return artifactCode switch
        {
            "01.01.01" => new TaxonomyMatchCandidate(1, "01.01", "01.01.01", "Clinical Trial Protocol", 1.0),
            "01.01.02" => new TaxonomyMatchCandidate(1, "01.01", "01.01.02", "Clinical Trial Protocol Amendment", 1.0),
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

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
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

    public static async Task<IResult> ExportDocuments(
        [FromBody] ExportDocumentsRequest request,
        [FromServices] EtmfDbContext dbContext,
        [FromServices] EtmfExportQueue exportQueue,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // An export is one study's binder, and the caller must be assigned to that study.
        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return TypedResults.BadRequest(new { error = "study_id is required: an export is one study's binder." });
        }

        if (!CallerScope.CanAccessStudy(httpContext, request.StudyId))
        {
            return OutsideStudy();
        }

        var query = dbContext.TmfDocuments.AsNoTracking().Where(d => d.StudyId == request.StudyId);

        if (request.DocumentIds != null && request.DocumentIds.Count > 0)
        {
            query = query.Where(d => request.DocumentIds.Contains(d.Id));
        }
        else if (!string.IsNullOrWhiteSpace(request.ArtifactCode))
        {
            query = query.Where(d => d.ArtifactCode == request.ArtifactCode);
        }
        else if (!string.IsNullOrWhiteSpace(request.Section))
        {
            query = query.Where(d => d.Section == request.Section);
        }
        else if (request.Zone.HasValue)
        {
            query = query.Where(d => d.Zone == request.Zone.Value);
        }

        var matchingDocs = await query
            .OrderBy(d => d.Zone)
            .ThenBy(d => d.Section)
            .ThenBy(d => d.Filename)
            .ToListAsync(ct);

        int count = matchingDocs.Count;
        var studyId = request.StudyId;

        // Requirement 2:
        // If count <= 50 and IsFullBinder == false -> stream ZIP synchronously (200 OK)
        // If count > 50 or IsFullBinder == true -> enqueue background job and return 202 Accepted
        if (count <= 50 && !request.IsFullBinder)
        {
            var exportId = "EXP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var now = clock.UtcNow;
            var zipBytes = EtmfExportPackager.CreateZipArchive(
                exportId,
                studyId,
                matchingDocs,
                now);

            var filename = $"etmf_export_{studyId}_{now:yyyyMMdd_HHmmss}.zip";
            return TypedResults.File(zipBytes, "application/zip", filename);
        }
        else
        {
            var jobId = "JOB-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var user = Caller(httpContext);
            var now = clock.UtcNow;

            var job = new TmfExportJob(jobId)
            {
                StudyId = studyId,
                RequestedBy = user,
                Status = "QUEUED",
                ProgressPercentage = 0.0,
                TotalDocuments = count,
                ProcessedDocuments = 0,
                CreatedAt = now.UtcDateTime,
                ReasonForChange = "Study binder bulk export request"
            };

            dbContext.TmfExportJobs.Add(job);
            await dbContext.SaveChangesAsync(ct);

            await exportQueue.EnqueueAsync(jobId, ct);

            return TypedResults.Accepted(
                uri: $"/api/v1/etmf/export-jobs/{jobId}",
                value: new
                {
                    job_id = jobId,
                    study_id = job.StudyId,
                    requested_by = user,
                    status = "QUEUED",
                    progress_percentage = 0.0,
                    total_documents = count,
                    processed_documents = 0,
                    download_url = (string?)null,
                    expires_at = (DateTimeOffset?)null,
                    created_at = job.CreatedAt,
                    status_url = $"/api/v1/etmf/export-jobs/{jobId}",
                    message = "Export job enqueued successfully for background processing."
                }
            );
        }
    }

    public static async Task<IResult> GetExportJob(
        [FromRoute] string job_id,
        [FromServices] EtmfDbContext dbContext,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var job = await dbContext.TmfExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == job_id, ct);
        if (job == null || !CallerScope.CanAccessStudy(httpContext, job.StudyId))
        {
            return TypedResults.NotFound(new { error = $"Export job '{job_id}' not found." });
        }

        return TypedResults.Ok(new
        {
            job_id = job.Id,
            study_id = job.StudyId,
            requested_by = job.RequestedBy,
            status = job.Status,
            progress_percentage = job.ProgressPercentage,
            total_documents = job.TotalDocuments,
            processed_documents = job.ProcessedDocuments,
            download_url = job.DownloadUrl,
            expires_at = job.ExpiresAt,
            created_at = job.CreatedAt
        });
    }

    public static async Task<IResult> DownloadExportJob(
        [FromRoute] string job_id,
        [FromServices] EtmfDbContext dbContext,
        [FromServices] IClock clock,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var job = await dbContext.TmfExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == job_id, ct);
        if (job == null || !CallerScope.CanAccessStudy(httpContext, job.StudyId))
        {
            return TypedResults.NotFound(new { error = $"Export job '{job_id}' not found." });
        }

        if (job.Status != "COMPLETED")
        {
            return TypedResults.BadRequest(new { error = $"Export job '{job_id}' is not in COMPLETED status (Current status: {job.Status})." });
        }

        if (job.ExpiresAt.HasValue && clock.UtcNow > job.ExpiresAt.Value)
        {
            return TypedResults.StatusCode(StatusCodes.Status410Gone);
        }

        var filePath = Path.Combine(Path.GetTempPath(), "etmf_exports", $"{job_id}.zip");
        if (!File.Exists(filePath))
        {
            var docs = await dbContext.TmfDocuments
                .AsNoTracking()
                .Where(d => d.StudyId == job.StudyId)
                .OrderBy(d => d.Zone)
                .ThenBy(d => d.Section)
                .ThenBy(d => d.Filename)
                .ToListAsync(ct);

            var zipBytes = EtmfExportPackager.CreateZipArchive(job.Id, job.StudyId, docs, clock.UtcNow);
            return TypedResults.File(zipBytes, "application/zip", $"etmf_export_{job.StudyId}_{job.Id}.zip");
        }

        var fileStream = File.OpenRead(filePath);
        return TypedResults.File(fileStream, "application/zip", $"etmf_export_{job.StudyId}_{job.Id}.zip");
    }

    public static async Task<IResult> ListExportJobs(
        [FromQuery] string? study_id,
        [FromServices] EtmfDbContext dbContext,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var studies = CallerScope.StudyFilter(httpContext);
        var query = dbContext.TmfExportJobs.AsNoTracking().Where(j => studies == null || studies.Contains(j.StudyId));
        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(j => j.StudyId == study_id);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Select(job => new
            {
                job_id = job.Id,
                study_id = job.StudyId,
                requested_by = job.RequestedBy,
                status = job.Status,
                progress_percentage = job.ProgressPercentage,
                total_documents = job.TotalDocuments,
                processed_documents = job.ProcessedDocuments,
                download_url = job.DownloadUrl,
                expires_at = job.ExpiresAt,
                created_at = job.CreatedAt
            })
            .ToListAsync(ct);

        return TypedResults.Ok(jobs);
    }
}


public record ClassifyRequest(
    string Filename,
    string? Content = null,
    string? MimeType = "application/pdf",
    string? ContentHint = null,
    string? TaxonomyVersion = "v3.2.0-complete"
);

public record StageDocumentQCRequest(
    string Content,
    string Filename,
    string StudyId,
    string? SiteId = null,
    string? MimeType = "application/pdf",
    string? ArtifactHint = null,
    string? TaxonomyVersion = "v3.2.0-complete",
    string ReasonForChange = "Batch upload AI auto-classification staging for CRA QC",
    string? AssignedCra = null
);

public record CraqcReviewRequest(
    string Decision,
    string ReasonForChange,
    string? OverrideArtifactCode = null,
    string? DiscrepancyComment = null
);

public record IngestDocumentRequest(
    string Filename,
    string? Content,
    string? StudyId,
    string? SiteId,
    string? MimeType,
    int Zone,
    string? Section,
    string? ArtifactCode,
    string? ArtifactType,
    string? TaxonomyVersion,
    string? ReasonForChange
);

public record DocumentIntelligenceReport(
    string? DocumentId,
    string Filename,
    string MimeType,
    string Sha256Hash,
    string Modality,
    TaxonomyMatchCandidate PrimaryClassification,
    List<TaxonomyMatchCandidate> AlternativeCandidates,
    string ConfidenceTier,
    string QcRecommendation,
    Dictionary<string, object> AiGenerationManifest
);

public record TaxonomyMatchCandidate(
    int Zone,
    string Section,
    string ArtifactCode,
    string ArtifactType,
    double Confidence
);

public record CraqcStagingItem(
    string DocumentId,
    string StudyId,
    string? SiteId,
    string Filename,
    string Status,
    DocumentIntelligenceReport IntelligenceReport,
    DateTimeOffset StagedAt,
    string StagedBy,
    string? QcAssignedTo,
    List<string> DiscrepancyNotes
);

public record ExportDocumentsRequest(
    [property: JsonPropertyName("study_id")] string? StudyId = null,
    [property: JsonPropertyName("document_ids")] List<string>? DocumentIds = null,
    [property: JsonPropertyName("zone")] int? Zone = null,
    [property: JsonPropertyName("section")] string? Section = null,
    [property: JsonPropertyName("artifact_code")] string? ArtifactCode = null,
    [property: JsonPropertyName("is_full_binder")] bool IsFullBinder = false
);

