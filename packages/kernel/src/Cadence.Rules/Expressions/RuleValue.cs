using System;
using System.Globalization;

namespace Cadence.Rules.Expressions;

/// <summary>The kind of a <see cref="RuleValue"/>.</summary>
public enum RuleValueKind
{
    /// <summary>No value: a missing variable, a JSON null or an undefined result.</summary>
    Null,
    /// <summary>True or false.</summary>
    Boolean,
    /// <summary>A <see cref="decimal"/> number.</summary>
    Number,
    /// <summary>A string, compared ordinally.</summary>
    Text,
}

/// <summary>
/// A value a rule reads or computes: null, a boolean, a decimal number or text. Numbers are <see cref="decimal"/> so
/// evaluation never depends on floating-point rounding, and text never depends on the current culture (K-RULE-4).
/// </summary>
public readonly struct RuleValue : IEquatable<RuleValue>
{
    private readonly decimal _number;
    private readonly string? _text;
    private readonly bool _boolean;

    private RuleValue(RuleValueKind kind, decimal number, string? text, bool boolean)
    {
        Kind = kind;
        _number = number;
        _text = text;
        _boolean = boolean;
    }

    /// <summary>The null value.</summary>
    public static RuleValue Null => default;

    /// <summary>The boolean true.</summary>
    public static RuleValue True { get; } = new(RuleValueKind.Boolean, 0m, null, true);

    /// <summary>The boolean false.</summary>
    public static RuleValue False { get; } = new(RuleValueKind.Boolean, 0m, null, false);

    /// <summary>What kind of value this is.</summary>
    public RuleValueKind Kind { get; }

    /// <summary>True when this is <see cref="Null"/>.</summary>
    public bool IsNull => Kind == RuleValueKind.Null;

    /// <summary>A boolean value.</summary>
    public static RuleValue FromBoolean(bool value) => value ? True : False;

    /// <summary>A number value.</summary>
    public static RuleValue FromNumber(decimal value) => new(RuleValueKind.Number, value, null, false);

    /// <summary>Text, or <see cref="Null"/> when <paramref name="value"/> is null.</summary>
    public static RuleValue FromText(string? value) => value is null ? Null : new(RuleValueKind.Text, 0m, value, false);

    /// <summary>True when this is the boolean true; null, false and every other kind are not.</summary>
    public bool IsTrue => Kind == RuleValueKind.Boolean && _boolean;

    /// <summary>The boolean value, when this is a boolean.</summary>
    public bool TryGetBoolean(out bool value)
    {
        value = _boolean;
        return Kind == RuleValueKind.Boolean;
    }

    /// <summary>The text, when this is text.</summary>
    public bool TryGetText(out string value)
    {
        value = _text ?? string.Empty;
        return Kind == RuleValueKind.Text;
    }

    /// <summary>
    /// The numeric value: a number, or text that is an invariant-culture decimal (for example a character-typed
    /// result such as <c>"120"</c>). Null, booleans and other text have none.
    /// </summary>
    public bool TryGetNumber(out decimal value)
    {
        switch (Kind)
        {
            case RuleValueKind.Number:
                value = _number;
                return true;
            case RuleValueKind.Text:
                return decimal.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                value = 0m;
                return false;
        }
    }

    /// <summary>Same kind and same value: numbers by value (<c>1.0</c> equals <c>1</c>), text ordinally.</summary>
    public bool Equals(RuleValue other) => Kind == other.Kind && Kind switch
    {
        RuleValueKind.Null => true,
        RuleValueKind.Boolean => _boolean == other._boolean,
        RuleValueKind.Number => _number == other._number,
        _ => string.Equals(_text, other._text, StringComparison.Ordinal),
    };

    /// <summary>True when <paramref name="obj"/> is an equal <see cref="RuleValue"/>.</summary>
    public override bool Equals(object? obj) => obj is RuleValue other && Equals(other);

    /// <summary>A hash consistent with <see cref="Equals(RuleValue)"/>.</summary>
    public override int GetHashCode() => Kind switch
    {
        RuleValueKind.Null => 0,
        RuleValueKind.Boolean => _boolean ? 1 : 2,
        RuleValueKind.Number => _number.GetHashCode(),
        _ => StringComparer.Ordinal.GetHashCode(_text!),
    };

    /// <summary>Same as <see cref="Equals(RuleValue)"/>.</summary>
    public static bool operator ==(RuleValue left, RuleValue right) => left.Equals(right);

    /// <summary>The negation of <see cref="Equals(RuleValue)"/>.</summary>
    public static bool operator !=(RuleValue left, RuleValue right) => !left.Equals(right);

    /// <summary>The value as invariant text: <c>null</c>, <c>true</c>, <c>false</c>, a number, or the text itself.</summary>
    public override string ToString() => Kind switch
    {
        RuleValueKind.Null => "null",
        RuleValueKind.Boolean => _boolean ? "true" : "false",
        RuleValueKind.Number => _number.ToString(CultureInfo.InvariantCulture),
        _ => _text!,
    };
}
