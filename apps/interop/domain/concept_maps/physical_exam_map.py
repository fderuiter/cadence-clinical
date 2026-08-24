"""Pre-compiled FHIR/SNOMED to CDASH Physical Examination (PE) ConceptMaps.

Requirements: PRD-CRF-007, PRD-SYS-185, PRD-SYS-186
"""

from apps.interop.domain.semantic_mapping_models import CDISCDomain, ConceptMapElement

SNOMED_SYSTEM = "http://snomed.info/sct"
LOINC_SYSTEM = "http://loinc.org"

PHYSICAL_EXAM_CONCEPT_MAP: list[ConceptMapElement] = [
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="5880005",
        source_display="Physical examination procedure",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="GENEXAM",
        cdash_test="General Physical Examination",
        category="exam",
        description="General physical examination finding",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="301857004",
        source_display="Cardiovascular examination",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="CVEXAM",
        cdash_test="Cardiovascular System Examination",
        category="exam",
        description="Cardiovascular examination evaluation",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="301870008",
        source_display="Respiratory examination",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="RESPEXAM",
        cdash_test="Respiratory System Examination",
        category="exam",
        description="Respiratory / pulmonary examination evaluation",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="301899001",
        source_display="Abdominal examination",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="ABDEXAM",
        cdash_test="Abdominal System Examination",
        category="exam",
        description="Abdomen and gastrointestinal physical examination",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="301831006",
        source_display="Neurological examination",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="NEUREXAM",
        cdash_test="Neurological System Examination",
        category="exam",
        description="Neurological system examination evaluation",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="301817006",
        source_display="Musculoskeletal examination",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="MSKEXAM",
        cdash_test="Musculoskeletal System Examination",
        category="exam",
        description="Musculoskeletal physical examination evaluation",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="301844005",
        source_display="Dermatological examination",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="SKINEXAM",
        cdash_test="Dermatological / Skin Examination",
        category="exam",
        description="Skin and integumentary physical examination evaluation",
    ),
    ConceptMapElement(
        source_system=SNOMED_SYSTEM,
        source_code="301887009",
        source_display="HEENT examination",
        target_domain=CDISCDomain.PE,
        target_variable="eCRF.PE.PETEST",
        cdash_testcd="HEENTEXAM",
        cdash_test="Head, Eyes, Ears, Nose, Throat Examination",
        category="exam",
        description="HEENT physical examination evaluation",
    ),
]
