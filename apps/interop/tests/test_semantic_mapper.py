# @req:PRD-CRF-007 - FHIR eSource Readiness & CDASH Pre-fill
# @req:PRD-SYS-001 - Standard Audit Logging (21 CFR Part 11)
# @req:PRD-SYS-051 - AI Gateway and Semantic Intelligence Architecture
"""Comprehensive test suite for Hybrid FHIR-to-CDISC Semantic Interoperability Mapper."""

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.interop.application.semantic_mapper_service import (
    SemanticMapperService,
)
from apps.interop.database import db_manager
from apps.interop.domain.concept_maps import (
    ALL_CONCEPT_MAPS,
    get_concept_maps_by_domain,
    lookup_concept_by_code,
)
from apps.interop.domain.semantic_mapping_models import (
    CDISCDomain,
    HybridMappingConfig,
    MappingStatus,
    MappingTier,
)
from apps.interop.infrastructure.embedding_matcher import EmbeddingMatcher
from apps.interop.infrastructure.llm_semantic_reasoner import LLMSemanticReasoner
from apps.interop.main import app
from apps.interop.models import Base, InteropAuditLog
from packages.testing.security import create_test_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory Interop database for unit and integration testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "admin,sponsor_dm",
    change_reason: str = "Standard GxP Testing Justification",
    user_id: str = "test_data_manager",
) -> dict[str, str]:
    """Helper to generate valid gateway V2 signed headers for testing."""
    return create_test_auth_headers(
        user_id=user_id,
        roles=[r.strip() for r in roles.split(",")],
        change_reason=change_reason,
    )


# ---------------------------------------------------------------------------
# Unit Tests: Pre-compiled ConceptMaps & Deterministic Lookup
# ---------------------------------------------------------------------------


def test_deterministic_concept_maps_catalog_coverage():
    """Validate that pre-compiled ConceptMaps cover all standard CDASH domains.

    @req:PRD-CRF-007
    """
    assert len(ALL_CONCEPT_MAPS) >= 30

    vs_maps = get_concept_maps_by_domain(CDISCDomain.VS)
    assert len(vs_maps) >= 10
    vs_codes = {m.source_code for m in vs_maps}
    assert "8480-6" in vs_codes  # SYSBP
    assert "8462-4" in vs_codes  # DIABP
    assert "8867-4" in vs_codes  # PULSE
    assert "8310-5" in vs_codes  # TEMP

    lb_maps = get_concept_maps_by_domain(CDISCDomain.LB)
    assert len(lb_maps) >= 15
    lb_codes = {m.source_code for m in lb_maps}
    assert "2339-0" in lb_codes  # GLUC
    assert "718-7" in lb_codes  # HGB
    assert "6690-2" in lb_codes  # WBC
    assert "2160-0" in lb_codes  # CREAT

    dm_maps = get_concept_maps_by_domain(CDISCDomain.DM)
    assert len(dm_maps) >= 3


def test_lookup_concept_by_code_and_system():
    """Validate exact code and system lookup in pre-compiled ConceptMaps.

    @req:PRD-CRF-007
    """
    # LOINC Systolic BP
    concept = lookup_concept_by_code("8480-6", "http://loinc.org")
    assert concept is not None
    assert concept.target_domain == CDISCDomain.VS
    assert concept.target_variable == "eCRF.VS.SYSBP"
    assert concept.cdash_testcd == "SYSBP"
    assert concept.standard_unit == "mmHg"

    # LOINC Glucose
    concept_lb = lookup_concept_by_code("2339-0")
    assert concept_lb is not None
    assert concept_lb.target_domain == CDISCDomain.LB
    assert concept_lb.cdash_testcd == "GLUC"

    # SNOMED Hypertension
    concept_mh = lookup_concept_by_code("38341003")
    assert concept_mh is not None
    assert concept_mh.target_domain == CDISCDomain.MH
    assert concept_mh.cdash_testcd == "MHTERM"

    # Non-existent code
    assert lookup_concept_by_code("NON_EXISTENT_CODE_99999") is None


# ---------------------------------------------------------------------------
# Unit Tests: Embedding Vector Similarity Matcher (Tier 2)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_embedding_matcher_synonym_resolution():
    """Validate Tier 2 embedding cosine similarity matches colloquial clinical verbatims.

    @req:PRD-CRF-007
    @req:PRD-SYS-051
    """
    matcher = EmbeddingMatcher()

    # 1. Colloquial / Synonym Vital Signs
    elem, score = await matcher.match_concept("systolic pressure", min_confidence=0.80)
    assert elem is not None
    assert elem.cdash_testcd == "SYSBP"
    assert score >= 0.82

    elem_hr, score_hr = await matcher.match_concept(
        "heart rate beats per min", min_confidence=0.80
    )
    assert elem_hr is not None
    assert elem_hr.cdash_testcd == "PULSE"
    assert score_hr >= 0.82

    elem_temp, score_temp = await matcher.match_concept(
        "body temperature", min_confidence=0.80
    )
    assert elem_temp is not None
    assert elem_temp.cdash_testcd == "TEMP"

    # 2. Colloquial / Synonym Laboratory tests
    elem_glu, score_glu = await matcher.match_concept(
        "fasting blood glucose", min_confidence=0.80
    )
    assert elem_glu is not None
    assert elem_glu.cdash_testcd == "GLUC"
    assert score_glu >= 0.82

    elem_alt, score_alt = await matcher.match_concept(
        "serum alanine aminotransferase", min_confidence=0.80
    )
    assert elem_alt is not None
    assert elem_alt.cdash_testcd == "ALT"

    # 3. Colloquial Medications & Conditions
    elem_med, score_med = await matcher.match_concept(
        "metformin antidiabetic", min_confidence=0.75
    )
    assert elem_med is not None
    assert elem_med.cdash_testcd == "CMTRT"

    elem_cond, score_cond = await matcher.match_concept(
        "essential hypertension", min_confidence=0.75
    )
    assert elem_cond is not None
    assert elem_cond.target_domain == CDISCDomain.MH


@pytest.mark.asyncio
async def test_embedding_matcher_low_confidence_rejection():
    """Validate that unclassifiable random text falls below minimum confidence.

    @req:PRD-CRF-007
    """
    matcher = EmbeddingMatcher()
    elem, score = await matcher.match_concept(
        "completely arbitrary unrelated aerospace engineering text",
        min_confidence=0.82,
    )
    assert elem is None
    assert score < 0.60


# ---------------------------------------------------------------------------
# Unit Tests: LLM Semantic Reasoner & Free-Text De-identification (Tier 3)
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_llm_semantic_reasoner_local_extraction():
    """Validate Tier 3 extraction of clinical concepts from unstructured narrative text.

    @req:PRD-CRF-007
    @req:PRD-SYS-051
    """
    reasoner = LLMSemanticReasoner()

    narrative = (
        "Patient presented with elevated BP 145/92 mmHg, pulse 84 bpm, and temp 37.4 C. "
        "Patient has a history of asthma and is currently taking Albuterol as needed."
    )

    items = await reasoner.extract_concepts_from_narrative(
        narrative_text=narrative,
        study_id="STUDY-EHR-01",
        custom_terms=["John Doe"],
    )

    assert len(items) >= 4
    vars_extracted = {i.target_variable: i for i in items}

    assert "eCRF.VS.SYSBP" in vars_extracted
    assert vars_extracted["eCRF.VS.SYSBP"].extracted_value == 145
    assert vars_extracted["eCRF.VS.SYSBP"].mapping_tier == MappingTier.LLM_FALLBACK

    assert "eCRF.VS.DIABP" in vars_extracted
    assert vars_extracted["eCRF.VS.DIABP"].extracted_value == 92

    assert "eCRF.VS.PULSE" in vars_extracted
    assert vars_extracted["eCRF.VS.PULSE"].extracted_value == 84

    assert "eCRF.VS.TEMP" in vars_extracted
    assert vars_extracted["eCRF.VS.TEMP"].extracted_value == 37.4


@pytest.mark.asyncio
async def test_llm_semantic_reasoner_phi_airgap_deidentification():
    """Validate that direct PHI (Patient names, SSNs) is sanitized prior to processing.

    @req:PRD-CRF-007
    @req:PRD-SYS-051
    """
    reasoner = LLMSemanticReasoner()
    raw_text = "Patient John Doe (SSN: 000-12-3456) has blood pressure 120/80 mmHg."

    sanitized = reasoner._deidentify_text(raw_text, custom_terms=["John Doe"])
    assert "John Doe" not in sanitized
    assert "000-12-3456" not in sanitized


# ---------------------------------------------------------------------------
# Integration Tests: Semantic Mapper Service 3-Tier Cascading
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_semantic_mapper_service_hybrid_cascade():
    """Validate full 3-tier hybrid cascade on mixed structured and unstructured FHIR bundle.

    @req:PRD-CRF-007
    @req:PRD-SYS-051
    """
    matcher = EmbeddingMatcher()
    reasoner = LLMSemanticReasoner(embedding_matcher=matcher)
    service = SemanticMapperService(embedding_matcher=matcher, llm_reasoner=reasoner)

    mixed_bundle = {
        "resourceType": "Bundle",
        "type": "transaction",
        "entry": [
            # 1. Patient Resource (Deterministic Demographics)
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "EHR-PATIENT-7788",
                    "gender": "female",
                    "birthDate": "1988-04-12",
                    "name": [{"family": "Johnson", "given": ["Alice"]}],
                }
            },
            # 2. Standard Observation (Tier 1: Deterministic LOINC match)
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-sbp-det",
                    "status": "final",
                    "code": {
                        "coding": [{"system": "http://loinc.org", "code": "8480-6"}],
                        "text": "Systolic blood pressure",
                    },
                    "valueQuantity": {"value": 128, "unit": "mmHg"},
                    "effectiveDateTime": "2026-08-21T10:00:00Z",
                }
            },
            # 3. Non-Standard Observation with Synonym Text (Tier 2: Embedding match)
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-glu-emb",
                    "status": "final",
                    "code": {
                        "coding": [{"code": "LOCAL_HOSP_GLUCOSE_CODE"}],
                        "text": "fasting blood glucose",
                    },
                    "valueQuantity": {"value": 98.5, "unit": "mg/dL"},
                    "effectiveDateTime": "2026-08-21T10:05:00Z",
                }
            },
            # 4. Standard Condition (Tier 1: Deterministic SNOMED)
            {
                "resource": {
                    "resourceType": "Condition",
                    "id": "cond-1",
                    "code": {
                        "coding": [
                            {"system": "http://snomed.info/sct", "code": "38341003"}
                        ],
                        "text": "Hypertension",
                    },
                    "onsetDateTime": "2020-01-15",
                }
            },
            # 5. Standard Medication (Tier 1: Deterministic RxNorm)
            {
                "resource": {
                    "resourceType": "MedicationStatement",
                    "id": "med-1",
                    "medicationCodeableConcept": {
                        "coding": [
                            {
                                "system": "http://www.nlm.nih.gov/research/umls/rxnorm",
                                "code": "6809",
                            }
                        ],
                        "text": "Metformin",
                    },
                    "effectiveDateTime": "2021-06-01",
                }
            },
            # 6. Unstructured Consultation Note (Tier 3: LLM Reasoner)
            {
                "resource": {
                    "resourceType": "DocumentReference",
                    "id": "doc-narrative-1",
                    "text": {
                        "div": "Follow-up note: Patient pulse 76 bpm on examination."
                    },
                }
            },
        ],
    }

    config = HybridMappingConfig(study_id="PROTOCOL-2026")
    result = await service.map_fhir_bundle(mixed_bundle, config=config)

    # 1. Subject pseudonymization & demographics
    assert result.study_id == "PROTOCOL-2026"
    assert result.subject_pseudonym != "EHR-PATIENT-7788"
    assert result.mapped_fields["DM.SEX"] == "F"
    assert result.mapped_fields["DM.BRTHDTC"] == "1988-04-12"
    assert "eCRF.DM.AGE" in result.mapped_fields

    # 2. Deterministic SBP mapping
    assert result.mapped_fields["eCRF.VS.SYSBP"] == 128

    # 3. Embedding Glucose mapping
    assert result.mapped_fields["eCRF.LB.GLUC"] == 98.5

    # 4. Condition & Medication
    assert "Hypertension" in str(result.mapped_fields["eCRF.MH.MHTERM"])
    assert "Metformin" in str(result.mapped_fields["eCRF.CM.CMTRT"])

    # 5. Tier statistics metrics
    assert result.statistics.total_extracted >= 6
    assert result.statistics.deterministic_count >= 4
    assert result.statistics.embedding_count >= 1
    assert result.statistics.llm_fallback_count >= 1
    assert result.statistics.execution_latency_ms >= 0.0


# ---------------------------------------------------------------------------
# API Integration Tests: Endpoints & GxP 21 CFR Part 11 Audit Trail
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_api_fhir_semantic_map_endpoint_e2e():
    """Validate POST /api/v1/interop/fhir/semantic-map endpoint and GxP audit logging.

    @req:PRD-CRF-007
    @req:PRD-SYS-001
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="sponsor_dm,admin",
        change_reason="Clinical data mapping ingestion for Protocol-500",
        user_id="lead_data_manager",
    )

    bundle_payload = {
        "study_id": "STUDY-ONCOLOGY-01",
        "bundle": {
            "resourceType": "Bundle",
            "type": "transaction",
            "entry": [
                {
                    "resource": {
                        "resourceType": "Patient",
                        "id": "PATIENT-9900",
                        "gender": "male",
                        "birthDate": "1975-11-20",
                    }
                },
                {
                    "resource": {
                        "resourceType": "Observation",
                        "id": "obs-hr",
                        "code": {
                            "coding": [
                                {"system": "http://loinc.org", "code": "8867-4"}
                            ],
                            "text": "Heart rate",
                        },
                        "valueQuantity": {"value": 72, "unit": "beats/min"},
                    }
                },
                {
                    "resource": {
                        "resourceType": "Observation",
                        "id": "obs-synonym-spo2",
                        "code": {"text": "oxygen saturation"},
                        "valueQuantity": {"value": 99, "unit": "%"},
                    }
                },
            ],
        },
        "enable_deterministic": True,
        "enable_embedding": True,
        "enable_llm_fallback": True,
        "embedding_confidence_threshold": 0.80,
    }

    response = client.post(
        "/api/v1/interop/fhir/semantic-map",
        json=bundle_payload,
        headers=headers,
    )
    assert response.status_code == 200
    data = response.json()

    assert data["study_id"] == "STUDY-ONCOLOGY-01"
    assert data["mapped_fields"]["DM.SEX"] == "M"
    assert data["mapped_fields"]["eCRF.VS.PULSE"] == 72
    assert data["mapped_fields"]["eCRF.VS.SPO2"] == 99

    assert data["statistics"]["deterministic_count"] >= 3
    assert data["statistics"]["embedding_count"] >= 1

    # Verify GxP Part 11 audit log entry was created
    async with db_manager.session_maker() as session:
        stmt = select(InteropAuditLog).where(
            InteropAuditLog.action == "FHIR_SEMANTIC_MAP"
        )
        res = await session.execute(stmt)
        log = res.scalars().first()

        assert log is not None
        assert log.user_id == "lead_data_manager"
        assert log.change_reason == "Clinical data mapping ingestion for Protocol-500"
        assert "STUDY-ONCOLOGY-01" in log.details


@pytest.mark.asyncio
async def test_api_list_concept_maps_metadata_endpoint():
    """Validate GET /api/v1/interop/fhir/concept-maps endpoint returns catalog summaries.

    @req:PRD-CRF-007
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="sponsor_dm")

    response = client.get("/api/v1/interop/fhir/concept-maps", headers=headers)
    assert response.status_code == 200
    data = response.json()

    assert data["total_concepts"] >= 30
    assert "VS" in data["domains_supported"]
    assert "LB" in data["domains_supported"]
    assert "DM" in data["domains_supported"]
    assert "MH" in data["domains_supported"]
    assert "CM" in data["domains_supported"]
    assert "PR" in data["domains_supported"]

    # Verify specific concept entry structure
    sbp_entry = next(
        (c for c in data["concept_maps"] if c["cdash_testcd"] == "SYSBP"), None
    )
    assert sbp_entry is not None
    assert sbp_entry["source_code"] == "8480-6"
    assert sbp_entry["target_variable"] == "eCRF.VS.SYSBP"


# ---------------------------------------------------------------------------
# Adversarial & Boundary Tests
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_adversarial_empty_and_malformed_bundles():
    """Validate resilient handling of empty or malformed FHIR payloads.

    @req:PRD-CRF-007
    """
    matcher = EmbeddingMatcher()
    reasoner = LLMSemanticReasoner(embedding_matcher=matcher)
    service = SemanticMapperService(embedding_matcher=matcher, llm_reasoner=reasoner)

    # 1. Empty bundle dictionary
    res_empty = await service.map_fhir_bundle({})
    assert res_empty.subject_pseudonym == "unknown_pseudonym"
    assert len(res_empty.mapped_items) == 0
    assert res_empty.statistics.total_extracted == 0

    # 2. Bundle with missing or corrupted entries
    corrupted_bundle = {
        "resourceType": "Bundle",
        "entry": [
            None,
            {},
            {"resource": None},
            {"resource": {"resourceType": "NonExistentResourceType"}},
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "bad-obs",
                    "code": {},
                }
            },
        ],
    }
    res_corrupted = await service.map_fhir_bundle(corrupted_bundle)
    assert res_corrupted.statistics.total_extracted >= 0


@pytest.mark.asyncio
async def test_confidence_gating_human_review_threshold():
    """Validate that items below human review threshold are explicitly flagged.

    @req:PRD-CRF-007
    """
    matcher = EmbeddingMatcher()
    reasoner = LLMSemanticReasoner(embedding_matcher=matcher)
    service = SemanticMapperService(embedding_matcher=matcher, llm_reasoner=reasoner)

    config = HybridMappingConfig(
        human_review_confidence_threshold=0.90,  # Strict threshold
        embedding_confidence_threshold=0.70,
    )

    bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "obs-synonym",
                    "code": {"text": "fasting blood glucose"},
                    "valueQuantity": {"value": 105, "unit": "mg/dL"},
                }
            }
        ],
    }

    result = await service.map_fhir_bundle(bundle, config=config)
    assert len(result.mapped_items) >= 1
    item = result.mapped_items[0]

    # Score from embedding is ~0.82-0.88, which is below 0.90 strict review threshold
    if item.confidence_score < 0.90:
        assert item.needs_human_review is True
        assert item.status == MappingStatus.FLAGGED_FOR_REVIEW
        assert result.statistics.flagged_for_review_count >= 1
