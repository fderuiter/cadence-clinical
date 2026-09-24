"""Central and Local Laboratory Batch Ingestion Service with Normal Range Governance
and Automated CTCAE/DAIDS Toxicity Grading.

Provides multi-format parsing (delimited CSV/TSV/sFTP, HL7 v2.x ORU^R01, HL7 FHIR
Observation JSON, CDISC LAB Standard v1.0.1 XML ODM & JSON, and Manual Local Lab Batch Entry),
LOINC and UCUM unit conversion, demographic-stratified normal reference range matching,
local lab certification/expiration tracking and uncalibrated range safeguards, real-time
CTCAE v5.0 / DAIDS v2.1 toxicity evaluation, acute baseline delta checks, and automated
Grade 3/4 Adverse Event eCRF drafting with 21 CFR Part 11 audit trails.

Requirements:
- ADR-2199 / PRD-SYS-123 (Lab Normal Range Governance & Interoperability)
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-PVG-001 (Pharmacovigilance & AE Staging Automation)
- PRD-MDR-001 (Metadata Repository & Catalog Normalization)
- PRD-QRY-001 (Automated Discrepancy Query Escalation)
- Trace-1 (Audit Trail & 21 CFR Part 11)
- Trace-15 (Laboratory Data Flow & Reference Ranges)
"""

import contextlib
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

import defusedxml.ElementTree as ET
from pydantic import BaseModel, Field

from apps.execution.database.context import audit_context
from apps.execution.domain.lab_assessment_models import LabAssessmentContext
from apps.execution.services.lab_assessment_engine import LabAssessmentEngine

logger = logging.getLogger(__name__)


class LabIngestFormat(StrEnum):
    """Supported laboratory batch ingestion formats."""

    CSV = "csv"
    HL7 = "hl7"
    FHIR = "fhir"
    CDISC_LAB = "cdisc_lab"
    LOCAL_MANUAL = "local_manual"


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
    co_analytes: dict[str, float] = Field(default_factory=dict)
    additional_metadata: dict[str, Any] = Field(default_factory=dict)


class LabBatchIngestRequest(BaseModel):
    """Transport schema for laboratory batch ingestion requests."""

    format: LabIngestFormat = LabIngestFormat.CSV
    payload: str | None = None
    resource: dict[str, Any] | list[dict[str, Any]] | None = None
    study_id: str | None = None
    site_id: str | None = None
    lab_source: str = "CENTRAL"
    grading_scale: str = "CTCAE_V5"
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
    staged_aes_count: int = 0
    uncalibrated_safeguards: int = 0
    toxicity_grades: dict[str, int] = Field(default_factory=dict)
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
    """Extract low and high numeric bounds from clinical range strings."""
    if not range_str or not str(range_str).strip():
        return None, None

    s = str(range_str).strip()

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

    m_less = re.search(r"<\s*([0-9]+(?:\.[0-9]+)?)", s)
    if m_less:
        try:
            return None, float(m_less.group(1))
        except ValueError:
            pass

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
    """Parse delimited CSV/TSV/sFTP laboratory observation data into RawLabRecord items."""
    if isinstance(payload, bytes):
        payload_str = payload.decode("utf-8-sig", errors="replace")
    else:
        payload_str = str(payload)

    records: list[RawLabRecord] = []
    errors: list[dict[str, Any]] = []

    if not payload_str.strip():
        return records, errors

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
        "lbornrlo",
        "lbstnrlo",
    ]
    ref_high_aliases = [
        "refhigh",
        "referencerangehigh",
        "rangehigh",
        "highbound",
        "normalhigh",
        "refrangehigh",
        "high",
        "lbornrhi",
        "lbstnrhi",
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
        "lbnrind",
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
    """Parse HL7 v2.x (ORU^R01) observation messages into RawLabRecord items."""
    if isinstance(payload, bytes):
        payload_str = payload.decode("utf-8", errors="replace")
    else:
        payload_str = str(payload)

    records: list[RawLabRecord] = []
    errors: list[dict[str, Any]] = []

    if not payload_str.strip():
        return records, errors

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
            if len(fields) > 4 and fields[4].strip():
                sending_fac = fields[4].strip()
                if not current_site_id:
                    current_site_id = sending_fac

        elif seg_type == "PID":
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
            if len(fields) > 19 and fields[19].strip():
                current_visit_id = fields[19].strip()

        elif seg_type == "OBR":
            if len(fields) > 7 and fields[7].strip():
                current_order_date = _parse_iso_or_clinical_date(fields[7].strip())

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

            val_type = fields[2].strip().upper() if len(fields) > 2 else "ST"

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

            raw_val_str = fields[5].strip() if len(fields) > 5 else ""
            val_num, val_text = _parse_numeric_value(raw_val_str)
            if val_type == "NM" and val_num is None:
                val_text = raw_val_str

            raw_unit = fields[6].strip() if len(fields) > 6 else None
            unit = raw_unit.split("^")[0].strip() if raw_unit else None

            ref_range_str = fields[7].strip() if len(fields) > 7 else None
            ref_low, ref_high = _parse_reference_range_bounds(ref_range_str)

            abnormal_flag = (
                fields[8].strip().upper()
                if len(fields) > 8 and fields[8].strip()
                else None
            )

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
    """Parse HL7 FHIR Observation resource JSON into RawLabRecord items."""
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
            if (
                isinstance(item, dict)
                and item.get("resourceType") == "Observation"
                or isinstance(item, dict)
            ):
                observation_resources.append(item)

    for idx, obs_json in enumerate(observation_resources):
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

        obs_date_str = (
            obs_json.get("effectiveDateTime")
            or obs_json.get("effectiveInstant")
            or obs_json.get("issued")
        )
        if not obs_date_str and isinstance(obs_json.get("effectivePeriod"), dict):
            obs_date_str = obs_json["effectivePeriod"].get("start")
        obs_date = _parse_iso_or_clinical_date(obs_date_str)

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


def parse_cdisc_lab_payload(
    payload: str | bytes | dict | list,
    default_study_id: str | None = None,
    default_site_id: str | None = None,
    default_source: str = "CENTRAL",
) -> tuple[list[RawLabRecord], list[dict[str, Any]]]:
    """Parse CDISC LAB Standard v1.0.1 XML ODM or CDISC LAB JSON observation records.

    Supports CDISC standard domain variables:
    STUDYID, USUBJID/SUBJID, LBTESTCD, LBTEST, LBCAT, LBORRES, LBORRESU,
    LBORNRLO, LBORNRHI, LBSTRESC, LBSTRESN, LBSTRESU, LBSTNRLO, LBSTNRHI,
    LBNRIND, LBDTC, VISIT, VISITNUM, LBSPEC, LBMETHOD.
    """
    records: list[RawLabRecord] = []
    errors: list[dict[str, Any]] = []

    if isinstance(payload, bytes):
        payload_str = payload.decode("utf-8-sig", errors="replace")
    elif isinstance(payload, str):
        payload_str = payload
    else:
        payload_str = ""

    # 1. XML ODM Parsing
    if (
        payload_str.strip().startswith("<")
        or "<ODM" in payload_str
        or "<ClinicalData" in payload_str
    ):
        try:
            root = ET.fromstring(payload_str.strip())

            # Handle XML namespaces gracefully
            def strip_ns(tag: str) -> str:
                return tag.split("}")[-1] if "}" in tag else tag

            for clinical_data in root.iter():
                if strip_ns(clinical_data.tag) == "ClinicalData":
                    study_id = clinical_data.get("StudyOID") or default_study_id
                    for subj_data in clinical_data:
                        if strip_ns(subj_data.tag) == "SubjectData":
                            subj_id = subj_data.get("SubjectKey")
                            site_id = subj_data.get("SiteOID") or default_site_id

                            for event_data in subj_data:
                                if strip_ns(event_data.tag) == "StudyEventData":
                                    visit_id = event_data.get("StudyEventOID")
                                    for form_data in event_data:
                                        for item_group in form_data:
                                            if (
                                                strip_ns(item_group.tag)
                                                == "ItemGroupData"
                                            ):
                                                # Extract item values
                                                item_dict: dict[str, str] = {}
                                                for item in item_group:
                                                    if strip_ns(item.tag) == "ItemData":
                                                        oid = item.get(
                                                            "ItemOID", ""
                                                        ).upper()
                                                        val = (
                                                            item.get("Value")
                                                            or item.text
                                                            or ""
                                                        )
                                                        item_dict[oid] = val.strip()

                                                test_code = (
                                                    item_dict.get("LBTESTCD")
                                                    or item_dict.get("PARAMCD")
                                                    or item_dict.get("TESTCD")
                                                )
                                                if not test_code:
                                                    continue

                                                test_name = (
                                                    item_dict.get("LBTEST")
                                                    or item_dict.get("PARAM")
                                                    or test_code
                                                )
                                                raw_val_str = (
                                                    item_dict.get("LBSTRESN")
                                                    or item_dict.get("LBORRES")
                                                    or item_dict.get("VALUE")
                                                )
                                                val_num, val_text = (
                                                    _parse_numeric_value(raw_val_str)
                                                )
                                                unit = (
                                                    item_dict.get("LBSTRESU")
                                                    or item_dict.get("LBORRESU")
                                                    or item_dict.get("UNIT")
                                                )
                                                obs_date = _parse_iso_or_clinical_date(
                                                    item_dict.get("LBDTC")
                                                    or item_dict.get("DATE")
                                                )
                                                ref_low, _ = _parse_numeric_value(
                                                    item_dict.get("LBSTNRLO")
                                                    or item_dict.get("LBORNRLO")
                                                )
                                                ref_high, _ = _parse_numeric_value(
                                                    item_dict.get("LBSTNRHI")
                                                    or item_dict.get("LBORNRHI")
                                                )
                                                flag = item_dict.get(
                                                    "LBNRIND"
                                                ) or item_dict.get("FLAG")

                                                if subj_id and (
                                                    val_num is not None
                                                    or val_text is not None
                                                ):
                                                    records.append(
                                                        RawLabRecord(
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
                                                            lab_source=default_source.upper(),
                                                            reference_range_low=ref_low,
                                                            reference_range_high=ref_high,
                                                            raw_abnormal_flag=flag,
                                                        )
                                                    )
        except Exception as e:
            errors.append({"error": f"Failed to parse CDISC LAB XML ODM: {e}"})
            return records, errors

        return records, errors

    # 2. JSON or Dictionary / List representation
    json_data = None
    if isinstance(payload, (dict, list)):
        json_data = payload
    elif payload_str.strip():
        with contextlib.suppress(Exception):
            json_data = json.loads(payload_str)

    if json_data is not None:
        items = (
            json_data
            if isinstance(json_data, list)
            else json_data.get("records") or json_data.get("items") or [json_data]
        )
        for idx, item in enumerate(items):
            if not isinstance(item, dict):
                continue
            subj_id = (
                item.get("USUBJID") or item.get("SUBJID") or item.get("subject_id")
            )
            test_code = (
                item.get("LBTESTCD") or item.get("PARAMCD") or item.get("test_code")
            )

            if not subj_id or not test_code:
                errors.append(
                    {
                        "index": idx,
                        "error": "Missing USUBJID or LBTESTCD in CDISC record",
                    }
                )
                continue

            test_name = item.get("LBTEST") or item.get("test_name") or test_code
            val_num, val_text = _parse_numeric_value(
                item.get("LBSTRESN") or item.get("LBORRES") or item.get("value")
            )
            unit = item.get("LBSTRESU") or item.get("LBORRESU") or item.get("unit")
            obs_date = _parse_iso_or_clinical_date(
                item.get("LBDTC") or item.get("observation_date")
            )
            ref_low, _ = _parse_numeric_value(
                item.get("LBSTNRLO") or item.get("LBORNRLO") or item.get("ref_low")
            )
            ref_high, _ = _parse_numeric_value(
                item.get("LBSTNRHI") or item.get("LBORNRHI") or item.get("ref_high")
            )
            flag = item.get("LBNRIND") or item.get("flag")
            site_id = item.get("SITEID") or item.get("site_id") or default_site_id
            study_id = item.get("STUDYID") or item.get("study_id") or default_study_id
            visit_id = item.get("VISIT") or item.get("visit_id")

            records.append(
                RawLabRecord(
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
                    lab_source=default_source.upper(),
                    reference_range_low=ref_low,
                    reference_range_high=ref_high,
                    raw_abnormal_flag=flag,
                )
            )
        return records, errors

    # 3. Delimited fallback
    return parse_csv_payload(
        payload=payload,
        default_study_id=default_study_id,
        default_site_id=default_site_id,
        default_source=default_source,
    )


def parse_local_manual_payload(
    payload: str | bytes | dict | list,
    default_study_id: str | None = None,
    default_site_id: str | None = None,
) -> tuple[list[RawLabRecord], list[dict[str, Any]]]:
    """Parse manual local laboratory batch entry payloads."""
    records: list[RawLabRecord] = []
    errors: list[dict[str, Any]] = []

    json_data = None
    if isinstance(payload, (dict, list)):
        json_data = payload
    else:
        try:
            raw_str = (
                payload.decode("utf-8") if isinstance(payload, bytes) else str(payload)
            )
            json_data = json.loads(raw_str)
        except Exception:
            pass

    if json_data is not None:
        batch_site = default_site_id
        batch_study = default_study_id

        if isinstance(json_data, dict):
            batch_site = json_data.get("site_id") or default_site_id
            batch_study = json_data.get("study_id") or default_study_id
            items = json_data.get("records") or json_data.get("items") or [json_data]
        else:
            items = json_data

        for idx, item in enumerate(items):
            if not isinstance(item, dict):
                continue
            subj_id = (
                item.get("subject_id") or item.get("USUBJID") or item.get("subjid")
            )
            test_code = (
                item.get("test_code") or item.get("LBTESTCD") or item.get("code")
            )

            if not subj_id or not test_code:
                errors.append(
                    {
                        "index": idx,
                        "error": "Manual local lab entry missing subject_id or test_code",
                    }
                )
                continue

            test_name = item.get("test_name") or item.get("LBTEST") or test_code
            val_num, val_text = _parse_numeric_value(
                item.get("value") or item.get("result")
            )
            unit = item.get("unit") or item.get("units")
            obs_date = _parse_iso_or_clinical_date(
                item.get("observation_date") or item.get("date")
            )
            ref_low, _ = _parse_numeric_value(
                item.get("reference_range_low") or item.get("low_bound")
            )
            ref_high, _ = _parse_numeric_value(
                item.get("reference_range_high") or item.get("high_bound")
            )
            crit_low, _ = _parse_numeric_value(item.get("critical_low"))
            crit_high, _ = _parse_numeric_value(item.get("critical_high"))
            visit_id = item.get("visit_id") or item.get("visit")
            site_id = item.get("site_id") or batch_site
            study_id = item.get("study_id") or batch_study

            records.append(
                RawLabRecord(
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
                    lab_source="LOCAL",
                    reference_range_low=ref_low,
                    reference_range_high=ref_high,
                    critical_low=crit_low,
                    critical_high=crit_high,
                )
            )
        return records, errors

    return parse_csv_payload(
        payload=payload,
        default_study_id=default_study_id,
        default_site_id=default_site_id,
        default_source="LOCAL",
    )


class LabIngestionService:
    """Service executing batch ingestion, normalization, reference range evaluation,
    local lab certification tracking, CTCAE/DAIDS toxicity grading, auto-query generation,
    and automated Grade 3/4 Adverse Event drafting with 21 CFR Part 11 audit trails."""

    @staticmethod
    async def ingest_batch(
        session: Any,
        payload: str | bytes | dict | list,
        format: str | LabIngestFormat = LabIngestFormat.CSV,
        study_id: str | None = None,
        site_id: str | None = None,
        lab_source: str = "CENTRAL",
        grading_scale: str = "CTCAE_V5",
        user_id: str = "system_lab_ingestion",
        change_reason: str = "Batch laboratory data ingestion",
        background_tasks: Any | None = None,
    ) -> LabBatchIngestResult:
        """Process and persist a laboratory batch payload."""
        batch_id = str(uuid.uuid4())
        norm_format = str(format).lower().strip()

        # 1. Parse payload into RawLabRecord items
        if norm_format in ("csv", "tsv", "delimited", "sftp"):
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
        elif norm_format in ("fhir", "fhir_json"):
            raw_records, parse_errors = parse_fhir_payload(
                payload=payload,
                default_study_id=study_id,
                default_site_id=site_id,
                default_source=lab_source,
            )
        elif norm_format in ("cdisc_lab", "cdisc", "cdisc_odm", "xml"):
            raw_records, parse_errors = parse_cdisc_lab_payload(
                payload=payload,
                default_study_id=study_id,
                default_site_id=site_id,
                default_source=lab_source,
            )
        elif norm_format in ("local_manual", "manual", "local"):
            raw_records, parse_errors = parse_local_manual_payload(
                payload=payload,
                default_study_id=study_id,
                default_site_id=site_id,
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
                staged_aes_count=0,
                uncalibrated_safeguards=0,
                errors=[{"error": f"Unsupported laboratory batch format: {format}"}],
            )
            _BATCH_STORE[batch_id] = res
            return res

        total_processed = len(raw_records) + len(parse_errors)
        ingested_count = 0
        out_of_range_count = 0
        critical_alerts_count = 0
        queries_raised_count = 0
        staged_aes_count = 0
        uncalibrated_safeguards_count = 0
        toxicity_counts: dict[str, int] = {
            "Grade 0": 0,
            "Grade 1": 0,
            "Grade 2": 0,
            "Grade 3": 0,
            "Grade 4": 0,
        }
        all_errors = list(parse_errors)
        with audit_context(user_id, change_reason):
            for idx, raw_rec in enumerate(raw_records, start=1):
                try:
                    assessment = await LabAssessmentEngine.evaluate_laboratory_result(
                        observation=raw_rec,
                        context=LabAssessmentContext(
                            session=session,
                            study_id=raw_rec.study_id or study_id,
                            site_id=raw_rec.site_id or site_id,
                            grading_scale=grading_scale,
                            co_analytes=raw_rec.co_analytes,
                            user_id=user_id,
                            change_reason=change_reason,
                            background_tasks=background_tasks,
                            persist_records=True,
                        ),
                    )

                    grade_key = f"Grade {assessment.ctcae_grade}"
                    toxicity_counts[grade_key] = toxicity_counts.get(grade_key, 0) + 1

                    if assessment.uncalibrated_safeguard_triggered:
                        uncalibrated_safeguards_count += 1

                    if assessment.out_of_range or assessment.indicator in (
                        "LOW",
                        "HIGH",
                        "LOW LOW",
                        "HIGH HIGH",
                        "ABNORMAL",
                    ):
                        out_of_range_count += 1

                    if (
                        assessment.indicator
                        in (
                            "LOW LOW",
                            "HIGH HIGH",
                        )
                        or assessment.is_critical_ae_trigger
                    ):
                        critical_alerts_count += 1

                    if assessment.is_critical_ae_trigger:
                        staged_aes_count += 1

                    queries_raised_count += len(assessment.queries_raised)
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

            await session.commit()

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
            staged_aes_count=staged_aes_count,
            uncalibrated_safeguards=uncalibrated_safeguards_count,
            toxicity_grades=toxicity_counts,
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
