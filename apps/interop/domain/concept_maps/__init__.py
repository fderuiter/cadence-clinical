"""Catalog and registry of pre-compiled FHIR-to-CDISC ConceptMaps.

Requirements: PRD-CRF-007
"""

from apps.interop.domain.concept_maps.conditions_map import CONDITIONS_CONCEPT_MAP
from apps.interop.domain.concept_maps.demographics_map import (
    DEMOGRAPHICS_CONCEPT_MAP,
    derive_subject_age,
    map_fhir_gender,
)
from apps.interop.domain.concept_maps.lab_tests_map import LAB_TESTS_CONCEPT_MAP
from apps.interop.domain.concept_maps.medications_map import MEDICATIONS_CONCEPT_MAP
from apps.interop.domain.concept_maps.procedures_map import PROCEDURES_CONCEPT_MAP
from apps.interop.domain.concept_maps.vital_signs_map import VITAL_SIGNS_CONCEPT_MAP
from apps.interop.domain.semantic_mapping_models import (
    CDISCDomain,
    ConceptMapElement,
)

ALL_CONCEPT_MAPS: list[ConceptMapElement] = (
    VITAL_SIGNS_CONCEPT_MAP
    + LAB_TESTS_CONCEPT_MAP
    + DEMOGRAPHICS_CONCEPT_MAP
    + CONDITIONS_CONCEPT_MAP
    + MEDICATIONS_CONCEPT_MAP
    + PROCEDURES_CONCEPT_MAP
)

# Index by source_code (e.g. LOINC code or standard token)
_CODE_LOOKUP: dict[str, ConceptMapElement] = {
    elem.source_code.lower(): elem for elem in ALL_CONCEPT_MAPS
}

# Index by (source_system, source_code)
_SYSTEM_CODE_LOOKUP: dict[tuple[str, str], ConceptMapElement] = {
    (elem.source_system.lower(), elem.source_code.lower()): elem
    for elem in ALL_CONCEPT_MAPS
}


def lookup_concept_by_code(
    code: str, system: str | None = None
) -> ConceptMapElement | None:
    """Lookup a pre-compiled ConceptMapElement by code and optional system."""
    if not code:
        return None
    code_norm = code.strip().lower()
    if system:
        system_norm = system.strip().lower()
        if (system_norm, code_norm) in _SYSTEM_CODE_LOOKUP:
            return _SYSTEM_CODE_LOOKUP[(system_norm, code_norm)]
    return _CODE_LOOKUP.get(code_norm)


def get_concept_maps_by_domain(domain: CDISCDomain) -> list[ConceptMapElement]:
    """Retrieve all ConceptMap elements targeting a specific CDISC domain."""
    return [elem for elem in ALL_CONCEPT_MAPS if elem.target_domain == domain]


__all__ = [
    "ALL_CONCEPT_MAPS",
    "CONDITIONS_CONCEPT_MAP",
    "DEMOGRAPHICS_CONCEPT_MAP",
    "LAB_TESTS_CONCEPT_MAP",
    "MEDICATIONS_CONCEPT_MAP",
    "PROCEDURES_CONCEPT_MAP",
    "VITAL_SIGNS_CONCEPT_MAP",
    "derive_subject_age",
    "get_concept_maps_by_domain",
    "lookup_concept_by_code",
    "map_fhir_gender",
]
