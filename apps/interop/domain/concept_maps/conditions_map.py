"""Pre-compiled FHIR Condition to CDASH Medical History (MH) and Adverse Events (AE) ConceptMaps.

Requirements: PRD-CRF-007
"""

from apps.interop.domain.semantic_mapping_models import CDISCDomain, ConceptMapElement

SNOMED_SYSTEM = "http://snomed.info/sct"
ICD10_SYSTEM = "http://hl7.org/fhir/sid/icd-10-cm"

CONDITIONS_CONCEPT_MAP: list[ConceptMapElement] = [
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="38341003",
        source_display="Hypertension",
        target_domain=CDISCDomain.MH,
        target_variable="eCRF.MH.MHTERM",
        cdash_testcd="MHTERM",
        cdash_test="Medical History Term",
        category="conditions",
        description="Hypertensive disorder",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="44054006",
        source_display="Type 2 diabetes mellitus",
        target_domain=CDISCDomain.MH,
        target_variable="eCRF.MH.MHTERM",
        cdash_testcd="MHTERM",
        cdash_test="Medical History Term",
        category="conditions",
        description="Type 2 diabetes mellitus diagnosis",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="195967001",
        source_display="Asthma",
        target_domain=CDISCDomain.MH,
        target_variable="eCRF.MH.MHTERM",
        cdash_testcd="MHTERM",
        cdash_test="Medical History Term",
        category="conditions",
        description="Asthma respiratory condition",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="37796009",
        source_display="Migraine",
        target_domain=CDISCDomain.MH,
        target_variable="eCRF.MH.MHTERM",
        cdash_testcd="MHTERM",
        cdash_test="Medical History Term",
        category="conditions",
        description="Migraine headache disorder",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="56265001",
        source_display="Heart disease",
        target_domain=CDISCDomain.MH,
        target_variable="eCRF.MH.MHTERM",
        cdash_testcd="MHTERM",
        cdash_test="Medical History Term",
        category="conditions",
        description="Cardiovascular disease",
    ),
]
