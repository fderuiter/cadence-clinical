using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

public sealed partial class ParticipantWalletApplicationService
{
    public async Task<IResult> DisburseSelfWalletBalanceAsync(
        SelfServiceDisburseRequestDto request,
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
                var (wallet, error) = await ResolveOwnWalletAsync(
                    uow,
                    httpContext,
                    caller,
                    requestedSubjectCode: request.SubjectId,
                    requestedWalletCode: request.WalletId,
                    requestedStudyCode: null,
                    requestedSiteCode: null,
                    createIfMissing: false,
                    now: now,
                    lockForUpdate: true,
                    ct: ct);

                if (error != null)
                {
                    return error;
                }

                if (wallet == null)
                {
                    return Results.NotFound(new { detail = "No active participant wallet found for the signed-in participant." });
                }

                resolvedWalletId = wallet.Id;

                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    var existingDisbursement = await uow.Context.ParticipantDisbursements.AsNoTracking()
                        .FirstOrDefaultAsync(d =>
                            d.WalletId == wallet.Id
                            && d.SubjectId == caller.UserId
                            && d.IdempotencyKey == idempotencyKey, ct);
                    if (existingDisbursement != null)
                    {
                        return BuildSelfServiceDisburseResponse(existingDisbursement, wallet, caller.UserId);
                    }
                }

                if (wallet.TaxableYtd >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                {
                    uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                    {
                        Id = Guid.NewGuid().ToString(),
                        EntityType = "ParticipantWallet",
                        EntityId = wallet.Id,
                        Action = "PAYOUT_HALTED_W9_REQUIRED",
                        UserId = caller.UserId,
                        UserRoles = rolesText,
                        Details = JsonSerializer.Serialize(new
                        {
                            wallet_id = wallet.Id,
                            subject_id = caller.UserId,
                            taxable_ytd = wallet.TaxableYtd,
                            w9_status = wallet.W9Status
                        }),
                        CreatedAt = now,
                        CreatedBy = caller.UserId,
                        ReasonForChange = "Self-service payout halted pending Form W-9 verification",
                        VersionIndex = wallet.VersionIndex
                    });
                    await uow.CommitAsync(ct);

                    return Results.Conflict(new
                    {
                        detail = "Annual taxable payments have reached $600.00 without a verified Form W-9. Automated payouts are halted until your Form W-9 is verified by site staff."
                    });
                }

                decimal payoutAmount = request.Amount.HasValue && request.Amount.Value > 0m
                    ? Math.Round(request.Amount.Value, 2)
                    : wallet.CurrentBalance;

                if (payoutAmount <= 0m)
                {
                    return Results.Conflict(new { detail = "Available wallet balance is $0.00; there are no funds to disburse." });
                }

                if (payoutAmount > wallet.CurrentBalance)
                {
                    return Results.BadRequest(new { detail = $"Requested payout (${payoutAmount:F2}) exceeds available wallet balance (${wallet.CurrentBalance:F2})." });
                }

                string disbursementType = string.Equals(request.DisbursementType, "VIRTUAL_CARD", StringComparison.OrdinalIgnoreCase)
                    ? "VIRTUAL_CARD"
                    : "DIRECT_ACH";

                string refPrefix = disbursementType == "DIRECT_ACH" ? "ACH" : "VCARD";
                string refNumber = $"DISB-{refPrefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
                string destinationMask = disbursementType == "DIRECT_ACH"
                    ? (MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321")
                    : (MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821");

                wallet.CurrentBalance -= payoutAmount;
                wallet.LifetimeDisbursements += payoutAmount;
                wallet.UpdatedAt = now;
                wallet.VersionIndex += 1;

                var disbursement = new ParticipantDisbursement
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = caller.UserId,
                    Amount = payoutAmount,
                    DisbursementType = disbursementType,
                    ReferenceNumber = refNumber,
                    IdempotencyKey = idempotencyKey,
                    Status = "DISPATCHED",
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    VersionIndex = 1
                };
                uow.Context.ParticipantDisbursements.Add(disbursement);

                uow.Context.ParticipantTransactions.Add(new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = caller.UserId,
                    StudyId = wallet.StudyId,
                    TransactionType = "DISBURSEMENT_DEBIT",
                    Amount = -payoutAmount,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = disbursement.Id,
                    Description = $"Participant on-demand balance payout via {disbursementType} ({refNumber})",
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    VersionIndex = 1
                });

                uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    EntityType = "ParticipantWallet",
                    EntityId = wallet.Id,
                    Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
                    UserId = caller.UserId,
                    UserRoles = rolesText,
                    Details = JsonSerializer.Serialize(new
                    {
                        disbursement_id = disbursement.Id,
                        subject_id = caller.UserId,
                        amount = payoutAmount,
                        new_balance = wallet.CurrentBalance,
                        reference_number = refNumber,
                        disbursement_type = disbursementType,
                        destination_mask = destinationMask
                    }),
                    CreatedAt = now,
                    CreatedBy = caller.UserId,
                    ReasonForChange = "Participant triggered on-demand wallet balance payout",
                    VersionIndex = wallet.VersionIndex
                });

                await uow.CommitAsync(ct);

                return BuildSelfServiceDisburseResponse(disbursement, wallet, caller.UserId, destinationMask);
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
                var racedDisbursement = await uow.Context.ParticipantDisbursements.AsNoTracking()
                    .FirstOrDefaultAsync(d =>
                        d.WalletId == racedWallet.Id
                        && d.SubjectId == caller.UserId
                        && d.IdempotencyKey == idempotencyKey);
                if (racedDisbursement != null)
                {
                    return BuildSelfServiceDisburseResponse(racedDisbursement, racedWallet, caller.UserId);
                }
            }

            throw;
        }
    }

    public async Task<IResult> ListTransactionsAsync(HttpContext httpContext, string? subjectCode = null, string? walletCode = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = uow.Context.ParticipantTransactions.AsNoTracking();
        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(uow.Context.ParticipantWallets.AsNoTracking(), httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            query = query.Where(t => walletIds.Contains(t.WalletId));
        }

        if (!string.IsNullOrWhiteSpace(subjectCode))
        {
            query = query.Where(t => t.SubjectId == subjectCode);
        }

        if (!string.IsNullOrWhiteSpace(walletCode))
        {
            query = query.Where(t => t.WalletId == walletCode);
        }

        var txs = await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Results.Ok(txs);
    }

    public async Task<IResult> ListDisbursementsAsync(HttpContext httpContext, string? subjectCode = null, string? walletCode = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = uow.Context.ParticipantDisbursements.AsNoTracking();
        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(uow.Context.ParticipantWallets.AsNoTracking(), httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            query = query.Where(d => walletIds.Contains(d.WalletId));
        }

        if (!string.IsNullOrWhiteSpace(subjectCode))
        {
            query = query.Where(d => d.SubjectId == subjectCode);
        }

        if (!string.IsNullOrWhiteSpace(walletCode))
        {
            query = query.Where(d => d.WalletId == walletCode);
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
            return NoSignedCaller();
        }

        var disb = await uow.Context.ParticipantDisbursements.FirstOrDefaultAsync(d => d.Id == id || d.ReferenceNumber == id);
        if (disb == null)
        {
            return Results.NotFound(new { detail = $"Disbursement '{id}' not found." });
        }

        var wallet = await uow.Context.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == disb.WalletId);
        if (wallet != null && !Reaches(httpContext, wallet.StudyId, wallet.SiteId))
        {
            return OutsideScope();
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

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantDisbursement",
            EntityId = disb.Id,
            Action = "DIGITAL_DISBURSEMENT_SETTLED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
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
        await uow.CommitAsync();

        return Results.Ok(disb);
    }

    public async Task<IResult> GetAuditLogsAsync(HttpContext httpContext, string? subjectCode = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = uow.Context.PaymentAuditLogs.AsNoTracking();

        if (!ClinicalScopeGuard.IsUnrestricted(httpContext))
        {
            var walletIds = ClinicalScopeGuard.NarrowToScope(uow.Context.ParticipantWallets.AsNoTracking(), httpContext, w => w.StudyId, w => w.SiteId).Select(w => w.Id);
            var claimIds = ClinicalScopeGuard.NarrowToScope(uow.Context.ParticipantExpenseClaims.AsNoTracking(), httpContext, c => c.StudyId, c => c.SiteId).Select(c => c.Id);
            var invoiceIds = ClinicalScopeGuard.NarrowToScope(uow.Context.SelfBillingInvoices.AsNoTracking(), httpContext, i => i.StudyId, i => i.SiteId).Select(i => i.Id);
            var remittanceIds = ClinicalScopeGuard.NarrowToScope(uow.Context.RemittanceAdvices.AsNoTracking(), httpContext, r => r.StudyId, r => r.SiteId).Select(r => r.Id);
            var ruleIds = ClinicalScopeGuard.NarrowToStudyScope(uow.Context.ParticipantStipendRules.AsNoTracking(), httpContext, r => r.StudyId).Select(r => r.Id);
            query = query.Where(l =>
                walletIds.Contains(l.EntityId) || claimIds.Contains(l.EntityId) || invoiceIds.Contains(l.EntityId)
                || remittanceIds.Contains(l.EntityId) || ruleIds.Contains(l.EntityId));
        }

        if (!string.IsNullOrWhiteSpace(subjectCode))
        {
            query = query.Where(l => l.UserId == subjectCode || (l.Details != null && l.Details.Contains(subjectCode)));
        }

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(100)
            .ToListAsync();

        return Results.Ok(logs);
    }

    private static IResult BuildSelfServiceDisburseResponse(
        ParticipantDisbursement disbursement,
        ParticipantWallet wallet,
        string subjectCode,
        string? explicitDestinationMask = null)
    {
        string destinationMask = explicitDestinationMask ?? (
            string.Equals(disbursement.DisbursementType, "VIRTUAL_CARD", StringComparison.OrdinalIgnoreCase)
                ? (MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821")
                : (MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321"));

        return Results.Ok(new SelfServiceDisburseResponseDto(
            Id: disbursement.Id,
            WalletId: wallet.Id,
            SubjectId: subjectCode,
            Amount: disbursement.Amount,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            Currency: wallet.Currency,
            DisbursementType: disbursement.DisbursementType,
            DestinationMask: destinationMask,
            ReferenceNumber: disbursement.ReferenceNumber,
            Status: disbursement.Status,
            CreatedAt: disbursement.CreatedAt));
    }
}
