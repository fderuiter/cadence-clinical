namespace Cadence.Kernel.Knowledge;

/// <summary>What a Concept stands for (spec §6.1).</summary>
public enum ConceptKind
{
    /// <summary>Something observed, measured or done to a participant, such as systolic blood pressure.</summary>
    BiomedicalConcept,

    /// <summary>A property of a biomedical concept, such as its result, unit, location or position.</summary>
    DataElement,

    /// <summary>A permissible value or qualifier, such as "sitting" or "left arm".</summary>
    Qualifier,

    /// <summary>A unit of measure.</summary>
    Unit,
}

/// <summary>Where a Concept's identity comes from (ADR-2252).</summary>
public enum ConceptOrigin
{
    /// <summary>A CDISC Biomedical Concept; its CDISC concept code is its identity in the tenant.</summary>
    CdiscBiomedicalConcept,

    /// <summary>A sponsor concept created in the tenant's namespace where no CDISC concept exists.</summary>
    Sponsor,
}

/// <summary>Lifecycle of a Concept. Deprecation keeps every version resolvable for the facts that reference it.</summary>
public enum ConceptStatus
{
    Active,
    Deprecated,
}

/// <summary>How a mapped code relates to the concept (spec §6.1).</summary>
public enum MappingRelation
{
    Exact,
    Broader,
    Narrower,
    Related,
}

/// <summary>Review state of a ConceptMapping. Only confirmed mappings resolve incoming codes to concepts.</summary>
public enum MappingStatus
{
    Proposed,
    Confirmed,
    Rejected,
}

/// <summary>
/// Whether a sponsor concept is anchored to external codes yet (ADR-2252). Derived from its mappings on read, never
/// stored, so it cannot drift from them.
/// </summary>
public enum ConceptMappingState
{
    /// <summary>No mapping at all.</summary>
    Unmapped,

    /// <summary>Only proposed mappings, none confirmed.</summary>
    Proposed,

    /// <summary>At least one confirmed mapping.</summary>
    Mapped,
}

/// <summary>Licence terms of a code system's content (K-SEM-2).</summary>
public enum LicenseClass
{
    /// <summary>Freely redistributable (CDISC CT, UCUM).</summary>
    Open,

    /// <summary>Free to use under terms the user accepts, such as LOINC's licence. Loaded per tenant, never shipped.</summary>
    AcceptedTerms,

    /// <summary>Content licensed per organization (MedDRA, WHODrug, SNOMED CT). Loaded per tenant under its licence.</summary>
    Licensed,
}
