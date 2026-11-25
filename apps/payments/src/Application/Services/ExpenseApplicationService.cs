using System;
using System.Linq;
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
/// Manages staff expense claims, OCR receipt extraction, coordinator reviews, disbursement settlements, and payment ledgers.
/// </summary>
public sealed class ExpenseApplicationService(
    PaymentsDbContext dbContext,
    IPaymentsUnitOfWork unitOfWork,
    IWalletRowLockRepository walletLocks,
    IClock clock,
    IOptionsMonitor<GatewayAuthenticationOptions>? gatewayAuthentication = null)
{
    public async Task<IResult> ListExpenseClaimsAsync(HttpContext httpContext, string? status = null, string? subjectId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantExpenseClaims, httpContext, c => c.StudyId, c => c.SiteId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(c => c.SubjectId == subjectId);
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
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId) || request.GrossAmount <= 0m)
        {
            return Results.BadRequest(new { detail = "subject_id and positive gross_amount are required." });
        }

        if (!PaymentScopeAndMappingHelpers.Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return PaymentScopeAndMappingHelpers.OutsideScope();
        }

        DateTimeOffset now = clock.UtcNow;
        string? gatewaySecret = PaymentHmacTokenHelper.ResolveConfiguredGatewaySecret(gatewayAuthentication);
        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);

        return await walletLocks.ExecuteWalletUnitOfWorkAsync(async ct =>
        {
            var wallet = await walletLocks.LockWalletBySubjectAndStudyAsync(request.SubjectId, request.StudyId, ct);

            if (wallet != null && !PaymentScopeAndMappingHelpers.Reaches(httpContext, wallet))
            {
                return PaymentScopeAndMappingHelpers.OutsideScope();
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
                dbContext.ParticipantWallets.Add(wallet);
            }

            var policy = await ExpensePolicyEvaluator.EvaluateServerExpensePolicyAsync(
                dbContext,
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
                gatewaySecret: gatewaySecret,
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
                ReviewedBy = policy.CanAutoApprove ? PaymentScopeAndMappingHelpers.AutoApprovalSystemUser : null,
                ReviewedAt = policy.CanAutoApprove ? now : null,
                ReviewNotes = policy.ReviewNotes,
                CreatedAt = now,
                CreatedBy = userId,
                UpdatedAt = now,
                VersionIndex = 1
            };

            ExpensePolicyEvaluator.RecordServerReceiptVerificationAuditLogs(dbContext, claim, userId, rolesText, policy, now);

            if (policy.CanAutoApprove)
            {
                ExpensePolicyEvaluator.ApplyAutoApprovedExpenseDisbursement(
                    dbContext,
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
                dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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

            dbContext.ParticipantExpenseClaims.Add(claim);
            await unitOfWork.CommitAsync(ct);

            return Results.Ok(claim);
        });
    }

    public async Task<IResult> ExtractReceiptOcrAsync(OcrExtractRequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(ClinicalScopeGuard.ResolvePrincipal(httpContext)))
        {
            return PaymentScopeAndMappingHelpers.OutsideScope();
        }

        DateTimeOffset now = clock.UtcNow;
        string? gatewaySecret = PaymentHmacTokenHelper.ResolveConfiguredGatewaySecret(gatewayAuthentication);

        byte[] receiptBytes = PaymentHmacTokenHelper.DecodeReceiptImageBytes(request.ImageBase64);
        var extracted = ReceiptOcrEngine.ExtractDecodedOrFallback(
            receiptBytes,
            request.ImageBase64,
            request.RawText,
            request.CategoryHint);

        string? receiptSha256 = receiptBytes.Length > 0 ? HmacSigner.ComputeSha256Hash(receiptBytes) : null;
        string scanId = "OCR_SCAN_" + Guid.NewGuid().ToString("N");
        string? ocrToken = receiptSha256 is not null
            && string.IsNullOrEmpty(request.RawText)
            && extracted.OcrReadable
            && !string.IsNullOrWhiteSpace(gatewaySecret)
            ? PaymentHmacTokenHelper.IssueReceiptOcrToken(new PaymentHmacTokenHelper.ReceiptOcrTokenPayload(
                ScanId: scanId,
                CallerUserId: userId,
                ReceiptSha256: receiptSha256,
                OcrReadable: extracted.OcrReadable,
                MerchantName: extracted.MerchantName,
                ExpenseDate: extracted.ExpenseDate,
                GrossAmount: extracted.GrossAmount,
                Category: extracted.Category,
                ConfidenceScore: extracted.ConfidenceScore,
                IssuedAtUnix: now.ToUnixTimeSeconds(),
                ExpiresAtUnix: now.Add(PaymentHmacTokenHelper.OcrTokenLifetime).ToUnixTimeSeconds()), gatewaySecret)
            : null;

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantExpenseClaim",
            EntityId = scanId,
            Action = "OCR_RECEIPT_EXTRACTED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength),
            Details = JsonSerializer.Serialize(new
            {
                ocr_readable = extracted.OcrReadable,
                merchant_name = extracted.MerchantName,
                expense_date = extracted.ExpenseDate,
                gross_amount = extracted.GrossAmount,
                tax_deduction = extracted.TaxDeduction,
                net_amount = extracted.NetAmount,
                category = extracted.Category,
                confidence_score = extracted.ConfidenceScore,
                receipt_sha256 = receiptSha256,
                filename = request.Filename
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Receipt OCR scan performed",
            VersionIndex = 1
        });
        await unitOfWork.CommitAsync();

        return Results.Ok(new ReceiptOcrExtractResponseDto(
            OcrReadable: extracted.OcrReadable,
            MerchantName: extracted.MerchantName,
            ExpenseDate: extracted.ExpenseDate,
            GrossAmount: extracted.GrossAmount,
            TaxDeduction: extracted.TaxDeduction,
            NetAmount: extracted.NetAmount,
            Category: extracted.Category,
            ConfidenceScore: extracted.ConfidenceScore,
            RawText: extracted.RawText,
            Warning: extracted.Warning,
            OcrToken: ocrToken,
            ReceiptSha256: receiptSha256
        ));
    }

    public async Task<IResult> ReviewExpenseClaimAsync(string claimId, ReviewExpenseClaimRequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);

        return await walletLocks.ExecuteWalletUnitOfWorkAsync(async ct =>
        {
            var claim = await dbContext.ParticipantExpenseClaims
                .FirstOrDefaultAsync(c => c.Id == claimId, ct);

            if (claim == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, claim.StudyId, claim.SiteId))
            {
                return Results.NotFound(new { detail = $"Expense claim '{claimId}' not found." });
            }

            DateTimeOffset now = clock.UtcNow;

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
                var wallet = await walletLocks.LockWalletByIdAsync(claim.WalletId, ct)
                    ?? await walletLocks.LockWalletBySubjectAndStudyAsync(claim.SubjectId, claim.StudyId, ct);

                if (wallet != null)
                {
                    claim.RecordWalletCredit();
                    wallet.CurrentBalance += claim.NetAmount;
                    wallet.LifetimeReimbursements += claim.NetAmount;
                    wallet.UpdatedAt = now;
                    wallet.VersionIndex += 1;

                    dbContext.ParticipantTransactions.Add(new ParticipantTransaction
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

                dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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
                dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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

            await unitOfWork.CommitAsync(ct);

            return Results.Ok(claim);
        });
    }

    public async Task<IResult> ListTransactionsAsync(HttpContext httpContext, string? subjectId = null, string? walletId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var query = dbContext.ParticipantTransactions.AsQueryable();
        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            query = query.Where(t => walletIds.Contains(t.WalletId));
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(t => t.SubjectId == subjectId);
        }

        if (!string.IsNullOrWhiteSpace(walletId))
        {
            query = query.Where(t => t.WalletId == walletId);
        }

        var txs = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Results.Ok(txs);
    }

    public async Task<IResult> ListDisbursementsAsync(HttpContext httpContext, string? subjectId = null, string? walletId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var query = dbContext.ParticipantDisbursements.AsQueryable();
        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            query = query.Where(d => walletIds.Contains(d.WalletId));
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(d => d.SubjectId == subjectId);
        }

        if (!string.IsNullOrWhiteSpace(walletId))
        {
            query = query.Where(d => d.WalletId == walletId);
        }

        var disbs = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return Results.Ok(disbs);
    }

    public async Task<IResult> SettleDisbursementAsync(string id, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var disb = await dbContext.ParticipantDisbursements.FirstOrDefaultAsync(d => d.Id == id || d.ReferenceNumber == id);
        if (disb == null)
        {
            return Results.NotFound(new { detail = $"Disbursement '{id}' not found." });
        }

        var wallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.Id == disb.WalletId);
        if (wallet != null && !PaymentScopeAndMappingHelpers.Reaches(httpContext, wallet.StudyId, wallet.SiteId))
        {
            return PaymentScopeAndMappingHelpers.OutsideScope();
        }

        DateTimeOffset now = clock.UtcNow;
        try
        {
            disb.Settle(now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantDisbursement",
            EntityId = disb.Id,
            Action = "DIGITAL_DISBURSEMENT_SETTLED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength),
            Details = JsonSerializer.Serialize(new
            {
                disbursement_id = disb.Id,
                reference_number = disb.ReferenceNumber,
                status = disb.Status,
                amount = disb.Amount
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Disbursement confirmed and settled by payment rail",
            VersionIndex = disb.VersionIndex
        });
        await unitOfWork.CommitAsync();

        return Results.Ok(disb);
    }

    public async Task<IResult> GetAuditLogsAsync(HttpContext httpContext, string? subjectId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var query = dbContext.PaymentAuditLogs.AsQueryable();

        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            var claimIds = ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantExpenseClaims, httpContext, c => c.StudyId, c => c.SiteId).Select(c => c.Id);
            var invoiceIds = ClinicalScopeGuard.NarrowToScope(dbContext.SelfBillingInvoices, httpContext, i => i.StudyId, i => i.SiteId).Select(i => i.Id);
            var remittanceIds = ClinicalScopeGuard.NarrowToScope(dbContext.RemittanceAdvices, httpContext, r => r.StudyId, r => r.SiteId).Select(r => r.Id);
            var ruleIds = ClinicalScopeGuard.NarrowToStudyScope(dbContext.ParticipantStipendRules, httpContext, r => r.StudyId).Select(r => r.Id);
            query = query.Where(l =>
                walletIds.Contains(l.EntityId) || claimIds.Contains(l.EntityId) || invoiceIds.Contains(l.EntityId)
                || remittanceIds.Contains(l.EntityId) || ruleIds.Contains(l.EntityId));
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            query = query.Where(l => l.UserId == subjectId || (l.Details != null && l.Details.Contains(subjectId)));
        }

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Results.Ok(logs);
    }
}
