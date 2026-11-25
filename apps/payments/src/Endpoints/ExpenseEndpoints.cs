using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// Schedule-of-Activities stipend rules, verified eCRF visit events, participant diary stipends,
/// receipt OCR scanning, and travel/meal expense reimbursement route slice (10 routes).
/// </summary>
public static class ExpenseEndpoints
{
    public static IEndpointRouteBuilder MapExpenseEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapGet("/participant/stipend-rules", ListStipendRules)
            .WithName("ListStipendRules")
            .RequirePermission("payments_stipend_rule:read").RequireCallerStudyScope()
            .WithSummary("List SoA protocol visit stipend compensation rules.");

        group.MapPost("/participant/stipend-rules", CreateStipendRule)
            .WithName("CreateStipendRule")
            .RequirePermission("payments_stipend_rule:configure").RequireCallerStudyScope()
            .WithSummary("Configure participant visit compensation rules linked to Schedule of Activities.");

        group.MapPost("/participant/events/visit-verified", IngestVisitVerifiedEvent)
            .WithName("IngestVisitVerifiedEvent")
            .RequirePermission("payments_visit_event:record").RequireCallerStudyScope()
            .WithSummary("Receives verified eCRF visit completion event and evaluates automated stipend disbursement rules.");

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

        group.MapPost("/participant/me/diary-stipend", CreditSelfDiaryStipend)
            .WithName("CreditSelfDiaryStipend")
            .RequirePermission("payments_self_stipend:credit").RequireCallerStudyScope()
            .WithSummary("Credits protocol stipend for a synchronized participant diary strictly from server rules.");

        return routes;
    }

    internal static Task<IResult> ListStipendRules(
        [FromServices] ParticipantStipendApplicationService stipendService,
        HttpContext httpContext,
        string? studyId = null) =>
        stipendService.ListStipendRulesAsync(httpContext, studyCode: studyId);

    internal static Task<IResult> CreateStipendRule(
        ParticipantStipendRuleCreateDto request,
        [FromServices] ParticipantStipendApplicationService stipendService,
        HttpContext httpContext) =>
        stipendService.CreateStipendRuleAsync(request, httpContext);

    internal static Task<IResult> IngestVisitVerifiedEvent(
        VisitVerificationEventDto request,
        [FromServices] ParticipantStipendApplicationService stipendService,
        HttpContext httpContext) =>
        stipendService.IngestVisitVerifiedEventAsync(request, httpContext);

    internal static Task<IResult> ListExpenseClaims(
        [FromServices] ExpenseClaimApplicationService expenseService,
        HttpContext httpContext,
        string? status = null,
        string? subjectId = null) =>
        expenseService.ListExpenseClaimsAsync(httpContext, status: status, subjectCode: subjectId);

    internal static Task<IResult> SubmitStaffExpenseClaim(
        SubmitExpenseClaimRequestDto request,
        [FromServices] ExpenseClaimApplicationService expenseService,
        HttpContext httpContext) =>
        expenseService.SubmitStaffExpenseClaimAsync(request, httpContext);

    internal static Task<IResult> ExtractStaffReceiptOcr(
        OcrExtractRequestDto request,
        [FromServices] ExpenseReceiptPolicyService policyService,
        HttpContext httpContext) =>
        policyService.ExtractReceiptOcrAsync(request, httpContext);

    internal static Task<IResult> ReviewExpenseClaim(
        string claimId,
        ReviewExpenseClaimRequestDto request,
        [FromServices] ExpenseClaimApplicationService expenseService,
        HttpContext httpContext) =>
        expenseService.ReviewExpenseClaimAsync(claimId, request, httpContext);

    internal static Task<IResult> ExtractSelfReceiptOcr(
        OcrExtractRequestDto request,
        [FromServices] ExpenseReceiptPolicyService policyService,
        HttpContext httpContext) =>
        policyService.ExtractReceiptOcrAsync(request, httpContext);

    internal static Task<IResult> SubmitSelfExpenseClaim(
        SelfServiceSubmitExpenseRequestDto request,
        [FromServices] ExpenseClaimApplicationService expenseService,
        HttpContext httpContext,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader = null) =>
        expenseService.SubmitSelfExpenseClaimAsync(request, httpContext, idempotencyKeyHeader);

    internal static Task<IResult> CreditSelfDiaryStipend(
        SelfServiceDiaryStipendRequestDto request,
        [FromServices] ParticipantStipendApplicationService stipendService,
        HttpContext httpContext) =>
        stipendService.CreditSelfDiaryStipendAsync(request, httpContext);
}
