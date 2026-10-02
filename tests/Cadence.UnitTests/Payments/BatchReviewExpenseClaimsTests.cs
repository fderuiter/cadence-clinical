using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public class BatchReviewExpenseClaimsTests
{
    private static PaymentsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new PaymentsDbContext(options, TestFieldProtection.CreateEncryptor());
    }

    [Fact]
    [GxPRequirement("PRD-SYS-182")]
    public async Task BatchReviewExpenseClaims_Approved_UpdatesClaims_CreditsWallet_CreatesTransactions_AndLogsAudit()
    {
        // Arrange
        using var db = CreateDbContext();
        DateTimeOffset now = SystemClock.Instance.UtcNow;

        var wallet = new ParticipantWallet
        {
            Id = "w-test-1",
            SubjectId = "SUBJ-2001",
            StudyId = "STUDY-001",
            SiteId = "SITE-101",
            CurrentBalance = 50.00m,
            LifetimeStipends = 50.00m,
            LifetimeReimbursements = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = "SUBJ-2001",
            UpdatedAt = now,
            VersionIndex = 1
        };
        db.ParticipantWallets.Add(wallet);

        var claim1 = new ParticipantExpenseClaim
        {
            Id = "claim-101",
            WalletId = wallet.Id,
            SubjectId = wallet.SubjectId,
            StudyId = wallet.StudyId,
            SiteId = wallet.SiteId,
            ExpenseDate = "2026-10-01",
            Category = "TRANSIT",
            MerchantName = "City Metro",
            GrossAmount = 25.00m,
            TaxDeduction = 0m,
            NetAmount = 25.00m,
            Status = "PENDING_REVIEW",
            CreatedAt = now,
            CreatedBy = wallet.SubjectId,
            UpdatedAt = now,
            VersionIndex = 1
        };

        var claim2 = new ParticipantExpenseClaim
        {
            Id = "claim-102",
            WalletId = wallet.Id,
            SubjectId = wallet.SubjectId,
            StudyId = wallet.StudyId,
            SiteId = wallet.SiteId,
            ExpenseDate = "2026-10-02",
            Category = "PARKING",
            MerchantName = "Medical Garage",
            GrossAmount = 15.00m,
            TaxDeduction = 0m,
            NetAmount = 15.00m,
            Status = "PENDING_REVIEW",
            CreatedAt = now,
            CreatedBy = wallet.SubjectId,
            UpdatedAt = now,
            VersionIndex = 1
        };

        db.ParticipantExpenseClaims.AddRange(claim1, claim2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new BatchReviewExpenseClaimsRequestDto(
            ClaimIds: new List<string> { claim1.Id, claim2.Id },
            Action: "APPROVED",
            ReviewNotes: "Bulk approved valid travel expenses."
        );

        // Act
        var result = await PaymentEndpoints.BatchReviewExpenseClaims(request, db);

        // Assert
        var okResult = result.Should().BeOfType<Ok<BatchReviewExpenseClaimsResponseDto>>().Subject;
        var response = okResult.Value!;

        response.Success.Should().BeTrue();
        response.ProcessedCount.Should().Be(2);
        response.Action.Should().Be("APPROVED");
        response.TotalAmount.Should().Be(40.00m);
        response.ProcessedClaimIds.Should().BeEquivalentTo(claim1.Id, claim2.Id);

        // Verify claims updated in DB
        var updatedClaim1 = await db.ParticipantExpenseClaims.FindAsync(new object[] { claim1.Id }, TestContext.Current.CancellationToken);
        var updatedClaim2 = await db.ParticipantExpenseClaims.FindAsync(new object[] { claim2.Id }, TestContext.Current.CancellationToken);

        updatedClaim1!.Status.Should().Be("APPROVED");
        updatedClaim1.ReviewNotes.Should().Be("Bulk approved valid travel expenses.");
        updatedClaim2!.Status.Should().Be("APPROVED");

        // Verify wallet updated
        var updatedWallet = await db.ParticipantWallets.FindAsync(new object[] { wallet.Id }, TestContext.Current.CancellationToken);
        updatedWallet!.CurrentBalance.Should().Be(90.00m); // 50 + 25 + 15
        updatedWallet.LifetimeReimbursements.Should().Be(40.00m);

        // Verify transactions ledger entries
        var txs = await db.ParticipantTransactions.Where(t => t.WalletId == wallet.Id).ToListAsync(TestContext.Current.CancellationToken);
        txs.Should().HaveCount(2);
        txs.All(t => t.TransactionType == "REIMBURSEMENT_CREDIT").Should().BeTrue();

        // Verify audit log entries
        var auditLogs = await db.PaymentAuditLogs.Where(a => a.Action == "EXPENSE_CLAIM_APPROVED").ToListAsync(TestContext.Current.CancellationToken);
        auditLogs.Should().HaveCount(2);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-182")]
    public async Task BatchReviewExpenseClaims_Rejected_UpdatesClaims_DoesNotCreditWallet_LogsAudit()
    {
        // Arrange
        using var db = CreateDbContext();
        DateTimeOffset now = SystemClock.Instance.UtcNow;

        var wallet = new ParticipantWallet
        {
            Id = "w-test-2",
            SubjectId = "SUBJ-2002",
            StudyId = "STUDY-001",
            SiteId = "SITE-101",
            CurrentBalance = 100.00m,
            LifetimeStipends = 100.00m,
            LifetimeReimbursements = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = "SUBJ-2002",
            UpdatedAt = now,
            VersionIndex = 1
        };
        db.ParticipantWallets.Add(wallet);

        var claim1 = new ParticipantExpenseClaim
        {
            Id = "claim-201",
            WalletId = wallet.Id,
            SubjectId = wallet.SubjectId,
            StudyId = wallet.StudyId,
            SiteId = wallet.SiteId,
            ExpenseDate = "2026-10-01",
            Category = "MEAL",
            MerchantName = "Bistro",
            GrossAmount = 50.00m,
            TaxDeduction = 0m,
            NetAmount = 50.00m,
            Status = "PENDING_REVIEW",
            CreatedAt = now,
            CreatedBy = wallet.SubjectId,
            UpdatedAt = now,
            VersionIndex = 1
        };

        db.ParticipantExpenseClaims.Add(claim1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new BatchReviewExpenseClaimsRequestDto(
            ClaimIds: new List<string> { claim1.Id },
            Action: "REJECTED",
            ReviewNotes: "Missing itemized receipt."
        );

        // Act
        var result = await PaymentEndpoints.BatchReviewExpenseClaims(request, db);

        // Assert
        var okResult = result.Should().BeOfType<Ok<BatchReviewExpenseClaimsResponseDto>>().Subject;
        var response = okResult.Value!;

        response.Success.Should().BeTrue();
        response.ProcessedCount.Should().Be(1);
        response.Action.Should().Be("REJECTED");

        var updatedClaim = await db.ParticipantExpenseClaims.FindAsync(new object[] { claim1.Id }, TestContext.Current.CancellationToken);
        updatedClaim!.Status.Should().Be("REJECTED");

        var updatedWallet = await db.ParticipantWallets.FindAsync(new object[] { wallet.Id }, TestContext.Current.CancellationToken);
        updatedWallet!.CurrentBalance.Should().Be(100.00m); // Balance unchanged

        var auditLogs = await db.PaymentAuditLogs.Where(a => a.Action == "EXPENSE_CLAIM_REJECTED").ToListAsync(TestContext.Current.CancellationToken);
        auditLogs.Should().HaveCount(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-182")]
    public async Task BatchReviewExpenseClaims_Rejects_WhenAnyClaimNotPending()
    {
        // Arrange
        using var db = CreateDbContext();
        DateTimeOffset now = SystemClock.Instance.UtcNow;

        var claim1 = new ParticipantExpenseClaim
        {
            Id = "claim-301",
            WalletId = "w-3",
            SubjectId = "SUBJ-3",
            StudyId = "S1",
            SiteId = "SITE-1",
            GrossAmount = 10m,
            NetAmount = 10m,
            Status = "PENDING_REVIEW",
            CreatedAt = now,
            CreatedBy = "SUBJ-3",
            UpdatedAt = now,
            VersionIndex = 1
        };

        var claim2 = new ParticipantExpenseClaim
        {
            Id = "claim-302",
            WalletId = "w-3",
            SubjectId = "SUBJ-3",
            StudyId = "S1",
            SiteId = "SITE-1",
            GrossAmount = 20m,
            NetAmount = 20m,
            Status = "APPROVED", // Already approved!
            CreatedAt = now,
            CreatedBy = "SUBJ-3",
            UpdatedAt = now,
            VersionIndex = 1
        };

        db.ParticipantExpenseClaims.AddRange(claim1, claim2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new BatchReviewExpenseClaimsRequestDto(
            ClaimIds: new List<string> { claim1.Id, claim2.Id },
            Action: "APPROVED"
        );

        // Act
        var result = await PaymentEndpoints.BatchReviewExpenseClaims(request, db);

        // Assert
        var badRequest = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        // Verify claim1 remains PENDING_REVIEW (atomic rollback / no change)
        var freshClaim1 = await db.ParticipantExpenseClaims.FindAsync(new object[] { claim1.Id }, TestContext.Current.CancellationToken);
        freshClaim1!.Status.Should().Be("PENDING_REVIEW");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-182")]
    public async Task BatchReviewExpenseClaims_Rejects_WhenExceeding100Claims()
    {
        using var db = CreateDbContext();
        var claimIds = Enumerable.Range(1, 101).Select(i => $"c-{i}").ToList();

        var request = new BatchReviewExpenseClaimsRequestDto(
            ClaimIds: claimIds,
            Action: "APPROVED"
        );

        var result = await PaymentEndpoints.BatchReviewExpenseClaims(request, db);
        var badRequest = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-182")]
    public async Task BatchReviewExpenseClaims_Rejects_WhenClaimIdsEmpty()
    {
        using var db = CreateDbContext();
        var request = new BatchReviewExpenseClaimsRequestDto(
            ClaimIds: new List<string>(),
            Action: "APPROVED"
        );

        var result = await PaymentEndpoints.BatchReviewExpenseClaims(request, db);
        var badRequest = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }
}

