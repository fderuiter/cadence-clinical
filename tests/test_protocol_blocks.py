import pytest
from fastapi.testclient import TestClient
from protocol_authoring import (
    CANONICAL_ICH_SKELETON,
    BlockType,
    EligibilityBlock,
    NarrativeBlock,
    ObjectiveBlock,
    SoADerivedBlock,
)

from apps.designer.comparison import compare_payloads
from apps.designer.db import (
    MOCK_STUDY_VERSIONS,
)
from apps.designer.delta import (
    MOCK_SOA_DATA,
    ConcurrentLockingError,
    ImmutabilityViolationError,
    create_block,
    create_study_arm,
    delete_block,
    get_block,
    get_soa_matrix_projection,
    link_arm_applicability,
    list_blocks,
    reorder_blocks,
    update_block,
    update_study_arm,
)
from apps.designer.inverse_mapper import map_usdm_to_study
from apps.designer.main import app
from apps.designer.mapper import map_study_to_usdm
from packages.security.signing import generate_gateway_signature


@pytest.fixture(autouse=True)
def clean_mock_stores():
    """Ensures a clean state of mock stores for each test."""
    MOCK_SOA_DATA.clear()
    MOCK_STUDY_VERSIONS.clear()


# ==========================================
# 1. Model Validation Tests
# ==========================================


def test_protocol_block_validation():
    # Valid NarrativeBlock
    nb = NarrativeBlock(
        block_id="b_1",
        order=1,
        created_by="jules",
        reason_for_change="Initial text",
        title="Study Outline",
        text="This is a test narrative description.",
    )
    assert nb.block_type == BlockType.NARRATIVE
    assert nb.block_id == "b_1"
    assert nb.version_index == 1
    assert nb.derived_from_soa is False

    # Valid ObjectiveBlock
    ob = ObjectiveBlock(
        block_id="b_2",
        order=2,
        created_by="jules",
        reason_for_change="Associated objective",
        objective_id="obj_primary",
        text="To evaluate drug efficacy",
    )
    assert ob.block_type == BlockType.OBJECTIVE
    assert ob.objective_id == "obj_primary"

    # Valid EligibilityBlock
    eb = EligibilityBlock(
        block_id="b_3",
        order=3,
        created_by="jules",
        reason_for_change="Criterion linkage",
        criterion_id="INC_01",
        criterion_type="inclusion",
        text="Age >= 18 years",
    )
    assert eb.block_type == BlockType.ELIGIBILITY
    assert eb.criterion_id == "INC_01"

    # Valid SoADerivedBlock
    sb = SoADerivedBlock(
        block_id="b_4",
        order=4,
        created_by="jules",
        reason_for_change="Derived from Arm mutation",
        source_entity_id="arm_active",
        source_entity_type="arm",
        text="Description of Arm Active",
    )
    assert sb.block_type == BlockType.SOA_DERIVED
    assert sb.derived_from_soa is True


def test_protocol_block_parenting():
    nb_child = NarrativeBlock(
        block_id="b_child",
        parent_id="b_parent",
        order=2,
        created_by="jules",
        reason_for_change="Sub-heading child block",
        title="Background",
        text="Sub-heading narrative content.",
    )
    assert nb_child.parent_id == "b_parent"


def test_canonical_ich_skeleton():
    skeleton = CANONICAL_ICH_SKELETON
    assert len(skeleton) == 6
    assert skeleton[0].section_id == "sec_1"
    assert skeleton[0].title == "Protocol Synopsis"
    assert skeleton[1].section_id == "sec_2"
    assert len(skeleton[1].children) == 2
    assert skeleton[1].children[0].section_id == "sec_2_1"
    assert skeleton[1].children[0].title == "Study Rationale"


# ==========================================
# 2. Persistence & Projection Tests
# ==========================================


@pytest.mark.asyncio
async def test_block_persistence_lifecycle():
    study_version_id = "sv_block_1"
    MOCK_STUDY_VERSIONS["study_abc"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Create Block
    b_id = await create_block(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_test",
        change_reason="Add initial narrative",
        block_id="block_narr",
        properties={
            "title": "Introduction",
            "text": "This is introductory text.",
            "block_type": "narrative",
            "order": 1,
        },
    )
    assert b_id == "block_narr"

    # Get Block
    block = await get_block(
        driver=None, study_version_id=study_version_id, block_id="block_narr"
    )
    assert block["title"] == "Introduction"
    assert block["order"] == 1
    assert block["version_index"] == 1

    # Duplicate block creation failure
    with pytest.raises(ConcurrentLockingError):
        await create_block(
            driver=None,
            study_version_id=study_version_id,
            user_id="user_test",
            change_reason="Duplicate block attempt",
            block_id="block_narr",
            properties={"text": "Dup"},
        )

    # Update Block
    await update_block(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_test",
        change_reason="Modify introductory text",
        block_id="block_narr",
        properties={
            "title": "Introduction (Updated)",
            "text": "New text.",
            "block_type": "narrative",
            "order": 1,
        },
    )
    updated_block = await get_block(
        driver=None, study_version_id=study_version_id, block_id="block_narr"
    )
    assert updated_block["title"] == "Introduction (Updated)"
    assert updated_block["version_index"] == 2

    # Delete Block
    await delete_block(
        driver=None,
        study_version_id=study_version_id,
        user_id="user_test",
        change_reason="Soft-delete narrative",
        block_id="block_narr",
    )
    deleted_block = await get_block(
        driver=None, study_version_id=study_version_id, block_id="block_narr"
    )
    assert deleted_block is None


@pytest.mark.asyncio
async def test_reorder_blocks():
    study_version_id = "sv_reorder"
    MOCK_STUDY_VERSIONS["study_reorder"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    await create_block(
        None,
        study_version_id,
        "user1",
        "b1",
        "b_1",
        {"block_type": "narrative", "order": 1, "text": "One"},
    )
    await create_block(
        None,
        study_version_id,
        "user1",
        "b2",
        "b_2",
        {"block_type": "narrative", "order": 2, "text": "Two"},
    )

    success = await reorder_blocks(
        driver=None,
        study_version_id=study_version_id,
        user_id="user1",
        change_reason="Swap order",
        block_ids_ordered=["b_2", "b_1"],
    )
    assert success is True

    blocks = await list_blocks(None, study_version_id)
    assert len(blocks) == 2
    assert blocks[0]["id"] == "b_2"
    assert blocks[0]["order"] == 1
    assert blocks[1]["id"] == "b_1"
    assert blocks[1]["order"] == 2


@pytest.mark.asyncio
async def test_immutability_guard_rejects_locked_block_writes():
    study_version_id = "sv_locked_block"
    MOCK_STUDY_VERSIONS["study_locked_block"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "LOCKED",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    with pytest.raises(ImmutabilityViolationError):
        await create_block(
            driver=None,
            study_version_id=study_version_id,
            user_id="user1",
            change_reason="Create in locked",
            block_id="b_locked",
            properties={"block_type": "narrative", "order": 1, "text": "Locked write"},
        )


@pytest.mark.asyncio
async def test_arm_aware_soa_matrix_projection():
    study_version_id = "sv_soa_arm"
    MOCK_STUDY_VERSIONS["study_soa_arm"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Create StudyArm
    await create_study_arm(
        None,
        study_version_id,
        "user1",
        "create arm",
        "arm_tx",
        {"name": "Active Arm", "sequence": 1},
    )

    # Link applicability
    await link_arm_applicability(
        None,
        study_version_id,
        "user1",
        "link applicability",
        "arm_tx",
        "visit_1",
        "visit",
    )

    # Query projection
    proj = await get_soa_matrix_projection(None, study_version_id)
    assert "arms" in proj
    assert len(proj["arms"]) == 1
    assert proj["arms"][0]["arm_id"] == "arm_tx"
    assert proj["arms"][0]["arm_name"] == "Active Arm"


# ==========================================
# 3. Round-Trip Mapping Tests
# ==========================================


def test_usdm_block_round_trip():
    # Construct original flat projection with blocks
    study_data = {
        "study_id": "study_rt_1",
        "title": "Oncology Phase II Blocks",
        "current_version": "1.0",
        "blocks": [
            {
                "block_id": "block_narr_1",
                "block_type": "narrative",
                "order": 1,
                "title": "Introduction",
                "text": "Clinical Trial introductory description.",
            },
            {
                "block_id": "block_obj_1",
                "block_type": "objective",
                "order": 2,
                "objective_id": "obj_primary",
                "text": "To validate primary survival rates.",
            },
            {
                "block_id": "block_elig_1",
                "block_type": "eligibility",
                "order": 3,
                "criterion_id": "INC_01",
                "criterion_type": "inclusion",
                "text": "Adult patient >= 18.",
            },
        ],
    }

    # Map to USDM
    usdm_payload = map_study_to_usdm(study_data)
    assert "documentedBy" in usdm_payload
    assert "versions" in usdm_payload

    # Inverse map from USDM
    reconstructed = map_usdm_to_study(usdm_payload)
    assert "blocks" in reconstructed
    assert len(reconstructed["blocks"]) == 3

    # Check narrative block reconstruction
    narr = next(b for b in reconstructed["blocks"] if b["block_type"] == "narrative")
    assert narr["block_id"] == "block_narr_1"
    assert narr["title"] == "Introduction"
    assert narr["text"] == "Clinical Trial introductory description."

    # Check objective block reconstruction
    obj = next(b for b in reconstructed["blocks"] if b["block_type"] == "objective")
    assert obj["block_id"] == "block_obj_1"
    assert obj["text"] == "To validate primary survival rates."

    # Verify lossless roundtrip comparison
    report = compare_payloads(study_data, reconstructed)
    assert report["lossless"] is True
    assert report["material_difference_count"] == 0


# ==========================================
# 4. Lineage & Selective Propagation Tests
# ==========================================


@pytest.mark.asyncio
async def test_selective_lineage_propagation():
    study_version_id = "sv_lineage"
    MOCK_STUDY_VERSIONS["study_lineage"] = [
        {
            "id": study_version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Create SoA derived block
    await create_block(
        None,
        study_version_id,
        "user1",
        "derived block setup",
        "b_derived_1",
        {
            "block_type": "soa_derived",
            "order": 1,
            "source_entity_id": "arm_tx_1",
            "source_entity_type": "arm",
            "text": "Treatment Arm 1 details",
            "derived_from_soa": False,
        },
    )

    # Update StudyArm to trigger lineage propagation
    await create_study_arm(
        None, study_version_id, "user1", "create arm Y", "arm_tx_1", {"name": "Arm Y"}
    )
    await update_study_arm(
        None,
        study_version_id,
        "user1",
        "rename arm Y",
        "arm_tx_1",
        {"name": "Arm Y (Modified)"},
    )

    # Assert selective block was propagated and flagged derived_from_soa = True
    block = await get_block(None, study_version_id, "b_derived_1")
    assert block["derived_from_soa"] is True


# ==========================================
# 5. REST API Security & Endpoint Routing Tests
# ==========================================


def test_api_block_crud_with_rbac():
    client = TestClient(app)
    study_id = "study_api"
    version_id = "sv_api"

    # Setup StudyVersion DRAFT
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "system",
        }
    ]

    # Helper to generate HMAC headers
    def get_auth_headers(roles="sponsor_designer", change_reason="REST Action"):
        import time

        timestamp = str(time.time())
        secret = b"internal-gateway-secret-12345"
        sig = generate_gateway_signature(
            user_id="user_admin",
            roles=roles,
            timestamp=timestamp,
            secret=secret,
            change_reason=change_reason,
        )
        return {
            "X-User-Id": "user_admin",
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": sig,
            "X-Signature-Version": "2",
            "X-Change-Reason": change_reason,
        }

    # 1. Reject without permissions/roles
    resp = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/blocks",
        json={
            "id": "b_api_1",
            "block_type": "narrative",
            "order": 1,
            "properties": {"text": "A"},
        },
    )
    assert resp.status_code == 403

    # 2. Accept with authorized role and valid change reason
    headers = get_auth_headers(roles="sponsor_designer")
    resp = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/blocks",
        json={
            "id": "b_api_1",
            "block_type": "narrative",
            "order": 1,
            "properties": {"text": "A"},
        },
        headers=headers,
    )
    assert resp.status_code == 201
    assert resp.json()["id"] == "b_api_1"

    # 3. Conflict ProblemDetails response on duplicate creation
    resp = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/blocks",
        json={
            "id": "b_api_1",
            "block_type": "narrative",
            "order": 1,
            "properties": {"text": "A"},
        },
        headers=headers,
    )
    assert resp.status_code == 409

    # 4. List blocks endpoint
    resp = client.get(
        f"/api/v1/studies/{study_id}/versions/{version_id}/blocks",
        headers=headers,
    )
    assert resp.status_code == 200
    assert len(resp.json()) == 1

    # 5. Retrieve block detail
    resp = client.get(
        f"/api/v1/studies/{study_id}/versions/{version_id}/blocks/b_api_1",
        headers=headers,
    )
    assert resp.status_code == 200
    assert resp.json()["block_id"] == "b_api_1"

    # 6. Put update endpoint
    resp = client.put(
        f"/api/v1/studies/{study_id}/versions/{version_id}/blocks/b_api_1",
        json={"properties": {"text": "A (Modified)"}},
        headers=headers,
    )
    assert resp.status_code == 200
    assert resp.json()["id"] == "b_api_1"

    # 7. Delete endpoint
    resp = client.delete(
        f"/api/v1/studies/{study_id}/versions/{version_id}/blocks/b_api_1",
        headers=headers,
    )
    assert resp.status_code == 200
    assert resp.json()["id"] == "b_api_1"
