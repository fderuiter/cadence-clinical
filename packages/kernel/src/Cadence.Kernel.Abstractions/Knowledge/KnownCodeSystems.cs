using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Knowledge;

/// <summary>
/// Canonical URIs of the code systems spec §6.1 names, and the licence class each one has. Registering one of these
/// URIs with a more permissive licence class is refused, so licensed content can never be flagged as shippable
/// (K-SEM-2). The URIs follow HL7 FHIR's canonical code-system URIs where FHIR defines one.
/// </summary>
public static partial class KnownCodeSystems
{
    /// <summary>NCI Thesaurus, which carries CDISC Controlled Terminology (C-codes).</summary>
    public const string NciThesaurus = "http://ncicb.nci.nih.gov/xml/owl/EVS/Thesaurus.owl";

    public const string Loinc = "http://loinc.org";

    public const string Ucum = "http://unitsofmeasure.org";

    public const string SnomedCt = "http://snomed.info/sct";

    public const string RxNorm = "http://www.nlm.nih.gov/research/umls/rxnorm";

    public const string Icd10 = "http://hl7.org/fhir/sid/icd-10";

    public const string Icd11 = "http://id.who.int/icd/release/11/mms";

    public const string MedDra = "http://terminology.hl7.org/CodeSystem/mdr";

    public const string WhoDrug = "https://www.who-umc.org/whodrug";

    /// <summary>
    /// The prefix of a CDISC Controlled Terminology package code system. Each package type the CDISC Library publishes
    /// (<c>sdtmct</c>, <c>adamct</c>, …) is its own code system, and its release date is the version, so a standard's CT
    /// binding <c>(sdtmct, 2024-09-27)</c> resolves to exactly one pinned code-system version. The C-codes inside a
    /// package are NCI Thesaurus codes (<see cref="NciThesaurus"/>).
    /// </summary>
    public const string CdiscCtPackagePrefix = "https://library.cdisc.org/api/mdr/ct/packages/";

    /// <summary>SDTM Controlled Terminology.</summary>
    public const string SdtmCt = CdiscCtPackagePrefix + "sdtmct";

    /// <summary>CDASH Controlled Terminology.</summary>
    public const string CdashCt = CdiscCtPackagePrefix + "cdashct";

    /// <summary>ADaM Controlled Terminology.</summary>
    public const string AdamCt = CdiscCtPackagePrefix + "adamct";

    /// <summary>SEND Controlled Terminology.</summary>
    public const string SendCt = CdiscCtPackagePrefix + "sendct";

    /// <summary>Protocol Controlled Terminology.</summary>
    public const string ProtocolCt = CdiscCtPackagePrefix + "protocolct";

    /// <summary>Define-XML Controlled Terminology.</summary>
    public const string DefineXmlCt = CdiscCtPackagePrefix + "define-xmlct";

    private static readonly FrozenDictionary<string, LicenseClass> Licences = new Dictionary<string, LicenseClass>(StringComparer.Ordinal)
    {
        [NciThesaurus] = LicenseClass.Open,
        [Ucum] = LicenseClass.Open,
        [Icd10] = LicenseClass.Open,
        [Loinc] = LicenseClass.AcceptedTerms,
        [RxNorm] = LicenseClass.AcceptedTerms,
        [Icd11] = LicenseClass.AcceptedTerms,
        [SnomedCt] = LicenseClass.Licensed,
        [MedDra] = LicenseClass.Licensed,
        [WhoDrug] = LicenseClass.Licensed,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The licence class a known code system requires, or null for a code system this list does not know.</summary>
    /// <remarks>Every CDISC CT package is <see cref="LicenseClass.Open"/>.</remarks>
    public static LicenseClass? RequiredLicence(string uri)
    {
        if (Licences.TryGetValue(uri, out var licence))
        {
            return licence;
        }

        return TryGetCdiscCtPackage(uri, out _) ? LicenseClass.Open : null;
    }

    /// <summary>
    /// The code-system URI of a CDISC CT package type, as the CDISC Library names it in
    /// <c>/mdr/ct/packages/{type}-{date}</c> (for example <c>sdtmct</c> or <c>define-xmlct</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The name is not a CDISC CT package type (lower case, ending in <c>ct</c>).</exception>
    public static string CdiscCt(string package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return PackageType().IsMatch(package)
            ? CdiscCtPackagePrefix + package
            : throw new ArgumentException($"'{package}' is not a CDISC CT package type such as 'sdtmct'.", nameof(package));
    }

    /// <summary>Whether a URI names a CDISC CT package code system, and which package type.</summary>
    public static bool TryGetCdiscCtPackage(string uri, [NotNullWhen(true)] out string? package)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.StartsWith(CdiscCtPackagePrefix, StringComparison.Ordinal)
            && uri[CdiscCtPackagePrefix.Length..] is var type
            && PackageType().IsMatch(type))
        {
            package = type;
            return true;
        }

        package = null;
        return false;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*ct$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PackageType();
}
