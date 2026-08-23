"""Harmonization and normalization engine for heterogeneous clinical trial streams into the Lakehouse.

Supports:
- Transactional EDC eCRFs
- Subject-Facing ePRO / eCOA Questionnaires
- Central Diagnostic Laboratory feeds (HL7 / CSV)
- Site Local Laboratory feeds
- Connected Sensors & Wearable IoT telemetry
- EHR HL7 FHIR bundles
- Blinded Medical Imaging Adjudication results

Enforces cryptographic SHA-256 hashing, deterministic snapshot sealing,
and strict role-based unblinded data partitioning / masking.

Requirements: PRD-CDR-001, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import hashlib
import json
import uuid
from datetime import datetime

from apps.interop.domain.lakehouse_models import (
    CanonicalClinicalRecord,
    CentralLabIngestItem,
    DataStreamType,
    EDCEcrfIngestItem,
    EHRFHIRIngestItem,
    EPROEcoaIngestItem,
    IOTWearableIngestItem,
    LocalLabIngestItem,
    MedicalImagingIngestItem,
    ObservationDomain,
)


def _compute_sha256(data: str | bytes) -> str:
    """Compute SHA-256 hex digest for string or bytes input."""
    if isinstance(data, str):
        data = data.encode("utf-8")
    return hashlib.sha256(data).hexdigest()


def compute_canonical_record_hash(
    study_id: str,
    subject_id: str,
    stream_type: str,
    domain: str,
    item_code: str,
    observation_datetime: datetime,
    numeric_value: float | None,
    text_value: str | None,
    unit: str | None,
    is_unblinded: bool,
) -> str:
    """Compute a deterministic SHA-256 hash for a canonical record's core clinical fields."""
    iso_dt = (
        observation_datetime.isoformat()
        if isinstance(observation_datetime, datetime)
        else str(observation_datetime)
    )
    hash_payload = {
        "study_id": study_id,
        "subject_id": subject_id,
        "stream_type": stream_type,
        "domain": domain,
        "item_code": item_code,
        "observation_datetime": iso_dt,
        "numeric_value": numeric_value,
        "text_value": text_value,
        "unit": unit,
        "is_unblinded": is_unblinded,
    }
    canonical_json = json.dumps(hash_payload, sort_keys=True, separators=(",", ":"))
    return _compute_sha256(canonical_json)


def compute_snapshot_pool_digest(
    study_id: str,
    version_tag: str,
    milestone: str,
    records: list[CanonicalClinicalRecord],
) -> str:
    """Compute a deterministic SHA-256 digest sealing an entire version-pinned snapshot pool.

    Sorts all record data hashes deterministically to ensure reproducible cryptographic integrity.
    """
    sorted_hashes = sorted([r.data_hash for r in records])
    manifest_payload = {
        "study_id": study_id,
        "version_tag": version_tag,
        "milestone": milestone,
        "record_count": len(records),
        "record_hashes": sorted_hashes,
    }
    canonical_json = json.dumps(manifest_payload, sort_keys=True, separators=(",", ":"))
    return _compute_sha256(canonical_json)


def _infer_domain_from_code(
    code: str, fallback: str = ObservationDomain.OTHER.value
) -> str:
    """Infer CDISC standard domain from variable/field name or code."""
    c = code.upper()
    if any(
        prefix in c
        for prefix in [
            "SYSBP",
            "DIABP",
            "HR",
            "PULSE",
            "TEMP",
            "RESP",
            "WEIGHT",
            "HEIGHT",
            "BMI",
            "VS",
        ]
    ):
        return ObservationDomain.VS.value
    if any(prefix in c for prefix in ["AE", "SAE", "TOXICITY", "ADVERSE"]):
        return ObservationDomain.AE.value
    if any(
        prefix in c
        for prefix in [
            "LAB",
            "HEMOGLOBIN",
            "ALT",
            "AST",
            "BILI",
            "GLUCOSE",
            "CREAT",
            "LB",
        ]
    ):
        return ObservationDomain.LB.value
    if any(
        prefix in c
        for prefix in ["PROMIS", "SF36", "EQ5D", "DIARY", "SURVEY", "QUESTION", "QS"]
    ):
        return ObservationDomain.QS.value
    if any(
        prefix in c
        for prefix in ["LESION", "TUMOR", "RECIST", "DIAMETER", "TR", "TU", "RS"]
    ):
        return ObservationDomain.TR.value
    if any(
        prefix in c for prefix in ["AGE", "SEX", "RACE", "ETHNICITY", "DM", "DEMOG"]
    ):
        return ObservationDomain.DM.value
    if any(prefix in c for prefix in ["DOSE", "DRUG", "EXPOSURE", "EX"]):
        return ObservationDomain.EX.value
    return fallback


# =========================================================================
# Normalizers for Heterogeneous Data Streams
# =========================================================================


def normalize_edc_ecrf(
    study_id: str, item: EDCEcrfIngestItem
) -> CanonicalClinicalRecord:
    """Normalize a transactional EDC eCRF field entry into a CanonicalClinicalRecord."""
    domain = item.domain or _infer_domain_from_code(
        item.field_name, ObservationDomain.OTHER.value
    )

    num_val: float | None = None
    txt_val: str | None = None
    if isinstance(item.value, (int, float)) and not isinstance(item.value, bool):
        num_val = float(item.value)
    else:
        txt_val = str(item.value) if item.value is not None else None

    data_hash = compute_canonical_record_hash(
        study_id=study_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.EDC_ECRF.value,
        domain=domain,
        item_code=item.field_name,
        observation_datetime=item.recorded_at,
        numeric_value=num_val,
        text_value=txt_val,
        unit=item.unit,
        is_unblinded=item.is_unblinded,
    )

    return CanonicalClinicalRecord(
        record_id=str(uuid.uuid4()),
        study_id=study_id,
        site_id=item.site_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.EDC_ECRF,
        domain=domain,
        item_code=item.field_name,
        item_name=f"EDC Field: {item.field_name} ({item.form_oid})",
        observation_datetime=item.recorded_at,
        numeric_value=num_val,
        text_value=txt_val,
        unit=item.unit,
        is_unblinded=item.is_unblinded,
        raw_payload=item.model_dump(mode="json"),
        normalized_attributes={
            "visit_id": item.visit_id,
            "form_oid": item.form_oid,
            "status": item.status,
            "investigator_id": item.investigator_id,
            **item.metadata,
        },
        data_hash=data_hash,
    )


def normalize_epro_ecoa(
    study_id: str, item: EPROEcoaIngestItem
) -> list[CanonicalClinicalRecord]:
    """Normalize an ePRO questionnaire submission into CanonicalClinicalRecords."""
    records: list[CanonicalClinicalRecord] = []
    obs_time = item.completed_at or item.device_timestamp

    # Total score observation if present
    if item.total_score is not None:
        total_code = f"{item.diary_id}_TOTAL_SCORE"
        data_hash = compute_canonical_record_hash(
            study_id=study_id,
            subject_id=item.subject_id,
            stream_type=DataStreamType.EPRO_ECOA.value,
            domain=ObservationDomain.QS.value,
            item_code=total_code,
            observation_datetime=obs_time,
            numeric_value=item.total_score,
            text_value=None,
            unit="points",
            is_unblinded=False,
        )
        records.append(
            CanonicalClinicalRecord(
                record_id=str(uuid.uuid4()),
                study_id=study_id,
                site_id=item.site_id,
                subject_id=item.subject_id,
                stream_type=DataStreamType.EPRO_ECOA,
                domain=ObservationDomain.QS.value,
                item_code=total_code,
                item_name=f"{item.questionnaire_name} Total Score",
                observation_datetime=obs_time,
                numeric_value=item.total_score,
                text_value=None,
                unit="points",
                is_unblinded=False,
                raw_payload=item.model_dump(mode="json"),
                normalized_attributes={
                    "diary_id": item.diary_id,
                    "questionnaire_name": item.questionnaire_name,
                    "compliance_flag": item.compliance_flag,
                    **item.metadata,
                },
                data_hash=data_hash,
            )
        )

    # Individual question items
    for q_id, q_val in item.answers.items():
        num_val: float | None = None
        txt_val: str | None = None
        if isinstance(q_val, (int, float)) and not isinstance(q_val, bool):
            num_val = float(q_val)
        else:
            txt_val = str(q_val)

        item_code = f"{item.diary_id}_{q_id}"
        data_hash = compute_canonical_record_hash(
            study_id=study_id,
            subject_id=item.subject_id,
            stream_type=DataStreamType.EPRO_ECOA.value,
            domain=ObservationDomain.QS.value,
            item_code=item_code,
            observation_datetime=obs_time,
            numeric_value=num_val,
            text_value=txt_val,
            unit=None,
            is_unblinded=False,
        )
        records.append(
            CanonicalClinicalRecord(
                record_id=str(uuid.uuid4()),
                study_id=study_id,
                site_id=item.site_id,
                subject_id=item.subject_id,
                stream_type=DataStreamType.EPRO_ECOA,
                domain=ObservationDomain.QS.value,
                item_code=item_code,
                item_name=f"{item.questionnaire_name} - Item {q_id}",
                observation_datetime=obs_time,
                numeric_value=num_val,
                text_value=txt_val,
                unit=None,
                is_unblinded=False,
                raw_payload=item.model_dump(mode="json"),
                normalized_attributes={
                    "diary_id": item.diary_id,
                    "questionnaire_name": item.questionnaire_name,
                    "question_id": q_id,
                    "compliance_flag": item.compliance_flag,
                    **item.metadata,
                },
                data_hash=data_hash,
            )
        )

    return records


def normalize_central_lab(
    study_id: str, item: CentralLabIngestItem
) -> CanonicalClinicalRecord:
    """Normalize a Central Laboratory feed item into a CanonicalClinicalRecord."""
    data_hash = compute_canonical_record_hash(
        study_id=study_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.CENTRAL_LAB.value,
        domain=ObservationDomain.LB.value,
        item_code=item.test_code,
        observation_datetime=item.collection_timestamp,
        numeric_value=item.numeric_value,
        text_value=item.string_value,
        unit=item.unit,
        is_unblinded=item.is_unblinded,
    )

    return CanonicalClinicalRecord(
        record_id=str(uuid.uuid4()),
        study_id=study_id,
        site_id=item.site_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.CENTRAL_LAB,
        domain=ObservationDomain.LB.value,
        item_code=item.test_code,
        item_name=item.test_name,
        observation_datetime=item.collection_timestamp,
        numeric_value=item.numeric_value,
        text_value=item.string_value,
        unit=item.unit,
        reference_low=item.reference_low,
        reference_high=item.reference_high,
        abnormal_flag=item.abnormal_flag,
        is_unblinded=item.is_unblinded,
        raw_payload=item.model_dump(mode="json"),
        normalized_attributes={
            "specimen_id": item.specimen_id,
            "accession_number": item.accession_number,
            "lab_type": "CENTRAL",
            **item.metadata,
        },
        data_hash=data_hash,
    )


def normalize_local_lab(
    study_id: str, item: LocalLabIngestItem
) -> CanonicalClinicalRecord:
    """Normalize a Site Local Laboratory feed item into a CanonicalClinicalRecord."""
    num_val: float | None = None
    txt_val: str | None = None
    if isinstance(item.value, (int, float)) and not isinstance(item.value, bool):
        num_val = float(item.value)
    else:
        txt_val = str(item.value)

    data_hash = compute_canonical_record_hash(
        study_id=study_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.LOCAL_LAB.value,
        domain=ObservationDomain.LB.value,
        item_code=item.test_code,
        observation_datetime=item.collection_timestamp,
        numeric_value=num_val,
        text_value=txt_val,
        unit=item.unit,
        is_unblinded=item.is_unblinded,
    )

    return CanonicalClinicalRecord(
        record_id=str(uuid.uuid4()),
        study_id=study_id,
        site_id=item.site_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.LOCAL_LAB,
        domain=ObservationDomain.LB.value,
        item_code=item.test_code,
        item_name=item.test_name,
        observation_datetime=item.collection_timestamp,
        numeric_value=num_val,
        text_value=txt_val,
        unit=item.unit,
        reference_low=item.reference_low,
        reference_high=item.reference_high,
        abnormal_flag=item.abnormal_flag,
        is_unblinded=item.is_unblinded,
        raw_payload=item.model_dump(mode="json"),
        normalized_attributes={
            "lab_type": "LOCAL",
            **item.metadata,
        },
        data_hash=data_hash,
    )


def normalize_iot_wearable(
    study_id: str, item: IOTWearableIngestItem
) -> list[CanonicalClinicalRecord]:
    """Normalize connected sensor time-series telemetry into CanonicalClinicalRecords."""
    records: list[CanonicalClinicalRecord] = []
    domain = (
        ObservationDomain.VS.value
        if item.device_type.upper() in ["ACTIGRAPHY", "SPIROMETER", "ECG", "HEART_RATE"]
        else ObservationDomain.LB.value
    )

    for reading in item.telemetry_readings:
        item_code = f"{item.device_type}_{reading.metric_name}".upper()
        data_hash = compute_canonical_record_hash(
            study_id=study_id,
            subject_id=item.subject_id,
            stream_type=DataStreamType.IOT_WEARABLE.value,
            domain=domain,
            item_code=item_code,
            observation_datetime=reading.timestamp,
            numeric_value=reading.value,
            text_value=None,
            unit=reading.unit,
            is_unblinded=False,
        )

        records.append(
            CanonicalClinicalRecord(
                record_id=str(uuid.uuid4()),
                study_id=study_id,
                site_id=item.site_id,
                subject_id=item.subject_id,
                stream_type=DataStreamType.IOT_WEARABLE,
                domain=domain,
                item_code=item_code,
                item_name=f"{item.device_type} {reading.metric_name}",
                observation_datetime=reading.timestamp,
                numeric_value=reading.value,
                text_value=None,
                unit=reading.unit,
                abnormal_flag=reading.flag,
                is_unblinded=False,
                raw_payload=item.model_dump(mode="json"),
                normalized_attributes={
                    "device_id": item.device_id,
                    "device_type": item.device_type,
                    "observation_type": item.observation_type,
                    "sample_rate_hz": item.sample_rate_hz,
                    "calibration_status": item.calibration_status,
                    **item.metadata,
                },
                data_hash=data_hash,
            )
        )

    return records


def normalize_ehr_fhir(
    study_id: str, item: EHRFHIRIngestItem
) -> CanonicalClinicalRecord:
    """Normalize an EHR HL7 FHIR resource bundle entry into a CanonicalClinicalRecord."""
    res_type = item.resource_type.upper()
    if res_type == "OBSERVATION":
        domain = (
            ObservationDomain.LB.value
            if "LAB" in (item.coding_display or "").upper()
            else ObservationDomain.VS.value
        )
    elif res_type == "CONDITION":
        domain = ObservationDomain.AE.value
    elif res_type == "MEDICATIONREQUEST":
        domain = ObservationDomain.EX.value
    elif res_type == "ENCOUNTER":
        domain = ObservationDomain.PR.value
    else:
        domain = ObservationDomain.OTHER.value

    # Extract value if present in FHIR payload
    num_val: float | None = None
    txt_val: str | None = None
    unit_val: str | None = None

    payload = item.resource_payload
    if "valueQuantity" in payload:
        vq = payload["valueQuantity"]
        num_val = float(vq.get("value", 0.0))
        unit_val = vq.get("unit")
    elif "valueString" in payload:
        txt_val = str(payload["valueString"])
    elif "valueCodeableConcept" in payload:
        txt_val = payload["valueCodeableConcept"].get("text") or item.coding_display
    else:
        txt_val = item.coding_display or item.coding_code

    code = item.coding_code or f"FHIR_{item.resource_type}_{item.fhir_id}"
    name = item.coding_display or f"FHIR {item.resource_type}"

    data_hash = compute_canonical_record_hash(
        study_id=study_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.EHR_FHIR.value,
        domain=domain,
        item_code=code,
        observation_datetime=item.effective_datetime,
        numeric_value=num_val,
        text_value=txt_val,
        unit=unit_val,
        is_unblinded=False,
    )

    return CanonicalClinicalRecord(
        record_id=str(uuid.uuid4()),
        study_id=study_id,
        site_id=item.site_id,
        subject_id=item.subject_id,
        stream_type=DataStreamType.EHR_FHIR,
        domain=domain,
        item_code=code,
        item_name=name,
        observation_datetime=item.effective_datetime,
        numeric_value=num_val,
        text_value=txt_val,
        unit=unit_val,
        is_unblinded=False,
        raw_payload=item.model_dump(mode="json"),
        normalized_attributes={
            "resource_type": item.resource_type,
            "fhir_id": item.fhir_id,
            "coding_code": item.coding_code,
            "coding_display": item.coding_display,
            **item.metadata,
        },
        data_hash=data_hash,
    )


def normalize_medical_imaging(
    study_id: str, item: MedicalImagingIngestItem
) -> list[CanonicalClinicalRecord]:
    """Normalize Blinded Medical Imaging / RECIST 1.1 adjudication results into CanonicalClinicalRecords."""
    records: list[CanonicalClinicalRecord] = []

    # 1. Overall Tumor Response Record (RS domain)
    if item.overall_response:
        resp_code = f"RECIST_OVERALL_RESPONSE_{item.timepoint}"
        data_hash = compute_canonical_record_hash(
            study_id=study_id,
            subject_id=item.subject_id,
            stream_type=DataStreamType.MEDICAL_IMAGING.value,
            domain=ObservationDomain.RS.value,
            item_code=resp_code,
            observation_datetime=item.scan_datetime,
            numeric_value=item.sum_of_diameters_mm,
            text_value=item.overall_response,
            unit="mm" if item.sum_of_diameters_mm is not None else None,
            is_unblinded=item.is_unblinded,
        )
        records.append(
            CanonicalClinicalRecord(
                record_id=str(uuid.uuid4()),
                study_id=study_id,
                site_id=item.site_id,
                subject_id=item.subject_id,
                stream_type=DataStreamType.MEDICAL_IMAGING,
                domain=ObservationDomain.RS.value,
                item_code=resp_code,
                item_name=f"RECIST 1.1 Overall Response ({item.timepoint})",
                observation_datetime=item.scan_datetime,
                numeric_value=item.sum_of_diameters_mm,
                text_value=item.overall_response,
                unit="mm" if item.sum_of_diameters_mm is not None else None,
                is_unblinded=item.is_unblinded,
                raw_payload=item.model_dump(mode="json"),
                normalized_attributes={
                    "study_instance_uid": item.study_instance_uid,
                    "series_instance_uid": item.series_instance_uid,
                    "modality": item.modality,
                    "timepoint": item.timepoint,
                    "reader_id": item.reader_id,
                    "adjudication_tier": item.adjudication_tier,
                    "sum_of_diameters_mm": item.sum_of_diameters_mm,
                    **item.metadata,
                },
                data_hash=data_hash,
            )
        )

    # 2. Individual Lesion Evaluations (TR domain)
    for lesion in item.lesions:
        lesion_code = f"LESION_{lesion.lesion_id}_{item.timepoint}"
        data_hash = compute_canonical_record_hash(
            study_id=study_id,
            subject_id=item.subject_id,
            stream_type=DataStreamType.MEDICAL_IMAGING.value,
            domain=ObservationDomain.TR.value,
            item_code=lesion_code,
            observation_datetime=item.scan_datetime,
            numeric_value=lesion.longest_diameter_mm,
            text_value=lesion.response_category or lesion.lesion_type,
            unit="mm",
            is_unblinded=item.is_unblinded,
        )
        records.append(
            CanonicalClinicalRecord(
                record_id=str(uuid.uuid4()),
                study_id=study_id,
                site_id=item.site_id,
                subject_id=item.subject_id,
                stream_type=DataStreamType.MEDICAL_IMAGING,
                domain=ObservationDomain.TR.value,
                item_code=lesion_code,
                item_name=f"Lesion {lesion.lesion_id} ({lesion.site_location})",
                observation_datetime=item.scan_datetime,
                numeric_value=lesion.longest_diameter_mm,
                text_value=lesion.response_category or lesion.lesion_type,
                unit="mm",
                is_unblinded=item.is_unblinded,
                raw_payload=item.model_dump(mode="json"),
                normalized_attributes={
                    "lesion_id": lesion.lesion_id,
                    "lesion_type": lesion.lesion_type,
                    "site_location": lesion.site_location,
                    "short_axis_mm": lesion.short_axis_mm,
                    "reader_id": item.reader_id,
                    "adjudication_tier": item.adjudication_tier,
                    "timepoint": item.timepoint,
                    "study_instance_uid": item.study_instance_uid,
                    **item.metadata,
                },
                data_hash=data_hash,
            )
        )

    return records


# =========================================================================
# Blinding / Masking Enforcement
# =========================================================================


def mask_unblinded_record(record: CanonicalClinicalRecord) -> CanonicalClinicalRecord:
    """Mask sensitive treatment or unblinded variables when queried by a blinded role."""
    if not record.is_unblinded:
        return record

    # Mask unblinded values
    masked_attrs = dict(record.normalized_attributes)
    masked_attrs["unblinded_status"] = "MASKED_BLINDED_ROLE"

    return record.model_copy(
        update={
            "text_value": "[BLINDED]",
            "numeric_value": None,
            "raw_payload": {"status": "[BLINDED_BY_GxP_POLICY]"},
            "normalized_attributes": masked_attrs,
        }
    )


def partition_and_filter_records(
    records: list[CanonicalClinicalRecord],
    is_unblinded_access: bool,
) -> tuple[list[CanonicalClinicalRecord], int]:
    """Partition records and apply field-level masking or record exclusion based on unblinded role access.

    Returns:
        (filtered_records, masked_count)
    """
    if is_unblinded_access:
        return records, 0

    processed: list[CanonicalClinicalRecord] = []
    masked_count = 0
    for rec in records:
        if rec.is_unblinded:
            masked_count += 1
            processed.append(mask_unblinded_record(rec))
        else:
            processed.append(rec)
    return processed, masked_count
