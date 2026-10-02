namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Result structure for UCUM unit canonicalization and normalization status.
/// </summary>
public sealed record UcumNormalizationResult(
    bool IsNormalized,
    double? NormalizedValue,
    string? NormalizedUnit,
    string StatusCode)
{
    public void Deconstruct(out double? normalizedValue, out string? normalizedUnit)
    {
        normalizedValue = NormalizedValue;
        normalizedUnit = NormalizedUnit;
    }

    public void Deconstruct(out bool isNormalized, out double? normalizedValue, out string? normalizedUnit, out string statusCode)
    {
        isNormalized = IsNormalized;
        normalizedValue = NormalizedValue;
        normalizedUnit = NormalizedUnit;
        statusCode = StatusCode;
    }
}

/// <summary>
/// Service abstraction for UCUM unit canonicalization and mathematical conversion.
/// @req:PRD-SYS-042
/// @req:SRS-EDC-001
/// </summary>
public interface IUcumNormalizationService
{
    string NormalizeUnitName(string unit);

    double ConvertUnit(double value, string fromUnit, string toUnit);

    UcumNormalizationResult GetNormalizedRepresentation(double? value, string? unit);
}
