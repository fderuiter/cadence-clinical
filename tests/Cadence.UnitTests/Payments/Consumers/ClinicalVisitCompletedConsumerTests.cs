using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Consumers;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Payments.Consumers;

public sealed class ClinicalVisitCompletedConsumerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

    public ClinicalVisitCompletedConsumerTests()
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

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateContext());
        services.AddSingleton<IClock>(_clock);
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<ClinicalVisitCompletedConsumer>()
                .Endpoint(e => e.ConcurrentMessageLimit = 1);
        });
        return services.BuildServiceProvider();
    }

    private async Task SeedContractAndFeeScheduleAsync(PaymentsDbContext db)
    {
        DateTimeOffset now = _clock.UtcNow;

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
            Currency = "USD",
            CreatedAt = now,
            CreatedBy = "admin",
            UpdatedAt = now,
            VersionIndex = 1
        });

        db.FeeSchedules.Add(new FeeSchedule
        {
            Id = "FEE-001",
            ContractId = "CONTRACT-001",
            VisitCode = "IMV",
            ProcedureCode = "IMV_MONITORING",
            ProcedureName = "Interim Monitoring Visit",
            BaseAmount = 1000m,
            OverheadPercentage = 25m,
            WithholdingPercentage = 10m,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = "admin",
            UpdatedAt = now,
            VersionIndex = 1
        });

        db.SplitPayeeConfigs.AddRange(
            new SplitPayeeConfig
            {
                Id = "SPLIT-001",
                ContractId = "CONTRACT-001",
                PayeeId = "INST-1",
                PayeeName = "Institution",
                PayeeRole = "INSTITUTION",
                AllocationPercentage = 60m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "admin",
                UpdatedAt = now,
                VersionIndex = 1
            },
            new SplitPayeeConfig
            {
                Id = "SPLIT-002",
                ContractId = "CONTRACT-001",
                PayeeId = "PI-1",
                PayeeName = "Principal Investigator",
                PayeeRole = "PI",
                AllocationPercentage = 40m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "admin",
                UpdatedAt = now,
                VersionIndex = 1
            }
        );

        await db.SaveChangesAsync();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ClinicalVisitCompleted_CreatesTriggeredPayablesAllocationsAndHoldbacks()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await SeedContractAndFeeScheduleAsync(db);

        await using var provider = CreateServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ClinicalVisitCompleted(
            EventId: "MVR-VISIT-001",
            Study: "STUDY-ONC-201",
            Site: "SITE-101",
            VisitCode: "IMV",
            CompletedAt: _clock.UtcNow,
            CraId: "cra-user-1",
            Signer: "supervisor-1",
            VisitId: "VISIT-001"
        );

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<ClinicalVisitCompleted>(ct)).Should().BeTrue();

        // Criterion 1: A signed visit report event creates corresponding Payable records in TRIGGERED status
        var payable = await db.Payables.FirstOrDefaultAsync(p => p.TriggerEventId == "MVR-VISIT-001", ct);
        payable.Should().NotBeNull();
        payable!.Status.Should().Be("TRIGGERED");
        payable.StudyId.Should().Be("STUDY-ONC-201");
        payable.SiteId.Should().Be("SITE-101");
        payable.ContractId.Should().Be("CONTRACT-001");
        payable.VisitCode.Should().Be("IMV");
        payable.ProcedureCode.Should().Be("IMV_MONITORING");
        payable.ProcedureName.Should().Be("Interim Monitoring Visit");
        payable.BaseAmount.Should().Be(1000m);
        payable.OverheadAmount.Should().Be(250m);
        payable.GrossAmount.Should().Be(1250m);
        payable.WithholdingAmount.Should().Be(125m);
        payable.NetAmount.Should().Be(1000m);
        payable.Currency.Should().Be("USD");
        payable.TriggerType.Should().Be("VISIT_COMPLETED");
        payable.TriggerSource.Should().Be("CTMS_SERVICE");

        // Requirement 4: Verify PayableAllocation records
        var allocations = await db.PayableAllocations.Where(a => a.PayableId == payable.Id).ToListAsync(ct);
        allocations.Should().HaveCount(2);
        allocations.Should().Contain(a => a.PayeeId == "INST-1" && a.AllocationPercentage == 60m && a.Status == "ALLOCATED");
        allocations.Should().Contain(a => a.PayeeId == "PI-1" && a.AllocationPercentage == 40m && a.Status == "ALLOCATED");

        // Requirement 4: Verify HoldbackReserve records
        var holdbacks = await db.HoldbackReserves.Where(h => h.PayableId == payable.Id).ToListAsync(ct);
        holdbacks.Should().HaveCount(1);
        holdbacks[0].HoldbackAmount.Should().Be(125m);
        holdbacks[0].Status.Should().Be("HELD");
        holdbacks[0].StudyId.Should().Be("STUDY-ONC-201");
        holdbacks[0].SiteId.Should().Be("SITE-101");

        // Requirement 6: Verify PaymentAuditLogs immutable entry
        var auditLogs = await db.PaymentAuditLogs.Where(a => a.Action == "MILESTONE_PAYABLE_GENERATED").ToListAsync(ct);
        auditLogs.Should().HaveCount(1);
        auditLogs[0].UserId.Should().Be("supervisor-1");
        auditLogs[0].Details.Should().Contain("MVR-VISIT-001");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ClinicalVisitCompleted_IsIdempotentOnDuplicateTriggerEventId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await SeedContractAndFeeScheduleAsync(db);

        await using var provider = CreateServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ClinicalVisitCompleted(
            EventId: "MVR-VISIT-001",
            Study: "STUDY-ONC-201",
            Site: "SITE-101",
            VisitCode: "IMV",
            CompletedAt: _clock.UtcNow,
            CraId: "cra-user-1",
            Signer: "supervisor-1",
            VisitId: "VISIT-001"
        );

        // First delivery
        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<ClinicalVisitCompleted>(ct)).Should().BeTrue();

        var countFirst = await db.Payables.CountAsync(p => p.TriggerEventId == "MVR-VISIT-001", ct);
        countFirst.Should().Be(1);

        // Second delivery (duplicate replay)
        Guid replayMessageId = Guid.NewGuid();
        await harness.Bus.Publish(message, ctx => ctx.MessageId = replayMessageId, ct);
        (await harness.Consumed.Any<ClinicalVisitCompleted>(x => x.Context.MessageId == replayMessageId, ct)).Should().BeTrue();

        // Criterion 2: Duplicate events with identical TriggerEventId values produce no duplicate payables
        var countSecond = await db.Payables.CountAsync(p => p.TriggerEventId == "MVR-VISIT-001", ct);
        countSecond.Should().Be(1);

        var allocCount = await db.PayableAllocations.CountAsync(ct);
        allocCount.Should().Be(2);

        var holdbackCount = await db.HoldbackReserves.CountAsync(ct);
        holdbackCount.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ClinicalVisitCompleted_WithUnmatchedVisitCode_LogsAuditAndDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await SeedContractAndFeeScheduleAsync(db);

        await using var provider = CreateServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ClinicalVisitCompleted(
            EventId: "MVR-VISIT-999",
            Study: "STUDY-ONC-201",
            Site: "SITE-101",
            VisitCode: "UNSCHEDULED_VISIT_UNMATCHED",
            CompletedAt: _clock.UtcNow,
            CraId: "cra-user-1",
            Signer: "supervisor-1",
            VisitId: "VISIT-999"
        );

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<ClinicalVisitCompleted>(ct)).Should().BeTrue();

        // Criterion 3: Unmatched visit codes log an explanatory audit record and raise no unhandled exceptions
        var payables = await db.Payables.Where(p => p.TriggerEventId == "MVR-VISIT-999").ToListAsync(ct);
        payables.Should().BeEmpty();

        var audit = await db.PaymentAuditLogs.FirstOrDefaultAsync(a => a.Action == "UNMATCHED_VISIT_CODE", ct);
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain("UNSCHEDULED_VISIT_UNMATCHED");
        audit.Details.Should().Contain("MVR-VISIT-999");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_ClinicalVisitCompleted_SubsequentInvoiceCompile_IncludesGeneratedPayables()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await SeedContractAndFeeScheduleAsync(db);

        await using var provider = CreateServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new ClinicalVisitCompleted(
            EventId: "MVR-VISIT-001",
            Study: "STUDY-ONC-201",
            Site: "SITE-101",
            VisitCode: "IMV",
            CompletedAt: _clock.UtcNow,
            CraId: "cra-user-1",
            Signer: "supervisor-1",
            VisitId: "VISIT-001"
        );

        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<ClinicalVisitCompleted>(ct)).Should().BeTrue();

        // Verify payable exists in TRIGGERED status
        var payable = await db.Payables.FirstOrDefaultAsync(p => p.TriggerEventId == "MVR-VISIT-001", ct);
        payable.Should().NotBeNull();
        payable!.Status.Should().Be("TRIGGERED");

        // Criterion 4: Subsequent calls to POST /invoices/compile include the generated payables automatically
        var compilationRequest = new BillingCycleCompilationRequest(
            StudyId: "STUDY-ONC-201",
            SiteId: "SITE-101",
            BillingCycle: "MONTHLY",
            PeriodStart: "2026-10-01",
            PeriodEnd: "2026-10-31",
            AutoApprove: false
        );

        var invoice = await PaymentEndpointsTestInvoker.InvokeCompileAsync(compilationRequest, db, _clock);

        invoice.Should().NotBeNull();
        invoice.StudyId.Should().Be("STUDY-ONC-201");
        invoice.SiteId.Should().Be("SITE-101");
        invoice.PayableCount.Should().Be(1);
        invoice.TotalBaseAmount.Should().Be(1000m);
        invoice.TotalOverheadAmount.Should().Be(250m);
        invoice.TotalGrossAmount.Should().Be(1250m);
        invoice.TotalHoldbackAmount.Should().Be(125m);
        invoice.TotalNetAmount.Should().Be(1125m);

        // Verify the payable in DB transitioned to INVOICED
        var reloadedPayable = await db.Payables.FindAsync([payable.Id], ct);
        reloadedPayable!.Status.Should().Be("INVOICED");
    }
}
