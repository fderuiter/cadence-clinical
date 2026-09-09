namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Service abstraction for UCUM unit canonicalization and mathematical conversion.
/// @req:PRD-SYS-042
/// @req:SRS-EDC-001
/// </summary>
public interface IUcumNormalizationService
{
    string NormalizeUnitName(string unit);

    double ConvertUnit(double value, string fromUnit, string toUnit);

    (double? NormalizedValue, string? NormalizedUnit) GetNormalizedRepresentation(double? value, string? unit);
}
