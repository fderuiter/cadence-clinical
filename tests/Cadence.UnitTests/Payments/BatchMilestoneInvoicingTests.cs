using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application.Services;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public sealed class BatchMilestoneInvoicingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

    public BatchMilestoneInvoicingTests()
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

    private async Task SeedDataAsync(PaymentsDbContext db)
    {
        DateTimeOffset now = _clock.UtcNow;

        // Active budget contract for SITE-101
        db.BudgetContracts.Add(new BudgetContract
        {
            Id = "CONTRACT-001",
            StudyId = "STUDY-ONC-201",
            SiteId = "SITE-101",
            ContractName = "Site 101 Grant Contract",
            IsActive = true,
            Status = "ACTIVE",
            TotalBudget = 50000m,
            OverheadPercentage = 25m,
            WithholdingPercentage = 10m,
            CreatedAt = now,
            CreatedBy = "test_admin",
            UpdatedAt = now,
            VersionIndex = 1
        });

        // Active budget contract for SITE-102
        db.BudgetContracts.Add(new BudgetContract
        {
            Id = "CONTRACT-002",
            StudyId = "STUDY-ONC-201",
            SiteId = "SITE-102",
            ContractName = "Site 102 Grant Contract",
            IsActive = true,
            Status = "ACTIVE",
            TotalBudget = 75000m,
            OverheadPercentage = 20m,
            WithholdingPercentage = 10m,
            CreatedAt = now,
            CreatedBy = "test_admin",
            UpdatedAt = now,
            VersionIndex = 1
        });

        // Pending payables for SITE-101
        db.Payables.AddRange(
            new Payable
            {
                Id = "PAY-101-01",
                StudyId = "STUDY-ONC-201",
                SiteId = "SITE-101",
                ContractId = "CONTRACT-001",
                SubjectId = "SUBJ-101",
                VisitCode = "V1_SCREENING",
                ProcedureCode = "PROC-01",
                ProcedureName = "Screening Blood Panel",
                TriggerType = "VISIT_COMPLETED",
                TriggerEventId = "EVT-101",
                BaseAmount = 1000m,
                OverheadAmount = 250m,
                GrossAmount = 1250m,
                WithholdingRate = 10m,
                WithholdingAmount = 125m,
                NetAmount = 1125m,
                Currency = "USD",
                Status = "TRIGGERED",
                CreatedAt = now,
                CreatedBy = "edc_service",
                UpdatedAt = now,
                VersionIndex = 1
            },
            new Payable
            {
                Id = "PAY-101-02",
                StudyId = "STUDY-ONC-201",
                SiteId = "SITE-101",
                ContractId = "CONTRACT-001",
                SubjectId = "SUBJ-102",
                VisitCode = "V2_DOSING",
                ProcedureCode = "PROC-02",
                ProcedureName = "Investigational Drug Infusion",
                TriggerType = "VISIT_COMPLETED",
                TriggerEventId = "EVT-102",
                BaseAmount = 2000m,
                OverheadAmount = 500m,
                GrossAmount = 2500m,
                WithholdingRate = 10m,
                WithholdingAmount = 250m,
                NetAmount = 2250m,
                Currency = "USD",
                Status = "TRIGGERED",
                CreatedAt = now,
                CreatedBy = "edc_service",
                UpdatedAt = now,
                VersionIndex = 1
            }
        );

        // Pending payables for SITE-102
        db.Payables.Add(new Payable
        {
            Id = "PAY-102-01",
            StudyId = "STUDY-ONC-201",
            SiteId = "SITE-102",
            ContractId = "CONTRACT-002",
            SubjectId = "SUBJ-201",
            VisitCode = "V1_SCREENING",
            ProcedureCode = "PROC-01",
            ProcedureName = "Screening Biopsy",
            TriggerType = "VISIT_COMPLETED",
            TriggerEventId = "EVT-201",
            BaseAmount = 1500m,
            OverheadAmount = 300m,
            GrossAmount = 1800m,
            WithholdingRate = 10m,
            WithholdingAmount = 180m,
            NetAmount = 1620m,
            Currency = "USD",
            Status = "TRIGGERED",
            CreatedAt = now,
            CreatedBy = "edc_service",
            UpdatedAt = now,
            VersionIndex = 1
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task BatchCompileBillingCycles_CompilesAllActiveSites_AndCreatesInvoicesAndAuditLogs()
    {
        await using var db = CreateContext();
        await SeedDataAsync(db);

        var request = new BatchBillingCycleCompilationRequest(
            StudyId: "STUDY-ONC-201",
            BillingCycle: "MONTHLY",
            PeriodStart: "2026-08-01",
            PeriodEnd: "2026-08-31",
            AutoApprove: true,
            SiteIds: null
        );

        var result = await PaymentEndpointsTestInvoker.InvokeBatchCompileAsync(request, db, _clock);

        result.Should().NotBeNull();
        result.StudyId.Should().Be("STUDY-ONC-201");
        result.TotalSitesProcessed.Should().Be(2);
        result.SuccessfulSitesCount.Should().Be(2);
        result.FailedSitesCount.Should().Be(0);
        result.TotalInvoicesGenerated.Should().Be(2);
        result.TotalPayablesCompiled.Should().Be(3);

        // Financial precision check: Total Gross = (1250 + 2500) + 1800 = 5550
        result.TotalGrossAmount.Should().Be(5550m);
        // Total Holdback = (125 + 250) + 180 = 555
        result.TotalHoldbackAmount.Should().Be(555m);
        // Total Net = 5550 - 555 = 4995
        result.TotalNetAmount.Should().Be(4995m);

        // Verify SelfBillingInvoices persisted in DB
        var invoices = await db.SelfBillingInvoices.Where(i => i.StudyId == "STUDY-ONC-201").ToListAsync(TestContext.Current.CancellationToken);
        invoices.Should().HaveCount(2);
        invoices.All(i => i.Status == "APPROVED").Should().BeTrue();

        // Verify Payables marked as INVOICED
        var payables = await db.Payables.Where(p => p.StudyId == "STUDY-ONC-201").ToListAsync(TestContext.Current.CancellationToken);
        payables.All(p => p.Status == "INVOICED").Should().BeTrue();

        // Verify Audit Logs recorded for batch execution and each site, including verified step-up signature metadata
        var auditLogs = await db.PaymentAuditLogs.ToListAsync(TestContext.Current.CancellationToken);
        auditLogs.Should().Contain(a => a.EntityType == "SelfBillingInvoiceBatch" && a.EntityId == result.BatchExecutionId);
        var siteAudits = auditLogs.Where(a => a.EntityType == "SelfBillingInvoice" && a.Action == "BATCH_SITE_INVOICE_COMPILED").ToList();
        siteAudits.Should().HaveCount(2);
        foreach (var siteAudit in siteAudits)
        {
            using var detailsDoc = JsonDocument.Parse(siteAudit.Details!);
            detailsDoc.RootElement.GetProperty("signature_subject").GetString().Should().Be("finance-1");
            detailsDoc.RootElement.GetProperty("signature_token_id").GetString().Should().NotBeNullOrWhiteSpace();
            detailsDoc.RootElement.GetProperty("signature_semantic_action").GetString().Should().Be("payments.invoice.approve");
        }
    }

    [Fact]
    public async Task CompileHandlers_RejectAutoApproveWithoutVerifiedSigToken_AndWriteNothing()
    {
        await using var db = CreateContext();
        await SeedDataAsync(db);

        var service = new InvoiceCompilationService(db, new PaymentsUnitOfWork(db), _clock);

        var singleResult = await InvoiceEndpoints.CompileBillingCycle(
            new BillingCycleCompilationRequest(
                StudyId: "STUDY-ONC-201",
                SiteId: "SITE-101",
                BillingCycle: "MONTHLY",
                AutoApprove: true),
            service,
            PaymentEndpointsTestInvoker.FinanceOfficerContext());
        var singleProblem = singleResult.Should().BeOfType<ProblemHttpResult>().Subject;
        singleProblem.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        singleProblem.ProblemDetails.Detail.Should().Be(SigTokenVerifier.ReauthenticationRequired);

        var batchResult = await InvoiceEndpoints.BatchCompileBillingCycles(
            new BatchBillingCycleCompilationRequest(
                StudyId: "STUDY-ONC-201",
                BillingCycle: "MONTHLY",
                AutoApprove: true),
            service,
            PaymentEndpointsTestInvoker.FinanceOfficerContext());
        var batchProblem = batchResult.Should().BeOfType<ProblemHttpResult>().Subject;
        batchProblem.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        batchProblem.ProblemDetails.Detail.Should().Be(SigTokenVerifier.ReauthenticationRequired);

        (await db.SelfBillingInvoices.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Payables.AllAsync(p => p.Status == "TRIGGERED", TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task BatchCompileBillingCycles_IsolatesErrorsOnSingleSite_AndContinuesOtherSites()
    {
        await using var db = CreateContext();
        await SeedDataAsync(db);

        // Explicitly include SITE-101 (valid payables), SITE-102 (valid payables), and SITE-999 (no payables -> error)
        var request = new BatchBillingCycleCompilationRequest(
            StudyId: "STUDY-ONC-201",
            BillingCycle: "MONTHLY",
            PeriodStart: "2026-08-01",
            PeriodEnd: "2026-08-31",
            AutoApprove: false,
            SiteIds: new List<string> { "SITE-101", "SITE-999", "SITE-102" }
        );

        var result = await PaymentEndpointsTestInvoker.InvokeBatchCompileAsync(request, db, _clock);

        result.Should().NotBeNull();
        result.TotalSitesProcessed.Should().Be(3);
        result.SuccessfulSitesCount.Should().Be(2);
        result.FailedSitesCount.Should().Be(1);
        result.TotalInvoicesGenerated.Should().Be(2);

        var failedSite = result.SiteResults.FirstOrDefault(s => s.SiteId == "SITE-999");
        failedSite.Should().NotBeNull();
        failedSite!.Success.Should().BeFalse();
        failedSite.ErrorMessage.Should().Contain("No pending achieved milestone payables");

        var successSite1 = result.SiteResults.FirstOrDefault(s => s.SiteId == "SITE-101");
        successSite1.Should().NotBeNull();
        successSite1!.Success.Should().BeTrue();
        successSite1.PayableCount.Should().Be(2);

        var successSite2 = result.SiteResults.FirstOrDefault(s => s.SiteId == "SITE-102");
        successSite2.Should().NotBeNull();
        successSite2!.Success.Should().BeTrue();
        successSite2.PayableCount.Should().Be(1);
    }

    [Fact]
    public async Task SingleCompileBillingCycle_CreatesInvoiceLineItemsAndRemittanceAdvice()
    {
        await using var db = CreateContext();
        await SeedDataAsync(db);

        var request = new BillingCycleCompilationRequest(
            StudyId: "STUDY-ONC-201",
            SiteId: "SITE-101",
            BillingCycle: "MONTHLY",
            PeriodStart: "2026-08-01",
            PeriodEnd: "2026-08-31",
            AutoApprove: false
        );

        var invoiceResponse = await PaymentEndpointsTestInvoker.InvokeCompileAsync(request, db, _clock);

        invoiceResponse.Should().NotBeNull();
        invoiceResponse.StudyId.Should().Be("STUDY-ONC-201");
        invoiceResponse.SiteId.Should().Be("SITE-101");
        invoiceResponse.PayableCount.Should().Be(2);
        invoiceResponse.Status.Should().Be("DRAFT");

        // Verify line items map to milestone payables
        invoiceResponse.LineItems.Should().HaveCount(2);
        invoiceResponse.TotalGrossAmount.Should().Be(3750m);
        invoiceResponse.TotalHoldbackAmount.Should().Be(375m);
        invoiceResponse.TotalNetAmount.Should().Be(3375m);

        // Verify Remittance Advice exists and has cryptographic hash
        invoiceResponse.RemittanceAdvice.Should().NotBeNull();
        invoiceResponse.RemittanceAdvice!.PdfSha256Hash.Should().NotBeNullOrEmpty();
        invoiceResponse.RemittanceAdvice.IsSealed.Should().BeTrue();
    }
}

public static class PaymentEndpointsTestInvoker
{
    /// <summary>The finance officer compiling invoices, as the gateway signs them (#5361).</summary>
    public static ClaimsPrincipal FinanceOfficer { get; } = TestSecurityContext.CreateScopedPrincipal(
        "finance-1", TestSecurityContext.AllStudies, [], "Grants Manager");

    public static HttpContext FinanceOfficerContext(
        bool withVerifiedApproveSignature = false,
        string action = "/api/v1/payments/invoices/compile",
        DateTimeOffset? issuedAt = null)
    {
        var context = new DefaultHttpContext { User = FinanceOfficer };
        if (withVerifiedApproveSignature)
        {
            string rawToken = TestSecurityContext.CreateSigToken(
                userId: "finance-1",
                action: action,
                semanticAction: InvoiceEndpoints.InvoiceApproveSemanticAction,
                roles: ["finance_officer"],
                issuedAt: issuedAt);
            context.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] = new JwtSecurityTokenHandler().ReadJwtToken(rawToken);
        }

        return context;
    }

    public static async Task<BatchBillingCompilationResponse> InvokeBatchCompileAsync(
        BatchBillingCycleCompilationRequest request, PaymentsDbContext db, IClock clock)
    {
        var service = new InvoiceCompilationService(db, new PaymentsUnitOfWork(db), clock);
        var context = FinanceOfficerContext(
            withVerifiedApproveSignature: request.AutoApprove,
            action: "/api/v1/payments/invoices/batch-compile",
            issuedAt: clock.UtcNow);
        var result = await InvoiceEndpoints.BatchCompileBillingCycles(request, service, context);

        var createdResult = result as Created<BatchBillingCompilationResponse>;
        createdResult.Should().NotBeNull("Method should return Created<BatchBillingCompilationResponse>");
        return createdResult!.Value!;
    }

    public static async Task<SelfBillingInvoiceResponse> InvokeCompileAsync(
        BillingCycleCompilationRequest request, PaymentsDbContext db, IClock clock)
    {
        var service = new InvoiceCompilationService(db, new PaymentsUnitOfWork(db), clock);
        var context = FinanceOfficerContext(
            withVerifiedApproveSignature: request.AutoApprove,
            action: "/api/v1/payments/invoices/compile",
            issuedAt: clock.UtcNow);
        var result = await InvoiceEndpoints.CompileBillingCycle(request, service, context);

        var createdResult = result as Created<SelfBillingInvoiceResponse>;
        createdResult.Should().NotBeNull("Method should return Created<SelfBillingInvoiceResponse>");
        return createdResult!.Value!;
    }
}
