from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel, Field

from apps.etmf.application.classification_service import classify_tmf_document
from apps.etmf.domain.tmf_reference_model import (
    get_active_catalog,
    get_catalog,
)
from packages.security.rbac import Principal, require_permission, require_study_scope

router = APIRouter(prefix="/api/v1/etmf", tags=["Taxonomy"])


class TaxonomyArtifactNode(BaseModel):
    """
    Representation of an artifact node in the taxonomy structure.
    """

    artifact_code: str
    artifact_name: str


class TaxonomySectionNode(BaseModel):
    """
    Representation of a section node in the taxonomy structure.
    """

    section_code: str
    section_name: str
    artifacts: list[TaxonomyArtifactNode]


class TaxonomyZoneNode(BaseModel):
    """
    Representation of a zone node in the taxonomy structure.
    """

    zone_code: int
    zone_name: str
    sections: list[TaxonomySectionNode]


class TaxonomyCatalogResponse(BaseModel):
    """
    Top-level taxonomy catalog response representation.
    """

    version: str
    zones: list[TaxonomyZoneNode]


class AutoFileRequest(BaseModel):
    """
    Request model for auto-filing/classification suggestion.
    """

    filename: str
    artifact_type: str | None = Field(None, description="Optional artifact type hint")
    free_text: str | None = Field(None, description="Optional free-text hint")
    study_id: str | None = Field(
        None, description="Optional study ID for scope enforcement"
    )


class AutoFileResponse(BaseModel):
    """
    Response model for auto-filing/classification suggestion.
    """

    resolved_zone: int
    resolved_section: str
    artifact_code: str
    artifact_type: str
    match_basis: str


@router.get("/taxonomy", response_model=TaxonomyCatalogResponse)
async def get_taxonomy_catalog(
    version: str | None = Query(None, description="Optional taxonomy version"),
    principal: Principal = Depends(require_permission("etmf_taxonomy:read")),
) -> TaxonomyCatalogResponse:
    """
    Expose the full static DIA TMF catalog as a browsable tree.
    """
    catalog_version = version or get_active_catalog().version
    try:
        catalog = get_catalog(catalog_version)
    except KeyError:
        raise HTTPException(
            status_code=404,
            detail=f"Taxonomy catalog version '{catalog_version}' not found.",
        )

    zones_list = []
    for z in catalog.zones:
        sections_list = []
        for s in z.sections:
            artifacts_list = []
            for a in s.artifacts:
                artifacts_list.append(
                    TaxonomyArtifactNode(
                        artifact_code=a.code,
                        artifact_name=a.name,
                    )
                )
            sections_list.append(
                TaxonomySectionNode(
                    section_code=s.code,
                    section_name=s.name,
                    artifacts=artifacts_list,
                )
            )
        zones_list.append(
            TaxonomyZoneNode(
                zone_code=z.code,
                zone_name=z.name,
                sections=sections_list,
            )
        )

    return TaxonomyCatalogResponse(
        version=catalog_version,
        zones=zones_list,
    )


@router.post("/taxonomy/classify", response_model=AutoFileResponse)
@router.post("/classify", response_model=AutoFileResponse)
async def suggest_classification(
    payload: AutoFileRequest,
    principal: Principal = Depends(require_permission("etmf_taxonomy:read")),
) -> AutoFileResponse:
    """
    Provide automatic classification/auto-filing suggestions for a document.
    """
    res = classify_tmf_document(
        filename=payload.filename,
        artifact_type=payload.artifact_type,
        free_text=payload.free_text,
    )
    if res is None:
        raise HTTPException(
            status_code=422,
            detail="Unable to auto-classify document with the provided parameters.",
        )

    return AutoFileResponse(
        resolved_zone=res.resolved_zone,
        resolved_section=res.resolved_section,
        artifact_code=res.artifact_code,
        artifact_type=res.artifact_type,
        match_basis=res.match_basis,
    )


@router.post("/auto-file", response_model=AutoFileResponse)
async def auto_file_suggestion(
    payload: AutoFileRequest,
    principal: Principal = Depends(require_permission("etmf_document:read")),
    study_scope: Principal = Depends(require_study_scope()),
) -> AutoFileResponse:
    """
    Provide automatic classification/auto-filing suggestions for a document with study scope.
    """
    res = classify_tmf_document(
        filename=payload.filename,
        artifact_type=payload.artifact_type,
        free_text=payload.free_text,
    )
    if res is None:
        raise HTTPException(
            status_code=422,
            detail="Unable to auto-classify document with the provided parameters.",
        )

    return AutoFileResponse(
        resolved_zone=res.resolved_zone,
        resolved_section=res.resolved_section,
        artifact_code=res.artifact_code,
        artifact_type=res.artifact_type,
        match_basis=res.match_basis,
    )
