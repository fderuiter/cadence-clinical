using System;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Payments.Application.Services;

/// <summary>
/// Handles authenticated participant self-service receipt OCR scans and idempotent expense claim submissions.
/// </summary>
public sealed class SelfServiceExpenseApplicationService(
    PaymentsDbContext dbContext,
    IPaymentsUnitOfWork unitOfWork,
    IWalletRowLockRepository walletLocks,
    IClock clock,
    ExpenseApplicationService expenseService,
    IOptions<GatewayAuthenticationOptions> gatewayAuthentication)
{
    public Task<IResult> ExtractSelfReceiptOcrAsync(OcrExtractRequestDto request, HttpContext httpContext) =>
        expenseService.ExtractReceiptOcrAsync(request, httpContext);

    public Task<IResult> SubmitSelfExpenseClaimAsync(
        SelfServiceSubmitExpenseRequestDto request,
        HttpContext httpContext,
        string? idempotencyKeyHeader = null)
    {
        var scope = new SelfWalletMutationScope(
            dbContext,
            walletLocks,
            httpContext,
            idempotencyKeyHeader,
            request.IdempotencyKey,
            request.SubjectId,
            request.WalletId,
            request.StudyCode,
            request.SiteCode,
            CreateIfMissing: true,
            clock.UtcNow);

        return SelfWalletResolver.ExecuteLockedOwnWalletMutationAsync(
            scope,
            async ctx =>
            {
                var wallet = ctx.Wallet;
                var caller = ctx.Caller;
                string rolesText = ctx.RolesText;
                string? idempotencyKey = ctx.IdempotencyKey;
                DateTimeOffset now = ctx.Now;
                var ct = ctx.CancellationToken;

                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    var existingClaim = await dbContext.ParticipantExpenseClaims.AsNoTracking()
                        .FirstOrDefaultAsync(c =>
                            c.WalletId == wallet.Id
                            && c.SubjectId == caller.UserId
                            && c.IdempotencyKey == idempotencyKey, ct);
                    if (existingClaim != null)
                    {
                        return BuildSelfServiceExpenseClaimResponse(existingClaim, wallet, caller.UserId);
                    }
                }

                if (request.GrossAmount <= 0m)
                {
                    return Results.BadRequest(new { detail = "Expense gross amount must be greater than zero." });
                }

                if (string.IsNullOrWhiteSpace(request.MerchantName))
                {
                    return Results.BadRequest(new { detail = "Merchant name is required to submit an expense claim." });
                }

                var policy = await ExpensePolicyEvaluator.EvaluateServerExpensePolicyAsync(
                    dbContext,
                    caller.UserId,
                    wallet,
                    rawCategory: request.Category,
                    rawMerchantName: request.MerchantName,
                    requestedGrossAmount: request.GrossAmount,
                    isMealAllowance: request.IsMealAllowance,
                    requestedExpenseDate: request.ExpenseDate,
                    explicitOcrToken: request.OcrToken,
                    receiptImageBase64: request.ReceiptImageBase64,
                    rawReceiptOcrData: request.ReceiptOcrData,
                    gatewaySecret: PaymentHmacTokenHelper.ResolveConfiguredGatewaySecret(gatewayAuthentication),
                    now: now,
                    approvedSummaryNote: "Auto-approved under participant expense policy and dispatched via direct ACH.",
                    ct: ct);

                var claim = new ParticipantExpenseClaim
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = caller.UserId,
                    StudyId = wallet.StudyId,
                    SiteId = wallet.SiteId,
                    ExpenseDate = policy.ResolvedExpenseDate,
                    Category = policy.NormalizedCategory,
                    MerchantName = policy.MerchantName,
                    GrossAmount = policy.GrossAmount,
                    TaxDeduction = policy.TaxDeduction,
                    NetAmount = policy.NetAmount,
                    Status = policy.InitialStatus,
                    ReceiptImageName = !string.IsNullOrWhiteSpace(request.ReceiptImageName) ? request.ReceiptImageName : "receipt.png",
                    ReceiptOcrDataJson = policy.ServerReceiptOcrJson,
                    IdempotencyKey = idempotencyKey,
                    ReviewedBy = policy.CanAutoApprove ? PaymentScopeAndMappingHelpers.AutoApprovalSystemUser : null,
                    ReviewedAt = policy.CanAutoApprove ? now : null,
                    ReviewNotes = policy.ReviewNotes,
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    UpdatedAt = now,
                    VersionIndex = 1
                };

                ExpensePolicyEvaluator.RecordServerReceiptVerificationAuditLogs(dbContext, claim, caller.UserId, rolesText, policy, now);

                if (policy.CanAutoApprove)
                {
                    ExpensePolicyEvaluator.ApplyAutoApprovedExpenseDisbursement(
                        dbContext,
                        wallet,
                        claim,
                        caller.UserId,
                        rolesText,
                        policy.OcrConfidence,
                        now,
                        auditAsSystemActor: false);
                }
                else
                {
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantExpenseClaim",
                        EntityId = claim.Id,
                        Action = "EXPENSE_CLAIM_SUBMITTED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            submitted_by = caller.UserId,
                            category = claim.Category,
                            gross = policy.GrossAmount,
                            net = policy.NetAmount,
                            merchant = claim.MerchantName,
                            ocr_confidence = policy.OcrConfidence,
                            status = claim.Status,
                            review_notes = policy.ReviewNotes
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Participant expense claim held for coordinator review",
                        VersionIndex = 1
                    });
                }

                dbContext.ParticipantExpenseClaims.Add(claim);
                await unitOfWork.CommitAsync(ct);

                return Results.Ok(new SelfServiceExpenseClaimResponseDto(
                    Id: claim.Id,
                    WalletId: wallet.Id,
                    SubjectId: caller.UserId,
                    StudyCode: wallet.StudyId,
                    SiteCode: wallet.SiteId,
                    ExpenseDate: claim.ExpenseDate,
                    Category: claim.Category,
                    MerchantName: claim.MerchantName,
                    GrossAmount: claim.GrossAmount,
                    TaxDeduction: claim.TaxDeduction,
                    NetAmount: claim.NetAmount,
                    Status: claim.Status,
                    ApprovalDecision: policy.ApprovalDecision,
                    AutoApproved: policy.CanAutoApprove,
                    DisbursementReference: claim.DisbursementReference,
                    ReviewNotes: claim.ReviewNotes,
                    ReceiptImageName: claim.ReceiptImageName,
                    WalletCurrentBalance: wallet.CurrentBalance,
                    LifetimeStipends: wallet.LifetimeStipends,
                    LifetimeReimbursements: wallet.LifetimeReimbursements,
                    LifetimeDisbursements: wallet.LifetimeDisbursements,
                    CreatedAt: claim.CreatedAt
                ));
            },
            async (racedWallet, caller, idempotencyKey) =>
            {
                var racedClaim = await dbContext.ParticipantExpenseClaims.AsNoTracking()
                    .FirstOrDefaultAsync(c =>
                        c.WalletId == racedWallet.Id
                        && c.SubjectId == caller.UserId
                        && c.IdempotencyKey == idempotencyKey);
                return racedClaim != null
                    ? BuildSelfServiceExpenseClaimResponse(racedClaim, racedWallet, caller.UserId)
                    : null;
            });
    }

    private static IResult BuildSelfServiceExpenseClaimResponse(
        ParticipantExpenseClaim existingClaim,
        ParticipantWallet wallet,
        string subjectId)
    {
        bool wasAutoApproved = !string.IsNullOrEmpty(existingClaim.DisbursementReference)
            && string.Equals(
                existingClaim.Status,
                ParticipantExpenseClaimStatus.APPROVED.ToString(),
                StringComparison.OrdinalIgnoreCase);
        string existingDecision = wasAutoApproved ? "AUTO_APPROVED" : existingClaim.Status;

        return Results.Ok(new SelfServiceExpenseClaimResponseDto(
            Id: existingClaim.Id,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            StudyCode: wallet.StudyId,
            SiteCode: wallet.SiteId,
            ExpenseDate: existingClaim.ExpenseDate,
            Category: existingClaim.Category,
            MerchantName: existingClaim.MerchantName,
            GrossAmount: existingClaim.GrossAmount,
            TaxDeduction: existingClaim.TaxDeduction,
            NetAmount: existingClaim.NetAmount,
            Status: existingClaim.Status,
            ApprovalDecision: existingDecision,
            AutoApproved: wasAutoApproved,
            DisbursementReference: existingClaim.DisbursementReference,
            ReviewNotes: existingClaim.ReviewNotes,
            ReceiptImageName: existingClaim.ReceiptImageName,
            WalletCurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            CreatedAt: existingClaim.CreatedAt
        ));
    }
}
