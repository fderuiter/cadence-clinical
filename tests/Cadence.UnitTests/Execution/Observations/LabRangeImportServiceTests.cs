using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Observations;

public sealed class LabRangeImportServiceTests
{
    private static ExecutionDbContext CreateInMemoryDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ExecutionDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>
    /// Validates CSV batch ingestion dry-run with LOINC code mapping and UCUM SI unit conversion.
    /// @req:PRD-SYS-170
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-170")]
    public async Task DryRunAsync_ValidCsv_ReturnsSuccessAndMappedFields()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = CreateInMemoryDbContext(connection);

        var ucum = new UcumNormalizationService();
        var clock = new FakeClock();

        var service = new LabRangeImportService(db, ucum, clock);

        string csvContent =
            "Test Code,Test Name,Source,Site ID,Unit,Normalized Unit,Sex,Age Low,Age High,Low Bound,High Bound,Critical Low,Critical High,LOINC\n" +
            "ALT,Alanine Aminotransferase,CENTRAL,,U/L,U/L,ALL,18,65,10,40,5,100,29463-7\n" +
            "CREAT,Creatinine,CENTRAL,,mg/dL,umol/L,M,18,80,0.7,1.3,,,\n";

        var request = new BulkLabRangeDryRunRequest(
            StudyId: "STUDY-101",
            LabSource: "CENTRAL",
            FileName: "test_ranges.csv",
            RawFileContent: csvContent
        );

        var result = await service.DryRunAsync(request, ct);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.TotalRows.Should().Be(2);
        result.ValidRows.Should().Be(2);
        result.InvalidRows.Should().Be(0);
        result.LoincMappedCount.Should().Be(1); // CREAT auto-mapped
        result.UnitConvertedCount.Should().Be(1); // CREAT mg/dL -> umol/L

        var row1 = result.Rows[0];
        row1.TestCode.Should().Be("ALT");
        row1.LoincCode.Should().Be("29463-7");
        row1.IsValid.Should().BeTrue();

        var row2 = result.Rows[1];
        row2.TestCode.Should().Be("CREAT");
        row2.LoincCode.Should().Be("2160-0");
        row2.NormalizedUnit.Should().Be("umol/L");
        row2.ConvertedLowBound.Should().BeGreaterThan(60.0);
        row2.ConversionFactor.Should().BeApproximately(88.4, 0.1);
        row2.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task DryRunAsync_InvalidCsvRows_ReportsValidationErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = CreateInMemoryDbContext(connection);

        var ucum = new UcumNormalizationService();
        var clock = new FakeClock();

        var service = new LabRangeImportService(db, ucum, clock);

        string csvContent =
            "Test Code,Test Name,Source,Site ID,Unit,Normalized Unit,Sex,Age Low,Age High,Low Bound,High Bound,Critical Low,Critical High,LOINC\n" +
            "WBC,White Blood Cells,CENTRAL,,10^3/uL,10^9/L,ALL,18,65,10.0,4.0,,,\n" + // low > high error
            ",Missing Code,CENTRAL,,U/L,U/L,ALL,18,65,10,40,,,\n"; // missing code error

        var request = new BulkLabRangeDryRunRequest(
            StudyId: "STUDY-101",
            FileName: "invalid_ranges.csv",
            RawFileContent: csvContent
        );

        var result = await service.DryRunAsync(request, ct);

        result.IsSuccess.Should().BeFalse();
        result.TotalRows.Should().Be(2);
        result.ValidRows.Should().Be(0);
        result.InvalidRows.Should().Be(2);

        result.Rows[0].IsValid.Should().BeFalse();
        result.Rows[0].Errors.Should().Contain(e => e.Contains("cannot exceed upper bound"));

        result.Rows[1].IsValid.Should().BeFalse();
        result.Rows[1].Errors.Should().Contain(e => e.Contains("is required"));
    }

    [Fact]
    public async Task DryRunAsync_ValidXlsx_ParsesSuccessfully()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = CreateInMemoryDbContext(connection);

        var ucum = new UcumNormalizationService();
        var clock = new FakeClock();

        var service = new LabRangeImportService(db, ucum, clock);

        byte[] xlsxBytes = CreateSampleXlsxBytes();

        var request = new BulkLabRangeDryRunRequest(
            StudyId: "STUDY-101",
            FileName: "test_ranges.xlsx",
            FileContentBase64: Convert.ToBase64String(xlsxBytes)
        );

        var result = await service.DryRunAsync(request, ct);

        result.Should().NotBeNull();
        result.TotalRows.Should().Be(1);
        result.Rows[0].TestCode.Should().Be("AST");
        result.Rows[0].TestName.Should().Be("Aspartate Aminotransferase");
    }

    /// <summary>
    /// Commits CSV batch reference range ingestion with LOINC mapping and SI unit conversion, keeping ranges and jobs scoped per study.
    /// @req:PRD-SYS-170
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-170")]
    public async Task CommitImportAsync_ValidImport_PersistsRangesAndCreatesJob()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = CreateInMemoryDbContext(connection);

        var ucum = new UcumNormalizationService();
        var clock = new FakeClock();

        var service = new LabRangeImportService(db, ucum, clock);

        string csvContent =
            "Test Code,Test Name,Source,Site ID,Unit,Normalized Unit,Sex,Age Low,Age High,Low Bound,High Bound,Critical Low,Critical High,LOINC\n" +
            "HEMOGLOBIN,Hemoglobin,CENTRAL,,g/dL,g/L,M,18,65,13.5,17.5,10.0,20.0,718-7\n";

        var request = new BulkLabRangeImportCommitRequest(
            StudyId: "STUDY-101",
            LabSource: "CENTRAL",
            FileName: "hgb_ranges.csv",
            RawFileContent: csvContent,
            ReasonForChange: "Importing study baseline reference range"
        );

        var jobResponse = await service.CommitImportAsync(request, "user@cadence.clinical", ct);

        jobResponse.Should().NotBeNull();
        jobResponse.Status.Should().Be("COMPLETED");
        jobResponse.TotalRows.Should().Be(1);
        jobResponse.SuccessRows.Should().Be(1);
        jobResponse.ErrorRows.Should().Be(0);
        jobResponse.SubmittedBy.Should().Be("user@cadence.clinical");

        var persistedRanges = await db.LabReferenceRanges.ToListAsync(ct);
        persistedRanges.Should().HaveCount(1);
        var range = persistedRanges[0];
        range.StudyId.Should().Be("STUDY-101");
        range.TestCode.Should().Be("HEMOGLOBIN");
        range.NormalizedUnit.Should().Be("g/L");
        range.LowBound.Should().BeApproximately(135.0, 0.1);
        range.HighBound.Should().BeApproximately(175.0, 0.1);
        range.CreatedBy.Should().Be("user@cadence.clinical");
        range.ReasonForChange.Should().Be("Importing study baseline reference range");

        var persistedJob = await db.BulkLabRangeImportJobs.FirstOrDefaultAsync(j => j.JobId == jobResponse.JobId, ct);
        persistedJob.Should().NotBeNull();
        persistedJob!.Status.Should().Be("COMPLETED");

        await service.CommitImportAsync(new BulkLabRangeImportCommitRequest(
            StudyId: "STUDY-202",
            LabSource: "CENTRAL",
            FileName: "study202_ranges.csv",
            RawFileContent:
                "Test Code,Test Name,Source,Site ID,Unit,Normalized Unit,Sex,Age Low,Age High,Low Bound,High Bound,Critical Low,Critical High,LOINC\n" +
                "GLUC,Glucose,CENTRAL,,mg/dL,mg/dL,ALL,18,65,70.0,99.0,,,2345-7\n",
            ReasonForChange: "Importing separate study reference range"
        ), "user@cadence.clinical", ct);

        (await service.GetReferenceRangesAsync("STUDY-101", null, ct))
            .Should().ContainSingle()
            .Which.Should().Match<LabReferenceRangeResponse>(r => r.StudyId == "STUDY-101" && r.TestCode == "HEMOGLOBIN");
        (await service.GetReferenceRangesAsync("STUDY-202", null, ct))
            .Should().ContainSingle()
            .Which.Should().Match<LabReferenceRangeResponse>(r => r.StudyId == "STUDY-202" && r.TestCode == "GLUC");
        (await service.GetReferenceRangesAsync("STUDY-999", null, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// Governs reference range creation, LOINC/UCUM normalization, study- and site-scoped retrieval, versioned updates, and deactivation.
    /// @req:PRD-SYS-170
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-170")]
    public async Task CrudOperations_CreateUpdateDeleteGet_SucceedsWithAuditTrail()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = CreateInMemoryDbContext(connection);

        var ucum = new UcumNormalizationService();
        var clock = new FakeClock();

        var service = new LabRangeImportService(db, ucum, clock);

        // Create in STUDY-101 (central) and STUDY-202 + site-specific local ranges to verify study/site scoping
        var createReq = new LabReferenceRangeCreateRequest(
            StudyId: "STUDY-101",
            TestCode: "BILI",
            TestName: "Bilirubin",
            Source: "CENTRAL",
            Unit: "mg/dL",
            NormalizedUnit: "umol/L",
            SexApplicability: "ALL",
            LowBound: 0.2,
            HighBound: 1.2,
            ReasonForChange: "Initial reference range registration"
        );

        var created = await service.CreateReferenceRangeAsync(createReq, "user@cadence.clinical", ct);
        created.Should().NotBeNull();
        created.TestCode.Should().Be("BILI");
        created.NormalizedUnit.Should().Be("umol/L");
        created.LoincCode.Should().Be("1975-2");

        await service.CreateReferenceRangeAsync(new LabReferenceRangeCreateRequest(
            StudyId: "STUDY-202",
            TestCode: "GLUC",
            TestName: "Glucose",
            Source: "CENTRAL",
            Unit: "mg/dL",
            NormalizedUnit: "mmol/L",
            SexApplicability: "ALL",
            LowBound: 70.0,
            HighBound: 99.0,
            ReasonForChange: "Separate study reference range"
        ), "user@cadence.clinical", ct);

        Guid id = Guid.Parse(created.Id);

        // Get by ID
        var fetched = await service.GetReferenceRangeByIdAsync(id, ct);
        fetched.Should().NotBeNull();
        fetched!.TestName.Should().Be("Bilirubin");

        // Study-scoped list: STUDY-101 never includes STUDY-202's ranges, and vice versa
        var list = await service.GetReferenceRangesAsync("STUDY-101", null, ct);
        list.Should().ContainSingle(r => r.StudyId == "STUDY-101" && r.TestCode == "BILI");

        var otherStudyList = await service.GetReferenceRangesAsync("STUDY-202", null, ct);
        otherStudyList.Should().ContainSingle(r => r.StudyId == "STUDY-202" && r.TestCode == "GLUC");

        // Site-scoped query includes central (null site) and matching local site ranges, excluding other sites
        var site1Range = await service.CreateReferenceRangeAsync(new LabReferenceRangeCreateRequest(
            StudyId: "STUDY-101",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            Source: "LOCAL",
            SiteId: "SITE-001",
            Unit: "U/L",
            SexApplicability: "F",
            LowBound: 7.0,
            HighBound: 35.0,
            ReasonForChange: "Site 001 local lab range"
        ), "user@cadence.clinical", ct);

        await service.CreateReferenceRangeAsync(new LabReferenceRangeCreateRequest(
            StudyId: "STUDY-101",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            Source: "LOCAL",
            SiteId: "SITE-002",
            Unit: "U/L",
            SexApplicability: "F",
            LowBound: 9.0,
            HighBound: 40.0,
            ReasonForChange: "Site 002 local lab range"
        ), "user@cadence.clinical", ct);

        var site1Scoped = await service.GetReferenceRangesAsync("STUDY-101", "SITE-001", ct);
        site1Scoped.Should().HaveCount(2);
        site1Scoped.Should().Contain(r => r.Id == created.Id && r.SiteId == null);
        site1Scoped.Should().Contain(r => r.Id == site1Range.Id && r.SiteId == "SITE-001");
        site1Scoped.Should().NotContain(r => r.SiteId == "SITE-002");

        // Update
        var updateReq = new LabReferenceRangeUpdateRequest(
            HighBound: 1.4,
            ReasonForChange: "Adjusting upper normal bound"
        );

        var updated = await service.UpdateReferenceRangeAsync(id, updateReq, "user2@cadence.clinical", ct);
        updated.Should().NotBeNull();
        updated!.HighBound.Should().Be(1.4);
        updated.Version.Should().Be(2);

        // Delete (Deactivate)
        bool deleted = await service.DeleteReferenceRangeAsync(id, "user2@cadence.clinical", "Deactivating obsolete range", ct);
        deleted.Should().BeTrue();

        var postDelete = await service.GetReferenceRangeByIdAsync(id, ct);
        postDelete.Should().BeNull();
    }

    [Fact]
    public async Task ConvertUnitAndLoincCatalog_ReturnsExpectedData()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = CreateInMemoryDbContext(connection);

        var ucum = new UcumNormalizationService();
        var clock = new FakeClock();

        var service = new LabRangeImportService(db, ucum, clock);

        var conv = service.ConvertUnit(new UnitConversionRequest(
            TestCode: "CREAT",
            Value: 1.0,
            FromUnit: "mg/dL",
            ToUnit: "umol/L"
        ));

        conv.ConvertedValue.Should().BeApproximately(88.4, 0.1);
        conv.LoincCode.Should().Be("2160-0");

        var catalog = service.GetLoincCatalog();
        catalog.Should().NotBeNull();
        catalog.TotalCount.Should().BeGreaterThan(0);
        catalog.Items.Should().Contain(x => x.TestCode == "CREAT" && x.LoincCode == "2160-0");

        await Task.CompletedTask;
    }

    private static byte[] CreateSampleXlsxBytes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            // xl/sharedStrings.xml
            var ssEntry = zip.CreateEntry("xl/sharedStrings.xml");
            using (var writer = new StreamWriter(ssEntry.Open(), Encoding.UTF8))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                             "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"3\" uniqueCount=\"3\">\n" +
                             "  <si><t>AST</t></si>\n" +
                             "  <si><t>Aspartate Aminotransferase</t></si>\n" +
                             "  <si><t>CENTRAL</t></si>\n" +
                             "</sst>");
            }

            // xl/worksheets/sheet1.xml
            var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                             "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\n" +
                             "  <sheetData>\n" +
                             "    <row r=\"1\">\n" +
                             "      <c r=\"A1\" t=\"s\"><v>0</v></c>\n" + // Test Code header
                             "      <c r=\"B1\" t=\"s\"><v>1</v></c>\n" + // Test Name header
                             "      <c r=\"C1\" t=\"s\"><v>2</v></c>\n" + // Source header
                             "    </row>\n" +
                             "    <row r=\"2\">\n" +
                             "      <c r=\"A2\" t=\"s\"><v>0</v></c>\n" + // AST
                             "      <c r=\"B2\" t=\"s\"><v>1</v></c>\n" + // Aspartate Aminotransferase
                             "      <c r=\"C2\" t=\"s\"><v>2</v></c>\n" + // CENTRAL
                             "    </row>\n" +
                             "  </sheetData>\n" +
                             "</worksheet>");
            }
        }
        return ms.ToArray();
    }
}
