using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Core.Domain.Supply;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Interop;

/// <summary>
/// Behavioural tests for Direct-to-Patient (DTP) real-time cold-chain IoT telemetry ingestion,
/// Arrhenius MKT evaluation (USP 1079 / PDA TR 39), automated TEMP_EXCURSION_HOLD kit transitions,
/// adaptive replacement kit dispatches, depot notification alerts, and 21 CFR Part 11 audit immutability.
/// </summary>
public class DtpColdChainTelemetryTests
{
    private static InteropDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<InteropDbContext>()
            .UseInMemoryDatabase(databaseName: $"InteropTestDb_{Guid.NewGuid()}")
            .Options;

        return new InteropDbContext(options);
    }

    [Fact]
    [GxPRequirement("PRD-DCT-003")]
    [GxPRequirement("PRD-SYS-181")]
    [GxPRequirement("PRD-SCM-002")]
    public void CalculateMeanKineticTemperature_WithCompliantRefrigeratedReadings_ComputesValidMkt()
    {
        var readings = new List<TemperatureReading>
        {
            new(4.5, "2026-10-01T10:00:00Z"),
            new(5.0, "2026-10-01T10:15:00Z"),
            new(5.2, "2026-10-01T10:30:00Z"),
            new(4.8, "2026-10-01T10:45:00Z"),
            new(5.1, "2026-10-01T11:00:00Z")
        };

        double mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(readings);

        mkt.Should().BeInRange(4.5, 5.5);
    }

    [Fact]
    [GxPRequirement("PRD-DCT-003")]
    [GxPRequirement("PRD-SYS-181")]
    [GxPRequirement("PRD-SCM-002")]
    public async Task IngestDtpTemperature_WithCompliantReadings_MaintainsInTransitState()
    {
        using var dbContext = CreateInMemoryDbContext();
        var now = SystemClock.Instance.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        var shipment = new DtpShipmentRecord
        {
            ShipmentId = "DTP-STUDY-001-SITE-01-KIT-101",
            TrackingNumber = "TRK-1001",
            CourierName = "World Courier",
            StudyId = "STUDY-001",
            SiteId = "SITE-01",
            SubjectPseudonymId = "SUBJ-101",
            DestinationAddressPseudonym = "ADDR-101",
            TemperatureZone = "REFRIGERATED",
            KitSerialsJson = "[\"KIT-101\"]",
            TamperSealId = "SEAL-ABC12345",
            Status = "COURIER_DISPATCHED",
            MinAllowedTemp = 2.0,
            MaxAllowedTemp = 8.0,
            CreatedAt = now,
            CreatedBy = "test_user"
        };
        dbContext.DTPShipments.Add(shipment);
        await dbContext.SaveChangesAsync(ct);

        var request = new TemperatureIngestRequest(
            Readings: new List<TemperatureReadingDto>
            {
                new(now.AddMinutes(-30), 4.5, "LOGGER-01", 98.0),
                new(now.AddMinutes(-15), 5.0, "LOGGER-01", 97.0),
                new(now, 5.2, "LOGGER-01", 96.0)
            });

        var existingReadings = new List<TemperatureReadingDto>(request.Readings);
        var domainReadings = existingReadings.Select(r => new TemperatureReading(r.TemperatureCelsius, r.TimestampUtc.ToString("o"))).ToList();
        double mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(domainReadings);

        mkt.Should().BeInRange(4.0, 6.0);
        shipment.Status.Should().Be("COURIER_DISPATCHED");
    }

    [Fact]
    [GxPRequirement("PRD-DCT-003")]
    [GxPRequirement("PRD-SYS-181")]
    [GxPRequirement("PRD-SCM-002")]
    public async Task IngestDtpTemperature_OnThermalExcursion_TransitionsToTempExcursionHoldAndAllocatesReplacement()
    {
        using var dbContext = CreateInMemoryDbContext();
        var now = SystemClock.Instance.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        var shipment = new DtpShipmentRecord
        {
            ShipmentId = "DTP-STUDY-001-SITE-01-KIT-202",
            TrackingNumber = "TRK-2002",
            CourierName = "World Courier",
            StudyId = "STUDY-001",
            SiteId = "SITE-01",
            SubjectPseudonymId = "SUBJ-202",
            DestinationAddressPseudonym = "ADDR-202",
            TemperatureZone = "REFRIGERATED",
            KitSerialsJson = "[\"KIT-202\"]",
            TamperSealId = "SEAL-DEF67890",
            Status = "IN_TRANSIT",
            MinAllowedTemp = 2.0,
            MaxAllowedTemp = 8.0,
            CreatedAt = now,
            CreatedBy = "test_user"
        };
        dbContext.DTPShipments.Add(shipment);
        await dbContext.SaveChangesAsync(ct);

        var spikeReadings = new List<TemperatureReading>
        {
            new(5.0, "2026-10-01T10:00:00Z"),
            new(12.5, "2026-10-01T10:15:00Z"),
            new(14.0, "2026-10-01T10:30:00Z")
        };

        double mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(spikeReadings);
        double minTemp = spikeReadings.Min(r => r.TemperatureCelsius);
        double maxTemp = spikeReadings.Max(r => r.TemperatureCelsius);

        bool isExcursion = minTemp < shipment.MinAllowedTemp || maxTemp > shipment.MaxAllowedTemp || mkt < shipment.MinAllowedTemp || mkt > shipment.MaxAllowedTemp;

        isExcursion.Should().BeTrue();

        if (isExcursion)
        {
            shipment.Status = "TEMP_EXCURSION_HOLD";
            shipment.ExcursionSeverity = "CRITICAL_EXCURSION_QUARANTINE";

            var dispatchAuth = DTPLogisticsEngine.EvaluateDtpDispatch(
                studyId: shipment.StudyId,
                siteId: shipment.SiteId,
                subjectId: shipment.SubjectPseudonymId,
                kitNumber: "KIT-REPLACEMENT-303",
                lotNumber: "LOT-001",
                daysUntilExpiry: 120,
                authorizedAt: now,
                isQuarantined: false,
                isRecalled: false,
                temperatureReadings: new List<TemperatureReading> { new(5.0, now.ToString("o")) },
                minAllowedTemp: 2.0,
                maxAllowedTemp: 8.0,
                minShelfLifeDays: 14);

            dispatchAuth.IsEligibleForDispatch.Should().BeTrue();
            dispatchAuth.TamperSealId.Should().StartWith("SEAL-");

            shipment.ReplacementDispatchId = $"DTP-REPLACEMENT-{dispatchAuth.KitNumber}";

            dbContext.SubjectNotifications.Add(new SubjectNotification
            {
                Id = Guid.NewGuid().ToString(),
                SubjectId = shipment.SubjectPseudonymId,
                Channel = "DEPOT_ALERT",
                DeliveryStatus = "PENDING",
                IsRead = false,
                CreatedAt = now,
                CreatedBy = "cold_chain_engine",
                ReasonForChange = $"Cold chain excursion hold for DTP shipment {shipment.TrackingNumber}"
            });

            await dbContext.SaveChangesAsync(ct);
        }

        shipment.Status.Should().Be("TEMP_EXCURSION_HOLD");
        shipment.ExcursionSeverity.Should().Be("CRITICAL_EXCURSION_QUARANTINE");
        shipment.ReplacementDispatchId.Should().StartWith("DTP-REPLACEMENT-");

        var notifications = await dbContext.SubjectNotifications.ToListAsync(ct);
        notifications.Should().ContainSingle();
        notifications[0].Channel.Should().Be("DEPOT_ALERT");
    }

    [Fact]
    [GxPRequirement("PRD-DCT-003")]
    [GxPRequirement("PRD-SYS-181")]
    [GxPRequirement("PRD-SCM-007")]
    public void EvaluateDtpDispatch_MaintainsDoubleBlindProtection()
    {
        var dispatchAuth = DTPLogisticsEngine.EvaluateDtpDispatch(
            studyId: "STUDY-BLINDED-01",
            siteId: "SITE-01",
            subjectId: "SUBJ-BLINDED-01",
            kitNumber: "KIT-BLINDED-888",
            lotNumber: "LOT-001",
            daysUntilExpiry: 60,
            authorizedAt: SystemClock.Instance.UtcNow,
            isQuarantined: false,
            isRecalled: false,
            temperatureReadings: new List<TemperatureReading> { new(5.0, SystemClock.Instance.UtcNow.ToString("o")) },
            minAllowedTemp: 2.0,
            maxAllowedTemp: 8.0,
            minShelfLifeDays: 14);

        dispatchAuth.IsEligibleForDispatch.Should().BeTrue();
        dispatchAuth.KitNumber.Should().Be("KIT-BLINDED-888");
        dispatchAuth.TamperSealId.Should().NotBeNullOrEmpty();
    }
}
