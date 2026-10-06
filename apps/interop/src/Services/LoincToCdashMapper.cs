using System;
using System.Collections.Generic;

namespace Cadence.Apps.Interop.Services;

public record CdashMappingResult(
    string TargetDomain,
    string TargetVariable,
    string CdashTestcd,
    string CdashTest,
    string MappingProvenance);

public static class LoincToCdashMapper
{
    private static readonly Dictionary<string, (string Domain, string Variable, string TestCode, string TestName)> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["8480-6"] = ("VS", "SYSBP", "SYSBP", "Systolic Blood Pressure"),
        ["8867-4"] = ("VS", "PULSE", "PULSE", "Pulse Rate"),
        ["2339-0"] = ("LB", "GLUC", "GLUC", "Glucose Lab Test"),
        ["8310-5"] = ("VS", "TEMP", "TEMP", "Body Temperature"),
        ["29463-7"] = ("VS", "WEIGHT", "WEIGHT", "Body Weight"),
        ["8462-4"] = ("VS", "DIABP", "DIABP", "Diastolic Blood Pressure"),
    };

    public static CdashMappingResult? TryMapLoinc(string loincCode)
    {
        if (string.IsNullOrWhiteSpace(loincCode)) return null;

        var cleanCode = loincCode.Trim();
        if (Registry.TryGetValue(cleanCode, out var match))
        {
            var provenance = $"FHIR Observation.code LOINC {cleanCode} -> CDASH {match.Domain}.{match.Variable}";
            return new CdashMappingResult(match.Domain, match.Variable, match.TestCode, match.TestName, provenance);
        }

        return null;
    }
}
