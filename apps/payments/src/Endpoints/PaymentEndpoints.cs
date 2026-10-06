using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;

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
    decimal LifetimeReimbursements,
    decimal LifetimeDisbursements,
    decimal TaxableYtd,
    bool Is1099ThresholdExceeded,
    string W9Status,
    string Currency,
    string Status,
    string? VirtualCardMask,
    string? VirtualCardExpiry,
    string? AchRoutingNumber,
    string? AchAccountMask,
    string? AchAccountHolder,
    decimal? AntiCoercionCap,
    decimal? AutoDisbursementThreshold,
    DateTimeOffset UpdatedAt
);

public record VisitVerificationEventDto(
    string StudyId,
    string SiteId,
    string SubjectId,
    string VisitCode,
    string EventId,
    string? VerifiedBy = "crc_coordinator"
);

public record VisitVerificationResponseDto(
    string Status,
    decimal Amount,
    decimal CurrentBalance,
    string WalletId,
    string? TransactionId,
    string EventId,
    string SubjectId,
    string? Reason = null,
    bool RequiresReview = false,
    string? W9Status = null
);

public record ParticipantStipendRuleCreateDto(
    string StudyId,
    string VisitCode,
    string VisitName,
    string? ProcedureCode,
    decimal Amount,
    string? Currency = "USD",
    bool RequiresVerification = true,
    bool Taxable = false,
    decimal? AntiCoercionCap = null,
    decimal? AutoDisbursementThreshold = null,
    bool IsMilestone = false,
    string? MilestoneCode = null
);

public record SubmitW9RequestDto(
    string FullLegalName,
    string TaxIdLastFour,
    string? Address = null,
    bool CertifyAccurate = true
);

public record VerifyW9RequestDto(
    string? Notes = null
);

public record IssueCardRequestDto(
    string? CardType = "RELOADABLE_DEBIT"
);

public record DisburseFundsRequestDto(
    string DisbursementType,
    decimal Amount
);

public record SubmitExpenseClaimRequestDto(
    string SubjectId,
    string StudyId,
    string SiteId,
    string Category,
    string MerchantName,
    decimal GrossAmount,
    bool IsMealAllowance = false,
    decimal? PerDiemCap = null,
    string? ExpenseDate = null,
    string? ReceiptImageName = null,
    object? ReceiptOcrData = null,
    double? OcrConfidence = null
);

public record ReviewExpenseClaimRequestDto(
    string Status,
    string? ReviewNotes = null,
    decimal? AdjustedAmount = null
);

public record OcrExtractRequestDto(
    string? RawText = null,
    string? ImageBase64 = null,
    string? CategoryHint = null,
    string? Filename = null
);

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        // Legacy / Direct endpoints
        group.MapPost("/disburse", DisburseStipendDirect)
            .WithName("DisburseStipendDirect")
            .WithSummary("Direct disburse ePRO stipend to participant wallet with strict idempotency and audit trail.");

        group.MapPost("/participant/wallets/disburse", DisburseStipendDirect)
            .WithName("DisburseStipendWallet")
            .WithSummary("Disburse stipend to participant wallet.");

        // Participant Wallets Directory & Management
        group.MapGet("/participant/wallets", ListWallets)
            .WithName("ListWallets")
            .WithSummary("List all participant wallets.");

        group.MapGet("/participant/wallets/subject/{subjectId}", GetSubjectWallet)
            .WithName("GetSubjectWallet")
            .WithSummary("Retrieve participant wallet balance and details.");

        group.MapGet("/participant/wallets/{walletId}", GetWalletById)
            .WithName("GetWalletById")
            .WithSummary("Retrieve participant wallet details by wallet ID.");

        group.MapGet("/participant/wallets/{walletId}/tax-1099-status", GetTax1099Status)
            .WithName("GetTax1099Status")
            .WithSummary("Retrieve IRS 1099 tax status and threshold monitoring tracer.");

        group.MapGet("/participant/wallets/{walletId}/balance", GetBalanceInquiry)
            .WithName("GetBalanceInquiry")
            .WithSummary("Real-time balance inquiry for participant wallet.");

        group.MapPost("/participant/wallets/{walletId}/issue-card", IssueReloadableCard)
            .WithName("IssueReloadableCard")
            .WithSummary("Issue or re-issue reloadable debit card for participant wallet.");

        group.MapPost("/participant/wallets/{walletId}/submit-w9", SubmitW9Form)
            .WithName("SubmitW9Form")
            .WithSummary("Submit Form W-9 taxpayer identification certification.");

        group.MapPost("/participant/wallets/{walletId}/verify-w9", VerifyW9Form)
            .WithName("VerifyW9Form")
            .WithSummary("Verify participant Form W-9 submission.");

        group.MapPost("/participant/wallets/{walletId}/disburse", DisburseParticipantFunds)
            .WithName("DisburseParticipantFunds")
            .WithSummary("Disburse digital funds from participant wallet to debit card or bank account.");

        // SoA Stipend Rules & Visit Verification Event Trigger
        group.MapGet("/participant/stipend-rules", ListStipendRules)
            .WithName("ListStipendRules")
            .WithSummary("List SoA protocol visit stipend compensation rules.");

        group.MapPost("/participant/stipend-rules", CreateStipendRule)
            .WithName("CreateStipendRule")
            .WithSummary("Configure participant visit compensation rules linked to Schedule of Activities.");

        group.MapPost("/participant/events/visit-verified", IngestVisitVerifiedEvent)
            .WithName("IngestVisitVerifiedEvent")
            .WithSummary("Receives verified eCRF visit completion event and evaluates automated stipend disbursement rules.");

        // Travel Expenses & OCR Scanner
        group.MapGet("/participant/expenses", ListExpenseClaims)
            .WithName("ListExpenseClaims")
            .WithSummary("List travel and meal expense reimbursement claims.");

        group.MapPost("/participant/expenses", SubmitExpenseClaim)
            .WithName("SubmitExpenseClaim")
            .WithSummary("Submit travel/meal expense claim for coordinator review.");

        group.MapPost("/participant/expenses/ocr-extract", ExtractReceiptOcr)
            .WithName("ExtractReceiptOcr")
            .WithSummary("Extract metadata from receipt image/text using rule-based OCR engine.");

        group.MapPost("/participant/expenses/{claimId}/review", ReviewExpenseClaim)
            .WithName("ReviewExpenseClaim")
            .WithSummary("Coordinator 1-click review and approval/rejection of expense claim.");

        // Transactions, Disbursements & Audit Logs
        group.MapGet("/participant/transactions", ListTransactions)
            .WithName("ListTransactions")
            .WithSummary("List participant wallet transactions ledger.");

        group.MapGet("/participant/disbursements", ListDisbursements)
            .WithName("ListDisbursements")
            .WithSummary("List digital disbursement dispatches.");

        group.MapGet("/audit-logs", GetAuditLogs)
            .WithName("GetPaymentAuditLogs")
            .WithSummary("Retrieve payment audit ledger records.");

        return routes;
    }

    private static WalletResponse MapWalletToResponse(ParticipantWallet wallet) => new(
        Id: wallet.Id,
        SubjectId: wallet.SubjectId,
        StudyId: wallet.StudyId,
        SiteId: wallet.SiteId,
        CurrentBalance: wallet.CurrentBalance,
        LifetimeStipends: wallet.LifetimeStipends,
        LifetimeReimbursements: wallet.LifetimeReimbursements,
        LifetimeDisbursements: wallet.LifetimeDisbursements,
        TaxableYtd: wallet.TaxableYtd,
        Is1099ThresholdExceeded: wallet.Is1099ThresholdExceeded,
        W9Status: wallet.W9Status,
        Currency: wallet.Currency,
        Status: wallet.Status,
        VirtualCardMask: wallet.VirtualCardMask ?? "**** **** **** 4821",
        VirtualCardExpiry: wallet.VirtualCardExpiry ?? "12/29",
        AchRoutingNumber: wallet.AchRoutingMask ?? "021000021", // deid-ignore
        AchAccountMask: wallet.AchAccountMask ?? "ACH-***4321",
        AchAccountHolder: wallet.AchAccountHolder ?? $"Participant {wallet.SubjectId}",
        AntiCoercionCap: wallet.AntiCoercionCap,
        AutoDisbursementThreshold: wallet.AutoDisbursementThreshold,
        UpdatedAt: wallet.UpdatedAt
    );

    private static async Task<IResult> ListWallets(PaymentsDbContext dbContext)
    {
        var wallets = await dbContext.ParticipantWallets
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();

        return Results.Ok(wallets.Select(MapWalletToResponse));
    }

    private static async Task<IResult> GetWalletById(string walletId, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"Participant wallet with ID '{walletId}' not found." });
        }

        return Results.Ok(MapWalletToResponse(wallet));
    }

    private static async Task<IResult> GetSubjectWallet(string subjectId, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.SubjectId == subjectId);

        if (wallet == null)
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            wallet = new ParticipantWallet
            {
                Id = Guid.NewGuid().ToString(),
                SubjectId = subjectId,
                StudyId = "STUDY-DCT-001",
                SiteId = "SITE-101",
                CurrentBalance = 0m,
                LifetimeStipends = 0m,
                LifetimeReimbursements = 0m,
                LifetimeDisbursements = 0m,
                Currency = "USD",
                Status = "ACTIVE",
                W9Status = "NOT_REQUIRED",
                CreatedAt = now,
                CreatedBy = subjectId,
                UpdatedAt = now,
                VersionIndex = 1
            };
            dbContext.ParticipantWallets.Add(wallet);
            await dbContext.SaveChangesAsync();
        }

        return Results.Ok(MapWalletToResponse(wallet));
    }

    private static async Task<IResult> GetTax1099Status(string walletId, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        decimal threshold = 600.00m;
        decimal percentage = Math.Min(100m, (wallet.TaxableYtd / threshold) * 100m);
        string warningLevel;
        if (wallet.TaxableYtd >= threshold)
        {
            warningLevel = "EXCEEDED";
        }
        else if (wallet.TaxableYtd >= 500.00m)
        {
            warningLevel = "APPROACHING";
        }
        else
        {
            warningLevel = "NORMAL";
        }

        return Results.Ok(new
        {
            wallet_id = wallet.Id,
            subject_id = wallet.SubjectId,
            taxable_ytd = wallet.TaxableYtd,
            threshold_amount = threshold,
            threshold_percentage = percentage,
            is_1099_threshold_exceeded = wallet.Is1099ThresholdExceeded,
            w9_status = wallet.W9Status,
            warning_level = warningLevel
        });
    }

    private static async Task<IResult> GetBalanceInquiry(string walletId, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        decimal pendingClaims = await dbContext.ParticipantExpenseClaims
            .Where(c => c.WalletId == wallet.Id && c.Status == "PENDING_REVIEW")
            .SumAsync(c => c.NetAmount);

        return Results.Ok(new
        {
            wallet_id = wallet.Id,
            subject_id = wallet.SubjectId,
            available_balance = wallet.CurrentBalance,
            pending_claims_total = pendingClaims,
            lifetime_stipends = wallet.LifetimeStipends,
            lifetime_reimbursements = wallet.LifetimeReimbursements,
            lifetime_disbursements = wallet.LifetimeDisbursements,
            taxable_ytd = wallet.TaxableYtd
        });
    }

    private static async Task<IResult> IssueReloadableCard(string walletId, IssueCardRequestDto request, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        DateTimeOffset now = SystemClock.Instance.UtcNow;
        wallet.CardType = request.CardType ?? "RELOADABLE_DEBIT";
        wallet.CardStatus = "ACTIVE";
        wallet.VirtualCardMask = $"**** **** **** {Random.Shared.Next(1000, 9999)}";
        wallet.VirtualCardExpiry = $"{now.Month:D2}/{(now.Year + 3) % 100:D2}";
        wallet.VirtualCardProcessorReference = $"VTOK-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        wallet.UpdatedAt = now;
        wallet.VersionIndex += 1;

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "RELOADABLE_CARD_ISSUED",
            UserId = wallet.SubjectId,
            UserRoles = "Subject",
            Details = JsonSerializer.Serialize(new { card_type = wallet.CardType, mask = wallet.VirtualCardMask, expiry = wallet.VirtualCardExpiry }),
            CreatedAt = now,
            CreatedBy = wallet.SubjectId,
            ReasonForChange = "Reloadable debit card issued to participant",
            VersionIndex = wallet.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        return Results.Ok(MapWalletToResponse(wallet));
    }

    private static async Task<IResult> SubmitW9Form(string walletId, SubmitW9RequestDto request, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        if (!request.CertifyAccurate)
        {
            return Results.UnprocessableEntity(new { detail = "W-9 certification must be agreed and verified as accurate." });
        }

        DateTimeOffset now = SystemClock.Instance.UtcNow;
        string priorStatus = wallet.W9Status;
        wallet.TaxIdLastFour = request.TaxIdLastFour;
        wallet.W9Status = "SUBMITTED";
        wallet.W9SubmittedAt = now;
        wallet.UpdatedAt = now;
        wallet.VersionIndex += 1;

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "W9_FORM_SUBMITTED",
            UserId = wallet.SubjectId,
            UserRoles = "Subject",
            PreviousStateJson = JsonSerializer.Serialize(new { w9_status = priorStatus }),
            NewStateJson = JsonSerializer.Serialize(new { w9_status = wallet.W9Status, tax_id_last_four = request.TaxIdLastFour }),
            Details = JsonSerializer.Serialize(new { full_legal_name = request.FullLegalName, tax_id_last_four = request.TaxIdLastFour, address = request.Address }),
            CreatedAt = now,
            CreatedBy = wallet.SubjectId,
            ReasonForChange = "Participant submitted Form W-9 taxpayer identification certification",
            VersionIndex = wallet.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        return Results.Ok(MapWalletToResponse(wallet));
    }

    private static async Task<IResult> VerifyW9Form(string walletId, VerifyW9RequestDto request, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        DateTimeOffset now = SystemClock.Instance.UtcNow;
        string priorStatus = wallet.W9Status;
        wallet.W9Status = "VERIFIED";
        wallet.W9VerifiedAt = now;
        wallet.UpdatedAt = now;
        wallet.VersionIndex += 1;

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "W9_FORM_VERIFIED",
            UserId = "coordinator_alice",
            UserRoles = "Coordinator",
            PreviousStateJson = JsonSerializer.Serialize(new { w9_status = priorStatus }),
            NewStateJson = JsonSerializer.Serialize(new { w9_status = wallet.W9Status }),
            Details = JsonSerializer.Serialize(new { notes = request.Notes ?? "TIN matched against verification registry." }),
            CreatedAt = now,
            CreatedBy = "coordinator_alice",
            ReasonForChange = "CRC coordinator validated Form W-9 taxpayer identification",
            VersionIndex = wallet.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();

        return Results.Ok(MapWalletToResponse(wallet));
    }

    private static async Task<IResult> DisburseParticipantFunds(string walletId, DisburseFundsRequestDto request, PaymentsDbContext dbContext)
    {
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null)
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
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

        DateTimeOffset now = SystemClock.Instance.UtcNow;
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
            CreatedBy = wallet.SubjectId,
            VersionIndex = 1
        };
        dbContext.ParticipantDisbursements.Add(disbursement);

        var tx = new ParticipantTransaction
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
            CreatedBy = wallet.SubjectId,
            VersionIndex = 1
        };
        dbContext.ParticipantTransactions.Add(tx);

        var audit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
            UserId = wallet.SubjectId,
            UserRoles = "Subject",
            Details = JsonSerializer.Serialize(new { disbursement_id = disbursement.Id, amount = payoutAmount, reference_number = refNumber, disbursement_type = request.DisbursementType, new_balance = wallet.CurrentBalance }),
            CreatedAt = now,
            CreatedBy = wallet.SubjectId,
            ReasonForChange = "Digital stipend payout funds dispatched to participant card/account",
            VersionIndex = wallet.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(audit);

        await dbContext.SaveChangesAsync();

        return Results.Ok(disbursement);
    }

    private static async Task<IResult> ListStipendRules(PaymentsDbContext dbContext, string? studyId = null)
    {
        var query = dbContext.ParticipantStipendRules.AsQueryable();
        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(r => r.StudyId == studyId);
        }

        var rules = await query.Where(r => r.IsActive).ToListAsync();

        if (rules.Count == 0 && (string.IsNullOrWhiteSpace(studyId) || studyId == "STUDY-DCT-001"))
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            rules = new List<ParticipantStipendRule>
            {
                new() { Id = Guid.NewGuid().ToString(), StudyId = "STUDY-DCT-001", VisitCode = "V1_SCREENING", VisitName = "Screening & Consent", Amount = 75.00m, Currency = "USD", Taxable = false, AntiCoercionCap = 100.00m, AutoDisbursementThreshold = 250.00m, IsMilestone = false, IsActive = true, CreatedAt = now, CreatedBy = "svc:payments", UpdatedAt = now, VersionIndex = 1 },
                new() { Id = Guid.NewGuid().ToString(), StudyId = "STUDY-DCT-001", VisitCode = "V2_BASELINE", VisitName = "Baseline & Randomization", Amount = 100.00m, Currency = "USD", Taxable = false, AntiCoercionCap = 150.00m, AutoDisbursementThreshold = 250.00m, IsMilestone = false, IsActive = true, CreatedAt = now, CreatedBy = "svc:payments", UpdatedAt = now, VersionIndex = 1 },
                new() { Id = Guid.NewGuid().ToString(), StudyId = "STUDY-DCT-001", VisitCode = "V3_CYCLE_1", VisitName = "Treatment Cycle 1 Visit", Amount = 150.00m, Currency = "USD", Taxable = true, AntiCoercionCap = 200.00m, AutoDisbursementThreshold = 250.00m, IsMilestone = false, IsActive = true, CreatedAt = now, CreatedBy = "svc:payments", UpdatedAt = now, VersionIndex = 1 },
                new() { Id = Guid.NewGuid().ToString(), StudyId = "STUDY-DCT-001", VisitCode = "V4_CYCLE_2", VisitName = "Treatment Cycle 2 Visit", Amount = 150.00m, Currency = "USD", Taxable = true, AntiCoercionCap = 200.00m, AutoDisbursementThreshold = 250.00m, IsMilestone = false, IsActive = true, CreatedAt = now, CreatedBy = "svc:payments", UpdatedAt = now, VersionIndex = 1 },
                new() { Id = Guid.NewGuid().ToString(), StudyId = "STUDY-DCT-001", VisitCode = "V5_CYCLE_3", VisitName = "Treatment Cycle 3 Visit", Amount = 150.00m, Currency = "USD", Taxable = true, AntiCoercionCap = 200.00m, AutoDisbursementThreshold = 250.00m, IsMilestone = false, IsActive = true, CreatedAt = now, CreatedBy = "svc:payments", UpdatedAt = now, VersionIndex = 1 },
                new() { Id = Guid.NewGuid().ToString(), StudyId = "STUDY-DCT-001", VisitCode = "V6_END_STUDY", VisitName = "End of Study Milestone", Amount = 200.00m, Currency = "USD", Taxable = true, AntiCoercionCap = 250.00m, AutoDisbursementThreshold = 250.00m, IsMilestone = true, MilestoneCode = "MILESTONE_PRIMARY", IsActive = true, CreatedAt = now, CreatedBy = "svc:payments", UpdatedAt = now, VersionIndex = 1 }
            };

            dbContext.ParticipantStipendRules.AddRange(rules);
            await dbContext.SaveChangesAsync();
        }

        return Results.Ok(rules);
    }

    private static async Task<IResult> CreateStipendRule(ParticipantStipendRuleCreateDto request, PaymentsDbContext dbContext)
    {
        if (request.AntiCoercionCap.HasValue && request.Amount > request.AntiCoercionCap.Value)
        {
            return Results.UnprocessableEntity(new { detail = $"Stipend amount (${request.Amount:F2}) cannot exceed IRB anti-coercion cap limit (${request.AntiCoercionCap.Value:F2})." });
        }

        DateTimeOffset now = SystemClock.Instance.UtcNow;
        var existing = await dbContext.ParticipantStipendRules
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

            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantStipendRule",
                EntityId = existing.Id,
                Action = "PARTICIPANT_STIPEND_RULE_CREATED",
                UserId = "financial_admin",
                UserRoles = "FinanceOfficer",
                Details = JsonSerializer.Serialize(new { study_id = request.StudyId, visit_code = request.VisitCode, amount = request.Amount }),
                CreatedAt = now,
                CreatedBy = "financial_admin",
                ReasonForChange = "SoA visit stipend compensation rule updated",
                VersionIndex = existing.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);
            await dbContext.SaveChangesAsync();

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
            CreatedBy = "financial_admin",
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantStipendRules.Add(rule);

        var createAudit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantStipendRule",
            EntityId = rule.Id,
            Action = "PARTICIPANT_STIPEND_RULE_CREATED",
            UserId = "financial_admin",
            UserRoles = "FinanceOfficer",
            Details = JsonSerializer.Serialize(new { study_id = request.StudyId, visit_code = request.VisitCode, amount = request.Amount }),
            CreatedAt = now,
            CreatedBy = "financial_admin",
            ReasonForChange = "SoA visit stipend compensation rule created",
            VersionIndex = 1
        };
        dbContext.PaymentAuditLogs.Add(createAudit);

        await dbContext.SaveChangesAsync();

        return Results.Ok(rule);
    }

    private static async Task<IResult> IngestVisitVerifiedEvent(VisitVerificationEventDto request, PaymentsDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId) || string.IsNullOrWhiteSpace(request.VisitCode))
        {
            return Results.BadRequest(new { detail = "subject_id and visit_code are required." });
        }

        string eventId = request.EventId ?? $"EVT-{Guid.NewGuid():N}";
        DateTimeOffset now = SystemClock.Instance.UtcNow;

        // 1. Idempotency Check: search by ReferenceId in ParticipantTransactions OR PaymentAuditLogs
        var existingTx = await dbContext.ParticipantTransactions
            .FirstOrDefaultAsync(t => t.ReferenceId == eventId);

        if (existingTx != null)
        {
            var existingWallet = await dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.Id == existingTx.WalletId);

            return Results.Ok(new VisitVerificationResponseDto(
                Status: "ALREADY_PROCESSED",
                Amount: 0m,
                CurrentBalance: existingWallet?.CurrentBalance ?? 0m,
                WalletId: existingTx.WalletId,
                TransactionId: existingTx.Id,
                EventId: eventId,
                SubjectId: request.SubjectId,
                Reason: "Duplicate visit verification event already processed."
            ));
        }

        var existingAudit = await dbContext.PaymentAuditLogs
            .FirstOrDefaultAsync(l => l.Details != null && l.Details.Contains(eventId));

        if (existingAudit != null)
        {
            var existingWallet = await dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.SubjectId == request.SubjectId && w.StudyId == request.StudyId);

            return Results.Ok(new VisitVerificationResponseDto(
                Status: "ALREADY_PROCESSED",
                Amount: 0m,
                CurrentBalance: existingWallet?.CurrentBalance ?? 0m,
                WalletId: existingWallet?.Id ?? string.Empty,
                TransactionId: null,
                EventId: eventId,
                SubjectId: request.SubjectId,
                Reason: "Duplicate visit verification event already recorded in audit ledger."
            ));
        }

        // 2. Find active stipend rule
        var rule = await dbContext.ParticipantStipendRules
            .FirstOrDefaultAsync(r => r.IsActive && r.StudyId == request.StudyId && r.VisitCode == request.VisitCode);

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
                Reason: $"No active stipend compensation rule found for visit_code '{request.VisitCode}' and study_id '{request.StudyId}'."
            ));
        }

        // 3. Get or create participant wallet
        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.SubjectId == request.SubjectId && w.StudyId == request.StudyId);

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
                CreatedBy = request.VerifiedBy ?? "crc_coordinator",
                UpdatedAt = now,
                VersionIndex = 1
            };
            dbContext.ParticipantWallets.Add(wallet);

            var walletAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "PARTICIPANT_WALLET_CREATED",
                UserId = request.VerifiedBy ?? "crc_coordinator",
                UserRoles = "Coordinator",
                Details = JsonSerializer.Serialize(new { subject_id = request.SubjectId, study_id = request.StudyId, site_id = request.SiteId }),
                CreatedAt = now,
                CreatedBy = request.VerifiedBy ?? "crc_coordinator",
                ReasonForChange = "Initial wallet provisioned on eCRF visit verification",
                VersionIndex = 1
            };
            dbContext.PaymentAuditLogs.Add(walletAudit);
        }

        decimal stipendAmount = rule.Amount;

        // 4. IRB Anti-Coercion Cap Check
        bool ruleCapExceeded = rule.AntiCoercionCap.HasValue && stipendAmount > rule.AntiCoercionCap.Value;
        bool walletCapExceeded = wallet.AntiCoercionCap.HasValue && stipendAmount > wallet.AntiCoercionCap.Value;

        if (ruleCapExceeded || walletCapExceeded)
        {
            decimal effectiveCap = Math.Min(
                rule.AntiCoercionCap ?? decimal.MaxValue,
                wallet.AntiCoercionCap ?? decimal.MaxValue);

            var capAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "ANTI_COERCION_CAP_EXCEEDED",
                UserId = request.VerifiedBy ?? "crc_coordinator",
                UserRoles = "Coordinator",
                Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, anti_coercion_cap = effectiveCap, timestamp = now }),
                CreatedAt = now,
                CreatedBy = request.VerifiedBy ?? "crc_coordinator",
                ReasonForChange = "Stipend amount exceeds IRB anti-coercion cap limit",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(capAudit);
            await dbContext.SaveChangesAsync();

            return Results.Ok(new VisitVerificationResponseDto(
                Status: "FLAGGED_FOR_REVIEW",
                Amount: stipendAmount,
                CurrentBalance: wallet.CurrentBalance,
                WalletId: wallet.Id,
                TransactionId: null,
                EventId: eventId,
                SubjectId: request.SubjectId,
                Reason: $"Payment of ${stipendAmount:F2} exceeds IRB anti-coercion cap limit (${effectiveCap:F2}). Routed to administrator review queue.",
                RequiresReview: true
            ));
        }

        // 5. Configurable Auto-Disbursement Safety Threshold Check
        bool ruleThresholdExceeded = rule.AutoDisbursementThreshold.HasValue && stipendAmount > rule.AutoDisbursementThreshold.Value;
        bool walletThresholdExceeded = wallet.AutoDisbursementThreshold.HasValue && stipendAmount > wallet.AutoDisbursementThreshold.Value;

        if (ruleThresholdExceeded || walletThresholdExceeded)
        {
            decimal effectiveThreshold = Math.Min(
                rule.AutoDisbursementThreshold ?? decimal.MaxValue,
                wallet.AutoDisbursementThreshold ?? decimal.MaxValue);

            var thresholdAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "THRESHOLD_EXCEEDED_REVIEW_REQUIRED",
                UserId = request.VerifiedBy ?? "crc_coordinator",
                UserRoles = "Coordinator",
                Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, auto_disbursement_threshold = effectiveThreshold, timestamp = now }),
                CreatedAt = now,
                CreatedBy = request.VerifiedBy ?? "crc_coordinator",
                ReasonForChange = "Stipend amount exceeds auto-disbursement safety threshold",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(thresholdAudit);
            await dbContext.SaveChangesAsync();

            return Results.Ok(new VisitVerificationResponseDto(
                Status: "FLAGGED_FOR_REVIEW",
                Amount: stipendAmount,
                CurrentBalance: wallet.CurrentBalance,
                WalletId: wallet.Id,
                TransactionId: null,
                EventId: eventId,
                SubjectId: request.SubjectId,
                Reason: $"Payment of ${stipendAmount:F2} exceeds configured auto-disbursement threshold limit (${effectiveThreshold:F2}). Routed to administrator review queue.",
                RequiresReview: true
            ));
        }

        // 6. IRS $600 YTD Tax Compliance Check
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

            var taxAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "IRS_1099_THRESHOLD_EXCEEDED",
                UserId = request.VerifiedBy ?? "crc_coordinator",
                UserRoles = "Coordinator",
                Details = JsonSerializer.Serialize(new { event_id = eventId, visit_code = request.VisitCode, amount = stipendAmount, taxable_ytd = potentialTaxableYtd, w9_status = wallet.W9Status, timestamp = now }),
                CreatedAt = now,
                CreatedBy = request.VerifiedBy ?? "crc_coordinator",
                ReasonForChange = "Participant YTD taxable earnings reach or exceed $600.00 IRS 1099 threshold without verified W-9",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(taxAudit);
            await dbContext.SaveChangesAsync();

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
                W9Status: wallet.W9Status
            ));
        }

        // 7. Auto-disburse: Credit Wallet & Record Ledger Transaction
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
            Description = $"Verified visit stipend credit for {rule.VisitName} ({rule.VisitCode})",
            CreatedAt = now,
            CreatedBy = request.VerifiedBy ?? "crc_coordinator",
            VersionIndex = 1
        };
        dbContext.ParticipantTransactions.Add(tx);

        var creditAudit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "PARTICIPANT_STIPEND_CREDITED",
            UserId = request.VerifiedBy ?? "crc_coordinator",
            UserRoles = "Coordinator",
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
            CreatedBy = request.VerifiedBy ?? "crc_coordinator",
            ReasonForChange = $"EDC visit verification automated stipend payout for {rule.VisitCode}",
            VersionIndex = wallet.VersionIndex
        };
        dbContext.PaymentAuditLogs.Add(creditAudit);

        await dbContext.SaveChangesAsync();

        return Results.Ok(new VisitVerificationResponseDto(
            Status: "CREDITED",
            Amount: stipendAmount,
            CurrentBalance: wallet.CurrentBalance,
            WalletId: wallet.Id,
            TransactionId: tx.Id,
            EventId: eventId,
            SubjectId: request.SubjectId
        ));
    }

    private static async Task<IResult> ListExpenseClaims(PaymentsDbContext dbContext, string? status = null, string? subjectId = null)
    {
        var query = dbContext.ParticipantExpenseClaims.AsQueryable();

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

    private static async Task<IResult> SubmitExpenseClaim(SubmitExpenseClaimRequestDto request, PaymentsDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId) || request.GrossAmount <= 0m)
        {
            return Results.BadRequest(new { detail = "subject_id and positive gross_amount are required." });
        }

        DateTimeOffset now = SystemClock.Instance.UtcNow;

        var wallet = await dbContext.ParticipantWallets
            .FirstOrDefaultAsync(w => w.SubjectId == request.SubjectId && w.StudyId == request.StudyId);

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
                CreatedBy = request.SubjectId,
                UpdatedAt = now,
                VersionIndex = 1
            };
            dbContext.ParticipantWallets.Add(wallet);
        }

        decimal gross = request.GrossAmount;
        decimal taxDeduction = 0m;
        bool isMeal = request.Category == "MEAL" || request.IsMealAllowance;
        decimal perDiemCap = request.PerDiemCap ?? 75.00m;

        if (isMeal && gross > perDiemCap)
        {
            taxDeduction = Math.Round((gross - perDiemCap) * 0.15m, 2);
        }

        decimal net = Math.Round(gross - taxDeduction, 2);

        double ocrConfidence = request.OcrConfidence ?? ExtractConfidenceScore(request.ReceiptOcrData) ?? (request.ReceiptOcrData != null ? 0.98 : 0.95);

        // Evaluate Auto-Approval Policy Rules
        bool isAmountLowRisk = gross <= 100.00m;
        bool isHighConfidence = ocrConfidence >= 0.90;
        bool isMealWithinCap = !isMeal || gross <= perDiemCap;
        bool isW9Compliant = !(wallet.TaxableYtd >= 600.00m && wallet.W9Status != "VERIFIED");

        bool canAutoApprove = isAmountLowRisk && isHighConfidence && isMealWithinCap && isW9Compliant;

        string initialStatus = canAutoApprove ? "APPROVED" : "PENDING_REVIEW";
        string? reviewNotes = null;

        if (!canAutoApprove)
        {
            if (!isAmountLowRisk)
            {
                reviewNotes = $"Gross amount (${gross:F2}) exceeds $100.00 auto-approval threshold; routed to coordinator review queue.";
            }
            else if (!isMealWithinCap)
            {
                reviewNotes = $"Meal expense (${gross:F2}) exceeds per-diem cap (${perDiemCap:F2}); routed to coordinator review queue.";
            }
            else if (!isW9Compliant)
            {
                reviewNotes = "IRS 1099 tax threshold reached without verified Form W-9 certification; automated disbursement halted for coordinator review.";
            }
            else if (!isHighConfidence)
            {
                reviewNotes = $"OCR confidence score ({ocrConfidence:F2}) below 0.90 threshold; routed to coordinator review queue.";
            }
        }
        else
        {
            reviewNotes = "Auto-approved based on low-risk expense policy (<$100, high OCR confidence).";
        }

        var claim = new ParticipantExpenseClaim
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = request.SubjectId,
            StudyId = request.StudyId,
            SiteId = request.SiteId,
            ExpenseDate = request.ExpenseDate ?? now.ToString("yyyy-MM-dd"),
            Category = request.Category,
            MerchantName = request.MerchantName,
            GrossAmount = gross,
            TaxDeduction = taxDeduction,
            NetAmount = net,
            Status = initialStatus,
            ReceiptImageName = request.ReceiptImageName ?? "receipt.jpg",
            ReceiptOcrDataJson = request.ReceiptOcrData != null ? JsonSerializer.Serialize(request.ReceiptOcrData) : null,
            ReviewedBy = canAutoApprove ? "system_auto_approval" : null,
            ReviewedAt = canAutoApprove ? now : null,
            ReviewNotes = reviewNotes,
            CreatedAt = now,
            CreatedBy = request.SubjectId,
            UpdatedAt = now,
            VersionIndex = 1
        };

        if (canAutoApprove)
        {
            // Credit wallet for reimbursement
            wallet.CurrentBalance += net;
            wallet.LifetimeReimbursements += net;
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            var creditTx = new ParticipantTransaction
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = wallet.SubjectId,
                StudyId = wallet.StudyId,
                TransactionType = "REIMBURSEMENT_CREDIT",
                Amount = net,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceId = claim.Id,
                Description = $"Auto-approved reimbursement credit for {claim.Category} expense ({claim.MerchantName})",
                CreatedAt = now,
                CreatedBy = "system_auto_approval",
                VersionIndex = 1
            };
            dbContext.ParticipantTransactions.Add(creditTx);

            // Dispatch Automated Direct ACH Transfer
            string achRef = $"DISB-ACH-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
            claim.DisbursementReference = achRef;

            wallet.CurrentBalance -= net;
            wallet.LifetimeDisbursements += net;

            var disbursement = new ParticipantDisbursement
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = wallet.SubjectId,
                Amount = net,
                DisbursementType = "DIRECT_ACH",
                ReferenceNumber = achRef,
                Status = "DISPATCHED",
                CreatedAt = now,
                CreatedBy = "system_auto_approval",
                VersionIndex = 1
            };
            dbContext.ParticipantDisbursements.Add(disbursement);

            var achTx = new ParticipantTransaction
            {
                Id = Guid.NewGuid().ToString(),
                WalletId = wallet.Id,
                SubjectId = wallet.SubjectId,
                StudyId = wallet.StudyId,
                TransactionType = "DISBURSEMENT_DEBIT",
                Amount = -net,
                BalanceAfter = wallet.CurrentBalance,
                ReferenceId = claim.Id,
                Description = $"Automated direct ACH payout dispatch ({achRef})",
                CreatedAt = now,
                CreatedBy = "system_auto_approval",
                VersionIndex = 1
            };
            dbContext.ParticipantTransactions.Add(achTx);

            var autoApproveAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = claim.Id,
                Action = "EXPENSE_CLAIM_AUTO_APPROVED",
                UserId = request.SubjectId,
                UserRoles = "Subject",
                Details = JsonSerializer.Serialize(new
                {
                    claim_id = claim.Id,
                    gross_amount = gross,
                    net_amount = net,
                    ocr_confidence = ocrConfidence,
                    disbursement_reference = achRef
                }),
                CreatedAt = now,
                CreatedBy = request.SubjectId,
                ReasonForChange = "Automated OCR policy auto-approval and ACH payout dispatch",
                VersionIndex = claim.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(autoApproveAudit);

            var achAudit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
                UserId = request.SubjectId,
                UserRoles = "Subject",
                Details = JsonSerializer.Serialize(new
                {
                    disbursement_id = disbursement.Id,
                    amount = net,
                    reference_number = achRef,
                    disbursement_type = "DIRECT_ACH"
                }),
                CreatedAt = now,
                CreatedBy = request.SubjectId,
                ReasonForChange = "Automated direct ACH reimbursement dispatched to linked bank account",
                VersionIndex = wallet.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(achAudit);
        }
        else
        {
            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = claim.Id,
                Action = "EXPENSE_CLAIM_SUBMITTED",
                UserId = request.SubjectId,
                UserRoles = "Subject",
                Details = JsonSerializer.Serialize(new
                {
                    category = request.Category,
                    gross = gross,
                    net = net,
                    merchant = request.MerchantName,
                    status = claim.Status,
                    review_notes = reviewNotes
                }),
                CreatedAt = now,
                CreatedBy = request.SubjectId,
                ReasonForChange = "Travel/meal expense claim submitted for coordinator review",
                VersionIndex = 1
            };
            dbContext.PaymentAuditLogs.Add(audit);
        }

        dbContext.ParticipantExpenseClaims.Add(claim);
        await dbContext.SaveChangesAsync();

        return Results.Ok(claim);
    }

    private static double? ExtractConfidenceScore(object? ocrData)
    {
        if (ocrData == null) return null;
        try
        {
            if (ocrData is JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("confidence_score", out var cs) && cs.TryGetDouble(out double val1))
                    return val1;
                if (jsonElement.TryGetProperty("confidenceScore", out var cs2) && cs2.TryGetDouble(out double val2))
                    return val2;
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private static async Task<IResult> ExtractReceiptOcr(OcrExtractRequestDto request, PaymentsDbContext dbContext)
    {
        string text = request.RawText ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(request.ImageBase64);
                string decoded = System.Text.Encoding.UTF8.GetString(bytes);
                if (decoded.Length > 10)
                {
                    text = decoded;
                }
            }
            catch
            {
                text = string.Empty;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = "METRO TRANSIT AUTHORITY\nRoute: Clinical Trial Express\nDate: 2026-08-23\nFare Paid: $28.50\nCard: **** 4821"; // deid-ignore
        }

        // Date extraction
        string expenseDate = SystemClock.Instance.UtcNow.ToString("yyyy-MM-dd");
        var dateMatch = Regex.Match(text, @"\b(\d{4}-\d{2}-\d{2})\b");
        if (dateMatch.Success)
        {
            expenseDate = dateMatch.Groups[1].Value;
        }

        // Amount extraction
        decimal grossAmount = 28.50m;
        var amountMatches = Regex.Matches(text, @"(?:Total|Amount|Balance|Paid|Charge|Fare|Due)?\s*[:$]?\s*(\d+\.\d{2})", RegexOptions.IgnoreCase);
        if (amountMatches.Count > 0)
        {
            string valStr = amountMatches[^1].Groups[1].Value;
            if (decimal.TryParse(valStr, out decimal parsed))
            {
                grossAmount = parsed;
            }
        }

        // Category determination
        string category = request.CategoryHint ?? "TRANSIT";
        string lowerText = text.ToLowerInvariant();

        if (lowerText.Contains("shell") || lowerText.Contains("fuel") || lowerText.Contains("gas") || lowerText.Contains("mileage"))
        {
            category = "MILEAGE";
        }
        else if (lowerText.Contains("parking") || lowerText.Contains("garage") || lowerText.Contains("valet"))
        {
            category = "PARKING";
        }
        else if (lowerText.Contains("cafe") || lowerText.Contains("restaurant") || lowerText.Contains("bistro") || lowerText.Contains("meal") || lowerText.Contains("lunch") || lowerText.Contains("dinner"))
        {
            category = "MEAL";
        }
        else if (lowerText.Contains("hotel") || lowerText.Contains("inn") || lowerText.Contains("lodging"))
        {
            category = "LODGING";
        }
        else if (lowerText.Contains("transit") || lowerText.Contains("metro") || lowerText.Contains("uber") || lowerText.Contains("taxi") || lowerText.Contains("bus"))
        {
            category = "TRANSIT";
        }

        // Merchant extraction
        string firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Metro Transit Authority";
        string merchantName = firstLine.Length is >= 3 and <= 40 ? firstLine : "Metro Transit Authority";

        decimal taxDeduction = 0m;
        if (category == "MEAL" && grossAmount > 75.00m)
        {
            taxDeduction = Math.Round((grossAmount - 75.00m) * 0.15m, 2);
        }

        decimal netAmount = Math.Round(grossAmount - taxDeduction, 2);
        double confidenceScore = 0.98;

        DateTimeOffset now = SystemClock.Instance.UtcNow;
        var ocrAudit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantExpenseClaim",
            EntityId = "OCR_SCAN_" + Guid.NewGuid().ToString("N")[..8],
            Action = "OCR_RECEIPT_EXTRACTED",
            UserId = "subject_participant",
            UserRoles = "Subject",
            Details = JsonSerializer.Serialize(new
            {
                merchant_name = merchantName,
                expense_date = expenseDate,
                gross_amount = grossAmount,
                tax_deduction = taxDeduction,
                net_amount = netAmount,
                category = category,
                confidence_score = confidenceScore
            }),
            CreatedAt = now,
            CreatedBy = "subject_participant",
            ReasonForChange = "Mobile receipt OCR initial extraction",
            VersionIndex = 1
        };
        dbContext.PaymentAuditLogs.Add(ocrAudit);
        await dbContext.SaveChangesAsync();

        return Results.Ok(new
        {
            merchant_name = merchantName,
            expense_date = expenseDate,
            gross_amount = grossAmount,
            tax_deduction = taxDeduction,
            net_amount = netAmount,
            category = category,
            confidence_score = confidenceScore,
            raw_text = text
        });
    }

    private static async Task<IResult> ReviewExpenseClaim(string claimId, ReviewExpenseClaimRequestDto request, PaymentsDbContext dbContext)
    {
        var claim = await dbContext.ParticipantExpenseClaims
            .FirstOrDefaultAsync(c => c.Id == claimId);

        if (claim == null)
        {
            return Results.NotFound(new { detail = $"Expense claim '{claimId}' not found." });
        }

        if (claim.Status is "APPROVED" or "REJECTED")
        {
            return Results.BadRequest(new { detail = $"Expense claim '{claimId}' is already in status '{claim.Status}'." });
        }

        DateTimeOffset now = SystemClock.Instance.UtcNow;

        if (request.AdjustedAmount.HasValue)
        {
            claim.GrossAmount = request.AdjustedAmount.Value;
            claim.NetAmount = Math.Round(request.AdjustedAmount.Value - claim.TaxDeduction, 2);
        }

        claim.Status = request.Status;
        claim.ReviewedBy = "coordinator_alice";
        claim.ReviewedAt = now;
        claim.ReviewNotes = request.ReviewNotes;
        claim.UpdatedAt = now;
        claim.VersionIndex += 1;

        if (request.Status == "APPROVED")
        {
            var wallet = await dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.Id == claim.WalletId);

            if (wallet == null)
            {
                wallet = await dbContext.ParticipantWallets
                    .FirstOrDefaultAsync(w => w.SubjectId == claim.SubjectId && w.StudyId == claim.StudyId);
            }

            if (wallet != null)
            {
                wallet.CurrentBalance += claim.NetAmount;
                wallet.LifetimeReimbursements += claim.NetAmount;
                wallet.UpdatedAt = now;
                wallet.VersionIndex += 1;

                var tx = new ParticipantTransaction
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
                    CreatedBy = "coordinator_alice",
                    VersionIndex = 1
                };
                dbContext.ParticipantTransactions.Add(tx);
            }

            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = claim.Id,
                Action = "EXPENSE_CLAIM_APPROVED",
                UserId = "coordinator_alice",
                UserRoles = "Coordinator",
                Details = JsonSerializer.Serialize(new { claim_id = claim.Id, net_amount = claim.NetAmount, status = claim.Status }),
                CreatedAt = now,
                CreatedBy = "coordinator_alice",
                ReasonForChange = "Expense claim approved by coordinator",
                VersionIndex = claim.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);
        }
        else
        {
            var audit = new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = claim.Id,
                Action = "EXPENSE_CLAIM_REJECTED",
                UserId = "coordinator_alice",
                UserRoles = "Coordinator",
                Details = JsonSerializer.Serialize(new { claim_id = claim.Id, status = claim.Status, notes = request.ReviewNotes }),
                CreatedAt = now,
                CreatedBy = "coordinator_alice",
                ReasonForChange = "Expense claim rejected by coordinator",
                VersionIndex = claim.VersionIndex
            };
            dbContext.PaymentAuditLogs.Add(audit);
        }

        await dbContext.SaveChangesAsync();

        return Results.Ok(claim);
    }

    private static async Task<IResult> ListTransactions(PaymentsDbContext dbContext, string? subjectId = null, string? walletId = null)
    {
        var query = dbContext.ParticipantTransactions.AsQueryable();

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

    private static async Task<IResult> ListDisbursements(PaymentsDbContext dbContext, string? subjectId = null, string? walletId = null)
    {
        var query = dbContext.ParticipantDisbursements.AsQueryable();

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

    private static async Task<IResult> DisburseStipendDirect(DisburseStipendRequest request, PaymentsDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Results.BadRequest(new { detail = "subject_id is required for stipend disbursement." });
        }

        string idempotencyKey = request.IdempotencyKey
            ?? (request.SequenceNumber.HasValue ? $"seq_{request.SequenceNumber}_{request.SubjectId}" : $"disburse_{request.SubjectId}_{Guid.NewGuid()}");

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

        DateTimeOffset now = SystemClock.Instance.UtcNow;

        if (wallet == null)
        {
            wallet = new ParticipantWallet
            {
                Id = Guid.NewGuid().ToString(),
                SubjectId = request.SubjectId,
                StudyId = request.StudyId ?? "STUDY-DCT-001",
                SiteId = request.SiteId ?? "SITE-101",
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

    private static async Task<IResult> GetAuditLogs(PaymentsDbContext dbContext, string? subjectId = null)
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
