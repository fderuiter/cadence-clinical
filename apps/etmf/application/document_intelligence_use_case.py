"""Application use cases for multimodal document intelligence, DIA classification, and CRA QC review."""

import hashlib
from datetime import UTC, datetime
from typing import Any

from apps.etmf.domain.exceptions import DocumentNotFoundError
from apps.etmf.domain.intelligence_models import (
    CRAQCStagingItem,
    DocumentIntelligenceReport,
    TaxonomyMatchCandidate,
)
from apps.etmf.domain.models import DocumentStatus
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
from apps.etmf.domain.tmf_reference_model import (
    get_active_catalog,
    resolve_artifact,
)


class AnalyzeDocumentIntelligenceUseCase:
    """End-to-end intelligence analysis pipeline combining parsing, classification, metadata, and signatures."""

    def __init__(self) -> None:
        self.parser = DocumentIntelligenceParser()
        self.classifier = DIAReferenceModelClassifier()
        self.metadata_extractor = RegulatoryMetadataExtractor()
        self.signature_analyzer = SignatureCompletenessAnalyzer()

    def execute(
        self,
        content: str | bytes,
        filename: str,
        mime_type: str = "text/plain",
        study_id_hint: str | None = None,
        site_id_hint: str | None = None,
        artifact_hint: str | None = None,
        free_text: str | None = None,
        taxonomy_version: str | None = None,
        document_id: str | None = None,
        existing_signer: str | None = None,
        existing_manifestation: dict[str, Any] | str | None = None,
        ai_suggestion: dict[str, Any] | None = None,
    ) -> DocumentIntelligenceReport:
        """Run complete document intelligence analysis."""
        # 1. Parse layout and text
        parsed_doc = self.parser.parse(content, filename=filename, mime_type=mime_type)

        # 2. Multi-signal DIA Classification
        primary, alternatives, conf_tier, qc_rec, eisf_mapping = (
            self.classifier.classify(
                parsed_doc=parsed_doc,
                filename=filename,
                artifact_hint=artifact_hint,
                free_text=free_text,
                taxonomy_version=taxonomy_version,
                ai_structured_suggestion=ai_suggestion,
            )
        )

        # 3. Regulatory Metadata Extraction
        metadata = self.metadata_extractor.extract(
            parsed_doc=parsed_doc,
            study_id_hint=study_id_hint,
            site_id_hint=site_id_hint,
        )

        # 4. Signature Completeness Verification
        sig_result = self.signature_analyzer.analyze(
            parsed_doc=parsed_doc,
            artifact_code=primary.artifact_code,
            existing_doc_signer=existing_signer,
            existing_doc_signature_manifestation=existing_manifestation,
        )

        # 5. Build AI Generation Manifest (21 CFR Part 11 Provenance)
        prompt_hash = hashlib.sha256(
            (filename + parsed_doc.raw_text[:200]).encode("utf-8")
        ).hexdigest()

        ai_manifest = {
            "model_version": "cadence-multimodal-dia-v1.0",
            "prompt_hash": prompt_hash,
            "confidence_score": primary.confidence,
            "confidence_tier": conf_tier.value,
            "signals_used": primary.matched_signals,
            "analyzed_at": datetime.now(UTC).isoformat(),
        }

        return DocumentIntelligenceReport(
            document_id=document_id,
            filename=filename,
            mime_type=mime_type,
            sha256_hash=parsed_doc.sha256_hash,
            modality=parsed_doc.modality,
            primary_classification=primary,
            alternative_candidates=alternatives,
            confidence_tier=conf_tier,
            qc_recommendation=qc_rec,
            extracted_metadata=metadata,
            signature_analysis=sig_result,
            ai_generation_manifest=ai_manifest,
            eisf_target_mapping=eisf_mapping,
        )


class StageClassifiedArtifactUseCase:
    """Persists an analyzed document into the repository and stages it in the CRA Quality Control queue."""

    def __init__(self, repo: ETMFRepositoryPort) -> None:
        self.repo = repo
        self.analyzer = AnalyzeDocumentIntelligenceUseCase()

    async def execute(
        self,
        content: str | bytes,
        filename: str,
        mime_type: str,
        study_id: str,
        actor_id: str,
        actor_role: str,
        reason_for_change: str,
        site_id: str | None = None,
        artifact_hint: str | None = None,
        taxonomy_version: str | None = None,
        assigned_cra: str | None = None,
    ) -> tuple[Any, DocumentIntelligenceReport]:
        """Analyze, persist, and stage document with full Part 11 audit trail."""
        tax_version = taxonomy_version or get_active_catalog().version

        # Run intelligence pipeline
        report = self.analyzer.execute(
            content=content,
            filename=filename,
            mime_type=mime_type,
            study_id_hint=study_id,
            site_id_hint=site_id,
            artifact_hint=artifact_hint,
            taxonomy_version=tax_version,
        )

        art_code = report.primary_classification.artifact_code
        art_name = report.primary_classification.artifact_name
        zone_num = report.primary_classification.zone_code
        sec_code = report.primary_classification.section_code

        # Allocate version index
        max_v = await self.repo.get_max_version_index(study_id, site_id, art_code)
        version_index = max_v + 1

        # Initial status: TECHNICAL_QC for CRA review
        init_status = DocumentStatus.TECHNICAL_QC.value

        meta_json = {
            "document_intelligence": report.model_dump(mode="json"),
            "staged_by_ai": True,
            "assigned_cra": assigned_cra,
        }

        raw_text = (
            content.decode("utf-8", errors="replace")
            if isinstance(content, bytes)
            else str(content)
        )

        created_doc = await self.repo.create_document(
            study_id=study_id,
            artifact_type=art_name,
            filename=filename,
            content=raw_text,
            mime_type=mime_type,
            created_by=actor_id,
            version_index=version_index,
            status=init_status,
            taxonomy_version=tax_version,
            artifact_code=art_code,
            zone=zone_num,
            section=sec_code,
            site_id=site_id,
            reason_for_change=reason_for_change,
            metadata_json=meta_json,
            document_owner_id=report.extracted_metadata.investigator_name or actor_id,
        )

        # Record Part 11 Audit Log
        details = (
            f"Multimodal AI staged document '{filename}' as '{art_name}' ({art_code}) "
            f"with confidence {report.primary_classification.confidence:.2f}."
        )
        await self.repo.create_audit_log(
            user_id=actor_id,
            user_role=actor_role,
            action="AI_STAGE_QC",
            document_id=getattr(created_doc, "id", None),
            details=details,
            reason_for_change=reason_for_change,
        )

        report.document_id = getattr(created_doc, "id", None)
        return created_doc, report


class CRAQCReviewUseCase:
    """Allows Clinical Research Associates (CRAs) to review, accept, override, or reject staged documents."""

    def __init__(self, repo: ETMFRepositoryPort) -> None:
        self.repo = repo

    async def execute(
        self,
        document_id: str,
        decision: str,  # 'ACCEPT', 'OVERRIDE', 'REJECT'
        actor_id: str,
        actor_role: str,
        reason_for_change: str,
        override_artifact_code: str | None = None,
        discrepancy_comment: str | None = None,
    ) -> Any:
        """Execute CRA QC decision."""
        doc = await self.repo.get_document_by_id(document_id)
        if not doc:
            raise DocumentNotFoundError(f"Document '{document_id}' not found.")

        from_status = getattr(doc, "status", None) or DocumentStatus.TECHNICAL_QC.value

        if decision == "ACCEPT":
            to_status = DocumentStatus.APPROVED.value
            doc.status = to_status
            doc.approval_status = "APPROVED"
            details = f"CRA accepted AI classification '{doc.artifact_type}' ({doc.artifact_code})."

        elif decision == "OVERRIDE":
            if not override_artifact_code:
                raise ValueError(
                    "Must provide 'override_artifact_code' when overriding classification."
                )
            resolved = resolve_artifact(
                doc.taxonomy_version, code=override_artifact_code
            )
            doc.artifact_code = resolved["artifact"].code
            doc.artifact_type = resolved["artifact"].name
            doc.zone = resolved["zone"].code
            doc.section = resolved["section"].code
            to_status = DocumentStatus.APPROVED.value
            doc.status = to_status
            doc.approval_status = "APPROVED"
            details = f"CRA overridden classification to '{doc.artifact_type}' ({doc.artifact_code})."

        elif decision == "REJECT":
            to_status = DocumentStatus.REJECTED.value
            doc.status = to_status
            doc.approval_status = "REJECTED"
            details = f"CRA rejected document. Reason: {discrepancy_comment or reason_for_change}"
        else:
            raise ValueError(
                f"Invalid CRA QC decision '{decision}'. Must be ACCEPT, OVERRIDE, or REJECT."
            )

        doc.reason_for_change = reason_for_change.strip()

        # Record QC transition and audit log
        await self.repo.create_qc_transition(
            document_id=doc.id,
            from_status=from_status,
            to_status=to_status,
            actor_id=actor_id,
            actor_role=actor_role,
            reason_for_change=reason_for_change.strip(),
        )

        await self.repo.create_audit_log(
            user_id=actor_id,
            user_role=actor_role,
            action="CRA_QC_DECISION",
            document_id=doc.id,
            details=details,
            reason_for_change=reason_for_change,
        )

        await self.repo.save_document(doc)
        return doc


class GetQCQueueUseCase:
    """Retrieves all documents staged in QC queues."""

    def __init__(self, repo: ETMFRepositoryPort) -> None:
        self.repo = repo

    async def execute(self, study_id: str | None = None) -> list[CRAQCStagingItem]:
        """Fetch pending QC items."""
        docs: list[Any] = []
        if study_id:
            docs = list(await self.repo.get_documents_by_study(study_id))
        else:
            # If no study_id, fetch via filter
            docs = list(
                await self.repo.get_documents_filtered(None, None, None, None, None)
            )

        qc_statuses = {
            DocumentStatus.TECHNICAL_QC.value,
            DocumentStatus.CLINICAL_QC.value,
            DocumentStatus.DRAFT.value,
        }

        results: list[CRAQCStagingItem] = []
        for d in docs:
            if d.status in qc_statuses:
                meta = getattr(d, "metadata_json", {}) or {}
                raw_report = meta.get("document_intelligence")
                if raw_report:
                    try:
                        report = DocumentIntelligenceReport.model_validate(raw_report)
                    except Exception:
                        report = None
                else:
                    report = None

                if not report:
                    # Synthesize basic report for documents ingested without full pipeline
                    primary_cand = TaxonomyMatchCandidate(
                        zone_code=d.zone,
                        zone_name=f"Zone {d.zone}",
                        section_code=d.section,
                        section_name=f"Section {d.section}",
                        artifact_code=d.artifact_code,
                        artifact_name=d.artifact_type,
                        confidence=0.85,
                        matched_signals=["database_record"],
                    )
                    report = DocumentIntelligenceReport(
                        document_id=d.id,
                        filename=d.filename,
                        mime_type=d.mime_type,
                        sha256_hash=getattr(d, "content_checksum", "unknown")
                        or "unknown",
                        primary_classification=primary_cand,
                    )

                staged_at_val = getattr(d, "created_at", None) or datetime.now(UTC)
                results.append(
                    CRAQCStagingItem(
                        document_id=d.id,
                        study_id=d.study_id,
                        site_id=d.site_id,
                        filename=d.filename,
                        status=d.status,
                        intelligence_report=report,
                        staged_at=staged_at_val,
                        staged_by=d.created_by,
                        qc_assigned_to=meta.get("assigned_cra"),
                        discrepancy_notes=[],
                    )
                )

        return results
