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
/// Executes participant self-service wallet balance payouts with row-level wallet locks, idempotency, and W-9 enforcement.
/// </summary>
internal static class SelfServiceWalletDisburser
{
    public static Task<IResult> DisburseAsync(
        PaymentsDbContext dbContext,
        IPaymentsUnitOfWork unitOfWork,
        IWalletRowLockRepository walletLocks,
        IClock clock,
        SelfServiceDisburseRequestDto request,
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
            RequestedStudyCode: null,
            RequestedSiteCode: null,
            CreateIfMissing: false,
            clock.UtcNow,
            MissingWalletResult: Results.NotFound(new { detail = "No active participant wallet found for the signed-in participant." }));

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
                    var existingDisbursement = await dbContext.ParticipantDisbursements.AsNoTracking()
                        .FirstOrDefaultAsync(d =>
                            d.WalletId == wallet.Id
                            && d.SubjectId == caller.UserId
                            && d.IdempotencyKey == idempotencyKey, ct);
                    if (existingDisbursement != null)
                    {
                        return BuildResponse(existingDisbursement, wallet, caller.UserId);
                    }
                }

                if (wallet.TaxableYtd >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                {
                    dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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
                    await unitOfWork.CommitAsync(ct);

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
                    ? (PaymentScopeAndMappingHelpers.MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321")
                    : (PaymentScopeAndMappingHelpers.MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821");

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
                dbContext.ParticipantDisbursements.Add(disbursement);

                dbContext.ParticipantTransactions.Add(new ParticipantTransaction
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

                dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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

                await unitOfWork.CommitAsync(ct);

                return BuildResponse(disbursement, wallet, caller.UserId, destinationMask);
            },
            async (racedWallet, caller, idempotencyKey) =>
            {
                var racedDisbursement = await dbContext.ParticipantDisbursements.AsNoTracking()
                    .FirstOrDefaultAsync(d =>
                        d.WalletId == racedWallet.Id
                        && d.SubjectId == caller.UserId
                        && d.IdempotencyKey == idempotencyKey);
                return racedDisbursement != null
                    ? BuildResponse(racedDisbursement, racedWallet, caller.UserId)
                    : null;
            });
    }

    private static IResult BuildResponse(
        ParticipantDisbursement disbursement,
        ParticipantWallet wallet,
        string subjectId,
        string? explicitDestinationMask = null)
    {
        string destinationMask = explicitDestinationMask ?? (
            string.Equals(disbursement.DisbursementType, "VIRTUAL_CARD", StringComparison.OrdinalIgnoreCase)
                ? (PaymentScopeAndMappingHelpers.MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821")
                : (PaymentScopeAndMappingHelpers.MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321"));

        return Results.Ok(new SelfServiceDisburseResponseDto(
            Id: disbursement.Id,
            WalletId: wallet.Id,
            SubjectId: subjectId,
            Amount: disbursement.Amount,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            Currency: wallet.Currency,
            DisbursementType: disbursement.DisbursementType,
            DestinationMask: destinationMask,
            ReferenceNumber: disbursement.ReferenceNumber,
            Status: disbursement.Status,
            CreatedAt: disbursement.CreatedAt
        ));
    }
}
