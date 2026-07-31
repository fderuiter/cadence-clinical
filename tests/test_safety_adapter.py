import pytest
import os
import httpx
from sae_icsr import IndividualCaseSafetyReport
from apps.safety.safety_adapter import (
    pseudonymize_value,
    OutboundTransmissionAdapter,
    prepare_and_deidentify_icsr,
)
from tests.test_safety_e2b import get_valid_icsr


def test_pseudonymize_value_pattern():
    """Verify that pseudonymization is deterministic and uses HMAC-SHA256."""
    salt = "test-salt"
    val = "SUBJ-001"
    pseudo_1 = pseudonymize_value(val, salt)
    pseudo_2 = pseudonymize_value(val, salt)

    assert pseudo_1 == pseudo_2
    assert len(pseudo_1) == 64  # SHA256 hex digest length


def test_prepare_and_deidentify_icsr():
    """Verify that direct PII (birth_date) is stripped and patient_id is pseudonymized."""
    icsr = get_valid_icsr()
    icsr.patient.patient_id = "SUBJ-001"
    icsr.patient.birth_date = "1980-01-01"

    salt = "another-safety-salt"
    deidentified_icsr = prepare_and_deidentify_icsr(icsr, salt=salt)

    # Original shouldn't be mutated because of deepcopy
    assert icsr.patient.patient_id == "SUBJ-001"
    assert icsr.patient.birth_date == "1980-01-01"

    # Deidentified must be changed
    assert deidentified_icsr.patient.birth_date is None
    expected_pseudo = pseudonymize_value("SUBJ-001", salt)
    assert deidentified_icsr.patient.patient_id == expected_pseudo


@pytest.mark.asyncio
async def test_outbound_transmission_adapter_with_client():
    """Verify transmission adapter submits XML content correctly to endpoint."""
    class MockAsyncClient:
        def __init__(self):
            self.posts = []

        async def post(self, url, content, headers=None):
            self.posts.append({"url": url, "content": content, "headers": headers})
            return httpx.Response(status_code=200, content=b"Transmission OK")

    mock_client = MockAsyncClient()
    adapter = OutboundTransmissionAdapter(
        endpoint_url="http://mock-endpoint/api/pv",
        client=mock_client,
    )

    xml_content = "<ichicsr></ichicsr>"
    response = await adapter.transmit(xml_content)

    assert response.status_code == 200
    assert response.text == "Transmission OK"
    assert len(mock_client.posts) == 1
    assert mock_client.posts[0]["url"] == "http://mock-endpoint/api/pv"
    assert mock_client.posts[0]["content"] == xml_content
    assert mock_client.posts[0]["headers"] == {"Content-Type": "application/xml"}
