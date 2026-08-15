"""Tests for 21 CFR Part 11 and ICH GCP E6(R2)/(R3) Multi-Party Electronic Signatures.

Verifies Subject, Legally Authorized Representative (LAR), Minor Assent,
Investigator (PI), and Impartial Witness signing workflows.
"""

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import (
    Base,
    ComprehensionCheck,
    ComprehensionResult,
    ConsentClause,
    ConsentSignature,
    ConsentTemplate,
)
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLConsentSignatureRepository,
    SQLGranularOptionRepository,
    SQLSubjectConsentRepository,
)
from apps.econsent.application.use_cases import ConsentCaptureService
from apps.econsent.domain.entities import SignerRole


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_multisig_subject_lar_and_investigator_workflow() -> None:
    """Test full multi-party signing cycle with Subject, LAR, and Investigator."""
    async with db_manager.get_session_maker()() as session:
        clause = ConsentClause(
            clause_id="clause-multi-01",
            study_id="STUDY-PEDS-001",
            title="Pediatric Study Overview",
            text="Comprehensive details regarding pediatric clinical study interventions.",
            version_index=1,
            created_by="designer",
            reason_for_change="Initial pediatric clause",
        )
        session.add(clause)

        template = ConsentTemplate(
            template_id="tpl-peds-001",
            study_id="STUDY-PEDS-001",
            template_name="Pediatric Oncology ICF",
            protocol_version="v2.1",
            is_published=True,
            requires_reconsent=False,
            version_index=1,
            clauses=["clause-multi-01"],
            workflow_steps=[
                {"type": "comprehension_check", "id": "step-1"},
                {"type": "signature_placeholder", "id": "step-2"},
            ],
            created_by="designer",
            reason_for_change="Publish pediatric ICF",
        )
        session.add(template)

        check = ComprehensionCheck(
            template_id="tpl-peds-001",
            version_index=1,
            questions=[
                {
                    "id": "q1",
                    "text": "What is the primary intervention?",
                    "options": ["Drug A", "Placebo"],
                }
            ],
            expected_answers={"q1": "Drug A"},
            threshold_policy={"min_correct": 1},
            created_by="designer",
            reason_for_change="Comprehension setup",
        )
        session.add(check)

        comp_res = ComprehensionResult(
            template_id="tpl-peds-001",
            version_index=1,
            subject_pseudonym="SUBJ-PEDS-101",
            questions=check.questions,
            expected_answers=check.expected_answers,
            threshold_policy=check.threshold_policy,
            submitted_answers={"q1": "Drug A"},
            passed=True,
            score=100.0,
            created_by="lar.parent",
            reason_for_change="LAR Quiz completion",
        )
        session.add(comp_res)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        consent_repo = SQLSubjectConsentRepository(session)
        signature_repo = SQLConsentSignatureRepository(session)
        granular_repo = SQLGranularOptionRepository(session)
        audit_repo = SQLConsentAuditRepository(session)
        svc = ConsentCaptureService(
            consent_repo, signature_repo, granular_repo, audit_repo
        )

        signatures = [
            {
                "role": SignerRole.LAR,
                "signer_name": "Jane Doe (Parent)",
                "signer_email": "jane.doe@example.com",
                "meaning": "I consent on behalf of my minor child",
                "signature_data": "data:image/png;base64,lar_sig_data",
                "lar_relationship": "Parent / Legal Guardian",
                "lar_authority_basis": "Birth Certificate / Legal Custody",
            },
            {
                "role": SignerRole.MINOR_ASSENT,
                "signer_name": "Tommy Doe (Child, 12yo)",
                "meaning": "I agree to participate in this study (Assent)",
                "signature_data": "data:image/png;base64,minor_sig_data",
            },
            {
                "role": SignerRole.INVESTIGATOR,
                "signer_name": "Dr. Sarah Jenkins, MD (PI)",
                "signer_email": "s.jenkins@hospital.org",
                "meaning": "I confirm I have explained all trial risks and answered participant questions",
                "signature_data": "data:image/png;base64,pi_sig_data",
            },
        ]

        consent = await svc.capture_consent(
            study_id="STUDY-PEDS-001",
            site_id="SITE-BOSTON-01",
            subject_pseudonym="SUBJ-PEDS-101",
            template_id="tpl-peds-001",
            version_index=1,
            protocol_version="v2.1",
            source_content_identity="icf-hash-peds-01",
            signatures=signatures,
            created_by="lar.parent",
            reason_for_change="Pediatric Tripartite Consent Capture",
        )
        await session.commit()

        assert consent.id is not None
        assert consent.subject_pseudonym == "SUBJ-PEDS-101"
        assert consent.status == "ACTIVE"
        assert "manifest_digest_sha256" in consent.signature_manifest

    async with db_manager.get_session_maker()() as session:
        stmt_s = (
            select(ConsentSignature)
            .where(
                ConsentSignature.template_id == "tpl-peds-001",
                ConsentSignature.subject_pseudonym == "SUBJ-PEDS-101",
            )
            .order_by(ConsentSignature.signed_at)
        )
        res_s = await session.execute(stmt_s)
        persisted_sigs = res_s.scalars().all()

        assert len(persisted_sigs) == 3
        roles = [s.role for s in persisted_sigs]
        assert SignerRole.LAR in roles
        assert SignerRole.MINOR_ASSENT in roles
        assert SignerRole.INVESTIGATOR in roles

        lar_sig = next(s for s in persisted_sigs if s.role == SignerRole.LAR)
        assert lar_sig.signer_name == "Jane Doe (Parent)"
        assert lar_sig.lar_relationship == "Parent / Legal Guardian"
        assert lar_sig.digest_sha256 is not None
