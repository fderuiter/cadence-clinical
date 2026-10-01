using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Endpoints;

public record DisburseStipendRequest(
    string SubjectId,
    string? StudyId,
    string? SiteId,
    int? SequenceNumber,
    string? AssignmentId,
    string? DiaryId,
    decimal? Amount,
    string? IdempotencyKey,
    string? DisbursementType
);

public record DisburseStipendResponse(
    bool Success,
    string Id,
    string WalletId,
    string SubjectId,
    decimal Amount,
    decimal NewBalance,
    string Currency,
    string Status,
    string ReferenceNumber,
    string IdempotencyKey,
    DateTimeOffset DispatchedAt
);

public record WalletResponse(
    string Id,
    string SubjectId,
    string StudyId,
    string SiteId,
    decimal CurrentBalance,
    decimal LifetimeStipends,
    decimal LifetimeDisbursements,
    string Currency,
    string Status,
    DateTimeOffset UpdatedAt
);

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapPost("/disburse", DisburseStipend)
            .WithName("DisburseStipendDirect")
            .WithSummary("Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail.");

        group.MapPost("/participant/wallets/disburse", DisburseStipend)
            .WithName("DisburseStipendWallet")
            .WithSummary("Disburse stipend to participant wallet.");

        group.MapGet("/participant/wallets/subject/{subjectId}", GetSubjectWallet)
            .WithName("GetSubjectWallet")
            .WithSummary("Retrieve participant wallet balance and details.");

        group.MapGet("/audit-logs", GetAuditLogs)
            .WithName("GetPaymentAuditLogs")
            .WithSummary("Retrieve payment audit ledger records.");

        return routes;
    }

    private static async Task<IResult> DisburseStipend(
        DisburseStipendRequest request,
        PaymentsDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.BadRequest(new { detail = "subject_id is required for stipend disbursement." });
        }

        string idempotencyKey = request.IdempotencyKey
            ?? (request.SequenceNumber.HasValue ? $"seq_{request.SequenceNumber}_{request.SubjectId}" : $"disburse_{request.SubjectId}_{Guid.NewGuid()}");

        // Strict idempotency check using PaymentAuditLogs details
        var existingLog = await dbContext.PaymentAuditLogs
            .FirstOrDefaultAsync(l => l.Action == "AUTO_STIPEND_DISBURSEMENT" && l.Details != null && l.Details.Contains(idempotencyKey));

        if (existingLog != null)
        {
            var existingWallet = await dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.SubjectId == request.SubjectId);

            decimal currentBal = existingWallet?.CurrentBalance ?? 0m;
            string walletId = existingWallet?.Id ?? Guid.NewGuid().ToString();

            return Results.Ok(new DisburseStipendResponse(
                Success: true,
                Id: existingLog.Id,
                WalletId: walletId,
                SubjectId: request.SubjectId,
                Amount: request.Amount ?? 25.00m,
                NewBalance: currentBal,
                Currency: existingWallet?.Currency ?? "USD",
                Status: "DUPLICATE_SKIPPED",
                ReferenceNumber: $"DISB-{existingLog.Id[..Math.Min(8, existingLog.Id.Length)]}",
                IdempotencyKey: idempotencyKey,
                DispatchedAt: existingLog.CreatedAt
            ));
        }

        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.SubjectId == request.SubjectId);

        DateTimeOffset now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;

        if (wallet == null)
        {
            wallet = new ParticipantWallet
            {
                Id = Guid.NewGuid().ToString(),
                SubjectId = request.SubjectId,
                StudyId = request.StudyId ?? "study_01",
                SiteId = request.SiteId ?? "site_01",
                CurrentBalance = 0m,
                LifetimeStipends = 0m,
                LifetimeReimbursements = 0m,
                LifetimeDisbursements = 0m,
                Currency = "USD",
                Status = "ACTIVE",
                CreatedAt = now,
                CreatedBy = request.SubjectId,
                UpdatedAt = now,
                VersionIndex = 1
            };
            dbContext.ParticipantWallets.Add(wallet);
        }

        decimal stipendAmount = request.Amount ?? 25.00m;
        wallet.CurrentBalance += stipendAmount;
        wallet.LifetimeStipends += stipendAmount;
        wallet.LifetimeDisbursements += stipendAmount;
        wallet.UpdatedAt = now;

        var auditDetails = JsonSerializer.Serialize(new
        {
            subject_id = request.SubjectId,
            sequence_number = request.SequenceNumber,
            assignment_id = request.AssignmentId,
            diary_id = request.DiaryId,
            stipend_amount = stipendAmount,
            idempotency_key = idempotencyKey,
            new_balance = wallet.CurrentBalance,
            timestamp = now
        });

        var auditLog = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "AUTO_STIPEND_DISBURSEMENT",
            UserId = request.SubjectId,
            UserRoles = "Subject",
            Details = auditDetails,
            CreatedAt = now,
            CreatedBy = request.SubjectId,
            ReasonForChange = "Automatic ePRO survey completion stipend payout",
            VersionIndex = 1
        };

        dbContext.PaymentAuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new DisburseStipendResponse(
            Success: true,
            Id: auditLog.Id,
            WalletId: wallet.Id,
            SubjectId: request.SubjectId,
            Amount: stipendAmount,
            NewBalance: wallet.CurrentBalance,
            Currency: wallet.Currency,
            Status: "COMPLETED",
            ReferenceNumber: $"DISB-{auditLog.Id[..Math.Min(8, auditLog.Id.Length)]}",
            IdempotencyKey: idempotencyKey,
            DispatchedAt: now
        ));
    }

    private static async Task<IResult> GetSubjectWallet(
        string subjectId,
        PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.SubjectId == subjectId);

        if (wallet == null)
        {
            DateTimeOffset now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;
            wallet = new ParticipantWallet
            {
                Id = Guid.NewGuid().ToString(),
                SubjectId = subjectId,
                StudyId = "study_01",
                SiteId = "site_01",
                CurrentBalance = 0m,
                LifetimeStipends = 0m,
                LifetimeReimbursements = 0m,
                LifetimeDisbursements = 0m,
                Currency = "USD",
                Status = "ACTIVE",
                CreatedAt = now,
                CreatedBy = subjectId,
                UpdatedAt = now,
                VersionIndex = 1
            };
            dbContext.ParticipantWallets.Add(wallet);
            await dbContext.SaveChangesAsync();
        }

        return Results.Ok(new WalletResponse(
            Id: wallet.Id,
            SubjectId: wallet.SubjectId,
            StudyId: wallet.StudyId,
            SiteId: wallet.SiteId,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            Currency: wallet.Currency,
            Status: wallet.Status,
            UpdatedAt: wallet.UpdatedAt
        ));
    }

    private static async Task<IResult> GetAuditLogs(
        PaymentsDbContext dbContext,
        string? subjectId = null)
    {
        var query = dbContext.PaymentAuditLogs.AsQueryable();

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
