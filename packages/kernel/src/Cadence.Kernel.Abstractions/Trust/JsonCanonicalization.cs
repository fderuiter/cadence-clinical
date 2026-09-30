using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Kernel.Trust;

/// <summary>
/// The JSON Canonicalization Scheme of RFC 8785 (JCS), for hashes that a client computes independently of the server,
/// such as the payload hash a signer puts in a form approval. Object members are sorted by their names' UTF-16 code
/// units, there is no insignificant whitespace, strings use only the escapes <c>JSON.stringify</c> uses, and numbers are
/// written as ECMAScript writes an IEEE 754 double (<c>1.50</c> and <c>15e-1</c> both become <c>1.5</c>). The result is
/// what <c>canonicalSerialize</c> in <c>packages/ui/signing.js</c> produces for the same parsed JSON, so the text a
/// client submitted and the text PostgreSQL returns from a <c>jsonb</c> column canonicalize the same.
/// </summary>
/// <remarks>
/// <see cref="ContentHash"/> keeps number text as written and is not changed by this type: existing kernel hashes stay
/// valid.
/// </remarks>
public static class JsonCanonicalization
{
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

    /// <summary>The RFC 8785 canonical form of <paramref name="json"/>.</summary>
    /// <exception cref="JsonException">
    /// The text is not a single JSON value, repeats a member name, or holds a number outside the range of a double.
    /// </exception>
    public static string Canonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, ParseOptions);
        var builder = new StringBuilder(json.Length);
        Write(document.RootElement, builder);
        return builder.ToString();
    }

    /// <summary>The lower-case hex SHA-256 of the UTF-8 bytes of the canonical form of <paramref name="json"/>.</summary>
    /// <exception cref="JsonException">The text cannot be canonicalized (see <see cref="Canonicalize"/>).</exception>
    public static string Sha256Hex(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(json))));

    /// <summary>
    /// A double written as ECMAScript's <c>Number.prototype.toString</c> writes it (RFC 8785: serialization of numbers).
    /// </summary>
    /// <exception cref="JsonException">The value is NaN or infinite, which JSON cannot hold.</exception>
    public static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new JsonException("A JSON number must be a finite IEEE 754 double.");
        }

        // Zero and negative zero: every bit but the sign is clear.
        if ((BitConverter.DoubleToInt64Bits(value) & long.MaxValue) == 0)
        {
            return "0";
        }

        // The shortest digits that round-trip, as .NET and ECMAScript both choose them.
        string text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int exponentAt = text.IndexOfAny(['E', 'e']);
        int exponent = exponentAt < 0 ? 0 : int.Parse(text.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mantissa = exponentAt < 0 ? text : text[..exponentAt];
        int pointAt = mantissa.IndexOf('.', StringComparison.Ordinal);
        string digits = pointAt < 0 ? mantissa : string.Concat(mantissa.AsSpan(0, pointAt), mantissa.AsSpan(pointAt + 1));

        // value = 0.digits × 10^n
        int n = (pointAt < 0 ? mantissa.Length : pointAt) + exponent;
        int leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits[leadingZeros..].TrimEnd('0');
        n -= leadingZeros;
        int k = digits.Length;

        var result = new StringBuilder(k + 8);
        if (value < 0)
        {
            result.Append('-');
        }

        if (k <= n && n <= 21)
        {
            result.Append(digits).Append('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            result.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
        }
        else if (-6 < n && n <= 0)
        {
            result.Append("0.").Append('0', -n).Append(digits);
        }
        else
        {
            result.Append(digits[0]);
            if (k > 1)
            {
                result.Append('.').Append(digits, 1, k - 1);
            }

            int e = n - 1;
            result.Append('e').Append(e < 0 ? '-' : '+').Append(Math.Abs(e).ToString(CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }

    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                bool first = true;
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(property.Name, builder);
                    builder.Append(':');
                    Write(property.Value, builder);
                }

                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                bool firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }

                    firstItem = false;
                    Write(item, builder);
                }

                builder.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(element.GetString()!, builder);
                break;
            case JsonValueKind.Number:
                builder.Append(FormatNumber(double.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture)));
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    bool loneSurrogate = char.IsSurrogate(c)
                        && !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(value[i - 1]));
                    if (c < 0x20 || loneSurrogate)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
