using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Cadence.Kernel.Knowledge;

/// <summary>
/// Canonical URIs of the code systems spec §6.1 names, and the licence class each one has. Registering one of these
/// URIs with a more permissive licence class is refused, so licensed content can never be flagged as shippable
/// (K-SEM-2). The URIs follow HL7 FHIR's canonical code-system URIs where FHIR defines one.
/// </summary>
public static class KnownCodeSystems
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
    public static LicenseClass? RequiredLicence(string uri) =>
        Licences.TryGetValue(uri, out var licence) ? licence : null;
}
