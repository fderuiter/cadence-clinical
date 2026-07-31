import os
from typing import Any, Dict, List, Optional

import httpx


class EVSClientError(Exception):
    """Base exception for NCI EVS client errors."""

    pass


class EVSNotFoundError(EVSClientError):
    """Raised when a concept/code is not found or is invalid (e.g. 404)."""

    pass


class EVSTimeoutError(EVSClientError):
    """Raised when a request to NCI EVS times out."""

    pass


class EVSTransportError(EVSClientError):
    """Raised for connection issues, transport failures, or non-404 HTTP errors."""

    pass


def normalize_concept(
    concept_data: Dict[str, Any], default_system: str
) -> Dict[str, Any]:
    """Normalize EVS concept to the target concept shape: code, decode, system, plus valid."""
    code = concept_data.get("code") or ""
    # "decode" should map to the preferred name, which is "name" or "displayName" in EVS
    decode = concept_data.get("name") or concept_data.get("displayName") or ""
    # "system" maps to terminology, or default to configured terminology/system
    system = concept_data.get("terminology") or default_system
    # "valid" maps to the active status (default to True if not present)
    valid = concept_data.get("active")
    if valid is None:
        valid = True
    else:
        valid = bool(valid)

    return {
        "code": code,
        "decode": decode,
        "system": system,
        "valid": valid,
    }


class NCIEVSClient:
    """Asynchronous, configurable NCI EVS REST client for NCIt/CDISC Controlled Terminology."""

    def __init__(
        self,
        base_url: Optional[str] = None,
        terminology: Optional[str] = None,
        timeout: Optional[httpx.Timeout] = None,
    ) -> None:
        """Initialize the client.

        Args:
            base_url (str, optional): The base URL of the EVS REST API. Defaults to NCI_EVS_BASE_URL env var or a safe default.
            terminology (str, optional): The terminology source. Defaults to NCI_EVS_TERMINOLOGY env var or 'ncit'.
            timeout (httpx.Timeout, optional): Custom timeout configuration. Defaults to configured environment variables or 5.0s.
        """
        self.base_url = (
            base_url
            or os.getenv("NCI_EVS_BASE_URL")
            or "https://api-evsrest.nci.nih.gov"
        ).rstrip("/")

        self.terminology = terminology or os.getenv("NCI_EVS_TERMINOLOGY") or "ncit"

        if timeout is not None:
            self.timeout = timeout
        else:
            connect = float(os.getenv("NCI_EVS_TIMEOUT_CONNECT", "5.0"))
            read = float(os.getenv("NCI_EVS_TIMEOUT_READ", "5.0"))
            write = float(os.getenv("NCI_EVS_TIMEOUT_WRITE", "5.0"))
            pool = float(os.getenv("NCI_EVS_TIMEOUT_POOL", "5.0"))
            self.timeout = httpx.Timeout(
                connect=connect,
                read=read,
                write=write,
                pool=pool,
            )

    async def get_concept(
        self, code: str, client: Optional[httpx.AsyncClient] = None
    ) -> Dict[str, Any]:
        """Fetch concept details by concept code.

        Args:
            code (str): The terminology concept code (e.g. C123).
            client (httpx.AsyncClient, optional): Shared client instance.

        Returns:
            Dict[str, Any]: The normalized concept dict.

        Raises:
            EVSNotFoundError: If the code is invalid or not found.
            EVSTimeoutError: If the request times out.
            EVSTransportError: For transport failures, connection issues, or non-404 HTTP errors.
        """
        url = f"{self.base_url}/api/v1/concept/{self.terminology}/{code}"

        try:
            if client is not None:
                response = await client.get(url)
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url)

            if response.status_code == 404:
                raise EVSNotFoundError(f"Concept not found or invalid: {code}")

            response.raise_for_status()

        except httpx.TimeoutException as e:
            raise EVSTimeoutError(f"EVS client request timed out: {str(e)}") from e

        except httpx.HTTPStatusError as e:
            if e.response.status_code == 404:
                raise EVSNotFoundError(f"Concept not found or invalid: {code}") from e
            elif e.response.status_code in (400, 422):
                is_invalid = False
                try:
                    body = e.response.json()
                    detail = str(body).lower()
                    if "not found" in detail or "invalid" in detail:
                        is_invalid = True
                except Exception:
                    pass
                if is_invalid:
                    raise EVSNotFoundError(
                        f"Concept not found or invalid: {code}"
                    ) from e
            raise EVSTransportError(
                f"HTTP error from EVS API: {e.response.status_code} - {e.response.text}"
            ) from e

        except httpx.RequestError as e:
            raise EVSTransportError(
                f"Transport failure contacting EVS API: {str(e)}"
            ) from e

        try:
            data = response.json()
        except Exception as e:
            raise EVSTransportError(
                f"Failed to parse EVS JSON response: {str(e)}"
            ) from e

        return normalize_concept(data, self.terminology)

    async def search_concepts(
        self,
        term: str,
        client: Optional[httpx.AsyncClient] = None,
        from_record: Optional[int] = None,
        page_size: Optional[int] = None,
    ) -> List[Dict[str, Any]]:
        """Perform a text search for terminology concepts.

        Args:
            term (str): The search term.
            client (httpx.AsyncClient, optional): Shared client instance.
            from_record (int, optional): The starting record offset.
            page_size (int, optional): The maximum number of results to return.

        Returns:
            List[Dict[str, Any]]: A list of normalized concept dictionaries.

        Raises:
            EVSTimeoutError: If the request times out.
            EVSTransportError: For transport failures, connection issues, or HTTP errors.
        """
        url = f"{self.base_url}/api/v1/concept/{self.terminology}/search"
        params = {"term": term}
        if from_record is not None:
            params["fromRecord"] = str(from_record)
        if page_size is not None:
            params["pageSize"] = str(page_size)

        try:
            if client is not None:
                response = await client.get(url, params=params)
            else:
                async with httpx.AsyncClient(timeout=self.timeout) as cli:
                    response = await cli.get(url, params=params)

            response.raise_for_status()

        except httpx.TimeoutException as e:
            raise EVSTimeoutError(f"EVS client request timed out: {str(e)}") from e

        except httpx.HTTPStatusError as e:
            raise EVSTransportError(
                f"HTTP error from EVS API: {e.response.status_code} - {e.response.text}"
            ) from e

        except httpx.RequestError as e:
            raise EVSTransportError(
                f"Transport failure contacting EVS API: {str(e)}"
            ) from e

        try:
            data = response.json()
        except Exception as e:
            raise EVSTransportError(
                f"Failed to parse EVS JSON response: {str(e)}"
            ) from e

        concepts_list = []
        if isinstance(data, list):
            concepts_list = data
        elif isinstance(data, dict):
            concepts_list = data.get("concepts") or data.get("results") or []

        results = []
        for c in concepts_list:
            if isinstance(c, dict):
                results.append(normalize_concept(c, self.terminology))

        return results
