import pytest
import uuid
from unittest.mock import AsyncMock, patch
from apps.designer.validator import generate_alignment_report

@pytest.mark.asyncio
async def test_generate_alignment_report():
    study_id = str(uuid.uuid4())
    
    mock_payload = {
        "id": study_id,
        "name": "Test Study",
        "description": None,
        "label": None,
        "versions": [],
        "documentedBy": [],
        "instanceType": "Study"
    }

    mock_response = AsyncMock()
    from unittest.mock import MagicMock
    mock_response.json = MagicMock(return_value=mock_payload)
    mock_response.raise_for_status = MagicMock()
    
    # Mock httpx.AsyncClient
    class MockAsyncClient:
        async def __aenter__(self):
            return self
        
        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass
            
        async def get(self, url, timeout=None):
            return mock_response
            
    with patch("httpx.AsyncClient", return_value=MockAsyncClient()):
        report = await generate_alignment_report(study_id)
        
    assert report.study_id == study_id
    assert len(report.unmapped_activities) == 0
    assert len(report.complete_activities) == 0
    assert len(report.incomplete_activities) == 0

