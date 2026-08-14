"""Central and Local Laboratory Batch Ingestion Service.

Provides multi-format parsing (delimited CSV/TSV, HL7 v2.x ORU^R01, and HL7 FHIR
Observation JSON), UCUM unit conversion, demographic-stratified normal reference range
matching and evaluation, automated discrepancy query creation, and critical SAE alerts.

Requirements:
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-MDR-001 (Metadata Repository & Catalog Normalization)
- PRD-QRY-001 (Automated Discrepancy Query Escalation)
- Trace-1 (Audit Trail & 21 CFR Part 11)
- Trace-15 (Laboratory Data Flow & Reference Ranges)
"""

import csv
import io
import json
import logging
import re
import uuid
from collections.abc import Sequence
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field
from sqlalchemy import select

from apps.execution.database.context import audit_context
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    LabTestMaster,
    SubjectConsent,
)
from apps.execution.demographics import get_safe_demographics
from apps.execution.lab_range_cache import get_active_lab_ranges, lab_range_cache
from apps.execution.lab_ranges import (
    convert_lab_unit,
    evaluate_lab_value,
    select_reference_range,
)
from apps.execution.notification_events import dispatch_critical_lab_alerts
from apps.execution.ucum import get_normalized_representation

logger = logging.getLogger(__name__)


class LabIngestFormat(StrEnum):
    """Supported laboratory batch ingestion formats."""

    CSV = "csv"
    HL7 = "hl7"
    FHIR = "fhir"


class RawLabRecord(BaseModel):
    """Intermediate normalized representation of an incoming lab observation record."""

    subject_id: str
    study_id: str | None = None
    site_id: str | None = None
    visit_id: str | None = None
    test_code: str
    test_name: str | None = None
    value: float | None = None
    value_string: str | None = None
    unit: str | None = None
    observation_date: datetime | None = None
    lab_source: str = "CENTRAL"
    reference_range_low: float | None = None
    reference_range_high: float | None = None
    critical_low: float | None = None
    critical_high: float | None = None
    raw_abnormal_flag: str | None = None
    additional_metadata: dict[str, Any] = Field(default_factory=dict)


class LabBatchIngestRequest(BaseModel):
    """Transport schema for laboratory batch ingestion requests."""

    format: LabIngestFormat = LabIngestFormat.CSV
    payload: str | None = None
    resource: dict[str, Any] | list[dict[str, Any]] | None = None
    study_id: str | None = None
    site_id: str | None = None
    lab_source: str = "CENTRAL"
    reason_for_change: str = "Batch laboratory data ingestion"


class LabBatchIngestResult(BaseModel):
    """Summary of batch ingestion processing."""

    batch_id: str
    study_id: str | None = None
    format: str
    status: str  # "COMPLETED", "COMPLETED_WITH_ERRORS", "FAILED"
    total_processed: int = 0
    ingested_count: int = 0
    out_of_range_count: int = 0
    critical_alerts: int = 0
    queries_raised: int = 0
    errors: list[dict[str, Any]] = Field(default_factory=list)
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))


# In-memory store for tracking batch ingestion jobs
_BATCH_STORE: dict[str, LabBatchIngestResult] = {}


def _parse_iso_or_clinical_date(date_str: str | None) -> datetime | None:
    """Parse various ISO and clinical datetime string representations safely."""
    if not date_str or not str(date_str).strip():
        return None

    cleaned = str(date_str).strip()

    # Try standard ISO parsing
    try:
        dt = datetime.fromisoformat(cleaned.replace("Z", "+00:00"))
        return dt.replace(tzinfo=None)
    except Exception:
        pass

    # Common clinical date formats
    formats = [
        "%Y-%m-%d %H:%M:%S",
        "%Y-%m-%d %H:%M",
        "%Y-%m-%d",
        "%Y/%m/%d %H:%M:%S",
        "%Y/%m/%d",
        "%d-%m-%Y %H:%M:%S",
        "%d-%m-%Y",
        "%d/%m/%Y %H:%M:%S",
        "%d/%m/%Y",
        "%m/%d/%Y %H:%M:%S",
        "%m/%d/%Y",
        "%Y%m%d%H%M%S",
        "%Y%m%d%H%M",
        "%Y%m%d",
    ]

    for fmt in formats:
        try:
            return datetime.strptime(cleaned, fmt)
        except Exception:
            continue

    return None


def _parse_numeric_value(val: Any) -> tuple[float | None, str | None]:
    """Parse raw value into numeric float or string representation."""
    if val is None:
        return None, None

    if isinstance(val, (int, float)):
        return float(val), str(val)

    s = str(val).strip()
    if not s:
        return None, None

    # Check for direct numeric float
    try:
        return float(s), s
    except ValueError:
        pass

    # Handle prefixed inequality measurements like "< 0.05", "> 100", "<= 10"
    m = re.match(r"^([<>]=?)\s*([0-9]+(?:\.[0-9]+)?)$", s)
    if m:
        try:
            num = float(m.group(2))
            return num, s
        except ValueError:
            pass

    return None, s


def _parse_reference_range_bounds(
    range_str: str | None,
) -> tuple[float | None, float | None]:
    """Extract low and high numeric bounds from clinical range strings (e.g. '4.0-11.0', '4.0 - 11.0', '10 to 40', '< 200')."""
    if not range_str or not str(range_str).strip():
        return None, None

    s = str(range_str).strip()

    # Pattern: low - high or low to high
    m_between = re.search(
        r"([0-9]+(?:\.[0-9]+)?)\s*(?:-|–|—|to)\s*([0-9]+(?:\.[0-9]+)?)",
        s,
        re.IGNORECASE,
    )
    if m_between:
        try:
            return float(m_between.group(1)), float(m_between.group(2))
        except ValueError:
            pass

    # Pattern: < high (upper bound only)
    m_less = re.search(r"<\s*([0-9]+(?:\.[0-9]+)?)", s)
    if m_less:
        try:
            return None, float(m_less.group(1))
        except ValueError:
            pass

    # Pattern: > low (lower bound only)
    m_greater = re.search(r">\s*([0-9]+(?:\.[0-9]+)?)", s)
    if m_greater:
        try:
            return float(m_greater.group(1)), None
        except ValueError:
            pass

    return None, None


def parse_csv_payload(
    payload: str | bytes,
    default_study_id: str | None = None,
    default_site_id: str | None = None,
    default_source: str = "CENTRAL",
) -> tuple[list[RawLabRecord], list[dict[str, Any]]]:
    """Parse delimited CSV/TSV laboratory observation data into RawLabRecord items.

    Supports automatic delimiter sniffing (comma, tab, semicolon, pipe) and case-insensitive
    flexible column header matching.

    Args:
        payload: String or bytes containing delimited data.
        default_study_id: Optional fallback study identifier.
        default_site_id: Optional fallback site identifier.
        default_source: Lab source ('CENTRAL' or 'LOCAL').

    Returns:
        tuple containing parsed records and list of parsing errors.
    """
    if isinstance(payload, bytes):
        payload_str = payload.decode("utf-8-sig", errors="replace")
    else:
        payload_str = payload

    records: list[RawLabRecord] = []
    errors: list[dict[str, Any]] = []

    if not payload_str.strip():
        return records, errors

    # Detect delimiter
    sample = payload_str[:2048]
    delimiter = ","
    for d in ["\t", ";", "|", ","]:
        if d in sample:
            delimiter = d
            break

    f = io.StringIO(payload_str.strip())
    reader = csv.reader(f, delimiter=delimiter)

    try:
        raw_headers = next(reader)
    except StopIteration:
        return records, errors

    # Header normalization mapping
    def norm_col(h: str) -> str:
        return re.sub(r"[^a-z0-9]", "", h.lower().strip())

    header_indices = {
        norm_col(h): idx for idx, h in enumerate(raw_headers) if h.strip()
    }

    def get_field(
        row: list[str], aliases: Sequence[str], default: str | None = None
    ) -> str | None:
        for alias in aliases:
            cleaned_alias = norm_col(alias)
            if cleaned_alias in header_indices:
                idx = header_indices[cleaned_alias]
                if idx < len(row):
                    val = row[idx].strip()
                    if val:
                        return val
        return default

    # Aliases
    subj_aliases = [
        "subjectid",
        "subject",
        "usubjid",
        "patientid",
        "subjid",
        "ptid",
        "patid",
        "id",
    ]
    study_aliases = ["studyid", "study", "protocol", "protocolid"]
    site_aliases = ["siteid", "site", "center", "sitenumber", "sitecode"]
    visit_aliases = ["visitid", "visit", "folder", "visitname", "timepoint", "visitnum"]
    code_aliases = [
        "testcode",
        "test",
        "paramcd",
        "lbtestcd",
        "code",
        "analytecode",
        "labtestcode",
    ]
    name_aliases = [
        "testname",
        "testdescription",
        "lbtest",
        "description",
        "name",
        "analyte",
    ]
    val_aliases = [
        "value",
        "result",
        "val",
        "lbstresn",
        "lborres",
        "numericvalue",
        "measurement",
        "res",
    ]
    val_str_aliases = [
        "valuestring",
        "textvalue",
        "resulttext",
        "comment",
        "charvalue",
        "stringvalue",
    ]
    unit_aliases = ["unit", "units", "lbstresu", "lborresu", "uom"]
    date_aliases = [
        "collectiondate",
        "observationdate",
        "lbdtc",
        "datetime",
        "date",
        "collectiontime",
        "sampleddate",
        "sampledate",
        "specimendate",
        "time",
    ]
    source_aliases = ["labsource", "source", "labtype"]
    ref_low_aliases = [
        "reflow",
        "referencerangelow",
        "rangelow",
        "lowbound",
        "normallow",
        "refrangelow",
        "low",
    ]
    ref_high_aliases = [
        "refhigh",
        "referencerangehigh",
        "rangehigh",
        "highbound",
        "normalhigh",
        "refrangehigh",
        "high",
    ]
    crit_low_aliases = ["criticallow", "critlow", "paniclow"]
    crit_high_aliases = ["criticalhigh", "crithigh", "panichigh"]
    flag_aliases = [
        "abnormalflag",
        "flag",
        "indicator",
        "abnormal",
        "severity",
        "alert",
    ]

    for row_idx, row in enumerate(reader, start=2):
        if not row or not any(field.strip() for field in row):
            continue

        subj_id = get_field(row, subj_aliases)
        test_code = get_field(row, code_aliases)

        if not subj_id:
            errors.append(
                {
                    "row": row_idx,
                    "error": "Missing required Subject ID column/value",
                    "raw": row,
                }
            )
            continue

        if not test_code:
            errors.append(
                {
                    "row": row_idx,
                    "error": "Missing required Test Code column/value",
                    "raw": row,
                }
            )
            continue

        raw_val_str = get_field(row, val_aliases)
        explicit_val_str = get_field(row, val_str_aliases)

        val_num, val_text = _parse_numeric_value(raw_val_str)
        if explicit_val_str:
            val_text = explicit_val_str

        if val_num is None and val_text is None:
            errors.append(
                {
                    "row": row_idx,
                    "error": "Record contains neither numeric value nor text result",
                    "raw": row,
                }
            )
            continue

        # Parse reference range bounds
        ref_low_str = get_field(row, ref_low_aliases)
        ref_high_str = get_field(row, ref_high_aliases)
        crit_low_str = get_field(row, crit_low_aliases)
        crit_high_str = get_field(row, crit_high_aliases)

        ref_low, _ = _parse_numeric_value(ref_low_str)
        ref_high, _ = _parse_numeric_value(ref_high_str)
        crit_low, _ = _parse_numeric_value(crit_low_str)
        crit_high, _ = _parse_numeric_value(crit_high_str)

        obs_date = _parse_iso_or_clinical_date(get_field(row, date_aliases))
        test_name = get_field(row, name_aliases, default=test_code)
        unit = get_field(row, unit_aliases)
        site_id = get_field(row, site_aliases, default=default_site_id)
        study_id = get_field(row, study_aliases, default=default_study_id)
        visit_id = get_field(row, visit_aliases)
        lab_source = (
            get_field(row, source_aliases, default=default_source) or default_source
        )
        flag = get_field(row, flag_aliases)

        rec = RawLabRecord(
            subject_id=subj_id,
            study_id=study_id,
            site_id=site_id,
            visit_id=visit_id,
            test_code=test_code,
            test_name=test_name,
            value=val_num,
            value_string=val_text,
            unit=unit,
            observation_date=obs_date,
            lab_source=lab_source.upper(),
            reference_range_low=ref_low,
            reference_range_high=ref_high,
            critical_low=crit_low,
            critical_high=crit_high,
            raw_abnormal_flag=flag.upper() if flag else None,
        )
        records.append(rec)

    return records, errors


def parse_hl7_v2_payload(
    payload: str | bytes,
    default_study_id: str | None = None,
    default_site_id: str | None = None,
    default_source: str = "CENTRAL",
) -> tuple[list[RawLabRecord], list[dict[str, Any]]]:
    """Parse HL7 v2.x (ORU^R01) observation messages into RawLabRecord items.

    Extracts MSH, PID, PV1, OBR, and OBX segments, preserving observation dates,
    reference ranges, units, and abnormal flags.

    Args:
        payload: String or bytes containing HL7 v2 message text.
        default_study_id: Optional fallback study identifier.
        default_site_id: Optional fallback site identifier.
        default_source: Lab source ('CENTRAL' or 'LOCAL').

    Returns:
        tuple containing parsed records and list of parsing errors.
    """
    if isinstance(payload, bytes):
        payload_str = payload.decode("utf-8", errors="replace")
    else:
        payload_str = payload

    records: list[RawLabRecord] = []
    errors: list[dict[str, Any]] = []

    if not payload_str.strip():
        return records, errors

    # Split lines by carriage returns or newlines
    lines = [line.strip() for line in re.split(r"[\r\n]+", payload_str) if line.strip()]

    current_subject_id: str | None = None
    current_study_id: str | None = default_study_id
    current_site_id: str | None = default_site_id
    current_visit_id: str | None = None
    current_order_date: datetime | None = None
    current_source: str = default_source

    for line_idx, line in enumerate(lines, start=1):
        fields = line.split("|")
        seg_type = fields[0].upper()

        if seg_type == "MSH":
            # MSH-4: Sending Facility (maps to site or source)
            if len(fields) > 4 and fields[4].strip():
                sending_fac = fields[4].strip()
                if not current_site_id:
                    current_site_id = sending_fac

        elif seg_type == "PID":
            # PID-3: Patient Identifier List (e.g. SUBJ-101 or SUBJ-101^^^MRN)
            if len(fields) > 3 and fields[3].strip():
                pid_components = fields[3].split("^")
                current_subject_id = pid_components[0].strip()
            else:
                errors.append(
                    {
                        "line": line_idx,
                        "error": "PID segment missing Patient Identifier in PID-3",
                        "raw": line,
                    }
                )

        elif seg_type == "PV1":
            # PV1-19: Visit Number
            if len(fields) > 19 and fields[19].strip():
                current_visit_id = fields[19].strip()

        elif seg_type == "OBR":
            # OBR-7: Observation Date/Time
            if len(fields) > 7 and fields[7].strip():
                current_order_date = _parse_iso_or_clinical_date(fields[7].strip())

            # OBR-20: Filler Field (can contain site / source metadata)
            if len(fields) > 20 and fields[20].strip():
                obr_meta = fields[20].strip()
                if obr_meta.upper() in ("CENTRAL", "LOCAL"):
                    current_source = obr_meta.upper()

        elif seg_type == "OBX":
            if not current_subject_id:
                errors.append(
                    {
                        "line": line_idx,
                        "error": "OBX segment preceded by no valid PID segment",
                        "raw": line,
                    }
                )
                continue

            # OBX-2: Value Type (NM, ST, TX, CE, CWE)
            val_type = fields[2].strip().upper() if len(fields) > 2 else "ST"

            # OBX-3: Observation Identifier (code^name^system)
            obs_ident = fields[3].strip() if len(fields) > 3 else ""
            ident_parts = obs_ident.split("^")
            test_code = ident_parts[0].strip() if ident_parts else ""
            test_name = (
                ident_parts[1].strip()
                if len(ident_parts) > 1 and ident_parts[1].strip()
                else test_code
            )

            if not test_code:
                errors.append(
                    {
                        "line": line_idx,
                        "error": "OBX segment missing test code in OBX-3",
                        "raw": line,
                    }
                )
                continue

            # OBX-5: Observation Value
            raw_val_str = fields[5].strip() if len(fields) > 5 else ""
            val_num, val_text = _parse_numeric_value(raw_val_str)
            if val_type == "NM" and val_num is None:
                # Value was declared numeric but could not parse
                val_text = raw_val_str

            # OBX-6: Units (e.g. 10^9/L^UCUM or mg/dL)
            raw_unit = fields[6].strip() if len(fields) > 6 else None
            unit = raw_unit.split("^")[0].strip() if raw_unit else None

            # OBX-7: Reference Range string (e.g. 4.0-11.0)
            ref_range_str = fields[7].strip() if len(fields) > 7 else None
            ref_low, ref_high = _parse_reference_range_bounds(ref_range_str)

            # OBX-8: Abnormal Flags (L, H, LL, HH, N, A, CRIT, PANIC)
            abnormal_flag = (
                fields[8].strip().upper()
                if len(fields) > 8 and fields[8].strip()
                else None
            )

            # OBX-14: Date/Time of the Observation
            obs_date = current_order_date
            if len(fields) > 14 and fields[14].strip():
                parsed_obx_date = _parse_iso_or_clinical_date(fields[14].strip())
                if parsed_obx_date:
                    obs_date = parsed_obx_date

            rec = RawLabRecord(
                subject_id=current_subject_id,
                study_id=current_study_id,
                site_id=current_site_id,
                visit_id=current_visit_id,
                test_code=test_code,
                test_name=test_name,
                value=val_num,
                value_string=val_text,
                unit=unit,
                observation_date=obs_date,
                lab_source=current_source.upper(),
                reference_range_low=ref_low,
                reference_range_high=ref_high,
                raw_abnormal_flag=abnormal_flag,
            )
            records.append(rec)

    return records, errors


def parse_fhir_payload(
    payload: str | bytes | dict | list,
    default_study_id: str | None = None,
    default_site_id: str | None = None,
    default_source: str = "CENTRAL",
) -> tuple[list[RawLabRecord], list[dict[str, Any]]]:
    """Parse HL7 FHIR Observation resource JSON into RawLabRecord items.

    Accepts a single FHIR Observation resource, a FHIR Bundle containing Observations,
    or a JSON array of Observation resources.

    Args:
        payload: JSON string, bytes, dict, or list of FHIR resources.
        default_study_id: Optional fallback study identifier.
        default_site_id: Optional fallback site identifier.
        default_source: Lab source ('CENTRAL' or 'LOCAL').

    Returns:
        tuple containing parsed records and list of parsing errors.
    """
    records: list[RawLabRecord] = []
    errors: list[dict[str, Any]] = []

    if isinstance(payload, bytes):
        payload_str = payload.decode("utf-8", errors="replace")
        try:
            data = json.loads(payload_str)
        except Exception as e:
            errors.append({"error": f"Invalid JSON payload: {e}"})
            return records, errors
    elif isinstance(payload, str):
        try:
            data = json.loads(payload)
        except Exception as e:
            errors.append({"error": f"Invalid JSON payload: {e}"})
            return records, errors
    else:
        data = payload

    if not data:
        return records, errors

    # Normalize into list of observation resource dicts
    observation_resources: list[dict[str, Any]] = []

    if isinstance(data, dict):
        res_type = data.get("resourceType")
        if res_type == "Bundle":
            entries = data.get("entry", [])
            for entry in entries:
                res = entry.get("resource", {})
                if res.get("resourceType") == "Observation":
                    observation_resources.append(res)
        elif res_type == "Observation":
            observation_resources.append(data)
        else:
            errors.append({"error": f"Unsupported FHIR resourceType: {res_type}"})
            return records, errors
    elif isinstance(data, list):
        for item in data:
            if isinstance(item, dict) and item.get("resourceType") == "Observation":
                observation_resources.append(item)
            elif isinstance(item, dict):
                # Loose observation dictionary
                observation_resources.append(item)

    for idx, obs_json in enumerate(observation_resources):
        # 1. Subject extraction
        subj_id = None
        subj_obj = obs_json.get("subject") or obs_json.get("patient")
        if isinstance(subj_obj, dict):
            ref = subj_obj.get("reference", "")
            if ref.startswith("Patient/"):
                subj_id = ref.replace("Patient/", "").strip()
            elif ref:
                subj_id = ref.strip()
            elif "identifier" in subj_obj and isinstance(subj_obj["identifier"], dict):
                subj_id = str(subj_obj["identifier"].get("value", "")).strip()
            elif "display" in subj_obj:
                subj_id = str(subj_obj.get("display", "")).strip()
        elif isinstance(subj_obj, str):
            subj_id = subj_obj.replace("Patient/", "").strip()

        if not subj_id:
            errors.append(
                {
                    "index": idx,
                    "error": "FHIR Observation resource missing valid subject reference",
                    "resource": obs_json,
                }
            )
            continue

        # 2. Test code & name extraction
        test_code = None
        test_name = None
        code_obj = obs_json.get("code")
        if isinstance(code_obj, dict):
            codings = code_obj.get("coding", [])
            if isinstance(codings, list) and len(codings) > 0:
                first_coding = codings[0]
                test_code = first_coding.get("code")
                test_name = first_coding.get("display") or code_obj.get("text")
            if not test_code:
                test_code = code_obj.get("text")
                test_name = code_obj.get("text")
        elif isinstance(code_obj, str):
            test_code = code_obj
            test_name = code_obj

        if not test_code:
            errors.append(
                {
                    "index": idx,
                    "error": "FHIR Observation resource missing code.coding[0].code",
                    "resource": obs_json,
                }
            )
            continue

        test_name = test_name or test_code

        # 3. Value extraction
        val_num = None
        val_text = None
        unit = None

        if "valueQuantity" in obs_json and isinstance(obs_json["valueQuantity"], dict):
            vq = obs_json["valueQuantity"]
            val_num, val_text = _parse_numeric_value(vq.get("value"))
            unit = vq.get("unit") or vq.get("code")
        elif "valueString" in obs_json:
            val_text = str(obs_json["valueString"])
            val_num, _ = _parse_numeric_value(val_text)
        elif "valueInteger" in obs_json:
            val_num = float(obs_json["valueInteger"])
            val_text = str(obs_json["valueInteger"])
        elif "valueCodeableConcept" in obs_json and isinstance(
            obs_json["valueCodeableConcept"], dict
        ):
            vcc = obs_json["valueCodeableConcept"]
            val_text = vcc.get("text")
            if not val_text and "coding" in vcc and isinstance(vcc["coding"], list):
                val_text = vcc["coding"][0].get("display") or vcc["coding"][0].get(
                    "code"
                )

        if val_num is None and val_text is None:
            errors.append(
                {
                    "index": idx,
                    "error": "FHIR Observation missing valid value (valueQuantity, valueString, etc.)",
                    "resource": obs_json,
                }
            )
            continue

        # 4. Observation datetime extraction
        obs_date_str = (
            obs_json.get("effectiveDateTime")
            or obs_json.get("effectiveInstant")
            or obs_json.get("issued")
        )
        if not obs_date_str and isinstance(obs_json.get("effectivePeriod"), dict):
            obs_date_str = obs_json["effectivePeriod"].get("start")
        obs_date = _parse_iso_or_clinical_date(obs_date_str)

        # 5. Reference range extraction
        ref_low = None
        ref_high = None
        ref_ranges = obs_json.get("referenceRange")
        if isinstance(ref_ranges, list) and len(ref_ranges) > 0:
            first_rr = ref_ranges[0]
            if isinstance(first_rr, dict):
                if "low" in first_rr and isinstance(first_rr["low"], dict):
                    ref_low, _ = _parse_numeric_value(first_rr["low"].get("value"))
                if "high" in first_rr and isinstance(first_rr["high"], dict):
                    ref_high, _ = _parse_numeric_value(first_rr["high"].get("value"))
                if "text" in first_rr and ref_low is None and ref_high is None:
                    ref_low, ref_high = _parse_reference_range_bounds(
                        first_rr.get("text")
                    )

        # 6. Interpretation flag extraction
        abnormal_flag = None
        interp = obs_json.get("interpretation")
        if isinstance(interp, list) and len(interp) > 0:
            first_int = interp[0]
            if isinstance(first_int, dict):
                codings = first_int.get("coding", [])
                if isinstance(codings, list) and len(codings) > 0:
                    abnormal_flag = codings[0].get("code")
                elif "text" in first_int:
                    abnormal_flag = first_int.get("text")
        elif isinstance(interp, dict):
            codings = interp.get("coding", [])
            if isinstance(codings, list) and len(codings) > 0:
                abnormal_flag = codings[0].get("code")

        # 7. Visit / Encounter extraction
        visit_id = None
        enc = obs_json.get("encounter") or obs_json.get("context")
        if isinstance(enc, dict):
            visit_id = enc.get("reference", "").replace("Encounter/", "") or enc.get(
                "display"
            )

        rec = RawLabRecord(
            subject_id=subj_id,
            study_id=default_study_id,
            site_id=default_site_id,
            visit_id=visit_id,
            test_code=test_code,
            test_name=test_name,
            value=val_num,
            value_string=val_text,
            unit=unit,
            observation_date=obs_date,
            lab_source=default_source.upper(),
            reference_range_low=ref_low,
            reference_range_high=ref_high,
            raw_abnormal_flag=abnormal_flag.upper() if abnormal_flag else None,
        )
        records.append(rec)

    return records, errors


class LabIngestionService:
    """Service executing batch ingestion, normalization, reference range evaluation,
    auto-query generation, and critical alert notification."""

    @staticmethod
    async def ingest_batch(
        session: Any,
        payload: str | bytes | dict | list,
        format: str | LabIngestFormat = LabIngestFormat.CSV,
        study_id: str | None = None,
        site_id: str | None = None,
        lab_source: str = "CENTRAL",
        user_id: str = "system_lab_ingestion",
        change_reason: str = "Batch laboratory data ingestion",
        background_tasks: Any | None = None,
    ) -> LabBatchIngestResult:
        """Process and persist a laboratory batch payload.

        Performs:
        1. Multi-format parsing (CSV, HL7 v2.x, FHIR Observation).
        2. Subject demographics resolution (age and sex at observation date).
        3. Standardized UCUM unit conversion and catalog alignment.
        4. Multi-dimensional age/sex/site-stratified reference range selection.
        5. Normal and critical boundary evaluation.
        6. ClinicalObservation record creation with evaluated indicator snapshot.
        7. Automated ClinicalQuery discrepancy creation for out-of-range results.
        8. Critical SAE alert notification dispatch.

        Args:
            session: Async SQLAlchemy database session.
            payload: Raw batch content (CSV string, HL7 text, or FHIR JSON).
            format: Format type ('csv', 'hl7', 'fhir').
            study_id: Optional default study identifier.
            site_id: Optional default site identifier.
            lab_source: Source laboratory type ('CENTRAL' or 'LOCAL').
            user_id: Authenticated GxP user identifier.
            change_reason: GxP 21 CFR Part 11 audit rationale.
            background_tasks: Optional Starlette BackgroundTasks for alert dispatches.

        Returns:
            LabBatchIngestResult containing summary statistics and error records.
        """
        batch_id = str(uuid.uuid4())
        norm_format = str(format).lower().strip()

        # 1. Parse payload into RawLabRecord items
        if norm_format in ("csv", "tsv", "delimited"):
            raw_records, parse_errors = parse_csv_payload(
                payload=payload,
                default_study_id=study_id,
                default_site_id=site_id,
                default_source=lab_source,
            )
        elif norm_format in ("hl7", "hl7v2", "oru_r01", "oru^r01"):
            raw_records, parse_errors = parse_hl7_v2_payload(
                payload=payload,
                default_study_id=study_id,
                default_site_id=site_id,
                default_source=lab_source,
            )
        elif norm_format in ("fhir", "json"):
            raw_records, parse_errors = parse_fhir_payload(
                payload=payload,
                default_study_id=study_id,
                default_site_id=site_id,
                default_source=lab_source,
            )
        else:
            res = LabBatchIngestResult(
                batch_id=batch_id,
                study_id=study_id,
                format=norm_format,
                status="FAILED",
                total_processed=0,
                ingested_count=0,
                out_of_range_count=0,
                critical_alerts=0,
                queries_raised=0,
                errors=[{"error": f"Unsupported laboratory batch format: {format}"}],
            )
            _BATCH_STORE[batch_id] = res
            return res

        total_processed = len(raw_records) + len(parse_errors)
        ingested_count = 0
        out_of_range_count = 0
        critical_alerts_count = 0
        queries_raised_count = 0
        all_errors = list(parse_errors)

        with audit_context(user_id, change_reason):
            for idx, raw_rec in enumerate(raw_records, start=1):
                try:
                    effective_study_id = raw_rec.study_id or study_id

                    # Resolve subject from database
                    subj_db = None
                    if effective_study_id:
                        stmt_subj = select(ClinicalSubject).where(
                            ClinicalSubject.subject_id == raw_rec.subject_id,
                            ClinicalSubject.study_id == effective_study_id,
                            ClinicalSubject.is_deleted.is_(False),
                        )
                    else:
                        stmt_subj = select(ClinicalSubject).where(
                            ClinicalSubject.subject_id == raw_rec.subject_id,
                            ClinicalSubject.is_deleted.is_(False),
                        )
                    res_subj = await session.execute(stmt_subj)
                    subj_db = res_subj.scalars().first()

                    if subj_db and not effective_study_id:
                        effective_study_id = subj_db.study_id

                    effective_study_id = effective_study_id or "STUDY-DEFAULT"
                    effective_site_id = (
                        raw_rec.site_id
                        or (subj_db.site_id if subj_db else None)
                        or site_id
                    )

                    obs_date = raw_rec.observation_date or datetime.now(UTC).replace(
                        tzinfo=None
                    )

                    # Demographics extraction
                    gender = "U"
                    age = None
                    if subj_db:
                        demo = get_safe_demographics(
                            subj_db, obs_date, preserve_custom=True
                        )
                        gender = demo.get("gender", "U")
                        age = demo.get("age")

                    # Catalog normalization lookup
                    stmt_master = select(LabTestMaster).where(
                        LabTestMaster.study_id == effective_study_id,
                        LabTestMaster.test_code == raw_rec.test_code,
                        LabTestMaster.is_deleted.is_(False),
                    )
                    res_master = await session.execute(stmt_master)
                    master = res_master.scalars().first()

                    test_name = raw_rec.test_name
                    target_unit = None
                    if master:
                        if not test_name or test_name == raw_rec.test_code:
                            test_name = master.test_name
                        target_unit = master.normalized_unit

                    test_name = test_name or raw_rec.test_code

                    # Unit conversion and normalization
                    norm_val = raw_rec.value
                    norm_unit = raw_rec.unit

                    if raw_rec.value is not None and raw_rec.unit:
                        if target_unit and target_unit != raw_rec.unit:
                            try:
                                converted_val = await convert_lab_unit(
                                    session=session,
                                    test_code=raw_rec.test_code,
                                    from_unit=raw_rec.unit,
                                    to_unit=target_unit,
                                    value=raw_rec.value,
                                )
                                norm_val = converted_val
                                norm_unit = target_unit
                            except Exception as conv_err:
                                logger.warning(
                                    f"Catalog unit conversion failed ({conv_err}); attempting standard UCUM normalization"
                                )
                                norm_val, norm_unit = get_normalized_representation(
                                    raw_rec.value, raw_rec.unit
                                )
                        else:
                            norm_val, norm_unit = get_normalized_representation(
                                raw_rec.value, raw_rec.unit
                            )

                    # Reference Range Selection & Evaluation
                    ranges = await get_active_lab_ranges(
                        lab_range_cache,
                        session,
                        effective_study_id,
                        raw_rec.test_code,
                    )

                    matched_range = select_reference_range(
                        ranges=ranges,
                        study_id=effective_study_id,
                        test_code=raw_rec.test_code,
                        normalized_unit=norm_unit or "",
                        lab_source=raw_rec.lab_source or "CENTRAL",
                        sex=gender,
                        age=age,
                        site_id=effective_site_id,
                    )

                    ref_low = raw_rec.reference_range_low
                    ref_high = raw_rec.reference_range_high

                    if matched_range:
                        indicator, out_of_range, matched_bounds = evaluate_lab_value(
                            norm_val, matched_range
                        )
                        ref_low = getattr(matched_range, "low_bound", None)
                        ref_high = getattr(matched_range, "high_bound", None)
                    else:
                        # Fallback to payload bounds if provided
                        if (
                            raw_rec.reference_range_low is not None
                            or raw_rec.reference_range_high is not None
                            or raw_rec.critical_low is not None
                            or raw_rec.critical_high is not None
                        ):
                            ad_hoc_range = {
                                "low_bound": raw_rec.reference_range_low,
                                "high_bound": raw_rec.reference_range_high,
                                "critical_low": raw_rec.critical_low,
                                "critical_high": raw_rec.critical_high,
                            }
                            indicator, out_of_range, matched_bounds = (
                                evaluate_lab_value(norm_val, ad_hoc_range)
                            )
                            if raw_rec.raw_abnormal_flag:
                                flag_u = raw_rec.raw_abnormal_flag.upper()
                                if flag_u in ("HH", "CRITICAL_HIGH") and indicator in (
                                    "HIGH",
                                    "NORMAL",
                                    None,
                                ):
                                    indicator = "HIGH HIGH"
                                    out_of_range = True
                                elif flag_u in (
                                    "LL",
                                    "CRIT",
                                    "PANIC",
                                    "CRITICAL_LOW",
                                ) and indicator in ("LOW", "NORMAL", None):
                                    indicator = "LOW LOW"
                                    out_of_range = True
                        elif raw_rec.raw_abnormal_flag:
                            # Map raw abnormal flag
                            flag_u = raw_rec.raw_abnormal_flag.upper()
                            if flag_u in ("LL", "CRIT", "PANIC", "CRITICAL_LOW"):
                                indicator = "LOW LOW"
                                out_of_range = True
                            elif flag_u in ("HH", "CRITICAL_HIGH"):
                                indicator = "HIGH HIGH"
                                out_of_range = True
                            elif flag_u in ("L", "LOW"):
                                indicator = "LOW"
                                out_of_range = True
                            elif flag_u in ("H", "HIGH"):
                                indicator = "HIGH"
                                out_of_range = True
                            elif flag_u in ("A", "ABNORMAL"):
                                indicator = "ABNORMAL"
                                out_of_range = True
                            else:
                                indicator = "NORMAL"
                                out_of_range = False
                            matched_bounds = None
                        else:
                            indicator = None
                            out_of_range = False
                            matched_bounds = None

                    # Stamping capture-time protocol-version identity
                    protocol_version_tag = None
                    protocol_version_index = None
                    stmt_consent = (
                        select(SubjectConsent)
                        .where(
                            SubjectConsent.subject_id == raw_rec.subject_id,
                            SubjectConsent.study_id == effective_study_id,
                            SubjectConsent.icf_signed.is_(True),
                        )
                        .order_by(SubjectConsent.version_index.desc())
                    )
                    res_consent = await session.execute(stmt_consent)
                    active_consent = res_consent.scalars().first()
                    if active_consent:
                        protocol_version_tag = active_consent.version_tag
                        protocol_version_index = active_consent.version_index

                    # 6. Create and persist ClinicalObservation
                    obs = ClinicalObservation(
                        subject_id=raw_rec.subject_id,
                        study_id=effective_study_id,
                        site_id=effective_site_id,
                        visit_id=raw_rec.visit_id,
                        domain="LB",
                        observation_date=obs_date,
                        test_code=raw_rec.test_code,
                        test_name=test_name,
                        value=raw_rec.value,
                        value_string=raw_rec.value_string,
                        unit=raw_rec.unit,
                        normalized_value=norm_val,
                        normalized_unit=norm_unit,
                        is_outlier=out_of_range,
                        lab_source=raw_rec.lab_source or "CENTRAL",
                        lab_site_id=effective_site_id,
                        lab_indicator=indicator,
                        lab_out_of_range=out_of_range,
                        matched_normal_bounds=matched_bounds,
                        reference_range_low=ref_low,
                        reference_range_high=ref_high,
                        protocol_version_tag=protocol_version_tag,
                        protocol_version_index=protocol_version_index,
                    )
                    session.add(obs)
                    await session.flush()  # Populates obs.id

                    # 7. Discrepancy & SAE Auto-Query Generation
                    is_critical = indicator in ("LOW LOW", "HIGH HIGH")
                    if out_of_range or is_critical or indicator in ("LOW", "HIGH"):
                        query_type = (
                            "POTENTIAL_SAE_CRITICAL"
                            if is_critical
                            else "OUT_OF_RANGE_WARNING"
                        )
                        priority = (
                            "CRITICAL"
                            if is_critical
                            else "HIGH"
                            if indicator in ("LOW", "HIGH")
                            else "MEDIUM"
                        )

                        query_msg = (
                            f"Laboratory observation for test {raw_rec.test_code} ({test_name}) "
                            f"with value {raw_rec.value} {raw_rec.unit or ''} is out of normal "
                            f"reference range [{matched_bounds or 'N/A'}]. Evaluated indicator: {indicator}."
                        )

                        query = ClinicalQuery(
                            id=str(uuid.uuid4()),
                            study_id=effective_study_id,
                            site_id=effective_site_id,
                            subject_id=raw_rec.subject_id,
                            visit_id=raw_rec.visit_id,
                            domain="LB",
                            test_code=raw_rec.test_code,
                            observation_id=obs.id,
                            status="OPEN",
                            priority=priority,
                            query_type=query_type,
                            origin="SYSTEM_LAB_INGESTION",
                            explanation="Automated discrepancy query generated during lab batch ingestion",
                            message=query_msg,
                            created_by=user_id,
                            created_at=datetime.now(UTC).replace(tzinfo=None),
                        )
                        session.add(query)
                        queries_raised_count += 1
                        out_of_range_count += 1

                        if is_critical:
                            critical_alerts_count += 1
                            if background_tasks is not None:
                                dispatch_critical_lab_alerts(
                                    background_tasks,
                                    obs,
                                    indicator or "CRITICAL",
                                    user_id,
                                    change_reason,
                                )

                    ingested_count += 1

                except Exception as row_exc:
                    logger.error(
                        f"Error processing lab record at index {idx}: {row_exc}"
                    )
                    all_errors.append(
                        {
                            "index": idx,
                            "subject_id": raw_rec.subject_id,
                            "test_code": raw_rec.test_code,
                            "error": str(row_exc),
                        }
                    )

            # Commit all observations and queries in this batch
            await session.commit()

        # Compute final batch status
        if not all_errors:
            status = "COMPLETED"
        elif ingested_count > 0:
            status = "COMPLETED_WITH_ERRORS"
        else:
            status = "FAILED"

        result = LabBatchIngestResult(
            batch_id=batch_id,
            study_id=study_id,
            format=norm_format,
            status=status,
            total_processed=total_processed,
            ingested_count=ingested_count,
            out_of_range_count=out_of_range_count,
            critical_alerts=critical_alerts_count,
            queries_raised=queries_raised_count,
            errors=all_errors,
        )

        _BATCH_STORE[batch_id] = result
        return result

    @staticmethod
    def get_batch_status(batch_id: str) -> LabBatchIngestResult | None:
        """Retrieve status and metadata for a previously ingested laboratory batch."""
        return _BATCH_STORE.get(batch_id)

    @staticmethod
    def list_batch_statuses(study_id: str | None = None) -> list[LabBatchIngestResult]:
        """List historical batch ingestion statuses, optionally filtered by study_id."""
        batches = list(_BATCH_STORE.values())
        if study_id:
            return [b for b in batches if b.study_id == study_id]
        return batches
