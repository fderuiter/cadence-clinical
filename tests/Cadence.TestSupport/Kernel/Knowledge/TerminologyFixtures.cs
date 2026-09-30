using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.TestSupport.Kernel.Knowledge;

/// <summary>
/// Small terminology files in their publishers' formats. The CDISC CT rows are an excerpt of CDASH CT 2024-09-27
/// (<c>docs/CDISC/Library/Terminology</c>), which CDISC publishes openly. The LOINC and UCUM rows are hand-written
/// stand-ins in the published layouts, not LOINC or UCUM content.
/// </summary>
internal static class TerminologyFixtures
{
    private static readonly string[][] CdiscCtRows =
    [
        ["Code", "Codelist Code", "Codelist Extensible (Yes/No)", "Codelist Name", "CDISC Submission Value", "CDISC Synonym(s)", "CDISC Definition", "NCI Preferred Term"],
        ["C83004", "", "Yes", "Substance Use Never/Current/Former Classification", "SUNCF", "Substance Use Never/Current/Former Classification", "A terminology subset of the CDISC SDTM Never/Current/Former Classification codelist created for CDASH Substance Use Never/Current/Former Classification codelist. (NCI)", "CDISC CDASH Substance Use Never/Current/Former Classification Terminology"],
        ["C25471", "C83004", "", "Substance Use Never/Current/Former Classification", "CURRENT", "", "Occurring in or belonging to the present time. (NCI)", "Current"],
        ["C25627", "C83004", "", "Substance Use Never/Current/Former Classification", "FORMER", "Previous", "Occurring prior to something else. (NCI)", "Previous"],
        ["C70543", "C83004", "", "Substance Use Never/Current/Former Classification", "NEVER", "", "Not ever; at no time in the past.", "Never"],
        ["C78431", "", "Yes", "Vital Signs Position of Subject", "VSPOS", "Vital Signs Position of Subject", "A terminology subset of the CDISC SDTM Position codelist created for CDASH Vital Signs Position of Subject codelist. (NCI)", "CDISC CDASH Vital Signs Position of Subject Terminology"],
        ["C62122", "C78431", "", "Vital Signs Position of Subject", "SITTING", "Sitting", "The state or act of one who sits; the posture of one who occupies a seat. (NCI)", "Sitting"],
        ["C62166", "C78431", "", "Vital Signs Position of Subject", "STANDING", "Orthostatic; Standing", "The act of assuming or maintaining an erect upright position. (NCI)", "Standing"],
        ["C62167", "C78431", "", "Vital Signs Position of Subject", "SUPINE", "Supine", "A posterior recumbent body position whereby the person lies on its back and faces upward. (NCI)", "Supine Position"],
    ];

    /// <summary>An NCI EVS tab-delimited CDISC CT file: two codelists, six terms.</summary>
    public static byte[] CdiscCt { get; } =
        Encoding.UTF8.GetBytes(string.Join("\n", CdiscCtRows.Select(r => string.Join('\t', r))) + "\n");

    /// <summary>A table in the Loinc.csv layout, with a quoted comma in a name. Codes are illustrative.</summary>
    public static byte[] LoincTable { get; } = Encoding.UTF8.GetBytes(
        "\"LOINC_NUM\",\"COMPONENT\",\"SHORTNAME\",\"CLASS\",\"LONG_COMMON_NAME\",\"STATUS\"\r\n"
        + "\"8480-6\",\"Intravascular systolic\",\"BP sys\",\"BP.ATOM\",\"Systolic blood pressure\",\"ACTIVE\"\r\n"
        + "\"8462-4\",\"Intravascular diastolic\",\"BP dias\",\"BP.ATOM\",\"Diastolic blood pressure\",\"ACTIVE\"\r\n"
        + "\"2823-3\",\"Potassium\",\"Potassium SerPl-sCnc\",\"CHEM\",\"Potassium [Moles/volume] in Serum or Plasma, \"\"measured\"\"\",\"ACTIVE\"\r\n");

    /// <summary>A file in the ucum-essence.xml layout with one prefix, one base unit and one defined unit.</summary>
    public static byte[] UcumEssence { get; } = Encoding.UTF8.GetBytes("""
        <?xml version="1.0" encoding="UTF-8"?>
        <root xmlns="" version="2.1">
          <prefix Code="m" CODE="M"><name>milli</name><printSymbol>m</printSymbol><value value="1e-3">1 &#215; 10<sup>-3</sup></value></prefix>
          <base-unit Code="g" CODE="G" dim="M"><name>gram</name><printSymbol>g</printSymbol><property>mass</property></base-unit>
          <unit Code="mm[Hg]" CODE="MM[HG]" isMetric="yes" class="clinical"><name>meter of mercury column</name><printSymbol>mm Hg</printSymbol><property>pressure</property><value Unit="kPa" UNIT="KPAL" value="133.3220">133.3220</value></unit>
        </root>
        """);

    public static string Sha256(byte[] content) => System.Convert.ToHexStringLower(SHA256.HashData(content));
}
