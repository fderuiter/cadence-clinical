"""Comprehensive Unit & Integration Test Suite for Sovereign Site Execution Gatekeeper.

Requirements:
- PRD-SYS-121: Sovereign Site Execution Gatekeeper Architecture
- PRD-SSU-003: Country-Aware Greenlight Site Activation Token Gating
- PRD-TRN-002: Protocol Amendment GCP Training EDC Write-Gating
- PRD-SYS-001: Delegation of Authority (DOA) Log Verification & GxP Role Authorization
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- ADR-2202: Architecture Decision Record for Greenlight Execution Gating
- ICH GCP E6(R2) Section 4 & 8, 21 CFR Part 11
"""

import os
import time
from collections.abc import AsyncGenerator
from typing import Any

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base as ExecutionBase,
)
from apps.execution.domain.doa_models import (
    DOATaskDelegationEnum,
    DOATaskRoleEnum,
)
from apps.execution.greenlight_gating import (
    GreenlightVerificationClient,
    IGreenlightVerificationClient,
    set_greenlight_verification_client,
)
from apps.execution.main import app
from apps.execution.services.doa_service import (
    DOAService,
    get_global_doa_service,
    set_global_doa_service,
)
from apps.execution.site_execution_gatekeeper import (
    GatekeeperRejectionReason,
    SiteExecutionGatekeeper,
    SiteNotEligibleForMutationError,
    get_site_execution_gatekeeper,
    require_site_mutation_authorization,
    set_site_execution_gatekeeper,
)
from apps.execution.training_gating import (
    ITrainingVerificationClient,
    set_training_verification_client,
)
from packages.security.signing import (
    generate_gateway_signature,
    generate_greenlight_token,
)

pytestmark = pytest.mark.xdist_group("site_execution_gatekeeper")


def _make_site_auth_headers(
    user_id: str = "crc_e2e_lead",
    roles: str = "crc",
    change_reason: str = "Clinical execution e2e gatekeeper validation",
    site_id: str | None = None,
    study_id: str | None = None,
) -> dict[str, str]:
    timestamp = str(time.time())
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
        "utf-8"
    )
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id=site_id,
        tenant_id="tenant_default",
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }
    if site_id:
        headers["X-Site-Id"] = site_id
        headers["X-Assigned-Sites"] = site_id
    if study_id:
        headers["X-Assigned-Studies"] = study_id
    return headers


client = TestClient(app)


class MockGreenlightClient(IGreenlightVerificationClient):
    """Configurable mock for Greenlight CTMS startup verification."""

    def __init__(self, overall_status: str = "APPROVED") -> None:
        self.overall_status = overall_status
        self.missing_facets: list[str] = []

    async def get_greenlight_status(
        self, study_id: str, site_id: str
    ) -> dict[str, Any]:
        is_approved = self.overall_status in ("APPROVED", "OVERRIDDEN")
        return {
            "study_id": study_id,
            "site_id": site_id,
            "overall_status": self.overall_status,
            "is_greenlighted": is_approved,
            "facet_regulatory_irb": is_approved,
            "facet_cta_budget": is_approved,
            "facet_eisf_baseline": is_approved,
            "facet_doa_log": is_approved,
            "facet_staff_training": is_approved,
            "facet_ip_release": is_approved,
            "evaluation_details": {
                "facets": {
                    "regulatory_irb": {
                        "passed": is_approved,
                        "missing": self.missing_facets if not is_approved else [],
                    }
                }
            },
        }


class MockTrainingClient(ITrainingVerificationClient):
    """Configurable mock for Knowledge GCP training qualification."""

    def __init__(self) -> None:
        self.qualified_users: set[tuple[str, str, str]] = set()
        self.pending_curricula_map: dict[tuple[str, str], list[dict[str, Any]]] = {}

    def set_curriculum_mandatory(
        self, study_id: str, protocol_version: str, title: str
    ) -> None:
        self.pending_curricula_map[(study_id, protocol_version)] = [
            {"id": "curr_gk_01", "title": title, "is_mandatory": True}
        ]

    def mark_user_qualified(
        self, user_id: str, study_id: str, protocol_version: str
    ) -> None:
        self.qualified_users.add((user_id, study_id, protocol_version))

    async def get_qualification_status(
        self,
        user_id: str,
        study_id: str,
        protocol_version: str,
        site_id: str | None = None,
        user_role: str | None = None,
    ) -> dict[str, Any]:
        pending = self.pending_curricula_map.get((study_id, protocol_version), [])
        if not pending:
            return {"is_qualified": True, "pending_curricula": []}

        is_qual = (user_id, study_id, protocol_version) in self.qualified_users
        return {
            "is_qualified": is_qual,
            "pending_curricula": [] if is_qual else pending,
        }


@pytest_asyncio.fixture(autouse=True)
async def setup_gatekeeper_env() -> AsyncGenerator[None]:
    """Isolate DB, DOAService, Greenlight Client, and Training Client for each test."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(ExecutionBase.metadata.create_all)

    fresh_doa_service = DOAService()
    set_global_doa_service(fresh_doa_service)

    mock_greenlight = MockGreenlightClient(overall_status="APPROVED")
    set_greenlight_verification_client(mock_greenlight)

    mock_training = MockTrainingClient()
    set_training_verification_client(mock_training)

    gatekeeper = SiteExecutionGatekeeper(
        greenlight_client=mock_greenlight,
        training_client=mock_training,
        doa_service=fresh_doa_service,
        strict_doa=False,
    )
    set_site_execution_gatekeeper(gatekeeper)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(ExecutionBase.metadata.drop_all)
    await db_manager.close()

    set_greenlight_verification_client(GreenlightVerificationClient())
    set_site_execution_gatekeeper(SiteExecutionGatekeeper())


@pytest.mark.asyncio
async def test_gatekeeper_allows_fully_authorized_and_greenlighted_site() -> None:
    """Validate that fully qualified, greenlight certified, and DOA authorized operations pass smoothly.

    @req:PRD-SYS-121, PRD-SSU-003, PRD-TRN-002, PRD-SYS-001, ADR-2199, ADR-2202
    """
    study_id = "STUDY-GK-01"
    site_id = "SITE-GK-01"
    user_id = "crc_sarah_connor"
    proto_ver = "1.0.0"

    doa = get_global_doa_service()
    rec = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Sarah Connor",
        personnel_email=user_id,
        role=DOATaskRoleEnum.CLINICAL_RESEARCH_COORDINATOR,
        delegated_tasks=[
            DOATaskDelegationEnum.SUBJECT_INFORMED_CONSENT,
            DOATaskDelegationEnum.CRF_DATA_ENTRY,
        ],
        start_date="2026-08-01",
    )
    doa.sign_off_assignment(rec.record_id, "pi_dr_house", "Initial DOA signoff")

    gk = get_site_execution_gatekeeper()
    decision = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
        protocol_version=proto_ver,
    )

    assert decision.is_eligible is True
    assert len(decision.rejection_reasons) == 0
    assert decision.site_id == site_id
    assert decision.user_id == user_id
    assert decision.operation_type == "SUBJECT_CREATION"

    # Dependency check does not raise
    auth_decision = await require_site_mutation_authorization(
        site_id=site_id,
        user_id=user_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
        protocol_version=proto_ver,
    )
    assert auth_decision.is_eligible is True


@pytest.mark.asyncio
async def test_gatekeeper_blocks_non_greenlight_site() -> None:
    """Validate that unapproved greenlight status blocks site mutation with structured SITE_NOT_GREENLIGHTED rejection.

    @req:PRD-SYS-121, PRD-SSU-003, ADR-2202
    """
    study_id = "STUDY-GK-02"
    site_id = "SITE-GK-02"
    user_id = "crc_pending_site"

    # Mock greenlight as PENDING
    mock_gl = MockGreenlightClient(overall_status="PENDING")
    mock_gl.missing_facets = ["IRB Approval Protocol v1.0", "Executed CTA"]

    gk = SiteExecutionGatekeeper(
        greenlight_client=mock_gl,
        doa_service=get_global_doa_service(),
    )

    decision = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
    )

    assert decision.is_eligible is False
    assert GatekeeperRejectionReason.SITE_NOT_GREENLIGHTED in decision.rejection_reasons
    assert "greenlight" in decision.rejection_details
    assert decision.rejection_details["greenlight"]["overall_status"] == "PENDING"
    assert (
        "IRB Approval Protocol v1.0"
        in decision.rejection_details["greenlight"]["missing_facets"]
    )

    # Validate exception
    with pytest.raises(SiteNotEligibleForMutationError) as exc_info:
        await require_site_mutation_authorization(
            site_id=site_id,
            user_id=user_id,
            operation_type="SUBJECT_CREATION",
            study_id=study_id,
            gatekeeper=gk,
        )
    err = exc_info.value
    assert err.status_code == 403
    assert err.detail["error"] == "SITE_NOT_ELIGIBLE_FOR_MUTATION"
    assert "SITE_NOT_GREENLIGHTED" in err.detail["rejection_reasons"]
    assert err.detail["site_id"] == site_id


@pytest.mark.asyncio
async def test_gatekeeper_blocks_untrained_user_for_protocol_version() -> None:
    """Validate that incomplete Protocol GCP training blocks site mutation with structured TRAINING_REQUIRED reason.

    @req:PRD-SYS-121, PRD-TRN-002, ADR-2199
    """
    study_id = "STUDY-GK-03"
    site_id = "SITE-GK-03"
    user_id = "investigator_untrained"
    proto_ver = "2.0.0"

    mock_training = MockTrainingClient()
    mock_training.set_curriculum_mandatory(
        study_id=study_id,
        protocol_version=proto_ver,
        title="Protocol Amendment v2.0 Biomarker Protocol",
    )

    gk = SiteExecutionGatekeeper(
        training_client=mock_training,
        doa_service=get_global_doa_service(),
    )

    decision = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_id,
        operation_type="OBSERVATION_CREATION",
        study_id=study_id,
        protocol_version=proto_ver,
    )

    assert decision.is_eligible is False
    assert GatekeeperRejectionReason.TRAINING_REQUIRED in decision.rejection_reasons
    assert "training" in decision.rejection_details
    assert decision.rejection_details["training"]["protocol_version"] == proto_ver

    with pytest.raises(SiteNotEligibleForMutationError) as exc_info:
        await require_site_mutation_authorization(
            site_id=site_id,
            user_id=user_id,
            operation_type="OBSERVATION_CREATION",
            study_id=study_id,
            protocol_version=proto_ver,
            gatekeeper=gk,
        )
    err = exc_info.value
    assert err.status_code == 403
    assert "TRAINING_REQUIRED" in err.detail["rejection_reasons"]


@pytest.mark.asyncio
async def test_gatekeeper_blocks_unauthorized_or_unsigned_doa_record() -> None:
    """Validate that missing or unsigned Delegation of Authority log records block site mutations with UNAUTHORIZED_DOA_ROLE.

    @req:PRD-SYS-121, PRD-SYS-001, ADR-2199
    """
    study_id = "STUDY-GK-04"
    site_id = "SITE-GK-04"
    user_unregistered = "unknown_nurse@site.org"
    user_pending_signoff = "nurse_pending@site.org"

    doa = get_global_doa_service()
    # Register pending record (not signed off by PI)
    rec = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Nurse Pending",
        personnel_email=user_pending_signoff,
        role=DOATaskRoleEnum.STUDY_NURSE,
        delegated_tasks=[DOATaskDelegationEnum.PHYSICAL_EXAMINATION],
        start_date="2026-08-01",
    )
    assert rec.signed_off is False

    gk = get_site_execution_gatekeeper()

    # 1. Unregistered user -> UNAUTHORIZED_DOA_ROLE
    dec_unregistered = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_unregistered,
        operation_type="OBSERVATION_CREATION",
        study_id=study_id,
    )
    assert dec_unregistered.is_eligible is False
    assert (
        GatekeeperRejectionReason.UNAUTHORIZED_DOA_ROLE
        in dec_unregistered.rejection_reasons
    )

    # 2. Unsigned record -> UNAUTHORIZED_DOA_ROLE
    dec_pending = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_pending_signoff,
        operation_type="OBSERVATION_CREATION",
        study_id=study_id,
    )
    assert dec_pending.is_eligible is False
    assert (
        GatekeeperRejectionReason.UNAUTHORIZED_DOA_ROLE in dec_pending.rejection_reasons
    )
    assert "pending PI sign-off" in dec_pending.rejection_details["doa"]["reason"]

    # 3. After PI signs off, nurse is permitted for OBSERVATION_CREATION
    doa.sign_off_assignment(rec.record_id, "pi_lead", "Approved nurse delegation")
    dec_approved = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_pending_signoff,
        operation_type="OBSERVATION_CREATION",
        study_id=study_id,
    )
    assert dec_approved.is_eligible is True


@pytest.mark.asyncio
async def test_gatekeeper_blocks_mismatched_doa_task_delegation() -> None:
    """Validate that site staff attempting operations outside their delegated task scope are blocked.

    @req:PRD-SYS-121, PRD-SYS-001, ADR-2199
    """
    study_id = "STUDY-GK-05"
    site_id = "SITE-GK-05"
    user_id = "lab_coordinator@site.org"

    doa = get_global_doa_service()
    rec = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Lab Coordinator",
        personnel_email=user_id,
        role=DOATaskRoleEnum.STUDY_NURSE,
        delegated_tasks=[DOATaskDelegationEnum.PHYSICAL_EXAMINATION],
        start_date="2026-08-01",
    )
    doa.sign_off_assignment(rec.record_id, "pi_lead", "Approved lab nurse")

    gk = get_site_execution_gatekeeper()

    # Attempting PI Signoff with only Physical Exam delegation -> Blocked
    decision = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_id,
        operation_type="PI_SIGNOFF",
        study_id=study_id,
    )
    assert decision.is_eligible is False
    assert GatekeeperRejectionReason.UNAUTHORIZED_DOA_ROLE in decision.rejection_reasons


@pytest.mark.asyncio
async def test_gatekeeper_allows_principal_investigator_universal_authority() -> None:
    """Validate that Principal Investigators have universal site mutation authority once signed off.

    @req:PRD-SYS-121, PRD-SYS-001, ADR-2199
    """
    study_id = "STUDY-GK-06"
    site_id = "SITE-GK-06"
    pi_user_id = "dr_gregory_house"

    doa = get_global_doa_service()
    rec = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Dr. Gregory House",
        personnel_email=pi_user_id,
        role=DOATaskRoleEnum.PRINCIPAL_INVESTIGATOR,
        delegated_tasks=[
            DOATaskDelegationEnum.PI_CASEBOOK_SIGNOFF,
            DOATaskDelegationEnum.SUBJECT_INFORMED_CONSENT,
            DOATaskDelegationEnum.CRF_DATA_ENTRY,
            DOATaskDelegationEnum.PHYSICAL_EXAMINATION,
            DOATaskDelegationEnum.AE_SAE_REPORTING,
        ],
        start_date="2026-08-01",
    )
    doa.sign_off_assignment(rec.record_id, pi_user_id, "PI Self-Endorsement")

    gk = get_site_execution_gatekeeper()

    for op in [
        "SUBJECT_CREATION",
        "OBSERVATION_CREATION",
        "RANDOMIZATION",
        "TRANSFER_ORDER",
        "PI_SIGNOFF",
    ]:
        decision = await gk.validate_mutation_eligibility(
            site_id=site_id,
            user_id=pi_user_id,
            operation_type=op,
            study_id=study_id,
        )
        assert decision.is_eligible is True, f"PI should be authorized for {op}"


@pytest.mark.asyncio
async def test_gatekeeper_handles_multi_pillar_simultaneous_rejections() -> None:
    """Validate structured reporting when greenlight, training, and DOA all fail simultaneously.

    @req:PRD-SYS-121, PRD-SSU-003, PRD-TRN-002, PRD-SYS-001, ADR-2199, ADR-2202
    """
    study_id = "STUDY-GK-07"
    site_id = "SITE-GK-07"
    user_id = "unauthorized_untrained_personnel"
    proto_ver = "3.0.0"

    mock_gl = MockGreenlightClient(overall_status="PENDING")
    mock_gl.missing_facets = ["Missing Form 1572"]

    mock_training = MockTrainingClient()
    mock_training.set_curriculum_mandatory(
        study_id=study_id,
        protocol_version=proto_ver,
        title="Complex Adaptive Design Protocol Training",
    )

    # DOA has records for site, but none for this user
    doa = DOAService()
    r = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Legit CRC",
        personnel_email="crc_legit@site.org",
        role=DOATaskRoleEnum.CLINICAL_RESEARCH_COORDINATOR,
        delegated_tasks=[DOATaskDelegationEnum.CRF_DATA_ENTRY],
        start_date="2026-08-01",
    )
    doa.sign_off_assignment(r.record_id, "pi_main", "Signoff")

    gk = SiteExecutionGatekeeper(
        greenlight_client=mock_gl,
        training_client=mock_training,
        doa_service=doa,
    )

    decision = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
        protocol_version=proto_ver,
    )

    assert decision.is_eligible is False
    assert len(decision.rejection_reasons) == 3
    assert GatekeeperRejectionReason.SITE_NOT_GREENLIGHTED in decision.rejection_reasons
    assert GatekeeperRejectionReason.TRAINING_REQUIRED in decision.rejection_reasons
    assert GatekeeperRejectionReason.UNAUTHORIZED_DOA_ROLE in decision.rejection_reasons
    assert "greenlight" in decision.rejection_details
    assert "training" in decision.rejection_details
    assert "doa" in decision.rejection_details


@pytest.mark.asyncio
async def test_gatekeeper_cryptographic_greenlight_token_fast_path() -> None:
    """Validate that a valid HMAC greenlight token bypasses network CTMS checks even if CTMS returns PENDING.

    @req:PRD-SYS-121, PRD-SSU-003, ADR-2202, 21 CFR Part 11
    """
    study_id = "STUDY-GK-08"
    site_id = "SITE-GK-08"
    user_id = "token_bearer_user"

    valid_token = generate_greenlight_token(
        site_id=site_id,
        study_id=study_id,
        country_code="US",
        certified_by="qa_director",
    )

    # Greenlight client returns PENDING, but token will override it
    mock_gl = MockGreenlightClient(overall_status="PENDING")
    gk = SiteExecutionGatekeeper(
        greenlight_client=mock_gl,
        doa_service=get_global_doa_service(),
    )

    decision = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
        token=valid_token,
    )

    assert decision.is_eligible is True
    assert (
        GatekeeperRejectionReason.SITE_NOT_GREENLIGHTED
        not in decision.rejection_reasons
    )


@pytest.mark.asyncio
async def test_gatekeeper_ctms_network_timeout_fails_closed() -> None:
    """Validate that CTMS network timeouts fail closed with is_eligible=False and SITE_NOT_GREENLIGHTED.

    @req:PRD-SYS-121, PRD-SSU-003, ADR-2202
    """
    study_id = "STUDY-GK-TIMEOUT"
    site_id = "SITE-GK-TIMEOUT"
    user_id = "crc_timeout_user"

    class TimeoutGreenlightClient(IGreenlightVerificationClient):
        async def get_greenlight_status(
            self, study_id: str, site_id: str
        ) -> dict[str, Any]:
            raise httpx.TimeoutException("CTMS service request timed out after 3.0s")

    gk = SiteExecutionGatekeeper(
        greenlight_client=TimeoutGreenlightClient(),
        doa_service=get_global_doa_service(),
    )

    decision = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=user_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
    )

    assert decision.is_eligible is False
    assert GatekeeperRejectionReason.SITE_NOT_GREENLIGHTED in decision.rejection_reasons
    assert "greenlight" in decision.rejection_details
    assert decision.rejection_details["greenlight"]["overall_status"] == "TIMEOUT"
    assert (
        "CTMS greenlight status verification request failed or timed out"
        in decision.rejection_details["greenlight"]["missing_facets"]
    )

    with pytest.raises(SiteNotEligibleForMutationError) as exc_info:
        await require_site_mutation_authorization(
            site_id=site_id,
            user_id=user_id,
            operation_type="SUBJECT_CREATION",
            study_id=study_id,
            gatekeeper=gk,
        )
    err = exc_info.value
    assert err.status_code == 403
    assert err.detail["error"] == "SITE_NOT_ELIGIBLE_FOR_MUTATION"
    assert "SITE_NOT_GREENLIGHTED" in err.detail["rejection_reasons"]


@pytest.mark.asyncio
async def test_gatekeeper_rejects_empty_or_missing_site_id() -> None:
    """Validate that requests with empty or missing site_id are rejected (is_eligible=False).

    @req:PRD-SYS-121, PRD-SYS-001
    """
    study_id = "STUDY-GK-NOSITE"
    user_id = "crc_nosite_user"

    gk = get_site_execution_gatekeeper()

    for invalid_site_id in ["", "   ", None]:
        decision = await gk.validate_mutation_eligibility(
            site_id=invalid_site_id,  # type: ignore[arg-type]
            user_id=user_id,
            operation_type="SUBJECT_CREATION",
            study_id=study_id,
        )

        assert decision.is_eligible is False
        assert GatekeeperRejectionReason.MISSING_SITE_ID in decision.rejection_reasons
        assert "site" in decision.rejection_details

        with pytest.raises(SiteNotEligibleForMutationError) as exc_info:
            await require_site_mutation_authorization(
                site_id=invalid_site_id,  # type: ignore[arg-type]
                user_id=user_id,
                operation_type="SUBJECT_CREATION",
                study_id=study_id,
                gatekeeper=gk,
            )
        err = exc_info.value
        assert err.status_code == 403
        assert "MISSING_SITE_ID" in err.detail["rejection_reasons"]


@pytest.mark.asyncio
async def test_gatekeeper_service_accounts_undergo_verified_delegation_log_checks() -> (
    None
):
    """Validate that service accounts undergo verified delegation log checks and are blocked if unassigned or unauthorized.

    @req:PRD-SYS-121, PRD-SYS-001, ADR-2199
    """
    study_id = "STUDY-GK-SA"
    site_id = "SITE-GK-SA"
    service_account_id = "execution-service"

    doa = get_global_doa_service()

    # Register DOA log for site with another user (so site has DOA records)
    rec_pi = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Dr. PI",
        personnel_email="dr_pi@site.org",
        role=DOATaskRoleEnum.PRINCIPAL_INVESTIGATOR,
        delegated_tasks=[DOATaskDelegationEnum.PI_CASEBOOK_SIGNOFF],
        start_date="2026-08-01",
    )
    doa.sign_off_assignment(rec_pi.record_id, "dr_pi@site.org", "PI Signoff")

    gk = SiteExecutionGatekeeper(
        greenlight_client=MockGreenlightClient("APPROVED"),
        doa_service=doa,
    )

    # 1. Service account NOT in DOA log -> Blocked
    dec_unassigned = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=service_account_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
    )
    assert dec_unassigned.is_eligible is False
    assert (
        GatekeeperRejectionReason.UNAUTHORIZED_DOA_ROLE
        in dec_unassigned.rejection_reasons
    )

    # 2. Add service account to DOA log but pending sign-off -> Blocked
    rec_sa = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Execution Service Bot",
        personnel_email=service_account_id,
        role=DOATaskRoleEnum.CLINICAL_RESEARCH_COORDINATOR,
        delegated_tasks=[DOATaskDelegationEnum.CRF_DATA_ENTRY],
        start_date="2026-08-01",
    )
    dec_pending_sa = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=service_account_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
    )
    assert dec_pending_sa.is_eligible is False
    assert (
        GatekeeperRejectionReason.UNAUTHORIZED_DOA_ROLE
        in dec_pending_sa.rejection_reasons
    )

    # 3. Sign off service account DOA assignment -> Authorized
    doa.sign_off_assignment(rec_sa.record_id, "dr_pi@site.org", "Approved Service Bot")
    dec_authorized_sa = await gk.validate_mutation_eligibility(
        site_id=site_id,
        user_id=service_account_id,
        operation_type="SUBJECT_CREATION",
        study_id=study_id,
    )
    assert dec_authorized_sa.is_eligible is True


@pytest.mark.asyncio
async def test_gatekeeper_system_service_account_bypass() -> None:
    """Validate that automated system service accounts bypass user training checks.

    @req:PRD-SYS-121, ADR-2199
    """
    study_id = "STUDY-GK-09"
    site_id = "SITE-GK-09"

    for sys_user in ["system", "execution-service", "system_admin", "admin"]:
        auth = await require_site_mutation_authorization(
            site_id=site_id,
            user_id=sys_user,
            operation_type="OBSERVATION_CREATION",
            study_id=study_id,
        )
        assert auth.is_eligible is True


@pytest.mark.asyncio
async def test_fastapi_endpoints_integrate_with_consolidated_gatekeeper() -> None:
    """End-to-end integration test verifying subjects, observations, and transfer-orders endpoints use consolidated gatekeeper.

    @req:PRD-SYS-121, PRD-SSU-003, PRD-TRN-002, PRD-SYS-001, ADR-2199, ADR-2202
    """
    study_id = "STUDY-E2E-01"
    site_id = "SITE-E2E-01"
    user_id = "crc_e2e_lead"

    # 1. Setup active approved DOA record for user
    doa = get_global_doa_service()
    greenlight = MockGreenlightClient("APPROVED")
    training = MockTrainingClient()
    training.mark_user_qualified(user_id, study_id, "1.0")
    training.mark_user_qualified("cra_e2e_lead", study_id, "1.0")
    gk = SiteExecutionGatekeeper(
        doa_service=doa,
        greenlight_client=greenlight,
        training_client=training,
    )
    set_site_execution_gatekeeper(gk)

    rec = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="CRC E2E Lead",
        personnel_email=user_id,
        role=DOATaskRoleEnum.CLINICAL_RESEARCH_COORDINATOR,
        delegated_tasks=[
            DOATaskDelegationEnum.SUBJECT_INFORMED_CONSENT,
            DOATaskDelegationEnum.CRF_DATA_ENTRY,
            DOATaskDelegationEnum.PHYSICAL_EXAMINATION,
        ],
        start_date="2026-08-01",
    )
    doa.sign_off_assignment(rec.record_id, "pi_main", "E2E Delegation Signoff")

    headers = _make_site_auth_headers(
        user_id=user_id,
        roles="crc",
        change_reason="Clinical execution e2e gatekeeper validation",
        site_id=site_id,
    )
    headers["X-Assigned-Studies"] = study_id
    headers["X-Assigned-Sites"] = site_id

    # 2. Create Subject -> Success (200 OK)
    resp_subj = client.post(
        "/api/v1/execution/subjects",
        json={
            "subject_id": "SUB-E2E-101",
            "study_id": study_id,
            "site_id": site_id,
            "arm_id": "ARM-A",
        },
        headers=headers,
    )
    assert resp_subj.status_code == 200
    assert resp_subj.json()["subject_id"] == "SUB-E2E-101"

    # 3. Create Observation -> Success (200 OK)
    resp_obs = client.post(
        "/api/v1/execution/observations",
        json={
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": "SUB-E2E-101",
            "domain": "VS",
            "test_code": "SYSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 120.0,
            "unit": "mmHg",
        },
        headers=headers,
    )
    assert resp_obs.status_code == 200
    assert resp_obs.json()["value"] == 120.0

    # 4. Create Transfer Order -> Success (201 Created)
    cra_user = "cra_e2e_lead"
    rec_cra = doa.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="CRA E2E Lead",
        personnel_email=cra_user,
        role=DOATaskRoleEnum.CLINICAL_RESEARCH_COORDINATOR,
        delegated_tasks=[DOATaskDelegationEnum.CRF_DATA_ENTRY],
        start_date="2026-08-01",
    )
    doa.sign_off_assignment(rec_cra.record_id, "pi_main", "CRA Delegation")

    cra_headers = _make_site_auth_headers(
        user_id=cra_user,
        roles="cra",
        change_reason="CRA Transfer order creation",
        site_id=site_id,
        study_id=study_id,
    )

    resp_to = client.post(
        "/api/v1/execution/rtsm/transfer-orders",
        json={
            "study_id": study_id,
            "source_node_id": "DEPOT-01",
            "destination_node_id": site_id,
            "kit_type": "DRUG_A",
            "quantity": 10,
            "priority": "ROUTINE",
            "temperature_condition": "AMBIENT",
            "carrier": "FedEx Clinical",
            "transfer_order_number": "TO-E2E-999",
        },
        headers=cra_headers,
    )
    assert resp_to.status_code == 201
    assert resp_to.json()["transfer_order_number"] == "TO-E2E-999"
