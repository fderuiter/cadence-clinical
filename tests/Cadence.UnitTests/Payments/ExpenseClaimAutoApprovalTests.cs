using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public class ExpenseClaimAutoApprovalTests
{
    // The site coordinator submitting for the participant, as the gateway signs them (#5361).
    private static readonly ClaimsPrincipal SiteCoordinator = TestSecurityContext.CreatePrincipal("crc-1", "crc");

    private static string NewTempDbPath() => Path.Combine(Path.GetTempPath(), $"payments-ocr-test-{Guid.NewGuid():N}.db");

    private static DbContextOptions<PaymentsDbContext> FileOptions(string path) =>
        new DbContextOptionsBuilder<PaymentsDbContext>().UseSqlite($"Data Source={path}").Options;

    private static async Task<PaymentsDbContext> CreateDbContextAsync(string path)
    {
        var dbContext = new PaymentsDbContext(FileOptions(path), DesignTimeFieldEncryptor.Instance);
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        return dbContext;
    }

    /// <summary>
    /// Receipt OCR extraction parses merchant, date, amount, category and records audit log.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task ExtractReceiptOcr_ParsesMetadataAndRecordsAudit()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var req = new OcrExtractRequestDto(
            RawText: "METRO TRANSIT AUTHORITY\nRoute: Clinical Trial Express\nDate: 2026-08-23\nFare Paid: $28.50",
            CategoryHint: "TRANSIT"
        );

        // Act
        var result = await (Task<Microsoft.AspNetCore.Http.IResult>)typeof(PaymentEndpoints)
            .GetMethod("ExtractReceiptOcr", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [req, dbContext, SiteCoordinator])!;

        // Assert
        result.Should().NotBeNull();
        result.GetType().Name.Should().StartWith("Ok");

        var valueProp = result.GetType().GetProperty("Value");
        valueProp.Should().NotBeNull();
        var value = valueProp!.GetValue(result);
        value.Should().NotBeNull();

        var json = System.Text.Json.JsonSerializer.Serialize(value);
        json.Should().ContainEquivalentOf("Metro Transit Authority");
        json.Should().Contain("2026-08-23");
        json.Should().Contain("28.5");
        json.Should().Contain("TRANSIT");

        var audit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "OCR_RECEIPT_EXTRACTED", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();

        File.Delete(dbPath);
    }

    /// <summary>
    /// Routine claim under $100 with high confidence is auto-approved, credits wallet, and dispatches direct ACH payout.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    [GxPRequirement("PRD-SYS-182")]
    [GxPRequirement("PRD-PAY-006")]
    public async Task SubmitExpenseClaim_Under100WithHighConfidence_AutoApprovesAndDispatchesACH()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var now = SystemClock.Instance.UtcNow;
        var wallet = new ParticipantWallet
        {
            Id = "wallet-subj-01",
            SubjectId = "SUBJ-TRANSIT-01",
            StudyId = "STUDY-DCT-001",
            SiteId = "SITE-101",
            CurrentBalance = 50.00m,
            LifetimeStipends = 50.00m,
            LifetimeReimbursements = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            W9Status = "NOT_REQUIRED",
            CreatedAt = now,
            CreatedBy = "system",
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantWallets.Add(wallet);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var claimReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-TRANSIT-01",
            StudyId: "STUDY-DCT-001",
            SiteId: "SITE-101",
            Category: "TRANSIT",
            MerchantName: "Metro Transit Authority",
            GrossAmount: 28.50m,
            ExpenseDate: "2026-08-23",
            OcrConfidence: 0.98
        );

        // Act
        var result = await (Task<Microsoft.AspNetCore.Http.IResult>)typeof(PaymentEndpoints)
            .GetMethod("SubmitExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [claimReq, dbContext, SiteCoordinator])!;

        // Assert
        result.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var okResult = (Ok<ParticipantExpenseClaim>)result;
        var claim = okResult.Value!;

        claim.Status.Should().Be("APPROVED");
        claim.ReviewedBy.Should().Be("svc:system_auto_approval");
        claim.DisbursementReference.Should().StartWith("DISB-ACH-");

        var updatedWallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.Id == "wallet-subj-01", TestContext.Current.CancellationToken);
        updatedWallet!.LifetimeReimbursements.Should().Be(28.50m);
        updatedWallet.LifetimeDisbursements.Should().Be(28.50m);

        var creditTx = await dbContext.ParticipantTransactions.FirstOrDefaultAsync(t => t.TransactionType == "REIMBURSEMENT_CREDIT" && t.ReferenceId == claim.Id, TestContext.Current.CancellationToken);
        creditTx.Should().NotBeNull();
        creditTx!.Amount.Should().Be(28.50m);

        var achTx = await dbContext.ParticipantTransactions.FirstOrDefaultAsync(t => t.TransactionType == "DISBURSEMENT_DEBIT" && t.ReferenceId == claim.Id, TestContext.Current.CancellationToken);
        achTx.Should().NotBeNull();
        achTx!.Amount.Should().Be(-28.50m);

        var disb = await dbContext.ParticipantDisbursements.FirstOrDefaultAsync(d => d.ReferenceNumber == claim.DisbursementReference, TestContext.Current.CancellationToken);
        disb.Should().NotBeNull();
        disb!.DisbursementType.Should().Be("DIRECT_ACH");
        disb.Amount.Should().Be(28.50m);

        var autoApproveAudit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "EXPENSE_CLAIM_AUTO_APPROVED", TestContext.Current.CancellationToken);
        autoApproveAudit.Should().NotBeNull();

        File.Delete(dbPath);
    }

    /// <summary>
    /// Expense claim exceeding $100 threshold is routed to coordinator review queue.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task SubmitExpenseClaim_Exceeding100Threshold_RoutesToPendingReview()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var claimReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-LODGING-01",
            StudyId: "STUDY-DCT-001",
            SiteId: "SITE-101",
            Category: "LODGING",
            MerchantName: "Marriott Hotel",
            GrossAmount: 185.00m,
            ExpenseDate: "2026-08-23",
            OcrConfidence: 0.98
        );

        // Act
        var result = await (Task<Microsoft.AspNetCore.Http.IResult>)typeof(PaymentEndpoints)
            .GetMethod("SubmitExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [claimReq, dbContext, SiteCoordinator])!;

        // Assert
        result.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var claim = ((Ok<ParticipantExpenseClaim>)result).Value!;

        claim.Status.Should().Be("PENDING_REVIEW");
        claim.DisbursementReference.Should().BeNull();
        claim.ReviewNotes.Should().Contain("exceeds $100.00 auto-approval threshold");

        var audit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "EXPENSE_CLAIM_SUBMITTED", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();

        File.Delete(dbPath);
    }

    /// <summary>
    /// Meal expense surpassing $75 per-diem cap is routed to coordinator review queue.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task SubmitExpenseClaim_MealSurpassingPerDiemCap_RoutesToPendingReview()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var claimReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-MEAL-01",
            StudyId: "STUDY-DCT-001",
            SiteId: "SITE-101",
            Category: "MEAL",
            MerchantName: "Bistro 101",
            GrossAmount: 95.00m,
            IsMealAllowance: true,
            PerDiemCap: 75.00m,
            ExpenseDate: "2026-08-23",
            OcrConfidence: 0.98
        );

        // Act
        var result = await (Task<Microsoft.AspNetCore.Http.IResult>)typeof(PaymentEndpoints)
            .GetMethod("SubmitExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [claimReq, dbContext, SiteCoordinator])!;

        // Assert
        result.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var claim = ((Ok<ParticipantExpenseClaim>)result).Value!;

        claim.Status.Should().Be("PENDING_REVIEW");
        claim.ReviewNotes.Should().Contain("exceeds per-diem cap");

        File.Delete(dbPath);
    }

    /// <summary>
    /// Expense claim when taxable YTD >= $600 without verified W-9 halts auto-disbursement for coordinator review.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-182")]
    public async Task SubmitExpenseClaim_UnverifiedW9Over600Ytd_RoutesToPendingReview()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var now = SystemClock.Instance.UtcNow;
        var wallet = new ParticipantWallet
        {
            Id = "wallet-subj-w9",
            SubjectId = "SUBJ-W9-01",
            StudyId = "STUDY-DCT-001",
            SiteId = "SITE-101",
            CurrentBalance = 100.00m,
            LifetimeStipends = 600.00m,
            TaxableYtd = 600.00m,
            Is1099ThresholdExceeded = true,
            W9Status = "SUBMITTED", // Not yet VERIFIED!
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = "system",
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantWallets.Add(wallet);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var claimReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-W9-01",
            StudyId: "STUDY-DCT-001",
            SiteId: "SITE-101",
            Category: "TRANSIT",
            MerchantName: "City Cab",
            GrossAmount: 45.00m,
            ExpenseDate: "2026-08-23",
            OcrConfidence: 0.98
        );

        // Act
        var result = await (Task<Microsoft.AspNetCore.Http.IResult>)typeof(PaymentEndpoints)
            .GetMethod("SubmitExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [claimReq, dbContext, SiteCoordinator])!;

        // Assert
        result.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var claim = ((Ok<ParticipantExpenseClaim>)result).Value!;

        claim.Status.Should().Be("PENDING_REVIEW");
        claim.ReviewNotes.Should().Contain("verified Form W-9 certification");

        File.Delete(dbPath);
    }
}
