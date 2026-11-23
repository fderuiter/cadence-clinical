using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
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
/// The Payments host over SQLite, signed as real callers (#5361, ADR-2516): a caller whose roles lack a write route's
/// permission is refused with 403 and nothing is written, and what is written records the signed caller, not a literal
/// or a name from the request body.
/// </summary>
public sealed class PaymentsCallerAuthorizationTests : IAsyncDisposable
{
    private const string WalletId = "W-1";
    private const string InvoiceId = "INV-1";
    private const string ClaimId = "CLM-1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly WebApplicationFactory<Cadence.Apps.Payments.Program> _factory;

    public PaymentsCallerAuthorizationTests()
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

    /// <summary>
    /// Every mutating Payments route, with a body the handler would act on and a realistic role that lacks the route's
    /// permission.
    /// </summary>
    public static TheoryData<string, string, string> MutatingRoutesAndARoleWithoutThem => new()
    {
        { "/api/v1/payments/disburse", """{"subjectId":"SUBJ-1","amount":25.00,"idempotencyKey":"k-1"}""", "crc" },
        { "/api/v1/payments/participant/wallets/disburse", """{"subjectId":"SUBJ-9","amount":25.00}""", "Site Investigator" },
        { $"/api/v1/payments/participant/wallets/{WalletId}/disburse", """{"disbursementType":"DIRECT_ACH","amount":40.00}""", "crc" },
        { $"/api/v1/payments/participant/wallets/{WalletId}/disburse", """{"disbursementType":"DIRECT_ACH","amount":40.00}""", "subject" },
        { $"/api/v1/payments/participant/wallets/{WalletId}/issue-card", """{"cardType":"RELOADABLE_DEBIT"}""", "auditor" },
        { $"/api/v1/payments/participant/wallets/{WalletId}/submit-w9", """{"fullLegalName":"P One","taxIdLastFour":"1234"}""", "subject" },
        { $"/api/v1/payments/participant/wallets/{WalletId}/verify-w9", """{"notes":"ok"}""", "Grants Manager" },
        { "/api/v1/payments/participant/stipend-rules", """{"studyId":"S-1","visitCode":"V9","visitName":"Visit 9","amount":50.00}""", "crc" },
        { "/api/v1/payments/participant/events/visit-verified", """{"studyId":"S-1","siteId":"SITE-1","subjectId":"SUBJ-1","visitCode":"V1","eventId":"EVT-1"}""", "sponsor_dm" },
        { "/api/v1/payments/participant/expenses", """{"subjectId":"SUBJ-1","studyId":"S-1","siteId":"SITE-1","category":"TRANSIT","merchantName":"Metro","grossAmount":20.00}""", "subject" },
        { "/api/v1/payments/participant/expenses/ocr-extract", """{"rawText":"METRO\nFare Paid: $10.00"}""", "auditor" },
        { $"/api/v1/payments/participant/expenses/{ClaimId}/review", """{"status":"APPROVED"}""", "Grants Manager" },
        { $"/api/v1/payments/participant/expenses/{ClaimId}/review", """{"status":"APPROVED"}""", "auditor" },
        { "/api/v1/payments/invoices/compile", """{"study_id":"S-1","site_id":"SITE-1"}""", "sponsor_dm" },
        { "/api/v1/payments/invoices/batch-compile", """{"study_id":"S-1"}""", "crc" },
        { $"/api/v1/payments/invoices/{InvoiceId}/approve", "{}", "sponsor_dm" },
        { $"/api/v1/payments/invoices/{InvoiceId}/approve", "{}", "auditor" },
        { $"/api/v1/payments/invoices/{InvoiceId}/remittance-advice/sign", """{"signature_reason":"r"}""", "sysadmin" },
        { $"/api/v1/payments/invoices/{InvoiceId}/reconcile", """{"payment_reference":"WIRE-1"}""", "investigator" },
        { $"/api/v1/payments/invoices/{InvoiceId}/erp-export", "{}", "sysadmin" },
    };

    [Theory]
    [MemberData(nameof(MutatingRoutesAndARoleWithoutThem))]
    public async Task ACallerWithoutTheRole_Gets403_AndNothingIsWritten(string path, string body, string role)
    {
        await SeedAsync();
        string before = await SnapshotAsync();

        using var client = Client("no-role-user", role);
        using var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SnapshotAsync()).Should().Be(before, "a refused caller must not change any payments row");
    }

    [Fact]
    public async Task StipendRuleCreate_RecordsTheSignedCaller()
    {
        await SeedAsync();
        using var client = Client("finance-1", "Grants Manager");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/stipend-rules",
            new { studyId = "S-1", visitCode = "V9", visitName = "Visit 9", amount = 50.00m },
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = OpenDb();
        var rule = await db.ParticipantStipendRules.AsNoTracking().SingleAsync(r => r.VisitCode == "V9", Ct);
        rule.CreatedBy.Should().Be("finance-1");
        var audit = await db.PaymentAuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == rule.Id, Ct);
        audit.UserId.Should().Be("finance-1");
        audit.UserRoles.Should().Be("finance_officer", "the gateway signs the canonical role for the realm's \"Grants Manager\"");
        audit.CreatedBy.Should().Be("finance-1");
    }

    [Fact]
    public async Task InvoiceApproval_RecordsTheSignedCaller()
    {
        await SeedAsync();
        string approvePath = $"/api/v1/payments/invoices/{InvoiceId}/approve";
        using var client = _factory.CreateSignedClient(TestSecurityContext.Identity("finance-1", "Grants Manager") with
        {
            SigToken = TestSecurityContext.CreateSigToken("finance-1", approvePath, "payments.invoice.approve", ["finance_officer"]),
        });

        using var response = await client.PostAsync(approvePath, null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = OpenDb();
        var invoice = await db.SelfBillingInvoices.AsNoTracking().SingleAsync(i => i.Id == InvoiceId, Ct);
        invoice.Status.Should().Be("APPROVED");
        invoice.ApprovedBy.Should().Be("finance-1");
        var audit = await db.PaymentAuditLogs.AsNoTracking().SingleAsync(a => a.Action == "INVOICE_APPROVED", Ct);
        audit.UserId.Should().Be("finance-1");
        audit.UserRoles.Should().Be("finance_officer", "the gateway signs the canonical role for the realm's \"Grants Manager\"");
    }

    [Fact]
    public async Task RemittanceSignature_IsTheSignedCaller_NotTheBodysName_AndNotTheApprover()
    {
        await SeedAsync();
        string approvePath = $"/api/v1/payments/invoices/{InvoiceId}/approve";
        string signPath = $"/api/v1/payments/invoices/{InvoiceId}/remittance-advice/sign";
        using (var approver = _factory.CreateSignedClient(TestSecurityContext.Identity("finance-1", "Grants Manager") with
        {
            SigToken = TestSecurityContext.CreateSigToken("finance-1", approvePath, "payments.invoice.approve", ["finance_officer"]),
        }))
        {
            (await approver.PostAsync(approvePath, null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var approverSigner = _factory.CreateSignedClient(TestSecurityContext.Identity("finance-1", "Grants Manager") with
        {
            SigToken = TestSecurityContext.CreateSigToken("finance-1", signPath, "payments.remittance.sign", ["finance_officer"]),
        }))
        {
            using var ownSignature = await approverSigner.PostAsJsonAsync(
                signPath, new { signature_reason = "Approve and sign" }, Ct);
            ownSignature.StatusCode.Should().Be(HttpStatusCode.Conflict, "the approver may not also sign the remittance");
        }

        await using (var db = OpenDb())
        {
            (await db.RemittanceAdvices.AsNoTracking().SingleAsync(r => r.InvoiceId == InvoiceId, Ct)).IsSigned.Should().BeFalse();
        }

        using var signer = _factory.CreateSignedClient(TestSecurityContext.Identity("finance-2", "finance officer") with
        {
            SigToken = TestSecurityContext.CreateSigToken("finance-2", signPath, "payments.remittance.sign", ["finance_officer"]),
        });
        using var response = await signer.PostAsync(
            signPath,
            new StringContent("""{"signer_name":"Mallory","signer_role":"CFO","signature_reason":"Remittance statement"}""", Encoding.UTF8, "application/json"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var after = OpenDb();
        var remittance = await after.RemittanceAdvices.AsNoTracking().SingleAsync(r => r.InvoiceId == InvoiceId, Ct);
        remittance.IsSigned.Should().BeTrue();
        remittance.SignedBy.Should().Be("finance-2");
        remittance.SignerRole.Should().Be("finance_officer");
        var audit = await after.PaymentAuditLogs.AsNoTracking().SingleAsync(a => a.Action == "REMITTANCE_ADVICE_SIGNED", Ct);
        audit.UserId.Should().Be("finance-2");
        audit.UserRoles.Should().Be("finance_officer");
        audit.Details.Should().NotContain("Mallory");
    }

    [Fact]
    public async Task VisitVerified_RecordsTheSignedCoordinator_NotABodyField()
    {
        await SeedAsync();
        await using (var seed = OpenDb())
        {
            DateTimeOffset now = SystemClock.Instance.UtcNow;
            seed.ParticipantStipendRules.Add(new ParticipantStipendRule
            {
                StudyId = "S-1",
                VisitCode = "V1",
                VisitName = "Screening",
                Amount = 75.00m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "finance-1",
                UpdatedAt = now,
            });
            await seed.SaveChangesAsync(Ct);
        }

        using var client = SiteClient("crc-7", "SITE-1", "Clinical Research Coordinator");
        using var response = await client.PostAsync(
            "/api/v1/payments/participant/events/visit-verified",
            new StringContent(
                """{"studyId":"S-1","siteId":"SITE-1","subjectId":"SUBJ-2","visitCode":"V1","eventId":"EVT-7","verifiedBy":"someone-else"}""",
                Encoding.UTF8,
                "application/json"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = OpenDb();
        var credit = await db.ParticipantTransactions.AsNoTracking().SingleAsync(t => t.ReferenceId == "EVT-7", Ct);
        credit.CreatedBy.Should().Be("crc-7");
        var audits = await db.PaymentAuditLogs.AsNoTracking().Where(a => a.EntityId == credit.WalletId).ToListAsync(Ct);
        audits.Should().NotBeEmpty();
        audits.Should().OnlyContain(a => a.UserId == "crc-7" && a.UserRoles == "crc");
    }

    [Fact]
    public async Task StipendDisbursement_RecordsTheFinanceCaller_NotTheParticipant()
    {
        await SeedAsync();
        using var client = Client("finance-1", "Grants Manager");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/payments/disburse", new { subjectId = "SUBJ-1", amount = 25.00m, idempotencyKey = "k-77" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = OpenDb();
        var audit = await db.PaymentAuditLogs.AsNoTracking().SingleAsync(a => a.Action == "AUTO_STIPEND_DISBURSEMENT", Ct);
        audit.UserId.Should().Be("finance-1");
        audit.UserRoles.Should().Be("finance_officer", "the gateway signs the canonical role for the realm's \"Grants Manager\"");
        audit.CreatedBy.Should().Be("finance-1");
    }

    [Fact]
    public async Task ExpenseReview_RecordsTheSignedReviewer()
    {
        await SeedAsync();
        using var client = SiteClient("pi-3", "SITE-1", "principal investigator");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/payments/participant/expenses/{ClaimId}/review", new { status = "REJECTED", reviewNotes = "No receipt" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = OpenDb();
        var claim = await db.ParticipantExpenseClaims.AsNoTracking().SingleAsync(c => c.Id == ClaimId, Ct);
        claim.Status.Should().Be("REJECTED");
        claim.ReviewedBy.Should().Be("pi-3");
        var audit = await db.PaymentAuditLogs.AsNoTracking().SingleAsync(a => a.Action == "EXPENSE_CLAIM_REJECTED", Ct);
        audit.UserId.Should().Be("pi-3");
        audit.UserRoles.Should().Be("principal_investigator");
    }

    private HttpClient Client(string userId, params string[] roles) =>
        _factory.CreateSignedClient(TestSecurityContext.Identity(userId, roles));

    private HttpClient SiteClient(string userId, string site, params string[] roles) =>
        _factory.CreateSignedClient(TestSecurityContext.Identity(userId, roles) with { SiteIds = [site] });

    private PaymentsDbContext OpenDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<PaymentsDbContext>();

    private async Task SeedAsync()
    {
        await using var db = OpenDb();
        await db.Database.EnsureCreatedAsync(Ct);
        DateTimeOffset now = SystemClock.Instance.UtcNow;

        db.ParticipantWallets.Add(new ParticipantWallet
        {
            Id = WalletId,
            SubjectId = "SUBJ-1",
            StudyId = "S-1",
            SiteId = "SITE-1",
            CurrentBalance = 100.00m,
            Currency = "USD",
            Status = "ACTIVE",
            W9Status = "SUBMITTED",
            CreatedAt = now,
            CreatedBy = "crc-1",
            UpdatedAt = now,
        });
        db.ParticipantExpenseClaims.Add(new ParticipantExpenseClaim
        {
            Id = ClaimId,
            WalletId = WalletId,
            SubjectId = "SUBJ-1",
            StudyId = "S-1",
            SiteId = "SITE-1",
            ExpenseDate = "2026-10-01",
            MerchantName = "Metro",
            GrossAmount = 150.00m,
            NetAmount = 150.00m,
            Status = "PENDING_REVIEW",
            CreatedAt = now,
            CreatedBy = "crc-1",
            UpdatedAt = now,
        });
        db.SelfBillingInvoices.Add(new SelfBillingInvoice
        {
            Id = InvoiceId,
            InvoiceNumber = "INV-S-1-SITE-1-000001",
            StudyId = "S-1",
            SiteId = "SITE-1",
            ContractId = "C-1",
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-31",
            TotalGrossAmount = 1000m,
            TotalNetAmount = 900m,
            Status = "DRAFT",
            CreatedAt = now,
            CreatedBy = "finance-0",
            UpdatedAt = now,
        });
        db.RemittanceAdvices.Add(new RemittanceAdvice
        {
            Id = "REM-1",
            RemittanceNumber = "REM-INV-1",
            InvoiceId = InvoiceId,
            StudyId = "S-1",
            SiteId = "SITE-1",
            NetAmount = 900m,
            PdfSha256Hash = "00",
            CreatedAt = now,
            CreatedBy = "finance-0",
            UpdatedAt = now,
        });
        // A pending payable, so a compile that slipped past authorization would write an invoice.
        db.Payables.Add(new Payable
        {
            Id = "PAY-1",
            StudyId = "S-1",
            SiteId = "SITE-1",
            ContractId = "C-1",
            SubjectId = "SUBJ-1",
            VisitCode = "V1",
            TriggerType = "VISIT_COMPLETED",
            TriggerEventId = "EVT-0",
            BaseAmount = 100m,
            GrossAmount = 100m,
            NetAmount = 100m,
            Currency = "USD",
            Status = "TRIGGERED",
            CreatedAt = now,
            CreatedBy = "edc-1",
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Every payments row a write route could add or change, as one comparable value.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var db = OpenDb();
        var wallets = await db.ParticipantWallets.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => $"{w.Id}:{w.CurrentBalance}:{w.W9Status}:{w.CardStatus}:{w.VersionIndex}").ToListAsync(Ct);
        var claims = await db.ParticipantExpenseClaims.AsNoTracking().OrderBy(c => c.Id)
            .Select(c => $"{c.Id}:{c.Status}:{c.ReviewedBy}:{c.VersionIndex}").ToListAsync(Ct);
        var invoices = await db.SelfBillingInvoices.AsNoTracking().OrderBy(i => i.Id)
            .Select(i => $"{i.Id}:{i.Status}:{i.ApprovedBy}:{i.ReconciledBy}:{i.ErpSyncStatus}:{i.VersionIndex}").ToListAsync(Ct);
        var remittances = await db.RemittanceAdvices.AsNoTracking().OrderBy(r => r.Id)
            .Select(r => $"{r.Id}:{r.IsSigned}:{r.SignedBy}:{r.VersionIndex}").ToListAsync(Ct);
        var payables = await db.Payables.AsNoTracking().OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.Status}").ToListAsync(Ct);

        return string.Join(
            " | ",
            $"audit={await db.PaymentAuditLogs.CountAsync(Ct)}",
            $"rules={await db.ParticipantStipendRules.CountAsync(Ct)}",
            $"transactions={await db.ParticipantTransactions.CountAsync(Ct)}",
            $"disbursements={await db.ParticipantDisbursements.CountAsync(Ct)}",
            $"line_items={await db.InvoiceLineItems.CountAsync(Ct)}",
            $"erp={await db.ErpSyncLogs.CountAsync(Ct)}",
            $"wallets=[{string.Join(",", wallets)}]",
            $"claims=[{string.Join(",", claims)}]",
            $"invoices=[{string.Join(",", invoices)}]",
            $"remittances=[{string.Join(",", remittances)}]",
            $"payables=[{string.Join(",", payables)}]");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
