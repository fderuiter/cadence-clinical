using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API endpoints for laboratory reference range management, UCUM SI unit conversion, LOINC catalog lookup,
/// and native asynchronous bulk range file import (dry-run preflight preview &amp; GxP committed import).
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public static class LabRangeEndpoints
{
    // Route permissions (ADR-2516). Reference ranges are maintained under lab_range; a bulk import replaces a study's
    // reference data, so previewing and committing one is a sponsor data-management operation (lab_range_import).
    private const string ReadRanges = "lab_range:read";
    private const string ImportPreview = "lab_range_import:preview";
    private const string ImportCommit = "lab_range_import:commit";
    private const string ImportHistory = "lab_range_import:read";

    public static RouteGroupBuilder MapLabRangeEndpoints(this IEndpointRouteBuilder routes)
    {
        var rangesGroup = routes.MapGroup("/api/v1/execution/lab-ranges")
            .WithTags("Laboratory Reference Ranges");

        rangesGroup.MapGet("/", GetReferenceRangesAsync)
            .WithName("GetReferenceRanges")
            .WithSummary("Get reference ranges for study/site")
            .Produces<IReadOnlyList<LabReferenceRangeResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadRanges);

        rangesGroup.MapGet("/{id:guid}", GetReferenceRangeByIdAsync)
            .WithName("GetReferenceRangeById")
            .WithSummary("Get reference range by ID")
            .Produces<LabReferenceRangeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadRanges);

        rangesGroup.MapPost("/", CreateReferenceRangeAsync)
            .WithName("CreateReferenceRange")
            .WithSummary("Create laboratory reference range")
            .AddEndpointFilter<ChangeReasonHeaderFilter>()
            .Produces<LabReferenceRangeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("lab_range:create");

        rangesGroup.MapPut("/{id:guid}", UpdateReferenceRangeAsync)
            .WithName("UpdateReferenceRange")
            .WithSummary("Update laboratory reference range")
            .AddEndpointFilter<ChangeReasonHeaderFilter>()
            .Produces<LabReferenceRangeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("lab_range:update");

        rangesGroup.MapDelete("/{id:guid}", DeleteReferenceRangeAsync)
            .WithName("DeleteReferenceRange")
            .WithSummary("Deactivate laboratory reference range")
            .AddEndpointFilter<ChangeReasonHeaderFilter>()
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission("lab_range:delete");

        var labsGroup = routes.MapGroup("/api/v1/execution/labs")
            .WithTags("Laboratory Import & Catalog");

        labsGroup.MapPost("/import/dry-run", DryRunImportAsync)
            .WithName("DryRunLabRangeImport")
            .WithSummary("Pre-flight validation preview for bulk lab range CSV/XLSX file")
            .DisableAntiforgery()
            .Produces<BulkLabRangeDryRunResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ImportPreview);

        labsGroup.MapPost("/import/commit", CommitImportAsync)
            .WithName("CommitLabRangeImport")
            .WithSummary("Commit validated bulk lab ranges into persistent store with Part 11 audit logging")
            .DisableAntiforgery()
            .AddEndpointFilter<ChangeReasonHeaderFilter>()
            .Produces<BulkLabRangeImportJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ImportCommit);

        labsGroup.MapGet("/batch-status", GetBatchStatusAsync)
            .WithName("GetLabBatchStatus")
            .WithSummary("Retrieve batch status history for bulk lab range imports")
            .Produces<IReadOnlyList<BulkLabRangeImportJobResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ImportHistory);

        labsGroup.MapGet("/batch-status/{id:guid}", GetBatchStatusByIdAsync)
            .WithName("GetLabBatchStatusById")
            .WithSummary("Get lab batch import job status by ID")
            .Produces<BulkLabRangeImportJobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ImportHistory);

        labsGroup.MapPost("/convert-unit", ConvertUnit)
            .WithName("ConvertLabUnit")
            .WithSummary("Convert clinical laboratory unit via UCUM SI formulas")
            .Produces<UnitConversionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadRanges);

        labsGroup.MapGet("/loinc-catalog", GetLoincCatalog)
            .WithName("GetLoincCatalog")
            .WithSummary("Retrieve standardized LOINC catalog mappings")
            .Produces<LoincCatalogResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadRanges);

        return rangesGroup;
    }

    private static async Task<IResult> GetReferenceRangesAsync(
        string? study_id,
        string? studyId,
        string? site_id,
        string? siteId,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;
        string? effectiveSiteId = !string.IsNullOrWhiteSpace(site_id) ? site_id : siteId;

        var result = await labRangeImportService.GetReferenceRangesAsync(effectiveStudyId ?? "", effectiveSiteId, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetReferenceRangeByIdAsync(
        Guid id,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        var result = await labRangeImportService.GetReferenceRangeByIdAsync(id, ct);
        if (result is null)
        {
            return TypedResults.NotFound(new { code = "RANGE_NOT_FOUND", detail = $"Lab reference range with ID '{id}' was not found." });
        }
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> CreateReferenceRangeAsync(
        LabReferenceRangeCreateRequest request,
        HttpContext httpContext,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        string createdBy = httpContext.User.ToAuthenticatedCaller()?.UserId ?? "DATA_MANAGER";
        string? changeReason = ChangeReasonHeader.Read(httpContext.Request) ?? request.ReasonForChange;

        var updatedRequest = request with { ReasonForChange = changeReason };
        var result = await labRangeImportService.CreateReferenceRangeAsync(updatedRequest, createdBy, ct);
        return TypedResults.Created($"/api/v1/execution/lab-ranges/{result.Id}", result);
    }

    private static async Task<IResult> UpdateReferenceRangeAsync(
        Guid id,
        LabReferenceRangeUpdateRequest request,
        HttpContext httpContext,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        string updatedBy = httpContext.User.ToAuthenticatedCaller()?.UserId ?? "DATA_MANAGER";
        string? changeReason = ChangeReasonHeader.Read(httpContext.Request) ?? request.ReasonForChange;

        var updatedRequest = request with { ReasonForChange = changeReason };
        var result = await labRangeImportService.UpdateReferenceRangeAsync(id, updatedRequest, updatedBy, ct);
        if (result is null)
        {
            return TypedResults.NotFound(new { code = "RANGE_NOT_FOUND", detail = $"Lab reference range with ID '{id}' was not found." });
        }
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> DeleteReferenceRangeAsync(
        Guid id,
        HttpContext httpContext,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        string updatedBy = httpContext.User.ToAuthenticatedCaller()?.UserId ?? "DATA_MANAGER";
        string changeReason = ChangeReasonHeader.Read(httpContext.Request) ?? "Deactivating lab reference range.";

        bool deleted = await labRangeImportService.DeleteReferenceRangeAsync(id, updatedBy, changeReason, ct);
        if (!deleted)
        {
            return TypedResults.NotFound(new { code = "RANGE_NOT_FOUND", detail = $"Lab reference range with ID '{id}' was not found." });
        }
        return TypedResults.Ok(new { message = "Lab reference range deactivated successfully.", id });
    }

    private static async Task<IResult> DryRunImportAsync(
        HttpContext httpContext,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        BulkLabRangeDryRunRequest request;

        if (httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            string studyId = form["study_id"].FirstOrDefault() ?? form["studyId"].FirstOrDefault() ?? "STUDY-101";
            string? siteId = form["site_id"].FirstOrDefault() ?? form["siteId"].FirstOrDefault();
            string labSource = form["lab_source"].FirstOrDefault() ?? form["labSource"].FirstOrDefault() ?? "CENTRAL";
            string rawContent = form["raw_file_content"].FirstOrDefault() ?? form["raw_content"].FirstOrDefault() ?? string.Empty;

            string base64Content = string.Empty;
            string fileName = file?.FileName ?? "lab_ranges.csv";

            if (file != null && file.Length > 0)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                base64Content = Convert.ToBase64String(ms.ToArray());
            }

            request = new BulkLabRangeDryRunRequest(
                StudyId: studyId,
                SiteId: siteId,
                LabSource: labSource,
                FileName: fileName,
                FileContentBase64: string.IsNullOrWhiteSpace(base64Content) ? null : base64Content,
                RawFileContent: string.IsNullOrWhiteSpace(base64Content) && !string.IsNullOrWhiteSpace(rawContent) ? rawContent : null
            );
        }
        else
        {
            request = await httpContext.Request.ReadFromJsonAsync<BulkLabRangeDryRunRequest>(ct)
                ?? throw new BadHttpRequestException("Invalid JSON request body.");
        }

        try
        {
            var response = await labRangeImportService.DryRunAsync(request, ct);
            return TypedResults.Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> CommitImportAsync(
        HttpContext httpContext,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        BulkLabRangeImportCommitRequest request;

        if (httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            string studyId = form["study_id"].FirstOrDefault() ?? form["studyId"].FirstOrDefault() ?? "STUDY-101";
            string? siteId = form["site_id"].FirstOrDefault() ?? form["siteId"].FirstOrDefault();
            string labSource = form["lab_source"].FirstOrDefault() ?? form["labSource"].FirstOrDefault() ?? "CENTRAL";
            string changeReason = form["reason_for_change"].FirstOrDefault() ?? form["change_reason"].FirstOrDefault() ?? "Bulk lab range import";
            string rawContent = form["raw_file_content"].FirstOrDefault() ?? form["raw_content"].FirstOrDefault() ?? string.Empty;
            string? dryRunId = form["dry_run_id"].FirstOrDefault() ?? form["dryRunId"].FirstOrDefault();

            string base64Content = string.Empty;
            string fileName = file?.FileName ?? "lab_ranges.csv";

            if (file != null && file.Length > 0)
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                base64Content = Convert.ToBase64String(ms.ToArray());
            }

            request = new BulkLabRangeImportCommitRequest(
                DryRunId: dryRunId,
                StudyId: studyId,
                SiteId: siteId,
                LabSource: labSource,
                FileName: fileName,
                FileContentBase64: string.IsNullOrWhiteSpace(base64Content) ? null : base64Content,
                RawFileContent: string.IsNullOrWhiteSpace(base64Content) && !string.IsNullOrWhiteSpace(rawContent) ? rawContent : null,
                ReasonForChange: changeReason
            );
        }
        else
        {
            request = await httpContext.Request.ReadFromJsonAsync<BulkLabRangeImportCommitRequest>(ct)
                ?? throw new BadHttpRequestException("Invalid JSON request body.");
        }

        string submittedBy = httpContext.User.ToAuthenticatedCaller()?.UserId ?? "DATA_MANAGER";
        string? headerChangeReason = ChangeReasonHeader.Read(httpContext.Request);
        if (!string.IsNullOrWhiteSpace(headerChangeReason))
        {
            request = request with { ReasonForChange = headerChangeReason };
        }

        try
        {
            var response = await labRangeImportService.CommitImportAsync(request, submittedBy, ct);
            return TypedResults.Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> GetBatchStatusAsync(
        string? study_id,
        string? studyId,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;
        var result = await labRangeImportService.GetImportJobsAsync(effectiveStudyId ?? "", ct);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetBatchStatusByIdAsync(
        Guid id,
        ILabRangeImportService labRangeImportService,
        CancellationToken ct)
    {
        var result = await labRangeImportService.GetImportJobByIdAsync(id, ct);
        if (result is null)
        {
            return TypedResults.NotFound(new { code = "JOB_NOT_FOUND", detail = $"Batch import job with ID '{id}' was not found." });
        }
        return TypedResults.Ok(result);
    }

    private static IResult ConvertUnit(
        UnitConversionRequest request,
        ILabRangeImportService labRangeImportService)
    {
        var response = labRangeImportService.ConvertUnit(request);
        return TypedResults.Ok(response);
    }

    private static IResult GetLoincCatalog(
        ILabRangeImportService labRangeImportService)
    {
        var response = labRangeImportService.GetLoincCatalog();
        return TypedResults.Ok(response);
    }
}
