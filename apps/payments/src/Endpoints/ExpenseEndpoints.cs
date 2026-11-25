using System.Threading.Tasks;

using Cadence.Apps.Payments.Application.Services;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// Travel and meal expense claims, OCR receipt extraction, coordinator reviews, disbursement settlements, and audit logs (10 routes).
/// </summary>
public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapGet("/participant/expenses", ListExpenseClaims)
            .WithName("ListExpenseClaims")
            .RequirePermission("payments_expense:read").RequireCallerStudyScope()
            .WithSummary("List travel and meal expense reimbursement claims.");

        group.MapPost("/participant/expenses", SubmitStaffExpenseClaim)
            .WithName("SubmitExpenseClaim")
            .RequirePermission("payments_expense:submit").RequireCallerStudyScope()
            .WithSummary("Submit travel/meal expense claim for coordinator review.");

        group.MapPost("/participant/expenses/ocr-extract", ExtractStaffReceiptOcr)
            .WithName("ExtractReceiptOcr")
            .RequirePermission("payments_expense:submit").RequireCallerStudyScope()
            .WithSummary("Extract metadata from receipt image/text using rule-based OCR engine.");

        group.MapPost("/participant/expenses/{claimId}/review", ReviewExpenseClaim)
            .WithName("ReviewExpenseClaim")
            .RequirePermission("payments_expense:review").RequireCallerStudyScope()
            .WithSummary("Coordinator 1-click review and approval/rejection of expense claim.");

        group.MapPost("/participant/me/expenses/ocr-extract", ExtractSelfReceiptOcr)
            .WithName("ExtractSelfReceiptOcr")
            .RequirePermission("payments_self_expense:ocr_scan").RequireCallerStudyScope()
            .WithSummary("Extracts merchant, date, category, and amount from an uploaded receipt image for the signed participant.");

        group.MapPost("/participant/me/expenses", SubmitSelfExpenseClaim)
            .WithName("SubmitSelfExpenseClaim")
            .RequirePermission("payments_self_expense:submit").RequireCallerStudyScope()
            .WithSummary("Submits a travel or meal expense claim for the signed participant with policy auto-approval and direct ACH dispatch.");

        group.MapGet("/participant/transactions", ListTransactions)
            .WithName("ListTransactions")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("List participant wallet transactions ledger.");

        group.MapGet("/participant/disbursements", ListDisbursements)
            .WithName("ListDisbursements")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("List digital disbursement dispatches.");

        group.MapPost("/participant/disbursements/{id}/settle", SettleDisbursement)
            .WithName("SettleDisbursement")
            .RequirePermission("payments_wallet:disburse").RequireCallerStudyScope()
            .WithSummary("Marks a dispatched digital disbursement as settled upon confirmation.");

        group.MapGet("/audit-logs", GetAuditLogs)
            .WithName("GetPaymentAuditLogs")
            .RequirePermission("payments_audit_log:read").RequireCallerStudyScope()
            .WithSummary("Retrieve payment audit ledger records.");

        return routes;
    }

    public static Task<IResult> ListExpenseClaims(
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext,
        string? status = null,
        string? subjectId = null) =>
        service.ListExpenseClaimsAsync(httpContext, status, subjectId);

    public static Task<IResult> SubmitStaffExpenseClaim(
        SubmitExpenseClaimRequestDto request,
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext) =>
        service.SubmitStaffExpenseClaimAsync(request, httpContext);

    public static Task<IResult> ExtractStaffReceiptOcr(
        OcrExtractRequestDto request,
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext) =>
        service.ExtractReceiptOcrAsync(request, httpContext);

    public static Task<IResult> ReviewExpenseClaim(
        string claimId,
        ReviewExpenseClaimRequestDto request,
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext) =>
        service.ReviewExpenseClaimAsync(claimId, request, httpContext);

    public static Task<IResult> ExtractSelfReceiptOcr(
        OcrExtractRequestDto request,
        [FromServices] SelfServiceExpenseApplicationService service,
        HttpContext httpContext) =>
        service.ExtractSelfReceiptOcrAsync(request, httpContext);

    public static Task<IResult> SubmitSelfExpenseClaim(
        SelfServiceSubmitExpenseRequestDto request,
        [FromServices] SelfServiceExpenseApplicationService service,
        HttpContext httpContext,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader = null) =>
        service.SubmitSelfExpenseClaimAsync(request, httpContext, idempotencyKeyHeader);

    public static Task<IResult> ListTransactions(
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null) =>
        service.ListTransactionsAsync(httpContext, subjectId, walletId);

    public static Task<IResult> ListDisbursements(
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null) =>
        service.ListDisbursementsAsync(httpContext, subjectId, walletId);

    public static Task<IResult> SettleDisbursement(
        string id,
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext) =>
        service.SettleDisbursementAsync(id, httpContext);

    public static Task<IResult> GetAuditLogs(
        [FromServices] ExpenseApplicationService service,
        HttpContext httpContext,
        string? subjectId = null) =>
        service.GetAuditLogsAsync(httpContext, subjectId);
}
