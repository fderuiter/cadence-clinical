"""Pre-compiled FHIR MedicationStatement/Request to CDASH Concomitant Medications (CM) ConceptMaps.

Requirements: PRD-CRF-007
"""

from apps.interop.domain.semantic_mapping_models import CDISCDomain, ConceptMapElement

RXNORM_SYSTEM = "http://www.nlm.nih.gov/research/umls/rxnorm"

MEDICATIONS_CONCEPT_MAP: list[ConceptMapElement] = [
    ConceptMapElement(
        source_system=RXNORM_SYSTEM,
        source_code="6809",
        source_display="Metformin",
        target_domain=CDISCDomain.CM,
        target_variable="eCRF.CM.CMTRT",
        cdash_testcd="CMTRT",
        cdash_test="Reported Name of Drug, Med, or Therapy",
        standard_unit="mg",
        category="medications",
        description="Metformin antidiabetic medication",
    ),
    ConceptMapElement(
        source_system=RXNORM_SYSTEM,
        source_code="29046",
        source_display="Lisinopril",
        target_domain=CDISCDomain.CM,
        target_variable="eCRF.CM.CMTRT",
        cdash_testcd="CMTRT",
        cdash_test="Reported Name of Drug, Med, or Therapy",
        standard_unit="mg",
        category="medications",
        description="Lisinopril ACE inhibitor antihypertensive",
    ),
    ConceptMapElement(
        source_system=RXNORM_SYSTEM,
        source_code="435",
        source_display="Albuterol",
        target_domain=CDISCDomain.CM,
        target_variable="eCRF.CM.CMTRT",
        cdash_testcd="CMTRT",
        cdash_test="Reported Name of Drug, Med, or Therapy",
        category="medications",
        description="Albuterol bronchodilator",
    ),
    ConceptMapElement(
        source_system=RXNORM_SYSTEM,
        source_code="1191",
        source_display="Aspirin",
        target_domain=CDISCDomain.CM,
        target_variable="eCRF.CM.CMTRT",
        cdash_testcd="CMTRT",
        cdash_test="Reported Name of Drug, Med, or Therapy",
        standard_unit="mg",
        category="medications",
        description="Acetylsalicylic acid / Aspirin",
    ),
    ConceptMapElement(
        source_system=RXNORM_SYSTEM,
        source_code="161",
        source_display="Acetaminophen",
        target_domain=CDISCDomain.CM,
        target_variable="eCRF.CM.CMTRT",
        cdash_testcd="CMTRT",
        cdash_test="Reported Name of Drug, Med, or Therapy",
        standard_unit="mg",
        category="medications",
        description="Acetaminophen / Paracetamol analgesic",
    ),
]
