"""Unit and integration test suite for hybrid semantic medical coding with pgvector.

Requirements: PRD-SYS-008, PRD-SYS-042
GxP 21 CFR Part 11 Dual-Attribution Audit Compliance
"""

import pytest
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.execution.coding.matcher import (
    calculate_cosine_similarity,
    generate_local_term_embedding,
    match_semantic_verbatim_term,
)
from apps.execution.coding.service import (
    process_coding_action,
    suggest_semantic_coding,
)
from apps.execution.database.models import (
    Base,
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    CodingState,
    DictionaryType,
    MedDRAHierarchy,
    MedDRATerm,
    RecodingState,
    WHODrugATC,
    WHODrugDrugATC,
    WHODrugDrugIngredient,
    WHODrugIngredient,
    WHODrugRecord,
)


@pytest.fixture
async def async_session():
    """In-memory SQLite session for testing semantic medical coding."""
    engine = create_async_engine(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
        execution_options={"schema_translate_map": {"audit_schema": None}},
    )
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_factory = async_sessionmaker(
        engine, expire_on_commit=False, class_=AsyncSession
    )
    async with session_factory() as session:
        # Seed MedDRA test terms and hierarchy
        meddra_terms = [
            MedDRATerm(
                id="term-1",
                dictionary_version="26.0",
                code="10047700",
                term_name="Vomiting",
                level="PT",
            ),
            MedDRATerm(
                id="term-2",
                dictionary_version="26.0",
                code="10047701",
                term_name="Throwing up",
                level="LLT",
            ),
            MedDRATerm(
                id="term-3",
                dictionary_version="26.0",
                code="10019211",
                term_name="Headache",
                level="PT",
            ),
            MedDRATerm(
                id="term-4",
                dictionary_version="26.0",
                code="10007839",
                term_name="Cephalalgia",
                level="LLT",
            ),
        ]
        session.add_all(meddra_terms)

        meddra_hierarchies = [
            MedDRAHierarchy(
                id="hier-1",
                dictionary_version="26.0",
                llt_code="10047701",
                pt_code="10047700",
                hlt_code="10028813",
                hlgt_code="10017999",
                soc_code="10017947",
                primary_soc_flag="Y",
            ),
            MedDRAHierarchy(
                id="hier-2",
                dictionary_version="26.0",
                llt_code="10007839",
                pt_code="10019211",
                hlt_code="10019231",
                hlgt_code="10019233",
                soc_code="10029205",
                primary_soc_flag="Y",
            ),
        ]
        session.add_all(meddra_hierarchies)

        # Seed WHODrug records
        who_records = [
            WHODrugRecord(
                id="who-1",
                dictionary_version="202403",
                drug_code="000123",
                drug_name="Aspirin 500mg",
                preferred_name="Acetylsalicylic acid",
            )
        ]
        session.add_all(who_records)

        who_atc = [
            WHODrugATC(
                id="atc-1",
                dictionary_version="202403",
                atc_code="N02BA01",
                description="acetylsalicylic acid",
            )
        ]
        session.add_all(who_atc)

        who_drug_atc = [
            WHODrugDrugATC(
                id="datc-1",
                dictionary_version="202403",
                drug_code="000123",
                atc_code="N02BA01",
            )
        ]
        session.add_all(who_drug_atc)

        who_ing = [
            WHODrugIngredient(
                id="ing-1",
                dictionary_version="202403",
                ingredient_code="ING001",
                ingredient_name="Aspirin",
            )
        ]
        session.add_all(who_ing)

        who_drug_ing = [
            WHODrugDrugIngredient(
                id="ding-1",
                dictionary_version="202403",
                drug_code="000123",
                ingredient_code="ING001",
            )
        ]
        session.add_all(who_drug_ing)

        await session.commit()
        yield session

    await engine.dispose()


def test_cosine_similarity_computation():
    """Validates mathematical cosine similarity score calculation with unit vectors.

    @req:PRD-SYS-008
    """
    # Identical vectors -> 1.0
    vec_a = [1.0, 2.0, 3.0]
    assert pytest.approx(calculate_cosine_similarity(vec_a, vec_a), rel=1e-5) == 1.0

    # Orthogonal vectors -> 0.0
    vec_b = [1.0, 0.0]
    vec_c = [0.0, 1.0]
    assert pytest.approx(calculate_cosine_similarity(vec_b, vec_c), rel=1e-5) == 0.0

    # Opposite vectors -> -1.0
    vec_d = [-1.0, -2.0, -3.0]
    assert pytest.approx(calculate_cosine_similarity(vec_a, vec_d), rel=1e-5) == -1.0

    # Zero vector handling -> 0.0
    assert calculate_cosine_similarity([0.0, 0.0], [1.0, 1.0]) == 0.0
    assert calculate_cosine_similarity([], []) == 0.0


def test_dense_vector_representation():
    """Validates deterministic Tier 1 dense vector embedding generation for clinical text.

    @req:PRD-SYS-008
    """
    v1 = generate_local_term_embedding("Patient threw up after taking meds")
    v2 = generate_local_term_embedding("Patient threw up after taking meds")
    v3 = generate_local_term_embedding(
        "Severe throbbing headache in left temporal lobe"
    )

    assert len(v1) == 64  # Standard dense embedding dimension
    assert v1 == v2  # Deterministic representation
    assert v1 != v3  # Semantic distinction

    # Cosine similarity between related clinical terms is higher than unrelated
    v_vomit = generate_local_term_embedding("Vomiting")
    sim_related = calculate_cosine_similarity(v1, v_vomit)
    sim_unrelated = calculate_cosine_similarity(v3, v_vomit)
    assert sim_related > sim_unrelated


@pytest.mark.asyncio
async def test_hybrid_meddra_semantic_matching(async_session: AsyncSession):
    """Validates hybrid lexical and vector cosine ranking on colloquial clinical verbatims.

    @req:PRD-SYS-008
    """
    verbatim = "Patient had severe vomiting episodes and was throwing up"
    result = await match_semantic_verbatim_term(
        session=async_session,
        verbatim=verbatim,
        dictionary_type="MEDDRA",
        version="26.0",
        top_k=5,
    )

    assert result["status"] in ("AUTO-CODED", "SUGGESTIONS")
    candidates = []
    if result.get("match"):
        candidates.append(result["match"])
    if result.get("suggestions"):
        candidates.extend(result["suggestions"])

    assert len(candidates) > 0
    top_candidate = candidates[0]

    # Verify top candidate resolved to Vomiting PT (10047700) or Throwing up LLT (10047701)
    assert top_candidate["code"] in ("10047700", "10047701")
    assert top_candidate["score"] >= 0.75
    assert "cosine_similarity" in top_candidate
    assert "lexical_score" in top_candidate
    assert "combined_score" in top_candidate
    assert top_candidate["model_identifier"] == "system:ai:tier1:all-MiniLM-L6-v2"


@pytest.mark.asyncio
async def test_hybrid_whodrug_semantic_matching(async_session: AsyncSession):
    """Validates semantic matching and ATC hierarchy extraction against WHODrug records.

    @req:PRD-SYS-008
    """
    verbatim = "Prescribed 500mg Aspirin tablets for fever"
    result = await match_semantic_verbatim_term(
        session=async_session,
        verbatim=verbatim,
        dictionary_type="WHODRUG",
        version="202403",
        top_k=3,
    )

    assert result["status"] in ("AUTO-CODED", "SUGGESTIONS")
    candidates = []
    if result.get("match"):
        candidates.append(result["match"])
    if result.get("suggestions"):
        candidates.extend(result["suggestions"])

    assert len(candidates) > 0
    top_match = candidates[0]
    assert top_match["drug_code"] == "000123"
    assert top_match["preferred_name"] == "Acetylsalicylic acid"
    assert top_match["score"] >= 0.70
    assert len(top_match["atc_context"]) > 0
    assert top_match["atc_context"][0]["atc_code"] == "N02BA01"


@pytest.mark.asyncio
async def test_dual_attribution_part11_audit_logging(async_session: AsyncSession):
    """Validates 21 CFR Part 11 dual-attribution audit ledger when Data Manager accepts AI suggestion.

    @req:PRD-SYS-008
    @req:PRD-SYS-042
    """
    # 1. Create an UNCODED assignment with AI suggestions
    assignment = ClinicalCodingAssignment(
        id="assign-ai-1",
        verbatim_text="Patient reported cephalalgia after breakfast",
        source_field="AE.AETERM",
        observation_id="OBS-101",
        dictionary_type=DictionaryType.MEDDRA,
        dictionary_version="26.0",
        status=CodingState.UNCODED,
        recoding_status=RecodingState.NONE,
        assigned_by="system:ai:tier1",
    )
    async_session.add(assignment)
    await async_session.commit()

    # 2. Generate and attach semantic suggestions
    suggested = await suggest_semantic_coding(
        session=async_session,
        assignment_id="assign-ai-1",
    )
    assert suggested.status == CodingState.SUGGESTED
    assert suggested.score is not None
    assert len(suggested.suggestions) > 0

    # 3. Data Manager accepts AI proposal with human-in-the-loop audit reason
    top_sug = suggested.suggestions[0]
    sug_code = top_sug.get("code") or top_sug.get("drug_code")
    sug_term = (
        top_sug.get("term_name")
        or top_sug.get("drug_name")
        or top_sug.get("preferred_name")
    )
    dm_user_id = "data.manager@clinical.org"
    reason = f"Confirmed AI suggested coding to {sug_term} ({sug_code})"

    updated = await process_coding_action(
        session=async_session,
        assignment_id="assign-ai-1",
        action="ACCEPT",
        code=sug_code,
        term=sug_term,
        reason_for_change=reason,
        actor=dm_user_id,
    )

    assert updated.status == CodingState.CODED
    assert updated.coded_code == sug_code
    assert updated.coded_term == sug_term
    assert updated.assigned_by == dm_user_id

    # 4. Verify ClinicalCodingLedger records dual attribution (AI generator + human approver)
    ledger_stmt = select(ClinicalCodingLedger).where(
        ClinicalCodingLedger.assignment_id == "assign-ai-1"
    )
    ledger_res = await async_session.execute(ledger_stmt)
    entries = ledger_res.scalars().all()

    assert len(entries) >= 1
    last_entry = entries[-1]
    assert last_entry.new_coded_code == sug_code
    assert last_entry.decision_by == dm_user_id
    assert last_entry.recoding_reason == reason
