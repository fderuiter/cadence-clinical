import os
import time
from unittest.mock import AsyncMock, patch

import pytest

from apps.designer import EVSNotFoundError, EVSTransportError
from apps.designer.db import (
    MOCK_TERMINOLOGY,
    TerminologyCache,
    get_terminology_from_db,
)


def test_cache_hit_performs_no_external_lookup():
    """Verify that a cache hit performs no external lookup."""
    cache = TerminologyCache(max_size=10, ttl=60.0)
    concept_id = "C999_TEST_HIT"

    mock_concept = {
        "code": "C999_TEST_HIT",
        "decode": "Test Concept Hit",
        "system": "ncit",
        "valid": True,
    }

    with patch.dict(os.environ, {"TERMINOLOGY_OFFLINE": "false"}):
        # Mock the EVS client get_concept method
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.return_value = mock_concept

            # Initial request: Cache miss, queries DB/EVS
            val1 = cache.get(concept_id)
            assert val1 == mock_concept
            mock_get.assert_called_once_with(concept_id)

            # Reset mock
            mock_get.reset_mock()

            # Second request: Cache hit, no external lookup
            val2 = cache.get(concept_id)
            assert val2 == mock_concept
            mock_get.assert_not_called()


def test_expired_entry_fallback_on_unreachable_evs():
    """Verify that an expired entry falls back to the stale cached value when EVS is unavailable."""
    # Create cache with very small TTL
    cache = TerminologyCache(max_size=10, ttl=0.01)
    concept_id = "C999_TEST_STALE"

    mock_concept = {
        "code": "C999_TEST_STALE",
        "decode": "Test Concept Stale",
        "system": "ncit",
        "valid": True,
    }

    with patch.dict(os.environ, {"TERMINOLOGY_OFFLINE": "false"}):
        # 1. Populate the cache first
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.return_value = mock_concept
            val1 = cache.get(concept_id)
            assert val1 == mock_concept

        # 2. Wait for the TTL to expire
        time.sleep(0.02)

        # 3. DB/EVS is now unreachable (throws EVSTransportError)
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.side_effect = EVSTransportError(
                "Connection timeout contacting EVS API"
            )

            # Cache should silently handle the exception and fall back to the expired cached value
            val2 = cache.get(concept_id)
            assert val2 == mock_concept


def test_offline_fallback_resolves_supported_seed_concepts():
    """Verify that offline/configured fallback resolves supported seed concepts."""
    cache = TerminologyCache(max_size=10, ttl=60.0)

    # Enable offline mode via environment variable
    with patch.dict(os.environ, {"TERMINOLOGY_OFFLINE": "true"}):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            for concept_id, mock_val in MOCK_TERMINOLOGY.items():
                val = cache.get(concept_id)
                # Verify it resolves to seed concept
                assert val is not None
                assert val["code"] == concept_id
                assert val["decode"] == mock_val["decode"]
                # EVS client should not have been called because we are offline
                mock_get.assert_not_called()


def test_existing_cache_consumers_receive_expected_shape():
    """Verify that existing cache consumers continue to receive the expected concept shape."""
    cache = TerminologyCache(max_size=10, ttl=60.0)
    concept_id = "C123"

    with patch.dict(os.environ, {"TERMINOLOGY_OFFLINE": "false"}):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.return_value = {
                "code": "C123",
                "decode": "Treatment Arm",
                "system": "ncit",
                "valid": True,
            }

            val = cache.get(concept_id)
            assert isinstance(val, dict)
            assert "code" in val
            assert "decode" in val
            assert "system" in val
            assert val["code"] == "C123"
            assert val["decode"] == "Treatment Arm"
            assert val["system"] == "ncit"


def test_get_terminology_from_db_delegation():
    """Verify that get_terminology_from_db correctly delegates to NCIEVSClient."""
    concept_id = "C999_DELEGATION"
    mock_concept = {
        "code": "C999_DELEGATION",
        "decode": "Delegated Concept",
        "system": "ncit",
        "valid": True,
    }

    with patch.dict(
        os.environ, {"TERMINOLOGY_OFFLINE": "false", "NCI_EVS_OFFLINE": "false"}
    ):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.return_value = mock_concept

            res = get_terminology_from_db(concept_id)
            assert res == mock_concept
            mock_get.assert_called_once_with(concept_id)


def test_get_terminology_from_db_nci_evs_offline_fallback():
    """Verify that get_terminology_from_db falls back directly to MOCK_TERMINOLOGY when NCI_EVS_OFFLINE=1."""
    concept_id = "C123"  # In MOCK_TERMINOLOGY

    with patch.dict(
        os.environ, {"TERMINOLOGY_OFFLINE": "false", "NCI_EVS_OFFLINE": "1"}
    ):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            res = get_terminology_from_db(concept_id)
            # Should resolve from mock terminology and NOT call EVS client
            assert res is not None
            assert res["code"] == "C123"
            mock_get.assert_not_called()


def test_get_terminology_from_db_not_found_in_evs_but_in_mock():
    """Verify fallback to MOCK_TERMINOLOGY when EVS client raises EVSNotFoundError but the concept is registered in seed."""
    concept_id = "C123"  # In MOCK_TERMINOLOGY

    with patch.dict(
        os.environ, {"TERMINOLOGY_OFFLINE": "false", "NCI_EVS_OFFLINE": "false"}
    ):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.side_effect = EVSNotFoundError("Not found in EVS")

            res = get_terminology_from_db(concept_id)
            # Should fallback to MOCK_TERMINOLOGY
            assert res is not None
            assert res["code"] == "C123"
            mock_get.assert_called_once_with(concept_id)


def test_get_terminology_from_db_not_found_anywhere():
    """Verify that get_terminology_from_db returns None when not found in EVS and NOT in MOCK_TERMINOLOGY."""
    concept_id = "UNKNOWN_CONCEPT_ID"

    with patch.dict(
        os.environ, {"TERMINOLOGY_OFFLINE": "false", "NCI_EVS_OFFLINE": "false"}
    ):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.side_effect = EVSNotFoundError("Not found in EVS")

            res = get_terminology_from_db(concept_id)
            assert res is None
            mock_get.assert_called_once_with(concept_id)


def test_get_terminology_from_db_transport_error_but_in_mock():
    """Verify fallback to MOCK_TERMINOLOGY when EVS client raises a transport error and the concept exists in seed."""
    concept_id = "C123"  # In MOCK_TERMINOLOGY

    with patch.dict(
        os.environ, {"TERMINOLOGY_OFFLINE": "false", "NCI_EVS_OFFLINE": "false"}
    ):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.side_effect = EVSTransportError("EVS service is down")

            res = get_terminology_from_db(concept_id)
            # Should still fallback to seed because of EVS failure
            assert res is not None
            assert res["code"] == "C123"
            mock_get.assert_called_once_with(concept_id)


def test_get_terminology_from_db_transport_error_and_not_in_mock():
    """Verify that transport error propagates if the concept is not in seed."""
    concept_id = "UNKNOWN_CONCEPT_ID"

    with patch.dict(
        os.environ, {"TERMINOLOGY_OFFLINE": "false", "NCI_EVS_OFFLINE": "false"}
    ):
        with patch(
            "apps.designer.evs_client.NCIEVSClient.get_concept", new_callable=AsyncMock
        ) as mock_get:
            mock_get.side_effect = EVSTransportError("EVS service is down")

            with pytest.raises(EVSTransportError) as exc_info:
                get_terminology_from_db(concept_id)
            assert "EVS service is down" in str(exc_info.value)


def test_terminology_cache_unreachable_database_exception_fallback():
    """Verify that TerminologyCache falls back to expired stale value if database query raises Exception."""
    cache = TerminologyCache(max_size=10, ttl=0.01)
    concept_id = "C123"

    # Populate cache
    with patch.dict(os.environ, {"TERMINOLOGY_OFFLINE": "true"}):
        val = cache.get(concept_id)
        assert val is not None

    # Wait for expiry
    time.sleep(0.02)

    # Force database query to fail with Exception
    with patch(
        "apps.designer.db.get_terminology_from_db",
        side_effect=Exception("Database down"),
    ):
        val2 = cache.get(concept_id)
        # Should return expired cached value instead of raising Exception
        assert val2 == val
