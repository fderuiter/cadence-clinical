"""Pre-compiled FHIR Patient to CDASH Demographics (DM) ConceptMaps.

Requirements: PRD-CRF-007
"""

from apps.interop.domain.semantic_mapping_models import CDISCDomain, ConceptMapElement

DEMOGRAPHICS_CONCEPT_MAP: list[ConceptMapElement] = [
    ConceptMapElement(
        source_system="http://hl7.org/fhir/administrative-gender",
        source_code="gender",
        source_display="Administrative Gender",
        target_domain=CDISCDomain.DM,
        target_variable="eCRF.DM.SEX",
        cdash_testcd="SEX",
        cdash_test="Sex",
        category="demographics",
        description="Patient administrative or biological sex (M, F, U)",
    ),
    ConceptMapElement(
        source_system="http://hl7.org/fhir/patient-birthDate",
        source_code="birthDate",
        source_display="Birth Date",
        target_domain=CDISCDomain.DM,
        target_variable="eCRF.DM.BRTHDTC",
        cdash_testcd="BRTHDTC",
        cdash_test="Date of Birth",
        category="demographics",
        description="Date of birth in ISO 8601 YYYY-MM-DD format",
    ),
    ConceptMapElement(
        source_system="http://hl7.org/fhir/us/core/StructureDefinition/us-core-race",
        source_code="race",
        source_display="OMB Race Category",
        target_domain=CDISCDomain.DM,
        target_variable="eCRF.DM.RACE",
        cdash_testcd="RACE",
        cdash_test="Race",
        category="demographics",
        description="US OMB standard race classification",
    ),
    ConceptMapElement(
        source_system="http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity",
        source_code="ethnicity",
        source_display="OMB Ethnicity Category",
        target_domain=CDISCDomain.DM,
        target_variable="eCRF.DM.ETHNIC",
        cdash_testcd="ETHNIC",
        cdash_test="Ethnicity",
        category="demographics",
        description="US OMB standard Hispanic/Latino ethnicity classification",
    ),
    ConceptMapElement(
        source_system="http://hl7.org/fhir/address-country",
        source_code="country",
        source_display="Country of Residence",
        target_domain=CDISCDomain.DM,
        target_variable="eCRF.DM.COUNTRY",
        cdash_testcd="COUNTRY",
        cdash_test="Country",
        category="demographics",
        description="ISO-3166 3-letter country code of residence",
    ),
]


def map_fhir_gender(gender_str: str | None) -> str:
    """Normalize FHIR gender string to CDASH standard sex codelist (M, F, U, UNDIFFERENTIATED)."""
    if not gender_str:
        return "U"
    g_lower = str(gender_str).strip().lower()
    if g_lower in ("male", "m"):
        return "M"
    if g_lower in ("female", "f"):
        return "F"
    if g_lower in ("other", "o", "undifferentiated"):
        return "UNDIFFERENTIATED"
    return "U"


def derive_subject_age(birth_date_str: str | None) -> int | None:
    """Derive subject age from ISO birth date string."""
    if not birth_date_str:
        return None
    try:
        from datetime import datetime

        birth_year = int(str(birth_date_str).split("-")[0])
        current_year = datetime.now().year
        return max(0, current_year - birth_year)
    except ValueError, IndexError, TypeError:
        return None
