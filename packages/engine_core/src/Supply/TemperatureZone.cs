using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Cadence.Core.Domain.Supply;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TemperatureZone
{
    CRYOGENIC,
    ULTRA_COLD,
    FROZEN,
    REFRIGERATED,
    CONTROLLED_AMBIENT
}

public record TemperatureExcursionResult(
    [property: JsonPropertyName("excursion_count")] int ExcursionCount,
    [property: JsonPropertyName("total_excursion_duration")] TimeSpan TotalExcursionDuration,
    [property: JsonPropertyName("lowest_temp_celsius")] double LowestTempCelsius,
    [property: JsonPropertyName("highest_temp_celsius")] double HighestTempCelsius,
    [property: JsonPropertyName("calculated_mkt_celsius")] double? CalculatedMktCelsius,
    [property: JsonPropertyName("primary_zone")] TemperatureZone PrimaryZone
);

public static class TemperatureZoneClassifier
{
    public static TemperatureZone ClassifyZone(double temperatureCelsius)
    {
        if (temperatureCelsius <= -70.0)
        {
            return TemperatureZone.CRYOGENIC;
        }

        if (temperatureCelsius <= -20.0)
        {
            return TemperatureZone.ULTRA_COLD;
        }

        if (temperatureCelsius < 2.0)
        {
            return TemperatureZone.FROZEN;
        }

        if (temperatureCelsius <= 8.0)
        {
            return TemperatureZone.REFRIGERATED;
        }

        return TemperatureZone.CONTROLLED_AMBIENT;
    }

    public static TemperatureExcursionResult CalculateExcursionMetrics(
        IReadOnlyList<TemperatureReading> readings,
        double minAllowedTemp = -196.0,
        double maxAllowedTemp = -70.0)
    {
        if (readings == null || readings.Count == 0)
        {
            return new TemperatureExcursionResult(
                ExcursionCount: 0,
                TotalExcursionDuration: TimeSpan.Zero,
                LowestTempCelsius: 0.0,
                HighestTempCelsius: 0.0,
                CalculatedMktCelsius: null,
                PrimaryZone: TemperatureZone.CRYOGENIC);
        }

        int excursionCount = 0;
        TimeSpan totalExcursionDuration = TimeSpan.Zero;
        double lowestTemp = double.MaxValue;
        double highestTemp = double.MinValue;
        DateTimeOffset? previousExcursionTime = null;

        foreach (var reading in readings)
        {
            double temp = reading.TemperatureCelsius;
            if (temp < lowestTemp)
            {
                lowestTemp = temp;
            }

            if (temp > highestTemp)
            {
                highestTemp = temp;
            }

            bool isExcursion = temp < minAllowedTemp || temp > maxAllowedTemp;
            if (isExcursion)
            {
                excursionCount++;

                if (DateTimeOffset.TryParse(reading.Timestamp, System.Globalization.CultureInfo.InvariantCulture, out var currentTs))
                {
                    if (previousExcursionTime.HasValue)
                    {
                        var gap = currentTs - previousExcursionTime.Value;
                        if (gap > TimeSpan.Zero)
                        {
                            totalExcursionDuration += gap;
                        }
                    }

                    previousExcursionTime = currentTs;
                }
                else
                {
                    // Fallback to 1 minute increment if timestamp is missing or unparseable
                    totalExcursionDuration += TimeSpan.FromMinutes(1);
                }
            }
            else
            {
                previousExcursionTime = null;
            }
        }

        double? mkt = null;
        try
        {
            mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(readings);
        }
        catch (ArgumentException)
        {
            // MKT calculation failed due to invalid Kelvin readings
        }

        var primaryZone = ClassifyZone(lowestTemp);

        return new TemperatureExcursionResult(
            ExcursionCount: excursionCount,
            TotalExcursionDuration: totalExcursionDuration,
            LowestTempCelsius: lowestTemp,
            HighestTempCelsius: highestTemp,
            CalculatedMktCelsius: mkt,
            PrimaryZone: primaryZone);
    }
}
