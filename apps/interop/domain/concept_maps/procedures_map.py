"""Pre-compiled FHIR Procedure to CDASH Procedures (PR) ConceptMaps.

Requirements: PRD-CRF-007
"""

from apps.interop.domain.semantic_mapping_models import CDISCDomain, ConceptMapElement

SNOMED_SYSTEM = "http://snomed.info/sct"
CPT_SYSTEM = "http://www.ama-assn.org/go/cpt"

PROCEDURES_CONCEPT_MAP: list[ConceptMapElement] = [
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="80146002",
        source_display="Appendectomy",
        target_domain=CDISCDomain.PR,
        target_variable="eCRF.PR.PRTRT",
        cdash_testcd="PRTRT",
        cdash_test="Reported Name of Procedure",
        category="procedures",
        description="Excision of appendix",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="232717009",
        source_display="Coronary artery bypass graft",
        target_domain=CDISCDomain.PR,
        target_variable="eCRF.PR.PRTRT",
        cdash_testcd="PRTRT",
        cdash_test="Reported Name of Procedure",
        category="procedures",
        description="CABG procedure",
    ),
    ConceptMapElement(
        source_system=CPT_SYSTEM,
        source_code="93000",
        source_display="Electrocardiogram, routine ECG with at least 12 leads",
        target_domain=CDISCDomain.PR,
        target_variable="eCRF.PR.PRTRT",
        cdash_testcd="PRTRT",
        cdash_test="Reported Name of Procedure",
        category="procedures",
        description="12-lead diagnostic ECG",
    ),
]
