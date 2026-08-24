from unittest.mock import AsyncMock, MagicMock

import pytest

from apps.designer.adapters.repositories import (
    EntityLabel,
    RelationshipType,
    list_library_objects,
    retire_soa_entity,
    validate_entity_label,
    validate_relationship_type,
)
from apps.designer.adapters.safety_gateway import validate_query_safety
from apps.designer.infrastructure.neo4j_usdm_writer import (
    CYPHER_COMMIT_ACTIVITIES,
    CYPHER_COMMIT_ARMS,
    CYPHER_COMMIT_CRITERIA,
    CYPHER_COMMIT_EPOCHS,
    CYPHER_COMMIT_SOA_LINKS,
    CYPHER_COMMIT_STUDY,
    CYPHER_COMMIT_VISITS,
)


def test_entity_label_validation() -> None:
    """Verify that validate_entity_label allows valid labels and rejects invalid labels."""
    assert validate_entity_label(EntityLabel.STUDY) == "Study"
    assert validate_entity_label("StudyVersion") == "StudyVersion"
    assert validate_entity_label("LibraryObject") == "LibraryObject"

    with pytest.raises(ValueError) as exc_info:
        validate_entity_label("InvalidNodeLabel'; DROP TABLE--")
    assert "not in allowed EntityLabel enum" in str(exc_info.value)


def test_relationship_type_validation() -> None:
    """Verify that validate_relationship_type allows valid types and rejects invalid relationship types."""
    assert validate_relationship_type(RelationshipType.HAS_VERSION) == "HAS_VERSION"
    assert validate_relationship_type("PREVIOUS_VERSION") == "PREVIOUS_VERSION"
    assert validate_relationship_type("HAS_ARM") == "HAS_ARM"

    with pytest.raises(ValueError) as exc_info:
        validate_relationship_type("UNAUTHORIZED_REL_TYPE")
    assert "not in allowed RelationshipType enum" in str(exc_info.value)


def test_writer_cypher_queries_safety() -> None:
    """Verify that all Cypher commit queries in neo4j_usdm_writer pass safety gateway checks."""
    queries = [
        CYPHER_COMMIT_STUDY,
        CYPHER_COMMIT_EPOCHS,
        CYPHER_COMMIT_ARMS,
        CYPHER_COMMIT_VISITS,
        CYPHER_COMMIT_ACTIVITIES,
        CYPHER_COMMIT_SOA_LINKS,
        CYPHER_COMMIT_CRITERIA,
    ]
    for q in queries:
        validate_query_safety(q)


@pytest.mark.asyncio
async def test_list_library_objects_query_generation() -> None:
    """Verify that list_library_objects generates safe Cypher queries using allowlisted conditions."""
    mock_res = AsyncMock()
    mock_res.all = AsyncMock(return_value=[])

    mock_session = AsyncMock()
    mock_session.run = AsyncMock(return_value=mock_res)
    mock_session.__aenter__ = AsyncMock(return_value=mock_session)
    mock_session.__aexit__ = AsyncMock(return_value=None)

    mock_driver = MagicMock()
    mock_driver.session = MagicMock(return_value=mock_session)

    res = await list_library_objects(
        driver=mock_driver,
        sponsor_id="sponsor_123",
        object_type="Form",
        starting_after="obj_001",
        limit=10,
    )

    assert res == []
    mock_session.run.assert_called_once()
    executed_query = mock_session.run.call_args[0][0]
    validate_query_safety(executed_query)
    assert "MATCH (n:LibraryObject)" in executed_query
    assert "WHERE" in executed_query


@pytest.mark.asyncio
async def test_retire_soa_entity_enum_enforcement() -> None:
    """Verify that retire_soa_entity uses strict relationship and label allowlisting."""
    mock_record = AsyncMock()
    mock_record.single = AsyncMock(return_value={"old_node.id": "arm_123"})

    mock_tx = AsyncMock()
    mock_tx.run = AsyncMock(return_value=mock_record)

    mock_session = AsyncMock()
    mock_session.begin_transaction = AsyncMock(return_value=mock_tx)

    mock_driver = MagicMock()
    mock_driver.session = MagicMock(return_value=mock_session)

    # Calling with invalid entity_type should fail immediately
    with pytest.raises(ValueError) as exc_info:
        await retire_soa_entity(
            driver=mock_driver,
            study_version_id="sv_123",
            user_id="user_123",
            change_reason="Retiring test arm",
            entity_id="arm_123",
            entity_type="unauthorized_type",
        )
    assert "Invalid entity type" in str(exc_info.value)
