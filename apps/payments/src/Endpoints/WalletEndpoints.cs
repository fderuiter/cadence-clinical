using System.Threading.Tasks;

using Cadence.Apps.Payments.Application.Services;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// Participant wallet directory, stipend rules, visit verification stipends, and self-service wallet endpoints (14 routes).
/// </summary>
public static class WalletEndpoints
{
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapPost("/disburse", DisburseStipendDirect)
            .WithName("DisburseStipendDirect")
            .RequirePermission("payments_stipend:disburse").RequireCallerStudyScope()
            .WithSummary("Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail.");

        group.MapPost("/participant/wallets/disburse", DisburseStipendDirect)
            .WithName("DisburseStipendWallet")
            .RequirePermission("payments_stipend:disburse").RequireCallerStudyScope()
            .WithSummary("Disburse stipend to participant wallet.");

        group.MapGet("/participant/wallets", ListWallets)
            .WithName("ListWallets")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("List all participant wallets.");

        group.MapGet("/participant/wallets/subject/{subjectId}", GetSubjectWallet)
            .WithName("GetSubjectWallet")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieve participant wallet balance and details.");

        group.MapGet("/participant/wallets/{walletId}", GetWalletById)
            .WithName("GetWalletById")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieve participant wallet details by wallet ID.");

        group.MapGet("/participant/wallets/{walletId}/balance", GetBalanceInquiry)
            .WithName("GetBalanceInquiry")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Real-time balance inquiry for participant wallet.");

        group.MapPost("/participant/wallets/{walletId}/issue-card", IssueReloadableCard)
            .WithName("IssueReloadableCard")
            .RequirePermission("payments_wallet:issue_card").RequireCallerStudyScope()
            .WithSummary("Issue or re-issue reloadable debit card for participant wallet.");

        group.MapPost("/participant/wallets/{walletId}/disburse", DisburseParticipantFunds)
            .WithName("DisburseParticipantFunds")
            .RequirePermission("payments_wallet:disburse").RequireCallerStudyScope()
            .WithSummary("Disburse digital funds from participant wallet to debit card or bank account.");

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

        group.MapGet("/participant/me/wallet", GetSelfParticipantWallet)
            .WithName("GetSelfParticipantWallet")
            .RequirePermission("payments_self_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieves the authenticated participant's own study wallet balances, masks, and ledger activity.");

        group.MapPost("/participant/me/disburse", DisburseSelfWalletBalance)
            .WithName("DisburseSelfWalletBalance")
            .RequirePermission("payments_self_wallet:disburse").RequireCallerStudyScope()
            .WithSummary("Triggers an on-demand payout of the signed participant's available wallet balance.");

        group.MapPost("/participant/me/diary-stipend", CreditSelfDiaryStipend)
            .WithName("CreditSelfDiaryStipend")
            .RequirePermission("payments_self_stipend:credit").RequireCallerStudyScope()
            .WithSummary("Credits protocol stipend for a synchronized participant diary strictly from server rules.");

        return routes;
    }

    public static Task<IResult> DisburseStipendDirect(
        DisburseStipendRequest request,
        [FromServices] StipendApplicationService service,
        HttpContext httpContext) =>
        service.DisburseStipendDirectAsync(request, httpContext);

    public static Task<IResult> ListWallets(
        [FromServices] WalletApplicationService service,
        HttpContext httpContext) =>
        service.ListWalletsAsync(httpContext);

    public static Task<IResult> GetSubjectWallet(
        string subjectId,
        [FromServices] WalletApplicationService service,
        HttpContext httpContext) =>
        service.GetSubjectWalletAsync(subjectId, httpContext);

    public static Task<IResult> GetWalletById(
        string walletId,
        [FromServices] WalletApplicationService service,
        HttpContext httpContext) =>
        service.GetWalletByIdAsync(walletId, httpContext);

    public static Task<IResult> GetBalanceInquiry(
        string walletId,
        [FromServices] WalletApplicationService service,
        HttpContext httpContext) =>
        service.GetBalanceInquiryAsync(walletId, httpContext);

    public static Task<IResult> IssueReloadableCard(
        string walletId,
        IssueCardRequestDto request,
        [FromServices] WalletApplicationService service,
        HttpContext httpContext) =>
        service.IssueReloadableCardAsync(walletId, request, httpContext);

    public static Task<IResult> DisburseParticipantFunds(
        string walletId,
        DisburseFundsRequestDto request,
        [FromServices] WalletApplicationService service,
        HttpContext httpContext) =>
        service.DisburseParticipantFundsAsync(walletId, request, httpContext);

    public static Task<IResult> ListStipendRules(
        [FromServices] StipendApplicationService service,
        HttpContext httpContext,
        string? studyId = null) =>
        service.ListStipendRulesAsync(httpContext, studyId);

    public static Task<IResult> CreateStipendRule(
        ParticipantStipendRuleCreateDto request,
        [FromServices] StipendApplicationService service,
        HttpContext httpContext) =>
        service.CreateStipendRuleAsync(request, httpContext);

    public static Task<IResult> IngestVisitVerifiedEvent(
        VisitVerificationEventDto request,
        [FromServices] StipendApplicationService service,
        HttpContext httpContext) =>
        service.IngestVisitVerifiedEventAsync(request, httpContext);

    public static Task<IResult> GetSelfParticipantWallet(
        [FromServices] WalletApplicationService service,
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null,
        string? studyId = null) =>
        service.GetSelfParticipantWalletAsync(httpContext, subjectId, walletId, studyId);

    public static Task<IResult> DisburseSelfWalletBalance(
        SelfServiceDisburseRequestDto request,
        [FromServices] WalletApplicationService service,
        HttpContext httpContext,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader = null) =>
        service.DisburseSelfWalletBalanceAsync(request, httpContext, idempotencyKeyHeader);

    public static Task<IResult> CreditSelfDiaryStipend(
        SelfServiceDiaryStipendRequestDto request,
        [FromServices] DiaryStipendApplicationService service,
        HttpContext httpContext) =>
        service.CreditSelfDiaryStipendAsync(request, httpContext);
}
