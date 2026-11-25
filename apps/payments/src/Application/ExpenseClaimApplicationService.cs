using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Application service owning participant travel/meal expense claim queries, staff &amp; self-service submissions,
/// and coordinator review/approval workflows.
/// </summary>
public sealed class ExpenseClaimApplicationService(
    PaymentsUnitOfWork uow,
    IClock clock,
    ExpenseReceiptPolicyService policyService)
{
    public async Task<IResult> ListExpenseClaimsAsync(HttpContext httpContext, string? status = null, string? subjectCode = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToScope(uow.Context.ParticipantExpenseClaims.AsNoTracking(), httpContext, c => c.StudyId, c => c.SiteId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(subjectCode))
        {
            query = query.Where(c => c.SubjectId == subjectCode);
        }

        var claims = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Results.Ok(claims);
    }

    public async Task<IResult> SubmitStaffExpenseClaimAsync(SubmitExpenseClaimRequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId) || request.GrossAmount <= 0m)
        {
            return Results.BadRequest(new { detail = "subject_id and positive gross_amount are required." });
        }

        if (!Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return OutsideScope();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        DateTimeOffset now = clock.UtcNow;

        return await uow.ExecuteWalletTransactionAsync(async ct =>
        {
            var wallet = await uow.LockWalletBySubjectAndStudyAsync(request.SubjectId, request.StudyId, ct);

            if (wallet != null && !Reaches(httpContext, wallet))
            {
                return OutsideScope();
            }

            if (wallet == null)
            {
                wallet = new ParticipantWallet
                {
                    Id = Guid.NewGuid().ToString(),
                    SubjectId = request.SubjectId,
                    StudyId = request.StudyId,
                    SiteId = request.SiteId,
                    CurrentBalance = 0m,
                    LifetimeStipends = 0m,
                    LifetimeReimbursements = 0m,
                    LifetimeDisbursements = 0m,
                    Currency = "USD",
                    Status = "ACTIVE",
                    W9Status = "NOT_REQUIRED",
                    CreatedAt = now,
                    CreatedBy = userId,
                    UpdatedAt = now,
                    VersionIndex = 1
                };
                uow.Context.ParticipantWallets.Add(wallet);
            }

            var policy = await policyService.EvaluateServerExpensePolicyAsync(
                userId,
                wallet,
                rawCategory: request.Category,
                rawMerchantName: request.MerchantName,
                requestedGrossAmount: request.GrossAmount,
                isMealAllowance: request.IsMealAllowance,
                requestedExpenseDate: request.ExpenseDate,
                explicitOcrToken: request.OcrToken,
                receiptImageBase64: request.ReceiptImageBase64,
                rawReceiptOcrData: request.ReceiptOcrData,
                now: now,
                approvedSummaryNote: "Auto-approved based on low-risk expense policy (<$100, high OCR confidence).",
                ct: ct);

            var claim = new ParticipantExpenseClaim
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = request.SubjectId,
                StudyId = request.StudyId,
                SiteId = request.SiteId,
                ExpenseDate = policy.ResolvedExpenseDate,
                Category = policy.NormalizedCategory,
                MerchantName = policy.MerchantName,
                GrossAmount = policy.GrossAmount,
                TaxDeduction = policy.TaxDeduction,
                NetAmount = policy.NetAmount,
                Status = policy.InitialStatus,
                ReceiptImageName = !string.IsNullOrWhiteSpace(request.ReceiptImageName) ? request.ReceiptImageName : "receipt.jpg",
                ReceiptOcrDataJson = policy.ServerReceiptOcrJson,
                ReviewedBy = policy.CanAutoApprove ? AutoApprovalSystemUser : null,
                ReviewedAt = policy.CanAutoApprove ? now : null,
                ReviewNotes = policy.ReviewNotes,
                CreatedAt = now,
                CreatedBy = userId,
                UpdatedAt = now,
                VersionIndex = 1
            };

            ExpenseReceiptPolicyService.RecordServerReceiptVerificationAuditLogs(uow, claim, userId, rolesText, policy, now);

            if (policy.CanAutoApprove)
            {
                ExpenseReceiptPolicyService.ApplyAutoApprovedExpenseDisbursement(
                    uow,
                    wallet,
                    claim,
                    userId,
                    rolesText,
                    policy.OcrConfidence,
                    now,
                    auditAsSystemActor: true);
            }
            else
            {
                uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantExpenseClaim",
                    EntityId = claim.Id,
                    Action = "EXPENSE_CLAIM_SUBMITTED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        category = claim.Category,
                        gross = policy.GrossAmount,
                        net = policy.NetAmount,
                        merchant = claim.MerchantName,
                        ocr_confidence = policy.OcrConfidence,
                        status = claim.Status,
                        review_notes = policy.ReviewNotes
                    }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Travel/meal expense claim submitted for coordinator review",
                    VersionIndex = 1
                });
            }

            uow.Context.ParticipantExpenseClaims.Add(claim);
            await uow.CommitAsync(ct);

            return Results.Ok(claim);
        });
    }

    public async Task<IResult> SubmitSelfExpenseClaimAsync(
        SelfServiceSubmitExpenseRequestDto request,
        HttpContext httpContext,
        string? idempotencyKeyHeader = null)
    {
        if (!TryValidateSelfServiceCaller(httpContext, idempotencyKeyHeader, request.IdempotencyKey,
                out var caller, out string rolesText, out string? idempotencyKey, out var preambleError))
        {
            return preambleError!;
        }

        DateTimeOffset now = clock.UtcNow;
        string? resolvedWalletId = null;

        try
        {
            return await uow.ExecuteWalletTransactionAsync(async ct =>
            {
                var (wallet, error) = await LockOrProvisionOwnWalletAsync(
                    uow,
                    httpContext,
                    caller,
                    request.SubjectId,
                    request.WalletId,
                    request.StudyCode,
                    request.SiteCode,
                    now,
                    ct);
                if (error != null)
                {
                    return error;
                }

                resolvedWalletId = wallet!.Id;

                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    var existingClaim = await uow.Context.ParticipantExpenseClaims.AsNoTracking()
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

                var policy = await policyService.EvaluateServerExpensePolicyAsync(
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
                    ReviewedBy = policy.CanAutoApprove ? AutoApprovalSystemUser : null,
                    ReviewedAt = policy.CanAutoApprove ? now : null,
                    ReviewNotes = policy.ReviewNotes,
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    UpdatedAt = now,
                    VersionIndex = 1
                };

                ExpenseReceiptPolicyService.RecordServerReceiptVerificationAuditLogs(uow, claim, caller.UserId, rolesText, policy, now);

                if (policy.CanAutoApprove)
                {
                    ExpenseReceiptPolicyService.ApplyAutoApprovedExpenseDisbursement(
                        uow,
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
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
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

                uow.Context.ParticipantExpenseClaims.Add(claim);
                await uow.CommitAsync(ct);

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
                    CreatedAt: claim.CreatedAt));
            });
        }
        catch (DbUpdateException) when (!string.IsNullOrEmpty(idempotencyKey))
        {
            uow.Context.ChangeTracker.Clear();
            var racedWallet = resolvedWalletId is not null
                ? await uow.Context.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == resolvedWalletId)
                : await uow.Context.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.SubjectId == caller.UserId);
            if (racedWallet != null)
            {
                var racedClaim = await uow.Context.ParticipantExpenseClaims.AsNoTracking()
                    .FirstOrDefaultAsync(c =>
                        c.WalletId == racedWallet.Id
                        && c.SubjectId == caller.UserId
                        && c.IdempotencyKey == idempotencyKey);
                if (racedClaim != null)
                {
                    return BuildSelfServiceExpenseClaimResponse(racedClaim, racedWallet, caller.UserId);
                }
            }

            throw;
        }
    }

    public async Task<IResult> ReviewExpenseClaimAsync(string claimId, ReviewExpenseClaimRequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        DateTimeOffset now = clock.UtcNow;

        return await uow.ExecuteWalletTransactionAsync(async ct =>
        {
            var claim = await uow.Context.ParticipantExpenseClaims
                .FirstOrDefaultAsync(c => c.Id == claimId, ct);

            if (claim == null || !Reaches(httpContext, claim.StudyId, claim.SiteId))
            {
                return Results.NotFound(new { detail = $"Expense claim '{claimId}' not found." });
            }

            try
            {
                if (string.Equals(request.Status, ParticipantExpenseClaimStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(claim.CreatedBy)
                        && string.Equals(claim.CreatedBy, userId, StringComparison.Ordinal))
                    {
                        return Results.Conflict(new { detail = "The user who submitted this expense claim cannot also approve it." });
                    }

                    if (!claim.IsMealExpenseClaim())
                    {
                        claim.TaxDeduction = 0m;
                    }

                    claim.Approve(userId, now, request.ReviewNotes, request.AdjustedAmount);
                }
                else if (string.Equals(request.Status, ParticipantExpenseClaimStatus.REJECTED.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    claim.Reject(userId, now, request.ReviewNotes);
                }
                else
                {
                    return Results.Conflict(new { detail = $"Invalid review status '{request.Status}'. Only APPROVED or REJECTED are permitted." });
                }
            }
            catch (PaymentDomainException ex)
            {
                return Results.Conflict(new { detail = ex.Message });
            }

            if (string.Equals(claim.Status, ParticipantExpenseClaimStatus.APPROVED.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var wallet = await uow.LockWalletByIdAsync(claim.WalletId, ct);

                if (wallet == null)
                {
                    wallet = await uow.LockWalletBySubjectAndStudyAsync(claim.SubjectId, claim.StudyId, ct);
                }

                if (wallet != null)
                {
                    claim.RecordWalletCredit();
                    wallet.CurrentBalance += claim.NetAmount;
                    wallet.LifetimeReimbursements += claim.NetAmount;
                    wallet.UpdatedAt = now;
                    wallet.VersionIndex += 1;

                    uow.Context.ParticipantTransactions.Add(new ParticipantTransaction
                    {
                        Id = Guid.NewGuid().ToString(),
                        WalletId = wallet.Id,
                        SubjectId = wallet.SubjectId,
                        StudyId = wallet.StudyId,
                        TransactionType = "REIMBURSEMENT_CREDIT",
                        Amount = claim.NetAmount,
                        BalanceAfter = wallet.CurrentBalance,
                        ReferenceId = claim.Id,
                        Description = $"Reimbursement approval for {claim.Category} expense ({claim.MerchantName})",
                        CreatedAt = now,
                        CreatedBy = userId,
                        VersionIndex = 1
                    });
                }

                uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantExpenseClaim",
                    EntityId = claim.Id,
                    Action = "EXPENSE_CLAIM_APPROVED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new { claim_id = claim.Id, net_amount = claim.NetAmount, status = claim.Status }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Expense claim approved by coordinator",
                    VersionIndex = claim.VersionIndex
                });
            }
            else
            {
                uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantExpenseClaim",
                    EntityId = claim.Id,
                    Action = "EXPENSE_CLAIM_REJECTED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new { claim_id = claim.Id, status = claim.Status, notes = request.ReviewNotes }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Expense claim rejected by coordinator",
                    VersionIndex = claim.VersionIndex
                });
            }

            await uow.CommitAsync(ct);

            return Results.Ok(claim);
        });
    }
}
