"""Tests for eConsent Application Services and Use Cases."""

import pytest
import pytest_asyncio

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import Base
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLConsentClauseRepository,
    SQLConsentTemplateRepository,
    SQLConsentTranslationRepository,
)
from apps.econsent.application.use_cases import (
    ClauseManagementService,
    TemplateAuthoringService,
    TranslationService,
)
from apps.econsent.domain.exceptions import (
    ClauseNotFoundError,
    InvalidTranslationTransitionError,
)


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
async def test_clause_service_lifecycle() -> None:
    """Test clause creation, auto-increment update, and version lookup."""
    async with db_manager.get_session_maker()() as session:
        c_repo = SQLConsentClauseRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        svc = ClauseManagementService(c_repo, a_repo)

        created = await svc.create_clause(
            study_id="STUDY-UC-01",
            title="UC Clause Title",
            text="UC Clause Content",
        )
        await session.commit()
        assert created.version_index == 1
        clause_id = created.clause_id

    async with db_manager.get_session_maker()() as session:
        c_repo = SQLConsentClauseRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        svc = ClauseManagementService(c_repo, a_repo)

        updated = await svc.update_clause(
            clause_id=clause_id,
            title="UC Clause Title Updated",
            text="UC Clause Content Updated",
            created_by="editor",
            reason_for_change="Amendment update",
        )
        await session.commit()
        assert updated.version_index == 2

    async with db_manager.get_session_maker()() as session:
        c_repo = SQLConsentClauseRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        svc = ClauseManagementService(c_repo, a_repo)

        v1 = await svc.get_clause(clause_id, version_index=1)
        assert v1.title == "UC Clause Title"
        latest = await svc.get_clause(clause_id)
        assert latest.title == "UC Clause Title Updated"

        with pytest.raises(ClauseNotFoundError):
            await svc.get_clause("non-existent-clause")


@pytest.mark.asyncio
async def test_template_authoring_and_composition() -> None:
    """Test creating template, composing clauses, publishing, and version retrieval."""
    async with db_manager.get_session_maker()() as session:
        c_repo = SQLConsentClauseRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        c_svc = ClauseManagementService(c_repo, a_repo)

        c1 = await c_svc.create_clause("STUDY-UC-02", "Clause 1", "Text 1")
        c2 = await c_svc.create_clause("STUDY-UC-02", "Clause 2", "Text 2")
        await session.commit()
        cid1, cid2 = c1.clause_id, c2.clause_id

    async with db_manager.get_session_maker()() as session:
        t_repo = SQLConsentTemplateRepository(session)
        c_repo = SQLConsentClauseRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        t_svc = TemplateAuthoringService(t_repo, c_repo, a_repo)

        tpl = await t_svc.create_template(
            study_id="STUDY-UC-02",
            template_name="Template UC-02",
            protocol_version="v1.0",
            clauses=[cid1, cid2],
            workflow_steps=[],
        )
        await session.commit()
        tid = tpl.template_id

    async with db_manager.get_session_maker()() as session:
        t_repo = SQLConsentTemplateRepository(session)
        c_repo = SQLConsentClauseRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        t_svc = TemplateAuthoringService(t_repo, c_repo, a_repo)

        composed = await t_svc.compose_template(tid)
        assert len(composed["clauses"]) == 2
        assert composed["clauses"][0]["clause_id"] == cid1
        assert composed["clauses"][1]["clause_id"] == cid2

        published = await t_svc.publish_template(
            tid, created_by="approver", reason_for_change="Approval"
        )
        await session.commit()
        assert published.is_published is True


@pytest.mark.asyncio
async def test_translation_service_workflow() -> None:
    """Test translation creation and state transitions with invalid transition rejection."""
    async with db_manager.get_session_maker()() as session:
        tr_repo = SQLConsentTranslationRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        t_svc = TranslationService(tr_repo, a_repo)

        tr = await t_svc.create_translation(
            source_id="tpl-src-01",
            source_type="TEMPLATE",
            source_version_index=1,
            language_code="es",
            translated_title="Formulario de Consentimiento",
            translated_text="Texto traducido",
        )
        await session.commit()
        tr_id = tr.translation_id

    async with db_manager.get_session_maker()() as session:
        tr_repo = SQLConsentTranslationRepository(session)
        a_repo = SQLConsentAuditRepository(session)
        t_svc = TranslationService(tr_repo, a_repo)

        # Valid: DRAFT -> IN_REVIEW -> APPROVED
        in_rev = await t_svc.transition_status(
            tr_id, "IN_REVIEW", created_by="reviewer", reason_for_change="Review"
        )
        await session.commit()
        assert in_rev.status == "IN_REVIEW"

        approved = await t_svc.transition_status(
            tr_id, "APPROVED", created_by="approver", reason_for_change="Approval"
        )
        await session.commit()
        assert approved.status == "APPROVED"

        # Invalid transition: APPROVED -> RETIRED is not directly allowed
        with pytest.raises(InvalidTranslationTransitionError):
            await t_svc.transition_status(
                tr_id, "RETIRED", created_by="user", reason_for_change="Invalid"
            )
