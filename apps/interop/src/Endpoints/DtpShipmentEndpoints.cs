using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Core.Domain.Supply;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Endpoints;

public static class DtpShipmentEndpoints
{
    public static IEndpointRouteBuilder MapDtpShipmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/interop/dct/dtp/shipments")
            .WithTags("DTP Logistics & Cold-Chain Telemetry");

        group.MapGet("/", ListDtpShipments)
            .WithName("ListDtpShipments")
            .WithSummary("List Direct-to-Patient drug shipments");

        group.MapPost("/", CreateDtpShipment)
            .WithName("CreateDtpShipment")
            .WithSummary("Create Direct-to-Patient (DTP) courier dispatch order");

        group.MapGet("/{trackingNumber}", GetDtpShipment)
            .WithName("GetDtpShipment")
            .WithSummary("Retrieve single DTP shipment details by tracking number");

        group.MapPost("/{trackingNumber}/temperature", IngestDtpTemperature)
            .WithName("IngestDtpTemperature")
            .WithSummary("Ingest cold-chain temperature telemetry points & calculate MKT");

        group.MapPost("/{trackingNumber}/handoff", RecordDtpHandoff)
            .WithName("RecordDtpHandoff")
            .WithSummary("Record Chain of Custody (CoC) handoff event");

        group.MapPost("/{trackingNumber}/delivery", ConfirmDtpDelivery)
            .WithName("ConfirmDtpDelivery")
            .WithSummary("Confirm final delivery with recipient signature");

        return endpoints;
    }

    private static async Task<IResult> ListDtpShipments(
        InteropDbContext dbContext,
        string? study_id = null,
        string? site_id = null,
        string? subject_pseudonym_id = null)
    {
        var query = dbContext.DTPShipments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(study_id))
        {
            query = query.Where(x => x.StudyId == study_id);
        }

        if (!string.IsNullOrWhiteSpace(site_id))
        {
            query = query.Where(x => x.SiteId == site_id);
        }

        if (!string.IsNullOrWhiteSpace(subject_pseudonym_id))
        {
            query = query.Where(x => x.SubjectPseudonymId == subject_pseudonym_id);
        }

        var records = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
        var dtos = records.Select(ToDTO).ToList();

        return TypedResults.Ok(new DtpShipmentListResponse(
            Success: true,
            Shipments: dtos,
            Total: dtos.Count));
    }

    private static async Task<IResult> CreateDtpShipment(
        InteropDbContext dbContext,
        [FromServices] IClock clock,
        [FromBody] DtpShipmentCreateRequest request)
    {
        var now = clock.UtcNow;
        var trackingNumber = $"TRK-{Random.Shared.Next(1000000, 9999999)}";
        var kitSerials = request.KitSerials ?? new List<string> { $"KIT-{Random.Shared.Next(100, 999)}" };
        var kitNumber = kitSerials.FirstOrDefault() ?? "KIT-100";

        var record = new DtpShipmentRecord
        {
            ShipmentId = $"DTP-{request.StudyId}-{request.SiteId}-{kitNumber}",
            TrackingNumber = trackingNumber,
            CourierName = string.IsNullOrWhiteSpace(request.CourierName) ? "World Courier" : request.CourierName,
            StudyId = request.StudyId,
            SiteId = request.SiteId,
            SubjectPseudonymId = request.SubjectPseudonymId,
            DestinationAddressPseudonym = request.DestinationAddressPseudonym,
            TemperatureZone = string.IsNullOrWhiteSpace(request.TemperatureZone) ? "REFRIGERATED" : request.TemperatureZone,
            KitSerialsJson = JsonSerializer.Serialize(kitSerials),
            TamperSealId = string.IsNullOrWhiteSpace(request.TamperSealId) ? $"SEAL-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}" : request.TamperSealId,
            Status = "ORDER_CREATED",
            CurrentTempCelsius = 5.0,
            MinTempCelsius = 5.0,
            MaxTempCelsius = 5.0,
            MktCelsius = 5.0,
            ExcursionSeverity = "NONE",
            CustodyHandoffsJson = JsonSerializer.Serialize(new List<object>
            {
                new { from_party = "Central Depot", to_phase = "COURIER_DISPATCHED", handed_over_by = "Depot Ops", timestamp_utc = now.ToString("o") }
            }),
            TemperatureReadingsJson = "[]",
            DeliverySignatureJson = null,
            CreatedAt = now,
            CreatedBy = "svc:dtp-logistics"
        };

        dbContext.DTPShipments.Add(record);

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityName = nameof(DtpShipmentRecord),
            EntityId = record.ShipmentId,
            Action = "CREATE_DTP_SHIPMENT",
            Timestamp = AuditLogEntry.NormalizeTimestamp(now.UtcDateTime),
            UserId = "svc:dtp-logistics",
            ReasonForChange = "Initial courier dispatch creation"
        });

        await dbContext.SaveChangesAsync();

        return TypedResults.Created($"/api/v1/interop/dct/dtp/shipments/{trackingNumber}",
            new DtpShipmentResponse(
                Success: true,
                Shipment: ToDTO(record),
                Message: "DTP shipment dispatch order created successfully."));
    }

    private static async Task<IResult> GetDtpShipment(
        InteropDbContext dbContext,
        string trackingNumber)
    {
        var record = await dbContext.DTPShipments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TrackingNumber == trackingNumber || x.ShipmentId == trackingNumber);

        if (record is null)
        {
            return TypedResults.NotFound(new { message = $"DTP shipment with tracking number or ID '{trackingNumber}' not found." });
        }

        return TypedResults.Ok(new DtpShipmentResponse(
            Success: true,
            Shipment: ToDTO(record),
            Message: "DTP shipment retrieved successfully."));
    }

    private static async Task<IResult> IngestDtpTemperature(
        InteropDbContext dbContext,
        [FromServices] IClock clock,
        string trackingNumber,
        [FromBody] TemperatureIngestRequest request)
    {
        var now = clock.UtcNow;
        var record = await dbContext.DTPShipments
            .FirstOrDefaultAsync(x => x.TrackingNumber == trackingNumber || x.ShipmentId == trackingNumber);

        if (record is null)
        {
            return TypedResults.NotFound(new { message = $"DTP shipment '{trackingNumber}' not found." });
        }

        var existingReadings = JsonSerializer.Deserialize<List<TemperatureReadingDto>>(record.TemperatureReadingsJson) ?? new();
        if (request.Readings != null && request.Readings.Count > 0)
        {
            existingReadings.AddRange(request.Readings);
        }

        var domainReadings = existingReadings
            .Select(r => new TemperatureReading(r.TemperatureCelsius, r.TimestampUtc.ToString("o")))
            .ToList();

        double calculatedMkt = domainReadings.Count > 0
            ? DTPLogisticsEngine.CalculateMeanKineticTemperature(domainReadings)
            : record.CurrentTempCelsius;

        double minTemp = domainReadings.Count > 0 ? domainReadings.Min(r => r.TemperatureCelsius) : record.MinTempCelsius;
        double maxTemp = domainReadings.Count > 0 ? domainReadings.Max(r => r.TemperatureCelsius) : record.MaxTempCelsius;
        double currentTemp = request.Readings?.LastOrDefault()?.TemperatureCelsius ?? record.CurrentTempCelsius;

        record.CurrentTempCelsius = Math.Round(currentTemp, 2);
        record.MinTempCelsius = Math.Round(minTemp, 2);
        record.MaxTempCelsius = Math.Round(maxTemp, 2);
        record.MktCelsius = Math.Round(calculatedMkt, 2);
        record.TemperatureReadingsJson = JsonSerializer.Serialize(existingReadings);

        bool isExcursion = minTemp < record.MinAllowedTemp || maxTemp > record.MaxAllowedTemp || calculatedMkt < record.MinAllowedTemp || calculatedMkt > record.MaxAllowedTemp;

        string message;
        if (isExcursion)
        {
            record.Status = "TEMP_EXCURSION_HOLD";
            record.ExcursionSeverity = "CRITICAL_EXCURSION_QUARANTINE";

            // Evaluate replacement kit allocation & dispatch order generation
            var kitSerials = JsonSerializer.Deserialize<List<string>>(record.KitSerialsJson) ?? new();
            var primaryKit = kitSerials.FirstOrDefault() ?? "KIT-EXCURSION";

            var dispatchAuth = DTPLogisticsEngine.EvaluateDtpDispatch(
                studyId: record.StudyId,
                siteId: record.SiteId,
                subjectId: record.SubjectPseudonymId,
                kitNumber: primaryKit,
                lotNumber: "LOT-001",
                daysUntilExpiry: 90,
                authorizedAt: now,
                isQuarantined: false,
                isRecalled: false,
                temperatureReadings: domainReadings,
                minAllowedTemp: record.MinAllowedTemp,
                maxAllowedTemp: record.MaxAllowedTemp,
                minShelfLifeDays: 14);

            var replacementDispatchId = $"DTP-{record.StudyId}-{record.SiteId}-REPLACEMENT-{dispatchAuth.KitNumber}";
            record.ReplacementDispatchId = replacementDispatchId;
            message = $"Temperature telemetry ingested: THERMAL EXCURSION DETECTED. Shipment transitioned to TEMP_EXCURSION_HOLD. Replacement kit {dispatchAuth.KitNumber} allocated.";

            // Save depot notification alert
            dbContext.SubjectNotifications.Add(new SubjectNotification
            {
                Id = Guid.NewGuid().ToString(),
                SubjectId = record.SubjectPseudonymId,
                Channel = "DEPOT_ALERT",
                DeliveryStatus = "PENDING",
                IsRead = false,
                CreatedAt = now,
                CreatedBy = "svc:cold-chain-engine",
                ReasonForChange = $"Cold chain excursion hold for DTP shipment {record.TrackingNumber}"
            });

            // Log GxP audit event (historical telemetry logs are immutable per 21 CFR Part 11)
            dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EntityName = nameof(DtpShipmentRecord),
                EntityId = record.ShipmentId,
                Action = "TEMP_EXCURSION_HOLD_TRIGGERED",
                Timestamp = AuditLogEntry.NormalizeTimestamp(now.UtcDateTime),
                UserId = "svc:cold-chain-engine",
                ReasonForChange = "USP <1079> / PDA TR 39 MKT thermal excursion hold"
            });
        }
        else
        {
            if (record.Status == "ORDER_CREATED" || record.Status == "COURIER_DISPATCHED")
            {
                record.Status = "IN_TRANSIT";
            }
            record.ExcursionSeverity = "NONE";
            message = "Temperature telemetry ingested successfully. Shipment within normal stability limits.";
        }

        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(new DtpShipmentResponse(
            Success: true,
            Shipment: ToDTO(record),
            Message: message));
    }

    private static async Task<IResult> RecordDtpHandoff(
        InteropDbContext dbContext,
        [FromServices] IClock clock,
        string trackingNumber,
        [FromBody] CustodyHandoffRequest request)
    {
        var now = clock.UtcNow;
        var record = await dbContext.DTPShipments
            .FirstOrDefaultAsync(x => x.TrackingNumber == trackingNumber || x.ShipmentId == trackingNumber);

        if (record is null)
        {
            return TypedResults.NotFound(new { message = $"DTP shipment '{trackingNumber}' not found." });
        }

        var handoffs = JsonSerializer.Deserialize<List<object>>(record.CustodyHandoffsJson) ?? new();
        handoffs.Add(new
        {
            from_party = request.FromParty,
            to_phase = request.ToPhase,
            handed_over_by = request.HandedOverBy,
            verification_seal = request.VerificationSeal,
            timestamp_utc = now.ToString("o")
        });

        record.CustodyHandoffsJson = JsonSerializer.Serialize(handoffs);
        if (record.Status != "TEMP_EXCURSION_HOLD")
        {
            record.Status = string.IsNullOrWhiteSpace(request.ToPhase) ? "IN_TRANSIT" : request.ToPhase;
        }

        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(new DtpShipmentResponse(
            Success: true,
            Shipment: ToDTO(record),
            Message: "Chain of Custody handoff recorded successfully."));
    }

    private static async Task<IResult> ConfirmDtpDelivery(
        InteropDbContext dbContext,
        [FromServices] IClock clock,
        string trackingNumber,
        [FromBody] DeliveryConfirmRequest request)
    {
        var now = clock.UtcNow;
        var record = await dbContext.DTPShipments
            .FirstOrDefaultAsync(x => x.TrackingNumber == trackingNumber || x.ShipmentId == trackingNumber);

        if (record is null)
        {
            return TypedResults.NotFound(new { message = $"DTP shipment '{trackingNumber}' not found." });
        }

        if (record.Status == "TEMP_EXCURSION_HOLD")
        {
            return TypedResults.BadRequest(new { message = "Cannot confirm delivery for shipment under TEMP_EXCURSION_HOLD." });
        }

        record.DeliverySignatureJson = JsonSerializer.Serialize(new
        {
            recipient_name = request.RecipientName,
            signature_token = request.SignatureToken,
            delivery_notes = request.DeliveryNotes,
            confirmed_at = now.ToString("o")
        });

        record.Status = "DELIVERED";

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EntityName = nameof(DtpShipmentRecord),
            EntityId = record.ShipmentId,
            Action = "CONFIRM_DTP_DELIVERY",
            Timestamp = AuditLogEntry.NormalizeTimestamp(now.UtcDateTime),
            UserId = "svc:courier-agent",
            ReasonForChange = "Final delivery confirmation with recipient electronic signature"
        });

        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(new DtpShipmentResponse(
            Success: true,
            Shipment: ToDTO(record),
            Message: "DTP shipment delivery confirmed successfully."));
    }

    private static DtpShipmentDto ToDTO(DtpShipmentRecord record)
    {
        var kitSerials = JsonSerializer.Deserialize<List<string>>(record.KitSerialsJson) ?? new();
        var custodyHandoffs = JsonSerializer.Deserialize<List<object>>(record.CustodyHandoffsJson) ?? new();
        var tempReadings = JsonSerializer.Deserialize<List<object>>(record.TemperatureReadingsJson) ?? new();
        object? deliverySig = !string.IsNullOrEmpty(record.DeliverySignatureJson)
            ? JsonSerializer.Deserialize<object>(record.DeliverySignatureJson)
            : null;

        return new DtpShipmentDto(
            ShipmentId: record.ShipmentId,
            TrackingNumber: record.TrackingNumber,
            CourierName: record.CourierName,
            StudyId: record.StudyId,
            SiteId: record.SiteId,
            SubjectPseudonymId: record.SubjectPseudonymId,
            DestinationAddressPseudonym: record.DestinationAddressPseudonym,
            TemperatureZone: record.TemperatureZone,
            KitSerials: kitSerials,
            TamperSealId: record.TamperSealId,
            Status: record.Status,
            CurrentTempCelsius: record.CurrentTempCelsius,
            MinTempCelsius: record.MinTempCelsius,
            MaxTempCelsius: record.MaxTempCelsius,
            MktCelsius: record.MktCelsius,
            ExcursionSeverity: record.ExcursionSeverity,
            CustodyHandoffs: custodyHandoffs,
            TemperatureReadings: tempReadings,
            DeliverySignature: deliverySig,
            CreatedAt: record.CreatedAt);
    }
}
