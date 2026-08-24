"""Unit tests ensuring CTMS adapters/clients propagate 5xx HTTP status errors and network exceptions.

Requirements: Ensure exception handlers do not swallow errors or produce synthetic fallbacks ('SYNCED', 'ESCALATED', True).
"""

from unittest.mock import AsyncMock, MagicMock, patch
import httpx
import pytest

from apps.ctms.adapters.clients import ETMFClient, QualityClient, SafetyClient


@pytest.mark.asyncio
async def test_etmf_client_propagates_5xx_error():
    client = ETMFClient(base_url="http://mock-etmf")
    mock_resp = MagicMock()
    mock_resp.status_code = 500
    mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
        "500 Internal Server Error",
        request=MagicMock(),
        response=mock_resp,
    )

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_resp):
        with pytest.raises(httpx.HTTPStatusError) as exc_info:
            await client.push_document(
                study_id="STUDY-101",
                site_id="SITE-01",
                title="Protocol Document",
                content_text="Sample text",
                dia_zone="01",
                dia_section="01.01",
                dia_artifact="Protocol",
                user_id="user_1",
                user_roles=["CRA"],
                reason_for_change="Audit update",
            )
        assert exc_info.value.response.status_code == 500


@pytest.mark.asyncio
async def test_etmf_client_propagates_network_error():
    client = ETMFClient(base_url="http://mock-etmf")

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock, side_effect=httpx.ConnectError("Connection refused")):
        with pytest.raises(httpx.ConnectError):
            await client.push_document(
                study_id="STUDY-101",
                site_id="SITE-01",
                title="Protocol Document",
                content_text="Sample text",
                dia_zone="01",
                dia_section="01.01",
                dia_artifact="Protocol",
                user_id="user_1",
                user_roles=["CRA"],
                reason_for_change="Audit update",
            )


@pytest.mark.asyncio
async def test_quality_client_propagates_5xx_error():
    client = QualityClient(base_url="http://mock-quality")
    mock_resp = MagicMock()
    mock_resp.status_code = 503
    mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
        "503 Service Unavailable",
        request=MagicMock(),
        response=mock_resp,
    )

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_resp):
        with pytest.raises(httpx.HTTPStatusError) as exc_info:
            await client.create_capa_from_deviation(
                study_id="STUDY-101",
                site_id="SITE-01",
                title="Major Deviation",
                description="Lab range out of bounds",
                severity="MAJOR",
                root_cause_summary="SOP gap",
                corrective_action="Retrain staff",
                user_id="user_1",
                user_roles=["CRA"],
                reason_for_change="CAPA escalation",
            )
        assert exc_info.value.response.status_code == 503


@pytest.mark.asyncio
async def test_quality_client_propagates_network_error():
    client = QualityClient(base_url="http://mock-quality")

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock, side_effect=httpx.ConnectTimeout("Timeout")):
        with pytest.raises(httpx.ConnectTimeout):
            await client.create_capa_from_deviation(
                study_id="STUDY-101",
                site_id="SITE-01",
                title="Major Deviation",
                description="Lab range out of bounds",
                severity="MAJOR",
                root_cause_summary="SOP gap",
                corrective_action="Retrain staff",
                user_id="user_1",
                user_roles=["CRA"],
                reason_for_change="CAPA escalation",
            )


@pytest.mark.asyncio
async def test_safety_client_propagates_5xx_error():
    client = SafetyClient(base_url="http://mock-safety")
    mock_resp = MagicMock()
    mock_resp.status_code = 500
    mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
        "500 Internal Server Error",
        request=MagicMock(),
        response=mock_resp,
    )

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_resp):
        with pytest.raises(httpx.HTTPStatusError) as exc_info:
            await client.notify_deviation_event(
                study_id="STUDY-101",
                site_id="SITE-01",
                deviation_id="DEV-001",
                title="Critical Deviation",
                severity="CRITICAL",
                user_id="user_1",
            )
        assert exc_info.value.response.status_code == 500


@pytest.mark.asyncio
async def test_safety_client_propagates_network_error():
    client = SafetyClient(base_url="http://mock-safety")

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock, side_effect=httpx.ConnectError("Network down")):
        with pytest.raises(httpx.ConnectError):
            await client.notify_deviation_event(
                study_id="STUDY-101",
                site_id="SITE-01",
                deviation_id="DEV-001",
                title="Critical Deviation",
                severity="CRITICAL",
                user_id="user_1",
            )
