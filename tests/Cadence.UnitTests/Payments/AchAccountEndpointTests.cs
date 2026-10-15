using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public sealed class AchAccountEndpointTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AchAccountEndpointTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PaymentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new PaymentsDbContext(options, TestFieldProtection.CreateEncryptor());
    }

    private static ParticipantWallet CreateTestWallet(string walletId = "wallet-001", string subjectId = "SUBJ-101")
    {
        DateTimeOffset now = FakeClock.DefaultStart;
        return new ParticipantWallet
        {
            Id = walletId,
            SubjectId = subjectId,
            StudyId = "STUDY-001",
            SiteId = "SITE-101",
            CurrentBalance = 100.00m,
            LifetimeStipends = 100.00m,
            LifetimeReimbursements = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = subjectId,
            UpdatedAt = now,
            VersionIndex = 1
        };
    }

    [Fact]
    public void IsValidAbaRoutingNumber_ValidatesRoutingNumbersCorrectly()
    {
        // Valid ABA routing transit numbers
        PaymentEndpoints.IsValidAbaRoutingNumber("021000021").Should().BeTrue(); // Chase NY
        PaymentEndpoints.IsValidAbaRoutingNumber("121000358").Should().BeTrue(); // Bank of America CA

        // Invalid routing numbers
        PaymentEndpoints.IsValidAbaRoutingNumber("123456789").Should().BeFalse();
        PaymentEndpoints.IsValidAbaRoutingNumber("000000000").Should().BeFalse();
        PaymentEndpoints.IsValidAbaRoutingNumber("12345678").Should().BeFalse();
        PaymentEndpoints.IsValidAbaRoutingNumber("1234567890").Should().BeFalse();
        PaymentEndpoints.IsValidAbaRoutingNumber(null).Should().BeFalse();
        PaymentEndpoints.IsValidAbaRoutingNumber("abc").Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAchAccount_ValidDetails_RegistersAccountAndWritesAuditLog()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var wallet = CreateTestWallet();
        db.ParticipantWallets.Add(wallet);
        await db.SaveChangesAsync(ct);

        var req = new RegisterAchAccountRequestDto
        {
            RoutingNumberSnake = "021000021",
            AccountNumberSnake = "987654321",
            AccountHolderNameSnake = "Jane Participant",
            AccountTypeSnake = "CHECKING"
        };

        var method = typeof(PaymentEndpoints).GetMethod("RegisterAchAccount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var task = (Task<IResult>)method.Invoke(null, [wallet.Id, req, db])!;
        var result = await task;

        var statusCodeResult = result as IStatusCodeHttpResult;
        statusCodeResult.Should().NotBeNull();
        statusCodeResult!.StatusCode.Should().Be(200);

        var okResult = (Ok<WalletResponse>)result;
        var response = okResult.Value!;

        response.AchRoutingNumber.Should().Be("*****0021");
        response.AchAccountMask.Should().Be("ACH-***4321");
        response.AchAccountHolder.Should().Be("Jane Participant");

        // Verify database persistence
        var updatedWallet = await db.ParticipantWallets.FirstAsync(w => w.Id == wallet.Id, ct);
        updatedWallet.AchProcessorReference.Should().StartWith("ach_tok_");
        updatedWallet.AchRoutingMask.Should().Be("*****0021");
        updatedWallet.AchAccountMask.Should().Be("ACH-***4321");
        updatedWallet.AchAccountHolder.Should().Be("Jane Participant");

        // Verify audit log
        var audit = await db.PaymentAuditLogs.FirstOrDefaultAsync(a => a.EntityId == wallet.Id && a.Action == "ACH_ACCOUNT_REGISTERED", ct);
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain("ach_processor_reference");
        audit.Details.Should().Contain("*****0021");
        audit.Details.Should().Contain("ACH-***4321");
    }

    [Fact]
    public async Task RegisterAchAccount_InvalidRoutingNumber_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var wallet = CreateTestWallet();
        db.ParticipantWallets.Add(wallet);
        await db.SaveChangesAsync(ct);

        var req = new RegisterAchAccountRequestDto
        {
            RoutingNumberSnake = "123456789", // invalid checksum
            AccountNumberSnake = "987654321",
            AccountHolderNameSnake = "Jane Participant"
        };

        var method = typeof(PaymentEndpoints).GetMethod("RegisterAchAccount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var task = (Task<IResult>)method.Invoke(null, [wallet.Id, req, db])!;
        var result = await task;

        var statusCodeResult = result as IStatusCodeHttpResult;
        statusCodeResult.Should().NotBeNull();
        statusCodeResult!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task RegisterAchAccount_PendingDisbursementExists_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var wallet = CreateTestWallet();
        db.ParticipantWallets.Add(wallet);

        var pendingDisbursement = new ParticipantDisbursement
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = wallet.SubjectId,
            Amount = 50.00m,
            DisbursementType = "DIRECT_ACH",
            ReferenceNumber = "DISB-ACH-PENDING1",
            Status = "PENDING",
            CreatedAt = FakeClock.DefaultStart,
            CreatedBy = wallet.SubjectId,
            VersionIndex = 1
        };
        db.ParticipantDisbursements.Add(pendingDisbursement);
        await db.SaveChangesAsync(ct);

        var req = new RegisterAchAccountRequestDto
        {
            RoutingNumberSnake = "021000021",
            AccountNumberSnake = "987654321",
            AccountHolderNameSnake = "Jane Participant"
        };

        var method = typeof(PaymentEndpoints).GetMethod("RegisterAchAccount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var task = (Task<IResult>)method.Invoke(null, [wallet.Id, req, db])!;
        var result = await task;

        var statusCodeResult = result as IStatusCodeHttpResult;
        statusCodeResult.Should().NotBeNull();
        statusCodeResult!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task DisburseParticipantFunds_DirectAchWithoutAchAccount_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var wallet = CreateTestWallet();
        // Wallet has no AchProcessorReference or AchAccountMask
        db.ParticipantWallets.Add(wallet);
        await db.SaveChangesAsync(ct);

        var req = new DisburseFundsRequestDto("DIRECT_ACH", 50.00m);

        var method = typeof(PaymentEndpoints).GetMethod("DisburseParticipantFunds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var task = (Task<IResult>)method.Invoke(null, [wallet.Id, req, db])!;
        var result = await task;

        var statusCodeResult = result as IStatusCodeHttpResult;
        statusCodeResult.Should().NotBeNull();
        statusCodeResult!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task DisburseParticipantFunds_DirectAchWithRegisteredAchAccount_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var wallet = CreateTestWallet();
        wallet.AchProcessorReference = "ach_tok_123456789";
        wallet.AchRoutingMask = "*****0021";
        wallet.AchAccountMask = "ACH-***4321";
        wallet.AchAccountHolder = "Jane Participant";

        db.ParticipantWallets.Add(wallet);
        await db.SaveChangesAsync(ct);

        var req = new DisburseFundsRequestDto("DIRECT_ACH", 50.00m);

        var method = typeof(PaymentEndpoints).GetMethod("DisburseParticipantFunds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var task = (Task<IResult>)method.Invoke(null, [wallet.Id, req, db])!;
        var result = await task;

        var statusCodeResult = result as IStatusCodeHttpResult;
        statusCodeResult.Should().NotBeNull();
        statusCodeResult!.StatusCode.Should().Be(200);

        var okResult = (Ok<ParticipantDisbursement>)result;
        okResult.Value!.Amount.Should().Be(50.00m);
        okResult.Value.DisbursementType.Should().Be("DIRECT_ACH");
    }
}
