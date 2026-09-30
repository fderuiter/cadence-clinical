using System.Collections.Generic;

using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Standards.Mapping;

/// <summary>
/// Applies a mapping specification to one collected value (spec §6.3, ADR-2463). The engine sits behind this port so it
/// can be replaced, for example by a native engine (spec §13); <c>Cadence.Standards</c> provides the .NET one
/// (<c>AddCadenceMapping</c>). An engine is a pure function: the same specification, input and decodes give the same
/// record fragment, which is what makes a projection reproducible (K-LIN-3).
/// </summary>
public interface IMappingEngine
{
    /// <summary>
    /// Maps <paramref name="input"/> with the mapping <paramref name="specification"/> holds for its Concept.
    /// <paramref name="decoder"/> supplies CT submission values for coded results; it may be null when no mapping names
    /// a codelist.
    /// </summary>
    MappingResult Map(MappingSpecification specification, MappingInput input, ICodelistDecoder? decoder = null);
}

/// <summary>One collected value: the Concept it records, the value as invariant text, and its UCUM unit when it is a quantity.</summary>
/// <param name="Concept">What the value means (the CollectionSpec item's concept).</param>
/// <param name="Value">The value: a number in invariant form, text, or for a coded result the code; null when not collected.</param>
/// <param name="Unit">The UCUM unit the value was collected in, or null.</param>
public sealed record MappingInput(ConceptRef Concept, string? Value, string? Unit = null);

/// <summary>
/// What one mapping produced: the dataset and the variables of the record fragment, or the problems that stopped it.
/// A fragment is written only when there are no problems.
/// </summary>
public sealed record MappingResult(string? MappingId, string? Dataset, IReadOnlyList<MappedValue> Values, IReadOnlyList<string> Problems)
{
    /// <summary>Whether the value mapped without problems.</summary>
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>One variable of a mapped record fragment; <see cref="Value"/> is null when the record has no value for it.</summary>
public sealed record MappedValue(string Variable, string? Value);

/// <summary>Looks up controlled-terminology submission values for coded results.</summary>
public interface ICodelistDecoder
{
    /// <summary>The submission value of <paramref name="code"/> in <paramref name="codelist"/>, or null when the codelist does not hold it.</summary>
    string? SubmissionValue(string codelist, string code);
}
