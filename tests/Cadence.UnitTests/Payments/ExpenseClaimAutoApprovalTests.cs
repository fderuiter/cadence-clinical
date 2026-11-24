using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public class ExpenseClaimAutoApprovalTests
{
    // The site coordinator submitting for the participant, as the gateway signs them (#5361).
    private static readonly ClaimsPrincipal SiteCoordinator = TestSecurityContext.CreateScopedPrincipal(
        "crc-1", TestSecurityContext.AllStudies, ["SITE-101"], "crc");

    private static readonly ClaimsPrincipal SiteInvestigator = TestSecurityContext.CreateScopedPrincipal(
        "pi-1", TestSecurityContext.AllStudies, ["SITE-101"], "investigator");

    private static HttpContext SiteCoordinatorContext() => new DefaultHttpContext { User = SiteCoordinator };

    private static HttpContext SiteInvestigatorContext() => new DefaultHttpContext { User = SiteInvestigator };

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
            .Invoke(null, [req, dbContext, SiteCoordinatorContext()])!;

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

        byte[] transitPng = ReceiptOcrEngine.RenderReceiptPng(
        [
            "METRO TRANSIT AUTHORITY",
            "DATE 2026-08-23",
            "TOTAL $28.50"
        ]);
        var claimReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-TRANSIT-01",
            StudyId: "STUDY-DCT-001",
            SiteId: "SITE-101",
            Category: "TRANSIT",
            MerchantName: "METRO TRANSIT AUTHORITY",
            GrossAmount: 28.50m,
            ExpenseDate: "2026-08-23",
            ReceiptImageBase64: Convert.ToBase64String(transitPng)
        );

        // Act
        var result = await (Task<Microsoft.AspNetCore.Http.IResult>)typeof(PaymentEndpoints)
            .GetMethod("SubmitExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [claimReq, dbContext, SiteCoordinatorContext()])!;

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
            .Invoke(null, [claimReq, dbContext, SiteCoordinatorContext()])!;

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
            .Invoke(null, [claimReq, dbContext, SiteCoordinatorContext()])!;

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
            .Invoke(null, [claimReq, dbContext, SiteCoordinatorContext()])!;

        // Assert
        result.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var claim = ((Ok<ParticipantExpenseClaim>)result).Value!;

        claim.Status.Should().Be("PENDING_REVIEW");
        claim.ReviewNotes.Should().Contain("verified Form W-9 certification");

        File.Delete(dbPath);
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    [GxPRequirement("PRD-SYS-182")]
    public async Task SubmitExpenseClaim_UnscannedStaffMealWithSpoofedPerDiemCap_HoldsForReviewWithServerTaxDeduction()
    {
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var submitMethod = typeof(PaymentEndpoints)
            .GetMethod("SubmitExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Staff submits unscanned $95.00 MEAL claim with client perDiemCap=100.00 and spoofed OcrConfidence -> must route to PENDING_REVIEW with $3.00 taxDeduction
        var unscannedMealReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-VERIFY-02",
            StudyId: "STUDY-01",
            SiteId: "SITE-101",
            Category: "MEAL",
            MerchantName: "Staff Unscanned Meal",
            GrossAmount: 95.00m,
            IsMealAllowance: true,
            PerDiemCap: 100.00m,
            ExpenseDate: "2026-11-21",
            ReceiptOcrData: new { confidence_score = 0.99 },
            OcrConfidence: 0.99);

        var result = await (Task<Microsoft.AspNetCore.Http.IResult>)submitMethod
            .Invoke(null, [unscannedMealReq, dbContext, SiteCoordinatorContext()])!;

        result.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var claim = ((Ok<ParticipantExpenseClaim>)result).Value!;
        claim.Status.Should().Be("PENDING_REVIEW");
        claim.DisbursementReference.Should().BeNull();
        claim.TaxDeduction.Should().Be(3.00m);
        claim.NetAmount.Should().Be(92.00m);
        claim.ReviewNotes.Should().Contain("per-diem cap ($75.00)");

        // Even a low-amount unscanned transit claim without server receipt bytes must NOT default OCR confidence to 0.95/0.98
        var unscannedTransitReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-VERIFY-02B",
            StudyId: "STUDY-01",
            SiteId: "SITE-101",
            Category: "TRANSIT",
            MerchantName: "Unscanned Taxi",
            GrossAmount: 22.00m,
            ExpenseDate: "2026-11-21");

        var transitResult = await (Task<Microsoft.AspNetCore.Http.IResult>)submitMethod
            .Invoke(null, [unscannedTransitReq, dbContext, SiteCoordinatorContext()])!;
        var transitClaim = ((Ok<ParticipantExpenseClaim>)transitResult).Value!;
        transitClaim.Status.Should().Be("PENDING_REVIEW");
        transitClaim.DisbursementReference.Should().BeNull();

        File.Delete(dbPath);
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    [GxPRequirement("PRD-PAY-006")]
    public async Task ReviewExpenseClaim_RejectsSelfApproval_BoundsAdjustedAmount_AndRecomputesServerTaxAndNet()
    {
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var submitMethod = typeof(PaymentEndpoints)
            .GetMethod("SubmitExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var reviewMethod = typeof(PaymentEndpoints)
            .GetMethod("ReviewExpenseClaim", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Submitting CRC creates a $500.00 PENDING_REVIEW claim
        var highClaimReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-SOD-01",
            StudyId: "STUDY-01",
            SiteId: "SITE-101",
            Category: "LODGING",
            MerchantName: "Conference Hotel",
            GrossAmount: 500.00m,
            ExpenseDate: "2026-11-21");
        var submitResult = await (Task<Microsoft.AspNetCore.Http.IResult>)submitMethod
            .Invoke(null, [highClaimReq, dbContext, SiteCoordinatorContext()])!;
        var highClaim = ((Ok<ParticipantExpenseClaim>)submitResult).Value!;
        highClaim.Status.Should().Be("PENDING_REVIEW");

        // Submitting CRC attempting to self-approve must receive 409 Conflict
        var selfApproveResult = await (Task<Microsoft.AspNetCore.Http.IResult>)reviewMethod
            .Invoke(null, [highClaim.Id, new ReviewExpenseClaimRequestDto("APPROVED", "Self-approved by submitting CRC"), dbContext, SiteCoordinatorContext()])!;
        selfApproveResult.GetType().Name.Should().StartWith("Conflict");

        // Direct domain entity Approve also enforces separation of duties
        Action selfDomainApprove = () => highClaim.Approve("crc-1", SystemClock.Instance.UtcNow, "Self-approve");
        selfDomainApprove.Should().Throw<PaymentDomainException>().WithMessage("*cannot also approve*");

        // Out-of-bounds AdjustedAmount (< 0.01 or > GrossAmount) is rejected with 409 Conflict
        var zeroAdjustResult = await (Task<Microsoft.AspNetCore.Http.IResult>)reviewMethod
            .Invoke(null, [highClaim.Id, new ReviewExpenseClaimRequestDto("APPROVED", "Zero", AdjustedAmount: 0m), dbContext, SiteInvestigatorContext()])!;
        zeroAdjustResult.GetType().Name.Should().StartWith("Conflict");

        var overAdjustResult = await (Task<Microsoft.AspNetCore.Http.IResult>)reviewMethod
            .Invoke(null, [highClaim.Id, new ReviewExpenseClaimRequestDto("APPROVED", "Inflated", AdjustedAmount: 550.00m), dbContext, SiteInvestigatorContext()])!;
        overAdjustResult.GetType().Name.Should().StartWith("Conflict");

        // Submit a $95.00 MEAL claim (initial taxDeduction = $3.00, net = $92.00) and adjust to $85.00 during dual review
        var mealClaimReq = new SubmitExpenseClaimRequestDto(
            SubjectId: "SUBJ-SOD-MEAL",
            StudyId: "STUDY-01",
            SiteId: "SITE-101",
            Category: "MEAL",
            MerchantName: "Trial Bistro",
            GrossAmount: 95.00m,
            IsMealAllowance: true,
            ExpenseDate: "2026-11-21");
        var mealSubmitRes = await (Task<Microsoft.AspNetCore.Http.IResult>)submitMethod
            .Invoke(null, [mealClaimReq, dbContext, SiteCoordinatorContext()])!;
        var mealClaim = ((Ok<ParticipantExpenseClaim>)mealSubmitRes).Value!;
        mealClaim.TaxDeduction.Should().Be(3.00m);
        mealClaim.NetAmount.Should().Be(92.00m);

        var mealApproveRes = await (Task<Microsoft.AspNetCore.Http.IResult>)reviewMethod
            .Invoke(null, [mealClaim.Id, new ReviewExpenseClaimRequestDto("APPROVED", "Adjusted meal reimbursement", AdjustedAmount: 85.00m), dbContext, SiteInvestigatorContext()])!;
        mealApproveRes.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var approvedMeal = ((Ok<ParticipantExpenseClaim>)mealApproveRes).Value!;
        approvedMeal.Status.Should().Be("APPROVED");
        approvedMeal.GrossAmount.Should().Be(85.00m);
        approvedMeal.TaxDeduction.Should().Be(1.50m);
        approvedMeal.NetAmount.Should().Be(83.50m);

        var mealWallet = await dbContext.ParticipantWallets.FirstAsync(w => w.SubjectId == "SUBJ-SOD-MEAL", TestContext.Current.CancellationToken);
        mealWallet.CurrentBalance.Should().Be(83.50m);

        File.Delete(dbPath);
    }
}
