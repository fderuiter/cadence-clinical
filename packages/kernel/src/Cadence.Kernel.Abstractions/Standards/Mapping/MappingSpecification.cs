using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Standards.Mapping;

/// <summary>
/// A mapping specification (spec §6.3, ADR-2463): how the Concepts a study collects become variables of one standard
/// version's datasets. It is the content of a <see cref="LibraryItemKind.MappingSpecification"/> library item, so it is
/// versioned and governed like every Metadata Registry item, and it is pinned to exactly one standard version (the
/// item's <c>StandardVersions</c>) and to the controlled-terminology versions it names. It never names "the latest"
/// of anything. The JSON form is read and written by <see cref="MappingSpecificationJson"/>.
/// </summary>
/// <param name="Title">What the specification maps, for people.</param>
/// <param name="Overrides">
/// The key of the mapping specification this one overrides, or null for a base specification. A study that pins an
/// override also pins its base; the override's mappings replace the base's mappings for the same Concepts.
/// </param>
/// <param name="ControlledTerminology">The CT versions the decodes and unit terms come from.</param>
/// <param name="Units">How each UCUM unit is written in the standard (its CT submission value).</param>
/// <param name="Mappings">One mapping per Concept.</param>
public sealed record MappingSpecification(
    string Title,
    string? Overrides,
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology,
    IReadOnlyList<UnitTerm> Units,
    IReadOnlyList<VariableMapping> Mappings)
{
    /// <summary>The mapping for a Concept, whatever version of it was collected, or null.</summary>
    public VariableMapping? For(ConceptId concept) => Mappings.FirstOrDefault(m => m.Concept.ConceptId == concept);

    /// <summary>The standard's term for a UCUM unit, or null when the specification does not list the unit.</summary>
    public string? SubmissionValueOf(string ucum)
    {
        ArgumentNullException.ThrowIfNull(ucum);
        return Units.FirstOrDefault(u => string.Equals(u.Ucum, ucum, StringComparison.Ordinal))?.SubmissionValue;
    }
}

/// <summary>A UCUM unit and the controlled-terminology submission value a standard writes for it (<c>mm[Hg]</c> → <c>mmHg</c>).</summary>
public sealed record UnitTerm(string Ucum, string SubmissionValue);

/// <summary>
/// How one Concept maps to one dataset: the constant variables that identify the record (value-level metadata such as
/// <c>VSTESTCD = SYSBP</c>), the variables the result goes to, the standard unit and the conversions into it, and
/// the codelist that decodes a coded result.
/// </summary>
/// <param name="Id">The mapping's id within the specification.</param>
/// <param name="Concept">The Concept, at the version the mapping was authored against. Any version of it matches.</param>
/// <param name="Dataset">The dataset (SDTM domain, …) the record belongs to.</param>
/// <param name="Assign">Constant values, by variable, sorted by variable name.</param>
/// <param name="Result">Where the collected result and its standardised form go.</param>
/// <param name="StandardUnit">The UCUM unit standardised results are expressed in, when the result is a quantity.</param>
/// <param name="Conversions">Conversions from other UCUM units into <see cref="StandardUnit"/>.</param>
/// <param name="Decimals">Decimal places of the standardised numeric result, or null to write the converted value exactly, without trailing zeros.</param>
/// <param name="Codelist">The CT codelist (NCI C-code) a coded result is decoded against, or null.</param>
public sealed record VariableMapping(
    string Id,
    ConceptRef Concept,
    string Dataset,
    IReadOnlyDictionary<string, string> Assign,
    ResultVariables Result,
    string? StandardUnit,
    IReadOnlyList<UnitConversion> Conversions,
    int? Decimals,
    string? Codelist)
{
    /// <summary>Every variable the mapping writes, in the order it writes them.</summary>
    public IEnumerable<string> Variables() =>
        Assign.Keys.Concat(new[] { Result.Original, Result.OriginalUnit, Result.StandardCharacter, Result.StandardNumeric, Result.StandardUnit }.OfType<string>());
}

/// <summary>
/// The variables a result is written to: the original result and unit as collected (<c>--ORRES</c>, <c>--ORRESU</c>)
/// and the standardised result in character and numeric form with its unit (<c>--STRESC</c>, <c>--STRESN</c>,
/// <c>--STRESU</c>). Only <see cref="Original"/> is required.
/// </summary>
public sealed record ResultVariables(
    string Original,
    string? OriginalUnit = null,
    string? StandardCharacter = null,
    string? StandardNumeric = null,
    string? StandardUnit = null);

/// <summary>A linear conversion between UCUM units: <c>standard = value × Factor + Offset</c>.</summary>
public sealed record UnitConversion(string From, string To, decimal Factor, decimal Offset = 0m);
