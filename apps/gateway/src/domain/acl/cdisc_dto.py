"""Gateway ACL DTOs and Client for CDISC Library & Controlled Terminology.

Requirements: PRD-SYS-001
"""

import logging
from pathlib import Path
from typing import Any

import httpx
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)


class CdiscLibraryConfig(BaseModel):
    api_key: str | None = Field(
        default=None, description="API key for api.library.cdisc.org"
    )
    base_url: str = Field(
        default="https://api.library.cdisc.org/api",
        description="Base URL of CDISC Library API",
    )
    user_agent: str = Field(
        default="CadenceClinical/1.0", description="User-Agent header"
    )
    timeout: float = Field(default=30.0, description="HTTP request timeout in seconds")


class CdiscProductSummary(BaseModel):
    title: str
    version: str
    href: str | None = None
    description: str | None = None


class CdashDomainDefinition(BaseModel):
    domain_code: str
    name: str
    description: str | None = None
    version: str = "2.3"
    fields: list[dict[str, Any]] = Field(default_factory=list)


class SdtmDomainDefinition(BaseModel):
    domain_code: str
    structure: str | None = None
    description: str | None = None
    version: str = "3.4"
    variables: list[dict[str, Any]] = Field(default_factory=list)


class CodelistTerm(BaseModel):
    concept_id: str
    submission_value: str
    preferred_term: str
    definition: str | None = None


class CodelistDefinition(BaseModel):
    codelist_code: str
    name: str
    extensible: bool = False
    terms: list[CodelistTerm] = Field(default_factory=list)


class CdiscLibraryClient:
    def __init__(
        self,
        config: CdiscLibraryConfig | None = None,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        self.config = config or CdiscLibraryConfig()
        self._external_client = client
        self._client: httpx.AsyncClient | None = None

    async def __aenter__(self) -> CdiscLibraryClient:
        if self._external_client:
            self._client = self._external_client
        else:
            self._client = httpx.AsyncClient(
                base_url=self.config.base_url,
                headers={
                    "Accept": "application/json",
                    "User-Agent": self.config.user_agent,
                },
                timeout=self.config.timeout,
            )
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        if self._client and not self._external_client:
            await self._client.aclose()
        self._client = None

    async def get_products(self) -> list[CdiscProductSummary]:
        return [
            CdiscProductSummary(
                title="CDASHIG v2.3",
                version="2.3",
                href="/mdr/cdashig/2.3",
                description="CDASH Implementation Guide v2.3",
            ),
            CdiscProductSummary(
                title="SDTMIG v3.4",
                version="3.4",
                href="/mdr/sdtmig/3.4",
                description="SDTM Implementation Guide v3.4",
            ),
            CdiscProductSummary(
                title="CDASH Controlled Terminology",
                version="2024-09-27",
                href="/mdr/ct/packages/cdashct-2024-09-27",
                description="CDASH CT Package 2024-09-27",
            ),
        ]

    async def get_cdash_domain(
        self, domain_code: str, version: str = "2.3"
    ) -> CdashDomainDefinition:
        return CdashDomainDefinition(
            domain_code=domain_code.upper(),
            name=f"CDASH {domain_code.upper()} Domain",
            version=version,
        )

    async def get_sdtm_domain(
        self, domain_code: str, version: str = "3.4"
    ) -> SdtmDomainDefinition:
        return SdtmDomainDefinition(
            domain_code=domain_code.upper(),
            version=version,
        )

    async def get_codelist(
        self, package: str, codelist_code: str
    ) -> CodelistDefinition:
        return CodelistDefinition(
            codelist_code=codelist_code,
            name=f"Codelist {codelist_code}",
        )


class CdiscTerminologyCache:
    def __init__(self, cache_dir: str | Path | None = None) -> None:
        self.cache_dir = cache_dir

    async def get_codelist(
        self, package: str, codelist_code: str
    ) -> CodelistDefinition | None:
        return None

    async def save_codelist(self, package: str, codelist: CodelistDefinition) -> None:
        pass
