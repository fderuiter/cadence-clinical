using System;
using System.Collections.Generic;
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

/// <summary>
/// Application service owning Schedule-of-Activities stipend rules, verified eCRF visit stipend ingestion,
/// direct ePRO stipend disbursement, and participant self-service diary stipend crediting.
/// </summary>
public sealed class ParticipantStipendApplicationService(
    PaymentsUnitOfWork uow,
    IClock clock,
    IOptionsMonitor<GatewayAuthenticationOptions>? gatewayAuthentication = null)
{
    public async Task<IResult> ListStipendRulesAsync(HttpContext httpContext, string? studyCode = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToStudyScope(uow.Context.ParticipantStipendRules.AsNoTracking(), httpContext, r => r.StudyId);
        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            query = query.Where(r => r.StudyId == studyCode);
        }

        var rules = await query.Where(r => r.IsActive).ToListAsync();
        return Results.Ok(rules);
    }

    public async Task<IResult> CreateStipendRuleAsync(ParticipantStipendRuleCreateDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId))
        {
            return OutsideScope();
        }

        if (request.AntiCoercionCap.HasValue && request.Amount > request.AntiCoercionCap.Value)
        {
            return Results.UnprocessableEntity(new { detail = $"Stipend amount (${request.Amount:F2}) cannot exceed IRB anti-coercion cap limit (${request.AntiCoercionCap.Value:F2})." });
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        DateTimeOffset now = clock.UtcNow;
        var existing = await uow.Context.ParticipantStipendRules
            .FirstOrDefaultAsync(r => r.IsActive && r.StudyId == request.StudyId && r.VisitCode == request.VisitCode);

        if (existing != null)
        {
            existing.VisitName = request.VisitName;
            existing.ProcedureCode = request.ProcedureCode;
            existing.Amount = request.Amount;
            existing.Currency = request.Currency ?? "USD";
            existing.RequiresVerification = request.RequiresVerification;
            existing.Taxable = request.Taxable;
            existing.AntiCoercionCap = request.AntiCoercionCap;
            existing.AutoDisbursementThreshold = request.AutoDisbursementThreshold;
            existing.IsMilestone = request.IsMilestone;
            existing.MilestoneCode = request.MilestoneCode;
            existing.UpdatedAt = now;
            existing.VersionIndex += 1;

            uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantStipendRule",
                EntityId = existing.Id,
                Action = "PARTICIPANT_STIPEND_RULE_CREATED",
                UserId = userId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new { study_id = request.StudyId, visit_code = request.VisitCode, amount = request.Amount }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "SoA visit stipend compensation rule updated",
                VersionIndex = existing.VersionIndex
            });
            await uow.CommitAsync();

            return Results.Ok(existing);
        }

        var rule = new ParticipantStipendRule
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = request.StudyId,
            VisitCode = request.VisitCode,
            VisitName = request.VisitName,
            ProcedureCode = request.ProcedureCode,
            Amount = request.Amount,
            Currency = request.Currency ?? "USD",
            RequiresVerification = request.RequiresVerification,
            Taxable = request.Taxable,
            AntiCoercionCap = request.AntiCoercionCap,
            AutoDisbursementThreshold = request.AutoDisbursementThreshold,
            IsMilestone = request.IsMilestone,
            MilestoneCode = request.MilestoneCode,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = userId,
            UpdatedAt = now,
            VersionIndex = 1
        };
        uow.Context.ParticipantStipendRules.Add(rule);

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantStipendRule",
            EntityId = rule.Id,
            Action = "PARTICIPANT_STIPEND_RULE_CREATED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new { study_id = request.StudyId, visit_code = request.VisitCode, amount = request.Amount }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "SoA visit stipend compensation rule created",
            VersionIndex = 1
        });

        await uow.CommitAsync();

        return Results.Ok(rule);
    }

    public async Task<IResult> IngestVisitVerifiedEventAsync(VisitVerificationEventDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId) || string.IsNullOrWhiteSpace(request.VisitCode))
        {
            return Results.BadRequest(new { detail = "subject_id and visit_code are required." });
        }

        if (!Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return OutsideScope();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        string eventId = request.EventId ?? $"EVT-{Guid.NewGuid():N}";
        DateTimeOffset now = clock.UtcNow;

        try
        {
            return await uow.ExecuteWalletTransactionAsync(async ct =>
            {
                var wallet = await uow.LockWalletBySubjectAndStudyAsync(request.SubjectId, request.StudyId, ct);
                if (wallet != null && !Reaches(httpContext, wallet))
                {
                    return OutsideScope();
                }

                var existingTx = await uow.Context.ParticipantTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.ReferenceId == eventId || t.IdempotencyKey == eventId, ct);

                if (existingTx != null)
                {
                    var existingWallet = wallet ?? await uow.Context.ParticipantWallets.AsNoTracking()
                        .FirstOrDefaultAsync(w => w.Id == existingTx.WalletId, ct);
                    if (existingWallet != null && !Reaches(httpContext, existingWallet))
                    {
                        return OutsideScope();
                    }

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "ALREADY_PROCESSED",
                        Amount: 0m,
                        CurrentBalance: existingWallet?.CurrentBalance ?? 0m,
                        WalletId: existingTx.WalletId,
                        TransactionId: existingTx.Id,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: "Duplicate visit verification event already processed."));
                }

                var existingAudit = await uow.Context.PaymentAuditLogs.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.Details != null && l.Details.Contains(eventId), ct);

                if (existingAudit != null)
                {
                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "ALREADY_PROCESSED",
                        Amount: 0m,
                        CurrentBalance: wallet?.CurrentBalance ?? 0m,
                        WalletId: wallet?.Id ?? string.Empty,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: "Duplicate visit verification event already recorded in audit ledger."));
                }

                var rule = await uow.Context.ParticipantStipendRules.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IsActive && r.StudyId == request.StudyId && r.VisitCode == request.VisitCode, ct);

                if (rule == null)
                {
                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "SKIPPED",
                        Amount: 0m,
                        CurrentBalance: 0m,
                        WalletId: string.Empty,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"No active stipend compensation rule found for visit_code '{request.VisitCode}' and study_id '{request.StudyId}'."));
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
                        Currency = rule.Currency ?? "USD",
                        Status = "ACTIVE",
                        W9Status = "NOT_REQUIRED",
                        CreatedAt = now,
                        CreatedBy = userId,
                        UpdatedAt = now,
                        VersionIndex = 1
                    };
                    uow.Context.ParticipantWallets.Add(wallet);

                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "PARTICIPANT_WALLET_CREATED",
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { subject_id = request.SubjectId, study_id = request.StudyId, site_id = request.SiteId }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Initial wallet provisioned on eCRF visit verification",
                        VersionIndex = 1
                    });
                }

                decimal stipendAmount = rule.Amount;

                bool ruleCapExceeded = rule.AntiCoercionCap.HasValue && stipendAmount > rule.AntiCoercionCap.Value;
                bool walletCapExceeded = wallet.AntiCoercionCap.HasValue && stipendAmount > wallet.AntiCoercionCap.Value;
                if (ruleCapExceeded || walletCapExceeded)
                {
                    decimal effectiveCap = Math.Min(rule.AntiCoercionCap ?? decimal.MaxValue, wallet.AntiCoercionCap ?? decimal.MaxValue);
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "ANTI_COERCION_CAP_EXCEEDED",
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, anti_coercion_cap = effectiveCap, timestamp = now }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Stipend amount exceeds IRB anti-coercion cap limit",
                        VersionIndex = wallet.VersionIndex
                    });
                    await uow.CommitAsync(ct);

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "FLAGGED_FOR_REVIEW",
                        Amount: stipendAmount,
                        CurrentBalance: wallet.CurrentBalance,
                        WalletId: wallet.Id,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"Payment of ${stipendAmount:F2} exceeds IRB anti-coercion cap limit (${effectiveCap:F2}). Routed to administrator review queue.",
                        RequiresReview: true));
                }

                bool ruleThresholdExceeded = rule.AutoDisbursementThreshold.HasValue && stipendAmount > rule.AutoDisbursementThreshold.Value;
                bool walletThresholdExceeded = wallet.AutoDisbursementThreshold.HasValue && stipendAmount > wallet.AutoDisbursementThreshold.Value;
                if (ruleThresholdExceeded || walletThresholdExceeded)
                {
                    decimal effectiveThreshold = Math.Min(rule.AutoDisbursementThreshold ?? decimal.MaxValue, wallet.AutoDisbursementThreshold ?? decimal.MaxValue);
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "THRESHOLD_EXCEEDED_REVIEW_REQUIRED",
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, auto_disbursement_threshold = effectiveThreshold, timestamp = now }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Stipend amount exceeds auto-disbursement safety threshold",
                        VersionIndex = wallet.VersionIndex
                    });
                    await uow.CommitAsync(ct);

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "FLAGGED_FOR_REVIEW",
                        Amount: stipendAmount,
                        CurrentBalance: wallet.CurrentBalance,
                        WalletId: wallet.Id,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"Payment of ${stipendAmount:F2} exceeds configured auto-disbursement threshold limit (${effectiveThreshold:F2}). Routed to administrator review queue.",
                        RequiresReview: true));
                }

                decimal potentialTaxableYtd = rule.Taxable ? wallet.TaxableYtd + stipendAmount : wallet.TaxableYtd;
                if (potentialTaxableYtd >= 600.00m && wallet.W9Status != "VERIFIED")
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
                        UserId = userId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, taxable_ytd = potentialTaxableYtd, w9_status = wallet.W9Status, timestamp = now }),
                        CreatedAt = now,
                        CreatedBy = userId,
                        ReasonForChange = "Participant YTD taxable earnings reach or exceed $600.00 IRS 1099 threshold without verified W-9",
                        VersionIndex = wallet.VersionIndex
                    });
                    await uow.CommitAsync(ct);

                    return Results.Ok(new VisitVerificationResponseDto(
                        Status: "FLAGGED_FOR_REVIEW",
                        Amount: stipendAmount,
                        CurrentBalance: wallet.CurrentBalance,
                        WalletId: wallet.Id,
                        TransactionId: null,
                        EventId: eventId,
                        SubjectId: request.SubjectId,
                        Reason: $"IRS $600.00 YTD tax threshold reached (YTD: ${potentialTaxableYtd:F2}) without a verified W-9 form. Auto-disbursement halted for administrator review.",
                        RequiresReview: true,
                        W9Status: wallet.W9Status));
                }

                wallet.CurrentBalance += stipendAmount;
                wallet.LifetimeStipends += stipendAmount;
                if (rule.Taxable)
                {
                    wallet.TaxableYtd += stipendAmount;
                    if (wallet.TaxableYtd >= 600.00m)
                    {
                        wallet.Is1099ThresholdExceeded = true;
                    }
                }

                wallet.UpdatedAt = now;
                wallet.VersionIndex += 1;

                string txType = rule.IsMilestone ? "MILESTONE_CREDIT" : "STIPEND_CREDIT";
                var tx = new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = request.SubjectId,
                    StudyId = request.StudyId,
                    TransactionType = txType,
                    Amount = stipendAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = eventId,
                    IdempotencyKey = eventId,
                    Description = $"Verified visit stipend credit for {rule.VisitName} ({rule.VisitCode})",
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
                    Action = "PARTICIPANT_STIPEND_CREDITED",
                    UserId = userId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        event_id = eventId,
                        visit_code = request.VisitCode,
                        stipend_amount = stipendAmount,
                        new_balance = wallet.CurrentBalance,
                        transaction_id = tx.Id,
                        rule_id = rule.Id,
                        timestamp = now
                    }),
                    CreatedAt = now,
                    CreatedBy = userId,
                    ReasonForChange = $"EDC visit verification automated stipend payout for {rule.VisitCode}",
                    VersionIndex = wallet.VersionIndex
                });

                await uow.CommitAsync(ct);

                return Results.Ok(new VisitVerificationResponseDto(
                    Status: "CREDITED",
                    Amount: stipendAmount,
                    CurrentBalance: wallet.CurrentBalance,
                    WalletId: wallet.Id,
                    TransactionId: tx.Id,
                    EventId: eventId,
                    SubjectId: request.SubjectId));
            });
        }
        catch (DbUpdateException ex) when (PaymentsUnitOfWork.IsUniqueViolation(ex, "payments_participant_transactions"))
        {
            uow.Context.ChangeTracker.Clear();
            var racedTx = await uow.Context.ParticipantTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.ReferenceId == eventId || t.IdempotencyKey == eventId);
            var racedWallet = await uow.Context.ParticipantWallets.AsNoTracking()
                .FirstOrDefaultAsync(w => w.SubjectId == request.SubjectId && w.StudyId == request.StudyId);
            if (racedWallet != null && !Reaches(httpContext, racedWallet))
            {
                return OutsideScope();
            }

            if (racedTx != null && racedWallet != null)
            {
                return Results.Ok(new VisitVerificationResponseDto(
                    Status: "ALREADY_PROCESSED",
                    Amount: 0m,
                    CurrentBalance: racedWallet.CurrentBalance,
                    WalletId: racedWallet.Id,
                    TransactionId: racedTx.Id,
                    EventId: eventId,
                    SubjectId: request.SubjectId,
                    Reason: "Duplicate visit verification event already processed."));
            }

            throw;
        }
    }

    public Task<IResult> DisburseStipendDirectAsync(DisburseStipendRequest request, HttpContext httpContext) =>
        ParticipantDiaryStipendEvaluator.DisburseStipendDirectAsync(uow, clock, request, httpContext);

    public Task<IResult> CreditSelfDiaryStipendAsync(SelfServiceDiaryStipendRequestDto request, HttpContext httpContext) =>
        ParticipantDiaryStipendEvaluator.CreditSelfDiaryStipendAsync(uow, clock, gatewayAuthentication, request, httpContext);
}
