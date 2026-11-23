using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Security.DependencyInjection;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Devices;

public sealed class DeviceEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = FakeClock.DefaultStart;
    private static readonly DateTime FixedNowUtc = FakeClock.DefaultStart.UtcDateTime;

    private readonly IDeviceApplicationService _deviceService;
    private readonly IHost _host;
    private readonly HttpClient _client;

    public DeviceEndpointsTests()
    {
        _deviceService = Substitute.For<IDeviceApplicationService>();

        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseTestGatewaySecret();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCadenceSecurity();
                    services.AddSingleton(_deviceService);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseCadenceSecurity();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapDeviceEndpoints();
                    });
                });
            })
            .Start();

        // The site pharmacist registers, dispenses and reports on devices, so it holds every route these tests call.
        var server = _host.GetTestServer();
        _client = new HttpClient(new TestSecurityContext.GatewaySigningHandler(
            TestSecurityContext.Identity("pharmacist-01", "Pharmacist") with { SiteIds = ["SITE-1"] })
        {
            InnerHandler = server.CreateHandler(),
        })
        {
            BaseAddress = server.BaseAddress,
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public async Task ParseUdi_Returns200WithParsedResult()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceService.ParseUdi(Arg.Any<UdiParseRequest>())
            .Returns(new ParsedUdiResponse(
                RawBarcode: "(01)00850006789013",
                UdiCarrierFormat: "GS1_128",
                UdiDi: "00850006789013",
                LotNumber: null,
                SerialNumber: null,
                ExpirationDate: null,
                ManufacturingDate: null,
                SoftwareVersion: null,
                IsValid: true,
                ValidationErrors: Array.Empty<string>()));

        var response = await _client.PostAsJsonAsync("/api/v1/execution/devices/parse-udi", new UdiParseRequest("(01)00850006789013"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ParsedUdiResponse>(ct);
        Assert.NotNull(body);
        Assert.Equal("00850006789013", body.UdiDi);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public async Task RegisterInventory_Returns201Created()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceService.RegisterInventoryAsync(Arg.Any<DeviceInventoryCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<DeviceInventoryResponse>
            {
                new(
                    Id: Guid.NewGuid().ToString(),
                    StudyId: "STUDY-1",
                    SiteId: "SITE-1",
                    DeviceIdentifier: "DEV-1",
                    BrandName: "CardioPulse",
                    DeviceModel: null,
                    DeviceType: "THERAPEUTIC",
                    DeviceClassification: "CLASS_IIA",
                    UdiDi: "00850006789013",
                    UdiPi: null,
                    UdiCarrierFormat: "GS1_128",
                    LotNumber: "LOT-1",
                    SerialNumber: "SN-1",
                    ExpirationDate: null,
                    ManufacturingDate: null,
                    SoftwareVersion: null,
                    Status: "AVAILABLE",
                    IsDispensationBlocked: false,
                    AllocatedSubjectId: null,
                    DispensedAt: null,
                    DispensedBy: null,
                    QuarantinedAt: null,
                    QuarantinedBy: null,
                    QuarantineReason: null,
                    CreatedAt: FixedNowUtc,
                    CreatedBy: "usr:1",
                    ReasonForChange: "intake",
                    VersionIndex: 1)
            });

        var response = await _client.PostAsJsonAsync("/api/v1/execution/devices/inventory", new DeviceInventoryCreateRequest(
            StudyId: "STUDY-1",
            SiteId: "SITE-1",
            BrandName: "CardioPulse",
            UdiDi: "00850006789013",
            LotNumber: "LOT-1"), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-003")]
    public async Task DispenseDevice_Returns200OK()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceService.DispenseDeviceAsync(Arg.Any<DeviceDispensationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceDispensationResponse(
                Status: "SUCCESS",
                Message: "Dispensed",
                Device: new DeviceInventoryResponse(
                    Id: "DEV-1",
                    StudyId: "STUDY-1",
                    SiteId: "SITE-1",
                    DeviceIdentifier: "DEV-1",
                    BrandName: "CardioPulse",
                    DeviceModel: null,
                    DeviceType: "THERAPEUTIC",
                    DeviceClassification: "CLASS_IIA",
                    UdiDi: "00850006789013",
                    UdiPi: null,
                    UdiCarrierFormat: "GS1_128",
                    LotNumber: "LOT-1",
                    SerialNumber: "SN-1",
                    ExpirationDate: null,
                    ManufacturingDate: null,
                    SoftwareVersion: null,
                    Status: "DISPENSED",
                    IsDispensationBlocked: false,
                    AllocatedSubjectId: "SUB-1",
                    DispensedAt: FixedNow,
                    DispensedBy: "usr:1",
                    QuarantinedAt: null,
                    QuarantinedBy: null,
                    QuarantineReason: null,
                    CreatedAt: FixedNowUtc,
                    CreatedBy: "usr:1",
                    ReasonForChange: "dispense",
                    VersionIndex: 2),
                DispensedAt: FixedNow));

        var response = await _client.PostAsJsonAsync("/api/v1/execution/devices/dispense", new DeviceDispensationRequest(
            StudyId: "STUDY-1",
            SiteId: "SITE-1",
            SubjectId: "SUB-1",
            DeviceInventoryId: "DEV-1"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-002")]
    public async Task LogDeficiency_Returns201Created()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceService.LogDeficiencyAsync(Arg.Any<DeviceDeficiencyCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceDeficiencyResponse(
                Id: Guid.NewGuid().ToString(),
                StudyId: "STUDY-1",
                SiteId: "SITE-1",
                SubjectId: "SUB-1",
                DeviceInventoryId: "DEV-1",
                UdiDi: "00850006789013",
                LotNumber: "LOT-1",
                SerialNumber: "SN-1",
                DeviceName: "CardioPulse",
                DeficiencyType: "MALFUNCTION",
                DeficiencyDescription: "Battery failure",
                IncidentDate: FixedNow,
                AwarenessDate: FixedNow,
                CouldLeadToSade: false,
                PatientInvolved: false,
                HealthImpact: "NONE",
                ActionTakenDevice: "QUARANTINED",
                DeviceReturnedToSponsor: false,
                ReturnTrackingNumber: null,
                RootCauseAnalysis: null,
                SponsorEvaluation: null,
                QuarantineTriggered: true,
                QuarantineScope: "SERIAL",
                SafetyCaseId: null,
                SafetyClassification: null,
                InvestigationStatus: "LOGGED",
                CreatedAt: FixedNowUtc,
                CreatedBy: "usr:1",
                ReasonForChange: "deficiency",
                VersionIndex: 1));

        var response = await _client.PostAsJsonAsync("/api/v1/execution/devices/deficiencies", new DeviceDeficiencyCreateRequest(
            StudyId: "STUDY-1",
            SiteId: "SITE-1",
            UdiDi: "00850006789013",
            LotNumber: "LOT-1",
            DeficiencyDescription: "Battery failure",
            IncidentDate: FixedNow,
            AwarenessDate: FixedNow), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-003")]
    public async Task GetInvestigationSummary_Returns200OK()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceService.GetInvestigationSummaryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceInvestigationSummaryResponse(
                StudyId: "STUDY-1",
                TotalDevices: 10,
                AvailableDevices: 8,
                DispensedDevices: 2,
                QuarantinedDevices: 0,
                TotalDeficiencies: 0,
                OpenSafetyCases: 0,
                SadeCount: 0,
                ActiveQuarantines: 0));

        var response = await _client.GetAsync("/api/v1/execution/devices/investigation-summary?study_id=STUDY-1", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
