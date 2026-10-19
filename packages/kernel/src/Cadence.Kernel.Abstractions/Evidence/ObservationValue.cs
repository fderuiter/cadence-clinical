using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// The value of a recorded clinical fact (spec §8.1, K-FACT-2, K-FACT-3, ADR-2623): exactly one of a quantity with its
/// UCUM unit, a code from a pinned code-system version, text, a boolean, an ISO 8601 date or time at the precision it was
/// known, or a reasoned absence. It is the value <em>as entered</em>: a normalised unit, a standardised result or a
/// decoded label is a derivation of it, never a replacement (K-FACT-2).
/// </summary>
/// <remarks>
/// <para>JSON carries the variant in a <c>type</c> member (<c>quantity</c>, <c>coded</c>, <c>text</c>, <c>boolean</c>,
/// <c>dateTime</c>, <c>missing</c>) beside the variant's own members, so the HTTP contract, the event payloads and the
/// <c>jsonb</c> columns share one shape (member names follow each surface's naming policy: the kernel API writes
/// <c>code_system_version_id</c>, storage <c>codeSystemVersionId</c>).</para>
/// <para>A value can be constructed in any state, because JSON reads it; <see cref="Problem"/> says what makes it
/// invalid, and commands refuse an invalid value with <c>K.Observation.ValueInvalid</c> instead of failing to bind.</para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(QuantityValue), "quantity")]
[JsonDerivedType(typeof(CodedValue), "coded")]
[JsonDerivedType(typeof(TextValue), "text")]
[JsonDerivedType(typeof(BooleanValue), "boolean")]
[JsonDerivedType(typeof(DateTimeValue), "dateTime")]
[JsonDerivedType(typeof(MissingValue), "missing")]
public abstract record ObservationValue
{
    /// <summary>The longest text, code or detail the kernel stores in a value.</summary>
    public const int MaxTextLength = 4000;

    /// <summary>The longest UCUM unit expression the kernel stores.</summary>
    public const int MaxUnitLength = 64;

    private protected ObservationValue() { }

    /// <summary>Which variant this is.</summary>
    [JsonIgnore]
    public abstract ObservationValueKind Kind { get; }

    /// <summary>Why the value is invalid, or null when it is valid.</summary>
    public abstract string? Problem();

    /// <summary>The value as one line of text for people and for a standard's original-result variable.</summary>
    public abstract string ToDisplayString();

    /// <summary>Why <paramref name="value"/> cannot be recorded: absent, or invalid (<see cref="Problem"/>); null when it can.</summary>
    public static string? ProblemOf(ObservationValue? value) =>
        value is null ? "A value is required: a quantity, coded, text, boolean, dateTime or missing value." : value.Problem();

    private protected static string? TextProblem(string? text, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"{what} is required.";
        }

        return text.Length > MaxTextLength ? $"{what} is at most {MaxTextLength} characters." : null;
    }
}

/// <summary>The variants of <see cref="ObservationValue"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ObservationValueKind>))]
public enum ObservationValueKind
{
    Quantity,
    Coded,
    Text,
    Boolean,
    DateTime,
    Missing,
}

/// <summary>
/// A measured amount in the UCUM unit it was entered in (K-FACT-2), such as 120 <c>mm[Hg]</c>, with an optional
/// comparator for a bound such as &lt;0.5 <c>mg/dL</c>. The decimal keeps the precision it was entered with (1.50 stays
/// 1.50).
/// </summary>
/// <param name="Value">The amount as entered.</param>
/// <param name="Unit">The UCUM unit expression, case-sensitive; <c>1</c> for a dimensionless quantity.</param>
/// <param name="Comparator">For a bound, how the true amount relates to <paramref name="Value"/>; null for an exact amount.</param>
public sealed record QuantityValue(decimal Value, string Unit, QuantityComparator? Comparator = null) : ObservationValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override ObservationValueKind Kind => ObservationValueKind.Quantity;

    /// <inheritdoc />
    public override string? Problem() =>
        Ucum.Problem(Unit)
        ?? (Comparator is { } comparator && !Enum.IsDefined(comparator) ? $"\"{comparator}\" is not a comparator." : null);

    /// <inheritdoc />
    public override string ToDisplayString() =>
        $"{ComparatorSymbol(Comparator)}{Value.ToString(CultureInfo.InvariantCulture)} {Unit}";

    /// <summary>The value as a standard writes a result: the comparator, then the amount, without the unit.</summary>
    public string ToResultString() => $"{ComparatorSymbol(Comparator)}{Value.ToString(CultureInfo.InvariantCulture)}";

    private static string ComparatorSymbol(QuantityComparator? comparator) => comparator switch
    {
        QuantityComparator.LessThan => "<",
        QuantityComparator.LessOrEqual => "<=",
        QuantityComparator.GreaterThan => ">",
        QuantityComparator.GreaterOrEqual => ">=",
        _ => string.Empty,
    };
}

/// <summary>How a bounded quantity's true amount relates to its stated amount.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuantityComparator>))]
public enum QuantityComparator
{
    [JsonStringEnumMemberName("<")]
    LessThan,

    [JsonStringEnumMemberName("<=")]
    LessOrEqual,

    [JsonStringEnumMemberName(">")]
    GreaterThan,

    [JsonStringEnumMemberName(">=")]
    GreaterOrEqual,
}

/// <summary>
/// A code from one pinned code-system version (spec §6.1), such as an NCI CT submission value in CDISC CT 2025-09-26 or
/// a LOINC answer code. The version is part of the value, so the same code read years later still means what it meant.
/// </summary>
/// <param name="CodeSystemVersionId">The code-system version the code was chosen from.</param>
/// <param name="Code">The code, exactly as the code system writes it.</param>
public sealed record CodedValue(CodeSystemVersionId CodeSystemVersionId, string Code) : ObservationValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override ObservationValueKind Kind => ObservationValueKind.Coded;

    /// <inheritdoc />
    public override string? Problem() =>
        CodeSystemVersionId.IsEmpty ? "A coded value names the code-system version its code was chosen from." : TextProblem(Code, "The code");

    /// <inheritdoc />
    public override string ToDisplayString() => Code;
}

/// <summary>Free text as entered, such as a verbatim adverse event term.</summary>
/// <param name="Value">The text.</param>
public sealed record TextValue(string Value) : ObservationValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override ObservationValueKind Kind => ObservationValueKind.Text;

    /// <inheritdoc />
    public override string? Problem() => TextProblem(Value, "The text");

    /// <inheritdoc />
    public override string ToDisplayString() => Value;
}

/// <summary>A yes or no answer.</summary>
/// <param name="Value">The answer.</param>
public sealed record BooleanValue(bool Value) : ObservationValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override ObservationValueKind Kind => ObservationValueKind.Boolean;

    /// <inheritdoc />
    public override string? Problem() => null;

    /// <inheritdoc />
    public override string ToDisplayString() => Value ? "true" : "false";
}

/// <summary>
/// An ISO 8601 date or date and time at the precision it was known (ISO 8601-1 reduced precision): <c>2026</c>,
/// <c>2026-03</c>, <c>2026-03-14</c>, <c>2026-03-14T09:30</c> or <c>2026-03-14T09:30:15</c>. A time may carry <!-- deid-ignore: ISO 8601 format examples, not PHI -->
/// <c>Z</c> or an offset; without one it is the local time where it was observed. A partial date is never padded to a
/// full one.
/// </summary>
/// <param name="Value">The ISO 8601 text.</param>
public sealed partial record DateTimeValue(string Value) : ObservationValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override ObservationValueKind Kind => ObservationValueKind.DateTime;

    /// <summary>How precisely the date or time is known, or null when <see cref="Value"/> is not valid ISO 8601.</summary>
    public DateTimePrecision? Precision => Parse(Value);

    /// <inheritdoc />
    public override string? Problem() =>
        Precision is null
            ? $"\"{Value}\" is not an ISO 8601 date or time: use YYYY, YYYY-MM, YYYY-MM-DD, YYYY-MM-DDThh:mm or YYYY-MM-DDThh:mm:ss, optionally with Z or an offset."
            : null;

    /// <inheritdoc />
    public override string ToDisplayString() => Value;

    private static DateTimePrecision? Parse(string? value)
    {
        if (value is null || IsoPattern().Match(value) is not { Success: true } match)
        {
            return null;
        }

        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        if (year < 1)
        {
            return null;
        }

        if (!match.Groups["month"].Success)
        {
            return DateTimePrecision.Year;
        }

        var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12)
        {
            return null;
        }

        if (!match.Groups["day"].Success)
        {
            return DateTimePrecision.Month;
        }

        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        if (!match.Groups["hour"].Success)
        {
            return DateTimePrecision.Day;
        }

        if (int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture) > 23
            || int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) > 59
            || (match.Groups["second"].Success && int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture) > 59)
            || (match.Groups["offsetHour"].Success && int.Parse(match.Groups["offsetHour"].Value, CultureInfo.InvariantCulture) > 14)
            || (match.Groups["offsetMinute"].Success && int.Parse(match.Groups["offsetMinute"].Value, CultureInfo.InvariantCulture) > 59))
        {
            return null;
        }

        return match.Groups["second"].Success ? DateTimePrecision.Second : DateTimePrecision.Minute;
    }

    [GeneratedRegex(
        @"^(?<year>\d{4})(?:-(?<month>\d{2})(?:-(?<day>\d{2})(?:T(?<hour>\d{2}):(?<minute>\d{2})(?::(?<second>\d{2}))?(?:Z|[+-](?<offsetHour>\d{2}):(?<offsetMinute>\d{2}))?)?)?)?$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex IsoPattern();
}

/// <summary>How precisely a <see cref="DateTimeValue"/> is known.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DateTimePrecision>))]
public enum DateTimePrecision
{
    Year,
    Month,
    Day,
    Minute,
    Second,
}

/// <summary>
/// A value that was not obtained, with why (K-FACT-3): the test was not done, the answer is unknown, the question does
/// not apply, and so on. It is a recorded fact in its own right, corrected like any other value.
/// </summary>
/// <param name="Reason">Why there is no value.</param>
/// <param name="Detail">More about why, such as the reason a test was not done (SDTM <c>--REASND</c>); optional.</param>
public sealed record MissingValue(NullFlavor Reason, string? Detail = null) : ObservationValue
{
    /// <inheritdoc />
    [JsonIgnore]
    public override ObservationValueKind Kind => ObservationValueKind.Missing;

    /// <inheritdoc />
    public override string? Problem()
    {
        if (!Enum.IsDefined(Reason))
        {
            return $"\"{Reason}\" is not a reason for a missing value.";
        }

        return Detail is null ? null : TextProblem(Detail, "The detail");
    }

    /// <inheritdoc />
    public override string ToDisplayString() => Detail is null ? Reason.ToString() : $"{Reason}: {Detail}";
}

/// <summary>Why a value is missing (HL7 null flavors, narrowed to the ones clinical data collection uses).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NullFlavor>))]
public enum NullFlavor
{
    /// <summary>The test or assessment was not performed (SDTM <c>--STAT = NOT DONE</c>).</summary>
    NotDone,

    /// <summary>A value applies but is not known.</summary>
    Unknown,

    /// <summary>The source was asked and did not know (HL7 ASKU).</summary>
    AskedButUnknown,

    /// <summary>The source was not asked (HL7 NASK).</summary>
    NotAsked,

    /// <summary>No value applies to this participant (HL7 NA).</summary>
    NotApplicable,

    /// <summary>A value exists but is withheld, for example for blinding (HL7 MSK).</summary>
    Masked,
}

/// <summary>
/// Syntax checks for UCUM unit expressions. The kernel checks that a unit is a well-formed UCUM expression; whether it
/// is the unit an item allows is the collection specification's check (<c>CollectionItem.Unit</c>).
/// </summary>
public static class Ucum
{
    /// <summary>Why <paramref name="unit"/> is not a UCUM expression, or null when it is well-formed.</summary>
    public static string? Problem(string? unit)
    {
        if (string.IsNullOrEmpty(unit))
        {
            return "A quantity needs a UCUM unit; use \"1\" for a dimensionless quantity.";
        }

        if (unit.Length > ObservationValue.MaxUnitLength)
        {
            return $"A UCUM unit is at most {ObservationValue.MaxUnitLength} characters.";
        }

        // UCUM terminal symbols are printable ASCII without spaces; brackets and braces must balance and not nest.
        if (unit.Any(c => c is < '!' or > '~'))
        {
            return $"\"{unit}\" is not a UCUM unit: units are written in printable ASCII without spaces.";
        }

        var open = new Stack<char>();
        foreach (var c in unit)
        {
            switch (c)
            {
                case '(' or '[' or '{':
                    if (open.Count > 0 && open.Peek() is '[' or '{')
                    {
                        return $"\"{unit}\" is not a UCUM unit: annotations and bracketed symbols cannot contain brackets.";
                    }

                    open.Push(c);
                    break;
                case ')' or ']' or '}':
                    if (open.Count == 0 || open.Pop() != Opening(c))
                    {
                        return $"\"{unit}\" is not a UCUM unit: its brackets do not balance.";
                    }

                    break;
            }
        }

        return open.Count == 0 ? null : $"\"{unit}\" is not a UCUM unit: its brackets do not balance.";
    }

    private static char Opening(char closing) => closing switch
    {
        ')' => '(',
        ']' => '[',
        _ => '{',
    };
}
