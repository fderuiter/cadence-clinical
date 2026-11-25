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

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

internal static partial class ParticipantDiaryStipendEvaluator
{
    public static async Task<IResult> CreditSelfDiaryStipendAsync(
        PaymentsUnitOfWork uow,
        IClock clock,
        IOptionsMonitor<GatewayAuthenticationOptions>? gatewayAuthentication,
        SelfServiceDiaryStipendRequestDto request,
        HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectId)
            && !string.Equals(request.SubjectId.Trim(), caller.UserId, StringComparison.Ordinal))
        {
            return CrossParticipantAccessDenied();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string fallbackDiaryKey = $"diary_{caller.UserId}_{request.AssignmentId ?? request.DiaryId}_{request.SequenceNumber ?? 0}";
        string? suppliedKey = PaymentTokenService.NormalizeIdempotencyKey(
            httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault(),
            request.IdempotencyKey);
        string idempotencyKey = suppliedKey ?? fallbackDiaryKey;
        string diaryReference = (!string.IsNullOrWhiteSpace(request.AssignmentId) || request.SequenceNumber.HasValue)
            ? fallbackDiaryKey
            : idempotencyKey;
        if (idempotencyKey.Length > 255 || diaryReference.Length > 255)
        {
            return Results.BadRequest(new { detail = "Idempotency-Key must not exceed 255 characters." });
        }

        DateTimeOffset now = clock.UtcNow;
        string? gatewaySecret = PaymentTokenService.ResolveConfiguredGatewaySecret(gatewayAuthentication);
        string? resolvedWalletId = null;

        try
        {
            return await uow.ExecuteWalletTransactionAsync(async ct =>
            {
                var (wallet, error) = await ResolveOwnWalletAsync(
                    uow,
                    httpContext,
                    caller,
                    requestedSubjectCode: request.SubjectId,
                    requestedWalletCode: request.WalletId,
                    requestedStudyCode: request.StudyCode,
                    requestedSiteCode: request.SiteCode,
                    createIfMissing: false,
                    now: now,
                    lockForUpdate: true,
                    ct: ct);

                if (error != null)
                {
                    return error;
                }

                if (request.AdditionalFields is { Count: > 0 } extra
                    && extra.Keys.Any(k =>
                        k.Equals("amount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("grossAmount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("gross_amount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("stipendAmount", StringComparison.OrdinalIgnoreCase)
                        || k.Equals("stipend_amount", StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest(new
                    {
                        detail = "Client-supplied stipend amounts are prohibited; stipend credits are computed exclusively from server protocol rules."
                    });
                }

                if (string.IsNullOrWhiteSpace(request.DiaryId) && string.IsNullOrWhiteSpace(request.AssignmentId))
                {
                    return Results.BadRequest(new { detail = "Either diaryId or assignmentId is required to evaluate diary stipend rules." });
                }

                if (wallet == null)
                {
                    (wallet, error) = await LockOrProvisionOwnWalletAsync(
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
                }

                resolvedWalletId = wallet!.Id;

                async Task<IResult> SaveFlaggedAsync(string reason, string? w9Status = null)
                {
                    await uow.CommitAsync(ct);
                    return BuildFlaggedDiaryStipendResponse(wallet, caller.UserId, idempotencyKey, reason, w9Status);
                }

                var existingTx = await uow.Context.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.WalletId == wallet.Id
                        && t.TransactionType == "STIPEND_CREDIT"
                        && (t.IdempotencyKey == idempotencyKey || t.ReferenceId == diaryReference), ct);

                if (existingTx != null)
                {
                    return BuildAlreadyProcessedDiaryStipendResponse(existingTx, wallet, caller.UserId, idempotencyKey);
                }

                string diaryCode = (request.DiaryId ?? request.AssignmentId ?? "EPRO_DIARY").Trim();
                string? candidateSyncToken = ResolveRequestedDiarySyncToken(request);

                if (!PaymentTokenService.TryVerifyDiarySyncVerificationToken(
                        candidateSyncToken,
                        gatewaySecret,
                        caller.UserId,
                        wallet.StudyId,
                        request.DiaryId,
                        request.AssignmentId,
                        request.SequenceNumber,
                        now,
                        out var verifiedSyncToken)
                    || verifiedSyncToken is null)
                {
                    string invalidReason = string.IsNullOrWhiteSpace(candidateSyncToken)
                        ? "Server-verified ePRO diary synchronization token is required before crediting protocol stipends; routed to coordinator review."
                        : "ePRO diary synchronization token could not be verified for this participant, study, diary, or sequence number; routed to coordinator review.";

                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "DIARY_SYNC_VERIFICATION_FAILED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            assignment_id = request.AssignmentId,
                            sequence_number = request.SequenceNumber,
                            token_supplied = !string.IsNullOrWhiteSpace(candidateSyncToken)
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Unverified self-service diary stipend request held for review",
                        VersionIndex = wallet.VersionIndex
                    });
                    return await SaveFlaggedAsync(invalidReason);
                }

                bool syncTokenAlreadyConsumed = await uow.Context.PaymentAuditLogs.AsNoTracking()
                    .AnyAsync(l => l.Action == PaymentTokenService.DiarySyncTokenConsumedAuditAction && l.EntityId == verifiedSyncToken.TokenId, ct)
                    || uow.Context.ChangeTracker.Entries<PaymentAuditLog>()
                        .Any(e => e.Entity.Action == PaymentTokenService.DiarySyncTokenConsumedAuditAction && e.Entity.EntityId == verifiedSyncToken.TokenId);

                if (syncTokenAlreadyConsumed)
                {
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "DIARY_SYNC_TOKEN_REPLAYED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            token_id = verifiedSyncToken.TokenId
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Replayed single-use diary sync verification token held for review",
                        VersionIndex = wallet.VersionIndex
                    });
                    return await SaveFlaggedAsync(
                        "ePRO diary synchronization token was already consumed by a prior stipend evaluation; routed to coordinator review.");
                }

                var activeStudyRules = await uow.Context.ParticipantStipendRules.AsNoTracking()
                    .Where(r => r.IsActive && r.StudyId == wallet.StudyId)
                    .ToListAsync(ct);

                var eligibleStudyRules = activeStudyRules
                    .Where(r => !r.RequiresVerification && !r.IsMilestone)
                    .ToList();

                var explicitEligibleRule = eligibleStudyRules.FirstOrDefault(r =>
                    string.Equals(r.VisitCode, diaryCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.ProcedureCode, diaryCode, StringComparison.OrdinalIgnoreCase));

                var explicitRestrictedRule = explicitEligibleRule == null
                    ? activeStudyRules.FirstOrDefault(r =>
                        (r.RequiresVerification || r.IsMilestone)
                        && (string.Equals(r.VisitCode, diaryCode, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(r.ProcedureCode, diaryCode, StringComparison.OrdinalIgnoreCase)))
                    : null;

                if (explicitRestrictedRule != null)
                {
                    RecordConsumedDiarySyncTokenAuditLog(uow, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "STIPEND_VERIFICATION_REQUIRED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            idempotency_key = idempotencyKey,
                            diary_id = diaryCode,
                            rule_id = explicitRestrictedRule.Id,
                            requires_verification = explicitRestrictedRule.RequiresVerification,
                            is_milestone = explicitRestrictedRule.IsMilestone
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Clinician-verified or milestone stipend rule cannot be auto-credited via participant self-service",
                        VersionIndex = wallet.VersionIndex
                    });
                    return await SaveFlaggedAsync(
                        $"Protocol rule '{explicitRestrictedRule.VisitCode}' requires clinician verification or milestone sign-off and cannot be auto-credited via self-service.");
                }

                var matchingRule = explicitEligibleRule
                    ?? eligibleStudyRules.FirstOrDefault(r =>
                        string.Equals(r.VisitCode, "DIARY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.VisitCode, "EPRO_DIARY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.VisitCode, "DIARY_SYNC", StringComparison.OrdinalIgnoreCase));

                if (matchingRule == null)
                {
                    RecordConsumedDiarySyncTokenAuditLog(uow, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);
                    await uow.CommitAsync(ct);

                    return Results.Ok(new SelfServiceDiaryStipendResponseDto(
                        Success: true,
                        Status: "NO_APPLICABLE_RULE",
                        CreditedAmount: 0m,
                        CurrentBalance: wallet.CurrentBalance,
                        LifetimeStipends: wallet.LifetimeStipends,
                        WalletId: wallet.Id,
                        SubjectId: caller.UserId,
                        Currency: wallet.Currency,
                        TransactionId: null,
                        ReferenceNumber: null,
                        IdempotencyKey: idempotencyKey,
                        Reason: $"No active self-service diary stipend rule configured for '{diaryCode}' in study '{wallet.StudyId}'."));
                }

                decimal stipendAmount = matchingRule.Amount;
                RecordConsumedDiarySyncTokenAuditLog(uow, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);

                bool ruleCapExceeded = matchingRule.AntiCoercionCap.HasValue && stipendAmount > matchingRule.AntiCoercionCap.Value;
                bool walletCapExceeded = wallet.AntiCoercionCap.HasValue && stipendAmount > wallet.AntiCoercionCap.Value;
                if (ruleCapExceeded || walletCapExceeded)
                {
                    decimal effectiveCap = Math.Min(matchingRule.AntiCoercionCap ?? decimal.MaxValue, wallet.AntiCoercionCap ?? decimal.MaxValue);
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "ANTI_COERCION_CAP_EXCEEDED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { idempotency_key = idempotencyKey, diary_id = diaryCode, amount = stipendAmount, anti_coercion_cap = effectiveCap }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Diary stipend exceeds IRB anti-coercion cap; routed to review",
                        VersionIndex = wallet.VersionIndex
                    });
                    return await SaveFlaggedAsync(
                        $"Protocol stipend (${stipendAmount:F2}) exceeds IRB anti-coercion cap (${effectiveCap:F2}); routed to coordinator review.");
                }

                bool ruleThresholdExceeded = matchingRule.AutoDisbursementThreshold.HasValue && stipendAmount > matchingRule.AutoDisbursementThreshold.Value;
                bool walletThresholdExceeded = wallet.AutoDisbursementThreshold.HasValue && stipendAmount > wallet.AutoDisbursementThreshold.Value;
                if (ruleThresholdExceeded || walletThresholdExceeded)
                {
                    decimal effectiveThreshold = Math.Min(matchingRule.AutoDisbursementThreshold ?? decimal.MaxValue, wallet.AutoDisbursementThreshold ?? decimal.MaxValue);
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "THRESHOLD_EXCEEDED_REVIEW_REQUIRED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { idempotency_key = idempotencyKey, diary_id = diaryCode, amount = stipendAmount, auto_disbursement_threshold = effectiveThreshold }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Stipend amount exceeds auto-disbursement safety threshold",
                        VersionIndex = wallet.VersionIndex
                    });
                    return await SaveFlaggedAsync(
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

                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "IRS_1099_THRESHOLD_EXCEEDED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { idempotency_key = idempotencyKey, diary_id = diaryCode, amount = stipendAmount, taxable_ytd = projectedTaxable, w9_status = wallet.W9Status }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Diary stipend halted pending Form W-9 verification",
                        VersionIndex = wallet.VersionIndex
                    });
                    return await SaveFlaggedAsync(
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
                    SubjectId = caller.UserId,
                    StudyId = wallet.StudyId,
                    TransactionType = "STIPEND_CREDIT",
                    Amount = stipendAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = diaryReference,
                    IdempotencyKey = idempotencyKey,
                    Description = $"Protocol diary synchronization stipend credit ({matchingRule.VisitName})",
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    VersionIndex = 1
                };
                uow.Context.ParticipantTransactions.Add(tx);

                uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantWallet",
                    EntityId = wallet.Id,
                    Action = "PARTICIPANT_STIPEND_CREDITED",
                    UserId = caller.UserId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new { idempotency_key = idempotencyKey, diary_id = diaryCode, rule_id = matchingRule.Id, stipend_amount = stipendAmount, new_balance = wallet.CurrentBalance, transaction_id = tx.Id }),
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    ReasonForChange = $"Server-rule protocol stipend credited for synchronized diary '{diaryCode}'",
                    VersionIndex = wallet.VersionIndex
                });

                await uow.CommitAsync(ct);

                return Results.Ok(new SelfServiceDiaryStipendResponseDto(
                    Success: true,
                    Status: "CREDITED",
                    CreditedAmount: stipendAmount,
                    CurrentBalance: wallet.CurrentBalance,
                    LifetimeStipends: wallet.LifetimeStipends,
                    WalletId: wallet.Id,
                    SubjectId: caller.UserId,
                    Currency: wallet.Currency,
                    TransactionId: tx.Id,
                    ReferenceNumber: tx.Id,
                    IdempotencyKey: idempotencyKey));
            });
        }
        catch (DbUpdateException ex) when (PaymentsUnitOfWork.IsUniqueViolation(ex) || !uow.Context.Database.IsRelational())
        {
            uow.Context.ChangeTracker.Clear();
            var racedWallet = resolvedWalletId is not null
                ? await uow.Context.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == resolvedWalletId)
                : await uow.Context.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.SubjectId == caller.UserId);
            if (racedWallet != null)
            {
                var racedTx = await uow.Context.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.WalletId == racedWallet.Id
                        && t.TransactionType == "STIPEND_CREDIT"
                        && (t.IdempotencyKey == idempotencyKey || t.ReferenceId == diaryReference));
                if (racedTx != null)
                {
                    return BuildAlreadyProcessedDiaryStipendResponse(racedTx, racedWallet, caller.UserId, idempotencyKey);
                }
            }

            throw;
        }
    }

    private static string? ResolveRequestedDiarySyncToken(SelfServiceDiaryStipendRequestDto request)
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

    private static void RecordConsumedDiarySyncTokenAuditLog(
        PaymentsUnitOfWork uow,
        ParticipantWallet wallet,
        string callerUserId,
        string callerRolesText,
        string tokenId,
        string idempotencyKey,
        string diaryCode,
        DateTimeOffset now)
    {
        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = tokenId,
            Action = PaymentTokenService.DiarySyncTokenConsumedAuditAction,
            UserId = callerUserId,
            UserRoles = callerRolesText,
            Details = JsonSerializer.Serialize(new { wallet_id = wallet.Id, token_id = tokenId, idempotency_key = idempotencyKey, diary_id = diaryCode }),
            CreatedAt = now,
            CreatedBy = callerUserId,
            ReasonForChange = "Single-use ePRO diary sync verification token bound to stipend evaluation",
            VersionIndex = 1
        });
    }

    private static IResult BuildFlaggedDiaryStipendResponse(
        ParticipantWallet wallet,
        string subjectCode,
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
            SubjectId: subjectCode,
            Currency: wallet.Currency,
            TransactionId: null,
            ReferenceNumber: null,
            IdempotencyKey: idempotencyKey,
            Reason: reason,
            RequiresReview: true,
            W9Status: w9Status));

    private static IResult BuildAlreadyProcessedDiaryStipendResponse(
        ParticipantTransaction existingTx,
        ParticipantWallet wallet,
        string subjectCode,
        string idempotencyKey) =>
        Results.Ok(new SelfServiceDiaryStipendResponseDto(
            Success: true,
            Status: "ALREADY_PROCESSED",
            CreditedAmount: 0m,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            WalletId: wallet.Id,
            SubjectId: subjectCode,
            Currency: wallet.Currency,
            TransactionId: existingTx.Id,
            ReferenceNumber: existingTx.ReferenceId,
            IdempotencyKey: idempotencyKey,
            Reason: "Diary synchronization stipend was already credited for this submission."));
}
