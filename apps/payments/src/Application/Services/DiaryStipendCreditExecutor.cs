using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Payments.Application.Services;

/// <summary>
/// Evaluates cap/threshold/1099 policy gates and records ledger transactions and audit logs for verified ePRO diary stipends.
/// </summary>
internal static class DiaryStipendCreditExecutor
{
    public static async Task<IResult> ApplyVerifiedRuleAndCreditAsync(
        PaymentsDbContext dbContext,
        IPaymentsUnitOfWork unitOfWork,
        ParticipantWallet wallet,
        ParticipantStipendRule matchingRule,
        string callerUserId,
        string rolesText,
        string verifiedTokenId,
        string idempotencyKey,
        string diaryReference,
        string diaryCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        decimal stipendAmount = matchingRule.Amount;
        RecordConsumedDiarySyncTokenAuditLog(dbContext, wallet, callerUserId, rolesText, verifiedTokenId, idempotencyKey, diaryCode, now);

        bool ruleCapExceeded = matchingRule.AntiCoercionCap.HasValue && stipendAmount > matchingRule.AntiCoercionCap.Value;
        bool walletCapExceeded = wallet.AntiCoercionCap.HasValue && stipendAmount > wallet.AntiCoercionCap.Value;
        if (ruleCapExceeded || walletCapExceeded)
        {
            decimal effectiveCap = Math.Min(
                matchingRule.AntiCoercionCap ?? decimal.MaxValue,
                wallet.AntiCoercionCap ?? decimal.MaxValue);

            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "ANTI_COERCION_CAP_EXCEEDED",
                UserId = callerUserId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new
                {
                    idempotency_key = idempotencyKey,
                    diary_id = diaryCode,
                    amount = stipendAmount,
                    anti_coercion_cap = effectiveCap
                }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Diary stipend exceeds IRB anti-coercion cap; routed to review",
                VersionIndex = wallet.VersionIndex
            });
            await unitOfWork.CommitAsync(ct);
            return BuildFlaggedResponse(
                wallet,
                callerUserId,
                idempotencyKey,
                $"Protocol stipend (${stipendAmount:F2}) exceeds IRB anti-coercion cap (${effectiveCap:F2}); routed to coordinator review.");
        }

        bool ruleThresholdExceeded = matchingRule.AutoDisbursementThreshold.HasValue && stipendAmount > matchingRule.AutoDisbursementThreshold.Value;
        bool walletThresholdExceeded = wallet.AutoDisbursementThreshold.HasValue && stipendAmount > wallet.AutoDisbursementThreshold.Value;
        if (ruleThresholdExceeded || walletThresholdExceeded)
        {
            decimal effectiveThreshold = Math.Min(
                matchingRule.AutoDisbursementThreshold ?? decimal.MaxValue,
                wallet.AutoDisbursementThreshold ?? decimal.MaxValue);

            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "THRESHOLD_EXCEEDED_REVIEW_REQUIRED",
                UserId = callerUserId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new
                {
                    idempotency_key = idempotencyKey,
                    diary_id = diaryCode,
                    amount = stipendAmount,
                    auto_disbursement_threshold = effectiveThreshold
                }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Stipend amount exceeds auto-disbursement safety threshold",
                VersionIndex = wallet.VersionIndex
            });
            await unitOfWork.CommitAsync(ct);
            return BuildFlaggedResponse(
                wallet,
                callerUserId,
                idempotencyKey,
                $"Payment of ${stipendAmount:F2} exceeds configured auto-disbursement threshold limit (${effectiveThreshold:F2}). Routed to administrator review queue.");
        }

        decimal projectedTaxable = matchingRule.Taxable ? wallet.TaxableYtd + stipendAmount : wallet.TaxableYtd;
        if (projectedTaxable >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
        {
            wallet.Is1099ThresholdExceeded = true;
            if (wallet.W9Status == "NOT_REQUIRED")
            {
                wallet.W9Status = "REQUIRED";
            }
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "IRS_1099_THRESHOLD_EXCEEDED",
                UserId = callerUserId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new
                {
                    idempotency_key = idempotencyKey,
                    diary_id = diaryCode,
                    amount = stipendAmount,
                    taxable_ytd = projectedTaxable,
                    w9_status = wallet.W9Status
                }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Diary stipend halted pending Form W-9 verification",
                VersionIndex = wallet.VersionIndex
            });
            await unitOfWork.CommitAsync(ct);
            return BuildFlaggedResponse(
                wallet,
                callerUserId,
                idempotencyKey,
                $"Annual taxable payments reach ${projectedTaxable:F2} without a verified Form W-9. Stipend credit halted for review.",
                wallet.W9Status);
        }

        wallet.CurrentBalance += stipendAmount;
        wallet.LifetimeStipends += stipendAmount;
        if (matchingRule.Taxable)
        {
            wallet.TaxableYtd += stipendAmount;
            if (wallet.TaxableYtd >= 600.00m)
            {
                wallet.Is1099ThresholdExceeded = true;
            }
        }
        wallet.UpdatedAt = now;
        wallet.VersionIndex += 1;

        var tx = new ParticipantTransaction
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = callerUserId,
            StudyId = wallet.StudyId,
            TransactionType = "STIPEND_CREDIT",
            Amount = stipendAmount,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceId = diaryReference,
            IdempotencyKey = idempotencyKey,
            Description = $"Protocol diary synchronization stipend credit ({matchingRule.VisitName})",
            CreatedAt = now,
            CreatedBy = callerUserId,
            VersionIndex = 1
        };
        dbContext.ParticipantTransactions.Add(tx);

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "PARTICIPANT_STIPEND_CREDITED",
            UserId = callerUserId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new
            {
                idempotency_key = idempotencyKey,
                diary_id = diaryCode,
                rule_id = matchingRule.Id,
                stipend_amount = stipendAmount,
                new_balance = wallet.CurrentBalance,
                transaction_id = tx.Id
            }),
            CreatedAt = now,
            CreatedBy = callerUserId,
            ReasonForChange = $"Server-rule protocol stipend credited for synchronized diary '{diaryCode}'",
            VersionIndex = wallet.VersionIndex
        });

        await unitOfWork.CommitAsync(ct);

        return Results.Ok(new SelfServiceDiaryStipendResponseDto(
            Success: true,
            Status: "CREDITED",
            CreditedAmount: stipendAmount,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            WalletId: wallet.Id,
            SubjectId: callerUserId,
            Currency: wallet.Currency,
            TransactionId: tx.Id,
            ReferenceNumber: tx.Id,
            IdempotencyKey: idempotencyKey
        ));
    }

    public static void RecordConsumedDiarySyncTokenAuditLog(
        PaymentsDbContext dbContext,
        ParticipantWallet wallet,
        string callerUserId,
        string callerRolesText,
        string tokenId,
        string idempotencyKey,
        string diaryCode,
        DateTimeOffset now)
    {
        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = tokenId,
            Action = PaymentHmacTokenHelper.DiarySyncTokenConsumedAuditAction,
            UserId = callerUserId,
            UserRoles = callerRolesText,
            Details = JsonSerializer.Serialize(new
            {
                wallet_id = wallet.Id,
                token_id = tokenId,
                idempotency_key = idempotencyKey,
                diary_id = diaryCode
            }),
            CreatedAt = now,
            CreatedBy = callerUserId,
            ReasonForChange = "Single-use ePRO diary sync verification token bound to stipend evaluation",
            VersionIndex = 1
        });
    }

    public static string? ResolveRequestedDiarySyncToken(SelfServiceDiaryStipendRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.SyncVerificationToken))
        {
            return request.SyncVerificationToken.Trim();
        }

        if (request.AdditionalFields is { Count: > 0 } extra
            && extra.TryGetValue("sync_verification_token", out var snakeToken)
            && snakeToken.ValueKind == JsonValueKind.String)
        {
            string? raw = snakeToken.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
        }

        return null;
    }

    public static IResult BuildFlaggedResponse(
        ParticipantWallet wallet,
        string subjectId,
        string idempotencyKey,
        string reason,
        string? w9Status = null) =>
        Results.Ok(new SelfServiceDiaryStipendResponseDto(
            Success: false,
            Status: "FLAGGED_FOR_REVIEW",
            CreditedAmount: 0m,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            Currency: wallet.Currency,
            TransactionId: null,
            ReferenceNumber: null,
            IdempotencyKey: idempotencyKey,
            Reason: reason,
            RequiresReview: true,
            W9Status: w9Status
        ));

    public static IResult BuildAlreadyProcessedResponse(
        ParticipantTransaction existingTx,
        ParticipantWallet wallet,
        string subjectId,
        string idempotencyKey) =>
        Results.Ok(new SelfServiceDiaryStipendResponseDto(
            Success: true,
            Status: "ALREADY_PROCESSED",
            CreditedAmount: 0m,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            Currency: wallet.Currency,
            TransactionId: existingTx.Id,
            ReferenceNumber: existingTx.ReferenceId,
            IdempotencyKey: idempotencyKey,
            Reason: "Diary synchronization stipend was already credited for this submission."
        ));
}
