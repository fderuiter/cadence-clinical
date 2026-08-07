import os
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest

from apps.designer import (
    EVSNotFoundError,
    EVSTimeoutError,
    EVSTransportError,
    NCIEVSClient,
)
from apps.designer.evs_client import normalize_concept


@pytest.mark.asyncio
async def test_import_does_not_make_network_calls():
    """Ensure that importing modules does not make any live network calls or client requests."""
    # We can verify by patching the httpx.AsyncClient methods and then performing a reload or importing
    with patch("httpx.AsyncClient.get") as mock_get:
        import apps.designer.evs_client  # noqa: F401

        assert not mock_get.called


@pytest.mark.asyncio
async def test_get_concept_success():
    """Test successful retrieval and normalization of a concept by code."""
    client = NCIEVSClient()
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 200
    mock_response.json.return_value = {
        "code": "C123",
        "name": "Treatment Arm",
        "terminology": "ncit",
        "active": True,
    }

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        result = await client.get_concept("C123")

        assert result == {
            "code": "C123",
            "decode": "Treatment Arm",
            "system": "ncit",
            "valid": True,
        }
        mock_get.assert_called_once_with(
            "https://api-evsrest.nci.nih.gov/api/v1/concept/ncit/C123"
        )


@pytest.mark.asyncio
async def test_get_concept_not_found():
    """Test that a 404 response raises EVSNotFoundError."""
    client = NCIEVSClient()
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 404

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        with pytest.raises(EVSNotFoundError) as exc_info:
            await client.get_concept("C123_INVALID")

        assert "Concept not found or invalid" in str(exc_info.value)


@pytest.mark.asyncio
async def test_get_concept_http_status_error_404():
    """Test that a 404 raised via raise_for_status raises EVSNotFoundError."""
    client = NCIEVSClient()
    mock_request = httpx.Request(
        "GET", "https://api-evsrest.nci.nih.gov/api/v1/concept/ncit/C123"
    )
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 404
    mock_response.raise_for_status.side_effect = httpx.HTTPStatusError(
        "404 Not Found", request=mock_request, response=mock_response
    )

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        with pytest.raises(EVSNotFoundError):
            await client.get_concept("C123")


@pytest.mark.asyncio
async def test_get_concept_invalid_via_400():
    """Test that a 400 response with 'not found' or 'invalid' in body raises EVSNotFoundError."""
    client = NCIEVSClient()
    mock_request = httpx.Request(
        "GET", "https://api-evsrest.nci.nih.gov/api/v1/concept/ncit/C123"
    )
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 400
    mock_response.json.return_value = {"detail": "Concept C123 was not found."}
    mock_response.raise_for_status.side_effect = httpx.HTTPStatusError(
        "400 Bad Request", request=mock_request, response=mock_response
    )

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        with pytest.raises(EVSNotFoundError):
            await client.get_concept("C123")


@pytest.mark.asyncio
async def test_get_concept_timeout():
    """Test that a TimeoutException raises EVSTimeoutError."""
    client = NCIEVSClient()

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.side_effect = httpx.TimeoutException("Connection timed out")
        with pytest.raises(EVSTimeoutError) as exc_info:
            await client.get_concept("C123")

        assert "timed out" in str(exc_info.value)


@pytest.mark.asyncio
async def test_get_concept_transport_error():
    """Test that standard network RequestError raises EVSTransportError."""
    client = NCIEVSClient()

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.side_effect = httpx.RequestError("Network unreachable")
        with pytest.raises(EVSTransportError) as exc_info:
            await client.get_concept("C123")

        assert "Transport failure" in str(exc_info.value)


@pytest.mark.asyncio
async def test_get_concept_server_error_500():
    """Test that 5xx status codes raise EVSTransportError."""
    client = NCIEVSClient()
    mock_request = httpx.Request(
        "GET", "https://api-evsrest.nci.nih.gov/api/v1/concept/ncit/C123"
    )
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 500
    mock_response.text = "Internal Server Error"
    mock_response.raise_for_status.side_effect = httpx.HTTPStatusError(
        "500 Internal Server Error", request=mock_request, response=mock_response
    )

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        with pytest.raises(EVSTransportError) as exc_info:
            await client.get_concept("C123")

        assert "HTTP error from EVS API: 500" in str(exc_info.value)


@pytest.mark.asyncio
async def test_search_concepts_success():
    """Test successful search and list normalization."""
    client = NCIEVSClient()
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 200
    mock_response.json.return_value = {
        "concepts": [
            {
                "code": "C123",
                "name": "Treatment Arm",
                "terminology": "ncit",
                "active": True,
            },
            {
                "code": "C456",
                "displayName": "Placebo Arm",
                "terminology": "ncit",
                "active": False,
            },
        ]
    }

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        results = await client.search_concepts("Arm", from_record=0, page_size=10)

        assert len(results) == 2
        assert results[0] == {
            "code": "C123",
            "decode": "Treatment Arm",
            "system": "ncit",
            "valid": True,
        }
        assert results[1] == {
            "code": "C456",
            "decode": "Placebo Arm",
            "system": "ncit",
            "valid": False,
        }

        mock_get.assert_called_once_with(
            "https://api-evsrest.nci.nih.gov/api/v1/concept/ncit/search",
            params={"term": "Arm", "fromRecord": "0", "pageSize": "10"},
        )


@pytest.mark.asyncio
async def test_search_concepts_list_shape():
    """Test search with a plain list response shape."""
    client = NCIEVSClient()
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 200
    mock_response.json.return_value = [
        {
            "code": "C789",
            "name": "Vitals",
            "terminology": "ncit",
            "active": True,
        }
    ]

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        results = await client.search_concepts("Vitals")

        assert len(results) == 1
        assert results[0]["code"] == "C789"
        assert results[0]["decode"] == "Vitals"


@pytest.mark.asyncio
async def test_search_concepts_timeout():
    """Test that text search handles timeout by raising EVSTimeoutError."""
    client = NCIEVSClient()

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.side_effect = httpx.TimeoutException("Read timeout")
        with pytest.raises(EVSTimeoutError):
            await client.search_concepts("Arm")


@pytest.mark.asyncio
async def test_search_concepts_transport_error():
    """Test that text search handles transport errors."""
    client = NCIEVSClient()

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.side_effect = httpx.RequestError("No route to host")
        with pytest.raises(EVSTransportError):
            await client.search_concepts("Arm")


@pytest.mark.asyncio
async def test_client_configuration_overrides():
    """Test that NCIEVSClient picks up configuration overrides from custom init parameters."""
    custom_timeout = httpx.Timeout(timeout=10.0, connect=2.0)
    client = NCIEVSClient(
        base_url="https://custom-evs.com",
        terminology="cdisc",
        timeout=custom_timeout,
    )

    assert client.base_url == "https://custom-evs.com"
    assert client.terminology == "cdisc"
    assert client.timeout == custom_timeout


@pytest.mark.asyncio
async def test_client_configuration_env_vars():
    """Test that NCIEVSClient picks up configuration from environment variables."""
    env_overrides = {
        "NCI_EVS_BASE_URL": "https://env-evs.com/",
        "NCI_EVS_TERMINOLOGY": "custom-term",
        "NCI_EVS_TIMEOUT_CONNECT": "1.5",
        "NCI_EVS_TIMEOUT_READ": "2.5",
        "NCI_EVS_TIMEOUT_WRITE": "3.5",
        "NCI_EVS_TIMEOUT_POOL": "4.5",
    }

    with patch.dict(os.environ, env_overrides):
        client = NCIEVSClient()

        assert client.base_url == "https://env-evs.com"  # stripped trailing slash
        assert client.terminology == "custom-term"
        assert client.timeout.connect == 1.5
        assert client.timeout.read == 2.5
        assert client.timeout.write == 3.5
        assert client.timeout.pool == 4.5


@pytest.mark.asyncio
async def test_get_concept_invalid_json():
    """Test that a non-JSON response from EVS raises EVSTransportError."""
    client = NCIEVSClient()
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 200
    mock_response.json.side_effect = ValueError("Expecting value: line 1 column 1")

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        with pytest.raises(EVSTransportError) as exc_info:
            await client.get_concept("C123")
        assert "Failed to parse EVS JSON response" in str(exc_info.value)


@pytest.mark.asyncio
async def test_search_concepts_invalid_json():
    """Test that a non-JSON search response raises EVSTransportError."""
    client = NCIEVSClient()
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 200
    mock_response.json.side_effect = ValueError("Expecting value: line 1 column 1")

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        with pytest.raises(EVSTransportError) as exc_info:
            await client.search_concepts("Arm")
        assert "Failed to parse EVS JSON response" in str(exc_info.value)


@pytest.mark.asyncio
async def test_get_concept_invalid_via_422_not_found():
    """Test that a 422 response with 'not found' or 'invalid' in body raises EVSNotFoundError."""
    client = NCIEVSClient()
    mock_request = httpx.Request(
        "GET", "https://api-evsrest.nci.nih.gov/api/v1/concept/ncit/C123"
    )
    mock_response = MagicMock(spec=httpx.Response)
    mock_response.status_code = 422
    mock_response.json.return_value = {
        "detail": "The requested concept C123 is invalid."
    }
    mock_response.raise_for_status.side_effect = httpx.HTTPStatusError(
        "422 Unprocessable Entity", request=mock_request, response=mock_response
    )

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_get.return_value = mock_response
        with pytest.raises(EVSNotFoundError) as exc_info:
            await client.get_concept("C123")
        assert "Concept not found or invalid" in str(exc_info.value)


def test_normalize_concept_edge_cases():
    """Test normalize_concept with missing/empty fields to ensure proper defaults."""
    # Empty concept data
    res1 = normalize_concept({}, default_system="ncit")
    assert res1 == {
        "code": "",
        "decode": "",
        "system": "ncit",
        "valid": True,
    }

    # Custom terminology system specified in concept data
    res2 = normalize_concept(
        {"code": "C999", "terminology": "cdisc"}, default_system="ncit"
    )
    assert res2 == {
        "code": "C999",
        "decode": "",
        "system": "cdisc",
        "valid": True,
    }

    # Active is False
    res3 = normalize_concept({"active": False}, default_system="ncit")
    assert res3 == {
        "code": "",
        "decode": "",
        "system": "ncit",
        "valid": False,
    }
