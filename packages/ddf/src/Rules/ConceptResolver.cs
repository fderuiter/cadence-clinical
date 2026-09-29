using System.Collections.Generic;

namespace Cadence.Ddf.Rules;

public static class ConceptResolver
{
    public static readonly IReadOnlyDictionary<string, (string Code, string Decode)> DefaultMockTerminology =
        new Dictionary<string, (string Code, string Decode)>
        {
            ["sys_bp"] = ("SYSBP", "Systolic Blood Pressure"),
            ["dia_bp"] = ("DIABP", "Diastolic Blood Pressure"),
            ["heart_rate"] = ("HR", "Heart Rate"),
            ["temp"] = ("TEMP", "Body Temperature"),
            ["weight"] = ("WEIGHT", "Body Weight"),
            ["screening"] = ("SCRN", "Screening Visit"),
            ["baseline"] = ("BASE", "Baseline Visit"),
            ["treatment"] = ("TRT", "Treatment Visit"),
            ["follow_up"] = ("FUP", "Follow-Up Visit")
        };

    public static string? ResolveConceptId(
        IReadOnlyDictionary<string, object?>? conceptDict,
        IReadOnlyDictionary<string, (string Code, string Decode)>? terminologyCache = null)
    {
        if (conceptDict is null) return null;

        if (conceptDict.TryGetValue("_original_id", out var origId) && origId != null && !string.IsNullOrEmpty(origId.ToString()))
        {
            return origId.ToString();
        }

        if (!conceptDict.TryGetValue("code", out var codeObj) || codeObj is null)
        {
            return null;
        }

        string code = codeObj.ToString()!;
        var cache = terminologyCache ?? DefaultMockTerminology;

        foreach (var (k, v) in cache)
        {
            if (v.Code == code)
            {
                return k;
            }
        }

        return code;
    }
}
