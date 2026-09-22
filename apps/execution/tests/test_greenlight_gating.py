"""Test suite for EDC & RTSM Programmatic Site Greenlight Activation Gating.

Requirements:
- PRD-SYS-183: Greenlight Site Activation Multi-Factor Engine Tracer
- PRD-SSU-003: Cryptographic Gating Token & Programmatic Execution/RTSM Gating
- ADR-2202: Architecture Decision Record for Greenlight Execution Gating
- ICH GCP E6(R2) Section 8 & 21 CFR Part 11
"""

import pytest

from apps.execution.greenlight_gating import (
    GreenlightVerificationClient,
    IGreenlightVerificationClient,
    SiteNotGreenlightedError,
    enforce_site_greenlight_gate,
    set_greenlight_verification_client,
)
from apps.execution.site_execution_gatekeeper import (
    GatekeeperRejectionReason,
    SiteExecutionGatekeeper,
    SiteNotEligibleForMutationError,
    require_site_mutation_authorization,
    set_site_execution_gatekeeper,
)
from packages.security.signing import generate_greenlight_token


@pytest.fixture(autouse=True)
def reset_gatekeeper_clients():
    """Ensure gatekeeper and greenlight verification clients are reset before and after each test."""
    set_greenlight_verification_client(GreenlightVerificationClient())
    set_site_execution_gatekeeper(SiteExecutionGatekeeper())
    yield
    set_greenlight_verification_client(GreenlightVerificationClient())
    set_site_execution_gatekeeper(SiteExecutionGatekeeper())


@pytest.mark.asyncio
async def test_execution_gating_blocks_non_greenlight_site():
    """Validate that enforce_site_greenlight_gate raises SiteNotGreenlightedError (HTTP 403) for non-greenlight sites.

    @req:PRD-SYS-183, PRD-SSU-003, ADR-2202, ICH GCP E6(R2) Section 8
    """
    study_id = "STUDY-GATE-101"
    site_id = "SITE-GATE-101"

    class MockPendingGreenlightClient(IGreenlightVerificationClient):
        async def get_greenlight_status(self, study_id: str, site_id: str) -> dict:
            return {
                "study_id": study_id,
                "site_id": site_id,
                "overall_status": "PENDING",
                "facet_regulatory_irb": False,
                "facet_cta_budget": False,
                "facet_eisf_baseline": False,
                "facet_doa_log": False,
                "facet_staff_training": False,
                "facet_ip_release": False,
                "evaluation_details": {
                    "facets": {
                        "regulatory_irb": {
                            "passed": False,
                            "missing": ["Form FDA 1572 Statement of Investigator"],
                        },
                        "cta_budget": {
                            "passed": False,
                            "missing": ["Executed CTA"],
                        },
                    }
                },
            }

    mock_client = MockPendingGreenlightClient()
    set_greenlight_verification_client(mock_client)

    with pytest.raises(SiteNotGreenlightedError) as exc_info:
        await enforce_site_greenlight_gate(
            study_id=study_id,
            site_id=site_id,
            greenlight_client=mock_client,
        )

    err = exc_info.value
    assert err.status_code == 403
    assert err.detail["error"] == "SITE_NOT_GREENLIGHTED"
    assert err.detail["site_id"] == site_id
    assert err.detail["study_id"] == study_id
    assert "Form FDA 1572" in str(err.detail["missing_facets"])


@pytest.mark.asyncio
async def test_execution_gating_ungates_approved_and_overridden_sites():
    """Validate that greenlight certified (APPROVED) and executive overridden (OVERRIDDEN) sites pass un-gated.

    @req:PRD-SYS-183, PRD-SSU-003, PRD-SSU-002, ADR-2202
    """
    study_id = "STUDY-GATE-102"
    site_id = "SITE-GATE-102"

    class MockApprovedGreenlightClient(IGreenlightVerificationClient):
        def __init__(self, status="APPROVED"):
            self.status = status

        async def get_greenlight_status(self, study_id: str, site_id: str) -> dict:
            return {
                "study_id": study_id,
                "site_id": site_id,
                "overall_status": self.status,
                "facet_regulatory_irb": True,
                "facet_cta_budget": True,
                "facet_eisf_baseline": True,
                "facet_doa_log": True,
                "facet_staff_training": True,
                "facet_ip_release": True,
            }

    # 1. Test APPROVED site
    approved_client = MockApprovedGreenlightClient("APPROVED")
    await enforce_site_greenlight_gate(
        study_id=study_id,
        site_id=site_id,
        greenlight_client=approved_client,
    )

    # 2. Test OVERRIDDEN site
    overridden_client = MockApprovedGreenlightClient("OVERRIDDEN")
    await enforce_site_greenlight_gate(
        study_id=study_id,
        site_id=site_id,
        greenlight_client=overridden_client,
    )


@pytest.mark.asyncio
async def test_execution_gating_cryptographic_token_bypass():
    """Validate fast-path HMAC-SHA256 token verification bypasses network checks.

    @req:PRD-SYS-183, PRD-SSU-003, ADR-2202, 21 CFR Part 11
    """
    study_id = "STUDY-GATE-103"
    site_id = "SITE-GATE-103"

    valid_token = generate_greenlight_token(
        site_id=site_id,
        study_id=study_id,
        country_code="US",
        certified_by="reg_lead_qa",
    )

    # Even if client would raise or return PENDING, valid token un-gates operations
    class ExplodingClient(IGreenlightVerificationClient):
        async def get_greenlight_status(self, study_id: str, site_id: str) -> dict:
            raise RuntimeError("Network should not be called when token is valid")

    exploding_client = ExplodingClient()
    await enforce_site_greenlight_gate(
        study_id=study_id,
        site_id=site_id,
        greenlight_token=valid_token,
        greenlight_client=exploding_client,
    )


@pytest.mark.asyncio
async def test_randomization_gating_blocks_non_greenlight_site():
    """Validate that subject randomization is hard-blocked at non-greenlit sites with detailed diagnostic error messages.

    @req:PRD-SYS-183, PRD-SSU-003, ADR-2202, ICH GCP E6(R2) Section 8
    """
    study_id = "STUDY-GATE-RANDOMIZATION"
    site_id = "SITE-GATE-RANDOMIZATION"
    user_id = "investigator_01"

    class MockPendingGreenlightClient(IGreenlightVerificationClient):
        async def get_greenlight_status(self, study_id: str, site_id: str) -> dict:
            return {
                "study_id": study_id,
                "site_id": site_id,
                "overall_status": "PENDING",
                "facet_regulatory_irb": False,
                "facet_cta_budget": False,
                "facet_eisf_baseline": False,
                "facet_doa_log": False,
                "facet_staff_training": False,
                "facet_ip_release": False,
                "evaluation_details": {
                    "facets": {
                        "regulatory_irb": {
                            "passed": False,
                            "missing": [
                                "Form FDA 1572 Statement of Investigator",
                                "IRB Approval Letter",
                            ],
                        },
                        "cta_budget": {
                            "passed": False,
                            "missing": ["Executed Clinical Trial Agreement (CTA)"],
                        },
                    }
                },
            }

    mock_client = MockPendingGreenlightClient()
    set_greenlight_verification_client(mock_client)

    gk = SiteExecutionGatekeeper(greenlight_client=mock_client)
    set_site_execution_gatekeeper(gk)

    with pytest.raises(SiteNotEligibleForMutationError) as exc_info:
        await require_site_mutation_authorization(
            site_id=site_id,
            user_id=user_id,
            operation_type="RANDOMIZATION",
            study_id=study_id,
            gatekeeper=gk,
        )

    err = exc_info.value
    assert err.status_code == 403
    assert err.detail["error"] == "SITE_NOT_ELIGIBLE_FOR_MUTATION"
    assert (
        GatekeeperRejectionReason.SITE_NOT_GREENLIGHTED
        in err.detail["rejection_reasons"]
    )
    assert "greenlight" in err.detail["rejection_details"]
    gl_details = err.detail["rejection_details"]["greenlight"]
    assert gl_details["overall_status"] == "PENDING"
    assert "Form FDA 1572 Statement of Investigator" in gl_details["missing_facets"]
    assert "Executed Clinical Trial Agreement (CTA)" in gl_details["missing_facets"]


@pytest.mark.asyncio
async def test_ecrf_visit_creation_gating_blocks_non_greenlight_site():
    """Validate that eCRF visit creation is hard-blocked at non-greenlit sites with detailed diagnostic error messages.

    @req:PRD-SYS-183, PRD-SSU-003, ADR-2202, ICH GCP E6(R2) Section 8
    """
    study_id = "STUDY-GATE-VISIT"
    site_id = "SITE-GATE-VISIT"
    user_id = "crc_lead_01"

    class MockPendingGreenlightClient(IGreenlightVerificationClient):
        async def get_greenlight_status(self, study_id: str, site_id: str) -> dict:
            return {
                "study_id": study_id,
                "site_id": site_id,
                "overall_status": "PENDING",
                "facet_regulatory_irb": False,
                "facet_cta_budget": False,
                "facet_eisf_baseline": False,
                "facet_doa_log": False,
                "facet_staff_training": False,
                "facet_ip_release": False,
                "evaluation_details": {
                    "facets": {
                        "regulatory_irb": {
                            "passed": False,
                            "missing": ["EU CTR Part I Authorisation"],
                        },
                        "staff_training": {
                            "passed": False,
                            "missing": [
                                "GCP & Protocol Amendment Training Verification"
                            ],
                        },
                    }
                },
            }

    mock_client = MockPendingGreenlightClient()
    set_greenlight_verification_client(mock_client)

    gk = SiteExecutionGatekeeper(greenlight_client=mock_client)
    set_site_execution_gatekeeper(gk)

    with pytest.raises(SiteNotEligibleForMutationError) as exc_info:
        await require_site_mutation_authorization(
            site_id=site_id,
            user_id=user_id,
            operation_type="VISIT_CREATION",
            study_id=study_id,
            gatekeeper=gk,
        )

    err = exc_info.value
    assert err.status_code == 403
    assert err.detail["error"] == "SITE_NOT_ELIGIBLE_FOR_MUTATION"
    assert (
        GatekeeperRejectionReason.SITE_NOT_GREENLIGHTED
        in err.detail["rejection_reasons"]
    )
    assert "greenlight" in err.detail["rejection_details"]
    gl_details = err.detail["rejection_details"]["greenlight"]
    assert gl_details["overall_status"] == "PENDING"
    assert "EU CTR Part I Authorisation" in gl_details["missing_facets"]
    assert (
        "GCP & Protocol Amendment Training Verification" in gl_details["missing_facets"]
    )
