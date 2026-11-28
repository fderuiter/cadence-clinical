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
/// Evaluates server-verified ePRO diary synchronization tokens and credits protocol stipends for self-service participants.
/// </summary>
public sealed class DiaryStipendApplicationService(
    PaymentsDbContext dbContext,
    IPaymentsUnitOfWork unitOfWork,
    IWalletRowLockRepository walletLocks,
    IClock clock,
    IOptions<GatewayAuthenticationOptions> gatewayAuthentication)
{
    public async Task<IResult> CreditSelfDiaryStipendAsync(
        SelfServiceDiaryStipendRequestDto request,
        HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectId)
            && !string.Equals(request.SubjectId.Trim(), caller.UserId, StringComparison.Ordinal))
        {
            return PaymentScopeAndMappingHelpers.CrossParticipantAccessDenied();
        }

        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);
        string fallbackDiaryKey = $"diary_{caller.UserId}_{request.AssignmentId ?? request.DiaryId}_{request.SequenceNumber ?? 0}";
        string? suppliedKey = PaymentScopeAndMappingHelpers.NormalizeIdempotencyKey(
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
        string gatewaySecret = PaymentHmacTokenHelper.ResolveConfiguredGatewaySecret(gatewayAuthentication);
        string? resolvedWalletId = null;

        try
        {
            return await walletLocks.ExecuteWalletUnitOfWorkAsync(async ct =>
            {
                var (wallet, error) = await SelfWalletResolver.ResolveOwnWalletAsync(
                    dbContext,
                    walletLocks,
                    httpContext,
                    caller,
                    requestedSubjectId: request.SubjectId,
                    requestedWalletId: request.WalletId,
                    requestedStudyId: request.StudyCode,
                    requestedSiteId: request.SiteCode,
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
                    (wallet, error) = await SelfWalletResolver.ResolveRequiredLockedWalletAsync(
                        dbContext,
                        walletLocks,
                        httpContext,
                        caller,
                        request.SubjectId,
                        request.WalletId,
                        request.StudyCode,
                        request.SiteCode,
                        createIfMissing: true,
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
                    await unitOfWork.CommitAsync(ct);
                    return DiaryStipendCreditExecutor.BuildFlaggedResponse(wallet, caller.UserId, idempotencyKey, reason, w9Status);
                }

                var existingTx = await dbContext.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.WalletId == wallet.Id
                        && t.TransactionType == "STIPEND_CREDIT"
                        && (t.IdempotencyKey == idempotencyKey || t.ReferenceId == diaryReference), ct);

                if (existingTx != null)
                {
                    return DiaryStipendCreditExecutor.BuildAlreadyProcessedResponse(existingTx, wallet, caller.UserId, idempotencyKey);
                }

                string diaryCode = (request.DiaryId ?? request.AssignmentId ?? "EPRO_DIARY").Trim();
                string? candidateSyncToken = DiaryStipendCreditExecutor.ResolveRequestedDiarySyncToken(request);

                if (!PaymentHmacTokenHelper.TryVerifyDiarySyncVerificationToken(
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

                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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

                bool syncTokenAlreadyConsumed = await dbContext.PaymentAuditLogs.AsNoTracking()
                    .AnyAsync(l => l.Action == PaymentHmacTokenHelper.DiarySyncTokenConsumedAuditAction && l.EntityId == verifiedSyncToken.TokenId, ct)
                    || dbContext.ChangeTracker.Entries<PaymentAuditLog>()
                        .Any(e => e.Entity.Action == PaymentHmacTokenHelper.DiarySyncTokenConsumedAuditAction && e.Entity.EntityId == verifiedSyncToken.TokenId);

                if (syncTokenAlreadyConsumed)
                {
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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

                var activeStudyRules = await dbContext.ParticipantStipendRules.AsNoTracking()
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
                    DiaryStipendCreditExecutor.RecordConsumedDiarySyncTokenAuditLog(
                        dbContext, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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
                    DiaryStipendCreditExecutor.RecordConsumedDiarySyncTokenAuditLog(
                        dbContext, wallet, caller.UserId, rolesText, verifiedSyncToken.TokenId, idempotencyKey, diaryCode, now);
                    await unitOfWork.CommitAsync(ct);

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
                        Reason: $"No active self-service diary stipend rule configured for '{diaryCode}' in study '{wallet.StudyId}'."
                    ));
                }

                return await DiaryStipendCreditExecutor.ApplyVerifiedRuleAndCreditAsync(
                    dbContext,
                    unitOfWork,
                    wallet,
                    matchingRule,
                    caller.UserId,
                    rolesText,
                    verifiedSyncToken.TokenId,
                    idempotencyKey,
                    diaryReference,
                    diaryCode,
                    now,
                    ct);
            });
        }
        catch (DbUpdateException ex) when (walletLocks.IsUniqueViolation(ex) || !walletLocks.IsRelational)
        {
            walletLocks.ClearChangeTracker();
            var racedWallet = resolvedWalletId is not null
                ? await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == resolvedWalletId)
                : await dbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.SubjectId == caller.UserId);
            if (racedWallet != null)
            {
                var racedTx = await dbContext.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.WalletId == racedWallet.Id
                        && t.TransactionType == "STIPEND_CREDIT"
                        && (t.IdempotencyKey == idempotencyKey || t.ReferenceId == diaryReference));
                if (racedTx != null)
                {
                    return DiaryStipendCreditExecutor.BuildAlreadyProcessedResponse(racedTx, racedWallet, caller.UserId, idempotencyKey);
                }
            }
            throw;
        }
    }
}
