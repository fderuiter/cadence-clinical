import hashlib
import hmac
import os
from typing import Any

from packages.deid.detector import DeidDetector
from packages.deid.models import ComplianceProfile
from packages.deid.transforms import apply_deid_transforms


def deidentify_free_text(
    text: str,
    profile: ComplianceProfile = ComplianceProfile.HIPAA,
    custom_terms: list[str] | None = None,
) -> str:
    detector = DeidDetector()
    results = detector.detect(text, profile=profile, custom_terms=custom_terms)
    redacted_text, _ = apply_deid_transforms(text, results, default_strategy="mask")
    return redacted_text


def pseudonymize_identifier(identifier: str) -> str:
    salt = os.getenv("PSEUDONYMIZATION_SALT", default="secure-clinical-salt-98765")
    return hmac.new(salt.encode(), identifier.encode(), hashlib.sha256).hexdigest()


def strip_pii_from_patient(patient_resource: dict[str, Any]) -> dict[str, Any]:
    custom_terms = []
    names = patient_resource.get("name", [])
    if isinstance(names, list):
        for name in names:
            if isinstance(name, dict):
                givens = name.get("given", [])
                if isinstance(givens, list):
                    for g in givens:
                        if isinstance(g, str) and g:
                            custom_terms.append(g)
                family = name.get("family", "")
                if isinstance(family, str) and family:
                    custom_terms.append(family)
                if family and givens:
                    full_name = " ".join([str(g) for g in givens if g]) + " " + family
                    custom_terms.append(full_name)

    stripped = patient_resource.copy()
    pii_keys = [
        "name",
        "telecom",
        "address",
        "photo",
        "contact",
        "multipleBirthBoolean",
        "multipleBirthInteger",
        "communication",
    ]
    for key in pii_keys:
        stripped.pop(key, None)

    if "text" in stripped and isinstance(stripped["text"], dict):
        div_text = stripped["text"].get("div", "")
        if div_text:
            stripped["text"]["div"] = deidentify_free_text(
                div_text, ComplianceProfile.HIPAA, custom_terms=custom_terms
            )

    if "note" in stripped:
        if isinstance(stripped["note"], list):
            new_notes = []
            for n in stripped["note"]:
                if isinstance(n, dict) and "text" in n:
                    n_copy = n.copy()
                    n_copy["text"] = deidentify_free_text(
                        n["text"], ComplianceProfile.HIPAA, custom_terms=custom_terms
                    )
                    new_notes.append(n_copy)
                elif isinstance(n, str):
                    new_notes.append(
                        deidentify_free_text(
                            n, ComplianceProfile.HIPAA, custom_terms=custom_terms
                        )
                    )
                else:
                    new_notes.append(n)
            stripped["note"] = new_notes
        elif isinstance(stripped["note"], str):
            stripped["note"] = deidentify_free_text(
                stripped["note"], ComplianceProfile.HIPAA, custom_terms=custom_terms
            )

    orig_id = stripped.get("id", "unknown_id")
    stripped["id"] = pseudonymize_identifier(orig_id)

    if "identifier" in stripped:
        new_identifiers = []
        for ident in stripped["identifier"]:
            ident_copy = ident.copy()
            if "value" in ident_copy:
                ident_copy["value"] = pseudonymize_identifier(ident_copy["value"])
            new_identifiers.append(ident_copy)
        stripped["identifier"] = new_identifiers

    return stripped


class FHIRAdapter:
    """FHIR to CDASH eCRF data mapping adapter."""

    def __init__(self, study_id: str) -> None:
        self.study_id = study_id

    def build_ecrf_context(self, parsed_result: dict[str, Any]) -> dict[str, Any]:
        ecrf_context: dict[str, Any] = {}

        mapped_fields = parsed_result.get("mapped_fields", {})
        if "DM.SEX" in mapped_fields:
            ecrf_context["eCRF.DM.SEX"] = mapped_fields["DM.SEX"]

        birth_date_str = mapped_fields.get("DM.BRTHDTC")
        if birth_date_str:
            try:
                birth_year = int(birth_date_str.split("-")[0])
                from datetime import datetime

                current_year = datetime.now().year
                ecrf_context["eCRF.DM.AGE"] = current_year - birth_year
            except (ValueError, IndexError):  # fmt: skip
                pass

        for vs in parsed_result.get("clinical_records", {}).get("vital_signs", []):
            test_cd = vs.get("cdash_testcd")
            val = vs.get("value")
            if test_cd and val is not None:
                ecrf_context[f"eCRF.VS.{test_cd}"] = val

        for lab in parsed_result.get("clinical_records", {}).get("labs", []):
            test_cd = lab.get("cdash_testcd")
            val = lab.get("value")
            if test_cd and val is not None:
                ecrf_context[f"eCRF.LB.{test_cd}"] = val

        conditions = [
            c["display_name"]
            for c in parsed_result.get("clinical_records", {}).get("conditions", [])
            if c.get("display_name")
        ]
        if conditions:
            ecrf_context["eCRF.MH.MHTERM"] = (
                conditions[0] if len(conditions) == 1 else conditions
            )

        medications = [
            m["display_name"]
            for m in parsed_result.get("clinical_records", {}).get("medications", [])
            if m.get("display_name")
        ]
        if medications:
            ecrf_context["eCRF.CM.CMTRT"] = (
                medications[0] if len(medications) == 1 else medications
            )

        return ecrf_context

    def parse_bundle(self, bundle: dict[str, Any]) -> dict[str, Any]:
        entries = bundle.get("entry", [])
        mapped_fields: dict[str, Any] = {}
        de_identified_patient: dict[str, Any] | None = None
        patient_id_raw = "unknown"
        patient_pseudonym = "unknown_pseudonym"

        for entry in entries:
            resource = entry.get("resource", {})
            if resource.get("resourceType") == "Patient":
                patient_id_raw = resource.get("id", "unknown")
                patient_pseudonym = pseudonymize_identifier(patient_id_raw)
                de_identified_patient = strip_pii_from_patient(resource)

                mapped_fields["DM.USUBJID"] = f"{self.study_id}-{patient_pseudonym}"
                mapped_fields["DM.SUBJID"] = patient_pseudonym[:12]

                birth_date = resource.get("birthDate")
                if birth_date:
                    mapped_fields["DM.BRTHDTC"] = birth_date

                gender = resource.get("gender")
                if gender:
                    gender_lower = gender.lower()
                    if gender_lower == "male":
                        mapped_fields["DM.SEX"] = "M"
                    elif gender_lower == "female":
                        mapped_fields["DM.SEX"] = "F"
                    else:
                        mapped_fields["DM.SEX"] = "U"
                break

        if patient_pseudonym == "unknown_pseudonym":
            for entry in entries:
                resource = entry.get("resource", {})
                subject_ref = resource.get("subject", {}).get("reference", "")
                if subject_ref.startswith("Patient/"):
                    patient_id_raw = subject_ref.split("/")[-1]
                    patient_pseudonym = pseudonymize_identifier(patient_id_raw)
                    mapped_fields["DM.USUBJID"] = f"{self.study_id}-{patient_pseudonym}"
                    mapped_fields["DM.SUBJID"] = patient_pseudonym[:12]
                    break

        vitals_list: list[dict[str, Any]] = []
        labs_list: list[dict[str, Any]] = []
        conditions_list: list[dict[str, Any]] = []
        medications_list: list[dict[str, Any]] = []

        for entry in entries:
            resource = entry.get("resource", {})
            res_type = resource.get("resourceType")

            if res_type == "Observation":
                self._parse_observation(resource, vitals_list, labs_list)
            elif res_type == "Condition":
                self._parse_condition(resource, conditions_list)
            elif res_type == "MedicationStatement":
                self._parse_medication_statement(resource, medications_list)

        return {
            "study_id": self.study_id,
            "subject_pseudonym": patient_pseudonym,
            "de_identified_patient": de_identified_patient,
            "mapped_fields": mapped_fields,
            "clinical_records": {
                "vital_signs": vitals_list,
                "labs": labs_list,
                "conditions": conditions_list,
                "medications": medications_list,
            },
        }

    def _parse_observation(
        self,
        resource: dict[str, Any],
        vitals_list: list[dict[str, Any]],
        labs_list: list[dict[str, Any]],
    ) -> None:
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        loinc_code = ""
        for coding in code_codings:
            if "loinc" in coding.get("system", "").lower():
                loinc_code = coding.get("code", "")
                if not display_name:
                    display_name = coding.get("display", "")
                break

        val_quantity = resource.get("valueQuantity", {})
        val_num = val_quantity.get("value")
        val_unit = val_quantity.get("unit")

        obs_date = resource.get("effectiveDateTime") or resource.get("issued")

        if not loinc_code and code_codings:
            loinc_code = code_codings[0].get("code", "")
            if not display_name:
                display_name = code_codings[0].get("display", "")

        category_codings = resource.get("category", [])
        is_vital = False
        for cat in category_codings:
            for cat_coding in cat.get("coding", []):
                if cat_coding.get("code", "").lower() == "vital-signs":
                    is_vital = True
                    break

        vital_loincs = [
            "8480-6",
            "8462-4",
            "8867-4",
            "8310-5",
            "29463-7",  # deid: ignore
            "8302-2",
        ]
        if loinc_code in vital_loincs or "vital" in display_name.lower():
            is_vital = True

        record = {
            "loinc_code": loinc_code,
            "display_name": display_name,
            "value": val_num,
            "unit": val_unit,
            "date": obs_date,
        }

        if is_vital:
            if "8480-6" in loinc_code or "systolic" in display_name.lower():
                record["cdash_testcd"] = "SYSBP"
                record["cdash_test"] = "Systolic Blood Pressure"
            elif "8462-4" in loinc_code or "diastolic" in display_name.lower():
                record["cdash_testcd"] = "DIABP"
                record["cdash_test"] = "Diastolic Blood Pressure"
            elif (
                "8867-4" in loinc_code
                or "heart" in display_name.lower()
                or "pulse" in display_name.lower()
            ):
                record["cdash_testcd"] = "PULSE"
                record["cdash_test"] = "Pulse Rate"
            elif "8310-5" in loinc_code or "temp" in display_name.lower():
                record["cdash_testcd"] = "TEMP"
                record["cdash_test"] = "Temperature"
            elif (
                "29463-7" in loinc_code or "weight" in display_name.lower()
            ):  # deid: ignore
                record["cdash_testcd"] = "WEIGHT"
                record["cdash_test"] = "Weight"
            elif "8302-2" in loinc_code or "height" in display_name.lower():
                record["cdash_testcd"] = "HEIGHT"
                record["cdash_test"] = "Height"
            vitals_list.append(record)
        else:
            if "2339-0" in loinc_code or "glucose" in display_name.lower():
                record["cdash_testcd"] = "GLUC"
                record["cdash_test"] = "Glucose"
            labs_list.append(record)

    def _parse_condition(
        self, resource: dict[str, Any], conditions_list: list[dict[str, Any]]
    ) -> None:
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        condition_code = ""

        if code_codings:
            condition_code = code_codings[0].get("code", "")
            if not display_name:
                display_name = code_codings[0].get("display", "")

        onset_date = (
            resource.get("onsetDateTime")
            or resource.get("recordedDate")
            or resource.get("onsetPeriod", {}).get("start")
        )

        conditions_list.append(
            {
                "condition_code": condition_code,
                "display_name": display_name,
                "onset_date": onset_date,
                "clinical_status": resource.get("clinicalStatus", {})
                .get("coding", [{}])[0]
                .get("code", "active"),
                "cdash_variable": "MH.MHTERM",
            }
        )

    def _parse_medication_statement(
        self, resource: dict[str, Any], medications_list: list[dict[str, Any]]
    ) -> None:
        med_concept = resource.get("medicationCodeableConcept", {})
        code_codings = med_concept.get("coding", [])
        display_name = med_concept.get("text", "")
        med_code = ""

        if code_codings:
            med_code = code_codings[0].get("code", "")
            if not display_name:
                display_name = code_codings[0].get("display", "")

        start_date = (
            resource.get("effectiveDateTime")
            or resource.get("dateAsserted")
            or resource.get("effectivePeriod", {}).get("start")
        )

        medications_list.append(
            {
                "medication_code": med_code,
                "display_name": display_name,
                "start_date": start_date,
                "status": resource.get("status", "unknown"),
                "cdash_variable": "CM.CMTRT",
            }
        )
