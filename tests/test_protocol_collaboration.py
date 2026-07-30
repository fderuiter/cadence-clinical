from datetime import datetime, timezone

import pytest
from fastapi.testclient import TestClient
from protocol_authoring.models import (
    SectionReviewStatus,
    SuggestionStatus,
)

from apps.designer.db import (
    MOCK_STUDY_VERSIONS,
)
from apps.designer.delta import (
    MOCK_COLLABORATION_DATA,
    MOCK_SOA_DATA,
    ConcurrentLockingError,
    ImmutabilityViolationError,
    add_comment_to_thread,
    create_block,
    create_comment_thread,
    create_suggestion,
    decide_suggestion,
    delete_block,
    get_block,
    get_comment_threads,
    get_section_status,
    get_suggestions,
    reorder_blocks,
    resolve_comment_thread,
    transition_section_status,
    update_block,
)
from apps.designer.main import app
from packages.security.signing import generate_gateway_signature


@pytest.fixture(autouse=True)
def clean_collaboration_stores():
    """Ensures a clean state of mock stores for each test."""
    MOCK_SOA_DATA.clear()
    MOCK_STUDY_VERSIONS.clear()
    MOCK_COLLABORATION_DATA["section_statuses"].clear()
    MOCK_COLLABORATION_DATA["threads"].clear()
    MOCK_COLLABORATION_DATA["suggestions"].clear()
    MOCK_COLLABORATION_DATA["transitions"].clear()


def get_auth_headers(
    user_id="user_admin",
    roles="sponsor_designer",
    change_reason="REST Collaboration Action",
):
    import time

    timestamp = str(time.time())
    secret = b"internal-gateway-secret-12345"
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


# =====================================================================
# 1. Section Review Transition State Machine & RBAC Tests
# =====================================================================


@pytest.mark.asyncio
async def test_section_review_transitions_lifecycle():
    study_id = "study_collab_1"
    section_id = "sec_rationale"

    # Setup StudyVersion DRAFT
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": study_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Verify initial status is DRAFT
    init_status = await get_section_status(None, study_id, section_id)
    assert init_status == SectionReviewStatus.DRAFT

    # Transition: DRAFT -> IN_REVIEW
    t1 = await transition_section_status(
        None,
        study_id,
        section_id,
        SectionReviewStatus.IN_REVIEW,
        actor_id="designer_1",
        actor_role="sponsor_designer",
        reason_for_change="Ready for collaborative review by medical monitors.",
    )
    assert t1.from_status == SectionReviewStatus.DRAFT
    assert t1.to_status == SectionReviewStatus.IN_REVIEW
    assert (
        await get_section_status(None, study_id, section_id)
    ) == SectionReviewStatus.IN_REVIEW

    # Transition: IN_REVIEW -> LOCKED
    t2 = await transition_section_status(
        None,
        study_id,
        section_id,
        SectionReviewStatus.LOCKED,
        actor_id="designer_1",
        actor_role="sponsor_designer",
        reason_for_change="Comments resolved. Ready for formal approval and signing.",
    )
    assert t2.from_status == SectionReviewStatus.IN_REVIEW
    assert t2.to_status == SectionReviewStatus.LOCKED

    # Transition: LOCKED -> APPROVED (captures signature manifest)
    sig_manifestation = {
        "signer_id": "pi_1",
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "signing_reason": "Approve rationale section",
    }
    t3 = await transition_section_status(
        None,
        study_id,
        section_id,
        SectionReviewStatus.APPROVED,
        actor_id="pi_1",
        actor_role="sponsor_designer",
        reason_for_change="Approve rational and design description section.",
        signature_manifestation=sig_manifestation,
    )
    assert t3.from_status == SectionReviewStatus.LOCKED
    assert t3.to_status == SectionReviewStatus.APPROVED

    # Transition: APPROVED -> DRAFT (revert/unlock with justification)
    t4 = await transition_section_status(
        None,
        study_id,
        section_id,
        SectionReviewStatus.DRAFT,
        actor_id="designer_1",
        actor_role="sponsor_designer",
        reason_for_change="Unlock section for clinical amendment updates.",
    )
    assert t4.from_status == SectionReviewStatus.APPROVED
    assert t4.to_status == SectionReviewStatus.DRAFT

    # Verify invalid transitions throw ValueError
    # Attempting to go DRAFT -> APPROVED directly should fail
    with pytest.raises(ValueError):
        await transition_section_status(
            None,
            study_id,
            section_id,
            SectionReviewStatus.APPROVED,
            actor_id="designer_1",
            actor_role="sponsor_designer",
            reason_for_change="Invalid skip transition attempt.",
        )


# =====================================================================
# 2. Block Mutation Locking & Rejection Tests
# =====================================================================


@pytest.mark.asyncio
async def test_block_mutation_locks_enforcement():
    study_id = "study_collab_lock"
    section_id = "sec_locked"

    # Setup StudyVersion
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": study_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Create block in DRAFT section (unlocked)
    b_id = await create_block(
        None,
        study_id,
        "user_1",
        "Initial add",
        "block_test",
        {
            "block_type": "narrative",
            "order": 1,
            "text": "Intro",
            "section_id": section_id,
        },
    )
    assert b_id == "block_test"

    # Mutate section status -> LOCKED
    await transition_section_status(
        None,
        study_id,
        section_id,
        SectionReviewStatus.LOCKED,
        actor_id="designer_1",
        actor_role="sponsor_designer",
        reason_for_change="Lock section for regulatory review.",
    )

    # Attempt to CREATE another block under the locked section should fail
    with pytest.raises(ImmutabilityViolationError):
        await create_block(
            None,
            study_id,
            "user_1",
            "Try write locked",
            "block_fail",
            {
                "block_type": "narrative",
                "order": 2,
                "text": "Blocked",
                "section_id": section_id,
            },
        )

    # Attempt to UPDATE existing block in the locked section should fail
    with pytest.raises(ImmutabilityViolationError):
        await update_block(
            None,
            study_id,
            "user_1",
            "Try update locked",
            "block_test",
            {"text": "Attempted edit text"},
        )

    # Attempt to DELETE existing block in the locked section should fail
    with pytest.raises(ImmutabilityViolationError):
        await delete_block(None, study_id, "user_1", "Try delete locked", "block_test")

    # Attempt to REORDER blocks containing locked sections should fail
    with pytest.raises(ImmutabilityViolationError):
        await reorder_blocks(
            None, study_id, "user_1", "Try reorder locked", ["block_test"]
        )


# =====================================================================
# 3. Block-Anchored Comment Threads & Comments
# =====================================================================


@pytest.mark.asyncio
async def test_comments_and_threads_lifecycle():
    study_id = "study_comment_1"
    section_id = "sec_comments"
    block_id = "b_anchor"

    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": study_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Create block to anchor thread to
    await create_block(
        None,
        study_id,
        "user_1",
        "Setup block",
        block_id,
        {
            "block_type": "narrative",
            "order": 1,
            "text": "Anchor Text",
            "section_id": section_id,
        },
    )

    # Create thread with initial comment
    thread = await create_comment_thread(
        None,
        study_id,
        section_id,
        block_id,
        text="Is this wording medically accurate?",
        created_by="reviewer_1",
    )
    assert thread.status == "open"
    assert thread.block_version_index == 1
    assert len(thread.comments) == 1
    assert thread.comments[0].text == "Is this wording medically accurate?"

    # List threads for section
    threads = await get_comment_threads(None, study_id, section_id)
    assert len(threads) == 1
    assert threads[0].thread_id == thread.thread_id

    # Add comment to thread
    updated_thread = await add_comment_to_thread(
        None,
        study_id,
        thread.thread_id,
        text="Yes, it matches FDA guidance exactly.",
        created_by="designer_1",
    )
    assert len(updated_thread.comments) == 2
    assert updated_thread.comments[1].text == "Yes, it matches FDA guidance exactly."

    # Resolve thread
    resolved_thread = await resolve_comment_thread(None, study_id, thread.thread_id)
    assert resolved_thread.status == "resolved"


# =====================================================================
# 4. Suggestions, Acceptance, & GxP Stale-Version Gating
# =====================================================================


@pytest.mark.asyncio
async def test_suggestions_decision_and_stale_rejection():
    study_id = "study_suggestion_1"
    section_id = "sec_suggestions"
    block_id = "b_suggest"

    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": study_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Create initial block
    await create_block(
        None,
        study_id,
        "user_1",
        "Setup block",
        block_id,
        {
            "block_type": "narrative",
            "order": 1,
            "text": "Treatment with 10mg",
            "section_id": section_id,
        },
    )

    # Propose suggestion on block version 1
    s1 = await create_suggestion(
        None,
        study_id,
        block_id,
        suggested_text="Treatment with 20mg dose",
        reason="Update protocol to match revised investigator brochure.",
        created_by="reviewer_1",
    )
    assert s1.status == SuggestionStatus.PENDING
    assert s1.block_version_index == 1

    # Fetch suggestions
    sug_list = await get_suggestions(None, study_id, block_id)
    assert len(sug_list) == 1
    assert sug_list[0].suggestion_id == s1.suggestion_id

    # Mutate the block so its version advances (making version 1 stale)
    await update_block(
        None,
        study_id,
        "designer_1",
        "Direct revision",
        block_id,
        {"text": "Treatment with 15mg"},
    )
    current_block = await get_block(None, study_id, block_id)
    assert current_block["version_index"] == 2

    # Attempt to ACCEPT suggestion s1 (which was based on version 1) should raise ConcurrentLockingError
    with pytest.raises(ConcurrentLockingError):
        await decide_suggestion(
            None,
            study_id,
            s1.suggestion_id,
            decision="accept",
            decided_by="designer_1",
            decision_reason="Approve dosage increase",
        )

    # Rejecting the suggestion should still succeed even if stale
    s1_rejected = await decide_suggestion(
        None,
        study_id,
        s1.suggestion_id,
        decision="reject",
        decided_by="designer_1",
        decision_reason="Dosage has already been modified to 15mg.",
    )
    assert s1_rejected.status == SuggestionStatus.REJECTED


# =====================================================================
# 5. REST API Controller / Gateway Routing Tests
# =====================================================================


def test_api_section_collaboration_gates():
    client = TestClient(app)
    study_id = "study_api_collab"
    section_id = "sec_api"
    block_id = "b_api"

    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": study_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "system",
        }
    ]

    # Create block in mock stores
    MOCK_SOA_DATA[study_id] = {
        "blocks": {
            block_id: {
                "id": block_id,
                "block_id": block_id,
                "version_index": 1,
                "created_by": "system",
                "created_at": datetime.now(timezone.utc).isoformat(),
                "reason_for_change": "Init",
                "is_deleted": False,
                "block_type": "narrative",
                "order": 1,
                "text": "API Initial Text",
                "section_id": section_id,
            }
        },
        "arms": {},
        "epochs": {},
        "visits": {},
        "procedures": {},
        "forms": {},
        "timing_windows": {},
        "links": [],
        "actions": [],
    }

    # 1. Transition: Unauthenticated request should fail with 403
    resp = client.post(
        f"/api/v1/studies/{study_id}/sections/{section_id}/transition",
        json={
            "to_status": "IN_REVIEW",
            "reason_for_change": "Collaborative study review.",
        },
    )
    assert resp.status_code == 403

    # 2. Transition: Valid Sponsor Designer credentials
    headers = get_auth_headers(roles="sponsor_designer")
    resp = client.post(
        f"/api/v1/studies/{study_id}/sections/{section_id}/transition",
        json={
            "to_status": "IN_REVIEW",
            "reason_for_change": "Collaborative study review rationale.",
        },
        headers=headers,
    )
    assert resp.status_code == 200
    assert resp.json()["to_status"] == "IN_REVIEW"

    # 3. Transition: GxP change reason validation (reject short reason)
    resp = client.post(
        f"/api/v1/studies/{study_id}/sections/{section_id}/transition",
        json={"to_status": "LOCKED", "reason_for_change": "Too short"},
        headers=headers,
    )
    assert resp.status_code == 400
    assert "at least 10 characters" in resp.json()["detail"]

    # 4. Comments: Create thread
    resp = client.post(
        f"/api/v1/studies/{study_id}/sections/{section_id}/threads",
        json={"block_id": block_id, "text": "Is this text fine?"},
        headers=headers,
    )
    assert resp.status_code == 201
    thread_id = resp.json()["thread_id"]

    # 5. Comments: Add reply comment
    resp = client.post(
        f"/api/v1/studies/{study_id}/threads/{thread_id}/comments",
        json={"text": "Yes, completely fine."},
        headers=headers,
    )
    assert resp.status_code == 201
    assert len(resp.json()["comments"]) == 2

    # 6. Suggestions: Create a suggestion
    resp = client.post(
        f"/api/v1/studies/{study_id}/blocks/{block_id}/suggestions",
        json={"suggested_text": "Updated API Text", "reason": "Better clarity"},
        headers=headers,
    )
    assert resp.status_code == 201
    suggestion_id = resp.json()["suggestion_id"]

    # 7. Suggestions: Decision - Accept suggestions
    resp = client.post(
        f"/api/v1/studies/{study_id}/suggestions/{suggestion_id}/decision",
        json={"decision": "accept", "decision_reason": "Better wording chosen"},
        headers=headers,
    )
    assert resp.status_code == 200
    assert resp.json()["status"] == "accepted"

    # Verify that the block's text was updated in the DB
    updated_block = MOCK_SOA_DATA[study_id]["blocks"][block_id]
    assert updated_block["text"] == "Updated API Text"
    assert updated_block["version_index"] == 2
