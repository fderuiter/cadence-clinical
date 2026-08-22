"""Application service orchestrating Hybrid FHIR-to-CDISC Semantic Interoperability Mapping.

Requirements: PRD-CRF-007, PRD-SYS-001, PRD-SYS-051
"""

import time
from typing import Any

from apps.interop.domain.concept_maps import (
    derive_subject_age,
    lookup_concept_by_code,
    map_fhir_gender,
)
from apps.interop.domain.fhir_deid import (
    pseudonymize_identifier,
    strip_pii_from_patient,
)
from apps.interop.domain.ports import (
    EmbeddingMatcherPort,
    LLMSemanticReasonerPort,
)
from apps.interop.domain.semantic_mapping_models import (
    CDISCDomain,
    FHIRSemanticMapResult,
    HybridMappingConfig,
    MappingStatus,
    MappingTier,
    MappingTierStatistics,
    SemanticMappedItem,
)


class SemanticMapperService:
    """Orchestrates 3-tier hybrid semantic mapping (Deterministic -> Embedding -> LLM Fallback)."""

    def __init__(
        self,
        embedding_matcher: EmbeddingMatcherPort | None = None,
        llm_reasoner: LLMSemanticReasonerPort | None = None,
    ) -> None:
        self.embedding_matcher = embedding_matcher
        self.llm_reasoner = llm_reasoner

    async def map_fhir_bundle(
        self,
        bundle: dict[str, Any],
        config: HybridMappingConfig | None = None,
    ) -> FHIRSemanticMapResult:
        """Map a standard HL7 FHIR Bundle into structured CDISC SDTM/CDASH fields and records.

        Args:
            bundle: HL7 FHIR Bundle JSON payload.
            config: Hybrid mapping configuration and threshold overrides.

        Returns:
            FHIRSemanticMapResult containing mapped eCRF fields, clinical records, and tier metrics.
        """
        start_time = time.perf_counter()
        cfg = config or HybridMappingConfig()

        entries = bundle.get("entry", [])
        if not isinstance(entries, list):
            entries = []

        patient_pseudonym = "unknown_pseudonym"
        de_identified_patient: dict[str, Any] | None = None
        custom_terms: list[str] = []

        mapped_fields: dict[str, Any] = {}
        mapped_items: list[SemanticMappedItem] = []
        vital_signs: list[dict[str, Any]] = []
        labs: list[dict[str, Any]] = []
        conditions: list[dict[str, Any]] = []
        medications: list[dict[str, Any]] = []
        adverse_events: list[dict[str, Any]] = []
        procedures: list[dict[str, Any]] = []
        unstructured_notes: list[dict[str, Any]] = []

        # 1. First Pass: Locate Patient Resource for Pseudonymization & Demographics
        for entry in entries:
            if not isinstance(entry, dict):
                continue
            resource = entry.get("resource")
            if not isinstance(resource, dict):
                continue
            if resource.get("resourceType") == "Patient":
                raw_id = resource.get("id", "unknown")
                patient_pseudonym = pseudonymize_identifier(raw_id)
                de_identified_patient = strip_pii_from_patient(resource)

                # Extract patient names for LLM de-identification air-gap
                for name_entry in resource.get("name", []):
                    if isinstance(name_entry, dict):
                        for g in name_entry.get("given", []):
                            if g:
                                custom_terms.append(str(g))
                        fam = name_entry.get("family")
                        if fam:
                            custom_terms.append(str(fam))

                self._process_patient_demographics(
                    resource=resource,
                    study_id=cfg.study_id,
                    patient_pseudonym=patient_pseudonym,
                    mapped_fields=mapped_fields,
                    mapped_items=mapped_items,
                )
                break

        # Fallback for subject reference if Patient resource is omitted
        if patient_pseudonym == "unknown_pseudonym":
            for entry in entries:
                if not isinstance(entry, dict):
                    continue
                resource = entry.get("resource")
                if not isinstance(resource, dict):
                    continue
                subject_ref = resource.get("subject", {}).get("reference", "")
                if subject_ref.startswith("Patient/"):
                    raw_id = subject_ref.split("/")[-1]
                    patient_pseudonym = pseudonymize_identifier(raw_id)
                    mapped_fields["DM.USUBJID"] = f"{cfg.study_id}-{patient_pseudonym}"
                    mapped_fields["DM.SUBJID"] = patient_pseudonym[:12]
                    break

        # 2. Second Pass: Process Clinical Observations, Conditions, Meds, Procedures, Notes
        for entry in entries:
            if not isinstance(entry, dict):
                continue
            resource = entry.get("resource")
            if not isinstance(resource, dict):
                continue
            res_type = resource.get("resourceType")

            if res_type == "Observation":
                await self._process_observation(
                    resource=resource,
                    config=cfg,
                    mapped_items=mapped_items,
                    mapped_fields=mapped_fields,
                    vital_signs=vital_signs,
                    labs=labs,
                )
            elif res_type == "Condition":
                await self._process_condition(
                    resource=resource,
                    config=cfg,
                    mapped_items=mapped_items,
                    mapped_fields=mapped_fields,
                    conditions=conditions,
                    adverse_events=adverse_events,
                )
            elif res_type in (
                "MedicationStatement",
                "MedicationRequest",
                "MedicationAdministration",
            ):
                await self._process_medication(
                    resource=resource,
                    config=cfg,
                    mapped_items=mapped_items,
                    mapped_fields=mapped_fields,
                    medications=medications,
                )
            elif res_type == "Procedure":
                await self._process_procedure(
                    resource=resource,
                    config=cfg,
                    mapped_items=mapped_items,
                    mapped_fields=mapped_fields,
                    procedures=procedures,
                )
            elif res_type in ("DocumentReference", "DiagnosticReport"):
                await self._process_unstructured_document(
                    resource=resource,
                    config=cfg,
                    custom_terms=custom_terms,
                    mapped_items=mapped_items,
                    mapped_fields=mapped_fields,
                    unstructured_notes=unstructured_notes,
                )

        # 3. Derive Summary Telemetry & Statistics
        duration_ms = (time.perf_counter() - start_time) * 1000.0
        stats = self._calculate_tier_statistics(mapped_items, duration_ms)

        clinical_records = {
            "vital_signs": vital_signs,
            "labs": labs,
            "conditions": conditions,
            "medications": medications,
            "adverse_events": adverse_events,
            "procedures": procedures,
        }

        return FHIRSemanticMapResult(
            study_id=cfg.study_id,
            subject_pseudonym=patient_pseudonym,
            de_identified_patient=de_identified_patient,
            mapped_fields=mapped_fields,
            mapped_items=mapped_items,
            clinical_records=clinical_records,
            unstructured_notes=unstructured_notes,
            statistics=stats,
        )

    def _process_patient_demographics(
        self,
        resource: dict[str, Any],
        study_id: str,
        patient_pseudonym: str,
        mapped_fields: dict[str, Any],
        mapped_items: list[SemanticMappedItem],
    ) -> None:
        """Process FHIR Patient resource into CDASH Demographics fields."""
        mapped_fields["DM.USUBJID"] = f"{study_id}-{patient_pseudonym}"
        mapped_fields["DM.SUBJID"] = patient_pseudonym[:12]

        # Birth Date & Derived Age
        birth_date = resource.get("birthDate")
        if birth_date:
            mapped_fields["DM.BRTHDTC"] = birth_date
            mapped_items.append(
                SemanticMappedItem(
                    source_resource_type="Patient",
                    source_id=resource.get("id"),
                    source_code="birthDate",
                    source_display="Birth Date",
                    target_domain=CDISCDomain.DM,
                    target_variable="eCRF.DM.BRTHDTC",
                    cdash_testcd="BRTHDTC",
                    cdash_test="Date of Birth",
                    extracted_value=birth_date,
                    mapping_tier=MappingTier.DETERMINISTIC,
                    confidence_score=1.0,
                    provenance="Mapped directly from FHIR Patient.birthDate",
                )
            )
            age = derive_subject_age(birth_date)
            if age is not None:
                mapped_fields["eCRF.DM.AGE"] = age
                mapped_items.append(
                    SemanticMappedItem(
                        source_resource_type="Patient",
                        source_id=resource.get("id"),
                        source_code="age",
                        source_display="Calculated Age",
                        target_domain=CDISCDomain.DM,
                        target_variable="eCRF.DM.AGE",
                        cdash_testcd="AGE",
                        cdash_test="Age",
                        extracted_value=age,
                        extracted_unit="YEARS",
                        mapping_tier=MappingTier.DETERMINISTIC,
                        confidence_score=1.0,
                        provenance="Derived from birthDate calculation",
                    )
                )

        # Gender
        gender = resource.get("gender")
        if gender:
            cdash_sex = map_fhir_gender(gender)
            mapped_fields["DM.SEX"] = cdash_sex
            mapped_fields["eCRF.DM.SEX"] = cdash_sex
            mapped_items.append(
                SemanticMappedItem(
                    source_resource_type="Patient",
                    source_id=resource.get("id"),
                    source_code="gender",
                    source_display=gender,
                    target_domain=CDISCDomain.DM,
                    target_variable="eCRF.DM.SEX",
                    cdash_testcd="SEX",
                    cdash_test="Sex",
                    extracted_value=cdash_sex,
                    mapping_tier=MappingTier.DETERMINISTIC,
                    confidence_score=1.0,
                    provenance="Mapped from FHIR Patient.gender codelist",
                )
            )

    async def _process_observation(
        self,
        resource: dict[str, Any],
        config: HybridMappingConfig,
        mapped_items: list[SemanticMappedItem],
        mapped_fields: dict[str, Any],
        vital_signs: list[dict[str, Any]],
        labs: list[dict[str, Any]],
    ) -> None:
        """Process FHIR Observation resource through the 3-tier cascade."""
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        extracted_val = (
            resource.get("valueQuantity", {}).get("value")
            or resource.get("valueString")
            or resource.get("valueInteger")
        )
        extracted_unit = resource.get("valueQuantity", {}).get("unit")
        obs_date = resource.get("effectiveDateTime") or resource.get("issued")

        matched_element = None
        tier = MappingTier.DETERMINISTIC
        confidence = 1.0
        provenance = ""

        # Tier 1: Deterministic ConceptMap lookup by code/system
        if config.enable_deterministic and code_codings:
            for coding in code_codings:
                code_val = coding.get("code", "")
                sys_val = coding.get("system", "")
                if not display_name:
                    display_name = coding.get("display", "")

                matched_element = lookup_concept_by_code(code_val, sys_val)
                if matched_element:
                    provenance = f"Deterministic ConceptMap match for code '{code_val}' in system '{sys_val}'"
                    break

        # Tier 2: Embedding semantic search if not matched deterministically
        if (
            not matched_element
            and config.enable_embedding
            and (display_name or code_codings)
        ):
            search_query = (
                display_name
                or (code_codings[0].get("display") if code_codings else "")
                or (code_codings[0].get("code") if code_codings else "")
            )
            matched_element, conf = await self.embedding_matcher.match_concept(
                query_text=search_query,
                min_confidence=config.embedding_confidence_threshold,
            )
            if matched_element:
                tier = MappingTier.EMBEDDING
                confidence = conf
                provenance = f"Embedding cosine similarity match ({confidence:.2f}) for query '{search_query}'"

        # Tier 3: LLM reasoning fallback if still unresolved
        if not matched_element and config.enable_llm_fallback and display_name:
            llm_items = await self.llm_reasoner.extract_concepts_from_narrative(
                narrative_text=display_name, study_id=config.study_id
            )
            if llm_items:
                first = llm_items[0]
                first.source_id = resource.get("id")
                first.observation_date = obs_date
                if extracted_val is not None and first.extracted_value is None:
                    first.extracted_value = extracted_val
                if extracted_unit and first.extracted_unit is None:
                    first.extracted_unit = extracted_unit

                mapped_items.append(first)
                if first.target_variable and first.extracted_value is not None:
                    mapped_fields[first.target_variable] = first.extracted_value

                rec = {
                    "source_id": resource.get("id"),
                    "display_name": display_name,
                    "cdash_testcd": first.cdash_testcd,
                    "cdash_test": first.cdash_test,
                    "value": first.extracted_value,
                    "unit": first.extracted_unit,
                    "date": obs_date,
                }
                if first.target_domain == CDISCDomain.VS:
                    vital_signs.append(rec)
                else:
                    labs.append(rec)
                return

        # Record resolved mapped item
        if matched_element:
            needs_review = confidence < config.human_review_confidence_threshold
            mapped_item = SemanticMappedItem(
                source_resource_type="Observation",
                source_id=resource.get("id"),
                source_code=(code_codings[0].get("code") if code_codings else None),
                source_system=(code_codings[0].get("system") if code_codings else None),
                source_display=display_name or matched_element.source_display,
                target_domain=matched_element.target_domain,
                target_variable=matched_element.target_variable,
                cdash_testcd=matched_element.cdash_testcd,
                cdash_test=matched_element.cdash_test,
                extracted_value=extracted_val,
                extracted_unit=extracted_unit or matched_element.standard_unit,
                observation_date=obs_date,
                mapping_tier=tier,
                confidence_score=confidence,
                provenance=provenance,
                needs_human_review=needs_review,
                status=(
                    MappingStatus.FLAGGED_FOR_REVIEW
                    if needs_review
                    else MappingStatus.MAPPED
                ),
            )
            mapped_items.append(mapped_item)

            if mapped_item.target_variable and mapped_item.extracted_value is not None:
                mapped_fields[mapped_item.target_variable] = mapped_item.extracted_value

            record_entry = {
                "source_id": resource.get("id"),
                "loinc_code": mapped_item.source_code or "",
                "display_name": mapped_item.source_display or "",
                "cdash_testcd": mapped_item.cdash_testcd,
                "cdash_test": mapped_item.cdash_test,
                "value": extracted_val,
                "unit": extracted_unit or matched_element.standard_unit,
                "date": obs_date,
            }

            if matched_element.target_domain == CDISCDomain.VS:
                vital_signs.append(record_entry)
            else:
                labs.append(record_entry)
        else:
            # Unmapped observation
            mapped_items.append(
                SemanticMappedItem(
                    source_resource_type="Observation",
                    source_id=resource.get("id"),
                    source_code=(code_codings[0].get("code") if code_codings else None),
                    source_display=display_name,
                    target_domain=CDISCDomain.LB,
                    target_variable="UNMAPPED.OBSERVATION",
                    extracted_value=extracted_val,
                    mapping_tier=MappingTier.DETERMINISTIC,
                    confidence_score=0.0,
                    provenance="No matching ConceptMap or semantic candidate found",
                    needs_human_review=True,
                    status=MappingStatus.UNMAPPED,
                )
            )

    async def _process_condition(
        self,
        resource: dict[str, Any],
        config: HybridMappingConfig,
        mapped_items: list[SemanticMappedItem],
        mapped_fields: dict[str, Any],
        conditions: list[dict[str, Any]],
        adverse_events: list[dict[str, Any]],
    ) -> None:
        """Process FHIR Condition resource into MH or AE domain."""
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        onset_date = (
            resource.get("onsetDateTime")
            or resource.get("recordedDate")
            or resource.get("onsetPeriod", {}).get("start")
        )

        matched_elem = None
        tier = MappingTier.DETERMINISTIC
        confidence = 1.0
        provenance = ""

        if config.enable_deterministic and code_codings:
            for coding in code_codings:
                c_val = coding.get("code", "")
                s_val = coding.get("system", "")
                if not display_name:
                    display_name = coding.get("display", "")
                matched_elem = lookup_concept_by_code(c_val, s_val)
                if matched_elem:
                    provenance = (
                        f"Deterministic ConceptMap match for Condition code '{c_val}'"
                    )
                    break

        if not matched_elem and config.enable_embedding and display_name:
            matched_elem, conf = await self.embedding_matcher.match_concept(
                query_text=display_name,
                min_confidence=config.embedding_confidence_threshold,
            )
            if matched_elem:
                tier = MappingTier.EMBEDDING
                confidence = conf
                provenance = f"Embedding similarity match ({confidence:.2f}) for Condition '{display_name}'"

        term_val = display_name or (
            matched_elem.source_display if matched_elem else "Unknown Condition"
        )
        target_var = matched_elem.target_variable if matched_elem else "eCRF.MH.MHTERM"
        domain = matched_elem.target_domain if matched_elem else CDISCDomain.MH

        mapped_item = SemanticMappedItem(
            source_resource_type="Condition",
            source_id=resource.get("id"),
            source_code=(code_codings[0].get("code") if code_codings else None),
            source_display=display_name or term_val,
            target_domain=domain,
            target_variable=target_var,
            cdash_testcd="MHTERM",
            cdash_test="Medical History Term",
            extracted_value=term_val,
            observation_date=onset_date,
            mapping_tier=tier,
            confidence_score=confidence,
            provenance=provenance or f"Mapped condition term '{term_val}' to {domain}",
            needs_human_review=(confidence < config.human_review_confidence_threshold),
            status=MappingStatus.MAPPED,
        )
        mapped_items.append(mapped_item)

        # Update eCRF context list
        existing_mh = mapped_fields.get("eCRF.MH.MHTERM")
        if existing_mh is None:
            mapped_fields["eCRF.MH.MHTERM"] = term_val
        elif isinstance(existing_mh, list):
            existing_mh.append(term_val)
        else:
            mapped_fields["eCRF.MH.MHTERM"] = [existing_mh, term_val]

        conditions.append(
            {
                "condition_code": (code_codings[0].get("code") if code_codings else ""),
                "display_name": term_val,
                "onset_date": onset_date,
                "clinical_status": resource.get("clinicalStatus", {})
                .get("coding", [{}])[0]
                .get("code", "active"),
                "cdash_variable": "MH.MHTERM",
            }
        )

    async def _process_medication(
        self,
        resource: dict[str, Any],
        config: HybridMappingConfig,
        mapped_items: list[SemanticMappedItem],
        mapped_fields: dict[str, Any],
        medications: list[dict[str, Any]],
    ) -> None:
        """Process FHIR Medication resource into CM domain."""
        med_concept = resource.get("medicationCodeableConcept", {})
        code_codings = med_concept.get("coding", [])
        display_name = med_concept.get("text", "")
        start_date = (
            resource.get("effectiveDateTime")
            or resource.get("dateAsserted")
            or resource.get("effectivePeriod", {}).get("start")
        )

        matched_elem = None
        tier = MappingTier.DETERMINISTIC
        confidence = 1.0
        provenance = ""

        if config.enable_deterministic and code_codings:
            for coding in code_codings:
                c_val = coding.get("code", "")
                s_val = coding.get("system", "")
                if not display_name:
                    display_name = coding.get("display", "")
                matched_elem = lookup_concept_by_code(c_val, s_val)
                if matched_elem:
                    provenance = (
                        f"Deterministic ConceptMap match for Medication code '{c_val}'"
                    )
                    break

        if not matched_elem and config.enable_embedding and display_name:
            matched_elem, conf = await self.embedding_matcher.match_concept(
                query_text=display_name,
                min_confidence=config.embedding_confidence_threshold,
            )
            if matched_elem:
                tier = MappingTier.EMBEDDING
                confidence = conf
                provenance = f"Embedding similarity match ({confidence:.2f}) for Medication '{display_name}'"

        med_val = display_name or (
            matched_elem.source_display if matched_elem else "Unknown Medication"
        )

        mapped_item = SemanticMappedItem(
            source_resource_type=resource.get("resourceType", "MedicationStatement"),
            source_id=resource.get("id"),
            source_code=(code_codings[0].get("code") if code_codings else None),
            source_display=display_name or med_val,
            target_domain=CDISCDomain.CM,
            target_variable="eCRF.CM.CMTRT",
            cdash_testcd="CMTRT",
            cdash_test="Reported Name of Drug, Med, or Therapy",
            extracted_value=med_val,
            observation_date=start_date,
            mapping_tier=tier,
            confidence_score=confidence,
            provenance=provenance or f"Mapped medication term '{med_val}' to CM",
            needs_human_review=(confidence < config.human_review_confidence_threshold),
            status=MappingStatus.MAPPED,
        )
        mapped_items.append(mapped_item)

        existing_cm = mapped_fields.get("eCRF.CM.CMTRT")
        if existing_cm is None:
            mapped_fields["eCRF.CM.CMTRT"] = med_val
        elif isinstance(existing_cm, list):
            existing_cm.append(med_val)
        else:
            mapped_fields["eCRF.CM.CMTRT"] = [existing_cm, med_val]

        medications.append(
            {
                "medication_code": (
                    code_codings[0].get("code") if code_codings else ""
                ),
                "display_name": med_val,
                "start_date": start_date,
                "status": resource.get("status", "unknown"),
                "cdash_variable": "CM.CMTRT",
            }
        )

    async def _process_procedure(
        self,
        resource: dict[str, Any],
        config: HybridMappingConfig,
        mapped_items: list[SemanticMappedItem],
        mapped_fields: dict[str, Any],
        procedures: list[dict[str, Any]],
    ) -> None:
        """Process FHIR Procedure resource into PR domain."""
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        proc_date = resource.get("performedDateTime") or resource.get(
            "performedPeriod", {}
        ).get("start")

        matched_elem = None
        tier = MappingTier.DETERMINISTIC
        confidence = 1.0
        provenance = ""

        if config.enable_deterministic and code_codings:
            for coding in code_codings:
                c_val = coding.get("code", "")
                s_val = coding.get("system", "")
                if not display_name:
                    display_name = coding.get("display", "")
                matched_elem = lookup_concept_by_code(c_val, s_val)
                if matched_elem:
                    provenance = (
                        f"Deterministic ConceptMap match for Procedure code '{c_val}'"
                    )
                    break

        if not matched_elem and config.enable_embedding and display_name:
            matched_elem, conf = await self.embedding_matcher.match_concept(
                query_text=display_name,
                min_confidence=config.embedding_confidence_threshold,
            )
            if matched_elem:
                tier = MappingTier.EMBEDDING
                confidence = conf
                provenance = f"Embedding similarity match ({confidence:.2f}) for Procedure '{display_name}'"

        proc_val = display_name or (
            matched_elem.source_display if matched_elem else "Unknown Procedure"
        )

        mapped_item = SemanticMappedItem(
            source_resource_type="Procedure",
            source_id=resource.get("id"),
            source_code=(code_codings[0].get("code") if code_codings else None),
            source_display=display_name or proc_val,
            target_domain=CDISCDomain.PR,
            target_variable="eCRF.PR.PRTRT",
            cdash_testcd="PRTRT",
            cdash_test="Reported Name of Procedure",
            extracted_value=proc_val,
            observation_date=proc_date,
            mapping_tier=tier,
            confidence_score=confidence,
            provenance=provenance or f"Mapped procedure term '{proc_val}' to PR",
            needs_human_review=(confidence < config.human_review_confidence_threshold),
            status=MappingStatus.MAPPED,
        )
        mapped_items.append(mapped_item)

        procedures.append(
            {
                "procedure_code": (code_codings[0].get("code") if code_codings else ""),
                "display_name": proc_val,
                "performed_date": proc_date,
                "status": resource.get("status", "completed"),
                "cdash_variable": "PR.PRTRT",
            }
        )

    async def _process_unstructured_document(
        self,
        resource: dict[str, Any],
        config: HybridMappingConfig,
        custom_terms: list[str],
        mapped_items: list[SemanticMappedItem],
        mapped_fields: dict[str, Any],
        unstructured_notes: list[dict[str, Any]],
    ) -> None:
        """Process FHIR DocumentReference or narrative notes using LLM fallback."""
        if not config.enable_llm_fallback:
            return

        text_content = ""
        # Extract from narrative div or content attachment
        if "text" in resource and isinstance(resource["text"], dict):
            text_content = resource["text"].get("div", "")
        elif "content" in resource and isinstance(resource["content"], list):
            for c in resource["content"]:
                attachment = c.get("attachment", {})
                if attachment.get("data"):
                    text_content += attachment.get("data", "")
                elif attachment.get("title"):
                    text_content += " " + attachment.get("title", "")

        if not text_content.strip():
            return

        extracted = await self.llm_reasoner.extract_concepts_from_narrative(
            narrative_text=text_content,
            study_id=config.study_id,
            custom_terms=custom_terms,
        )

        for item in extracted:
            item.source_id = resource.get("id")
            mapped_items.append(item)
            if item.target_variable and item.extracted_value is not None:
                mapped_fields[item.target_variable] = item.extracted_value

        unstructured_notes.append(
            {
                "source_id": resource.get("id"),
                "extracted_concepts_count": len(extracted),
                "processed_timestamp": time.time(),
            }
        )

    def _calculate_tier_statistics(
        self, mapped_items: list[SemanticMappedItem], duration_ms: float
    ) -> MappingTierStatistics:
        """Aggregate execution metrics across mapping tiers."""
        deterministic = 0
        embedding = 0
        llm = 0
        unmapped = 0
        flagged = 0

        for item in mapped_items:
            if item.status == MappingStatus.UNMAPPED:
                unmapped += 1
            elif item.mapping_tier == MappingTier.DETERMINISTIC:
                deterministic += 1
            elif item.mapping_tier == MappingTier.EMBEDDING:
                embedding += 1
            elif item.mapping_tier == MappingTier.LLM_FALLBACK:
                llm += 1

            if item.needs_human_review:
                flagged += 1

        return MappingTierStatistics(
            total_extracted=len(mapped_items),
            deterministic_count=deterministic,
            embedding_count=embedding,
            llm_fallback_count=llm,
            unmapped_count=unmapped,
            flagged_for_review_count=flagged,
            execution_latency_ms=round(duration_ms, 2),
        )
