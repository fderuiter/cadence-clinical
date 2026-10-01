using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application.Services;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Xunit;

namespace Cadence.UnitTests.Payments;

public class BulkFeeScheduleImporterTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    private static PaymentsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new PaymentsDbContext(options, TestFieldProtection.CreateEncryptor());
    }

    [Fact]
    [GxPRequirement("PRD-SYS-161")]
    public void GenerateCsvTemplate_ReturnsValidHeaderAndSampleRows()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = new BulkFeeScheduleImporterService(dbContext, _clock);

        // Act
        string csv = service.GenerateCsvTemplate("STUDY-ONC-99", "SITE-888");

        // Assert
        csv.Should().Contain("StudyId,SiteId,VisitCode,ProcedureCode,ProcedureName");
        csv.Should().Contain("STUDY-ONC-99,SITE-888,V1_SCREENING,PROC-101");
        csv.Should().Contain("150.00,25.00,10.00,50.00,25.00,true");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-161")]
    public void ValidateCsv_ValidInput_CalculatesLineItemsAndNoDiscrepancies()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = new BulkFeeScheduleImporterService(dbContext, _clock);

        string csv = """
            StudyId,SiteId,VisitCode,ProcedureCode,ProcedureName,BaseAmount,OverheadPercentage,WithholdingPercentage,PatientStipend,TravelReimbursement,RequiresSdv
            STUDY-001,SITE-101,V1_SCREENING,PROC-101,Lab Draw,200.00,25.00,10.00,50.00,20.00,true
            STUDY-001,SITE-101,V1_SCREENING,PROC-102,ECG,100.00,20.00,10.00,0.00,0.00,false
            """;

        // Act
        var result = service.ValidateCsv(csv);

        // Assert
        result.IsValid.Should().BeTrue();
        result.TotalRows.Should().Be(2);
        result.ValidRowsCount.Should().Be(2);
        result.InvalidRowsCount.Should().Be(0);

        // Row 1: Base 200, Overhead 25% = 50, Gross = 250, Withholding 10% = 25, Net = 225
        var row1 = result.Rows[0];
        row1.BaseAmount.Should().Be(200.00m);
        row1.OverheadAmount.Should().Be(50.00m);
        row1.GrossAmount.Should().Be(250.00m);
        row1.WithholdingAmount.Should().Be(25.00m);
        row1.NetPayable.Should().Be(225.00m);
        row1.IsValid.Should().BeTrue();

        // Row 2: Base 100, Overhead 20% = 20, Gross = 120, Withholding 10% = 12, Net = 108
        var row2 = result.Rows[1];
        row2.BaseAmount.Should().Be(100.00m);
        row2.OverheadAmount.Should().Be(20.00m);
        row2.GrossAmount.Should().Be(120.00m);
        row2.WithholdingAmount.Should().Be(12.00m);
        row2.NetPayable.Should().Be(108.00m);
        row2.IsValid.Should().BeTrue();

        result.TotalEstimatedBudget.Should().Be(370.00m); // 250 + 120
    }

    [Fact]
    [GxPRequirement("PRD-SYS-161")]
    public void ValidateCsv_InvalidRows_CapturesRowLevelValidationErrors()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = new BulkFeeScheduleImporterService(dbContext, _clock);

        string csv = """
            StudyId,SiteId,VisitCode,ProcedureCode,ProcedureName,BaseAmount,OverheadPercentage,WithholdingPercentage,PatientStipend,TravelReimbursement,RequiresSdv
            STUDY-001,SITE-101,V1_SCREENING,,Missing Procedure Code,150.00,25.00,10.00,0.00,0.00,false
            STUDY-001,SITE-101,V1_SCREENING,PROC-102,Negative Base Fee,-50.00,25.00,10.00,0.00,0.00,false
            STUDY-001,SITE-101,V1_SCREENING,PROC-103,Invalid Overhead Rate,100.00,150.00,10.00,0.00,0.00,false
            """;

        // Act
        var result = service.ValidateCsv(csv);

        // Assert
        result.IsValid.Should().BeFalse();
        result.TotalRows.Should().Be(3);
        result.ValidRowsCount.Should().Be(0);
        result.InvalidRowsCount.Should().Be(3);

        result.Rows[0].ValidationErrors.Should().Contain(e => e.Contains("'ProcedureCode' is required"));
        result.Rows[1].ValidationErrors.Should().Contain(e => e.Contains("'BaseAmount' must be non-negative"));
        result.Rows[2].ValidationErrors.Should().Contain(e => e.Contains("'OverheadPercentage' must be between 0% and 100%"));
    }

    [Fact]
    [GxPRequirement("PRD-SYS-161")]
    public async Task CommitBulkFeeScheduleAsync_ValidRows_PopulatesGrantContractsInSingleBatchTransaction()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = new BulkFeeScheduleImporterService(dbContext, _clock);

        var rows = new List<FeeScheduleImportRowInput>
        {
            new("STUDY-DCT-100", "SITE-01", "V1_SCREEN", "P-101", "Blood Draw", 150m, 25m, 10m, 50m, 20m, true),
            new("STUDY-DCT-100", "SITE-01", "V1_SCREEN", "P-102", "Chest X-Ray", 200m, 25m, 10m, 0m, 0m, false),
            new("STUDY-DCT-100", "SITE-02", "V1_SCREEN", "P-101", "Blood Draw", 160m, 20m, 10m, 50m, 20m, true)
        };

        // Act
        var result = await service.CommitBulkFeeScheduleAsync(rows, "test_user_alice", ct);

        // Assert
        result.Success.Should().BeTrue();
        result.ImportedContractsCount.Should().Be(2); // SITE-01 and SITE-02
        result.ImportedFeeSchedulesCount.Should().Be(3);

        // Verify entities in DbContext
        var contracts = await dbContext.BudgetContracts.ToListAsync(ct);
        contracts.Should().HaveCount(2);

        var site1Contract = contracts.Single(c => c.SiteId == "SITE-01");
        site1Contract.StudyId.Should().Be("STUDY-DCT-100");
        site1Contract.CreatedBy.Should().Be("test_user_alice");
        // Budget for Site 01: (150*1.25) + (200*1.25) = 187.50 + 250 = 437.50
        site1Contract.TotalBudget.Should().Be(437.50m);

        var feeSchedules = await dbContext.FeeSchedules.ToListAsync(ct);
        feeSchedules.Should().HaveCount(3);
        feeSchedules.All(f => f.CreatedBy == "test_user_alice").Should().BeTrue();

        // GxP Audit Logs
        var auditLogs = await dbContext.PaymentAuditLogs.ToListAsync(ct);
        auditLogs.Should().HaveCount(2); // One per contract
        auditLogs.All(a => a.Action == "BULK_IMPORT_FEE_SCHEDULE" && a.UserId == "test_user_alice").Should().BeTrue();
    }
}
