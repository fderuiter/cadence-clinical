using System;
using System.Collections.Generic;

using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Standards;

/// <summary>
/// One version of a data standard as data: its datasets, variables, value-level definitions and the controlled
/// terminology versions it was published against (spec §6.3). This is the only place standard dataset and variable
/// names exist in the platform: no kernel type is named after them (K-PROJ-1).
/// </summary>
/// <param name="StandardCode">The standard's short code, for example <c>SDTMIG</c>, <c>ADaMIG</c> or <c>CDASHIG</c>.</param>
/// <param name="Version">The published version, for example <c>3.4</c>.</param>
/// <param name="Name">The publisher's name for the standard version.</param>
/// <param name="Publisher">Who publishes it, for example <c>CDISC</c> (K-RULE-2: public standards only).</param>
/// <param name="Source">Where the content came from, for example the CDISC Library path <c>/mdr/sdtmig/3-4</c>.</param>
/// <param name="EffectiveDate">The publisher's effective date, when it states one.</param>
/// <param name="Datasets">Dataset definitions, each with its variables.</param>
/// <param name="ValueLevelDefinitions">Value-level definitions (variable metadata that applies under a where clause).</param>
/// <param name="ControlledTerminology">Controlled-terminology versions the standard version references.</param>
public sealed record StandardPackage(
    string StandardCode,
    string Version,
    string Name,
    string Publisher,
    string? Source,
    DateOnly? EffectiveDate,
    IReadOnlyList<DatasetDefinitionData> Datasets,
    IReadOnlyList<ValueLevelDefinitionData> ValueLevelDefinitions,
    IReadOnlyList<CodeSystemVersionRef> ControlledTerminology);

/// <summary>A dataset (SDTM domain, ADaM data structure, CDASH domain, …) of a standard version.</summary>
/// <param name="Name">The dataset name as the standard publishes it.</param>
/// <param name="Label">The dataset label.</param>
/// <param name="Class">The observation class or data-structure class, when the standard has one.</param>
/// <param name="Structure">The standard's structure statement, for example "One record per subject".</param>
/// <param name="Ordinal">Order within the standard.</param>
/// <param name="Variables">The dataset's variables.</param>
public sealed record DatasetDefinitionData(
    string Name,
    string Label,
    string? Class,
    string? Structure,
    int Ordinal,
    IReadOnlyList<VariableDefinitionData> Variables);

/// <summary>A variable of a dataset in a standard version.</summary>
/// <param name="Name">The variable name as the standard publishes it.</param>
/// <param name="Label">The variable label.</param>
/// <param name="Ordinal">Order within the dataset.</param>
/// <param name="DataType">The standard's simple data type.</param>
/// <param name="Role">The variable's role (Identifier, Topic, Result Qualifier, …), when the standard assigns one.</param>
/// <param name="Core">Whether the variable is required, expected, permissible or conditional.</param>
/// <param name="Codelists">Controlled-terminology codelist codes (NCI C-codes) the variable's values come from.</param>
/// <param name="ValueList">Values the standard lists directly (for example the dataset code for a domain variable).</param>
/// <param name="DescribedValueDomain">A described value domain such as ISO 8601 datetime, when the standard names one.</param>
/// <param name="Description">The standard's notes on the variable.</param>
public sealed record VariableDefinitionData(
    string Name,
    string Label,
    int Ordinal,
    StandardDataType DataType,
    string? Role,
    VariableCore Core,
    IReadOnlyList<string> Codelists,
    IReadOnlyList<string> ValueList,
    string? DescribedValueDomain,
    string? Description);

/// <summary>
/// Value-level metadata: the definition a variable takes where a condition on other variables of the same record holds
/// (for example the permitted original units where the test code is a given test).
/// </summary>
/// <param name="Dataset">The dataset.</param>
/// <param name="Variable">The variable the definition applies to.</param>
/// <param name="Where">Conditions, all of which must hold.</param>
/// <param name="DataType">The data type under the condition.</param>
/// <param name="Length">Maximum length under the condition, when constrained.</param>
/// <param name="Codelists">Codelist codes under the condition.</param>
/// <param name="ValueList">Permitted values under the condition.</param>
/// <param name="Description">What the definition means.</param>
public sealed record ValueLevelDefinitionData(
    string Dataset,
    string Variable,
    IReadOnlyList<WhereCondition> Where,
    StandardDataType DataType,
    int? Length,
    IReadOnlyList<string> Codelists,
    IReadOnlyList<string> ValueList,
    string? Description);

/// <summary>One condition of a value-level where clause: <see cref="Variable"/> equals one of <see cref="Values"/>.</summary>
public sealed record WhereCondition(string Variable, IReadOnlyList<string> Values);

/// <summary>
/// A version of a code system, by natural key: <see cref="CodeSystem"/> is the code system's short name (for CDISC
/// controlled terminology, the CDISC Library package type such as <c>sdtmct</c>) and <see cref="Version"/> its release
/// (for CDISC CT, the package date such as <c>2024-09-27</c>). A code system that is not CDISC CT is named by its URI,
/// such as <c>http://terminology.hl7.org/CodeSystem/mdr</c>. Nothing references "the latest" release (spec §6.1). The
/// kernel resolves each reference to the tenant's imported Concept Registry version (ADR-2460 §4.2,
/// <see cref="ResolvedCodeSystemVersion"/>).
/// </summary>
public sealed record CodeSystemVersionRef(string CodeSystem, string Version)
{
    /// <summary>The Concept Registry code-system URI this reference names, or null when it names none (<see cref="KnownCodeSystems.UriFor"/>).</summary>
    public string? ToCodeSystemUri() => KnownCodeSystems.UriFor(CodeSystem);

    public override string ToString() => $"{CodeSystem}-{Version}";
}

/// <summary>A terminology reference together with the Concept Registry code-system version it resolved to in the tenant.</summary>
public sealed record ResolvedCodeSystemVersion(CodeSystemVersionRef Reference, CodeSystemVersionId CodeSystemVersionId);

/// <summary>The simple data types standards publish for variables.</summary>
public enum StandardDataType
{
    /// <summary>Character (text) values.</summary>
    Char,

    /// <summary>Numeric values.</summary>
    Num,
}

/// <summary>The core designation of a standard variable.</summary>
public enum VariableCore
{
    /// <summary>Required: must be present and populated.</summary>
    Required,

    /// <summary>Expected: must be present; may be null.</summary>
    Expected,

    /// <summary>Permissible: included when collected or derived.</summary>
    Permissible,

    /// <summary>Conditional (ADaM): required when a stated condition holds.</summary>
    Conditional,
}
