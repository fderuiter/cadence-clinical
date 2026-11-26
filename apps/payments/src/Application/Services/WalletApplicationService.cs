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

namespace Cadence.Apps.Payments.Application.Services;

/// <summary>
/// Manages participant study wallets, balance inquiries, card issuance, staff payouts, and self-service payouts.
/// </summary>
public sealed class WalletApplicationService(
    PaymentsDbContext dbContext,
    IPaymentsUnitOfWork unitOfWork,
    IWalletRowLockRepository walletLocks,
    IClock clock)
{
    public async Task<IResult> ListWalletsAsync(HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var wallets = await ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();

        return Results.Ok(wallets.Select(PaymentScopeAndMappingHelpers.MapWalletToResponse));
    }

    public async Task<IResult> GetWalletByIdAsync(string walletId, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, wallet))
        {
            return Results.NotFound(new { detail = $"Participant wallet with ID '{walletId}' not found." });
        }

        return Results.Ok(PaymentScopeAndMappingHelpers.MapWalletToResponse(wallet));
    }

    public async Task<IResult> GetSubjectWalletAsync(string subjectId, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var wallet = await ClinicalScopeGuard.NarrowToScope(dbContext.ParticipantWallets, httpContext, w => w.StudyId, w => w.SiteId)
            .FirstOrDefaultAsync(w => w.SubjectId == subjectId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"No participant wallet for subject '{subjectId}'." });
        }

        return Results.Ok(PaymentScopeAndMappingHelpers.MapWalletToResponse(wallet));
    }

    public async Task<IResult> GetBalanceInquiryAsync(string walletId, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, wallet))
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        decimal pendingClaims = await dbContext.ParticipantExpenseClaims
            .Where(c => c.WalletId == wallet.Id && c.Status == "PENDING_REVIEW")
            .SumAsync(c => c.NetAmount);

        return Results.Ok(new WalletBalanceInquiryResponseDto(
            WalletId: wallet.Id,
            SubjectId: wallet.SubjectId,
            AvailableBalance: wallet.CurrentBalance,
            PendingClaimsTotal: pendingClaims,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            TaxableYtd: wallet.TaxableYtd
        ));
    }

    public async Task<IResult> IssueReloadableCardAsync(string walletId, IssueCardRequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);

        return await walletLocks.ExecuteWalletUnitOfWorkAsync(async ct =>
        {
            var wallet = await walletLocks.LockWalletByIdAsync(walletId, ct);

            if (wallet == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            DateTimeOffset now = clock.UtcNow;
            wallet.CardType = request.CardType ?? "RELOADABLE_DEBIT";
            wallet.CardStatus = "ACTIVE";
            wallet.VirtualCardMask = $"**** **** **** {Random.Shared.Next(1000, 9999)}";
            wallet.VirtualCardExpiry = $"{now.Month:D2}/{(now.Year + 3) % 100:D2}";
            wallet.VirtualCardProcessorReference = $"VTOK-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "RELOADABLE_CARD_ISSUED",
                UserId = userId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new { card_type = wallet.CardType, mask = wallet.VirtualCardMask, expiry = wallet.VirtualCardExpiry }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Reloadable debit card issued to participant",
                VersionIndex = wallet.VersionIndex
            });
            await unitOfWork.CommitAsync(ct);

            return Results.Ok(PaymentScopeAndMappingHelpers.MapWalletToResponse(wallet));
        });
    }

    public async Task<IResult> DisburseParticipantFundsAsync(string walletId, DisburseFundsRequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);

        return await walletLocks.ExecuteWalletUnitOfWorkAsync(async ct =>
        {
            var wallet = await walletLocks.LockWalletByIdAsync(walletId, ct);

            if (wallet == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            if (wallet.TaxableYtd >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new
                {
                    detail = "IRS $600.00 annual taxable payment threshold reached without a verified Form W-9. Automated payouts are halted until Form W-9 is verified."
                });
            }

            decimal payoutAmount = request.Amount > 0m ? request.Amount : wallet.CurrentBalance;

            if (payoutAmount <= 0m)
            {
                return Results.BadRequest(new { detail = "Disbursement amount must be greater than zero." });
            }

            if (payoutAmount > wallet.CurrentBalance)
            {
                return Results.BadRequest(new { detail = $"Insufficient funds: Requested ${payoutAmount:F2} but available balance is ${wallet.CurrentBalance:F2}." });
            }

            DateTimeOffset now = clock.UtcNow;
            wallet.CurrentBalance -= payoutAmount;
            wallet.LifetimeDisbursements += payoutAmount;
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            string refPrefix = request.DisbursementType == "DIRECT_ACH" ? "ACH" : "VCARD";
            string refNumber = $"DISB-{refPrefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

            var disbursement = new ParticipantDisbursement
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = wallet.SubjectId,
                Amount = payoutAmount,
                DisbursementType = request.DisbursementType,
                ReferenceNumber = refNumber,
                Status = "DISPATCHED",
                CreatedAt = now,
                CreatedBy = userId,
                VersionIndex = 1
            };
            dbContext.ParticipantDisbursements.Add(disbursement);

            dbContext.ParticipantTransactions.Add(new ParticipantTransaction
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = wallet.SubjectId,
                StudyId = wallet.StudyId,
                TransactionType = "DISBURSEMENT_DEBIT",
                Amount = -payoutAmount,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceId = disbursement.Id,
                Description = $"Digital payout dispatch via {request.DisbursementType} ({refNumber})",
                CreatedAt = now,
                CreatedBy = userId,
                VersionIndex = 1
            });

            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
                UserId = userId,
                UserRoles = rolesText,
                Details = JsonSerializer.Serialize(new
                {
                    disbursement_id = disbursement.Id,
                    amount = payoutAmount,
                    reference_number = refNumber,
                    disbursement_type = request.DisbursementType,
                    new_balance = wallet.CurrentBalance
                }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Digital stipend payout funds dispatched to participant card/account",
                VersionIndex = wallet.VersionIndex
            });

            await unitOfWork.CommitAsync(ct);

            return Results.Ok(disbursement);
        });
    }

    public async Task<IResult> GetSelfParticipantWalletAsync(
        HttpContext httpContext,
        string? subjectId = null,
        string? walletId = null,
        string? studyId = null)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        DateTimeOffset now = clock.UtcNow;
        var (wallet, error) = await SelfWalletResolver.ResolveOwnWalletAsync(
            dbContext,
            walletLocks,
            httpContext,
            caller,
            requestedSubjectId: subjectId,
            requestedWalletId: walletId,
            requestedStudyId: studyId,
            requestedSiteId: null,
            createIfMissing: false,
            now: now);

        if (error != null)
        {
            return error;
        }

        if (wallet == null)
        {
            string? fallbackStudy = !string.IsNullOrWhiteSpace(studyId)
                ? studyId.Trim()
                : caller.AssignedStudies.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "*");
            if (string.IsNullOrWhiteSpace(fallbackStudy)
                && !caller.AssignedStudies.Any(s => string.Equals(s.Trim(), StudyAccessEvaluator.AllStudies, StringComparison.Ordinal)))
            {
                return PaymentScopeAndMappingHelpers.OutsideScope();
            }
            if (!string.IsNullOrWhiteSpace(fallbackStudy) && !ClinicalScopeGuard.CanAccessStudy(httpContext, fallbackStudy))
            {
                return PaymentScopeAndMappingHelpers.OutsideScope();
            }

            string emptyStudy = fallbackStudy ?? "UNASSIGNED";
            string emptySite = caller.AssignedSites.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "SITE-DEFAULT";
            if (!PaymentScopeAndMappingHelpers.Reaches(httpContext, emptyStudy, emptySite))
            {
                return PaymentScopeAndMappingHelpers.OutsideScope();
            }

            var emptyProjection = new WalletResponse(
                Id: $"unprovisioned:{caller.UserId}",
                SubjectId: caller.UserId,
                StudyId: emptyStudy,
                SiteId: emptySite,
                CurrentBalance: 0m,
                LifetimeStipends: 0m,
                LifetimeReimbursements: 0m,
                LifetimeDisbursements: 0m,
                TaxableYtd: 0m,
                Is1099ThresholdExceeded: false,
                W9Status: "NOT_REQUIRED",
                Currency: "USD",
                Status: "ACTIVE",
                VirtualCardMask: "**** **** **** 4821",
                VirtualCardExpiry: "12/29",
                AchRoutingNumber: "*****0021",
                AchAccountMask: "ACH-***4321",
                AchAccountHolder: $"Participant {caller.UserId}",
                AntiCoercionCap: null,
                AutoDisbursementThreshold: null,
                UpdatedAt: now,
                CardType: "RELOADABLE_DEBIT",
                CardStatus: "ACTIVE",
                AchRoutingMask: "*****0021"
            );

            return Results.Ok(new SelfServiceWalletSummaryResponseDto(emptyProjection, [], []));
        }

        var recentTransactions = (await dbContext.ParticipantTransactions.AsNoTracking()
            .Where(t => t.WalletId == wallet.Id && t.SubjectId == caller.UserId)
            .ToListAsync())
            .OrderByDescending(t => t.CreatedAt)
            .Take(25)
            .ToList();

        var claims = (await dbContext.ParticipantExpenseClaims.AsNoTracking()
            .Where(c => c.WalletId == wallet.Id && c.SubjectId == caller.UserId)
            .ToListAsync())
            .OrderByDescending(c => c.CreatedAt)
            .Take(25)
            .ToList();

        return Results.Ok(new SelfServiceWalletSummaryResponseDto(
            Wallet: PaymentScopeAndMappingHelpers.MapWalletToResponse(wallet),
            RecentTransactions: recentTransactions,
            ExpenseClaims: claims
        ));
    }

    public Task<IResult> DisburseSelfWalletBalanceAsync(
        SelfServiceDisburseRequestDto request,
        HttpContext httpContext,
        string? idempotencyKeyHeader = null) =>
        SelfServiceWalletDisburser.DisburseAsync(
            dbContext, unitOfWork, walletLocks, clock, request, httpContext, idempotencyKeyHeader);
}
