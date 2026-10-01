using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application.Services;
using Cadence.Apps.Payments.Infrastructure.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// HTTP Minimal API endpoints for Site Fee Schedule management and bulk CSV uploader.
/// </summary>
public static class FeeScheduleEndpoints
{
    public static IEndpointRouteBuilder MapFeeScheduleEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments/fee-schedules")
            .WithTags("FeeSchedules");

        group.MapPost("/bulk-preview", PreviewBulkCsv)
            .WithName("PreviewBulkFeeScheduleCsv")
            .WithSummary("Parse and validate site fee schedule CSV upload with calculation preview.")
            .Produces<BulkFeeScheduleValidationResult>(StatusCodes.Status200OK);

        group.MapPost("/bulk-commit", CommitBulkCsv)
            .WithName("CommitBulkFeeScheduleCsv")
            .WithSummary("Commit validated site fee schedule rows in a single batch transaction.")
            .Produces<BulkFeeScheduleCommitResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/template", DownloadCsvTemplate)
            .WithName("DownloadFeeScheduleCsvTemplate")
            .WithSummary("Download standardized CSV site fee schedule template with study/site pre-population.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        group.MapGet("/", ListFeeSchedules)
            .WithName("ListFeeSchedules")
            .WithSummary("List active site fee schedule rate entries.")
            .Produces(StatusCodes.Status200OK);

        return routes;
    }

    public sealed record PreviewRequest(string? CsvContent);

    private static async Task<IResult> PreviewBulkCsv(
        HttpContext context,
        [FromServices] IBulkFeeScheduleImporterService importerService)
    {
        string csvContent = string.Empty;

        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file != null)
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
                csvContent = await reader.ReadToEndAsync();
            }
            else if (form.TryGetValue("csvContent", out var formContent))
            {
                csvContent = formContent.ToString();
            }
        }
        else if (context.Request.HasJsonContentType())
        {
            var body = await context.Request.ReadFromJsonAsync<PreviewRequest>();
            csvContent = body?.CsvContent ?? string.Empty;
        }
        else
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            csvContent = await reader.ReadToEndAsync();
        }

        var result = importerService.ValidateCsv(csvContent);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> CommitBulkCsv(
        [FromBody] BulkFeeScheduleCommitRequest request,
        [FromServices] IBulkFeeScheduleImporterService importerService,
        HttpContext context,
        CancellationToken ct)
    {
        string userActor = request.CreatedBy
            ?? context.User?.Identity?.Name
            ?? context.User?.FindFirst("sub")?.Value
            ?? "sponsor_finance_manager";

        var result = await importerService.CommitBulkFeeScheduleAsync(request.Rows, userActor, ct);
        if (!result.Success)
        {
            return TypedResults.BadRequest(result);
        }

        return TypedResults.Ok(result);
    }

    private static IResult DownloadCsvTemplate(
        [FromServices] IBulkFeeScheduleImporterService importerService,
        [FromQuery] string? studyId = null,
        [FromQuery] string? siteId = null)
    {
        string templateCsv = importerService.GenerateCsvTemplate(studyId, siteId);
        byte[] bytes = Encoding.UTF8.GetBytes(templateCsv);
        return TypedResults.Bytes(bytes, contentType: "text/csv", fileDownloadName: "site_fee_schedule_template.csv");
    }

    private static async Task<IResult> ListFeeSchedules(
        [FromServices] PaymentsDbContext dbContext,
        [FromQuery] string? contractId = null,
        [FromQuery] string? studyId = null,
        [FromQuery] string? siteId = null,
        CancellationToken ct = default)
    {
        var query = dbContext.FeeSchedules.AsNoTracking().Where(f => f.IsActive);

        if (!string.IsNullOrWhiteSpace(contractId))
        {
            query = query.Where(f => f.ContractId == contractId);
        }
        else if (!string.IsNullOrWhiteSpace(studyId) || !string.IsNullOrWhiteSpace(siteId))
        {
            var contractIds = dbContext.BudgetContracts.AsNoTracking().Where(b => b.IsActive);
            if (!string.IsNullOrWhiteSpace(studyId))
            {
                contractIds = contractIds.Where(b => b.StudyId == studyId);
            }
            if (!string.IsNullOrWhiteSpace(siteId))
            {
                contractIds = contractIds.Where(b => b.SiteId == siteId);
            }
            var matchingIds = await contractIds.Select(b => b.Id).ToListAsync(ct);
            query = query.Where(f => matchingIds.Contains(f.ContractId));
        }

        var items = await query.ToListAsync(ct);
        return TypedResults.Ok(items);
    }
}
