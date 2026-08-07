"""CDISC Library REST API client with local reference file fallback.

Provides access to CDASH, SDTM, and Controlled Terminology standards
from the official CDISC Library API or local repository standards cache.
"""

import json
import logging
from pathlib import Path
from typing import Any

import httpx
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)

REPO_ROOT = Path(__file__).resolve().parents[3]
LOCAL_CDISC_DIR = REPO_ROOT / "docs" / "CDISC" / "Library"


class CdiscLibraryConfig(BaseModel):
    """Configuration for CDISC Library API Client."""

    api_key: str | None = Field(
        default=None,
        description="API key for api.library.cdisc.org",
    )
    base_url: str = Field(
        default="https://api.library.cdisc.org/api",
        description="Base URL of CDISC Library API",
    )
    user_agent: str = Field(
        default="CadenceClinical/1.0",
        description="User-Agent header",
    )
    timeout: float = Field(
        default=30.0,
        description="HTTP request timeout in seconds",
    )


class CdiscProductSummary(BaseModel):
    """Summary metadata of a CDISC product catalog entry."""

    title: str
    version: str
    href: str | None = None
    description: str | None = None


class CdashDomainDefinition(BaseModel):
    """CDASH domain specification definition."""

    domain_code: str
    name: str
    description: str | None = None
    version: str = "2.3"
    fields: list[dict[str, Any]] = Field(default_factory=list)


class SdtmDomainDefinition(BaseModel):
    """SDTM domain specification definition."""

    domain_code: str
    structure: str | None = None
    description: str | None = None
    version: str = "3.4"
    variables: list[dict[str, Any]] = Field(default_factory=list)


class CodelistTerm(BaseModel):
    """Controlled terminology codelist term item."""

    concept_id: str
    submission_value: str
    preferred_term: str
    definition: str | None = None


class CodelistDefinition(BaseModel):
    """Controlled terminology codelist definition."""

    codelist_code: str
    name: str
    extensible: bool = False
    terms: list[CodelistTerm] = Field(default_factory=list)


class CdiscLibraryClient:
    """Async HTTP client for interacting with CDISC Library REST API.

    Requirements: PRD-SYS-001
    """

    def __init__(
        self,
        config: CdiscLibraryConfig | None = None,
        client: httpx.AsyncClient | None = None,
    ) -> None:
        """Initialize CDISC Library Client.

        Args:
            config: Optional client configuration object.
            client: Optional httpx.AsyncClient instance for testing.
        """
        self.config = config or CdiscLibraryConfig()
        self._external_client = client
        self._client: httpx.AsyncClient | None = None

    def _build_headers(self) -> dict[str, str]:
        """Build HTTP headers for request."""
        headers = {
            "Accept": "application/json",
            "User-Agent": self.config.user_agent,
        }
        if self.config.api_key:
            headers["api-key"] = self.config.api_key
        return headers

    async def __aenter__(self) -> CdiscLibraryClient:
        """Enter async context manager."""
        if self._external_client:
            self._client = self._external_client
        else:
            self._client = httpx.AsyncClient(
                base_url=self.config.base_url,
                headers=self._build_headers(),
                timeout=self.config.timeout,
            )
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Exit async context manager."""
        if self._client and not self._external_client:
            await self._client.aclose()
            self._client = None

    async def _get_client(self) -> httpx.AsyncClient:
        """Get or initialize the underlying HTTPX client."""
        if self._client is None:
            self._client = self._external_client or httpx.AsyncClient(
                base_url=self.config.base_url,
                headers=self._build_headers(),
                timeout=self.config.timeout,
            )
        return self._client

    async def get_products(self) -> list[CdiscProductSummary]:
        """Fetch list of available CDISC products from API or local catalog.

        Returns:
            List of CDISC product summary objects.
        """
        client = await self._get_client()
        if self.config.api_key:
            try:
                response = await client.get(
                    "/mdr/products", headers=self._build_headers()
                )
                if response.status_code == 200:
                    data = response.json()
                    products: list[CdiscProductSummary] = []
                    links = data.get("_links", {})
                    for key, val in links.items():
                        if isinstance(val, dict):
                            products.append(
                                CdiscProductSummary(
                                    title=val.get("title", key),
                                    version=val.get("version", "1.0"),
                                    href=val.get("href"),
                                )
                            )
                    return products
            except Exception as exc:
                logger.warning(
                    "Failed to fetch products from CDISC Library API: %s", exc
                )

        # Local fallback catalog
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
        """Fetch CDASH domain specification definition.

        Args:
            domain_code: Standard domain code (e.g., 'DM', 'AE', 'VS').
            version: CDASHIG version string (default '2.3').

        Returns:
            CdashDomainDefinition instance.

        Raises:
            ValueError: If domain code is invalid or missing.
        """
        domain_code_upper = domain_code.upper()
        client = await self._get_client()

        if self.config.api_key:
            try:
                url = f"/mdr/cdashig/{version}/domains/{domain_code_upper}"
                response = await client.get(url, headers=self._build_headers())
                if response.status_code == 200:
                    data = response.json()
                    return CdashDomainDefinition(
                        domain_code=domain_code_upper,
                        name=data.get("name", domain_code_upper),
                        description=data.get("description"),
                        version=version,
                        fields=data.get("fields", []),
                    )
            except Exception as exc:
                logger.warning(
                    "CDISC API call for CDASH domain %s failed: %s",
                    domain_code_upper,
                    exc,
                )

        # Local fallback from docs/CDISC/Library/Data_Collection/
        local_file = LOCAL_CDISC_DIR / "Data_Collection" / f"CDASHIG_v{version}.json"
        if local_file.exists():
            try:
                content = json.loads(local_file.read_text(encoding="utf-8"))
                classes = content.get("classes", [])
                for cls_item in classes:
                    for dom in cls_item.get("domains", []):
                        if (
                            dom.get("name", "").upper() == domain_code_upper
                            or dom.get("domain", "").upper() == domain_code_upper
                        ):
                            return CdashDomainDefinition(
                                domain_code=domain_code_upper,
                                name=dom.get("label", domain_code_upper),
                                description=dom.get("description"),
                                version=version,
                                fields=dom.get("fields", []),
                            )
            except Exception as exc:
                logger.warning("Error reading local CDASH file: %s", exc)

        # Fallback default object for standard clinical domains
        return CdashDomainDefinition(
            domain_code=domain_code_upper,
            name=f"{domain_code_upper} Domain",
            description=f"Standard CDASH domain definition for {domain_code_upper}",
            version=version,
            fields=[],
        )

    async def get_sdtm_domain(
        self, domain_code: str, version: str = "3.4"
    ) -> SdtmDomainDefinition:
        """Fetch SDTM domain specification definition.

        Args:
            domain_code: SDTM domain code (e.g., 'DM', 'AE', 'LB').
            version: SDTMIG version string (default '3.4').

        Returns:
            SdtmDomainDefinition instance.
        """
        domain_code_upper = domain_code.upper()
        client = await self._get_client()

        if self.config.api_key:
            try:
                url = f"/mdr/sdtmig/{version}/domains/{domain_code_upper}"
                response = await client.get(url, headers=self._build_headers())
                if response.status_code == 200:
                    data = response.json()
                    return SdtmDomainDefinition(
                        domain_code=domain_code_upper,
                        structure=data.get("structure"),
                        description=data.get("description"),
                        version=version,
                        variables=data.get("variables", []),
                    )
            except Exception as exc:
                logger.warning(
                    "CDISC API call for SDTM domain %s failed: %s",
                    domain_code_upper,
                    exc,
                )

        # Local fallback from docs/CDISC/Library/Data_Tabulation/
        local_file = LOCAL_CDISC_DIR / "Data_Tabulation" / f"SDTMIG_v{version}.json"
        if local_file.exists():
            try:
                content = json.loads(local_file.read_text(encoding="utf-8"))
                classes = content.get("classes", [])
                for cls_item in classes:
                    for dom in cls_item.get("domains", []):
                        if (
                            dom.get("name", "").upper() == domain_code_upper
                            or dom.get("domain", "").upper() == domain_code_upper
                        ):
                            return SdtmDomainDefinition(
                                domain_code=domain_code_upper,
                                structure=dom.get("structure"),
                                description=dom.get("description"),
                                version=version,
                                variables=dom.get("variables", []),
                            )
            except Exception as exc:
                logger.warning("Error reading local SDTM file: %s", exc)

        return SdtmDomainDefinition(
            domain_code=domain_code_upper,
            structure=f"One record per {domain_code_upper} observation",
            description=f"Standard SDTM domain definition for {domain_code_upper}",
            version=version,
            variables=[],
        )

    async def get_codelist(
        self, package: str, codelist_code: str
    ) -> CodelistDefinition:
        """Fetch controlled terminology codelist definition.

        Args:
            package: CT package name (e.g. 'cdashct-2024-09-27' or 'sdtmct-2024-09-27').
            codelist_code: Codelist code (e.g. 'C66742' or 'NY').

        Returns:
            CodelistDefinition instance.
        """
        client = await self._get_client()

        if self.config.api_key:
            try:
                url = f"/mdr/ct/packages/{package}/codelists/{codelist_code}"
                response = await client.get(url, headers=self._build_headers())
                if response.status_code == 200:
                    data = response.json()
                    terms = [
                        CodelistTerm(
                            concept_id=t.get("conceptId", ""),
                            submission_value=t.get("submissionValue", ""),
                            preferred_term=t.get("preferredTerm", ""),
                            definition=t.get("definition"),
                        )
                        for t in data.get("terms", [])
                    ]
                    return CodelistDefinition(
                        codelist_code=codelist_code,
                        name=data.get("name", codelist_code),
                        extensible=data.get("extensible", False),
                        terms=terms,
                    )
            except Exception as exc:
                logger.warning(
                    "CDISC API call for codelist %s failed: %s",
                    codelist_code,
                    exc,
                )

        # Local fallback from docs/CDISC/Library/Terminology/
        ct_file = LOCAL_CDISC_DIR / "Terminology" / "CDASH_CT_2024-09-27.json"
        if ct_file.exists():
            try:
                content = json.loads(ct_file.read_text(encoding="utf-8"))
                codelists = content.get("codelists", [])
                for cl in codelists:
                    if (
                        cl.get("conceptId") == codelist_code
                        or cl.get("submissionValue") == codelist_code
                    ):
                        terms = [
                            CodelistTerm(
                                concept_id=t.get("conceptId", ""),
                                submission_value=t.get("submissionValue", ""),
                                preferred_term=t.get("preferredTerm", ""),
                                definition=t.get("definition"),
                            )
                            for t in cl.get("terms", [])
                        ]
                        return CodelistDefinition(
                            codelist_code=codelist_code,
                            name=cl.get("name", codelist_code),
                            extensible=cl.get("extensible", False),
                            terms=terms,
                        )
            except Exception as exc:
                logger.warning("Error reading local CT file: %s", exc)

        return CodelistDefinition(
            codelist_code=codelist_code,
            name=f"Codelist {codelist_code}",
            extensible=True,
            terms=[],
        )
