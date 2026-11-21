using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Payments;

/// <summary>
/// Payments reaches only the studies and sites the gateway signed for the caller (#5401, ADR-2516), through the real
/// payments host on PostgreSQL as the application role. Before the fix a CRC at one site could read and act on every
/// site's participants, and a finance officer, sponsor data manager or auditor could read every study's money.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PaymentsStudySiteScopeTests(PaymentsPostgresApiFactory factory) : IClassFixture<PaymentsPostgresApiFactory>
{
    private const string Base = "/api/v1/payments";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A CRC assigned to site A gets 404 reading or acting on a site-B wallet or claim, the same answer as a missing row,
    /// and nothing is written.
    /// </summary>
    [Theory]
    [InlineData("GET", "/participant/wallets/{wallet}", null)]
    [InlineData("GET", "/participant/wallets/{wallet}/balance", null)]
    [InlineData("GET", "/participant/wallets/{wallet}/tax-1099-status", null)]
    [InlineData("GET", "/participant/wallets/subject/{subject}", null)]
    [InlineData("POST", "/participant/wallets/{wallet}/issue-card", """{"cardType":"RELOADABLE_DEBIT"}""")]
    [InlineData("POST", "/participant/wallets/{wallet}/submit-w9", """{"fullLegalName":"P Two","taxIdLastFour":"9999","certifyAccurate":true}""")]
    [InlineData("POST", "/participant/wallets/{wallet}/verify-w9", """{"notes":"ok"}""")]
    [InlineData("POST", "/participant/expenses/{claim}/review", """{"status":"APPROVED"}""")]
    public async Task ACrcAtSiteA_GetsNotFoundForASiteBParticipant_AndNothingIsWritten(string method, string route, string? body)
    {
        factory.SkipIfUnavailable();
        var study = await SeedStudyAsync();
        string before = await SnapshotAsync(study);
        using var client = Client(Crc(study.SiteA));

        string path = Base + route
            .Replace("{wallet}", study.WalletB, StringComparison.Ordinal)
            .Replace("{subject}", study.SubjectB, StringComparison.Ordinal)
            .Replace("{claim}", study.ClaimB, StringComparison.Ordinal);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(Ct));
        (await SnapshotAsync(study)).Should().Be(before, "a caller outside the record's site must not change any payments row");
    }

    /// <summary>
    /// A CRC at site A cannot credit a stipend or file a claim at site B, nor reach a site-B participant by naming their
    /// own site in the body. Nothing is written and no audit row is added.
    /// </summary>
    [Theory]
    [InlineData("/participant/events/visit-verified", """{"studyId":"{study}","siteId":"{siteB}","subjectId":"SUBJ-NEW","visitCode":"V1","eventId":"{event}"}""")]
    [InlineData("/participant/events/visit-verified", """{"studyId":"{study}","siteId":"{siteA}","subjectId":"{subjectB}","visitCode":"V1","eventId":"{event}"}""")]
    [InlineData("/participant/expenses", """{"subjectId":"SUBJ-NEW","studyId":"{study}","siteId":"{siteB}","category":"TRANSIT","merchantName":"Metro","grossAmount":20.00}""")]
    [InlineData("/participant/expenses", """{"subjectId":"{subjectB}","studyId":"{study}","siteId":"{siteA}","category":"TRANSIT","merchantName":"Metro","grossAmount":20.00}""")]
    public async Task ACrcAtSiteA_IsRefusedWritesForSiteB_AndNothingIsWritten(string route, string template)
    {
        factory.SkipIfUnavailable();
        var study = await SeedStudyAsync();
        string before = await SnapshotAsync(study);
        using var client = Client(Crc(study.SiteA));
        string body = template
            .Replace("{study}", study.StudyId, StringComparison.Ordinal)
            .Replace("{siteA}", study.SiteA, StringComparison.Ordinal)
            .Replace("{siteB}", study.SiteB, StringComparison.Ordinal)
            .Replace("{subjectB}", study.SubjectB, StringComparison.Ordinal)
            .Replace("{event}", $"EVT-{Guid.NewGuid():N}", StringComparison.Ordinal);

        using var response = await client.PostAsync(Base + route, new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await SnapshotAsync(study)).Should().Be(before);
    }

    /// <summary>Lists hold only the caller's site: a site-A CRC never sees site-B wallets, claims or transactions.</summary>
    [Fact]
    public async Task ACrcAtSiteA_SeesOnlySiteAInLists()
    {
        factory.SkipIfUnavailable();
        var study = await SeedStudyAsync();
        using var client = Client(Crc(study.SiteA));

        (await IdsAsync(client, "/participant/wallets")).Should().Equal(study.WalletA);
        (await IdsAsync(client, "/participant/expenses")).Should().Equal(study.ClaimA);
        (await IdsAsync(client, "/participant/transactions")).Should().Equal(study.TransactionA);
        (await IdsAsync(client, $"/participant/transactions?walletId={study.WalletB}")).Should().BeEmpty();
        (await IdsAsync(client, $"/participant/disbursements?walletId={study.WalletB}")).Should().BeEmpty();
    }

    /// <summary>A site-scoped role with no signed site reaches no participant: scope fails closed.</summary>
    [Fact]
    public async Task ASiteScopedRoleWithNoSite_ReachesNothing()
    {
        factory.SkipIfUnavailable();
        var study = await SeedStudyAsync();
        using var client = Client(Crc());

        (await IdsAsync(client, "/participant/wallets")).Should().BeEmpty();
        (await client.GetAsync($"{Base}/participant/wallets/{study.WalletA}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A finance officer, sponsor data manager or auditor not assigned to the study sees none of its rows and gets 404 on
    /// its ids; finance cannot compile, approve, reconcile, export or configure for it either. Nothing is written.
    /// </summary>
    [Theory]
    [InlineData("Grants Manager")]
    [InlineData("sponsor_dm")]
    [InlineData("auditor")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task ACallerOutsideTheStudy_SeesNoRows_AndGetsNotFoundOnItsIds(string role)
    {
        factory.SkipIfUnavailable();
        var study = await SeedStudyAsync();
        string before = await SnapshotAsync(study);
        using var client = Client(TestSecurityContext.Identity($"outsider-{Guid.NewGuid():N}", role) with { StudyIds = [$"S-OTHER-{Guid.NewGuid():N}"] });

        (await IdsAsync(client, "/participant/wallets")).Should().BeEmpty();
        (await IdsAsync(client, "/participant/expenses")).Should().BeEmpty();
        (await IdsAsync(client, "/invoices")).Should().BeEmpty();
        (await client.GetAsync($"{Base}/invoices/{study.InvoiceA}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{Base}/invoices/{study.InvoiceA}/remittance-advice/pdf", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{Base}/participant/wallets/{study.WalletA}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        if (role == "auditor")
        {
            (await IdsAsync(client, "/audit-logs")).Should().BeEmpty();
        }

        if (role == "Grants Manager")
        {
            (await client.PostAsync($"{Base}/invoices/{study.InvoiceA}/approve", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsJsonAsync($"{Base}/invoices/{study.InvoiceA}/reconcile", new { payment_reference = "WIRE-9" }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsync($"{Base}/invoices/{study.InvoiceA}/erp-export", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsJsonAsync($"{Base}/invoices/compile", new { study_id = study.StudyId, site_id = study.SiteA }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"{Base}/invoices/batch-compile", new { study_id = study.StudyId }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"{Base}/participant/stipend-rules", new { studyId = study.StudyId, visitCode = "V9", visitName = "Visit 9", amount = 50.00m }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await SnapshotAsync(study)).Should().Be(before);
    }

    /// <summary>
    /// The same flows still work in scope: a finance officer assigned to the study approves, exports and reconciles its
    /// invoice and reads the resulting audit rows, and a site-A CRC credits a stipend at site A.
    /// </summary>
    [Fact]
    public async Task InScopeCallers_StillCompleteTheirFlows()
    {
        factory.SkipIfUnavailable();
        var study = await SeedStudyAsync();
        using (var finance = Client(TestSecurityContext.Identity("finance-1", "Grants Manager") with { StudyIds = [study.StudyId] }))
        {
            (await finance.PostAsync($"{Base}/invoices/{study.InvoiceA}/approve", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await finance.PostAsync($"{Base}/invoices/{study.InvoiceA}/erp-export", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await finance.PostAsJsonAsync($"{Base}/invoices/{study.InvoiceA}/reconcile", new { payment_reference = "WIRE-1" }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await IdsAsync(finance, "/audit-logs")).Should().HaveCount(3);
        }

        using var crc = Client(Crc(study.SiteA));
        using var credit = await crc.PostAsJsonAsync(
            $"{Base}/participant/events/visit-verified",
            new { studyId = study.StudyId, siteId = study.SiteA, subjectId = study.SubjectA, visitCode = "V1", eventId = $"EVT-{Guid.NewGuid():N}" },
            Ct);
        credit.StatusCode.Should().Be(HttpStatusCode.OK, await credit.Content.ReadAsStringAsync(Ct));
        (await credit.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().Should().Be("CREDITED");
    }

    private sealed record SeededStudy(string StudyId, string SiteA, string SiteB, string SubjectA, string SubjectB)
    {
        public string WalletA => $"W-{SubjectA}";
        public string WalletB => $"W-{SubjectB}";
        public string ClaimA => $"CLM-{SubjectA}";
        public string ClaimB => $"CLM-{SubjectB}";
        public string TransactionA => $"TX-{SubjectA}";
        public string InvoiceA => $"INV-{StudyId}";
    }

    private static GatewayIdentity Crc(params string[] sites) =>
        TestSecurityContext.Identity($"crc-{Guid.NewGuid():N}", "crc") with { SiteIds = sites };

    private HttpClient Client(GatewayIdentity identity) => factory.CreateSignedClient(identity);

    private static async Task<string[]> IdsAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(Base + route, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var rows = await response.Content.ReadFromJsonAsync<JsonElement[]>(Ct);
        return rows!.Select(r => r.GetProperty("id").GetString()!).ToArray();
    }

    /// <summary>
    /// A fresh study with two sites, each with a participant wallet, an expense claim and a stipend credit, a stipend rule
    /// for visit V1, and a site-A invoice with its remittance advice.
    /// </summary>
    private async Task<SeededStudy> SeedStudyAsync()
    {
        string tag = Guid.NewGuid().ToString("N")[..12];
        var study = new SeededStudy($"S-{tag}", $"SITE-A-{tag}", $"SITE-B-{tag}", $"SUBJ-A-{tag}", $"SUBJ-B-{tag}");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;

        foreach (var (subject, site) in new[] { (study.SubjectA, study.SiteA), (study.SubjectB, study.SiteB) })
        {
            db.ParticipantWallets.Add(new ParticipantWallet
            {
                Id = $"W-{subject}",
                SubjectId = subject,
                StudyId = study.StudyId,
                SiteId = site,
                CurrentBalance = 50.00m,
                Currency = "USD",
                Status = "ACTIVE",
                W9Status = "SUBMITTED",
                CreatedAt = now,
                CreatedBy = "crc-seed",
                UpdatedAt = now,
            });
            db.ParticipantExpenseClaims.Add(new ParticipantExpenseClaim
            {
                Id = $"CLM-{subject}",
                WalletId = $"W-{subject}",
                SubjectId = subject,
                StudyId = study.StudyId,
                SiteId = site,
                ExpenseDate = "2026-10-01",
                MerchantName = "Metro",
                GrossAmount = 150.00m,
                NetAmount = 150.00m,
                Status = "PENDING_REVIEW",
                CreatedAt = now,
                CreatedBy = "crc-seed",
                UpdatedAt = now,
            });
            db.ParticipantTransactions.Add(new ParticipantTransaction
            {
                Id = $"TX-{subject}",
                WalletId = $"W-{subject}",
                SubjectId = subject,
                StudyId = study.StudyId,
                TransactionType = "STIPEND_CREDIT",
                Amount = 10m,
                ReferenceId = $"EVT-SEED-{subject}",
                CreatedAt = now,
                CreatedBy = "crc-seed",
            });
        }

        db.ParticipantStipendRules.Add(new ParticipantStipendRule
        {
            StudyId = study.StudyId,
            VisitCode = "V1",
            VisitName = "Screening",
            Amount = 75.00m,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = "finance-seed",
            UpdatedAt = now,
        });
        db.SelfBillingInvoices.Add(new SelfBillingInvoice
        {
            Id = study.InvoiceA,
            InvoiceNumber = $"INV-{tag}",
            StudyId = study.StudyId,
            SiteId = study.SiteA,
            ContractId = "C-1",
            PeriodStart = "2026-10-01",
            PeriodEnd = "2026-10-31",
            TotalGrossAmount = 1000m,
            TotalNetAmount = 900m,
            Status = "DRAFT",
            CreatedAt = now,
            CreatedBy = "finance-seed",
            UpdatedAt = now,
        });
        db.RemittanceAdvices.Add(new RemittanceAdvice
        {
            Id = $"REM-{tag}",
            RemittanceNumber = $"REM-{tag}",
            InvoiceId = study.InvoiceA,
            StudyId = study.StudyId,
            SiteId = study.SiteA,
            NetAmount = 900m,
            PdfSha256Hash = "00",
            CreatedAt = now,
            CreatedBy = "finance-seed",
            UpdatedAt = now,
        });
        db.Payables.Add(new Payable
        {
            Id = $"PAY-{tag}",
            StudyId = study.StudyId,
            SiteId = study.SiteA,
            ContractId = "C-1",
            SubjectId = study.SubjectA,
            VisitCode = "V1",
            TriggerType = "VISIT_COMPLETED",
            TriggerEventId = $"EVT-PAY-{tag}",
            BaseAmount = 100m,
            GrossAmount = 100m,
            NetAmount = 100m,
            Currency = "USD",
            Status = "TRIGGERED",
            CreatedAt = now,
            CreatedBy = "edc-seed",
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(Ct);
        return study;
    }

    /// <summary>Every payments row of the study a route could add or change, plus the audit rows about them, as one value.</summary>
    private async Task<string> SnapshotAsync(SeededStudy study)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        string s = study.StudyId;
        var wallets = await db.ParticipantWallets.AsNoTracking().Where(w => w.StudyId == s).OrderBy(w => w.Id)
            .Select(w => $"{w.Id}:{w.CurrentBalance}:{w.W9Status}:{w.CardStatus}:{w.VersionIndex}").ToListAsync(Ct);
        var walletIds = await db.ParticipantWallets.AsNoTracking().Where(w => w.StudyId == s).Select(w => w.Id).ToListAsync(Ct);
        var claims = await db.ParticipantExpenseClaims.AsNoTracking().Where(c => c.StudyId == s).OrderBy(c => c.Id)
            .Select(c => $"{c.Id}:{c.Status}:{c.ReviewedBy}:{c.VersionIndex}").ToListAsync(Ct);
        var invoices = await db.SelfBillingInvoices.AsNoTracking().Where(i => i.StudyId == s).OrderBy(i => i.Id)
            .Select(i => $"{i.Id}:{i.Status}:{i.ApprovedBy}:{i.ReconciledBy}:{i.ErpSyncStatus}:{i.VersionIndex}").ToListAsync(Ct);
        var entityIds = walletIds.Concat(invoices.Select(i => i.Split(':')[0])).ToList();

        return string.Join(
            " | ",
            $"audit={await db.PaymentAuditLogs.CountAsync(a => entityIds.Contains(a.EntityId), Ct)}",
            $"rules={await db.ParticipantStipendRules.CountAsync(r => r.StudyId == s, Ct)}",
            $"transactions={await db.ParticipantTransactions.CountAsync(t => t.StudyId == s, Ct)}",
            $"disbursements={await db.ParticipantDisbursements.CountAsync(d => walletIds.Contains(d.WalletId), Ct)}",
            $"wallets=[{string.Join(",", wallets)}]",
            $"claims=[{string.Join(",", claims)}]",
            $"invoices=[{string.Join(",", invoices)}]",
            $"payables=[{string.Join(",", await db.Payables.AsNoTracking().Where(p => p.StudyId == s).Select(p => p.Status).ToListAsync(Ct))}]");
    }
}
