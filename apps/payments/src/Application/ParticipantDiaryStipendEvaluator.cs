using System;
using System.Collections.Generic;
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
/// Evaluates direct ePRO stipend disbursements and participant self-service diary stipend credits under row-level wallet locks.
/// </summary>
internal static partial class ParticipantDiaryStipendEvaluator
{
    public static async Task<IResult> DisburseStipendDirectAsync(
        PaymentsUnitOfWork uow,
        IClock clock,
        DisburseStipendRequest request,
        HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.BadRequest(new { detail = "subject_id is required for stipend disbursement." });
        }

        decimal stipendAmount = request.Amount ?? 25.00m;
        if (stipendAmount <= 0m)
        {
            return Results.BadRequest(new { detail = "Stipend disbursement amount must be greater than zero." });
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string idempotencyKey = ResolveDirectStipendIdempotencyKey(request);
        if (idempotencyKey.Length > 255)
        {
            return Results.BadRequest(new { detail = "idempotencyKey must not exceed 255 characters." });
        }

        try
        {
            return await uow.ExecuteWalletTransactionAsync(async ct =>
            {
                var subjectWallets = await uow.LockWalletsBySubjectAsync(request.SubjectId, ct);
                var wallet = !string.IsNullOrWhiteSpace(request.StudyId)
                    ? subjectWallets.FirstOrDefault(w => string.Equals(w.StudyId, request.StudyId, StringComparison.Ordinal))
                    : subjectWallets.FirstOrDefault();

                string walletStudyCode = wallet?.StudyId ?? request.StudyId ?? "STUDY-DCT-001";
                string walletSiteCode = wallet?.SiteId ?? request.SiteId ?? "SITE-101";
                if (!Reaches(httpContext, walletStudyCode, walletSiteCode))
                {
                    return OutsideScope();
                }

                if (wallet != null)
                {
                    var existingTx = await uow.Context.ParticipantTransactions.AsNoTracking()
                        .FirstOrDefaultAsync(t => t.WalletId == wallet.Id && t.IdempotencyKey == idempotencyKey, ct);

                    if (existingTx != null)
                    {
                        return BuildDuplicateDirectStipendResponse(existingTx, wallet, request.SubjectId, idempotencyKey);
                    }
                }

                var studyRules = await uow.Context.ParticipantStipendRules.AsNoTracking()
                    .Where(r => r.IsActive && r.StudyId == walletStudyCode)
                    .ToListAsync(ct);

                var (matchingRule, rejection) = EvaluateDirectStipendPolicy(stipendAmount, wallet, studyRules, request);
                if (rejection != null)
                {
                    return rejection;
                }

                DateTimeOffset now = clock.UtcNow;
                bool isNewWallet = wallet == null;

                if (wallet == null)
                {
                    wallet = new ParticipantWallet
                    {
                        Id = Guid.NewGuid().ToString(),
                        SubjectId = request.SubjectId,
                        StudyId = walletStudyCode,
                        SiteId = walletSiteCode,
                        CurrentBalance = 0m,
                        LifetimeStipends = 0m,
                        LifetimeReimbursements = 0m,
                        LifetimeDisbursements = 0m,
                        Currency = matchingRule?.Currency ?? "USD",
                        Status = "ACTIVE",
                        W9Status = "NOT_REQUIRED",
                        CreatedAt = now,
                        CreatedBy = userId,
                        UpdatedAt = now,
                        VersionIndex = 1
                    };
                    uow.Context.ParticipantWallets.Add(wallet);
                }

                wallet.CurrentBalance += stipendAmount;
                wallet.LifetimeStipends += stipendAmount;
                wallet.TaxableYtd += stipendAmount;
                if (wallet.TaxableYtd >= 600.00m)
                {
                    wallet.Is1099ThresholdExceeded = true;
                }

                wallet.UpdatedAt = now;
                if (!isNewWallet)
                {
                    wallet.VersionIndex += 1;
                }

                string txType = matchingRule is { IsMilestone: true } ? "MILESTONE_CREDIT" : "STIPEND_CREDIT";
                var tx = new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = request.SubjectId,
                    StudyId = wallet.StudyId,
                    TransactionType = txType,
                    Amount = stipendAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = idempotencyKey,
                    IdempotencyKey = idempotencyKey,
                    Description = !string.IsNullOrWhiteSpace(request.DiaryId)
                        ? $"Direct ePRO stipend credit ({request.DiaryId.Trim()})"
                        : "Direct participant stipend credit",
                    CreatedAt = now,
                    CreatedBy = userId,
                    VersionIndex = 1
                };
                uow.Context.ParticipantTransactions.Add(tx);

                uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantWallet",
                    EntityId = wallet.Id,
                    Action = "AUTO_STIPEND_DISBURSEMENT",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        subject_id = request.SubjectId,
                        sequence_number = request.SequenceNumber,
                        assignment_id = request.AssignmentId,
                        diary_id = request.DiaryId,
                        stipend_amount = stipendAmount,
                        taxable_ytd = wallet.TaxableYtd,
                        idempotency_key = idempotencyKey,
                        transaction_id = tx.Id,
                        new_balance = wallet.CurrentBalance,
                        timestamp = now
                    }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = "Automatic ePRO survey completion stipend payout",
                    VersionIndex = wallet.VersionIndex
                });

                await uow.CommitAsync(ct);

                return Results.Ok(new DisburseStipendResponse(
                    Success: true,
                    Id: tx.Id,
                    WalletId: wallet.Id,
                    SubjectId: request.SubjectId,
                    Amount: stipendAmount,
                    NewBalance: wallet.CurrentBalance,
                    Currency: wallet.Currency,
                    Status: "COMPLETED",
                    ReferenceNumber: $"DISB-{tx.Id[..Math.Min(8, tx.Id.Length)]}",
                    IdempotencyKey: idempotencyKey,
                    DispatchedAt: now));
            });
        }
        catch (DbUpdateException ex) when (PaymentsUnitOfWork.IsUniqueViolation(ex, "payments_participant_transactions"))
        {
            uow.Context.ChangeTracker.Clear();
            var racedWallets = await uow.Context.ParticipantWallets.AsNoTracking()
                .Where(w => w.SubjectId == request.SubjectId)
                .ToListAsync();
            var racedWallet = !string.IsNullOrWhiteSpace(request.StudyId)
                ? racedWallets.FirstOrDefault(w => string.Equals(w.StudyId, request.StudyId, StringComparison.Ordinal))
                : racedWallets.FirstOrDefault();
            if (racedWallet != null)
            {
                if (!Reaches(httpContext, racedWallet))
                {
                    return OutsideScope();
                }

                var racedTx = await uow.Context.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.WalletId == racedWallet.Id && t.IdempotencyKey == idempotencyKey);
                if (racedTx != null)
                {
                    return BuildDuplicateDirectStipendResponse(racedTx, racedWallet, request.SubjectId, idempotencyKey);
                }
            }

            throw;
        }
    }

    private static string ResolveDirectStipendIdempotencyKey(DisburseStipendRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return request.IdempotencyKey.Trim();
        }

        return request.SequenceNumber.HasValue
            ? $"seq_{request.SequenceNumber}_{request.SubjectId}"
            : $"disburse_{request.SubjectId}_{Guid.NewGuid()}";
    }

    private static (ParticipantStipendRule? MatchingRule, IResult? RejectionResult) EvaluateDirectStipendPolicy(
        decimal stipendAmount,
        ParticipantWallet? wallet,
        IReadOnlyList<ParticipantStipendRule> studyRules,
        DisburseStipendRequest request)
    {
        string? diaryCode = !string.IsNullOrWhiteSpace(request.DiaryId)
            ? request.DiaryId.Trim()
            : request.AssignmentId?.Trim();

        ParticipantStipendRule? matchingRule = null;
        if (!string.IsNullOrEmpty(diaryCode))
        {
            matchingRule = studyRules.FirstOrDefault(r =>
                string.Equals(r.VisitCode, diaryCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.ProcedureCode, diaryCode, StringComparison.OrdinalIgnoreCase));
        }

        matchingRule ??= studyRules.FirstOrDefault(r =>
            string.Equals(r.VisitCode, "DIARY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.VisitCode, "EPRO_DIARY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.VisitCode, "DIARY_SYNC", StringComparison.OrdinalIgnoreCase));

        decimal? ruleCap = matchingRule?.AntiCoercionCap;
        if (!ruleCap.HasValue && studyRules.Any(r => r.AntiCoercionCap.HasValue))
        {
            ruleCap = studyRules.Where(r => r.AntiCoercionCap.HasValue).Max(r => r.AntiCoercionCap!.Value);
        }

        decimal? walletCap = wallet?.AntiCoercionCap;
        if ((ruleCap.HasValue && stipendAmount > ruleCap.Value)
            || (walletCap.HasValue && stipendAmount > walletCap.Value))
        {
            decimal effectiveCap = Math.Min(ruleCap ?? decimal.MaxValue, walletCap ?? decimal.MaxValue);
            return (matchingRule, Results.UnprocessableEntity(new
            {
                detail = $"Stipend amount (${stipendAmount:F2}) exceeds IRB anti-coercion cap limit (${effectiveCap:F2})."
            }));
        }

        decimal? ruleThreshold = matchingRule?.AutoDisbursementThreshold;
        if (!ruleThreshold.HasValue && studyRules.Any(r => r.AutoDisbursementThreshold.HasValue))
        {
            ruleThreshold = studyRules.Where(r => r.AutoDisbursementThreshold.HasValue).Max(r => r.AutoDisbursementThreshold!.Value);
        }

        decimal? walletThreshold = wallet?.AutoDisbursementThreshold;
        if ((ruleThreshold.HasValue && stipendAmount > ruleThreshold.Value)
            || (walletThreshold.HasValue && stipendAmount > walletThreshold.Value))
        {
            decimal effectiveThreshold = Math.Min(ruleThreshold ?? decimal.MaxValue, walletThreshold ?? decimal.MaxValue);
            return (matchingRule, Results.Conflict(new
            {
                detail = $"Stipend amount (${stipendAmount:F2}) exceeds configured auto-disbursement threshold limit (${effectiveThreshold:F2})."
            }));
        }

        decimal potentialTaxableYtd = (wallet?.TaxableYtd ?? 0m) + stipendAmount;
        string w9Status = wallet?.W9Status ?? "NOT_REQUIRED";
        if (potentialTaxableYtd >= 600.00m && !string.Equals(w9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
        {
            return (matchingRule, Results.Conflict(new
            {
                detail = $"IRS $600.00 YTD tax threshold reached (YTD: ${potentialTaxableYtd:F2}) without a verified Form W-9. Direct disbursement halted until Form W-9 is verified."
            }));
        }

        return (matchingRule, null);
    }

    private static IResult BuildDuplicateDirectStipendResponse(
        ParticipantTransaction existingTx,
        ParticipantWallet wallet,
        string subjectCode,
        string idempotencyKey) =>
        Results.Ok(new DisburseStipendResponse(
            Success: true,
            Id: existingTx.Id,
            WalletId: wallet.Id,
            SubjectId: subjectCode,
            Amount: existingTx.Amount,
            NewBalance: wallet.CurrentBalance,
            Currency: wallet.Currency,
            Status: "DUPLICATE_SKIPPED",
            ReferenceNumber: $"DISB-{existingTx.Id[..Math.Min(8, existingTx.Id.Length)]}",
            IdempotencyKey: idempotencyKey,
            DispatchedAt: existingTx.CreatedAt));
}
