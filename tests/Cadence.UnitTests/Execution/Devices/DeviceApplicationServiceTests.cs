using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Domain.Devices.Errors;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Testing;
using Cadence.Testing.Time;

using MassTransit;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NSubstitute;


using Xunit;

namespace Cadence.UnitTests.Execution.Devices;

public sealed class DeviceApplicationServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly ExecutionDbContext _dbContext;
    private readonly FakeClock _clock;
    private readonly ICurrentAuditContextAccessor _auditContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly DeviceRepository _repository;
    private readonly DeviceApplicationService _service;

    public DeviceApplicationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new ExecutionDbContext(options);
        _dbContext.Database.EnsureCreated();

        _clock = new FakeClock(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        _auditContext = Substitute.For<ICurrentAuditContextAccessor>();
        _auditContext.UserId.Returns("usr:investigator-1");
        _publishEndpoint = Substitute.For<IPublishEndpoint>();

        _repository = new DeviceRepository(_dbContext);
        _service = new DeviceApplicationService(_repository, _clock, _auditContext, _publishEndpoint);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public async Task RegisterInventoryAsync_BulkQuantity_RegistersSerializedDevices()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Pacemaker",
            UdiDi: "00850006789013",
            LotNumber: "LOT-2026-A",
            SerialNumber: "SN-CARDIO",
            Quantity: 3,
            ExpirationDate: _clock.UtcNow.AddYears(1));

        var result = await _service.RegisterInventoryAsync(request, ct);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, d => d.SerialNumber == "SN-CARDIO-01");
        Assert.Contains(result, d => d.SerialNumber == "SN-CARDIO-02");
        Assert.Contains(result, d => d.SerialNumber == "SN-CARDIO-03");
        Assert.All(result, d => Assert.Equal("AVAILABLE", d.Status));
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public async Task RegisterInventoryAsync_WithRawBarcode_ParsesAndStoresAttributes()
    {
        var ct = TestContext.Current.CancellationToken;
        string rawBarcode = "(01)00850006789016(17)271231(10)LOT-BARCODE(21)SN-SCANNED";
        var request = new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Sensor",
            UdiDi: "",
            LotNumber: "",
            RawBarcode: rawBarcode);

        var result = await _service.RegisterInventoryAsync(request, ct);

        Assert.Single(result);
        var item = result[0];
        Assert.Equal("00850006789016", item.UdiDi);
        Assert.Equal("LOT-BARCODE", item.LotNumber);
        Assert.Equal("SN-SCANNED", item.SerialNumber);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-003")]
    public async Task DispenseDeviceAsync_ValidAvailableDevice_DispensesSuccessfully()
    {
        var ct = TestContext.Current.CancellationToken;
        var reg = await _service.RegisterInventoryAsync(new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Monitor",
            UdiDi: "00850006789013",
            LotNumber: "LOT-DISPENSE-1",
            SerialNumber: "SN-DISP-01",
            ExpirationDate: _clock.UtcNow.AddMonths(6)), ct);

        string deviceId = reg[0].Id;

        var disp = await _service.DispenseDeviceAsync(new DeviceDispensationRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            SubjectId: "SUB-1001",
            DeviceInventoryId: deviceId), ct);

        Assert.Equal("SUCCESS", disp.Status);
        Assert.Equal("DISPENSED", disp.Device.Status);
        Assert.Equal("SUB-1001", disp.Device.AllocatedSubjectId);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-003")]
    public async Task DispenseDeviceAsync_QuarantinedDevice_ThrowsQuarantinedError()
    {
        var ct = TestContext.Current.CancellationToken;
        var reg = await _service.RegisterInventoryAsync(new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Monitor",
            UdiDi: "00850006789013",
            LotNumber: "LOT-QUAR",
            SerialNumber: "SN-QUAR-01"), ct);

        string deviceId = reg[0].Id;

        // Quarantine device
        await _service.InitiateQuarantineAsync(new DeviceQuarantineCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            Reason: "Suspected battery flaw",
            DeviceInventoryId: deviceId), ct);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.DispenseDeviceAsync(new DeviceDispensationRequest(
                StudyId: "STUDY-DEV-101",
                SiteId: "SITE-001",
                SubjectId: "SUB-1001",
                DeviceInventoryId: deviceId), ct);
        });
    }

    [Fact]
    [GxPRequirement("PRD-DEV-003")]
    public async Task DispenseDeviceAsync_ExpiredDevice_ThrowsExpiredError()
    {
        var ct = TestContext.Current.CancellationToken;
        // Expired yesterday
        var reg = await _service.RegisterInventoryAsync(new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Monitor",
            UdiDi: "00850006789013",
            LotNumber: "LOT-EXP",
            SerialNumber: "SN-EXP-01",
            ExpirationDate: _clock.UtcNow.AddDays(-1)), ct);

        string deviceId = reg[0].Id;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.DispenseDeviceAsync(new DeviceDispensationRequest(
                StudyId: "STUDY-DEV-101",
                SiteId: "SITE-001",
                SubjectId: "SUB-1001",
                DeviceInventoryId: deviceId), ct);
        });
    }

    [Fact]
    [GxPRequirement("PRD-DEV-002")]
    public async Task LogDeficiencyAsync_TriggersQuarantineAndSafetyCaseAndOutboxEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        // Register inventory in lot LOT-DEFECT
        await _service.RegisterInventoryAsync(new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Monitor",
            UdiDi: "00850006789013",
            LotNumber: "LOT-DEFECT",
            SerialNumber: "SN-DEFECT-01"), ct);

        await _service.RegisterInventoryAsync(new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Monitor",
            UdiDi: "00850006789013",
            LotNumber: "LOT-DEFECT",
            SerialNumber: "SN-DEFECT-02"), ct);

        var defRequest = new DeviceDeficiencyCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            UdiDi: "00850006789013",
            LotNumber: "LOT-DEFECT",
            DeficiencyDescription: "Thermal housing fracture under operational stress",
            IncidentDate: _clock.UtcNow.AddDays(-1),
            AwarenessDate: _clock.UtcNow,
            DeficiencyType: "SADE",
            CouldLeadToSade: true,
            QuarantineScope: "LOT");

        var defResponse = await _service.LogDeficiencyAsync(defRequest, ct);

        Assert.NotNull(defResponse.SafetyCaseId);
        Assert.StartsWith("CASE-", defResponse.SafetyCaseId);
        Assert.Equal("EXPEDITED_SADE_INVESTIGATION", defResponse.SafetyClassification);
        Assert.True(defResponse.QuarantineTriggered);

        // Verify inventory items in lot LOT-DEFECT were quarantined
        var items = await _service.ListInventoryAsync("STUDY-DEV-101", "SITE-001", null, null, "LOT-DEFECT", null, ct);
        Assert.All(items, i => Assert.Equal("QUARANTINED", i.Status));
        Assert.All(items, i => Assert.True(i.IsDispensationBlocked));

        // Verify publish endpoint was invoked for outbox events
        await _publishEndpoint.ReceivedWithAnyArgs().Publish(Arg.Any<DeviceDeficiencyReportedEvent>(), Arg.Any<System.Threading.CancellationToken>());
        await _publishEndpoint.ReceivedWithAnyArgs().Publish(Arg.Any<DeviceQuarantinedEvent>(), Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    [GxPRequirement("PRD-DEV-003")]
    public async Task ReleaseQuarantineAsync_ReleasesDeviceAndQuarantineRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var reg = await _service.RegisterInventoryAsync(new DeviceInventoryCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Monitor",
            UdiDi: "00850006789013",
            LotNumber: "LOT-REL",
            SerialNumber: "SN-REL-01"), ct);

        string deviceId = reg[0].Id;

        var quar = await _service.InitiateQuarantineAsync(new DeviceQuarantineCreateRequest(
            StudyId: "STUDY-DEV-101",
            SiteId: "SITE-001",
            Reason: "Precautionary holding",
            DeviceInventoryId: deviceId), ct);

        var rel = await _service.ReleaseQuarantineAsync(new DeviceQuarantineReleaseRequest(
            QuarantineId: quar.Id,
            ReleaseJustification: "Investigation completed - false alarm",
            SignatureToken: "SIG-TOKEN-123"), ct);

        Assert.Equal("RELEASED", rel.Status);
        Assert.NotNull(rel.ReleasedAt);

        var items = await _service.ListInventoryAsync("STUDY-DEV-101", "SITE-001", null, null, null, "SN-REL-01", ct);
        Assert.Equal("AVAILABLE", items[0].Status);
        Assert.False(items[0].IsDispensationBlocked);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-002")]
    public async Task GetInvestigationSummaryAsync_CalculatesMetricsCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        await _service.RegisterInventoryAsync(new DeviceInventoryCreateRequest(
            StudyId: "STUDY-METRIC-01",
            SiteId: "SITE-001",
            BrandName: "CardioPulse Monitor",
            UdiDi: "00850006789013",
            LotNumber: "LOT-M1",
            Quantity: 5), ct);

        var summary = await _service.GetInvestigationSummaryAsync("STUDY-METRIC-01", ct);

        Assert.Equal("STUDY-METRIC-01", summary.StudyId);
        Assert.Equal(5, summary.TotalDevices);
        Assert.Equal(5, summary.AvailableDevices);
        Assert.Equal(0, summary.QuarantinedDevices);
    }
}
