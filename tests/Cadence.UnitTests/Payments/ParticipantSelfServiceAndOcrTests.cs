using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Payments;

/// <summary>
/// Verifies deterministic raster/vector receipt OCR (zero fabrication on unreadable images),
/// signed participant self-service wallet, expense auto-approval, manual review holds,
/// on-demand balance payouts, server-rule diary stipends, and cross-wallet RBAC isolation.
/// </summary>
public sealed class ParticipantSelfServiceAndOcrTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly WebApplicationFactory<Cadence.Apps.Payments.Program> _factory;

    public ParticipantSelfServiceAndOcrTests()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Cadence.Apps.Payments.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys();
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<PaymentsDbContext>>();
                services.AddDbContext<PaymentsDbContext>(options => options.UseSqlite(_connection));
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task OcrExtract_DistinctRasterPngReceipts_ExtractsRealTransitFuelAndMealMetadata()
    {
        await EnsureDbCreatedAsync();
        using var client = SubjectClient("SUBJ-OCR-01");

        byte[] transitPng = ReceiptOcrEngine.RenderReceiptPng(
        [
            "METRO TRANSIT",
            "DATE 2026-08-23",
            "FARE PAID $28.50",
        ]);
        byte[] fuelPng = ReceiptOcrEngine.RenderReceiptPng(
        [
            "SHELL FUEL",
            "DATE 2026-09-14",
            "TOTAL $42.75",
        ]);
        byte[] mealPng = ReceiptOcrEngine.RenderReceiptPng(
        [
            "BISTRO CAFE",
            "DATE 2026-10-05",
            "TOTAL $19.25",
        ]);

        var transit = await PostOcrAsync(client, Convert.ToBase64String(transitPng), "transit.png");
        transit.OcrReadable.Should().BeTrue();
        transit.MerchantName.Should().Be("METRO TRANSIT");
        transit.ExpenseDate.Should().Be("2026-08-23");
        transit.Category.Should().Be("TRANSIT");
        transit.GrossAmount.Should().Be(28.50m);
        transit.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90);

        var fuel = await PostOcrAsync(client, Convert.ToBase64String(fuelPng), "fuel.png");
        fuel.OcrReadable.Should().BeTrue();
        fuel.MerchantName.Should().Be("SHELL FUEL");
        fuel.ExpenseDate.Should().Be("2026-09-14");
        fuel.Category.Should().Be("FUEL");
        fuel.GrossAmount.Should().Be(42.75m);
        fuel.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90);

        var meal = await PostOcrAsync(client, Convert.ToBase64String(mealPng), "meal.png");
        meal.OcrReadable.Should().BeTrue();
        meal.MerchantName.Should().Be("BISTRO CAFE");
        meal.ExpenseDate.Should().Be("2026-10-05");
        meal.Category.Should().Be("MEAL");
        meal.GrossAmount.Should().Be(19.25m);
        meal.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.90);
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task OcrExtract_BlankOrCorruptedImages_NeverFabricatesFallbackValues()
    {
        await EnsureDbCreatedAsync();
        using var client = SubjectClient("SUBJ-OCR-02");

        byte[] blankPng = ReceiptOcrEngine.RenderReceiptPng([""], scale: 2, blurRadius: 0);
        var blankRes = await PostOcrAsync(client, Convert.ToBase64String(blankPng), "blank_transit_meal.png");
        blankRes.OcrReadable.Should().BeFalse();
        blankRes.ConfidenceScore.Should().Be(0.0);
        blankRes.MerchantName.Should().BeEmpty();
        blankRes.ExpenseDate.Should().BeEmpty();
        blankRes.GrossAmount.Should().Be(0m);
        blankRes.MerchantName.Should().NotContainEquivalentOf("Metro Transit");

        byte[] corruptedBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xFF, 0xD9];
        var corruptRes = await PostOcrAsync(client, Convert.ToBase64String(corruptedBytes), "meal_receipt_125.jpg");
        corruptRes.OcrReadable.Should().BeFalse();
        corruptRes.ConfidenceScore.Should().Be(0.0);
        corruptRes.MerchantName.Should().BeEmpty();
        corruptRes.GrossAmount.Should().Be(0m);

        await using var db = OpenDb();
        int ocrAudits = await db.PaymentAuditLogs.CountAsync(
            a => a.Action == "OCR_RECEIPT_EXTRACTED" && a.UserId == "SUBJ-OCR-02",
            Ct);
        ocrAudits.Should().Be(2);
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task OcrExtract_BlurryReceiptImage_LowersConfidenceAndRoutesClaimToPendingReview()
    {
        await EnsureDbCreatedAsync();
        using var client = SubjectClient("SUBJ-BLUR-01");

        byte[] blurryPng = ReceiptOcrEngine.RenderReceiptPng(
            ["CITY CAB", "2026-09-20", "TOTAL $32.00"],
            scale: 2,
            blurRadius: 3,
            inkLevel: 195,
            paperLevel: 235);

        var ocr = await PostOcrAsync(client, Convert.ToBase64String(blurryPng), "blurry.png");
        ocr.ConfidenceScore.Should().BeLessThan(0.90);

        using var claimResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "TRANSIT",
                MerchantName: "City Cab",
                GrossAmount: 32.00m,
                ExpenseDate: "2026-09-20",
                OcrConfidence: ocr.ConfidenceScore,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);

        claimResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await claimResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct);
        payload.Should().NotBeNull();
        var result = payload!;
        result.Status.Should().Be("PENDING_REVIEW");
        result.AutoApproved.Should().BeFalse();
        result.DisbursementReference.Should().BeNull();
        result.ReviewNotes.Should().Contain("could not read every detail on the receipt photo clearly");
        result.ReviewNotes.Should().NotContain("0.90");
    }

    [Fact]
    [GxPRequirement("PRD-PAY-006")]
    public async Task SelfServiceWallet_ReturnsLiveLedgerBalancesMaskedAccountsAndTransactions()
    {
        await EnsureDbCreatedAsync();
        await using (var seed = OpenDb())
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            seed.ParticipantWallets.Add(new ParticipantWallet
            {
                Id = "wallet-live-01",
                SubjectId = "SUBJ-LIVE-01",
                StudyId = "STUDY-LIVE",
                SiteId = "SITE-LIVE",
                CurrentBalance = 125.00m,
                LifetimeStipends = 200.00m,
                LifetimeReimbursements = 45.00m,
                LifetimeDisbursements = 120.00m,
                Currency = "USD",
                Status = "ACTIVE",
                CardStatus = "ACTIVE",
                CardType = "VIRTUAL_MASTERCARD",
                VirtualCardMask = "5412750011118842",
                VirtualCardExpiry = "09/29",
                AchRoutingMask = "021000021",
                AchAccountMask = "000998877654",
                AchAccountHolder = "Participant Alias",
                W9Status = "VERIFIED",
                TaxableYtd = 200.00m,
                CreatedAt = now,
                CreatedBy = "crc-1",
                UpdatedAt = now,
            });
            seed.ParticipantTransactions.Add(new ParticipantTransaction
            {
                WalletId = "wallet-live-01",
                StudyId = "STUDY-LIVE",
                SubjectId = "SUBJ-LIVE-01",
                TransactionType = "STIPEND_CREDIT",
                Amount = 75.00m,
                BalanceAfter = 125.00m,
                Description = "Visit V2 stipend",
                CreatedAt = now,
                CreatedBy = "crc-1",
            });
            await seed.SaveChangesAsync(Ct);
        }

        using var client = SubjectClient("SUBJ-LIVE-01", "SITE-LIVE");
        using var resp = await client.GetAsync("/api/v1/payments/participant/me/wallet", Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        string rawBody = await resp.Content.ReadAsStringAsync(Ct);
        rawBody.Should().NotContain("021000021", "raw routing numbers must never leave the server");
        rawBody.Should().NotContain("5412750011118842", "raw virtual card PAN must never leave the server");
        rawBody.Should().NotContain("000998877654", "raw bank account numbers must never leave the server");

        var dto = JsonSerializer.Deserialize<SelfServiceWalletSummaryResponseDto>(
            rawBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        dto.Should().NotBeNull();
        var summary = dto!;
        summary.Wallet.CurrentBalance.Should().Be(125.00m);
        summary.Wallet.LifetimeStipends.Should().Be(200.00m);
        summary.Wallet.LifetimeReimbursements.Should().Be(45.00m);
        summary.Wallet.LifetimeDisbursements.Should().Be(120.00m);
        summary.Wallet.VirtualCardMask.Should().EndWith("8842");
        summary.Wallet.AchRoutingMask.Should().EndWith("0021");
        summary.Wallet.AchAccountMask.Should().EndWith("7654");
        summary.RecentTransactions.Should().ContainSingle(t => t.Description == "Visit V2 stipend");
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    [GxPRequirement("PRD-SYS-182")]
    public async Task SelfServiceExpense_CompliantUnder100AutoApproves_AndPolicyBreachesHoldForReview()
    {
        await EnsureDbCreatedAsync();
        using var client = SubjectClient("SUBJ-EXP-01");

        // 1. Compliant $28.50 transit receipt with server-scanned receipt bytes -> Auto-approved + direct ACH payout
        byte[] transitPng = ReceiptOcrEngine.RenderReceiptPng(
        [
            "METRO TRANSIT",
            "DATE 2026-08-23",
            "TOTAL $28.50",
        ]);
        var transitScan = await PostOcrAsync(client, Convert.ToBase64String(transitPng), "transit_28_50.png");
        transitScan.OcrToken.Should().NotBeNullOrWhiteSpace();

        using var okResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "TRANSIT",
                MerchantName: "METRO TRANSIT",
                GrossAmount: 28.50m,
                ExpenseDate: "2026-08-23",
                OcrToken: transitScan.OcrToken,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        okResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = (await okResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        approved.Status.Should().Be("APPROVED");
        approved.AutoApproved.Should().BeTrue();
        approved.DisbursementReference.Should().StartWith("DISB-ACH-");
        approved.LifetimeReimbursements.Should().Be(28.50m);
        approved.LifetimeDisbursements.Should().Be(28.50m);

        // 2. Lodging > $100 -> PENDING_REVIEW
        using var highResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "LODGING",
                MerchantName: "Grand Hotel",
                GrossAmount: 145.00m,
                ExpenseDate: "2026-08-24",
                OcrConfidence: 0.97,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        var highClaim = (await highResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        highClaim.Status.Should().Be("PENDING_REVIEW");
        highClaim.AutoApproved.Should().BeFalse();
        highClaim.ReviewNotes.Should().Contain("exceeds $100.00 auto-approval threshold");

        // 3. Meal > $75 cap -> PENDING_REVIEW
        using var mealResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "MEAL",
                MerchantName: "Steakhouse",
                GrossAmount: 88.00m,
                IsMealAllowance: true,
                PerDiemCap: 75.00m,
                ExpenseDate: "2026-08-25",
                OcrConfidence: 0.96,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        var mealClaim = (await mealResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        mealClaim.Status.Should().Be("PENDING_REVIEW");
        mealClaim.ReviewNotes.Should().Contain("exceeds per-diem cap");
    }

    [Fact]
    [GxPRequirement("PRD-PAY-006")]
    public async Task SelfServiceDisburse_TransfersAvailableBalanceToZero_AndEnforcesW9Gate()
    {
        await EnsureDbCreatedAsync();
        await using (var seed = OpenDb())
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            seed.ParticipantWallets.Add(new ParticipantWallet
            {
                Id = "wallet-payout-01",
                SubjectId = "SUBJ-PAYOUT-01",
                StudyId = "STUDY-01",
                SiteId = "SITE-01",
                CurrentBalance = 75.00m,
                LifetimeStipends = 75.00m,
                Currency = "USD",
                Status = "ACTIVE",
                W9Status = "NOT_REQUIRED",
                CreatedAt = now,
                CreatedBy = "crc-1",
                UpdatedAt = now,
            });
            seed.ParticipantWallets.Add(new ParticipantWallet
            {
                Id = "wallet-payout-w9",
                SubjectId = "SUBJ-PAYOUT-W9",
                StudyId = "STUDY-01",
                SiteId = "SITE-01",
                CurrentBalance = 80.00m,
                LifetimeStipends = 680.00m,
                TaxableYtd = 680.00m,
                Is1099ThresholdExceeded = true,
                W9Status = "SUBMITTED",
                Currency = "USD",
                Status = "ACTIVE",
                CreatedAt = now,
                CreatedBy = "crc-1",
                UpdatedAt = now,
            });
            await seed.SaveChangesAsync(Ct);
        }

        using var client = SubjectClient("SUBJ-PAYOUT-01");
        using var disbResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/disburse",
            new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH"),
            Ct);
        disbResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var disb = (await disbResp.Content.ReadFromJsonAsync<SelfServiceDisburseResponseDto>(Ct))!;
        disb.Amount.Should().Be(75.00m);
        disb.CurrentBalance.Should().Be(0.00m);
        disb.ReferenceNumber.Should().StartWith("DISB-ACH-");

        // Duplicate payout attempt when balance is $0.00 fails cleanly with 409 Conflict
        using var emptyResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/disburse",
            new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH"),
            Ct);
        emptyResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Participant over $600 YTD without verified W-9 is blocked with 409 Conflict
        using var w9Client = SubjectClient("SUBJ-PAYOUT-W9");
        using var w9Resp = await w9Client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/disburse",
            new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH"),
            Ct);
        w9Resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    [GxPRequirement("PRD-PAY-006")]
    public async Task SelfServiceExpense_ReplayingSameIdempotencyKey_ReturnsPriorResult_IsolatesAcrossWallets_AndValidatesLength()
    {
        await EnsureDbCreatedAsync();
        using var clientA = SubjectClient("SUBJ-EXP-IDEM-01");
        using var clientB = SubjectClient("SUBJ-EXP-IDEM-02");

        // Oversized Idempotency-Key (> 255 chars) is rejected with 400 BadRequest
        using var oversizedReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/participant/me/expenses")
        {
            Content = JsonContent.Create(new SelfServiceSubmitExpenseRequestDto(
                Category: "TRANSIT",
                MerchantName: "Metro Transit",
                GrossAmount: 20.00m,
                ExpenseDate: "2026-08-23",
                OcrConfidence: 0.95,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01")),
        };
        oversizedReq.Headers.Add("Idempotency-Key", new string('k', 256));
        using var oversizedResp = await clientA.SendAsync(oversizedReq, Ct);
        oversizedResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        byte[] clientAReceipt = ReceiptOcrEngine.RenderReceiptPng(
        [
            "METRO TRANSIT",
            "DATE 2026-08-23",
            "TOTAL $28.50",
        ]);
        var autoRequest = new SelfServiceSubmitExpenseRequestDto(
            Category: "TRANSIT",
            MerchantName: "METRO TRANSIT",
            GrossAmount: 28.50m,
            ExpenseDate: "2026-08-23",
            ReceiptImageBase64: Convert.ToBase64String(clientAReceipt),
            StudyCode: "STUDY-01",
            SiteCode: "SITE-01");

        using var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/participant/me/expenses")
        {
            Content = JsonContent.Create(autoRequest),
        };
        firstReq.Headers.Add("Idempotency-Key", "exp-idem-auto-01");

        using var firstResp = await clientA.SendAsync(firstReq, Ct);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstClaim = (await firstResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        firstClaim.AutoApproved.Should().BeTrue();
        firstClaim.LifetimeReimbursements.Should().Be(28.50m);
        firstClaim.LifetimeDisbursements.Should().Be(28.50m);

        string snapshotAfterFirst = await SnapshotAsync();

        using var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/participant/me/expenses")
        {
            Content = JsonContent.Create(autoRequest),
        };
        replayReq.Headers.Add("Idempotency-Key", "exp-idem-auto-01");

        using var replayResp = await clientA.SendAsync(replayReq, Ct);
        replayResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayedClaim = (await replayResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        replayedClaim.Id.Should().Be(firstClaim.Id);
        replayedClaim.DisbursementReference.Should().Be(firstClaim.DisbursementReference);
        replayedClaim.LifetimeReimbursements.Should().Be(28.50m);
        replayedClaim.LifetimeDisbursements.Should().Be(28.50m);
        (await SnapshotAsync()).Should().Be(snapshotAfterFirst, "replaying an auto-approved expense Idempotency-Key must not create extra claims or payouts");

        // Another participant using the identical Idempotency-Key gets their own isolated wallet & claim
        byte[] clientBReceipt = ReceiptOcrEngine.RenderReceiptPng(
        [
            "CITY METRO RAIL",
            "DATE 2026-08-23",
            "TOTAL $28.50",
        ]);
        var clientBRequest = autoRequest with
        {
            MerchantName = "CITY METRO RAIL",
            ReceiptImageBase64 = Convert.ToBase64String(clientBReceipt),
        };
        using var otherSubjectReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/participant/me/expenses")
        {
            Content = JsonContent.Create(clientBRequest),
        };
        otherSubjectReq.Headers.Add("Idempotency-Key", "exp-idem-auto-01");
        using var otherSubjectResp = await clientB.SendAsync(otherSubjectReq, Ct);
        otherSubjectResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var otherSubjectClaim = (await otherSubjectResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        otherSubjectClaim.Id.Should().NotBe(firstClaim.Id);
        otherSubjectClaim.WalletId.Should().NotBe(firstClaim.WalletId);
        otherSubjectClaim.SubjectId.Should().Be("SUBJ-EXP-IDEM-02");

        // Body IdempotencyKey on a PENDING_REVIEW claim also deduplicates cleanly
        var pendingRequest = new SelfServiceSubmitExpenseRequestDto(
            Category: "LODGING",
            MerchantName: "Harbor Hotel",
            GrossAmount: 145.00m,
            ExpenseDate: "2026-08-24",
            OcrConfidence: 0.97,
            StudyCode: "STUDY-01",
            SiteCode: "SITE-01",
            IdempotencyKey: "exp-idem-pending-01");

        using var pendingResp1 = await clientA.PostAsJsonAsync("/api/v1/payments/participant/me/expenses", pendingRequest, Ct);
        pendingResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending1 = (await pendingResp1.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        pending1.Status.Should().Be("PENDING_REVIEW");

        string snapshotAfterPending = await SnapshotAsync();

        using var pendingResp2 = await clientA.PostAsJsonAsync("/api/v1/payments/participant/me/expenses", pendingRequest, Ct);
        pendingResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var pending2 = (await pendingResp2.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        pending2.Id.Should().Be(pending1.Id);
        pending2.Status.Should().Be("PENDING_REVIEW");
        (await SnapshotAsync()).Should().Be(snapshotAfterPending, "replaying a pending-review expense Idempotency-Key must not duplicate claims");
    }

    [Fact]
    [GxPRequirement("PRD-PAY-006")]
    public async Task SelfServiceDisburse_ReplayingSameIdempotencyKey_DeduplicatesPartialAndFullPayouts_AndIsolatesWallets()
    {
        await EnsureDbCreatedAsync();
        await using (var seed = OpenDb())
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            seed.ParticipantWallets.AddRange(
                new ParticipantWallet
                {
                    Id = "wallet-payout-idem-01",
                    SubjectId = "SUBJ-PAYOUT-IDEM-01",
                    StudyId = "STUDY-01",
                    SiteId = "SITE-01",
                    CurrentBalance = 100.00m,
                    LifetimeStipends = 100.00m,
                    Currency = "EUR",
                    Status = "ACTIVE",
                    W9Status = "NOT_REQUIRED",
                    VirtualCardMask = "5412750011119988",
                    AchAccountMask = "000998874321",
                    CreatedAt = now,
                    CreatedBy = "crc-1",
                    UpdatedAt = now,
                },
                new ParticipantWallet
                {
                    Id = "wallet-payout-idem-02",
                    SubjectId = "SUBJ-PAYOUT-IDEM-02",
                    StudyId = "STUDY-01",
                    SiteId = "SITE-01",
                    CurrentBalance = 50.00m,
                    LifetimeStipends = 50.00m,
                    Currency = "USD",
                    Status = "ACTIVE",
                    W9Status = "NOT_REQUIRED",
                    AchAccountMask = "000112237777",
                    CreatedAt = now,
                    CreatedBy = "crc-1",
                    UpdatedAt = now,
                });
            await seed.SaveChangesAsync(Ct);
        }

        using var client = SubjectClient("SUBJ-PAYOUT-IDEM-01");
        using var client2 = SubjectClient("SUBJ-PAYOUT-IDEM-02");

        // Oversized Idempotency-Key (> 255 chars) is rejected with 400 BadRequest
        using var oversizedResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/disburse",
            new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH", IdempotencyKey: new string('d', 256)),
            Ct);
        oversizedResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 1. Partial VIRTUAL_CARD payout of 40.00 EUR with Idempotency-Key header
        var partialPayload = new SelfServiceDisburseRequestDto(DisbursementType: "VIRTUAL_CARD", Amount: 40.00m);
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/participant/me/disburse")
        {
            Content = JsonContent.Create(partialPayload),
        };
        req1.Headers.Add("Idempotency-Key", "disb-idem-partial-01");

        using var resp1 = await client.SendAsync(req1, Ct);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var disb1 = (await resp1.Content.ReadFromJsonAsync<SelfServiceDisburseResponseDto>(Ct))!;
        disb1.Amount.Should().Be(40.00m);
        disb1.CurrentBalance.Should().Be(60.00m);
        disb1.Currency.Should().Be("EUR");
        disb1.DisbursementType.Should().Be("VIRTUAL_CARD");
        disb1.DestinationMask.Should().EndWith("9988");
        disb1.ReferenceNumber.Should().StartWith("DISB-VCARD-");

        string snapshotAfterPartial = await SnapshotAsync();

        // Replay partial payout -> returns identical disbursement without debiting another 40.00 EUR
        using var req1Replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/participant/me/disburse")
        {
            Content = JsonContent.Create(partialPayload),
        };
        req1Replay.Headers.Add("Idempotency-Key", "disb-idem-partial-01");

        using var resp1Replay = await client.SendAsync(req1Replay, Ct);
        resp1Replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var disb1Replay = (await resp1Replay.Content.ReadFromJsonAsync<SelfServiceDisburseResponseDto>(Ct))!;
        disb1Replay.Id.Should().Be(disb1.Id);
        disb1Replay.ReferenceNumber.Should().Be(disb1.ReferenceNumber);
        disb1Replay.CurrentBalance.Should().Be(60.00m);
        (await SnapshotAsync()).Should().Be(snapshotAfterPartial);

        // Distinct participant reusing the same Idempotency-Key executes independently on their own wallet
        using var client2Resp = await client2.PostAsJsonAsync(
            "/api/v1/payments/participant/me/disburse",
            new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH", Amount: 25.00m, IdempotencyKey: "disb-idem-partial-01"),
            Ct);
        client2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var client2Disb = (await client2Resp.Content.ReadFromJsonAsync<SelfServiceDisburseResponseDto>(Ct))!;
        client2Disb.Id.Should().NotBe(disb1.Id);
        client2Disb.WalletId.Should().Be("wallet-payout-idem-02");
        client2Disb.CurrentBalance.Should().Be(25.00m);

        // 2. Full remaining balance payout (60.00 EUR) draining wallet to 0.00 EUR
        var fullPayload = new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH", IdempotencyKey: "disb-idem-full-02");
        using var resp2 = await client.PostAsJsonAsync("/api/v1/payments/participant/me/disburse", fullPayload, Ct);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var disb2 = (await resp2.Content.ReadFromJsonAsync<SelfServiceDisburseResponseDto>(Ct))!;
        disb2.Amount.Should().Be(60.00m);
        disb2.CurrentBalance.Should().Be(0.00m);
        disb2.DestinationMask.Should().EndWith("4321");

        string snapshotAfterFull = await SnapshotAsync();

        // Replay full payout after wallet is $0.00 -> returns prior 200 OK instead of 409 Conflict
        using var resp2Replay = await client.PostAsJsonAsync("/api/v1/payments/participant/me/disburse", fullPayload, Ct);
        resp2Replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var disb2Replay = (await resp2Replay.Content.ReadFromJsonAsync<SelfServiceDisburseResponseDto>(Ct))!;
        disb2Replay.Id.Should().Be(disb2.Id);
        disb2Replay.Amount.Should().Be(60.00m);
        disb2Replay.CurrentBalance.Should().Be(0.00m);
        (await SnapshotAsync()).Should().Be(snapshotAfterFull);

        // New Idempotency-Key on empty wallet still returns 409 Conflict
        using var emptyNewKeyResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/disburse",
            new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH", IdempotencyKey: "disb-idem-empty-03"),
            Ct);
        emptyNewKeyResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [GxPRequirement("PRD-PAY-006")]
    public async Task SelfServiceDiaryStipend_UsesServerRule_EnforcesIdempotency_AndRejectsClientAmount()
    {
        await EnsureDbCreatedAsync();
        await using (var seed = OpenDb())
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            seed.ParticipantStipendRules.Add(new ParticipantStipendRule
            {
                StudyId = "STUDY-DIARY",
                VisitCode = "DIARY",
                VisitName = "Daily ePRO Diary",
                Amount = 30.00m,
                AntiCoercionCap = 500.00m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "finance-1",
                UpdatedAt = now,
            });
            await seed.SaveChangesAsync(Ct);
        }

        using var client = SubjectClient("SUBJ-DIARY-01");

        // Client-supplied amount is strictly rejected with 400 BadRequest
        using var forgedAmountResp = await client.PostAsync(
            "/api/v1/payments/participant/me/diary-stipend",
            new StringContent(
                """{"studyId":"STUDY-DIARY","siteId":"SITE-01","diaryId":"vas-01","sequenceNumber":1,"amount":999.00}""",
                Encoding.UTF8,
                "application/json"),
            Ct);
        forgedAmountResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Valid request without client amount credits strictly the $30.00 server rule
        using var validResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/diary-stipend",
            new
            {
                studyId = "STUDY-DIARY",
                siteId = "SITE-01",
                diaryId = "vas-01",
                sequenceNumber = 1,
                idempotencyKey = "seq_1_SUBJ-DIARY-01",
            },
            Ct);
        validResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var credited = (await validResp.Content.ReadFromJsonAsync<SelfServiceDiaryStipendResponseDto>(Ct))!;
        credited.CreditedAmount.Should().Be(30.00m);
        credited.CurrentBalance.Should().Be(30.00m);
        credited.Status.Should().Be("CREDITED");

        // Replay with same idempotency key does not double-credit
        using var replayResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/diary-stipend",
            new
            {
                studyId = "STUDY-DIARY",
                siteId = "SITE-01",
                diaryId = "vas-01",
                sequenceNumber = 1,
                idempotencyKey = "seq_1_SUBJ-DIARY-01",
            },
            Ct);
        replayResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = (await replayResp.Content.ReadFromJsonAsync<SelfServiceDiaryStipendResponseDto>(Ct))!;
        replayed.Status.Should().Be("ALREADY_PROCESSED");
        replayed.CurrentBalance.Should().Be(30.00m);
    }

    [Fact]
    [GxPRequirement("PRD-PAY-006")]
    public async Task CrossParticipantAndStaffEndpoints_FailAuthorizationWithZeroMutations()
    {
        await EnsureDbCreatedAsync();
        await using (var seed = OpenDb())
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            seed.ParticipantWallets.Add(new ParticipantWallet
            {
                Id = "wallet-victim",
                SubjectId = "SUBJ-VICTIM",
                StudyId = "STUDY-01",
                SiteId = "SITE-01",
                CurrentBalance = 250.00m,
                LifetimeStipends = 250.00m,
                Currency = "USD",
                Status = "ACTIVE",
                W9Status = "VERIFIED",
                CreatedAt = now,
                CreatedBy = "crc-1",
                UpdatedAt = now,
            });
            await seed.SaveChangesAsync(Ct);
        }

        string snapshotBefore = await SnapshotAsync();
        using var attacker = SubjectClient("SUBJ-ATTACKER");

        // Cross-participant attempts on self-service endpoints -> 403 Forbidden
        (await attacker.GetAsync("/api/v1/payments/participant/me/wallet?subjectId=SUBJ-VICTIM", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await attacker.PostAsJsonAsync(
            "/api/v1/payments/participant/me/disburse",
            new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH", SubjectId: "SUBJ-VICTIM", WalletId: "wallet-victim"),
            Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await attacker.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "TRANSIT",
                MerchantName: "Metro",
                GrossAmount: 20.00m,
                ExpenseDate: "2026-08-23",
                OcrConfidence: 0.98,
                SubjectId: "SUBJ-VICTIM",
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await attacker.PostAsJsonAsync(
            "/api/v1/payments/participant/me/diary-stipend",
            new { subjectId = "SUBJ-VICTIM", studyId = "STUDY-01", siteId = "SITE-01", sequenceNumber = 1 },
            Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Staff-only wallet & payment endpoints -> 403 Forbidden
        (await attacker.GetAsync("/api/v1/payments/participant/wallets", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await attacker.GetAsync("/api/v1/payments/participant/wallets/wallet-victim/balance", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await attacker.PostAsJsonAsync(
            "/api/v1/payments/participant/wallets/wallet-victim/verify-w9",
            new { notes = "forged" },
            Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await SnapshotAsync()).Should().Be(snapshotBefore, "rejected participant calls must not mutate any ledger rows");
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    [GxPRequirement("PRD-SYS-182")]
    [GxPRequirement("PRD-PAY-006")]
    public async Task ExpensePolicyAndDualReview_BlocksSpoofedOcrAndPerDiemCap_AndRejectsSelfApprovalBySubmittingCrc()
    {
        await EnsureDbCreatedAsync();
        using var subjectClient = SubjectClient("SUBJ-VERIFY-01");
        using var crcClient = _factory.CreateSignedClient(
            TestSecurityContext.Identity("crc-submitter-1", "crc") with { StudyIds = ["STUDY-01"], SiteIds = ["SITE-01"] });
        using var investigatorClient = _factory.CreateSignedClient(
            TestSecurityContext.Identity("pi-reviewer-1", "investigator") with { StudyIds = ["STUDY-01"], SiteIds = ["SITE-01"] });

        // 1. Subject spoofs ocrConfidence=0.99 / receiptOcrData and perDiemCap=100.00 without a readable receipt -> must NOT auto-approve or dispatch DIRECT_ACH
        using var spoofResp = await subjectClient.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new
            {
                category = "MEAL",
                merchantName = "Unverified Marker Cafe A",
                grossAmount = 95.00m,
                isMealAllowance = true,
                perDiemCap = 100.00m,
                expenseDate = "2026-11-21",
                ocrConfidence = 0.99,
                receiptOcrData = new { confidence_score = 0.99, ocr_readable = true },
                studyId = "STUDY-01",
                siteId = "SITE-01",
            },
            Ct);
        spoofResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var spoofClaim = (await spoofResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        spoofClaim.Status.Should().Be("PENDING_REVIEW");
        spoofClaim.AutoApproved.Should().BeFalse();
        spoofClaim.DisbursementReference.Should().BeNull();
        spoofClaim.TaxDeduction.Should().Be(3.00m);
        spoofClaim.NetAmount.Should().Be(92.00m);
        spoofClaim.ReviewNotes.Should().Contain("per-diem cap ($75.00)");

        // 2. Staff submits unscanned $95.00 MEAL claim with perDiemCap=100.00 -> must route to PENDING_REVIEW with $3.00 taxDeduction
        using var staffMealResp = await crcClient.PostAsJsonAsync(
            "/api/v1/payments/participant/expenses",
            new
            {
                subjectId = "SUBJ-VERIFY-02",
                studyId = "STUDY-01",
                siteId = "SITE-01",
                category = "MEAL",
                merchantName = "Staff Unscanned Meal",
                grossAmount = 95.00m,
                isMealAllowance = true,
                perDiemCap = 100.00m,
                expenseDate = "2026-11-21",
            },
            Ct);
        staffMealResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffMealClaim = (await staffMealResp.Content.ReadFromJsonAsync<ParticipantExpenseClaim>(Ct))!;
        staffMealClaim.Status.Should().Be("PENDING_REVIEW");
        staffMealClaim.DisbursementReference.Should().BeNull();
        staffMealClaim.TaxDeduction.Should().Be(3.00m);
        staffMealClaim.NetAmount.Should().Be(92.00m);

        // 3. Submitting CRC attempts to approve own $500.00 PENDING_REVIEW claim -> must return 409 Conflict
        using var highStaffResp = await crcClient.PostAsJsonAsync(
            "/api/v1/payments/participant/expenses",
            new
            {
                subjectId = "SUBJ-VERIFY-03",
                studyId = "STUDY-01",
                siteId = "SITE-01",
                category = "LODGING",
                merchantName = "Clinical Trial Lodging",
                grossAmount = 500.00m,
                expenseDate = "2026-11-21",
            },
            Ct);
        highStaffResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var highStaffClaim = (await highStaffResp.Content.ReadFromJsonAsync<ParticipantExpenseClaim>(Ct))!;
        highStaffClaim.Status.Should().Be("PENDING_REVIEW");

        using var selfApproveResp = await crcClient.PostAsJsonAsync(
            $"/api/v1/payments/participant/expenses/{highStaffClaim.Id}/review",
            new { status = "APPROVED", reviewNotes = "Self-approved by submitting CRC" },
            Ct);
        selfApproveResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // At single-coordinator sites, an assigned site investigator holds payments_expense:review and can approve the claim
        using var piApproveResp = await investigatorClient.PostAsJsonAsync(
            $"/api/v1/payments/participant/expenses/{highStaffClaim.Id}/review",
            new { status = "APPROVED", reviewNotes = "Dual-reviewed by site investigator" },
            Ct);
        piApproveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var approvedByPi = (await piApproveResp.Content.ReadFromJsonAsync<ParticipantExpenseClaim>(Ct))!;
        approvedByPi.Status.Should().Be("APPROVED");
        approvedByPi.ReviewedBy.Should().Be("pi-reviewer-1");
        approvedByPi.NetAmount.Should().Be(500.00m);
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    [GxPRequirement("PRD-SYS-182")]
    public async Task SelfServiceExpense_HoldsTokenReplayDuplicateReceiptDigestsBlurryScansAndManualEditsInPendingReview()
    {
        await EnsureDbCreatedAsync();
        using var client = SubjectClient("SUBJ-DEDUP-01");

        byte[] receiptPng = ReceiptOcrEngine.RenderReceiptPng(
        [
            "CAFE BISTRO",
            "DATE 2026-09-12",
            "TOTAL $42.00",
        ]);
        string receiptBase64 = Convert.ToBase64String(receiptPng);

        var scan1 = await PostOcrAsync(client, receiptBase64, "meal_42.png");
        scan1.OcrReadable.Should().BeTrue();
        scan1.OcrToken.Should().NotBeNullOrWhiteSpace();
        scan1.ReceiptSha256.Should().NotBeNullOrWhiteSpace();

        // First submission with valid server OCR token and meal <= $75 -> auto-approved + DIRECT_ACH
        using var firstResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "MEAL",
                MerchantName: "CAFE BISTRO",
                GrossAmount: 42.00m,
                IsMealAllowance: true,
                ExpenseDate: "2026-09-12",
                OcrToken: scan1.OcrToken,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstClaim = (await firstResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        firstClaim.AutoApproved.Should().BeTrue();
        firstClaim.DisbursementReference.Should().StartWith("DISB-ACH-");

        // Replaying the single-use OcrToken on a new claim holds for review (ocrConfidence = 0.0)
        using var tokenReplayResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "MEAL",
                MerchantName: "CAFE BISTRO",
                GrossAmount: 42.00m,
                IsMealAllowance: true,
                ExpenseDate: "2026-09-12",
                OcrToken: scan1.OcrToken,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        tokenReplayResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenReplayClaim = (await tokenReplayResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        tokenReplayClaim.Status.Should().Be("PENDING_REVIEW");
        tokenReplayClaim.AutoApproved.Should().BeFalse();
        tokenReplayClaim.DisbursementReference.Should().BeNull();
        tokenReplayClaim.ReviewNotes.Should().Contain("already consumed");

        // Re-scanning the exact same receipt image produces a fresh scanId, but duplicate SHA-256 digest holds in PENDING_REVIEW
        var scan2 = await PostOcrAsync(client, receiptBase64, "meal_42_copy.png");
        using var dupDigestResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "MEAL",
                MerchantName: "CAFE BISTRO",
                GrossAmount: 42.00m,
                IsMealAllowance: true,
                ExpenseDate: "2026-09-12",
                OcrToken: scan2.OcrToken,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        var dupDigestClaim = (await dupDigestResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        dupDigestClaim.Status.Should().Be("PENDING_REVIEW");
        dupDigestClaim.AutoApproved.Should().BeFalse();
        dupDigestClaim.ReviewNotes.Should().Contain("Duplicate receipt digest");

        // Blurry receipt scan followed by manual entry routes cleanly to PENDING_REVIEW (200 OK, ocrConfidence = 0.0) instead of failing
        byte[] blurryPng = ReceiptOcrEngine.RenderReceiptPng(
            ["PARKING GARAGE", "DATE 2026-09-13", "TOTAL $18.00"],
            blurRadius: 7);
        var blurryScan = await PostOcrAsync(client, Convert.ToBase64String(blurryPng), "blurry_parking.png");
        blurryScan.OcrReadable.Should().BeFalse();

        using var manualAfterBlurryResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "PARKING",
                MerchantName: "PARKING GARAGE",
                GrossAmount: 18.00m,
                ExpenseDate: "2026-09-13",
                OcrToken: blurryScan.OcrToken,
                ReceiptImageBase64: Convert.ToBase64String(blurryPng),
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        manualAfterBlurryResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var blurryClaim = (await manualAfterBlurryResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        blurryClaim.Status.Should().Be("PENDING_REVIEW");
        blurryClaim.AutoApproved.Should().BeFalse();

        // Sharp receipt scan followed by manual amount edit ($22.00 -> $68.00) holds mismatch in PENDING_REVIEW (ocrConfidence = 0.0)
        byte[] sharpTaxiPng = ReceiptOcrEngine.RenderReceiptPng(
        [
            "METRO CAB",
            "DATE 2026-09-14",
            "TOTAL $22.00",
        ]);
        var sharpTaxiScan = await PostOcrAsync(client, Convert.ToBase64String(sharpTaxiPng), "taxi_22.png");
        using var editedResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "TRANSIT",
                MerchantName: "METRO CAB",
                GrossAmount: 68.00m,
                ExpenseDate: "2026-09-14",
                OcrToken: sharpTaxiScan.OcrToken,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct);
        editedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var editedClaim = (await editedResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct))!;
        editedClaim.Status.Should().Be("PENDING_REVIEW");
        editedClaim.AutoApproved.Should().BeFalse();
        editedClaim.ReviewNotes.Should().Contain("differ from server-extracted receipt scan");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public async Task UnassignedOrCrossSiteSubject_FailsClosedWith403AcrossSelfServiceRoutes_WithZeroMutations()
    {
        await EnsureDbCreatedAsync();
        await using (var seed = OpenDb())
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            seed.ParticipantWallets.Add(new ParticipantWallet
            {
                Id = "wallet-scoped-subj",
                SubjectId = "SUBJ-SCOPED-01",
                StudyId = "STUDY-01",
                SiteId = "SITE-01",
                CurrentBalance = 90.00m,
                LifetimeStipends = 90.00m,
                Currency = "USD",
                Status = "ACTIVE",
                W9Status = "NOT_REQUIRED",
                CreatedAt = now,
                CreatedBy = "crc-1",
                UpdatedAt = now,
            });
            seed.ParticipantStipendRules.Add(new ParticipantStipendRule
            {
                StudyId = "STUDY-01",
                VisitCode = "DIARY",
                VisitName = "Daily ePRO Diary",
                Amount = 25.00m,
                AntiCoercionCap = 500.00m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "finance-1",
                UpdatedAt = now,
            });
            await seed.SaveChangesAsync(Ct);
        }

        string snapshotBefore = await SnapshotAsync();

        var unassignedExisting = TestSecurityContext.Identity("SUBJ-SCOPED-01", "subject") with { SiteIds = [] };
        var unassignedNew = TestSecurityContext.Identity("SUBJ-UNASSIGNED-NEW", "subject") with { SiteIds = [] };
        var wrongSiteExisting = TestSecurityContext.Identity("SUBJ-SCOPED-01", "subject") with { SiteIds = ["SITE-99"] };

        foreach (var identity in new[] { unassignedExisting, unassignedNew, wrongSiteExisting })
        {
            using var client = _factory.CreateSignedClient(identity);

            (await client.GetAsync("/api/v1/payments/participant/me/wallet", Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            if (identity.SiteIds.Count == 0)
            {
                (await client.PostAsJsonAsync(
                    "/api/v1/payments/participant/me/expenses/ocr-extract",
                    new OcrExtractRequestDto(RawText: "METRO\nFare Paid: $10.00"),
                    Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }

            (await client.PostAsJsonAsync(
                "/api/v1/payments/participant/me/expenses",
                new SelfServiceSubmitExpenseRequestDto(
                    Category: "TRANSIT",
                    MerchantName: "Metro",
                    GrossAmount: 18.00m,
                    ExpenseDate: "2026-08-23",
                    OcrConfidence: 0.96,
                    StudyCode: "STUDY-01",
                    SiteCode: "SITE-01"),
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await client.PostAsJsonAsync(
                "/api/v1/payments/participant/me/disburse",
                new SelfServiceDisburseRequestDto(DisbursementType: "DIRECT_ACH"),
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await client.PostAsJsonAsync(
                "/api/v1/payments/participant/me/diary-stipend",
                new { studyId = "STUDY-01", siteId = "SITE-01", diaryId = "vas-01", sequenceNumber = 1 },
                Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await SnapshotAsync()).Should().Be(snapshotBefore, "unassigned or cross-site subject calls must fail closed without mutating ledger rows");
    }

    private HttpClient SubjectClient(string subjectId, params string[] siteIds) =>
        _factory.CreateSignedClient(TestSecurityContext.Identity(subjectId, "subject") with
        {
            SiteIds = siteIds.Length > 0 ? siteIds : ["SITE-01"],
        });

    private PaymentsDbContext OpenDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<PaymentsDbContext>();

    private async Task EnsureDbCreatedAsync()
    {
        await using var db = OpenDb();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    private static async Task<ReceiptOcrExtractResponseDto> PostOcrAsync(HttpClient client, string imageBase64, string filename)
    {
        using var resp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses/ocr-extract",
            new OcrExtractRequestDto(ImageBase64: imageBase64, Filename: filename),
            Ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<ReceiptOcrExtractResponseDto>(Ct);
        dto.Should().NotBeNull();
        return dto!;
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = OpenDb();
        var wallets = await db.ParticipantWallets.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => $"{w.Id}:{w.CurrentBalance}:{w.LifetimeStipends}:{w.LifetimeDisbursements}:{w.VersionIndex}")
            .ToListAsync(Ct);
        return string.Join(
            " | ",
            $"audit={await db.PaymentAuditLogs.CountAsync(Ct)}",
            $"claims={await db.ParticipantExpenseClaims.CountAsync(Ct)}",
            $"tx={await db.ParticipantTransactions.CountAsync(Ct)}",
            $"disb={await db.ParticipantDisbursements.CountAsync(Ct)}",
            $"wallets=[{string.Join(",", wallets)}]");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
