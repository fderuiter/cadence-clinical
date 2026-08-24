"""Unit tests ensuring DesignerTicketsClient propagates 5xx HTTP status errors and network exceptions.

Requirements: Ensure exception handlers do not swallow errors or produce synthetic fallback ticket records ('OPEN').
"""

from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest

from apps.designer.adapters.tickets_client import DesignerTicketsClient
from apps.designer.domain.cdisc.ripple_models import (
    DomainQueue,
    OperationalTicketBlueprint,
)


@pytest.mark.asyncio
async def test_tickets_client_propagates_5xx_error():
    client = DesignerTicketsClient(base_url="http://mock-tickets")
    blueprint = OperationalTicketBlueprint(
        domain_queue=DomainQueue.DATA_CAPTURE_ECRF,
        title="Update eCRF Forms",
        description="Add Cohort B forms",
        category="CRF_AMENDMENT",
        priority="HIGH",
        gxp_severity="MAJOR",
        action_plan=["Update form definition"],
        assignee_role="Data Manager",
    )

    mock_resp = MagicMock()
    mock_resp.status_code = 500
    mock_resp.raise_for_status.side_effect = httpx.HTTPStatusError(
        "500 Internal Server Error",
        request=MagicMock(),
        response=mock_resp,
    )

    with patch(
        "httpx.AsyncClient.post", new_callable=AsyncMock, return_value=mock_resp
    ):
        with pytest.raises(httpx.HTTPStatusError) as exc_info:
            await client.dispatch_ticket(
                blueprint=blueprint,
                study_id="STUDY-101",
                user_id="designer_01",
                change_reason="Protocol amendment",
            )
        assert exc_info.value.response.status_code == 500


@pytest.mark.asyncio
async def test_tickets_client_propagates_network_error():
    client = DesignerTicketsClient(base_url="http://mock-tickets")
    blueprint = OperationalTicketBlueprint(
        domain_queue=DomainQueue.SUBJECT_MANAGEMENT_RTSM,
        title="Update RTSM Cohort Limits",
        description="Increase cohort cap to 50",
        category="RTSM_AMENDMENT",
        priority="CRITICAL",
        gxp_severity="CRITICAL",
        action_plan=["Update RTSM supply logic"],
        assignee_role="RTSM Lead",
    )

    with patch(
        "httpx.AsyncClient.post",
        new_callable=AsyncMock,
        side_effect=httpx.ConnectError("Service offline"),
    ):
        with pytest.raises(httpx.ConnectError):
            await client.dispatch_ticket(
                blueprint=blueprint,
                study_id="STUDY-101",
                user_id="designer_01",
                change_reason="Protocol amendment",
            )
