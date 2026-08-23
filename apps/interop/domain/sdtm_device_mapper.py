"""CDISC SDTM DD and DO Mapper for Interoperability Service.

Provides self-contained pure mapping logic for Device Deficiencies (DD)
and Device Tracking & Operations (DO) datasets.

Requirements: PRD-SYS-126, PRD-DEV-004, ISO 14155:2020, EU MDR 2017/745, ADR-2209.
"""

from datetime import date, datetime
from typing import Any

from pydantic import BaseModel


class SDTMRecordDD(BaseModel):
    """Pydantic v2 domain model for CDISC SDTM DD (Device Deficiencies)."""

    STUDYID: str
    DOMAIN: str = "DD"
    USUBJID: str
    DDSEQ: int
    DDTESTCD: str
    DDTEST: str
    DDCAT: str = "DEVICE DEFICIENCY"
    DDSCAT: str | None = None
    DDORRES: str
    DDSTRESC: str
    DDDET: str | None = None
    DDDEVTYP: str | None = None
    DDDEVID: str | None = None
    DDSPID: str | None = None
    DDAT: str | None = None
    DDCAUS: str | None = None
    DDLOT: str | None = None
    DDSER: str | None = None
    DDSTDTC: str | None = None
    created_by: str = "system"
    reason_for_change: str = "Automated SDTM DD Mapping"


class SDTMRecordDO(BaseModel):
    """Pydantic v2 domain model for CDISC SDTM DO (Device Tracking and Operations)."""

    STUDYID: str
    DOMAIN: str = "DO"
    USUBJID: str
    DOSEQ: int
    DODEVID: str
    DOTESTCD: str
    DOTEST: str
    DOCAT: str = "DEVICE TRACKING"
    DOSCAT: str | None = None
    DOSTATUS: str
    DODI: str | None = None
    DOPI: str | None = None
    DOLOT: str | None = None
    DOSER: str | None = None
    DOEXPDTC: str | None = None
    DOMFRDTC: str | None = None
    DODTC: str | None = None
    created_by: str = "system"
    reason_for_change: str = "Automated SDTM DO Mapping"


def _get_val(obj: Any, key: str, default: Any = None) -> Any:
    """Safe value retrieval from dict or object."""
    if obj is None:
        return default
    if isinstance(obj, dict):
        return obj.get(key, default)
    return getattr(obj, key, default)


def _to_dtc(val: Any) -> str | None:
    """Formats date/datetime into ISO DTC string."""
    if val is None:
        return None
    if isinstance(val, (datetime, date)):
        return val.isoformat()
    return str(val)


def map_dd(
    deficiencies: list[Any],
    created_by: str = "system",
    reason_for_change: str | None = None,
) -> list[SDTMRecordDD]:
    """Maps device deficiencies to SDTM DD records."""
    if reason_for_change is None:
        reason_for_change = "Automated Device Deficiency to SDTM DD Mapping"

    dd_records: list[SDTMRecordDD] = []
    grouped: dict[str, list[Any]] = {}

    for d in deficiencies:
        study_id = _get_val(d, "study_id") or "STUDY-001"
        site_id = _get_val(d, "site_id") or "SITE-001"
        subj_id = _get_val(d, "subject_id") or "UNASSIGNED"
        usubjid = f"{study_id}-{site_id}-{subj_id}"
        grouped.setdefault(usubjid, []).append(d)

    for usubjid, items in grouped.items():
        for seq, item in enumerate(items, start=1):
            study_id = _get_val(item, "study_id") or "STUDY-001"
            defic_type = _get_val(item, "deficiency_type") or "MALFUNCTION"
            testcd_map = {
                "MALFUNCTION": "MALFUNC",
                "USABILITY_ERROR": "USEERR",
                "INADEQUATE_LABELING": "LABELING",
                "DESIGN_DEFECT": "DESDEF",
                "PACKAGING_BREACH": "PKGBRCH",
                "SOFTWARE_FAULT": "SWFAULT",
            }
            testcd = testcd_map.get(str(defic_type).upper(), "MALFUNC")
            test_name = str(defic_type).replace("_", " ").title()

            inc_dt = _to_dtc(_get_val(item, "incident_date"))
            desc = _get_val(item, "deficiency_description") or "Device Deficiency"

            rec = SDTMRecordDD(
                STUDYID=study_id,
                DOMAIN="DD",
                USUBJID=usubjid,
                DDSEQ=seq,
                DDTESTCD=testcd,
                DDTEST=test_name,
                DDCAT="DEVICE DEFICIENCY",
                DDSCAT=_get_val(item, "health_impact"),
                DDORRES=desc,
                DDSTRESC=desc,
                DDDET=_get_val(item, "root_cause_analysis"),
                DDDEVTYP=_get_val(item, "device_name"),
                DDDEVID=_get_val(item, "udi_di"),
                DDSPID=str(_get_val(item, "id", "")),
                DDAT=_get_val(item, "action_taken_device"),
                DDCAUS=_get_val(item, "safety_classification"),
                DDLOT=_get_val(item, "lot_number"),
                DDSER=_get_val(item, "serial_number"),
                DDSTDTC=inc_dt,
                created_by=created_by,
                reason_for_change=reason_for_change,
            )
            dd_records.append(rec)

    return dd_records


def map_do(
    devices: list[Any],
    created_by: str = "system",
    reason_for_change: str | None = None,
) -> list[SDTMRecordDO]:
    """Maps investigational devices to SDTM DO records."""
    if reason_for_change is None:
        reason_for_change = "Automated Device Tracking to SDTM DO Mapping"

    do_records: list[SDTMRecordDO] = []
    grouped: dict[str, list[Any]] = {}

    for d in devices:
        study_id = _get_val(d, "study_id") or "STUDY-001"
        site_id = _get_val(d, "site_id") or "SITE-001"
        subj_id = _get_val(d, "allocated_subject_id") or "INVENTORY"
        usubjid = f"{study_id}-{site_id}-{subj_id}"
        grouped.setdefault(usubjid, []).append(d)

    for usubjid, items in grouped.items():
        for seq, item in enumerate(items, start=1):
            study_id = _get_val(item, "study_id") or "STUDY-001"
            status = _get_val(item, "status") or "AVAILABLE"
            testcd_map = {
                "AVAILABLE": "INTAKE",
                "ASSIGNED": "ASSIGN",
                "DISPENSED": "DISP",
                "IMPLANTED": "IMPLANT",
                "QUARANTINED": "QUAR",
                "RETURNED": "RETN",
                "DEFICIENT": "DEFIC",
                "DESTROYED": "DEST",
            }
            testcd = testcd_map.get(str(status).upper(), "TRACK")
            test_name = f"Device {str(status).replace('_', ' ').title()}"

            exp_dtc = _to_dtc(_get_val(item, "expiration_date"))
            mfg_dtc = _to_dtc(_get_val(item, "manufacturing_date"))
            disp_dtc = _to_dtc(
                _get_val(item, "dispensed_at") or _get_val(item, "quarantined_at")
            )

            rec = SDTMRecordDO(
                STUDYID=study_id,
                DOMAIN="DO",
                USUBJID=usubjid,
                DOSEQ=seq,
                DODEVID=_get_val(item, "device_identifier")
                or str(_get_val(item, "id", "DEV-001")),
                DOTESTCD=testcd,
                DOTEST=test_name,
                DOCAT="DEVICE TRACKING",
                DOSCAT=_get_val(item, "device_type"),
                DOSTATUS=status,
                DODI=_get_val(item, "udi_di"),
                DOPI=_get_val(item, "udi_pi"),
                DOLOT=_get_val(item, "lot_number"),
                DOSER=_get_val(item, "serial_number"),
                DOEXPDTC=exp_dtc,
                DOMFRDTC=mfg_dtc,
                DODTC=disp_dtc,
                created_by=created_by,
                reason_for_change=reason_for_change,
            )
            do_records.append(rec)

    return do_records
