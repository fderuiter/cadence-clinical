"""Unit & integration test suite for CDISC Library REST API client.

Requirements: PRD-SYS-001
"""

import httpx
import pytest

from apps.designer.src.domain.cdisc.cdisc_library_client import (
    CdashDomainDefinition,
    CdiscLibraryClient,
    CdiscLibraryConfig,
    CodelistDefinition,
    SdtmDomainDefinition,
)


@pytest.mark.asyncio
async def test_cdisc_library_config_defaults() -> None:
    """Validate default configuration parameters.

    Requirements: PRD-SYS-001
    """
    config = CdiscLibraryConfig()
    assert config.api_key is None
    assert config.base_url == "https://api.library.cdisc.org/api"
    assert config.user_agent == "CadenceClinical/1.0"
    assert config.timeout == 30.0


@pytest.mark.asyncio
async def test_cdisc_library_client_local_fallback_products() -> None:
    """Validate product summary retrieval falls back to local catalog when no API key.

    Requirements: PRD-SYS-001
    """
    async with CdiscLibraryClient() as client:
        products = await client.get_products()
        assert isinstance(products, list)
        assert len(products) >= 3
        titles = [p.title for p in products]
        assert "CDASHIG v2.3" in titles
        assert "SDTMIG v3.4" in titles


@pytest.mark.asyncio
async def test_cdisc_library_client_get_cdash_domain_fallback() -> None:
    """Validate CDASH domain definition retrieval using local fallback.

    Requirements: PRD-SYS-001
    """
    async with CdiscLibraryClient() as client:
        domain_def = await client.get_cdash_domain("DM", version="2.3")
        assert isinstance(domain_def, CdashDomainDefinition)
        assert domain_def.domain_code == "DM"
        assert domain_def.version == "2.3"


@pytest.mark.asyncio
async def test_cdisc_library_client_get_sdtm_domain_fallback() -> None:
    """Validate SDTM domain definition retrieval using local fallback.

    Requirements: PRD-SYS-001
    """
    async with CdiscLibraryClient() as client:
        domain_def = await client.get_sdtm_domain("AE", version="3.4")
        assert isinstance(domain_def, SdtmDomainDefinition)
        assert domain_def.domain_code == "AE"
        assert domain_def.version == "3.4"


@pytest.mark.asyncio
async def test_cdisc_library_client_get_codelist_fallback() -> None:
    """Validate codelist definition retrieval using local fallback.

    Requirements: PRD-SYS-001
    """
    async with CdiscLibraryClient() as client:
        codelist = await client.get_codelist(
            "cdashct-2024-09-27", codelist_code="C66742"
        )
        assert isinstance(codelist, CodelistDefinition)
        assert codelist.codelist_code == "C66742"


@pytest.mark.asyncio
async def test_cdisc_library_client_mock_api_key_auth() -> None:
    """Validate CDISC Library client sends api-key header when configured.

    Requirements: PRD-SYS-001
    """

    def handle_request(request: httpx.Request) -> httpx.Response:
        assert request.headers.get("api-key") == "test-cdisc-api-key-12345"
        assert request.headers.get("User-Agent") == "CadenceClinical/1.0"
        if request.url.path == "/api/mdr/products":
            return httpx.Response(
                200,
                json={"_links": {"cdash": {"title": "CDASH API", "version": "2.3"}}},
            )
        if request.url.path == "/api/mdr/cdashig/2.3/domains/VS":
            return httpx.Response(
                200,
                json={
                    "name": "Vital Signs",
                    "description": "Vital signs measurement domain",
                    "fields": [],
                },
            )
        return httpx.Response(404, json={"error": "not found"})

    transport = httpx.MockTransport(handle_request)
    mock_http_client = httpx.AsyncClient(
        base_url="https://api.library.cdisc.org/api", transport=transport
    )

    config = CdiscLibraryConfig(api_key="test-cdisc-api-key-12345")
    async with CdiscLibraryClient(config=config, client=mock_http_client) as client:
        products = await client.get_products()
        assert len(products) == 1
        assert products[0].title == "CDASH API"

        vs_domain = await client.get_cdash_domain("VS", version="2.3")
        assert vs_domain.domain_code == "VS"
        assert vs_domain.name == "Vital Signs"
