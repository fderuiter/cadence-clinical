"""FastAPI presentation router for eTMF/eISF multimodal document intelligence and DIA classification."""

from fastapi import APIRouter, Depends, HTTPException, Query, status

from apps.etmf.application.document_intelligence_use_case import (
    AnalyzeDocumentIntelligenceUseCase,
    CRAQCReviewUseCase,
    GetQCQueueUseCase,
    StageClassifiedArtifactUseCase,
)
from apps.etmf.domain.exceptions import DocumentNotFoundError
from apps.etmf.domain.intelligence_models import (
    CRAQCStagingItem,
    DocumentIntelligenceReport,
    ExtractedRegulatoryMetadata,
    SignatureAnalysisResult,
)
from apps.etmf.domain.ports import ETMFRepositoryPort
from apps.etmf.domain.services.dia_classifier import (
    DIAReferenceModelClassifier,
)
from apps.etmf.domain.services.document_intelligence_parser import (
    DocumentIntelligenceParser,
)
from apps.etmf.domain.services.metadata_extractor import (
    RegulatoryMetadataExtractor,
)
from apps.etmf.domain.services.signature_analyzer import (
    SignatureCompletenessAnalyzer,
)
from apps.etmf.infrastructure.database import transactional
from apps.etmf.presentation.dtos import (
    CRAQCReviewRequest,
    DocumentIntelligenceAnalyzeRequest,
    DocumentIntelligenceClassifyRequest,
    DocumentIntelligenceMetadataRequest,
    DocumentIntelligenceSignatureRequest,
    DocumentResponse,
    StageDocumentQCRequest,
    to_document_response,
)
from packages.security.rbac import Principal, require_permission

router = APIRouter(prefix="/api/v1/etmf/intelligence", tags=["Document Intelligence"])


def get_etmf_repository() -> ETMFRepositoryPort:
    import apps.etmf.main as main_module

    if hasattr(main_module, "_repo_instance"):
        return main_module._repo_instance
    from apps.etmf.infrastructure.repositories import SQLETMFRepository

    return SQLETMFRepository()


@router.post(
    "/analyze",
    response_model=DocumentIntelligenceReport,
    status_code=status.HTTP_200_OK,
    summary="Execute end-to-end multimodal document intelligence pipeline",
)
async def analyze_document_endpoint(
    payload: DocumentIntelligenceAnalyzeRequest,
    principal: Principal = Depends(require_permission("etmf_document:read")),
) -> DocumentIntelligenceReport:
    """Run layout parsing, DIA taxonomy classification, metadata extraction, and signature verification."""
    use_case = AnalyzeDocumentIntelligenceUseCase()
    return use_case.execute(
        content=payload.content,
        filename=payload.filename,
        mime_type=payload.mime_type,
        study_id_hint=payload.study_id,
        site_id_hint=payload.site_id,
        artifact_hint=payload.artifact_hint,
        free_text=payload.free_text,
        taxonomy_version=payload.taxonomy_version,
        document_id=payload.document_id,
    )


@router.post(
    "/classify",
    response_model=dict,
    status_code=status.HTTP_200_OK,
    summary="Execute multi-signal DIA Reference Model taxonomy classification",
)
async def classify_document_endpoint(
    payload: DocumentIntelligenceClassifyRequest,
    principal: Principal = Depends(require_permission("etmf_taxonomy:read")),
) -> dict:
    """Classify document content against DIA TMF Reference Model v3.2.0."""
    parser = DocumentIntelligenceParser()
    classifier = DIAReferenceModelClassifier()

    content = payload.content or ""
    parsed_doc = parser.parse(content, filename=payload.filename)

    primary, alternatives, conf_tier, qc_rec, eisf_mapping = classifier.classify(
        parsed_doc=parsed_doc,
        filename=payload.filename,
        artifact_hint=payload.artifact_hint,
        free_text=payload.free_text,
        taxonomy_version=payload.taxonomy_version,
    )

    return {
        "primary_classification": primary.model_dump(),
        "alternative_candidates": [a.model_dump() for a in alternatives],
        "confidence_tier": conf_tier.value,
        "qc_recommendation": qc_rec.value,
        "eisf_target_mapping": eisf_mapping,
    }


@router.post(
    "/extract-metadata",
    response_model=ExtractedRegulatoryMetadata,
    status_code=status.HTTP_200_OK,
    summary="Extract regulatory and trial metadata from document",
)
async def extract_metadata_endpoint(
    payload: DocumentIntelligenceMetadataRequest,
    principal: Principal = Depends(require_permission("etmf_document:read")),
) -> ExtractedRegulatoryMetadata:
    """Extract protocol, site, investigator, and date metadata."""
    parser = DocumentIntelligenceParser()
    extractor = RegulatoryMetadataExtractor()

    parsed_doc = parser.parse(payload.content, filename=payload.filename)
    return extractor.extract(
        parsed_doc=parsed_doc,
        study_id_hint=payload.study_id,
        site_id_hint=payload.site_id,
    )


@router.post(
    "/verify-signatures",
    response_model=SignatureAnalysisResult,
    status_code=status.HTTP_200_OK,
    summary="Verify regulatory signature completeness",
)
async def verify_signatures_endpoint(
    payload: DocumentIntelligenceSignatureRequest,
    principal: Principal = Depends(require_permission("etmf_document:read")),
) -> SignatureAnalysisResult:
    """Analyze signature lines, manifestations, and completeness for an artifact."""
    parser = DocumentIntelligenceParser()
    analyzer = SignatureCompletenessAnalyzer()

    parsed_doc = parser.parse(payload.content, filename=payload.filename)
    return analyzer.analyze(
        parsed_doc=parsed_doc,
        artifact_code=payload.artifact_code,
    )


@router.post(
    "/stage-qc",
    response_model=dict,
    status_code=status.HTTP_201_CREATED,
    summary="Ingest and stage classified artifact into CRA Quality Control queue",
)
@transactional
async def stage_classified_artifact_endpoint(
    payload: StageDocumentQCRequest,
    principal: Principal = Depends(require_permission("etmf_document:create")),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
) -> dict:
    """Analyze, ingest, and stage document with TECHNICAL_QC status for CRA review."""
    use_case = StageClassifiedArtifactUseCase(repo)
    actor_id = principal.user_id or "system"
    actor_role = principal.roles[0] if principal.roles else "system"

    doc, report = await use_case.execute(
        content=payload.content,
        filename=payload.filename,
        mime_type=payload.mime_type,
        study_id=payload.study_id,
        actor_id=actor_id,
        actor_role=actor_role,
        reason_for_change=payload.reason_for_change,
        site_id=payload.site_id,
        artifact_hint=payload.artifact_hint,
        taxonomy_version=payload.taxonomy_version,
        assigned_cra=payload.assigned_cra,
    )

    return {
        "status": "STAGED",
        "document": to_document_response(doc).model_dump(),
        "intelligence_report": report.model_dump(),
    }


@router.get(
    "/qc-queue",
    response_model=list[CRAQCStagingItem],
    status_code=status.HTTP_200_OK,
    summary="List pending documents staged in CRA Quality Control queue",
)
async def get_qc_queue_endpoint(
    study_id: str | None = Query(None, description="Optional study ID filter"),
    principal: Principal = Depends(require_permission("etmf_document:read")),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
) -> list[CRAQCStagingItem]:
    """Retrieve staged QC items."""
    use_case = GetQCQueueUseCase(repo)
    return await use_case.execute(study_id=study_id)


@router.post(
    "/qc-review/{doc_id}",
    response_model=DocumentResponse,
    status_code=status.HTTP_200_OK,
    summary="Execute CRA Quality Control review decision (Accept, Override, Reject)",
)
@transactional
async def cra_qc_review_endpoint(
    doc_id: str,
    payload: CRAQCReviewRequest,
    principal: Principal = Depends(require_permission("etmf_document:update")),
    repo: ETMFRepositoryPort = Depends(get_etmf_repository),
) -> DocumentResponse:
    """Adjudicate staged document classification with 21 CFR Part 11 audit trail."""
    use_case = CRAQCReviewUseCase(repo)
    actor_id = principal.user_id or "cra.user"
    actor_role = principal.roles[0] if principal.roles else "cra_monitor"

    try:
        updated_doc = await use_case.execute(
            document_id=doc_id,
            decision=payload.decision,
            actor_id=actor_id,
            actor_role=actor_role,
            reason_for_change=payload.reason_for_change,
            override_artifact_code=payload.override_artifact_code,
            discrepancy_comment=payload.discrepancy_comment,
        )
    except DocumentNotFoundError as err:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=str(err),
        ) from err
    except ValueError as err:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
            detail=str(err),
        ) from err

    return to_document_response(updated_doc)
