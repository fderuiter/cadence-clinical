using System.Linq;
using System.Text;

using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.TestSupport.Kernel.Protocol;

namespace Cadence.TestSupport.Kernel.Standards;

/// <summary>
/// Design-time conformance samples (ADR-2464): a controlled-terminology file holding the codelists the SDTMIG 3.4 VS
/// variables of <see cref="SampleMappingSpecifications"/> name, and a vital signs design that the samples map
/// conformantly. The rows are an excerpt in the NCI EVS layout; the term codes of the unit and test code rows are
/// illustrative stand-ins, not CDISC content.
/// </summary>
internal static class SampleDesignConformance
{
    private static readonly string[][] Rows =
    [
        ["Code", "Codelist Code", "Codelist Extensible (Yes/No)", "Codelist Name", "CDISC Submission Value", "CDISC Synonym(s)", "CDISC Definition", "NCI Preferred Term"],
        ["C66741", "", "Yes", "Vital Signs Test Code", "VSTESTCD", "Vital Signs Test Code", "Vital signs test codes.", "CDISC SDTM Vital Sign Test Code Terminology"],
        ["C25298", "C66741", "", "Vital Signs Test Code", "SYSBP", "Systolic Blood Pressure", "Systolic blood pressure.", "Systolic Blood Pressure"],
        ["C67153", "", "Yes", "Vital Signs Test Name", "VSTEST", "Vital Signs Test Name", "Vital signs test names.", "CDISC SDTM Vital Sign Test Name Terminology"],
        ["C25298", "C67153", "", "Vital Signs Test Name", "Systolic Blood Pressure", "Systolic Blood Pressure", "Systolic blood pressure.", "Systolic Blood Pressure"],
        ["C66770", "", "Yes", "Units for Vital Signs Results", "VSRESU", "Units for Vital Signs Results", "Units for vital signs results.", "CDISC SDTM Unit for Vital Sign Result Terminology"],
        ["C49670", "C66770", "", "Units for Vital Signs Results", "mmHg", "Millimeter of Mercury", "A unit of pressure.", "Millimeter of Mercury"],
        ["C67285", "C66770", "", "Units for Vital Signs Results", "kPa", "Kilopascal", "A unit of pressure.", "Kilopascal"],
        ["C71148", "", "Yes", "Position", "POSITION", "Position", "Body positions.", "CDISC SDTM Position Terminology"],
        ["C62122", "C71148", "", "Position", "SITTING", "Sitting", "Sitting.", "Sitting"],
        ["C62166", "C71148", "", "Position", "STANDING", "Standing", "Standing.", "Standing"],
        ["C62167", "C71148", "", "Position", "SUPINE", "Supine", "Supine.", "Supine Position"],
        ["C78431", "", "Yes", "Vital Signs Position of Subject", "VSPOS", "Vital Signs Position of Subject", "A subset of Position for vital signs.", "CDISC CDASH Vital Signs Position of Subject Terminology"],
        ["C62122", "C78431", "", "Vital Signs Position of Subject", "SITTING", "Sitting", "Sitting.", "Sitting"],
        ["C62166", "C78431", "", "Vital Signs Position of Subject", "STANDING", "Standing", "Standing.", "Standing"],
        ["C62167", "C78431", "", "Vital Signs Position of Subject", "SUPINE", "Supine", "Supine.", "Supine Position"],
    ];

    /// <summary>The terminology file, as SDTM CT 2024-09-27 (the version <see cref="SampleMappingSpecifications.SdtmCt"/> names).</summary>
    public static byte[] ControlledTerminology { get; } =
        Encoding.UTF8.GetBytes(string.Join("\n", Rows.Select(r => string.Join('\t', r))) + "\n");

    /// <summary>
    /// A design collecting systolic blood pressure in <paramref name="unit"/> for an endpoint, and the position of the
    /// subject as a CT-coded item for an operational need, both at one scheduled visit of
    /// <see cref="ProtocolDesignSamples.Complete"/>. Pass
    /// <paramref name="withPurpose"/> false to leave the blood pressure item with no path to a need (K-PROT-3).
    /// </summary>
    public static ProtocolDesign Vitals(ConceptRef systolic, ConceptRef position, CodeSystemVersionId terminology, string? unit = "mm[Hg]", bool withPurpose = true) =>
        ProtocolDesignSamples.Complete(systolic, terminology) with
        {
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital signs",
                    [
                        new CollectionItem("item.sbp", systolic, unit, Required: true, Purpose: withPurpose ? new DataPurpose { EndpointIds = ["ep.1"] } : null),
                        new CollectionItem("item.position", position, Terminology: new TerminologyBinding(terminology, "C78431"), Purpose: new DataPurpose { OperationalNeed = "Measurement conditions" }),
                    ]),
            ],
        };
}
