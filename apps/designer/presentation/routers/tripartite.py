"""FastAPI Router for Tripartite eCRF Designer IDE Endpoints.

Provides endpoints for:
- Form schema compilation & validation
- CDISC Global Library Smart Blocks catalog & instantiation
- Protocol amendment diffing & In-Flight Patient Migration Matrix calculation
- Automated synthetic UAT simulation & evidence reporting
- 21 CFR Part 11 electronic signature publication sealing
"""

from typing import Any

from fastapi import APIRouter, Header, HTTPException, status
from pydantic import BaseModel, Field

from apps.designer.domain.tripartite_service import (
    AmendmentDiffService,
    PublicationAuditService,
    TripartiteCompilerService,
    UatRunnerService,
)
from packages.ddf.smart_blocks import SmartBlocksCatalogService
from packages.ddf.tripartite import (
    PatientMigrationMatrix,
    SyntheticPayload,
    TripartiteFormDefinition,
    UatEvidenceReport,
)

router = APIRouter(prefix="/api/v1/designer", tags=["Tripartite eCRF Designer"])


class DiffRequest(BaseModel):
    """Request payload for comparing two form versions."""

    base_form: TripartiteFormDefinition
    amended_form: TripartiteFormDefinition


class UatExecutionRequest(BaseModel):
    """Request payload for executing synthetic UAT suite."""

    form_definition: TripartiteFormDefinition
    payloads: list[SyntheticPayload] = Field(default_factory=list)


class PublicationRequest(BaseModel):
    """Request payload for publishing and sealing a form."""

    form_definition: TripartiteFormDefinition
    reason_for_change: str
    step_up_signature_token: str


@router.post("/studies/{study_id}/forms/compile", status_code=status.HTTP_200_OK)
async def compile_form_schema(
    study_id: str,
    form_def: TripartiteFormDefinition,
) -> dict[str, Any]:
    """Compile and validate a Tripartite Form Definition."""
    return TripartiteCompilerService.compile_and_validate(form_def)


@router.get("/library/smart-blocks", status_code=status.HTTP_200_OK)
async def list_smart_blocks() -> list[dict[str, Any]]:
    """List pre-configured CDISC clinical Smart Blocks."""
    return SmartBlocksCatalogService.get_catalog()


@router.post(
    "/library/smart-blocks/{block_id}/instantiate",
    status_code=status.HTTP_201_CREATED,
    response_model=TripartiteFormDefinition,
)
async def instantiate_smart_block(
    block_id: str,
    form_id: str,
) -> TripartiteFormDefinition:
    """Instantiate a CDISC Smart Block into a complete Tripartite Form Schema."""
    try:
        return SmartBlocksCatalogService.instantiate_smart_block(block_id, form_id)
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(e)) from e


@router.post(
    "/studies/{study_id}/amendments/diff",
    status_code=status.HTTP_200_OK,
    response_model=PatientMigrationMatrix,
)
async def calculate_amendment_diff(
    study_id: str,
    diff_req: DiffRequest,
) -> PatientMigrationMatrix:
    """Calculate structural diff between base and amended form versions."""
    return AmendmentDiffService.calculate_diff(
        diff_req.base_form, diff_req.amended_form
    )


@router.post(
    "/studies/{study_id}/forms/{form_id}/uat/execute",
    status_code=status.HTTP_200_OK,
    response_model=UatEvidenceReport,
)
async def execute_uat_simulation(
    study_id: str,
    form_id: str,
    uat_req: UatExecutionRequest,
) -> UatEvidenceReport:
    """Execute synthetic UAT simulation suite and generate 21 CFR Part 11 Evidence Report."""
    return UatRunnerService.execute_uat_suite(uat_req.form_definition, uat_req.payloads)


@router.post(
    "/studies/{study_id}/forms/{form_id}/publish",
    status_code=status.HTTP_200_OK,
)
async def publish_and_seal_form(
    study_id: str,
    form_id: str,
    pub_req: PublicationRequest,
    x_user_id: str = Header(default="system", alias="X-User-Id"),
) -> dict[str, Any]:
    """Publish and cryptographically seal form definition with step-up electronic signature."""
    try:
        return PublicationAuditService.seal_publication(
            form_def=pub_req.form_definition,
            published_by=x_user_id,
            reason_for_change=pub_req.reason_for_change,
            step_up_signature_token=pub_req.step_up_signature_token,
        )
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST, detail=str(e)
        ) from e
