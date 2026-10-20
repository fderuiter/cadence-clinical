using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Standards.Mapping;

namespace Cadence.Standards.Mapping;

/// <summary>
/// The .NET <see cref="IMappingEngine"/> (ADR-2463). For the mapping of the input's Concept it writes the constant
/// variables, the original result and unit as collected, and, for a quantity, the result converted into the standard
/// unit. A coded result is written as its CT submission value, and a value that was not obtained as its status
/// (<c>--STAT = NOT DONE</c>) and reason (<c>--REASND</c>). Every unit is written as the specification's term for
/// it. The engine never guesses: a missing mapping, unit term, conversion or decode, a quantity that is neither a number
/// nor a bound such as &lt;1.0, or a conversion out of range is a problem, and a result with a problem produces no record
/// fragment.
/// </summary>
public sealed class MappingEngine : IMappingEngine
{
    private const string NotDone = "NOT DONE";

    private const NumberStyles NumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <inheritdoc />
    public MappingResult Map(MappingSpecification specification, MappingInput input, ICodelistDecoder? decoder = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(input);

        if (specification.For(input.Concept.ConceptId) is not { } mapping)
        {
            return new MappingResult(null, null, [], [$"The specification has no mapping for concept {input.Concept.ConceptId}."]);
        }

        var problems = new List<string>();
        var values = mapping.Assign.Select(a => new MappedValue(a.Key, a.Value)).ToList();
        var result = mapping.Result;

        if (input.Missing is { } missing)
        {
            MapNotDone(mapping, missing, values, problems);
        }
        else if (mapping.Codelist is { } codelist)
        {
            var decoded = Decode(codelist, input.Value, decoder, problems);
            values.Add(new MappedValue(result.Original, decoded));
            Add(values, result.StandardCharacter, decoded);
            if (input.Unit is not null)
            {
                problems.Add($"Mapping \"{mapping.Id}\" decodes a coded result, which has no unit; the value was collected in {input.Unit}.");
            }
        }
        else
        {
            MapResult(specification, mapping, input, values, problems);
        }

        if (input.Missing is null)
        {
            Add(values, result.Status, null);
            Add(values, result.ReasonNotDone, null);
        }

        return problems.Count == 0
            ? new MappingResult(mapping.Id, mapping.Dataset, values, [])
            : new MappingResult(mapping.Id, mapping.Dataset, [], problems);
    }

    private static void MapResult(MappingSpecification specification, VariableMapping mapping, MappingInput input, List<MappedValue> values, List<string> problems)
    {
        var result = mapping.Result;
        values.Add(new MappedValue(result.Original, input.Value));

        if (input.Unit is not null && result.OriginalUnit is null && result.StandardUnit is null)
        {
            problems.Add($"Mapping \"{mapping.Id}\" writes no unit, but the value was collected in {input.Unit}.");
        }

        if (result.OriginalUnit is not null)
        {
            values.Add(new MappedValue(result.OriginalUnit, input.Unit is null ? null : UnitTerm(specification, mapping, input.Unit, problems)));
        }

        var number = input.Value is not null && decimal.TryParse(input.Value, NumberStyle, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null;
        if (number is null && input.Value is not null && input.Unit is not null && mapping.StandardUnit is not null && !IsBound(input.Value))
        {
            // A quantity collected with a unit must be a number, or a bound such as <1.0; anything else would be a guess.
            problems.Add($"Mapping \"{mapping.Id}\" standardises into {mapping.StandardUnit}, but \"{input.Value}\" collected in {input.Unit} is neither a number nor a bound such as <1.0.");
            return;
        }

        if (number is null || mapping.StandardUnit is null)
        {
            // Text, or a number the mapping does not standardise: the standard character result is the result as collected.
            Add(values, result.StandardCharacter, input.Value);
            Add(values, result.StandardNumeric, null);
            Add(values, result.StandardUnit, null);
            return;
        }

        if (input.Unit is null)
        {
            problems.Add($"Mapping \"{mapping.Id}\" standardises into {mapping.StandardUnit}, but the value has no unit.");
            return;
        }

        if (!mapping.Conversions.Any(c => string.Equals(c.From, input.Unit, StringComparison.Ordinal))
            && !string.Equals(input.Unit, mapping.StandardUnit, StringComparison.Ordinal))
        {
            problems.Add($"Mapping \"{mapping.Id}\" has no conversion from {input.Unit} to {mapping.StandardUnit}.");
            return;
        }

        if (Convert(mapping, number.Value, input.Unit) is not { } standard)
        {
            problems.Add($"Mapping \"{mapping.Id}\": {input.Value} {input.Unit} is out of range when converted to {mapping.StandardUnit}.");
            return;
        }

        var text = mapping.Decimals is { } decimals
            ? Math.Round(standard, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : standard.ToString("0.############################", CultureInfo.InvariantCulture);
        Add(values, result.StandardCharacter, text);
        Add(values, result.StandardNumeric, text);
        if (result.StandardUnit is not null)
        {
            values.Add(new MappedValue(result.StandardUnit, UnitTerm(specification, mapping, mapping.StandardUnit, problems)));
        }
    }

    // A value that was not obtained (K-FACT-3) has no result: its status says NOT DONE and its reason goes to --REASND,
    // the detail when there is one, otherwise the null flavor when it says more than "not done".
    private static void MapNotDone(VariableMapping mapping, MissingValue missing, List<MappedValue> values, List<string> problems)
    {
        var result = mapping.Result;
        if (result.Status is null)
        {
            problems.Add($"Mapping \"{mapping.Id}\" names no status variable (--STAT), so it cannot write a value that was not obtained ({missing.Reason}).");
            return;
        }

        values.Add(new MappedValue(result.Original, null));
        Add(values, result.OriginalUnit, null);
        Add(values, result.StandardCharacter, null);
        Add(values, result.StandardNumeric, null);
        Add(values, result.StandardUnit, null);
        values.Add(new MappedValue(result.Status, NotDone));
        Add(values, result.ReasonNotDone, missing.Detail ?? (missing.Reason == NullFlavor.NotDone ? null : ReasonText(missing.Reason)));
    }

    private static string ReasonText(NullFlavor reason) => reason switch
    {
        NullFlavor.Unknown => "UNKNOWN",
        NullFlavor.AskedButUnknown => "ASKED BUT UNKNOWN",
        NullFlavor.NotAsked => "NOT ASKED",
        NullFlavor.NotApplicable => "NOT APPLICABLE",
        NullFlavor.Masked => "MASKED",
        _ => reason.ToString().ToUpperInvariant(),
    };

    private static decimal? Convert(VariableMapping mapping, decimal value, string unit)
    {
        if (string.Equals(unit, mapping.StandardUnit, StringComparison.Ordinal))
        {
            return value;
        }

        var conversion = mapping.Conversions.First(c => string.Equals(c.From, unit, StringComparison.Ordinal));
        try
        {
            return (value * conversion.Factor) + conversion.Offset;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    // A bound is a comparator followed by a number, such as <1.0 or >=200.
    private static bool IsBound(string value)
    {
        var comparator = new[] { "<=", ">=", "<", ">" }.FirstOrDefault(c => value.StartsWith(c, StringComparison.Ordinal));
        return comparator is not null
            && decimal.TryParse(value.AsSpan(comparator.Length), NumberStyle, CultureInfo.InvariantCulture, out _);
    }

    private static string? UnitTerm(MappingSpecification specification, VariableMapping mapping, string ucum, List<string> problems)
    {
        var term = specification.SubmissionValueOf(ucum);
        if (term is null)
        {
            problems.Add($"Mapping \"{mapping.Id}\": the specification has no standard term for the unit {ucum}.");
        }

        return term;
    }

    private static string? Decode(string codelist, string? code, ICodelistDecoder? decoder, List<string> problems)
    {
        if (code is null)
        {
            return null;
        }

        if (decoder is null)
        {
            problems.Add($"The result is coded against codelist {codelist}, and no decoder was given.");
            return null;
        }

        var submissionValue = decoder.SubmissionValue(codelist, code);
        if (submissionValue is null)
        {
            problems.Add($"Codelist {codelist} has no term {code}.");
        }

        return submissionValue;
    }

    private static void Add(List<MappedValue> values, string? variable, string? value)
    {
        if (variable is not null)
        {
            values.Add(new MappedValue(variable, value));
        }
    }
}
