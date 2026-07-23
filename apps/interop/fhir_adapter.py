import hashlib
import hmac
import os
from typing import Any, Dict, List, Optional


def pseudonymize_identifier(identifier: str) -> str:
    """
    Generate an irreversible HMAC-SHA256 pseudonym for patient direct identifiers.

    Args:
        identifier (str): The patient's direct identifier (e.g., SSN, EHR ID, MRN).

    Returns:
        str: Cryptographic hash of the identifier.
    """
    salt = os.getenv("PSEUDONYMIZATION_SALT", "secure-clinical-salt-98765")
    return hmac.new(salt.encode(), identifier.encode(), hashlib.sha256).hexdigest()


def strip_pii_from_patient(patient_resource: Dict[str, Any]) -> Dict[str, Any]:
    """
    Strip direct identifiers (PII) from a FHIR Patient resource.

    Args:
        patient_resource (Dict[str, Any]): Raw FHIR Patient resource.

    Returns:
        Dict[str, Any]: De-identified FHIR Patient resource.
    """
    stripped = patient_resource.copy()
    # Direct PII elements that must be completely removed
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

    # Pseudonymize patient ID
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
    """
    FHIR to CDASH eCRF data mapping adapter.
    Parses Patient, Observation, Condition, and MedicationStatement resources.
    """

    def __init__(self, study_id: str) -> None:
        self.study_id = study_id

    def parse_bundle(self, bundle: Dict[str, Any]) -> Dict[str, Any]:
        """
        Ingest a FHIR Bundle payload, pseudonymize patients, strip PII, and
        map properties to CDASH eCRF target fields.

        Args:
            bundle (Dict[str, Any]): A standard FHIR transaction/collection Bundle.

        Returns:
            Dict[str, Any]: A dictionary containing de-identified patient data
                            and a map of pre-filled CDASH fields.
        """
        entries = bundle.get("entry", [])
        mapped_fields: Dict[str, Any] = {}
        de_identified_patient: Optional[Dict[str, Any]] = None
        patient_id_raw = "unknown"
        patient_pseudonym = "unknown_pseudonym"

        # 1. Process Patient Resource first to get pseudonym
        for entry in entries:
            resource = entry.get("resource", {})
            if resource.get("resourceType") == "Patient":
                patient_id_raw = resource.get("id", "unknown")
                patient_pseudonym = pseudonymize_identifier(patient_id_raw)
                de_identified_patient = strip_pii_from_patient(resource)

                # Map Demographic (DM) fields
                mapped_fields["DM.USUBJID"] = f"{self.study_id}-{patient_pseudonym}"
                mapped_fields["DM.SUBJID"] = patient_pseudonym[:12]

                birth_date = resource.get("birthDate")
                if birth_date:
                    mapped_fields["DM.BRTHDTC"] = birth_date

                gender = resource.get("gender")
                if gender:
                    # Map FHIR gender to CDISC sex
                    gender_lower = gender.lower()
                    if gender_lower == "male":
                        mapped_fields["DM.SEX"] = "M"
                    elif gender_lower == "female":
                        mapped_fields["DM.SEX"] = "F"
                    else:
                        mapped_fields["DM.SEX"] = "U"
                break

        # If no Patient resource in Bundle, try to find patient reference in other resources
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

        vitals_list: List[Dict[str, Any]] = []
        labs_list: List[Dict[str, Any]] = []
        conditions_list: List[Dict[str, Any]] = []
        medications_list: List[Dict[str, Any]] = []

        # 2. Process other resources (Observation, Condition, MedicationStatement)
        for entry in entries:
            resource = entry.get("resource", {})
            res_type = resource.get("resourceType")

            if res_type == "Observation":
                self._parse_observation(resource, vitals_list, labs_list)
            elif res_type == "Condition":
                self._parse_condition(resource, conditions_list)
            elif res_type == "MedicationStatement":
                self._parse_medication_statement(resource, medications_list)

        # Structure mapped output
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
        resource: Dict[str, Any],
        vitals_list: List[Dict[str, Any]],
        labs_list: List[Dict[str, Any]],
    ) -> None:
        """Helper to parse a FHIR Observation resource."""
        code_codings = resource.get("code", {}).get("coding", [])
        display_name = resource.get("code", {}).get("text", "")
        loinc_code = ""
        for coding in code_codings:
            if "loinc" in coding.get("system", "").lower():
                loinc_code = coding.get("code", "")
                if not display_name:
                    display_name = coding.get("display", "")
                break

        # Try to extract value
        val_quantity = resource.get("valueQuantity", {})
        val_num = val_quantity.get("value")
        val_unit = val_quantity.get("unit")

        obs_date = resource.get("effectiveDateTime") or resource.get("issued")

        if not loinc_code and code_codings:
            loinc_code = code_codings[0].get("code", "")
            if not display_name:
                display_name = code_codings[0].get("display", "")

        # Categorize into Vital Signs or Labs
        category_codings = resource.get("category", [])
        is_vital = False
        for cat in category_codings:
            for cat_coding in cat.get("coding", []):
                if cat_coding.get("code", "").lower() == "vital-signs":
                    is_vital = True
                    break

        # Also fallback detection via loinc codes
        vital_loincs = ["8480-6", "8462-4", "8867-4", "8310-5", "29463-7", "8302-2"]
        if loinc_code in vital_loincs or "vital" in display_name.lower():
            is_vital = True

        record = {
            "loinc_code": loinc_code,
            "display_name": display_name,
            "value": val_num,
            "unit": val_unit,
            "date": obs_date,
        }

        # Map to specific CDASH-like variables for vital signs
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
            elif "29463-7" in loinc_code or "weight" in display_name.lower():
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
        self, resource: Dict[str, Any], conditions_list: List[Dict[str, Any]]
    ) -> None:
        """Helper to parse a FHIR Condition resource."""
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
        self, resource: Dict[str, Any], medications_list: List[Dict[str, Any]]
    ) -> None:
        """Helper to parse a FHIR MedicationStatement resource."""
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
