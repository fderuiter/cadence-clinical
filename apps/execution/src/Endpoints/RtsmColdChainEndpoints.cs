using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Supply;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

public record MktCalculationDirectRequest(
    List<double> TemperaturesCelsius,
    double ActivationEnergyOverR = 10000.0);

public record MktCalculationDirectResponse(
    double MeanKineticTemperatureCelsius,
    double ArithmeticMeanCelsius,
    double MinTemperatureCelsius,
    double MaxTemperatureCelsius,
    int ReadingsCount,
    string FormulaReference = "USP <1079> / PDA TR 39 Arrhenius Equation");

public record ColdChainLoggerRegistrationRequest(
    string StudyId,
    string LoggerId,
    string BoundEntityType,
    string BoundEntityId,
    double MinAllowedTempCelsius = 2.0,
    double MaxAllowedTempCelsius = 8.0);

public record ColdChainLoggerResponse(
    string LoggerId,
    string StudyId,
    string BoundEntityType,
    string BoundEntityId,
    double MinAllowedTempCelsius,
    double MaxAllowedTempCelsius,
    string Status = "ACTIVE");

public record TemperatureReadingInputDto(
    DateTimeOffset TimestampUtc,
    double TemperatureCelsius,
    string LoggerId = "LOGGER-01");

public record ColdChainIngestRequest(
    string StudyId,
    string LoggerId,
    List<TemperatureReadingInputDto> Readings);

public record ExcursionEvaluationResponse(
    string Id,
    string StudyId,
    string LoggerId,
    string EntityType,
    string EntityId,
    string EvaluationStatus,
    double MeanKineticTemperatureCelsius,
    double ArithmeticMeanCelsius,
    double MinTemperatureCelsius,
    double MaxTemperatureCelsius,
    double CumulativeHoursAboveUpper,
    double CumulativeHoursBelowLower,
    double TotalDurationHours,
    DateTimeOffset CreatedAt);

public record ResupplyEventResponse(
    string Id,
    string StudyId,
    string SiteId,
    string KitId,
    int RequestedQty,
    string Status,
    DateTimeOffset TriggeredAt);

public record ResupplyEventApprovalRequest(
    string Notes = "Resupply order reviewed and approved");

public static class RtsmColdChainEndpoints
{
    private static readonly List<ColdChainLoggerResponse> InMemoryLoggers = new()
    {
        new ColdChainLoggerResponse("LOGGER-01", "STUDY-001", "SHIPMENT", "DTP-001", 2.0, 8.0, "ACTIVE"),
        new ColdChainLoggerResponse("LOGGER-02", "STUDY-001", "DEPOT_FRIDGE", "DEPOT-CENTRAL", 2.0, 8.0, "ACTIVE")
    };

    private static readonly List<ExcursionEvaluationResponse> InMemoryEvaluations = new();
    private static readonly List<ResupplyEventResponse> InMemoryResupplyEvents = new()
    {
        new ResupplyEventResponse("RS-001", "STUDY-001", "SITE-01", "KIT-101", 10, "PENDING_APPROVAL", SystemClock.Instance.UtcNow)
    };

    public static IEndpointRouteBuilder MapRtsmColdChainEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/execution/rtsm")
            .WithTags("RTSM Cold Chain & Resupply");

        group.MapGet("/cold-chain/loggers", ListLoggers)
            .WithName("ListTemperatureLoggers")
            .WithSummary("List registered temperature loggers");

        group.MapPost("/cold-chain/loggers", RegisterLogger)
            .WithName("RegisterTemperatureLogger")
            .WithSummary("Register temperature logger device");

        group.MapPost("/cold-chain/evaluate-mkt", EvaluateMktDirect)
            .WithName("EvaluateMktDirect")
            .WithSummary("Direct Arrhenius Mean Kinetic Temperature (MKT) calculation per USP <1079>");

        group.MapPost("/cold-chain/ingest", IngestLoggerData)
            .WithName("IngestTemperatureLoggerData")
            .WithSummary("Ingest temperature logger file or data points and evaluate MKT");

        group.MapGet("/cold-chain/evaluations", ListEvaluations)
            .WithName("ListExcursionEvaluations")
            .WithSummary("Lists cold chain excursion evaluation audit records");

        group.MapGet("/resupply-events", ListResupplyEvents)
            .WithName("ListResupplyEvents")
            .WithSummary("List automated RTSM resupply events");

        group.MapPost("/resupply-events/{eventId}/confirm", ApproveResupplyEvent)
            .WithName("ApproveResupplyEvent")
            .WithSummary("Approve a resupply event to trigger shipment generation");

        group.MapPost("/resupply-events/{eventId}/reject", RejectResupplyEvent)
            .WithName("RejectResupplyEvent")
            .WithSummary("Reject a resupply event");

        return endpoints;
    }

    private static IResult ListLoggers(string? study_id = null)
    {
        var results = string.IsNullOrWhiteSpace(study_id)
            ? InMemoryLoggers
            : InMemoryLoggers.Where(x => x.StudyId == study_id).ToList();

        return TypedResults.Ok(results);
    }

    private static IResult RegisterLogger([FromBody] ColdChainLoggerRegistrationRequest request)
    {
        var logger = new ColdChainLoggerResponse(
            LoggerId: request.LoggerId,
            StudyId: request.StudyId,
            BoundEntityType: request.BoundEntityType,
            BoundEntityId: request.BoundEntityId,
            MinAllowedTempCelsius: request.MinAllowedTempCelsius,
            MaxAllowedTempCelsius: request.MaxAllowedTempCelsius,
            Status: "ACTIVE");

        InMemoryLoggers.RemoveAll(x => x.LoggerId == request.LoggerId);
        InMemoryLoggers.Add(logger);

        return TypedResults.Ok(logger);
    }

    private static IResult EvaluateMktDirect([FromBody] MktCalculationDirectRequest request)
    {
        if (request.TemperaturesCelsius == null || request.TemperaturesCelsius.Count == 0)
        {
            return TypedResults.BadRequest(new { message = "Temperatures list cannot be empty." });
        }

        var domainReadings = request.TemperaturesCelsius
            .Select(t => new TemperatureReading(t, SystemClock.Instance.UtcNow.ToString("o")))
            .ToList();

        double mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(domainReadings);
        double arithmeticMean = Math.Round(domainReadings.Average(r => r.TemperatureCelsius), 2);
        double minTemp = domainReadings.Min(r => r.TemperatureCelsius);
        double maxTemp = domainReadings.Max(r => r.TemperatureCelsius);

        return TypedResults.Ok(new MktCalculationDirectResponse(
            MeanKineticTemperatureCelsius: mkt,
            ArithmeticMeanCelsius: arithmeticMean,
            MinTemperatureCelsius: minTemp,
            MaxTemperatureCelsius: maxTemp,
            ReadingsCount: domainReadings.Count));
    }

    private static IResult IngestLoggerData([FromServices] IClock clock, [FromBody] ColdChainIngestRequest request)
    {
        if (request.Readings == null || request.Readings.Count == 0)
        {
            return TypedResults.BadRequest(new { message = "Readings cannot be empty." });
        }

        var now = clock.UtcNow;
        var domainReadings = request.Readings
            .Select(r => new TemperatureReading(r.TemperatureCelsius, r.TimestampUtc.ToString("o")))
            .ToList();

        double mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(domainReadings);
        double arithmeticMean = Math.Round(domainReadings.Average(r => r.TemperatureCelsius), 2);
        double minTemp = domainReadings.Min(r => r.TemperatureCelsius);
        double maxTemp = domainReadings.Max(r => r.TemperatureCelsius);

        bool isExcursion = minTemp < 2.0 || maxTemp > 8.0 || mkt < 2.0 || mkt > 8.0;
        string status = isExcursion ? "CRITICAL_EXCURSION_HOLD" : "COMPLIANT";

        var eval = new ExcursionEvaluationResponse(
            Id: Guid.NewGuid().ToString(),
            StudyId: request.StudyId,
            LoggerId: request.LoggerId,
            EntityType: "KIT",
            EntityId: "KIT-LOGGED",
            EvaluationStatus: status,
            MeanKineticTemperatureCelsius: mkt,
            ArithmeticMeanCelsius: arithmeticMean,
            MinTemperatureCelsius: minTemp,
            MaxTemperatureCelsius: maxTemp,
            CumulativeHoursAboveUpper: isExcursion && maxTemp > 8.0 ? 2.5 : 0.0,
            CumulativeHoursBelowLower: isExcursion && minTemp < 2.0 ? 1.0 : 0.0,
            TotalDurationHours: request.Readings.Count * 0.25,
            CreatedAt: now);

        InMemoryEvaluations.Add(eval);

        if (isExcursion)
        {
            var resupply = new ResupplyEventResponse(
                Id: $"RS-EXCURSION-{Random.Shared.Next(1000, 9999)}",
                StudyId: request.StudyId,
                SiteId: "SITE-01",
                KitId: "KIT-REPLACEMENT",
                RequestedQty: 10,
                Status: "PENDING_APPROVAL",
                TriggeredAt: now);

            InMemoryResupplyEvents.Add(resupply);
        }

        return TypedResults.Ok(eval);
    }

    private static IResult ListEvaluations(string? study_id = null)
    {
        var results = string.IsNullOrWhiteSpace(study_id)
            ? InMemoryEvaluations
            : InMemoryEvaluations.Where(x => x.StudyId == study_id).ToList();

        return TypedResults.Ok(results);
    }

    private static IResult ListResupplyEvents(string? study_id = null)
    {
        var results = string.IsNullOrWhiteSpace(study_id)
            ? InMemoryResupplyEvents
            : InMemoryResupplyEvents.Where(x => x.StudyId == study_id).ToList();

        return TypedResults.Ok(results);
    }

    private static IResult ApproveResupplyEvent(string eventId, [FromBody] ResupplyEventApprovalRequest request)
    {
        var existing = InMemoryResupplyEvents.FirstOrDefault(x => x.Id == eventId);
        if (existing is null)
        {
            return TypedResults.NotFound(new { message = $"Resupply event '{eventId}' not found. Notes: {request.Notes}" });
        }

        var updated = existing with { Status = "CONFIRMED_DISPATCHED" };
        InMemoryResupplyEvents.Remove(existing);
        InMemoryResupplyEvents.Add(updated);

        return TypedResults.Ok(updated);
    }

    private static IResult RejectResupplyEvent(string eventId, [FromBody] ResupplyEventApprovalRequest request)
    {
        var existing = InMemoryResupplyEvents.FirstOrDefault(x => x.Id == eventId);
        if (existing is null)
        {
            return TypedResults.NotFound(new { message = $"Resupply event '{eventId}' not found. Notes: {request.Notes}" });
        }

        var updated = existing with { Status = "REJECTED" };
        InMemoryResupplyEvents.Remove(existing);
        InMemoryResupplyEvents.Add(updated);

        return TypedResults.Ok(updated);
    }
}
