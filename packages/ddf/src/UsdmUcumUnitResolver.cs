using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Kernel.Evidence;

namespace Cadence.Ddf;

internal static class UsdmUcumUnitResolver
{
    /// <summary>CDISC Unit codelist (C71620) codes whose UCUM form is certain.</summary>
    public static readonly FrozenDictionary<string, string> UcumUnits = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C28253"] = "mg",
        ["C48155"] = "g",
        ["C28252"] = "kg",
        ["C48152"] = "ug",
        ["C48531"] = "[lb_av]",
        ["C28254"] = "mL",
        ["C48505"] = "L",
        ["C64783"] = "dL",
        ["C25613"] = "%",
        ["C49670"] = "mm[Hg]",
        ["C49671"] = "{beats}/min",
        ["C49673"] = "{breaths}/min",
        ["C42559"] = "Cel",
        ["C44277"] = "[degF]",
        ["C49668"] = "cm",
        ["C41139"] = "m",
        ["C28251"] = "mm",
        ["C48500"] = "[in_i]",
        ["C29848"] = "a",
        ["C29846"] = "mo",
        ["C29844"] = "wk",
        ["C25301"] = "d",
        ["C25529"] = "h",
        ["C48154"] = "min",
        ["C42535"] = "s",
        ["C67015"] = "mmol/L",
        ["C64572"] = "umol/L",
        ["C67014"] = "mg/dL",
        ["C67306"] = "g/dL",
        ["C67388"] = "U/L",
        ["C48570"] = "IU/L",
        ["C49672"] = "kg/m2",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> UcumAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["kilogram"] = "kg",
        ["kilograms"] = "kg",
        ["kg"] = "kg",
        ["gram"] = "g",
        ["grams"] = "g",
        ["g"] = "g",
        ["milligram"] = "mg",
        ["milligrams"] = "mg",
        ["mg"] = "mg",
        ["microgram"] = "ug",
        ["micrograms"] = "ug",
        ["ug"] = "ug",
        ["mcg"] = "ug",
        ["pound"] = "[lb_av]",
        ["pounds"] = "[lb_av]",
        ["millimeter of mercury"] = "mm[Hg]",
        ["mmhg"] = "mm[Hg]",
        ["mm[hg]"] = "mm[Hg]",
        ["degree celsius"] = "Cel",
        ["celsius"] = "Cel",
        ["cel"] = "Cel",
        ["degree fahrenheit"] = "[degF]",
        ["fahrenheit"] = "[degF]",
        ["centimeter"] = "cm",
        ["centimeters"] = "cm",
        ["cm"] = "cm",
        ["millimeter"] = "mm",
        ["millimeters"] = "mm",
        ["mm"] = "mm",
        ["meter"] = "m",
        ["meters"] = "m",
        ["m"] = "m",
        ["milliliter"] = "mL",
        ["milliliters"] = "mL",
        ["ml"] = "mL",
        ["liter"] = "L",
        ["liters"] = "L",
        ["l"] = "L",
        ["deciliter"] = "dL",
        ["deciliters"] = "dL",
        ["dl"] = "dL",
        ["percent"] = "%",
        ["percentage"] = "%",
        ["%"] = "%",
        ["beats per minute"] = "{beats}/min",
        ["bpm"] = "{beats}/min",
        ["breaths per minute"] = "{breaths}/min",
        ["year"] = "a",
        ["years"] = "a",
        ["month"] = "mo",
        ["months"] = "mo",
        ["week"] = "wk",
        ["weeks"] = "wk",
        ["day"] = "d",
        ["days"] = "d",
        ["hour"] = "h",
        ["hours"] = "h",
        ["minute"] = "min",
        ["minutes"] = "min",
        ["second"] = "s",
        ["seconds"] = "s",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool TryReadExplicitUnit(JsonElement container, string itemId, List<UsdmImportNote> notes, out string? unit)
    {
        unit = null;
        if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty("unit", out var unitProp))
        {
            return false;
        }

        if (unitProp.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (unitProp.ValueKind == JsonValueKind.String)
        {
            var raw = unitProp.GetString()?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            if (TryResolveUcumUnit(null, raw, allowArbitraryUcumExpression: true, out var resolved))
            {
                unit = resolved;
                return true;
            }

            notes.Add(new UsdmImportNote(itemId, $"unit '{raw}' is not a valid UCUM expression; imported item without fixed unit constraint."));
            return true;
        }

        if (unitProp.ValueKind == JsonValueKind.Object && Usdm.Code(unitProp) is { } unitCode)
        {
            if (TryResolveUcumUnit(unitCode.Code, unitCode.Decode, allowArbitraryUcumExpression: true, out var resolved))
            {
                unit = resolved;
                return true;
            }

            notes.Add(new UsdmImportNote(itemId, $"unit '{unitCode.Decode}' ({unitCode.Code}) has no canonical UCUM equivalent; imported item without fixed unit constraint."));
            return true;
        }

        return false;
    }

    public static bool TryResolveUcumUnit(
        string? cdiscCode,
        string? decodeOrSymbol,
        bool allowArbitraryUcumExpression,
        out string? ucum)
    {
        ucum = null;
        if (!string.IsNullOrWhiteSpace(cdiscCode) && UcumUnits.TryGetValue(cdiscCode.Trim(), out var byCode))
        {
            ucum = byCode;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(decodeOrSymbol))
        {
            var trimmed = decodeOrSymbol.Trim();
            if (UcumAliases.TryGetValue(trimmed, out var byAlias))
            {
                ucum = byAlias;
                return true;
            }

            if (allowArbitraryUcumExpression && Ucum.Problem(trimmed) is null)
            {
                ucum = trimmed;
                return true;
            }
        }

        return false;
    }
}
