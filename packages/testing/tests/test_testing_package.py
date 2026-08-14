"""Unit tests verifying the centralized testing package factories, fakes, and security helpers."""

import pytest
from pydantic import BaseModel

from packages.testing.factories import (
    AuditLogFactory,
    ClinicalObservationFactory,
    ConsentRecordFactory,
    DocumentMetadataFactory,
    ProtocolDefinitionFactory,
    QueryDiscrepancyFactory,
    SubjectFactory,
)
from packages.testing.fakes import InMemoryRepository
from packages.testing.security import (
    create_test_auth_headers,
    create_test_security_context,
    create_test_token,
)


class SampleDomainEntity(BaseModel):
    id: str
    name: str


@pytest.mark.asyncio
async def test_factories_creation():
    """Verify domain entity factories generate valid objects with sensible defaults."""
    subject = SubjectFactory.create(site_id="SITE-999")
    assert subject.id.startswith("SUBJ-")
    assert subject.site_id == "SITE-999"

    protocol = ProtocolDefinitionFactory.create()
    assert protocol.is_active is True
    assert len(protocol.arms) == 2

    obs = ClinicalObservationFactory.create(value="140")
    assert obs.value == "140"
    assert obs.status == "VALID"

    query = QueryDiscrepancyFactory.create()
    assert query.status == "OPEN"

    consent = ConsentRecordFactory.create()
    assert consent.protocol_version == "1.0"

    doc = DocumentMetadataFactory.create()
    assert doc.zone == "01_TRIAL_MANAGEMENT"

    audit = AuditLogFactory.create()
    assert audit.action == "UPDATE_OBSERVATION"


@pytest.mark.asyncio
async def test_in_memory_repository():
    """Verify InMemoryRepository supports full CRUD lifecycle."""
    repo: InMemoryRepository[SampleDomainEntity] = InMemoryRepository()

    entity = SampleDomainEntity(id="ent-1", name="Test Entity")
    await repo.save(entity)

    assert repo.count() == 1
    fetched = await repo.get_by_id("ent-1")
    assert fetched is not None
    assert fetched.name == "Test Entity"

    all_entities = await repo.list_all()
    assert len(all_entities) == 1

    deleted = await repo.delete("ent-1")
    assert deleted is True
    assert await repo.get_by_id("ent-1") is None
    assert repo.count() == 0


def test_security_helpers():
    """Verify security context and auth header generation."""
    token = create_test_token(user_id="user-123", roles=["Admin"])
    assert token["sub"] == "user-123"
    assert token["roles"] == ["Admin"]

    ctx = create_test_security_context(user_id="user-123", roles=["Admin"])
    assert ctx.user_id == "user-123"
    assert "Admin" in ctx.roles

    headers = create_test_auth_headers(user_id="user-123", roles=["Admin"])
    assert headers["X-User-Id"] == "user-123"
    assert "Admin" in headers["X-User-Roles"]
    assert "X-Gateway-Signature" in headers
