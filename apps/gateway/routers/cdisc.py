"""API Gateway router for CDISC Standards & Controlled Terminology.

Provides REST endpoints for fetching CDASH domains, SDTM variables, and
controlled terminology codelists from the CDISC Library or local cache.

Requirements: PRD-SYS-001
"""

import os

from fastapi import APIRouter, Depends, Query, status

from apps.designer.src.domain.cdisc.cdisc_library_client import (
    CdashDomainDefinition,
    CdiscLibraryClient,
    CdiscLibraryConfig,
    CdiscProductSummary,
    CodelistDefinition,
    SdtmDomainDefinition,
)
from apps.designer.src.domain.cdisc.terminology_cache import CdiscTerminologyCache
from packages.security.middleware import get_current_user

router = APIRouter()


def _get_cdisc_client() -> CdiscLibraryClient:
    """Instantiate CDISC Library Client with environment config."""
    api_key = os.getenv("CDISC_LIBRARY_API_KEY")
    config = CdiscLibraryConfig(api_key=api_key)
    return CdiscLibraryClient(config=config)


def _get_terminology_cache() -> CdiscTerminologyCache:
    """Instantiate CDISC Terminology Cache."""
    return CdiscTerminologyCache()


@router.get(
    "/products",
    response_model=list[CdiscProductSummary],
    status_code=status.HTTP_200_OK,
)
async def list_cdisc_products(
    user: dict = Depends(get_current_user),
) -> list[CdiscProductSummary]:
    """List available CDISC products and standards catalogs.

    Requirements: PRD-SYS-001
    """
    async with _get_cdisc_client() as client:
        return await client.get_products()


@router.get(
    "/cdash/{domain_code}",
    response_model=CdashDomainDefinition,
    status_code=status.HTTP_200_OK,
)
async def get_cdash_domain_definition(
    domain_code: str,
    version: str = Query(default="2.3", description="CDASHIG version"),
    user: dict = Depends(get_current_user),
) -> CdashDomainDefinition:
    """Retrieve CDASH domain specification definition.

    Requirements: PRD-SYS-001
    """
    async with _get_cdisc_client() as client:
        return await client.get_cdash_domain(domain_code=domain_code, version=version)


@router.get(
    "/sdtm/{domain_code}",
    response_model=SdtmDomainDefinition,
    status_code=status.HTTP_200_OK,
)
async def get_sdtm_domain_definition(
    domain_code: str,
    version: str = Query(default="3.4", description="SDTMIG version"),
    user: dict = Depends(get_current_user),
) -> SdtmDomainDefinition:
    """Retrieve SDTM domain specification definition.

    Requirements: PRD-SYS-001
    """
    async with _get_cdisc_client() as client:
        return await client.get_sdtm_domain(domain_code=domain_code, version=version)


@router.get(
    "/codelists/{codelist_code}",
    response_model=CodelistDefinition,
    status_code=status.HTTP_200_OK,
)
async def get_controlled_terminology_codelist(
    codelist_code: str,
    package: str = Query(default="cdashct-2024-09-27", description="CT package name"),
    user: dict = Depends(get_current_user),
) -> CodelistDefinition:
    """Retrieve controlled terminology codelist definition with local cache check.

    Requirements: PRD-SYS-001
    """
    cache = _get_terminology_cache()
    cached = await cache.get_codelist(package=package, codelist_code=codelist_code)
    if cached:
        return cached

    async with _get_cdisc_client() as client:
        codelist = await client.get_codelist(
            package=package, codelist_code=codelist_code
        )
        if codelist and codelist.terms:
            await cache.save_codelist(package=package, codelist=codelist)
        return codelist
