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
/// Participant wallet directory, reloadable card issuance, staff &amp; self-service payouts,
/// ledger transactions, disbursement settlement, and audit log route slice (14 routes).
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

        group.MapGet("/participant/me/wallet", GetSelfParticipantWallet)
            .WithName("GetSelfParticipantWallet")
            .RequirePermission("payments_self_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieves the authenticated participant's own study wallet balances, masks, and ledger activity.");

        group.MapPost("/participant/me/disburse", DisburseSelfWalletBalance)
            .WithName("DisburseSelfWalletBalance")
            .RequirePermission("payments_self_wallet:disburse").RequireCallerStudyScope()
            .WithSummary("Triggers an on-demand payout of the signed participant's available wallet balance.");

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

    internal static Task<IResult> DisburseStipendDirect(
        DisburseStipendRequest request,
        [FromServices] ParticipantStipendApplicationService stipendService,
        HttpContext httpContext) =>
        stipendService.DisburseStipendDirectAsync(request, httpContext);

    internal static Task<IResult> ListWallets(
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext) =>
        walletService.ListWalletsAsync(httpContext);

    internal static Task<IResult> GetSubjectWallet(
        string subjectId,
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext) =>
        walletService.GetSubjectWalletAsync(subjectId, httpContext);

    internal static Task<IResult> GetWalletById(
        string walletId,
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext) =>
        walletService.GetWalletByIdAsync(walletId, httpContext);

    internal static Task<IResult> GetBalanceInquiry(
        string walletId,
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext) =>
        walletService.GetBalanceInquiryAsync(walletId, httpContext);

    internal static Task<IResult> IssueReloadableCard(
        string walletId,
        IssueCardRequestDto request,
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext) =>
        walletService.IssueReloadableCardAsync(walletId, request, httpContext);

    internal static Task<IResult> DisburseParticipantFunds(
        string walletId,
        DisburseFundsRequestDto request,
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext) =>
        walletService.DisburseParticipantFundsAsync(walletId, request, httpContext);

    internal static Task<IResult> GetSelfParticipantWallet(
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null,
        string? studyId = null) =>
        walletService.GetSelfParticipantWalletAsync(
            httpContext,
            subjectCode: subjectId,
            walletCode: walletId,
            studyCode: studyId);

    internal static Task<IResult> DisburseSelfWalletBalance(
        SelfServiceDisburseRequestDto request,
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader = null) =>
        walletService.DisburseSelfWalletBalanceAsync(request, httpContext, idempotencyKeyHeader);

    internal static Task<IResult> ListTransactions(
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null) =>
        walletService.ListTransactionsAsync(httpContext, subjectCode: subjectId, walletCode: walletId);

    internal static Task<IResult> ListDisbursements(
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null) =>
        walletService.ListDisbursementsAsync(httpContext, subjectCode: subjectId, walletCode: walletId);

    internal static Task<IResult> SettleDisbursement(
        string id,
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext) =>
        walletService.SettleDisbursementAsync(id, httpContext);

    internal static Task<IResult> GetAuditLogs(
        [FromServices] ParticipantWalletApplicationService walletService,
        HttpContext httpContext,
        string? subjectId = null) =>
        walletService.GetAuditLogsAsync(httpContext, subjectCode: subjectId);
}
