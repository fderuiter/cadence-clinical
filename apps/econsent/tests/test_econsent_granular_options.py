"""Tests for Granular & Tiered Optional Consent items (Genetics, Biobanking, Sub-studies).

Verifies authoring, querying, subject opt-in selection, and persistence.
"""

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import (
    Base,
    ComprehensionResult,
    ConsentClause,
    ConsentTemplate,
    SubjectConsentOptionSelection,
)
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLConsentSignatureRepository,
    SQLGranularOptionRepository,
    SQLSubjectConsentRepository,
)
from apps.econsent.application.use_cases import (
    ConsentCaptureService,
    GranularOptionService,
)
from apps.econsent.domain.entities import GranularOptionCategory


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
async def test_granular_options_lifecycle_and_subject_selection() -> None:
    """Test configuring granular options and recording subject choices during consent."""
    async with db_manager.get_session_maker()() as session:
        clause = ConsentClause(
            clause_id="clause-genomics-01",
            study_id="STUDY-ONCO-202",
            title="Genomics Sub-study Clause",
            text="Optional whole-genome sequencing analysis on baseline tumor biopsy samples.",
            version_index=1,
            created_by="designer",
            reason_for_change="Seed genomics clause",
        )
        session.add(clause)

        template = ConsentTemplate(
            template_id="tpl-onco-202",
            study_id="STUDY-ONCO-202",
            template_name="Oncology Phase II ICF",
            protocol_version="v1.0",
            is_published=True,
            requires_reconsent=False,
            version_index=1,
            clauses=["clause-genomics-01"],
            workflow_steps=[
                {"type": "comprehension_check"},
                {"type": "signature_placeholder"},
            ],
            created_by="designer",
            reason_for_change="Seed published template",
        )
        session.add(template)

        comp_res = ComprehensionResult(
            template_id="tpl-onco-202",
            version_index=1,
            subject_pseudonym="SUBJ-ONCO-501",
            questions=[],
            expected_answers={},
            threshold_policy={},
            submitted_answers={},
            passed=True,
            score=100.0,
            created_by="patient",
            reason_for_change="Initial comprehension pass",
        )
        session.add(comp_res)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        granular_repo = SQLGranularOptionRepository(session)
        audit_repo = SQLConsentAuditRepository(session)
        opt_svc = GranularOptionService(granular_repo, audit_repo)

        opt1 = await opt_svc.create_option(
            template_id="tpl-onco-202",
            version_index=1,
            option_code="OPT_GENOMICS",
            title="Optional Pharmacogenomics Sequencing",
            description="Allow storage and sequencing of DNA samples for genetic markers.",
            category=GranularOptionCategory.GENETICS,
            is_mandatory=False,
            default_selected=False,
        )
        opt2 = await opt_svc.create_option(
            template_id="tpl-onco-202",
            version_index=1,
            option_code="OPT_BIOBANK",
            title="Future Biomedical Research Biobank",
            description="Allow leftover plasma to be retained for up to 15 years.",
            category=GranularOptionCategory.BIOBANKING,
            is_mandatory=False,
            default_selected=False,
        )
        opt3 = await opt_svc.create_option(
            template_id="tpl-onco-202",
            version_index=1,
            option_code="OPT_RECONTACT",
            title="Future Study Re-contact",
            description="Allow site to reach out for follow-on oncology clinical trials.",
            category=GranularOptionCategory.FUTURE_CONTACT,
            is_mandatory=False,
            default_selected=True,
        )
        await session.commit()

        assert opt1.option_code == "OPT_GENOMICS"
        assert opt2.option_code == "OPT_BIOBANK"
        assert opt3.option_code == "OPT_RECONTACT"

    async with db_manager.get_session_maker()() as session:
        consent_repo = SQLSubjectConsentRepository(session)
        signature_repo = SQLConsentSignatureRepository(session)
        granular_repo = SQLGranularOptionRepository(session)
        audit_repo = SQLConsentAuditRepository(session)
        cap_svc = ConsentCaptureService(
            consent_repo, signature_repo, granular_repo, audit_repo
        )

        granular_choices = [
            {"option_code": "OPT_GENOMICS", "selected": True},
            {"option_code": "OPT_BIOBANK", "selected": False},
            {"option_code": "OPT_RECONTACT", "selected": True},
        ]
        signatures = [
            {
                "role": "SUBJECT",
                "signer_name": "Marcus Vance",
                "meaning": "Consent to Main Protocol and chosen optional items",
                "signature_data": "data:image/png;base64,sample_sig",
            }
        ]

        consent = await cap_svc.capture_consent(
            study_id="STUDY-ONCO-202",
            site_id="SITE-NY-01",
            subject_pseudonym="SUBJ-ONCO-501",
            template_id="tpl-onco-202",
            version_index=1,
            protocol_version="v1.0",
            source_content_identity="hash-onco-v1",
            signatures=signatures,
            granular_selections=granular_choices,
            created_by="patient",
            reason_for_change="Initial Subject Consent with Granular Choices",
        )
        await session.commit()

        assert consent.id is not None

    async with db_manager.get_session_maker()() as session:
        stmt_sel = select(SubjectConsentOptionSelection).where(
            SubjectConsentOptionSelection.subject_pseudonym == "SUBJ-ONCO-501"
        )
        res_sel = await session.execute(stmt_sel)
        selections = res_sel.scalars().all()

        assert len(selections) == 3
        sel_map = {s.option_code: s.selected for s in selections}
        assert sel_map["OPT_GENOMICS"] is True
        assert sel_map["OPT_BIOBANK"] is False
        assert sel_map["OPT_RECONTACT"] is True
