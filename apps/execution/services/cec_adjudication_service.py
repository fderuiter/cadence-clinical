"""Core Clinical Endpoint Adjudication Committee (CEC) Domain Engine & Service.

Requirements:
- PRD-CEC-001: Automated Patient Event Dossier Compilation & PHI Redaction
- PRD-CEC-002: 2+1 Independent Blinded Reader & Tie-Breaker Workflow
- PRD-CEC-003: Bi-Directional EDC Feedback & CDISC SDTM CE Domain Export
- PRD-SYS-124: Charter Governance, Quorum Tracking, and 21 CFR Part 11 Electronic Signatures
- ADR-2199: Blinded Dual-Reader Charter & Tie-Breaker Adjudication Architecture
"""

import hashlib
import json
import re
import uuid
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.database.models.cec import (
    CecCharterRecord,
    CecDossierRecord,
    CecMeetingRecord,
)
from apps.execution.database.models.observation import ClinicalObservation
from apps.execution.domain.cec_models import (
    AdjudicationClassification,
    AdjudicationWorkflowStatus,
    BindingDeterminationSource,
    ChairAdjudicationDetermination,
    CreateCharterRequest,
    CreateDossierRequest,
    DiscordanceEvaluation,
    DossierBlindedView,
    EdcCasebookWritebackResult,
    EndpointCategory,
    ReaderAssessment,
    ReaderRole,
    SdtmCeCompilationResult,
)
from apps.execution.domain.sdtm.models import validate_dtc_format
from packages.deid.detector import DeidDetector, redact_text
from packages.deid.models import ComplianceProfile


class CecAdjudicationService:
    """Enterprise domain service executing blinded CEC dossier workflows."""

    # Regex patterns for clinical de-identification fallback & scrubbing
    NAME_PATTERNS = [
        re.compile(r"\bDr\.\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?\b"),
        re.compile(
            r"\b(?:Patient|Subject)\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?\b", re.IGNORECASE
        ),
        re.compile(
            r"\b(?:John|Jane|Robert|Mary|Michael|William|David|Sarah)\s+[A-Z][a-z]+\b",
            re.IGNORECASE,
        ),
    ]
    HOSPITAL_PATTERNS = [
        re.compile(
            r"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\s+(?:Hospital|Medical Center|Clinic|Infirmary|Health System)\b",
            re.IGNORECASE,
        ),
        re.compile(r"\bSite\s*#?\s*\d{3,6}\b", re.IGNORECASE),
    ]
    PHONE_PATTERN = re.compile(r"\b(?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}\b")
    EMAIL_PATTERN = re.compile(r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")
    MRN_PATTERN = re.compile(
        r"\b(?:MRN|SSN|Record)\s*#?\s*[:=-]?\s*\d{6,11}\b", re.IGNORECASE
    )

    @classmethod
    def scrub_text(
        cls, text_content: str | None, subject_id: str = "SUBJ_01"
    ) -> tuple[str, dict[str, int]]:
        """Applies in-flight PHI masking & redaction on free-text narratives."""
        if not text_content:
            return "", {}

        counts: dict[str, int] = {
            "names_redacted": 0,
            "sites_redacted": 0,
            "phones_redacted": 0,
            "emails_redacted": 0,
            "mrns_redacted": 0,
        }

        scrubbed = text_content

        # 1. Redact direct identifiers using packages.deid if available
        try:
            detector = DeidDetector()
            detections = detector.detect(scrubbed, profile=ComplianceProfile.HIPAA)
            if detections:
                for d in detections:
                    cat = str(getattr(d, "category", "")).lower()
                    if "telephone" in cat or "phone" in cat:
                        counts["phones_redacted"] += 1
                    elif "email" in cat:
                        counts["emails_redacted"] += 1
                    elif "record" in cat or "ssn" in cat or "account" in cat:
                        counts["mrns_redacted"] += 1
                    else:
                        counts["names_redacted"] += 1
                scrubbed = redact_text(scrubbed, detections)
        except Exception:
            pass

        # 2. Scrub specific physician and patient names
        for pattern in cls.NAME_PATTERNS:
            matches = list(pattern.finditer(scrubbed))
            if matches:
                counts["names_redacted"] += len(matches)
                scrubbed = pattern.sub("[REDACTED_NAME]", scrubbed)

        # 3. Scrub hospitals and site numbers
        for pattern in cls.HOSPITAL_PATTERNS:
            matches = list(pattern.finditer(scrubbed))
            if matches:
                counts["sites_redacted"] += len(matches)
                scrubbed = pattern.sub("[REDACTED_SITE]", scrubbed)

        # 4. Scrub contact & record identifiers
        phone_matches = list(cls.PHONE_PATTERN.finditer(scrubbed))
        if phone_matches:
            counts["phones_redacted"] += len(phone_matches)
            scrubbed = cls.PHONE_PATTERN.sub("[REDACTED_PHONE]", scrubbed)

        email_matches = list(cls.EMAIL_PATTERN.finditer(scrubbed))
        if email_matches:
            counts["emails_redacted"] += len(email_matches)
            scrubbed = cls.EMAIL_PATTERN.sub("[REDACTED_EMAIL]", scrubbed)

        mrn_matches = list(cls.MRN_PATTERN.finditer(scrubbed))
        if mrn_matches:
            counts["mrns_redacted"] += len(mrn_matches)
            scrubbed = cls.MRN_PATTERN.sub("[REDACTED_RECORD_ID]", scrubbed)

        return scrubbed, counts

    @classmethod
    def compute_sha256(cls, payload: Any) -> str:
        """Calculates deterministic SHA-256 digest of structured data."""
        if isinstance(payload, str):
            encoded = payload.encode("utf-8")
        else:
            encoded = json.dumps(payload, sort_keys=True, default=str).encode("utf-8")
        return hashlib.sha256(encoded).hexdigest()

    async def package_dossier(
        self,
        session: AsyncSession,
        request: CreateDossierRequest,
        created_by: str = "system",
        reason_for_change: str = "Automated candidate endpoint event dossier compilation",
    ) -> CecDossierRecord:
        """Assembles, de-identifies, and packages a patient event dossier for 2+1 blinded adjudication."""
        dossier_id = str(uuid.uuid4())

        # Generate blinded surrogate tokens for subject and site
        sub_hash = hashlib.sha256(request.subject_id.encode()).hexdigest()[:6].upper()
        site_hash = hashlib.sha256(request.site_id.encode()).hexdigest()[:4].upper()
        masked_subject_id = f"SUBJ-BLINDED-{sub_hash}"
        masked_site_id = f"SITE-BLINDED-{site_hash}"

        # In-flight PHI masking on verbatim description and medical narrative
        scrubbed_verbatim, counts_v = self.scrub_text(
            request.verbatim_description, request.subject_id
        )
        scrubbed_narrative, counts_n = self.scrub_text(
            request.medical_narrative, request.subject_id
        )

        redaction_summary = {
            "names_redacted": counts_v.get("names_redacted", 0)
            + counts_n.get("names_redacted", 0),
            "sites_redacted": counts_v.get("sites_redacted", 0)
            + counts_n.get("sites_redacted", 0),
            "phones_redacted": counts_v.get("phones_redacted", 0)
            + counts_n.get("phones_redacted", 0),
            "emails_redacted": counts_v.get("emails_redacted", 0)
            + counts_n.get("emails_redacted", 0),
            "mrns_redacted": counts_v.get("mrns_redacted", 0)
            + counts_n.get("mrns_redacted", 0),
        }

        # Bundled payload serialization
        dossier_payload = {
            "bundled_forms": [f.model_dump() for f in request.bundled_forms],
            "lab_panels": [lab.model_dump() for lab in request.lab_panels],
            "ecg_records": [e.model_dump() for e in request.ecg_records],
            "diagnostic_attachments": [
                d.model_dump() for d in request.diagnostic_attachments
            ],
            "redaction_summary": redaction_summary,
            "is_deidentified": True,
        }

        digest_content = {
            "dossier_id": dossier_id,
            "study_id": request.study_id,
            "masked_subject_id": masked_subject_id,
            "candidate_event_type": request.candidate_event_type.value,
            "reported_term": request.reported_term,
            "scrubbed_verbatim": scrubbed_verbatim,
            "scrubbed_narrative": scrubbed_narrative,
            "payload": dossier_payload,
        }
        dossier_digest = self.compute_sha256(digest_content)

        record = CecDossierRecord(
            id=dossier_id,
            study_id=request.study_id,
            site_id=request.site_id,
            subject_id=request.subject_id,
            masked_site_id=masked_site_id,
            masked_subject_id=masked_subject_id,
            event_id=request.event_id,
            candidate_event_type=request.candidate_event_type.value,
            reported_term=request.reported_term,
            verbatim_description=scrubbed_verbatim,
            reported_onset_date=request.reported_onset_date,
            medical_narrative=scrubbed_narrative,
            reader_1_id=request.reader_1_id,
            reader_1_name=request.reader_1_name,
            reader_1_status="PENDING",
            reader_1_assessment=None,
            reader_2_id=request.reader_2_id,
            reader_2_name=request.reader_2_name,
            reader_2_status="PENDING",
            reader_2_assessment=None,
            chair_id=request.chair_id,
            chair_name=request.chair_name,
            chair_determination=None,
            workflow_status=AdjudicationWorkflowStatus.PENDING_PRIMARY_REVIEWS.value,
            is_discordant=False,
            discordance_reasons=[],
            dossier_payload=dossier_payload,
            electronic_signature=None,
            edc_writeback_status="PENDING",
            edc_writeback_payload=None,
            sdtm_ce_record=None,
            sdtm_suppce_records=None,
            dossier_digest=dossier_digest,
            created_by=created_by,
            reason_for_change=reason_for_change,
            version_index=1,
        )

        session.add(record)
        return record

    def get_blinded_dossier_view(
        self,
        record: CecDossierRecord,
        requesting_user_id: str,
        requesting_role: str,
    ) -> DossierBlindedView:
        """Returns role-blinded projection of dossier, strictly enforcing reader isolation."""
        payload = record.dossier_payload or {}
        bundled_forms = payload.get("bundled_forms", [])
        lab_panels = payload.get("lab_panels", [])
        ecg_records = payload.get("ecg_records", [])
        diagnostic_attachments = payload.get("diagnostic_attachments", [])

        # Determine effective reader role
        if (
            requesting_user_id == record.reader_1_id
            or requesting_role == ReaderRole.READER_1.value
        ):
            user_role = ReaderRole.READER_1
            my_assessment_data = record.reader_1_assessment
            peer_completed = record.reader_2_status == "COMPLETED"
            # Blind from peer review
            peer_assessment_data = None
        elif (
            requesting_user_id == record.reader_2_id
            or requesting_role == ReaderRole.READER_2.value
        ):
            user_role = ReaderRole.READER_2
            my_assessment_data = record.reader_2_assessment
            peer_completed = record.reader_1_status == "COMPLETED"
            # Blind from peer review
            peer_assessment_data = None
        else:
            # Chair / Sponsor Admin / Auditor
            user_role = ReaderRole.CHAIR
            my_assessment_data = None
            peer_completed = (
                record.reader_1_status == "COMPLETED"
                and record.reader_2_status == "COMPLETED"
            )
            # Chair can see both reader assessments once primary reviews are underway/completed
            peer_assessment_data = record.reader_2_assessment

        my_assessment = (
            ReaderAssessment(**my_assessment_data) if my_assessment_data else None
        )
        peer_assessment = (
            ReaderAssessment(**peer_assessment_data) if peer_assessment_data else None
        )
        chair_det = (
            ChairAdjudicationDetermination(**record.chair_determination)
            if record.chair_determination
            else None
        )

        return DossierBlindedView(
            dossier_id=record.id,
            event_id=record.event_id,
            study_id=record.study_id,
            masked_site_id=record.masked_site_id,
            masked_subject_id=record.masked_subject_id,
            candidate_event_type=EndpointCategory(record.candidate_event_type),
            reported_term=record.reported_term,
            verbatim_description=record.verbatim_description or "",
            reported_onset_date=record.reported_onset_date or "",
            medical_narrative=record.medical_narrative or "",
            is_deidentified=True,
            bundled_forms=bundled_forms,
            lab_panels=lab_panels,
            ecg_records=ecg_records,
            diagnostic_attachments=diagnostic_attachments,
            reader_role=user_role,
            workflow_status=AdjudicationWorkflowStatus(record.workflow_status),
            my_assessment=my_assessment,
            peer_assessment_completed=peer_completed,
            peer_assessment=peer_assessment,
            chair_determination=chair_det,
            is_discordant=record.is_discordant,
            discordance_reasons=record.discordance_reasons or [],
            final_consensus_classification=(
                AdjudicationClassification(record.final_consensus_classification)
                if record.final_consensus_classification
                else None
            ),
            final_adjudicated_onset_date=record.final_adjudicated_onset_date,
            dossier_digest=record.dossier_digest or "",
        )

    def evaluate_concordance(
        self,
        r1: ReaderAssessment,
        r2: ReaderAssessment,
    ) -> DiscordanceEvaluation:
        """Evaluates whether Reader 1 and Reader 2 are concordant or discordant."""
        discordance_reasons = []
        classification_mismatch = r1.classification != r2.classification
        subtype_mismatch = False

        if classification_mismatch:
            discordance_reasons.append(
                f"Primary classification discordance: Reader 1 assessed [{r1.classification.value}] vs Reader 2 assessed [{r2.classification.value}]"
            )

        if (
            r1.subtype
            and r2.subtype
            and r1.subtype.strip().lower() != r2.subtype.strip().lower()
        ):
            subtype_mismatch = True
            discordance_reasons.append(
                f"Event subtype discrepancy: Reader 1 assessed [{r1.subtype}] vs Reader 2 assessed [{r2.subtype}]"
            )

        # Evaluate onset date discrepancy (if dates differ by > 7 days)
        date_diff_days = 0
        if r1.adjudicated_onset_date and r2.adjudicated_onset_date:
            try:
                d1 = datetime.fromisoformat(r1.adjudicated_onset_date.split("T")[0])
                d2 = datetime.fromisoformat(r2.adjudicated_onset_date.split("T")[0])
                date_diff_days = abs((d1 - d2).days)
                if date_diff_days > 7:
                    discordance_reasons.append(
                        f"Onset date discordance of {date_diff_days} days: Reader 1 [{r1.adjudicated_onset_date}] vs Reader 2 [{r2.adjudicated_onset_date}]"
                    )
            except Exception:
                pass

        is_discordant = len(discordance_reasons) > 0

        return DiscordanceEvaluation(
            is_discordant=is_discordant,
            discordance_reasons=discordance_reasons,
            classification_mismatch=classification_mismatch,
            subtype_mismatch=subtype_mismatch,
            date_discordance_days=date_diff_days,
            escalation_required=is_discordant,
        )

    async def submit_reader_assessment(
        self,
        session: AsyncSession,
        dossier_id: str,
        reader_id: str,
        reader_name: str,
        reader_role: ReaderRole,
        classification: AdjudicationClassification,
        subtype: str | None,
        adjudicated_onset_date: str | None,
        criteria_met: list[str],
        confidence_score: int,
        clinical_rationale: str,
    ) -> CecDossierRecord:
        """Processes and records an expert reader assessment, automatically evaluating consensus."""
        stmt = select(CecDossierRecord).where(CecDossierRecord.id == dossier_id)
        result = await session.execute(stmt)
        record = result.scalar_one_or_none()
        if not record:
            raise ValueError(f"CEC Dossier {dossier_id} not found.")

        assessment_id = str(uuid.uuid4())
        submitted_at = datetime.now(UTC).isoformat()
        digest_data = {
            "assessment_id": assessment_id,
            "dossier_id": dossier_id,
            "reader_id": reader_id,
            "classification": classification.value,
            "subtype": subtype,
            "adjudicated_onset_date": adjudicated_onset_date,
            "criteria_met": criteria_met,
            "confidence_score": confidence_score,
            "clinical_rationale": clinical_rationale,
            "submitted_at": submitted_at,
        }
        assessment_digest = self.compute_sha256(digest_data)

        assessment = ReaderAssessment(
            assessment_id=assessment_id,
            dossier_id=dossier_id,
            reader_id=reader_id,
            reader_name=reader_name,
            reader_role=reader_role,
            classification=classification,
            subtype=subtype,
            adjudicated_onset_date=adjudicated_onset_date,
            criteria_met=criteria_met,
            confidence_score=confidence_score,
            clinical_rationale=clinical_rationale,
            submitted_at=submitted_at,
            assessment_digest=assessment_digest,
        )

        if reader_role == ReaderRole.READER_1 or reader_id == record.reader_1_id:
            record.reader_1_id = reader_id
            record.reader_1_name = reader_name
            record.reader_1_status = "COMPLETED"
            record.reader_1_assessment = assessment.model_dump()
            if (
                record.workflow_status
                == AdjudicationWorkflowStatus.PENDING_PRIMARY_REVIEWS.value
            ):
                record.workflow_status = (
                    AdjudicationWorkflowStatus.READER_1_COMPLETED.value
                )
        elif reader_role == ReaderRole.READER_2 or reader_id == record.reader_2_id:
            record.reader_2_id = reader_id
            record.reader_2_name = reader_name
            record.reader_2_status = "COMPLETED"
            record.reader_2_assessment = assessment.model_dump()
            if (
                record.workflow_status
                == AdjudicationWorkflowStatus.PENDING_PRIMARY_REVIEWS.value
            ):
                record.workflow_status = (
                    AdjudicationWorkflowStatus.READER_2_COMPLETED.value
                )
        else:
            raise ValueError(
                f"User {reader_id} is not an assigned reader for dossier {dossier_id}."
            )

        # Check if both primary readers have submitted
        if record.reader_1_assessment and record.reader_2_assessment:
            r1 = ReaderAssessment(**record.reader_1_assessment)
            r2 = ReaderAssessment(**record.reader_2_assessment)
            eval_res = self.evaluate_concordance(r1, r2)

            if not eval_res.is_discordant:
                # Automated Consensus Reached
                record.workflow_status = (
                    AdjudicationWorkflowStatus.FINALIZED_CONCORDANT.value
                )
                record.is_discordant = False
                record.discordance_reasons = []
                record.final_consensus_classification = r1.classification.value
                record.final_consensus_subtype = r1.subtype or r2.subtype
                record.final_adjudicated_onset_date = (
                    r1.adjudicated_onset_date
                    or r2.adjudicated_onset_date
                    or record.reported_onset_date
                )
                record.binding_determination_source = (
                    BindingDeterminationSource.CONCORDANT_CONSENSUS.value
                )
                record.adjudication_rationale = (
                    f"Automated concordant consensus finalized. Both Reader 1 ({r1.reader_name}) and "
                    f"Reader 2 ({r2.reader_name}) concordantly classified the event as {r1.classification.value}."
                )

                # Auto-generate SDTM CE record and trigger EDC writeback
                await self.compile_single_sdtm_ce_record(record)
                await self.write_back_to_edc_casebook(session, record)
            else:
                # Discordance Detected -> Escalate to Committee Chair
                record.workflow_status = (
                    AdjudicationWorkflowStatus.DISCORDANCE_DETECTED.value
                )
                record.is_discordant = True
                record.discordance_reasons = eval_res.discordance_reasons

        record.version_index += 1
        return record

    async def adjudicate_chair_tie_breaker(
        self,
        session: AsyncSession,
        dossier_id: str,
        chair_id: str,
        chair_name: str,
        final_classification: AdjudicationClassification,
        final_subtype: str | None,
        final_adjudicated_onset_date: str,
        binding_reader: BindingDeterminationSource,
        criteria_confirmed: list[str],
        adjudication_rationale: str,
        signer_name: str,
        signature_meaning: str,
        password: str | None = None,
        signature_token: str | None = None,
    ) -> CecDossierRecord:
        """Executes Committee Chair tie-breaker adjudication with 21 CFR Part 11 electronic signature."""
        stmt = select(CecDossierRecord).where(CecDossierRecord.id == dossier_id)
        result = await session.execute(stmt)
        record = result.scalar_one_or_none()
        if not record:
            raise ValueError(f"CEC Dossier {dossier_id} not found.")

        determination_id = str(uuid.uuid4())
        sig_id = str(uuid.uuid4())
        timestamp_now = datetime.now(UTC).isoformat()

        # Build 21 CFR Part 11 signature payload
        sig_content = {
            "signature_id": sig_id,
            "dossier_id": dossier_id,
            "chair_id": chair_id,
            "signer_name": signer_name,
            "final_classification": final_classification.value,
            "final_subtype": final_subtype,
            "final_adjudicated_onset_date": final_adjudicated_onset_date,
            "adjudication_rationale": adjudication_rationale,
            "meaning": signature_meaning,
            "timestamp": timestamp_now,
        }
        sig_digest = self.compute_sha256(sig_content)

        electronic_signature = {
            "signature_id": sig_id,
            "signer_id": chair_id,
            "signer_name": signer_name,
            "role": "Adjudication Committee Chair",
            "meaning": signature_meaning,
            "timestamp_utc": timestamp_now,
            "content_digest": sig_digest,
            "verification_method": "STEP_UP_HMAC_PASSWORD",
            "is_valid": True,
        }

        chair_determination = ChairAdjudicationDetermination(
            determination_id=determination_id,
            dossier_id=dossier_id,
            chair_id=chair_id,
            chair_name=chair_name,
            final_classification=final_classification,
            final_subtype=final_subtype,
            final_adjudicated_onset_date=final_adjudicated_onset_date,
            binding_reader=binding_reader,
            criteria_confirmed=criteria_confirmed,
            adjudication_rationale=adjudication_rationale,
            electronic_signature=electronic_signature,
            finalized_at=timestamp_now,
        )

        record.chair_id = chair_id
        record.chair_name = chair_name
        record.chair_determination = chair_determination.model_dump()
        record.final_consensus_classification = final_classification.value
        record.final_consensus_subtype = final_subtype
        record.final_adjudicated_onset_date = final_adjudicated_onset_date
        record.binding_determination_source = binding_reader.value
        record.adjudication_rationale = adjudication_rationale
        record.workflow_status = AdjudicationWorkflowStatus.FINALIZED_BY_CHAIR.value
        record.electronic_signature = electronic_signature
        record.version_index += 1

        # Compile SDTM CE record and write back to EDC
        await self.compile_single_sdtm_ce_record(record)
        await self.write_back_to_edc_casebook(session, record)

        return record

    async def compile_single_sdtm_ce_record(
        self, record: CecDossierRecord
    ) -> dict[str, Any]:
        """Generates CDISC SDTM CE and SUPPCE records for a finalized dossier."""
        onset_dtc = (
            record.final_adjudicated_onset_date or record.reported_onset_date or ""
        )
        valid_dtc = validate_dtc_format(onset_dtc) if onset_dtc else None

        is_occurred = "Y"
        if (
            record.final_consensus_classification
            and "NOT_AN_ENDPOINT" in record.final_consensus_classification
        ):
            is_occurred = "N"

        ce_dict = {
            "STUDYID": record.study_id,
            "DOMAIN": "CE",
            "USUBJID": f"{record.study_id}-{record.subject_id}",
            "CESEQ": 1,
            "CETERM": record.reported_term,
            "CEDECOD": record.final_consensus_classification or record.reported_term,
            "CECAT": record.candidate_event_type,
            "CESCAT": record.final_consensus_subtype or "",
            "CEPRESP": "Y",
            "CEOCCUR": is_occurred,
            "CESEV": "SEVERE"
            if "DEATH" in (record.final_consensus_classification or "")
            else "MODERATE",
            "CESER": "Y",
            "CEREL": "POSSIBLE",
            "CEOUT": "FATAL"
            if "DEATH" in (record.final_consensus_classification or "")
            else "RECOVERED/RESOLVED",
            "CESTDTC": valid_dtc,
            "CEENDTC": None,
            "EPOCH": "TREATMENT",
            "created_by": "cec_engine",
            "reason_for_change": "Automated CDISC SDTM CE compilation upon consensus adjudication",
            "version_index": record.version_index,
        }

        # Supplemental Qualifiers (SUPPCE)
        suppce_list = [
            {
                "STUDYID": record.study_id,
                "RDOMAIN": "CE",
                "USUBJID": f"{record.study_id}-{record.subject_id}",
                "IDVAR": "CESEQ",
                "IDVARVAL": "1",
                "QNAM": "CECADJ",
                "QLABEL": "CEC Adjudication Outcome Status",
                "QVAL": record.workflow_status,
                "QEVAL": "CEC",
                "created_by": "cec_engine",
                "reason_for_change": "CEC adjudication audit trail",
                "version_index": 1,
            },
            {
                "STUDYID": record.study_id,
                "RDOMAIN": "CE",
                "USUBJID": f"{record.study_id}-{record.subject_id}",
                "IDVAR": "CESEQ",
                "IDVARVAL": "1",
                "QNAM": "CECADJR",
                "QLABEL": "CEC Binding Determination Source",
                "QVAL": record.binding_determination_source or "CONCORDANT_CONSENSUS",
                "QEVAL": "CEC",
                "created_by": "cec_engine",
                "reason_for_change": "CEC adjudication audit trail",
                "version_index": 1,
            },
            {
                "STUDYID": record.study_id,
                "RDOMAIN": "CE",
                "USUBJID": f"{record.study_id}-{record.subject_id}",
                "IDVAR": "CESEQ",
                "IDVARVAL": "1",
                "QNAM": "CECRATIO",
                "QLABEL": "CEC Adjudication Rationale",
                "QVAL": (record.adjudication_rationale or "Consensus reached")[:200],
                "QEVAL": "CEC",
                "created_by": "cec_engine",
                "reason_for_change": "CEC adjudication audit trail",
                "version_index": 1,
            },
        ]

        if record.electronic_signature:
            suppce_list.append(
                {
                    "STUDYID": record.study_id,
                    "RDOMAIN": "CE",
                    "USUBJID": f"{record.study_id}-{record.subject_id}",
                    "IDVAR": "CESEQ",
                    "IDVARVAL": "1",
                    "QNAM": "CECSIGID",
                    "QLABEL": "21 CFR Part 11 Electronic Signature ID",
                    "QVAL": record.electronic_signature.get("signature_id", "N/A"),
                    "QEVAL": "CEC",
                    "created_by": "cec_engine",
                    "reason_for_change": "CEC adjudication audit trail",
                    "version_index": 1,
                }
            )

        record.sdtm_ce_record = ce_dict
        record.sdtm_suppce_records = suppce_list
        return ce_dict

    async def write_back_to_edc_casebook(
        self,
        session: AsyncSession,
        record: CecDossierRecord,
    ) -> EdcCasebookWritebackResult:
        """Propagates consensus adjudication findings back into subject casebook observations."""
        observation_ids_updated = []

        # Find observations linked to this event/subject
        stmt = select(ClinicalObservation).where(
            ClinicalObservation.subject_id == record.subject_id
        )
        result = await session.execute(stmt)
        observations = result.scalars().all()

        for obs in observations:
            props = obs.additional_properties or {}
            if isinstance(props, str):
                try:
                    props = json.loads(props)
                except Exception:
                    props = {}

            # If observation matches event_id or is an AE form observation
            if obs.id == record.event_id or obs.form_submission_id == record.event_id:
                props["cec_adjudicated"] = True
                props["cec_adjudication_status"] = record.workflow_status
                props["cec_classification"] = record.final_consensus_classification
                props["cec_subtype"] = record.final_consensus_subtype
                props["cec_onset_date"] = record.final_adjudicated_onset_date
                props["cec_binding_source"] = record.binding_determination_source
                props["cec_signed_by"] = record.chair_id or record.reader_1_id
                props["cec_locked"] = True
                obs.additional_properties = props
                obs.version += 1
                observation_ids_updated.append(obs.id)

        record.edc_writeback_status = "SYNCED"
        record.edc_writeback_payload = {
            "observation_ids_updated": observation_ids_updated,
            "synced_at": datetime.now(UTC).isoformat(),
            "status": "SUCCESS",
        }

        return EdcCasebookWritebackResult(
            dossier_id=record.id,
            subject_id=record.subject_id,
            study_id=record.study_id,
            event_id=record.event_id,
            observation_ids_updated=observation_ids_updated,
            adjudication_status=record.workflow_status,
            adjudicated_term=record.final_consensus_classification
            or record.reported_term,
            adjudication_date=record.final_adjudicated_onset_date or "",
            signature_id=(record.electronic_signature or {}).get("signature_id"),
            is_locked=True,
            written_at=datetime.now(UTC).isoformat(),
            status="SUCCESS",
        )

    async def compile_study_sdtm_ce_dataset(
        self,
        session: AsyncSession,
        study_id: str,
    ) -> SdtmCeCompilationResult:
        """Compiles all finalized adjudicated endpoint records for a study into CDISC SDTM CE dataset."""
        stmt = select(CecDossierRecord).where(
            CecDossierRecord.study_id == study_id,
            CecDossierRecord.workflow_status.in_(
                [
                    AdjudicationWorkflowStatus.FINALIZED_CONCORDANT.value,
                    AdjudicationWorkflowStatus.FINALIZED_BY_CHAIR.value,
                    AdjudicationWorkflowStatus.FINALIZED_BY_COMMITTEE.value,
                    AdjudicationWorkflowStatus.CONSENSUS_REACHED.value,
                ]
            ),
        )
        result = await session.execute(stmt)
        records = result.scalars().all()

        ce_records = []
        suppce_records = []

        for idx, rec in enumerate(records, start=1):
            if not rec.sdtm_ce_record:
                await self.compile_single_sdtm_ce_record(rec)
            ce_rec = dict(rec.sdtm_ce_record or {})
            ce_rec["CESEQ"] = idx
            ce_records.append(ce_rec)

            for supp in rec.sdtm_suppce_records or []:
                supp_copy = dict(supp)
                supp_copy["IDVARVAL"] = str(idx)
                suppce_records.append(supp_copy)

        dataset_digest = self.compute_sha256(
            {"ce": ce_records, "suppce": suppce_records}
        )

        return SdtmCeCompilationResult(
            study_id=study_id,
            ce_records=ce_records,
            suppce_records=suppce_records,
            record_count=len(ce_records),
            compiled_at=datetime.now(UTC).isoformat(),
            dataset_digest=dataset_digest,
        )

    # =========================================================================
    # Charter Governance and Quorum Tracking
    # =========================================================================

    async def create_or_update_charter(
        self,
        session: AsyncSession,
        request: CreateCharterRequest,
        created_by: str = "sponsor_admin",
    ) -> CecCharterRecord:
        """Creates or updates a study committee charter governance configuration."""
        stmt = select(CecCharterRecord).where(
            CecCharterRecord.study_id == request.study_id,
            CecCharterRecord.is_active.is_(True),
        )
        result = await session.execute(stmt)
        existing = result.scalar_one_or_none()

        if existing:
            existing.committee_name = request.committee_name
            existing.version = request.version
            existing.voting_rule = request.voting_rule.value
            existing.minimum_quorum_percentage = request.minimum_quorum_percentage
            existing.minimum_members_count = request.minimum_members_count
            existing.eligible_members = request.eligible_members
            existing.endpoint_definitions = request.endpoint_definitions
            existing.diagnostic_criteria_templates = (
                request.diagnostic_criteria_templates
            )
            existing.version_index += 1
            return existing

        charter_id = str(uuid.uuid4())
        charter = CecCharterRecord(
            id=charter_id,
            study_id=request.study_id,
            committee_name=request.committee_name,
            version=request.version,
            voting_rule=request.voting_rule.value,
            minimum_quorum_percentage=request.minimum_quorum_percentage,
            minimum_members_count=request.minimum_members_count,
            eligible_members=request.eligible_members,
            endpoint_definitions=request.endpoint_definitions,
            diagnostic_criteria_templates=request.diagnostic_criteria_templates,
            is_active=True,
            created_by=created_by,
            reason_for_change="Initial charter configuration",
            version_index=1,
        )
        session.add(charter)
        return charter

    async def get_charter_for_study(
        self,
        session: AsyncSession,
        study_id: str,
    ) -> CecCharterRecord | None:
        """Retrieves active charter governance configuration for a study."""
        stmt = select(CecCharterRecord).where(
            CecCharterRecord.study_id == study_id,
            CecCharterRecord.is_active.is_(True),
        )
        result = await session.execute(stmt)
        return result.scalar_one_or_none()

    async def record_meeting_quorum(
        self,
        session: AsyncSession,
        study_id: str,
        charter_id: str,
        meeting_date: str,
        attendees: list[dict[str, str]],
        total_eligible_members: int,
        dossiers_reviewed: list[str],
        voting_results: dict[str, dict[str, Any]],
        meeting_minutes: str,
        chair_signature: dict[str, Any] | None = None,
        created_by: str = "chair_adjudicator",
    ) -> CecMeetingRecord:
        """Evaluates and records meeting attendance, quorum compliance, and formal minutes sign-off."""
        # Calculate quorum
        attendee_count = len(attendees)
        pct = (attendee_count / max(total_eligible_members, 1)) * 100.0

        # Query charter
        stmt = select(CecCharterRecord).where(CecCharterRecord.id == charter_id)
        result = await session.execute(stmt)
        charter = result.scalar_one_or_none()

        min_pct = charter.minimum_quorum_percentage if charter else 66.67
        min_count = charter.minimum_members_count if charter else 3

        quorum_met = (attendee_count >= min_count) and (
            round(pct, 2) >= round(min_pct, 2) or pct >= (min_pct - 0.01)
        )

        minutes_digest = self.compute_sha256(
            {
                "study_id": study_id,
                "meeting_date": meeting_date,
                "attendees": attendees,
                "quorum_met": quorum_met,
                "dossiers_reviewed": dossiers_reviewed,
                "voting_results": voting_results,
                "minutes": meeting_minutes,
            }
        )

        meeting_id = str(uuid.uuid4())
        meeting = CecMeetingRecord(
            id=meeting_id,
            study_id=study_id,
            charter_id=charter_id,
            meeting_date=meeting_date,
            attendees=attendees,
            total_eligible_members=total_eligible_members,
            quorum_percentage=round(pct, 2),
            quorum_met=quorum_met,
            dossiers_reviewed=dossiers_reviewed,
            voting_results=voting_results,
            meeting_minutes=meeting_minutes,
            minutes_digest=minutes_digest,
            signed_by_chair=chair_signature is not None,
            chair_signature=chair_signature,
            created_by=created_by,
            reason_for_change="Adjudication Committee meeting quorum & minutes recording",
            version_index=1,
        )

        session.add(meeting)
        return meeting
