using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application.Services;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.FieldProtection;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public class ParticipantStipendDisbursementTests
{
    // The site coordinator who verified the visit, as the gateway signs them (#5361).
    private static readonly ClaimsPrincipal SiteCoordinator = TestSecurityContext.CreateScopedPrincipal(
        "crc-1", TestSecurityContext.AllStudies, ["SITE-101"], "crc");

    private static HttpContext SiteCoordinatorContext() => new DefaultHttpContext { User = SiteCoordinator };

    private static string NewTempDbPath() => Path.Combine(Path.GetTempPath(), $"payments-test-{Guid.NewGuid():N}.db");

    private static DbContextOptions<PaymentsDbContext> FileOptions(string path) =>
        new DbContextOptionsBuilder<PaymentsDbContext>().UseSqlite($"Data Source={path}").Options;

    private static async Task<PaymentsDbContext> CreateDbContextAsync(string path)
    {
        var dbContext = new PaymentsDbContext(FileOptions(path), DesignTimeFieldEncryptor.Instance);
        await dbContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        return dbContext;
    }

    private static StipendApplicationService CreateStipendService(PaymentsDbContext db) =>
        new(db, new PaymentsUnitOfWork(db), new WalletRowLockRepository(db), SystemClock.Instance);

    private static TaxApplicationService CreateTaxService(PaymentsDbContext db) =>
        new(db, new PaymentsUnitOfWork(db), new WalletRowLockRepository(db), SystemClock.Instance);

    private static ExpenseApplicationService CreateExpenseService(PaymentsDbContext db) =>
        new(db, new PaymentsUnitOfWork(db), new WalletRowLockRepository(db), SystemClock.Instance);

    [Fact]
    public async Task IngestVisitVerifiedEvent_WithinThreshold_CreditsWalletAndRecordsAudit()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var rule = new ParticipantStipendRule
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-001",
            VisitCode = "V1_SCREENING",
            VisitName = "Screening Visit",
            Amount = 75.00m,
            Currency = "USD",
            Taxable = true,
            AutoDisbursementThreshold = 250.00m,
            AntiCoercionCap = 200.00m,
            IsActive = true,
            CreatedAt = SystemClock.Instance.UtcNow,
            CreatedBy = "system",
            UpdatedAt = SystemClock.Instance.UtcNow,
            VersionIndex = 1
        };
        dbContext.ParticipantStipendRules.Add(rule);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evt = new VisitVerificationEventDto(
            StudyId: "STUDY-001",
            SiteId: "SITE-101",
            SubjectId: "SUBJ-2001",
            VisitCode: "V1_SCREENING",
            EventId: "EVT-1001"
        );

        // Act
        var result = await WalletEndpoints.IngestVisitVerifiedEvent(
            evt, CreateStipendService(dbContext), SiteCoordinatorContext());

        // Assert
        result.Should().BeOfType<Ok<VisitVerificationResponseDto>>();
        var okResult = (Ok<VisitVerificationResponseDto>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.Status.Should().Be("CREDITED");
        okResult.Value.Amount.Should().Be(75.00m);
        okResult.Value.CurrentBalance.Should().Be(75.00m);

        var wallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.SubjectId == "SUBJ-2001", TestContext.Current.CancellationToken);
        wallet.Should().NotBeNull();
        wallet!.CurrentBalance.Should().Be(75.00m);
        wallet.LifetimeStipends.Should().Be(75.00m);
        wallet.TaxableYtd.Should().Be(75.00m);

        var tx = await dbContext.ParticipantTransactions.FirstOrDefaultAsync(t => t.ReferenceId == "EVT-1001", TestContext.Current.CancellationToken);
        tx.Should().NotBeNull();
        tx!.Amount.Should().Be(75.00m);
        tx.TransactionType.Should().Be("STIPEND_CREDIT");

        var audit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "PARTICIPANT_STIPEND_CREDITED", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();
        audit!.EntityType.Should().Be("ParticipantWallet");

        File.Delete(dbPath);
    }

    [Fact]
    public async Task IngestVisitVerifiedEvent_DuplicateEventId_ReturnsAlreadyProcessedWithoutDoubleCrediting()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var rule = new ParticipantStipendRule
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-001",
            VisitCode = "V1_SCREENING",
            VisitName = "Screening Visit",
            Amount = 100.00m,
            Currency = "USD",
            IsActive = true,
            CreatedAt = SystemClock.Instance.UtcNow,
            CreatedBy = "system",
            UpdatedAt = SystemClock.Instance.UtcNow,
            VersionIndex = 1
        };
        dbContext.ParticipantStipendRules.Add(rule);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evt = new VisitVerificationEventDto(
            StudyId: "STUDY-001",
            SiteId: "SITE-101",
            SubjectId: "SUBJ-2002",
            VisitCode: "V1_SCREENING",
            EventId: "EVT-DUPLICATE-999"
        );

        // Act 1: First call
        var stipendService = CreateStipendService(dbContext);
        await WalletEndpoints.IngestVisitVerifiedEvent(evt, stipendService, SiteCoordinatorContext());

        // Act 2: Duplicate call with same EventId
        var dupResult = await WalletEndpoints.IngestVisitVerifiedEvent(evt, stipendService, SiteCoordinatorContext());

        // Assert
        dupResult.Should().BeOfType<Ok<VisitVerificationResponseDto>>();
        var okDup = (Ok<VisitVerificationResponseDto>)dupResult;
        okDup.Value!.Status.Should().Be("ALREADY_PROCESSED");
        okDup.Value.Amount.Should().Be(0m);
        okDup.Value.CurrentBalance.Should().Be(100.00m);

        var wallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.SubjectId == "SUBJ-2002", TestContext.Current.CancellationToken);
        wallet!.CurrentBalance.Should().Be(100.00m); // Balance remains $100, not double-credited to $200

        var txCount = await dbContext.ParticipantTransactions.CountAsync(t => t.ReferenceId == "EVT-DUPLICATE-999", TestContext.Current.CancellationToken);
        txCount.Should().Be(1);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task IngestVisitVerifiedEvent_Reaches600TaxThresholdWithoutW9_HaltsAutoDisbursementAndFlagsForReview()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var now = SystemClock.Instance.UtcNow;
        var wallet = new ParticipantWallet
        {
            Id = Guid.NewGuid().ToString(),
            SubjectId = "SUBJ-2003",
            StudyId = "STUDY-001",
            SiteId = "SITE-101",
            CurrentBalance = 500.00m,
            LifetimeStipends = 500.00m,
            TaxableYtd = 500.00m,
            Is1099ThresholdExceeded = false,
            W9Status = "NOT_REQUIRED",
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = "system",
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantWallets.Add(wallet);

        var rule = new ParticipantStipendRule
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-001",
            VisitCode = "V3_CYCLE_1",
            VisitName = "Cycle 1 Visit",
            Amount = 150.00m, // 500 + 150 = 650 >= 600 tax threshold!
            Currency = "USD",
            Taxable = true,
            AutoDisbursementThreshold = 500.00m,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = "system",
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantStipendRules.Add(rule);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evt = new VisitVerificationEventDto(
            StudyId: "STUDY-001",
            SiteId: "SITE-101",
            SubjectId: "SUBJ-2003",
            VisitCode: "V3_CYCLE_1",
            EventId: "EVT-TAX-HOLD-01"
        );

        // Act
        var result = await WalletEndpoints.IngestVisitVerifiedEvent(
            evt, CreateStipendService(dbContext), SiteCoordinatorContext());

        // Assert
        result.Should().BeOfType<Ok<VisitVerificationResponseDto>>();
        var okResult = (Ok<VisitVerificationResponseDto>)result;
        okResult.Value!.Status.Should().Be("FLAGGED_FOR_REVIEW");
        okResult.Value.RequiresReview.Should().BeTrue();
        okResult.Value.Reason.Should().Contain("IRS $600.00 YTD tax threshold reached");

        var updatedWallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.SubjectId == "SUBJ-2003", TestContext.Current.CancellationToken);
        updatedWallet!.W9Status.Should().Be("REQUIRED");
        updatedWallet.Is1099ThresholdExceeded.Should().BeTrue();
        updatedWallet.CurrentBalance.Should().Be(500.00m); // Balance unchanged due to tax hold

        var audit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "IRS_1099_THRESHOLD_EXCEEDED", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();

        File.Delete(dbPath);
    }

    [Fact]
    public async Task IngestVisitVerifiedEvent_ExceedsAntiCoercionCap_HaltsAutoDisbursementAndFlagsForReview()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var rule = new ParticipantStipendRule
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-001",
            VisitCode = "V6_END_STUDY",
            VisitName = "End of Study Milestone",
            Amount = 300.00m,
            Currency = "USD",
            AntiCoercionCap = 250.00m, // 300 > 250 cap!
            IsActive = true,
            CreatedAt = SystemClock.Instance.UtcNow,
            CreatedBy = "system",
            UpdatedAt = SystemClock.Instance.UtcNow,
            VersionIndex = 1
        };
        dbContext.ParticipantStipendRules.Add(rule);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evt = new VisitVerificationEventDto(
            StudyId: "STUDY-001",
            SiteId: "SITE-101",
            SubjectId: "SUBJ-2004",
            VisitCode: "V6_END_STUDY",
            EventId: "EVT-CAP-EXCEEDED-01"
        );

        // Act
        var result = await WalletEndpoints.IngestVisitVerifiedEvent(
            evt, CreateStipendService(dbContext), SiteCoordinatorContext());

        // Assert
        result.Should().BeOfType<Ok<VisitVerificationResponseDto>>();
        var okResult = (Ok<VisitVerificationResponseDto>)result;
        okResult.Value!.Status.Should().Be("FLAGGED_FOR_REVIEW");
        okResult.Value.RequiresReview.Should().BeTrue();
        okResult.Value.Reason.Should().Contain("anti-coercion cap limit");

        var audit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "ANTI_COERCION_CAP_EXCEEDED", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();

        File.Delete(dbPath);
    }

    [Fact]
    public async Task IngestVisitVerifiedEvent_ExceedsAutoDisbursementThreshold_HaltsAutoDisbursementAndFlagsForReview()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var rule = new ParticipantStipendRule
        {
            Id = Guid.NewGuid().ToString(),
            StudyId = "STUDY-001",
            VisitCode = "V_SPECIAL",
            VisitName = "Special Procedure Visit",
            Amount = 500.00m,
            Currency = "USD",
            AutoDisbursementThreshold = 250.00m, // 500 > 250 threshold!
            AntiCoercionCap = 1000.00m,
            IsActive = true,
            CreatedAt = SystemClock.Instance.UtcNow,
            CreatedBy = "system",
            UpdatedAt = SystemClock.Instance.UtcNow,
            VersionIndex = 1
        };
        dbContext.ParticipantStipendRules.Add(rule);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evt = new VisitVerificationEventDto(
            StudyId: "STUDY-001",
            SiteId: "SITE-101",
            SubjectId: "SUBJ-2005",
            VisitCode: "V_SPECIAL",
            EventId: "EVT-THRESHOLD-EXCEEDED-01"
        );

        // Act
        var result = await WalletEndpoints.IngestVisitVerifiedEvent(
            evt, CreateStipendService(dbContext), SiteCoordinatorContext());

        // Assert
        result.Should().BeOfType<Ok<VisitVerificationResponseDto>>();
        var okResult = (Ok<VisitVerificationResponseDto>)result;
        okResult.Value!.Status.Should().Be("FLAGGED_FOR_REVIEW");
        okResult.Value.RequiresReview.Should().BeTrue();
        okResult.Value.Reason.Should().Contain("auto-disbursement threshold limit");

        var audit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "THRESHOLD_EXCEEDED_REVIEW_REQUIRED", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();

        File.Delete(dbPath);
    }

    [Fact]
    public async Task DirectDisburse_CreatesAuditLogAndLedgerTransaction()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var req = new DisburseStipendRequest(
            SubjectId: "SUBJ-DIRECT-01",
            StudyId: "STUDY-DCT-001",
            SiteId: "SITE-101",
            SequenceNumber: 1,
            AssignmentId: "A-100",
            DiaryId: "D-100",
            Amount: 50.00m,
            IdempotencyKey: "KEY-123456",
            DisbursementType: "VIRTUAL_CARD"
        );

        // Act
        var result = await WalletEndpoints.DisburseStipendDirect(
            req, CreateStipendService(dbContext), SiteCoordinatorContext());

        // Assert
        result.Should().BeOfType<Ok<DisburseStipendResponse>>();
        var okResult = (Ok<DisburseStipendResponse>)result;
        okResult.Value!.Success.Should().BeTrue();
        okResult.Value.Amount.Should().Be(50.00m);
        okResult.Value.NewBalance.Should().Be(50.00m);

        var wallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.SubjectId == "SUBJ-DIRECT-01", TestContext.Current.CancellationToken);
        wallet.Should().NotBeNull();
        wallet!.CurrentBalance.Should().Be(50.00m);
        wallet.LifetimeStipends.Should().Be(50.00m);
        wallet.TaxableYtd.Should().Be(50.00m);
        wallet.LifetimeDisbursements.Should().Be(0m);

        var tx = await dbContext.ParticipantTransactions.FirstOrDefaultAsync(
            t => t.WalletId == wallet.Id && t.IdempotencyKey == "KEY-123456",
            TestContext.Current.CancellationToken);
        tx.Should().NotBeNull();
        tx!.TransactionType.Should().Be("STIPEND_CREDIT");
        tx.Amount.Should().Be(50.00m);
        tx.BalanceAfter.Should().Be(50.00m);

        var audit = await dbContext.PaymentAuditLogs.FirstOrDefaultAsync(l => l.Action == "AUTO_STIPEND_DISBURSEMENT", TestContext.Current.CancellationToken);
        audit.Should().NotBeNull();

        File.Delete(dbPath);
    }

    [Fact]
    public async Task DirectDisburse_EnforcesAmountCapTaxHoldAndPerWalletIdempotency()
    {
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);
        var ct = TestContext.Current.CancellationToken;
        var secondReviewer = TestSecurityContext.CreateScopedPrincipal(
            "pi-1", TestSecurityContext.AllStudies, ["SITE-101"], "investigator");
        var secondReviewerContext = new DefaultHttpContext { User = secondReviewer };

        var stipendService = CreateStipendService(dbContext);
        var taxService = CreateTaxService(dbContext);

        // 1. Negative or zero stipend amounts fail with 400 Bad Request
        var negReq = new DisburseStipendRequest("SUBJ-GUARD-01", "STUDY-DCT-001", "SITE-101", 1, null, null, -10.00m, "k-neg", null);
        var negResult = await WalletEndpoints.DisburseStipendDirect(negReq, stipendService, SiteCoordinatorContext());
        negResult.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(400);

        // 2. Short idempotency key "1" on Subject A does NOT collide with Subject B (even when Subject A's audit JSON contains "1")
        var subjAReq = new DisburseStipendRequest("SUBJ-GUARD-A", "STUDY-DCT-001", "SITE-101", 1, "A-1", "D-1", 50.00m, "1", null);
        var subjARes = await WalletEndpoints.DisburseStipendDirect(subjAReq, stipendService, SiteCoordinatorContext());
        subjARes.Should().BeOfType<Ok<DisburseStipendResponse>>().Which.Value!.Status.Should().Be("COMPLETED");

        var subjAReplay = await WalletEndpoints.DisburseStipendDirect(subjAReq, stipendService, SiteCoordinatorContext());
        subjAReplay.Should().BeOfType<Ok<DisburseStipendResponse>>().Which.Value!.Status.Should().Be("DUPLICATE_SKIPPED");

        var subjBReq = new DisburseStipendRequest("SUBJ-GUARD-B", "STUDY-DCT-001", "SITE-101", 1, "A-1", "D-1", 50.00m, "1", null);
        var subjBRes = await WalletEndpoints.DisburseStipendDirect(subjBReq, stipendService, SiteCoordinatorContext());
        subjBRes.Should().BeOfType<Ok<DisburseStipendResponse>>().Which.Value!.Status.Should().Be("COMPLETED");

        // 3. Stipend reaching $600 YTD without verified W-9 is rejected with 409 Conflict; once dual-verified, it succeeds and sets Is1099ThresholdExceeded
        var walletB = await dbContext.ParticipantWallets.SingleAsync(w => w.SubjectId == "SUBJ-GUARD-B", ct);
        walletB.AntiCoercionCap = 600.00m;
        walletB.AutoDisbursementThreshold = 600.00m;
        await dbContext.SaveChangesAsync(ct);

        var overCapReq = new DisburseStipendRequest("SUBJ-GUARD-B", "STUDY-DCT-001", "SITE-101", 2, null, null, 650.00m, "k-cap", null);
        var overCapRes = await WalletEndpoints.DisburseStipendDirect(overCapReq, stipendService, SiteCoordinatorContext());
        overCapRes.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(422);

        var reach600Req = new DisburseStipendRequest("SUBJ-GUARD-B", "STUDY-DCT-001", "SITE-101", 3, null, null, 550.00m, "k-600", null);
        var holdRes = await WalletEndpoints.DisburseStipendDirect(reach600Req, stipendService, SiteCoordinatorContext());
        holdRes.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(409);

        var submitRes = await TaxEndpoints.SubmitW9Form(
            walletB.Id, new SubmitW9RequestDto("Participant B", "4321", "100 Clinical Way", true), taxService, SiteCoordinatorContext());
        submitRes.Should().BeOfType<Ok<WalletResponse>>();

        var selfVerifyRes = await TaxEndpoints.VerifyW9Form(
            walletB.Id, new VerifyW9RequestDto("self-verify"), taxService, SiteCoordinatorContext());
        selfVerifyRes.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(409);

        var dualVerifyRes = await TaxEndpoints.VerifyW9Form(
            walletB.Id, new VerifyW9RequestDto("dual-verify"), taxService, secondReviewerContext);
        dualVerifyRes.Should().BeOfType<Ok<WalletResponse>>().Which.Value!.W9Status.Should().Be("VERIFIED");

        var unlockedRes = await WalletEndpoints.DisburseStipendDirect(reach600Req, stipendService, SiteCoordinatorContext());
        unlockedRes.Should().BeOfType<Ok<DisburseStipendResponse>>().Which.Value!.NewBalance.Should().Be(600.00m);

        var refreshedWalletB = await dbContext.ParticipantWallets.AsNoTracking().SingleAsync(w => w.Id == walletB.Id, ct);
        refreshedWalletB.TaxableYtd.Should().Be(600.00m);
        refreshedWalletB.Is1099ThresholdExceeded.Should().BeTrue();
        refreshedWalletB.LifetimeStipends.Should().Be(600.00m);
        refreshedWalletB.LifetimeDisbursements.Should().Be(0m);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task ReviewExpenseClaim_ApprovesClaimAndCreditsWallet()
    {
        // Arrange
        string dbPath = NewTempDbPath();
        await using var dbContext = await CreateDbContextAsync(dbPath);

        var now = SystemClock.Instance.UtcNow;
        var wallet = new ParticipantWallet
        {
            Id = "wallet-claim-1",
            SubjectId = "SUBJ-CLAIM-01",
            StudyId = "STUDY-DCT-001",
            SiteId = "SITE-101",
            CurrentBalance = 100.00m,
            LifetimeStipends = 100.00m,
            LifetimeReimbursements = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = "system",
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantWallets.Add(wallet);

        var claim = new ParticipantExpenseClaim
        {
            Id = "claim-001",
            WalletId = "wallet-claim-1",
            SubjectId = "SUBJ-CLAIM-01",
            StudyId = "STUDY-DCT-001",
            SiteId = "SITE-101",
            ExpenseDate = "2026-08-25",
            Category = "TRANSIT",
            MerchantName = "Metro Transit Authority",
            GrossAmount = 28.50m,
            TaxDeduction = 0m,
            NetAmount = 28.50m,
            Status = "PENDING_REVIEW",
            CreatedAt = now,
            CreatedBy = "SUBJ-CLAIM-01",
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantExpenseClaims.Add(claim);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reviewReq = new ReviewExpenseClaimRequestDto(
            Status: "APPROVED",
            ReviewNotes: "Verified valid clinical travel receipt."
        );

        // Act
        var result = await ExpenseEndpoints.ReviewExpenseClaim(
            "claim-001", reviewReq, CreateExpenseService(dbContext), SiteCoordinatorContext());

        // Assert
        result.Should().BeOfType<Ok<ParticipantExpenseClaim>>();
        var okResult = (Ok<ParticipantExpenseClaim>)result;
        okResult.Value!.Status.Should().Be("APPROVED");

        var updatedWallet = await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.Id == "wallet-claim-1", TestContext.Current.CancellationToken);
        updatedWallet!.CurrentBalance.Should().Be(128.50m);
        updatedWallet.LifetimeReimbursements.Should().Be(28.50m);

        var tx = await dbContext.ParticipantTransactions.FirstOrDefaultAsync(t => t.ReferenceId == "claim-001", TestContext.Current.CancellationToken);
        tx.Should().NotBeNull();
        tx!.TransactionType.Should().Be("REIMBURSEMENT_CREDIT");
        tx.Amount.Should().Be(28.50m);

        File.Delete(dbPath);
    }
}
