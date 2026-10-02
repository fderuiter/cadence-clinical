using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cadence.Core.Domain;

/// <summary>
/// The JSON text Python's <c>json.dumps(..., sort_keys=True)</c> writes with its default settings (ASCII only,
/// <c>", "</c> and <c>": "</c>), so a port can reproduce a checksum the Python reference computed.
/// </summary>
internal static class PythonJson
{
    /// <summary>Writes null, a bool, an integer, a double, a string, a string-keyed map (keys sorted) or a list.</summary>
    internal static string Value(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => String(s),
        double d => Float(d),
        float f => Float(f),
        int or long or short or byte => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        IEnumerable<KeyValuePair<string, object?>> map =>
            "{" + string.Join(", ", map.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{String(v.Key)}: {Value(v.Value)}")) + "}",
        IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(Value)) + "]",
        _ => throw new ArgumentException($"No Python JSON form for {value.GetType().Name}.", nameof(value)),
    };

    internal static string Object(IReadOnlyDictionary<string, double> values) =>
        "{" + string.Join(", ", values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{String(v.Key)}: {Float(v.Value)}")) + "}";

    /// <summary>Python's <c>float.__repr__</c>: shortest round-trip digits, always with a fraction or exponent.</summary>
    internal static string Float(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsInfinity(value)) return value > 0 ? "Infinity" : "-Infinity";

        // Python writes positional notation for exponents from -4 up to 15, and scientific notation otherwise. .NET's
        // round-trip format agrees except from 1e15 to 1e16, where it switches to scientific notation one power early.
        string roundTrip = value.ToString("R", CultureInfo.InvariantCulture);
        double magnitude = Math.Abs(value);

        // Below the smallest subnormal means zero (either sign).
        if (magnitude < double.Epsilon || (magnitude >= 1e-4 && magnitude < 1e16))
        {
            string positional = roundTrip.Contains('E', StringComparison.Ordinal)
                ? value.ToString("F1", CultureInfo.InvariantCulture)
                : roundTrip;
            return positional.Contains('.', StringComparison.Ordinal) ? positional : positional + ".0";
        }

        int e = roundTrip.IndexOf('E', StringComparison.Ordinal);
        string mantissa = roundTrip[..e];
        int exponent = int.Parse(roundTrip[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return $"{mantissa}e{(exponent < 0 ? "-" : "+")}{Math.Abs(exponent):00}";
    }

    internal static string String(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
        {
            text.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                _ when c < 0x20 || c > 0x7f => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        return text.Append('"').ToString();
    }
}
