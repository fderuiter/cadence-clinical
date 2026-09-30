using System;
using System.Collections.Generic;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;

namespace Cadence.TestSupport.Kernel.Standards;

/// <summary>
/// Mapping specifications into SDTMIG 3.4 for tests (ADR-2463): systolic blood pressure to VS and glucose to LB with a
/// conventional-to-SI conversion. Units are written as their CDISC CT UNIT submission values. The glucose factor
/// (1 mg/dL = 0.0555 mmol/L) is the sponsor's stated conversion, which is data in the specification, not a rule of the
/// engine.
/// </summary>
internal static class SampleMappingSpecifications
{
    /// <summary>The SDTM CT version the samples name.</summary>
    public static readonly CodeSystemVersionRef SdtmCt = new("sdtmct", "2024-09-27");

    /// <summary>Systolic blood pressure, collected in mm[Hg], to VS with VSTESTCD SYSBP.</summary>
    public static VariableMapping SystolicBloodPressure(ConceptRef concept) => new(
        "vs-sysbp",
        concept,
        "VS",
        new SortedDictionary<string, string>(StringComparer.Ordinal) { ["VSTEST"] = "Systolic Blood Pressure", ["VSTESTCD"] = "SYSBP" },
        new ResultVariables("VSORRES", "VSORRESU", "VSSTRESC", "VSSTRESN", "VSSTRESU"),
        "mm[Hg]",
        [new UnitConversion("kPa", "mm[Hg]", 7.50062m)],
        0,
        null);

    /// <summary>Glucose, collected in mg/dL or mmol/L, to LB with LBTESTCD GLUC, standardised to mmol/L.</summary>
    public static VariableMapping Glucose(ConceptRef concept) => new(
        "lb-gluc",
        concept,
        "LB",
        new SortedDictionary<string, string>(StringComparer.Ordinal) { ["LBTEST"] = "Glucose", ["LBTESTCD"] = "GLUC" },
        new ResultVariables("LBORRES", "LBORRESU", "LBSTRESC", "LBSTRESN", "LBSTRESU"),
        "mmol/L",
        [new UnitConversion("mg/dL", "mmol/L", 0.0555m)],
        2,
        null);

    /// <summary>A coded result decoded against the Vital Signs Position of Subject codelist (C78431) into VSPOS.</summary>
    public static VariableMapping Position(ConceptRef concept) => new(
        "vs-position",
        concept,
        "VS",
        new SortedDictionary<string, string>(StringComparer.Ordinal),
        new ResultVariables("VSPOS"),
        null,
        [],
        null,
        "C78431");

    /// <summary>The unit terms the samples use.</summary>
    public static IReadOnlyList<UnitTerm> Units { get; } =
    [
        new("kPa", "kPa"),
        new("mg/dL", "mg/dL"),
        new("mm[Hg]", "mmHg"),
        new("mmol/L", "mmol/L"),
    ];

    /// <summary>A base specification holding <paramref name="mappings"/>.</summary>
    public static MappingSpecification Specification(params VariableMapping[] mappings) =>
        new("Sample SDTMIG 3.4 mappings", null, [SdtmCt], Units, mappings);
}
