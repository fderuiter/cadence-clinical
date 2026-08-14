# ruff: noqa: E402
import os
import sys

# Ensure repo root is on sys.path
repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if repo_root not in sys.path:
    sys.path.insert(0, repo_root)

import pytest

from tests.conftest import *  # noqa: E402, F401, F403


@pytest.fixture(autouse=True)
def mock_sidecar_service(monkeypatch):
    import httpx

    from apps.cdisc.main import app as sidecar_app
    from apps.execution.presentation.routers import exports

    async def mock_call_sdtm(domain: str, payload: dict) -> dict:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=sidecar_app)
        ) as client:
            res = await client.post(
                f"http://localhost/api/v1/cdisc/sdtm/{domain}", json=payload
            )
            res.raise_for_status()
            return res.json()

    async def mock_call_adam(dataset: str, payload: dict) -> dict:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=sidecar_app)
        ) as client:
            res = await client.post(
                f"http://localhost/api/v1/cdisc/adam/{dataset}", json=payload
            )
            res.raise_for_status()
            return res.json()

    async def mock_call_bundle(payload: dict) -> dict:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=sidecar_app)
        ) as client:
            res = await client.post(
                "http://localhost/api/v1/cdisc/bundle", json=payload
            )
            res.raise_for_status()
            return res.json()

    monkeypatch.setattr(exports, "call_sidecar_sdtm", mock_call_sdtm)
    monkeypatch.setattr(exports, "call_sidecar_adam", mock_call_adam)
    monkeypatch.setattr(exports, "call_sidecar_bundle", mock_call_bundle)
