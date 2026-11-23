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
}
