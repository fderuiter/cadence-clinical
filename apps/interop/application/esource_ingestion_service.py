"""eSource Ingestion Pipeline and Normalization Service.

Ingests and normalizes FHIR resources (Patient, Observation, Condition, MedicationRequest, MedicationStatement)
mapped to CDASH clinical domain models (DM, VS, LB, MH, CM) with complete source provenance tracking.

Requirements: PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, ADR-2199, FDA Electronic Source Data Guidance
"""

from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

import httpx

from apps.interop.application.smart_auth_service import SmartAuthService
from apps.interop.domain.concept_maps import (
    derive_subject_age,
    lookup_concept_by_code,
    map_fhir_gender,
)
from apps.interop.domain.esource_models import (
    DiscrepancyType,
    EhrCandidateRecord,
    EhrEndpointConfig,
)
from apps.interop.domain.fhir_deid import (
    pseudonymize_identifier,
    strip_pii_from_patient,
)
from apps.interop.domain.semantic_mapping_models import CDISCDomain


class EsourceIngestionService:
    """Orchestrates automated EHR polling, FHIR extraction, CDASH normalization, and candidate queue generation."""

    def __init__(self, smart_auth: SmartAuthService | None = None) -> None:
        self.smart_auth = smart_auth or SmartAuthService()

    def normalize_fhir_bundle(
        self,
        bundle: dict[str, Any],
        study_id: str,
        site_id: str = "SITE-01",
        ehr_system_id: str = "SMART_FHIR_EHR",
        current_ecrf_context: dict[str, Any] | None = None,
    ) -> list[EhrCandidateRecord]:
        """Normalize a FHIR R4 Bundle into candidate eSource records mapped to CDASH domains.

        Args:
            bundle: HL7 FHIR Bundle JSON payload.
            study_id: Clinical study identifier.
            site_id: Clinical trial site identifier.
            ehr_system_id: Hospital EHR system identifier.
            current_ecrf_context: Current eCRF field values in EDC for comparison.

        Returns:
            List of EhrCandidateRecord objects in PENDING_REVIEW status.
        """
        ecrf_ctx = current_ecrf_context or {}
        entries = bundle.get("entry", [])
        if not isinstance(entries, list):
            entries = []

        candidates: list[EhrCandidateRecord] = []

        # 1. First Pass: Locate Patient resource for subject pseudonymization
        raw_patient_id = "unknown"
        patient_pseudonym = "unknown_pseudonym"
        patient_resource: dict[str, Any] | None = None

        for entry in entries:
            res = entry.get("resource", {}) if isinstance(entry, dict) else {}
            if res.get("resourceType") == "Patient":
                raw_patient_id = str(res.get("id", "unknown"))
                patient_pseudonym = pseudonymize_identifier(raw_patient_id)
                patient_resource = res
                break

        if patient_pseudonym == "unknown_pseudonym":
            for entry in entries:
                res = entry.get("resource", {}) if isinstance(entry, dict) else {}
                subject_ref = res.get("subject", {}).get("reference", "")
                if subject_ref.startswith("Patient/"):
                    raw_patient_id = subject_ref.split("/")[-1]
                    patient_pseudonym = pseudonymize_identifier(raw_patient_id)
                    break

        # Process Demographics (DM) if Patient resource is present
        if patient_resource:
            self._normalize_patient_demographics(
                resource=patient_resource,
                study_id=study_id,
                site_id=site_id,
                raw_patient_id=raw_patient_id,
                patient_pseudonym=patient_pseudonym,
                ehr_system_id=ehr_system_id,
                ecrf_ctx=ecrf_ctx,
                candidates=candidates,
            )

        # 2. Second Pass: Process Clinical Resources (Observation, Condition, Medication)
        for entry in entries:
            res = entry.get("resource", {}) if isinstance(entry, dict) else {}
            res_type = res.get("resourceType")

            if res_type == "Observation":
                self._normalize_observation(
                    resource=res,
                    study_id=study_id,
                    site_id=site_id,
                    patient_id=raw_patient_id,
                    patient_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    ecrf_ctx=ecrf_ctx,
                    candidates=candidates,
                )
            elif res_type == "Condition":
                self._normalize_condition(
                    resource=res,
                    study_id=study_id,
                    site_id=site_id,
                    patient_id=raw_patient_id,
                    patient_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    ecrf_ctx=ecrf_ctx,
                    candidates=candidates,
                )
            elif res_type in (
                "MedicationRequest",
                "MedicationStatement",
                "MedicationAdministration",
            ):
                self._normalize_medication(
                    resource=res,
                    study_id=study_id,
                    site_id=site_id,
                    patient_id=raw_patient_id,
                    patient_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    ecrf_ctx=ecrf_ctx,
                    candidates=candidates,
                )
            elif res_type == "Procedure":
                self._normalize_procedure(
                    resource=res,
                    study_id=study_id,
                    site_id=site_id,
                    patient_id=raw_patient_id,
                    patient_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    ecrf_ctx=ecrf_ctx,
                    candidates=candidates,
                )
            elif res_type == "Encounter":
                self._normalize_encounter(
                    resource=res,
                    study_id=study_id,
                    site_id=site_id,
                    patient_id=raw_patient_id,
                    patient_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    ecrf_ctx=ecrf_ctx,
                    candidates=candidates,
                )

        return candidates

    def _compare_with_ecrf(
        self,
        target_variable: str,
        candidate_value: Any,
        candidate_unit: str | None,
        ecrf_ctx: dict[str, Any],
    ) -> tuple[Any | None, DiscrepancyType]:
        """Compare candidate EHR value against current eCRF context."""
        current_val = ecrf_ctx.get(target_variable)
        if current_val is None:
            # Check without eCRF. prefix
            short_var = target_variable.removeprefix("eCRF.")
            current_val = ecrf_ctx.get(short_var)

        if current_val is None:
            return None, DiscrepancyType.NEW_FIELD

        # Compare values
        if str(current_val).strip().lower() == str(candidate_value).strip().lower():
            return current_val, DiscrepancyType.EXACT_MATCH

        # Check numeric equivalence
        try:
            if float(current_val) == float(candidate_value):
                return current_val, DiscrepancyType.EXACT_MATCH
        except ValueError, TypeError:
            pass

        return current_val, DiscrepancyType.VALUE_MISMATCH

    def _normalize_patient_demographics(
        self,
        resource: dict[str, Any],
        study_id: str,
        site_id: str,
        raw_patient_id: str,
        patient_pseudonym: str,
        ehr_system_id: str,
        ecrf_ctx: dict[str, Any],
        candidates: list[EhrCandidateRecord],
    ) -> None:
        """Extract CDASH DM domain variables from FHIR Patient resource."""
        res_id = str(resource.get("id", raw_patient_id))
        now_iso = datetime.now().isoformat()
        deid_patient = strip_pii_from_patient(resource)

        # 1. Gender / Sex (DM.SEX)
        gender_raw = resource.get("gender")
        if gender_raw:
            cdash_sex = map_fhir_gender(str(gender_raw))
            current_val, disc = self._compare_with_ecrf(
                "eCRF.DM.SEX", cdash_sex, None, ecrf_ctx
            )
            candidates.append(
                EhrCandidateRecord(
                    study_id=study_id,
                    site_id=site_id,
                    subject_id=raw_patient_id,
                    subject_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    fhir_resource_type="Patient",
                    fhir_resource_id=res_id,
                    source_timestamp=now_iso,
                    target_domain=CDISCDomain.DM,
                    target_variable="eCRF.DM.SEX",
                    cdash_testcd="SEX",
                    cdash_test="Sex",
                    candidate_value=cdash_sex,
                    candidate_unit=None,
                    current_ecrf_value=current_val,
                    discrepancy_type=disc,
                    confidence_score=1.0,
                    mapping_provenance="Mapped directly from FHIR Patient.gender to CDASH DM.SEX",
                    raw_fhir_resource=deid_patient,
                )
            )

        # 2. Birth Date (DM.BRTHDTC)
        birth_date = resource.get("birthDate")
        if birth_date:
            current_val, disc = self._compare_with_ecrf(
                "eCRF.DM.BRTHDTC", birth_date, None, ecrf_ctx
            )
            candidates.append(
                EhrCandidateRecord(
                    study_id=study_id,
                    site_id=site_id,
                    subject_id=raw_patient_id,
                    subject_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    fhir_resource_type="Patient",
                    fhir_resource_id=res_id,
                    source_timestamp=now_iso,
                    target_domain=CDISCDomain.DM,
                    target_variable="eCRF.DM.BRTHDTC",
                    cdash_testcd="BRTHDTC",
                    cdash_test="Date of Birth",
                    candidate_value=birth_date,
                    candidate_unit=None,
                    current_ecrf_value=current_val,
                    discrepancy_type=disc,
                    confidence_score=1.0,
                    mapping_provenance="Mapped directly from FHIR Patient.birthDate to CDASH DM.BRTHDTC",
                    raw_fhir_resource=deid_patient,
                )
            )

            # Calculated Age (DM.AGE)
            age = derive_subject_age(str(birth_date))
            if age is not None:
                current_val, disc = self._compare_with_ecrf(
                    "eCRF.DM.AGE", age, "YEARS", ecrf_ctx
                )
                candidates.append(
                    EhrCandidateRecord(
                        study_id=study_id,
                        site_id=site_id,
                        subject_id=raw_patient_id,
                        subject_pseudonym=patient_pseudonym,
                        ehr_system_id=ehr_system_id,
                        fhir_resource_type="Patient",
                        fhir_resource_id=res_id,
                        source_timestamp=now_iso,
                        target_domain=CDISCDomain.DM,
                        target_variable="eCRF.DM.AGE",
                        cdash_testcd="AGE",
                        cdash_test="Age",
                        candidate_value=age,
                        candidate_unit="YEARS",
                        current_ecrf_value=current_val,
                        discrepancy_type=disc,
                        confidence_score=1.0,
                        mapping_provenance="Calculated from FHIR Patient.birthDate to CDASH DM.AGE",
                        raw_fhir_resource=deid_patient,
                    )
                )

    def _normalize_observation(
        self,
        resource: dict[str, Any],
        study_id: str,
        site_id: str,
        patient_id: str,
        patient_pseudonym: str,
        ehr_system_id: str,
        ecrf_ctx: dict[str, Any],
        candidates: list[EhrCandidateRecord],
    ) -> None:
        """Extract CDASH VS (Vital Signs) or LB (Laboratory) domain variables from FHIR Observation."""
        res_id = str(resource.get("id", uuid.uuid4()))
        obs_date = (
            resource.get("effectiveDateTime")
            or resource.get("effectivePeriod", {}).get("start")
            or resource.get("issued")
            or datetime.now().isoformat()
        )

        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        loinc_code = ""
        for coding in code_codings:
            system = coding.get("system", "").lower()
            if "loinc" in system or not loinc_code:
                loinc_code = coding.get("code", "")
            if not display_name:
                display_name = coding.get("display", "")

        # Value quantity
        val_quantity = resource.get("valueQuantity", {})
        val_num = val_quantity.get("value")
        val_unit = val_quantity.get("unit") or val_quantity.get("code")

        if val_num is None:
            # Check valueString, valueCodeableConcept
            val_num = resource.get("valueString") or resource.get(
                "valueCodeableConcept", {}
            ).get("text")

        # Multi-component Observation (e.g. Blood Pressure panel 85354-9 with systolic and diastolic)
        components = resource.get("component", [])
        if components and isinstance(components, list):
            for comp in components:
                self._normalize_observation_component(
                    comp=comp,
                    parent_resource=resource,
                    obs_date=obs_date,
                    study_id=study_id,
                    site_id=site_id,
                    patient_id=patient_id,
                    patient_pseudonym=patient_pseudonym,
                    ehr_system_id=ehr_system_id,
                    ecrf_ctx=ecrf_ctx,
                    candidates=candidates,
                )
            return

        if val_num is None:
            return

        # Determine Vital Signs vs Laboratory
        category_codings = resource.get("category", [])
        is_vital = False
        for cat in category_codings:
            for c in cat.get("coding", []):
                if c.get("code", "").lower() == "vital-signs":
                    is_vital = True
                    break

        # Concept Map lookup
        concept = lookup_concept_by_code(loinc_code)

        vital_loincs = {
            "8480-6": ("SYSBP", "Systolic Blood Pressure", "mmHg"),
            "8462-4": ("DIABP", "Diastolic Blood Pressure", "mmHg"),
            "8867-4": ("PULSE", "Pulse Rate", "beats/min"),
            "8310-5": ("TEMP", "Temperature", "C"),
            "29463-7": ("WEIGHT", "Weight", "kg"),
            "8302-2": ("HEIGHT", "Height", "cm"),
            "9279-1": ("RESP", "Respiratory Rate", "breaths/min"),
            "2708-6": ("OXYSAT", "Oxygen Saturation", "%"),
            "59408-5": ("OXYSAT", "Oxygen Saturation", "%"),
            "39156-5": ("BMI", "Body Mass Index", "kg/m2"),
        }

        lab_loincs = {
            "2339-0": ("GLUC", "Glucose", "mg/dL"),
            "718-7": ("HGB", "Hemoglobin", "g/dL"),
            "777-3": ("PLAT", "Platelets", "10*3/uL"),
            "6690-2": ("WBC", "Leukocytes", "10*3/uL"),
            "2160-0": ("CREAT", "Creatinine", "mg/dL"),
            "1742-6": ("ALT", "Alanine Aminotransferase", "U/L"),
            "1920-8": ("AST", "Aspartate Aminotransferase", "U/L"),
            "2823-3": ("K", "Potassium", "mmol/L"),
            "2951-2": ("SODIUM", "Sodium", "mmol/L"),
            "17861-6": ("CA", "Calcium", "mg/dL"),
            "4548-4": ("HBA1C", "Hemoglobin A1c", "%"),
        }

        is_exam = False
        for cat in category_codings:
            for c in cat.get("coding", []):
                cat_code = c.get("code", "").lower()
                if cat_code in ("exam", "physical-examination"):
                    is_exam = True
                    break
        if concept and concept.target_domain == CDISCDomain.PE:
            is_exam = True

        if is_exam or (concept and concept.target_domain == CDISCDomain.PE):
            target_domain = CDISCDomain.PE
            testcd = concept.cdash_testcd if concept else "GENEXAM"
            testname = (
                concept.cdash_test
                if concept
                else (display_name or "Physical Examination")
            )
            default_unit = None
            target_variable = f"eCRF.PE.{testcd}"
            provenance = f"Mapped Physical Examination '{display_name}' ({loinc_code}) to CDASH PE.{testcd}"
        elif loinc_code in vital_loincs or is_vital or "vital" in display_name.lower():
            target_domain = CDISCDomain.VS
            if loinc_code in vital_loincs:
                testcd, testname, default_unit = vital_loincs[loinc_code]
            elif "systolic" in display_name.lower():
                testcd, testname, default_unit = (
                    "SYSBP",
                    "Systolic Blood Pressure",
                    "mmHg",
                )
            elif "diastolic" in display_name.lower():
                testcd, testname, default_unit = (
                    "DIABP",
                    "Diastolic Blood Pressure",
                    "mmHg",
                )
            elif "pulse" in display_name.lower() or "heart" in display_name.lower():
                testcd, testname, default_unit = ("PULSE", "Pulse Rate", "beats/min")
            elif "temp" in display_name.lower():
                testcd, testname, default_unit = ("TEMP", "Temperature", "C")
            elif "weight" in display_name.lower():
                testcd, testname, default_unit = ("WEIGHT", "Weight", "kg")
            elif "height" in display_name.lower():
                testcd, testname, default_unit = ("HEIGHT", "Height", "cm")
            else:
                testcd = loinc_code.replace("-", "_") if loinc_code else "VSGEN"
                testname = display_name or "Vital Sign"
                default_unit = val_unit

            target_variable = f"eCRF.VS.{testcd}"
            provenance = (
                f"Mapped LOINC {loinc_code} ('{display_name}') to CDASH VS.{testcd}"
            )
        else:
            target_domain = CDISCDomain.LB
            if loinc_code in lab_loincs:
                testcd, testname, default_unit = lab_loincs[loinc_code]
            elif concept:
                testcd = concept.cdash_testcd
                testname = concept.cdash_test
                default_unit = concept.standard_unit or val_unit
            else:
                testcd = loinc_code.replace("-", "_") if loinc_code else "LABGEN"
                testname = display_name or "Laboratory Test"
                default_unit = val_unit

            target_variable = f"eCRF.LB.{testcd}"
            provenance = (
                f"Mapped LOINC {loinc_code} ('{display_name}') to CDASH LB.{testcd}"
            )

        unit_final = val_unit or default_unit
        current_val, disc = self._compare_with_ecrf(
            target_variable, val_num, unit_final, ecrf_ctx
        )

        candidates.append(
            EhrCandidateRecord(
                study_id=study_id,
                site_id=site_id,
                subject_id=patient_id,
                subject_pseudonym=patient_pseudonym,
                ehr_system_id=ehr_system_id,
                fhir_resource_type="Observation",
                fhir_resource_id=res_id,
                source_timestamp=obs_date,
                target_domain=target_domain,
                target_variable=target_variable,
                cdash_testcd=testcd,
                cdash_test=testname,
                candidate_value=val_num,
                candidate_unit=unit_final,
                current_ecrf_value=current_val,
                discrepancy_type=disc,
                confidence_score=1.0
                if (loinc_code in vital_loincs or loinc_code in lab_loincs)
                else 0.9,
                mapping_provenance=provenance,
                raw_fhir_resource=resource,
            )
        )

    def _normalize_observation_component(
        self,
        comp: dict[str, Any],
        parent_resource: dict[str, Any],
        obs_date: str,
        study_id: str,
        site_id: str,
        patient_id: str,
        patient_pseudonym: str,
        ehr_system_id: str,
        ecrf_ctx: dict[str, Any],
        candidates: list[EhrCandidateRecord],
    ) -> None:
        """Extract multi-component observation item (e.g. Systolic or Diastolic blood pressure component)."""
        res_id = str(parent_resource.get("id", uuid.uuid4()))
        code_codings = comp.get("code", {}).get("coding", [])
        display_name = comp.get("code", {}).get("text", "")
        loinc_code = ""
        for coding in code_codings:
            if "loinc" in coding.get("system", "").lower() or not loinc_code:
                loinc_code = coding.get("code", "")
            if not display_name:
                display_name = coding.get("display", "")

        val_quantity = comp.get("valueQuantity", {})
        val_num = val_quantity.get("value")
        val_unit = val_quantity.get("unit") or val_quantity.get("code")

        if val_num is None:
            return

        if "8480-6" in loinc_code or "systolic" in display_name.lower():
            testcd, testname, default_unit = (
                "SYSBP",
                "Systolic Blood Pressure",
                "mmHg",
            )
        elif "8462-4" in loinc_code or "diastolic" in display_name.lower():
            testcd, testname, default_unit = (
                "DIABP",
                "Diastolic Blood Pressure",
                "mmHg",
            )
        else:
            testcd = loinc_code.replace("-", "_") if loinc_code else "VSCOMP"
            testname = display_name or "Observation Component"
            default_unit = val_unit

        target_variable = f"eCRF.VS.{testcd}"
        unit_final = val_unit or default_unit
        current_val, disc = self._compare_with_ecrf(
            target_variable, val_num, unit_final, ecrf_ctx
        )

        candidates.append(
            EhrCandidateRecord(
                study_id=study_id,
                site_id=site_id,
                subject_id=patient_id,
                subject_pseudonym=patient_pseudonym,
                ehr_system_id=ehr_system_id,
                fhir_resource_type="Observation",
                fhir_resource_id=f"{res_id}-comp-{testcd.lower()}",
                source_timestamp=obs_date,
                target_domain=CDISCDomain.VS,
                target_variable=target_variable,
                cdash_testcd=testcd,
                cdash_test=testname,
                candidate_value=val_num,
                candidate_unit=unit_final,
                current_ecrf_value=current_val,
                discrepancy_type=disc,
                confidence_score=1.0,
                mapping_provenance=f"Mapped Observation.component LOINC {loinc_code} ('{display_name}') to CDASH VS.{testcd}",
                raw_fhir_resource=parent_resource,
            )
        )

    def _normalize_condition(
        self,
        resource: dict[str, Any],
        study_id: str,
        site_id: str,
        patient_id: str,
        patient_pseudonym: str,
        ehr_system_id: str,
        ecrf_ctx: dict[str, Any],
        candidates: list[EhrCandidateRecord],
    ) -> None:
        """Extract CDASH MH (Medical History) domain variables from FHIR Condition."""
        res_id = str(resource.get("id", uuid.uuid4()))
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        condition_code = ""

        for coding in code_codings:
            condition_code = coding.get("code", "")
            if not display_name:
                display_name = coding.get("display", "")

        if not display_name and condition_code:
            display_name = condition_code

        if not display_name:
            display_name = "Unspecified Condition"

        onset_date = (
            resource.get("onsetDateTime")
            or resource.get("recordedDate")
            or resource.get("onsetPeriod", {}).get("start")
            or datetime.now().isoformat()
        )

        clinical_status = (
            resource.get("clinicalStatus", {}).get("coding", [{}])[0].get("code")
            or resource.get("clinicalStatus", {}).get("text")
            or "active"
        )

        target_variable = "eCRF.MH.MHTERM"
        current_val, disc = self._compare_with_ecrf(
            target_variable, display_name, None, ecrf_ctx
        )

        candidates.append(
            EhrCandidateRecord(
                study_id=study_id,
                site_id=site_id,
                subject_id=patient_id,
                subject_pseudonym=patient_pseudonym,
                ehr_system_id=ehr_system_id,
                fhir_resource_type="Condition",
                fhir_resource_id=res_id,
                source_timestamp=onset_date,
                target_domain=CDISCDomain.MH,
                target_variable=target_variable,
                cdash_testcd="MHTERM",
                cdash_test="Medical History Term",
                candidate_value=display_name,
                candidate_unit=f"Status: {clinical_status}",
                current_ecrf_value=current_val,
                discrepancy_type=disc,
                confidence_score=0.95,
                mapping_provenance=f"Mapped FHIR Condition SNOMED/ICD '{condition_code}' to CDASH MH.MHTERM",
                raw_fhir_resource=resource,
            )
        )

    def _normalize_medication(
        self,
        resource: dict[str, Any],
        study_id: str,
        site_id: str,
        patient_id: str,
        patient_pseudonym: str,
        ehr_system_id: str,
        ecrf_ctx: dict[str, Any],
        candidates: list[EhrCandidateRecord],
    ) -> None:
        """Extract CDASH CM (Concomitant Medications) domain variables from Medication resources."""
        res_id = str(resource.get("id", uuid.uuid4()))
        res_type = resource.get("resourceType", "MedicationStatement")

        med_concept = resource.get("medicationCodeableConcept", {})
        code_codings = med_concept.get("coding", [])
        display_name = med_concept.get("text", "")
        med_code = ""

        for coding in code_codings:
            med_code = coding.get("code", "")
            if not display_name:
                display_name = coding.get("display", "")

        if not display_name and resource.get("medicationReference", {}).get("display"):
            display_name = resource["medicationReference"]["display"]

        if not display_name:
            display_name = "Unspecified Medication"

        start_date = (
            resource.get("effectiveDateTime")
            or resource.get("authoredOn")
            or resource.get("dateAsserted")
            or resource.get("effectivePeriod", {}).get("start")
            or datetime.now().isoformat()
        )

        status = resource.get("status", "active")

        # Extract dosage if present
        dosage_list = resource.get("dosageInstruction") or resource.get("dosage") or []
        dose_val = None
        dose_unit = None
        if dosage_list and isinstance(dosage_list, list):
            first_dose = dosage_list[0]
            dose_and_rate = first_dose.get("doseAndRate", [])
            if dose_and_rate and isinstance(dose_and_rate, list):
                dq = dose_and_rate[0].get("doseQuantity", {})
                dose_val = dq.get("value")
                dose_unit = dq.get("unit") or dq.get("code")

        target_variable = "eCRF.CM.CMTRT"
        current_val, disc = self._compare_with_ecrf(
            target_variable, display_name, None, ecrf_ctx
        )

        candidates.append(
            EhrCandidateRecord(
                study_id=study_id,
                site_id=site_id,
                subject_id=patient_id,
                subject_pseudonym=patient_pseudonym,
                ehr_system_id=ehr_system_id,
                fhir_resource_type=res_type,
                fhir_resource_id=res_id,
                source_timestamp=start_date,
                target_domain=CDISCDomain.CM,
                target_variable=target_variable,
                cdash_testcd="CMTRT",
                cdash_test="Concomitant Medication Name",
                candidate_value=display_name,
                candidate_unit=f"{dose_val} {dose_unit}".strip()
                if dose_val
                else f"Status: {status}",
                current_ecrf_value=current_val,
                discrepancy_type=disc,
                confidence_score=0.95,
                mapping_provenance=f"Mapped FHIR {res_type} RxNorm '{med_code}' to CDASH CM.CMTRT",
                raw_fhir_resource=resource,
            )
        )

    def _normalize_procedure(
        self,
        resource: dict[str, Any],
        study_id: str,
        site_id: str,
        patient_id: str,
        patient_pseudonym: str,
        ehr_system_id: str,
        ecrf_ctx: dict[str, Any],
        candidates: list[EhrCandidateRecord],
    ) -> None:
        """Extract CDASH PR (Procedures) domain variables from FHIR Procedure."""
        res_id = str(resource.get("id", uuid.uuid4()))
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        proc_code = ""

        for coding in code_codings:
            proc_code = coding.get("code", "")
            if not display_name:
                display_name = coding.get("display", "")

        if not display_name and proc_code:
            display_name = proc_code

        if not display_name:
            display_name = "Unspecified Procedure"

        proc_date = (
            resource.get("performedDateTime")
            or resource.get("performedPeriod", {}).get("start")
            or resource.get("authoredOn")
            or datetime.now().isoformat()
        )

        status = resource.get("status", "completed")
        concept = lookup_concept_by_code(proc_code)

        testcd = concept.cdash_testcd if concept else "PRTRT"
        testname = concept.cdash_test if concept else "Reported Name of Procedure"
        target_variable = "eCRF.PR.PRTRT"

        current_val, disc = self._compare_with_ecrf(
            target_variable, display_name, None, ecrf_ctx
        )

        candidates.append(
            EhrCandidateRecord(
                study_id=study_id,
                site_id=site_id,
                subject_id=patient_id,
                subject_pseudonym=patient_pseudonym,
                ehr_system_id=ehr_system_id,
                fhir_resource_type="Procedure",
                fhir_resource_id=res_id,
                source_timestamp=proc_date,
                target_domain=CDISCDomain.PR,
                target_variable=target_variable,
                cdash_testcd=testcd,
                cdash_test=testname,
                candidate_value=display_name,
                candidate_unit=f"Status: {status}",
                current_ecrf_value=current_val,
                discrepancy_type=disc,
                confidence_score=0.95,
                mapping_provenance=f"Mapped FHIR Procedure '{proc_code}' ('{display_name}') to CDASH PR.PRTRT",
                raw_fhir_resource=resource,
            )
        )

    def _normalize_encounter(
        self,
        resource: dict[str, Any],
        study_id: str,
        site_id: str,
        patient_id: str,
        patient_pseudonym: str,
        ehr_system_id: str,
        ecrf_ctx: dict[str, Any],
        candidates: list[EhrCandidateRecord],
    ) -> None:
        """Extract CDASH SV (Subject Visits) domain variables from FHIR Encounter."""
        res_id = str(resource.get("id", uuid.uuid4()))
        type_codings = (
            resource.get("type", [{}])[0].get("coding", [])
            if resource.get("type")
            else []
        )
        display_name = ""
        if type_codings:
            display_name = type_codings[0].get("display", "") or type_codings[0].get(
                "code", ""
            )
        if not display_name and resource.get("class"):
            display_name = resource.get("class", {}).get("display", "") or resource.get(
                "class", {}
            ).get("code", "")
        if not display_name:
            display_name = "Clinical Encounter"

        enc_period = resource.get("period", {})
        start_date = enc_period.get("start") or datetime.now().isoformat()

        target_variable = "eCRF.SV.VISIT"
        current_val, disc = self._compare_with_ecrf(
            target_variable, display_name, None, ecrf_ctx
        )

        candidates.append(
            EhrCandidateRecord(
                study_id=study_id,
                site_id=site_id,
                subject_id=patient_id,
                subject_pseudonym=patient_pseudonym,
                ehr_system_id=ehr_system_id,
                fhir_resource_type="Encounter",
                fhir_resource_id=res_id,
                source_timestamp=start_date,
                target_domain=CDISCDomain.SV,
                target_variable=target_variable,
                cdash_testcd="VISIT",
                cdash_test="Visit Name",
                candidate_value=display_name,
                candidate_unit=None,
                current_ecrf_value=current_val,
                discrepancy_type=disc,
                confidence_score=0.95,
                mapping_provenance=f"Mapped FHIR Encounter '{res_id}' to CDASH SV.VISIT",
                raw_fhir_resource=resource,
            )
        )

    async def poll_hospital_ehr(
        self,
        endpoint_config: EhrEndpointConfig,
        private_key_pem: str,
        study_id: str,
        site_id: str = "SITE-01",
        subject_id: str = "PAT-001",
        client: httpx.AsyncClient | None = None,
        current_ecrf_context: dict[str, Any] | None = None,
    ) -> list[EhrCandidateRecord]:
        """Execute automated polling against a configured hospital EHR FHIR server using SMART Backend Authentication.

        Queries Patient, Observation (vital-signs & laboratory), Condition, and MedicationRequest resources.

        Args:
            endpoint_config: Hospital EHR endpoint configuration.
            private_key_pem: RSA private key PEM for asymmetric client assertion.
            study_id: Clinical study protocol identifier.
            site_id: Clinical trial site identifier.
            subject_id: Patient MRN / identifier to poll.
            client: Optional httpx async client.
            current_ecrf_context: Current eCRF field values for comparison.

        Returns:
            List of generated EhrCandidateRecord items.
        """
        # 1. Obtain authenticated Bearer access token
        access_token = await self.smart_auth.get_authenticated_access_token(
            endpoint_config=endpoint_config,
            private_key_pem=private_key_pem,
            client=client,
        )

        headers = {
            "Authorization": f"Bearer {access_token}",
            "Accept": "application/fhir+json, application/json",
        }
        base_url = endpoint_config.fhir_base_url.rstrip("/")

        bundle_entries: list[dict[str, Any]] = []

        async def _query_fhir(http_client: httpx.AsyncClient) -> None:
            # Query Patient
            try:
                p_resp = await http_client.get(
                    f"{base_url}/Patient/{subject_id}", headers=headers, timeout=15.0
                )
                if p_resp.status_code == 200:
                    bundle_entries.append({"resource": p_resp.json()})
            except Exception:
                pass

            # Query Vital Signs Observations
            try:
                vs_resp = await http_client.get(
                    f"{base_url}/Observation?patient={subject_id}&category=vital-signs",
                    headers=headers,
                    timeout=15.0,
                )
                if vs_resp.status_code == 200:
                    data = vs_resp.json()
                    for entry in data.get("entry", []):
                        if "resource" in entry:
                            bundle_entries.append(entry)
            except Exception:
                pass

            # Query Laboratory Observations
            try:
                lb_resp = await http_client.get(
                    f"{base_url}/Observation?patient={subject_id}&category=laboratory",
                    headers=headers,
                    timeout=15.0,
                )
                if lb_resp.status_code == 200:
                    data = lb_resp.json()
                    for entry in data.get("entry", []):
                        if "resource" in entry:
                            bundle_entries.append(entry)
            except Exception:
                pass

            # Query Conditions (Medical History)
            try:
                cond_resp = await http_client.get(
                    f"{base_url}/Condition?patient={subject_id}",
                    headers=headers,
                    timeout=15.0,
                )
                if cond_resp.status_code == 200:
                    data = cond_resp.json()
                    for entry in data.get("entry", []):
                        if "resource" in entry:
                            bundle_entries.append(entry)
            except Exception:
                pass

            # Query MedicationRequests / Statements
            try:
                med_resp = await http_client.get(
                    f"{base_url}/MedicationRequest?patient={subject_id}",
                    headers=headers,
                    timeout=15.0,
                )
                if med_resp.status_code == 200:
                    data = med_resp.json()
                    for entry in data.get("entry", []):
                        if "resource" in entry:
                            bundle_entries.append(entry)
            except Exception:
                pass

        if client is not None:
            await _query_fhir(client)
        else:
            async with httpx.AsyncClient() as default_client:
                await _query_fhir(default_client)

        synthetic_bundle = {
            "resourceType": "Bundle",
            "type": "searchset",
            "entry": bundle_entries,
        }

        return self.normalize_fhir_bundle(
            bundle=synthetic_bundle,
            study_id=study_id,
            site_id=site_id,
            ehr_system_id=endpoint_config.name,
            current_ecrf_context=current_ecrf_context,
        )
