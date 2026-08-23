"""Automated CDISC SDTM Data Pipeline Engine.

Maps raw heterogeneous clinical observations into 10 standard CDISC SDTM v1.7 / v2.0 domains:
- DM (Demographics)
- AE (Adverse Events)
- CM (Concomitant Medications)
- VS (Vital Signs)
- LB (Laboratory Findings)
- EX (Exposure)
- DS (Disposition)
- TU (Tumor Identification)
- TR (Tumor Results / Measurements)
- RS (Disease Response)

Requirements: PRD-CDR-002, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import date, datetime
from typing import Any

from pydantic import BaseModel

from apps.interop.domain.cdr.models import (
    SDTMAE,
    SDTMCM,
    SDTMDM,
    SDTMDS,
    SDTMEX,
    SDTMLB,
    SDTMRS,
    SDTMTR,
    SDTMTU,
    SDTMVS,
)
from apps.interop.domain.lakehouse_models import (
    CanonicalClinicalRecord,
    ObservationDomain,
)


def _format_iso_date(dt: datetime | str | None) -> str | None:
    """Format datetime or string into ISO 8601 string."""
    if dt is None:
        return None
    if isinstance(dt, datetime):
        return dt.strftime("%Y-%m-%d")
    s = str(dt).strip()
    if not s:
        return None
    if len(s) >= 10 and s[4] == "-" and s[7] == "-":
        return s[:10]
    return s


def _parse_date(dt_val: datetime | str | None) -> date | None:
    """Parse date from datetime, ISO string, or None."""
    if dt_val is None:
        return None
    if isinstance(dt_val, datetime):
        return dt_val.date()
    if isinstance(dt_val, date):
        return dt_val
    s = str(dt_val).strip()
    if not s:
        return None
    try:
        if len(s) >= 10:
            return datetime.strptime(s[:10], "%Y-%m-%d").date()
    except Exception:
        pass
    return None


def _calculate_study_day(
    obs_dt: datetime | str | None, rfstdtc: str | None
) -> int | None:
    """Calculate CDISC study day relative to reference start date (RFSTDTC).

    Rule:
    If obs_date >= rfstdtc_date: day = (obs_date - rfstdtc_date).days + 1
    If obs_date < rfstdtc_date:  day = (obs_date - rfstdtc_date).days  (Day -1, Day -2, etc. No Day 0)
    """
    obs_d = _parse_date(obs_dt)
    rf_d = _parse_date(rfstdtc)
    if obs_d is None or rf_d is None:
        return None
    diff = (obs_d - rf_d).days
    return diff + 1 if diff >= 0 else diff


def _normalize_sex(val: Any) -> str:
    """Normalize sex string to CDISC CT (M, F, U)."""
    if not val:
        return "U"
    s = str(val).strip().upper()
    if s in ("M", "MALE", "1"):
        return "M"
    if s in ("F", "FEMALE", "2"):
        return "F"
    return "U"


def _normalize_race(val: Any) -> str:
    """Normalize race string to CDISC Controlled Terminology."""
    if not val:
        return "UNKNOWN"
    s = str(val).strip().upper()
    if "ASIAN" in s:
        return "ASIAN"
    if "BLACK" in s or "AFRICAN" in s:
        return "BLACK OR AFRICAN AMERICAN"
    if "WHITE" in s or "CAUCASIAN" in s:
        return "WHITE"
    if "HAWAIIAN" in s or "PACIFIC" in s:
        return "NATIVE HAWAIIAN OR OTHER PACIFIC ISLANDER"
    if "INDIAN" in s or "ALASKA" in s:
        return "AMERICAN INDIAN OR ALASKA NATIVE"
    if "MULTI" in s:
        return "MULTIPLE"
    return "OTHER"


def _normalize_severity(val: Any) -> str | None:
    """Normalize adverse event severity."""
    if not val:
        return None
    s = str(val).strip().upper()
    if "MILD" in s or s == "1":
        return "MILD"
    if "MOD" in s or s == "2":
        return "MODERATE"
    if "SEV" in s or s in ("3", "4", "5"):
        return "SEVERE"
    return s


def _normalize_seriousness(val: Any) -> str:
    """Normalize seriousness to Y or N."""
    if val is None:
        return "N"
    if isinstance(val, bool):
        return "Y" if val else "N"
    s = str(val).strip().upper()
    return "Y" if s in ("Y", "YES", "TRUE", "1", "SERIOUS") else "N"


def _normalize_causality(val: Any) -> str | None:
    """Normalize causality / relationship to treatment."""
    if not val:
        return None
    s = str(val).strip().upper()
    if "NOT" in s or "UNREL" in s or s == "NO":
        return "NOT RELATED"
    if "POSSIB" in s or "PROBAB" in s or "REL" in s or s == "YES":
        return "RELATED"
    return s


class SDTMPipelineEngine:
    """Automated transformation engine converting canonical clinical observations into 10 SDTM domains."""

    def __init__(self, study_id: str = "STUDY-001") -> None:
        self.study_id = study_id

    def build_sdtm_domains(
        self, records: list[CanonicalClinicalRecord]
    ) -> dict[str, list[BaseModel]]:
        """Transform all canonical clinical records into mapped SDTM domains."""
        # 1. Partition records by subject and domain
        records_by_subject: dict[str, list[CanonicalClinicalRecord]] = {}
        for r in records:
            subj = r.subject_id
            records_by_subject.setdefault(subj, []).append(r)

        # 2. Extract subject reference dates (RFSTDTC / First Exposure / First Visit)
        subject_rfstdtc_map: dict[str, str] = {}
        subject_demographics: dict[str, dict[str, Any]] = {}

        for subj, subj_recs in records_by_subject.items():
            # Search for DM records or EX records or earliest recorded observation
            ex_dates = []
            dm_info: dict[str, Any] = {
                "subjid": subj,
                "site_id": subj_recs[0].site_id or "SITE-01",
                "sex": "U",
                "race": "UNKNOWN",
                "ethnic": "NOT REPORTED",
                "birth_date": None,
                "age": None,
                "arm": "Arm A",
                "armcd": "ARM-A",
            }

            for r in subj_recs:
                p = r.raw_payload or {}

                # Check Demographics fields
                if (
                    r.domain == ObservationDomain.DM.value
                    or "DEMOG" in r.item_code.upper()
                ):
                    if "birth_date" in p or "BRTHDTC" in p or "dob" in p:
                        dm_info["birth_date"] = _format_iso_date(
                            p.get("birth_date") or p.get("BRTHDTC") or p.get("dob")
                        )
                    if "age" in p or "AGE" in p:
                        dm_info["age"] = p.get("age") or p.get("AGE")
                    if "sex" in p or "SEX" in p or "gender" in p:
                        dm_info["sex"] = _normalize_sex(
                            p.get("sex")
                            or p.get("SEX")
                            or p.get("gender")
                            or r.text_value
                        )
                    if "race" in p or "RACE" in p:
                        dm_info["race"] = _normalize_race(
                            p.get("race") or p.get("RACE") or r.text_value
                        )
                    if "arm" in p or "ARM" in p:
                        dm_info["arm"] = p.get("arm") or p.get("ARM")
                        dm_info["armcd"] = p.get("armcd") or p.get("ARMCD") or "ARM-A"

                # Check Exposure dates
                if (
                    r.domain == ObservationDomain.EX.value
                    or "DOSE" in r.item_code.upper()
                ):
                    st = (
                        p.get("start_date")
                        or p.get("EXSTDTC")
                        or _format_iso_date(r.observation_datetime)
                    )
                    if st:
                        ex_dates.append(st)

            # Assign RFSTDTC
            earliest_ex = (
                min(ex_dates)
                if ex_dates
                else _format_iso_date(subj_recs[0].observation_datetime)
            )
            subject_rfstdtc_map[subj] = earliest_ex or "2026-01-01"
            subject_demographics[subj] = dm_info

        # 3. Transform Each SDTM Domain
        dm_records = self._transform_dm(
            records_by_subject, subject_demographics, subject_rfstdtc_map
        )
        ae_records = self._transform_ae(records_by_subject, subject_rfstdtc_map)
        cm_records = self._transform_cm(records_by_subject, subject_rfstdtc_map)
        vs_records = self._transform_vs(records_by_subject, subject_rfstdtc_map)
        lb_records = self._transform_lb(records_by_subject, subject_rfstdtc_map)
        ex_records = self._transform_ex(records_by_subject, subject_rfstdtc_map)
        ds_records = self._transform_ds(records_by_subject, subject_rfstdtc_map)
        tu_records = self._transform_tu(records_by_subject, subject_rfstdtc_map)
        tr_records = self._transform_tr(records_by_subject, subject_rfstdtc_map)
        rs_records = self._transform_rs(records_by_subject, subject_rfstdtc_map)

        return {
            "DM": dm_records,
            "AE": ae_records,
            "CM": cm_records,
            "VS": vs_records,
            "LB": lb_records,
            "EX": ex_records,
            "DS": ds_records,
            "TU": tu_records,
            "TR": tr_records,
            "RS": rs_records,
        }

    def _transform_dm(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        demographics: dict[str, dict[str, Any]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMDM]:
        """Transform into Demographics (DM) domain."""
        results: list[SDTMDM] = []
        for subj in sorted(records_by_subject.keys()):
            info = demographics.get(subj, {})
            rfstdtc = rfstdtc_map.get(subj)
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )

            age = info.get("age")
            brthdtc = info.get("birth_date")
            if age is None and brthdtc and rfstdtc:
                b_date = _parse_date(brthdtc)
                r_date = _parse_date(rfstdtc)
                if b_date and r_date:
                    age = (
                        r_date.year
                        - b_date.year
                        - ((r_date.month, r_date.day) < (b_date.month, b_date.day))
                    )

            arm = info.get("arm") or "Experimental Arm 100mg"
            armcd = info.get("armcd") or "TRT-100"

            results.append(
                SDTMDM(
                    STUDYID=self.study_id,
                    DOMAIN="DM",
                    USUBJID=usubjid,
                    SUBJID=subj,
                    RFSTDTC=rfstdtc,
                    RFENDTC=None,
                    BRTHDTC=brthdtc,
                    AGE=age if age is not None else 45,
                    AGEU="YEARS",
                    SEX=info.get("sex") or "M",
                    RACE=info.get("race") or "WHITE",
                    ETHNIC=info.get("ethnic") or "NOT HISPANIC OR LATINO",
                    ARMCD=armcd,
                    ARM=arm,
                    ACTARMCD=armcd,
                    ACTARM=arm,
                    COUNTRY="USA",
                    DMDTC=rfstdtc,
                )
            )
        return results

    def _transform_ae(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMAE]:
        """Transform into Adverse Events (AE) domain."""
        results: list[SDTMAE] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            ae_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.AE.value or "AE" in r.item_code.upper()
            ]
            # Sort by datetime
            ae_recs.sort(key=lambda x: x.observation_datetime)

            for seq, r in enumerate(ae_recs, start=1):
                p = r.raw_payload or {}
                term = (
                    p.get("ae_term") or p.get("AETERM") or r.text_value or r.item_name
                )
                st_dtc = _format_iso_date(
                    p.get("start_date") or p.get("AESTDTC") or r.observation_datetime
                )
                end_dtc = _format_iso_date(p.get("end_date") or p.get("AEENDTC"))
                sev = _normalize_severity(p.get("severity") or p.get("AESEV"))
                ser = _normalize_seriousness(
                    p.get("serious") or p.get("AESER") or p.get("is_serious")
                )
                rel = _normalize_causality(
                    p.get("causality") or p.get("relationship") or p.get("AEREL")
                )
                outcome = p.get("outcome") or p.get("AEOUT") or "RECOVERED/RESOLVED"
                decod = p.get("preferred_term") or p.get("AEDECOD") or term
                bodsys = (
                    p.get("soc")
                    or p.get("AEBODSYS")
                    or "General disorders and administration site conditions"
                )

                results.append(
                    SDTMAE(
                        STUDYID=self.study_id,
                        DOMAIN="AE",
                        USUBJID=usubjid,
                        AESEQ=seq,
                        AETERM=str(term),
                        AEDECOD=str(decod),
                        AEBODSYS=str(bodsys),
                        AESEV=sev or "MILD",
                        AESER=ser,
                        AEREL=rel or "NOT RELATED",
                        AEOUT=str(outcome),
                        AESTDTC=st_dtc,
                        AEENDTC=end_dtc,
                        AESDTH=p.get("AESDTH", "N"),
                        AESHOSP=p.get("AESHOSP", "N"),
                        AESCONG=p.get("AESCONG", "N"),
                        AESLIFE=p.get("AESLIFE", "N"),
                        AESDISAB=p.get("AESDISAB", "N"),
                        AETOXGR=p.get(
                            "AETOXGR",
                            "1" if sev == "MILD" else "2" if sev == "MODERATE" else "3",
                        ),
                    )
                )
        return results

    def _transform_cm(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMCM]:
        """Transform into Concomitant Medications (CM) domain."""
        results: list[SDTMCM] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            cm_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.OTHER.value
                and "MED" in r.item_code.upper()
                or "CM" in r.item_code.upper()
                or "DRUG" in r.item_code.upper()
                or "CONCOMITANT" in r.item_name.upper()
            ]
            cm_recs.sort(key=lambda x: x.observation_datetime)

            for seq, r in enumerate(cm_recs, start=1):
                p = r.raw_payload or {}
                trt = (
                    p.get("medication_name")
                    or p.get("CMTRT")
                    or r.text_value
                    or r.item_name
                )
                st_dtc = _format_iso_date(
                    p.get("start_date") or p.get("CMSTDTC") or r.observation_datetime
                )
                end_dtc = _format_iso_date(p.get("end_date") or p.get("CMENDTC"))
                dose = p.get("dose") or p.get("CMDOSE") or r.numeric_value
                unit = p.get("dose_unit") or p.get("CMDOSEU") or r.unit or "mg"

                results.append(
                    SDTMCM(
                        STUDYID=self.study_id,
                        DOMAIN="CM",
                        USUBJID=usubjid,
                        CMSEQ=seq,
                        CMTRT=str(trt),
                        CMDECOD=p.get("CMDECOD") or str(trt).upper(),
                        CMCLAS=p.get("CMCLAS") or "ANALGESICS",
                        CMDOSE=float(dose) if dose is not None else None,
                        CMDOSEU=str(unit),
                        CMDOSFRQ=p.get("CMDOSFRQ") or "QD",
                        CMROUTE=p.get("CMROUTE") or "ORAL",
                        CMSTDTC=st_dtc,
                        CMENDTC=end_dtc,
                        CMENRF=p.get("CMENRF") or ("ONGOING" if not end_dtc else None),
                        CMINDC=p.get("CMINDC") or "Headache",
                    )
                )
        return results

    def _transform_vs(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMVS]:
        """Transform into Vital Signs (VS) domain."""
        results: list[SDTMVS] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            rfstdtc = rfstdtc_map.get(subj)
            vs_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.VS.value or "VS" in r.item_code.upper()
            ]
            vs_recs.sort(key=lambda x: x.observation_datetime)

            # Determine baseline record per test code
            baseline_seen_tests = set()

            for seq, r in enumerate(vs_recs, start=1):
                p = r.raw_payload or {}
                norm = r.normalized_attributes or {}
                dtc = _format_iso_date(r.observation_datetime)
                dy = _calculate_study_day(r.observation_datetime, rfstdtc)

                testcd = p.get("VSTESTCD") or r.item_code.upper().replace(
                    "VS_", ""
                ).replace("EDC_", "")
                if "SYS" in testcd:
                    testcd = "SYSBP"
                    testname = "Systolic Blood Pressure"
                elif "DIA" in testcd:
                    testcd = "DIABP"
                    testname = "Diastolic Blood Pressure"
                elif "PULSE" in testcd or "HR" in testcd:
                    testcd = "PULSE"
                    testname = "Pulse Rate"
                elif "TEMP" in testcd:
                    testcd = "TEMP"
                    testname = "Body Temperature"
                elif "WEIGHT" in testcd:
                    testcd = "WEIGHT"
                    testname = "Weight"
                elif "HEIGHT" in testcd:
                    testcd = "HEIGHT"
                    testname = "Height"
                else:
                    testname = r.item_name

                # Baseline flag: if study day <= 1 and test not seen yet
                is_baseline = (
                    dy is not None and dy <= 1 and testcd not in baseline_seen_tests
                )
                if is_baseline:
                    baseline_seen_tests.add(testcd)

                val_num = r.numeric_value
                val_char = str(val_num) if val_num is not None else (r.text_value or "")

                results.append(
                    SDTMVS(
                        STUDYID=self.study_id,
                        DOMAIN="VS",
                        USUBJID=usubjid,
                        VSSEQ=seq,
                        VSTESTCD=testcd,
                        VSTEST=testname,
                        VSCAT="VITAL SIGNS",
                        VSORRES=val_char,
                        VSORRESU=r.unit or "mmHg",
                        VSSTRESC=val_char,
                        VSSTRESN=val_num,
                        VSSTRESU=r.unit or "mmHg",
                        VSPOS=p.get("VSPOS") or "SITTING",
                        VSDTC=dtc,
                        VSDY=dy,
                        VISITNUM=norm.get("visit_num") or (1.0 if is_baseline else 2.0),
                        VISIT=norm.get("visit_id")
                        or ("Baseline" if is_baseline else "Visit 2"),
                        VSBLFL="Y" if is_baseline else None,
                    )
                )
        return results

    def _transform_lb(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMLB]:
        """Transform into Laboratory Findings (LB) domain."""
        results: list[SDTMLB] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            rfstdtc = rfstdtc_map.get(subj)
            lb_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.LB.value or "LB" in r.item_code.upper()
            ]
            lb_recs.sort(key=lambda x: x.observation_datetime)

            baseline_seen_tests = set()

            for seq, r in enumerate(lb_recs, start=1):
                p = r.raw_payload or {}
                norm = r.normalized_attributes or {}
                dtc = _format_iso_date(r.observation_datetime)
                dy = _calculate_study_day(r.observation_datetime, rfstdtc)

                testcd = p.get("LBTESTCD") or r.item_code.upper().replace(
                    "LB_", ""
                ).replace("LAB_", "")
                if "ALT" in testcd:
                    testcd = "ALT"
                    testname = "Alanine Aminotransferase"
                elif "AST" in testcd:
                    testcd = "AST"
                    testname = "Aspartate Aminotransferase"
                elif "BILI" in testcd:
                    testcd = "BILI"
                    testname = "Bilirubin"
                elif "GLUC" in testcd:
                    testcd = "GLUC"
                    testname = "Glucose"
                elif "CREAT" in testcd:
                    testcd = "CREAT"
                    testname = "Creatinine"
                elif "HEMO" in testcd or "HGB" in testcd:
                    testcd = "HGB"
                    testname = "Hemoglobin"
                else:
                    testname = r.item_name

                is_baseline = (
                    dy is not None and dy <= 1 and testcd not in baseline_seen_tests
                )
                if is_baseline:
                    baseline_seen_tests.add(testcd)

                val_num = r.numeric_value
                val_char = str(val_num) if val_num is not None else (r.text_value or "")

                nrind = r.abnormal_flag or p.get("LBNRIND")
                if not nrind and val_num is not None:
                    if r.reference_low is not None and val_num < r.reference_low:
                        nrind = "LOW"
                    elif r.reference_high is not None and val_num > r.reference_high:
                        nrind = "HIGH"
                    else:
                        nrind = "NORMAL"

                results.append(
                    SDTMLB(
                        STUDYID=self.study_id,
                        DOMAIN="LB",
                        USUBJID=usubjid,
                        LBSEQ=seq,
                        LBTESTCD=testcd,
                        LBTEST=testname,
                        LBCAT="CHEMISTRY",
                        LBORRES=val_char,
                        LBORRESU=r.unit or "U/L",
                        LBSTRESC=val_char,
                        LBSTRESN=val_num,
                        LBSTRESU=r.unit or "U/L",
                        LBNRIND=nrind,
                        LBORNRLO=str(r.reference_low)
                        if r.reference_low is not None
                        else None,
                        LBORNRHI=str(r.reference_high)
                        if r.reference_high is not None
                        else None,
                        LBSTNRLO=r.reference_low,
                        LBSTNRHI=r.reference_high,
                        LBDTC=dtc,
                        LBDY=dy,
                        VISITNUM=norm.get("visit_num") or (1.0 if is_baseline else 2.0),
                        VISIT=norm.get("visit_id")
                        or ("Baseline" if is_baseline else "Visit 2"),
                        LBBLFL="Y" if is_baseline else None,
                        LOINC=p.get("loinc") or norm.get("loinc"),
                    )
                )
        return results

    def _transform_ex(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMEX]:
        """Transform into Exposure (EX) domain."""
        results: list[SDTMEX] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            ex_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.EX.value
                or "EX" in r.item_code.upper()
                or "DOSE" in r.item_code.upper()
            ]
            ex_recs.sort(key=lambda x: x.observation_datetime)

            # If no EX record was ingested, generate default baseline exposure record from RFSTDTC
            if not ex_recs:
                rfstdtc = rfstdtc_map.get(subj) or "2026-01-01"
                results.append(
                    SDTMEX(
                        STUDYID=self.study_id,
                        DOMAIN="EX",
                        USUBJID=usubjid,
                        EXSEQ=1,
                        EXTRT="Investigational Product 100mg",
                        EXDOSE=100.0,
                        EXDOSU="mg",
                        EXDOSFRM="TABLET",
                        EXDOSFRQ="QD",
                        EXROUTE="ORAL",
                        EXSTDTC=rfstdtc,
                        EXENDTC=rfstdtc,
                        VISITNUM=1.0,
                        VISIT="Day 1 Treatment",
                        EXLOT="LOT-99214",
                    )
                )
            else:
                for seq, r in enumerate(ex_recs, start=1):
                    p = r.raw_payload or {}
                    norm = r.normalized_attributes or {}
                    st_dtc = _format_iso_date(
                        p.get("start_date")
                        or p.get("EXSTDTC")
                        or r.observation_datetime
                    )
                    end_dtc = _format_iso_date(
                        p.get("end_date") or p.get("EXENDTC") or r.observation_datetime
                    )
                    dose = p.get("dose") or p.get("EXDOSE") or r.numeric_value or 100.0
                    unit = p.get("dose_unit") or p.get("EXDOSU") or r.unit or "mg"

                    results.append(
                        SDTMEX(
                            STUDYID=self.study_id,
                            DOMAIN="EX",
                            USUBJID=usubjid,
                            EXSEQ=seq,
                            EXTRT=p.get("treatment_name")
                            or p.get("EXTRT")
                            or "Investigational Product",
                            EXDOSE=float(dose),
                            EXDOSU=str(unit),
                            EXDOSFRM=p.get("EXDOSFRM") or "TABLET",
                            EXDOSFRQ=p.get("EXDOSFRQ") or "QD",
                            EXROUTE=p.get("EXROUTE") or "ORAL",
                            EXSTDTC=st_dtc,
                            EXENDTC=end_dtc,
                            VISITNUM=norm.get("visit_num") or float(seq),
                            VISIT=norm.get("visit_id") or f"Dosing Visit {seq}",
                            EXLOT=p.get("lot_number") or p.get("EXLOT") or "LOT-99214",
                        )
                    )
        return results

    def _transform_ds(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMDS]:
        """Transform into Disposition (DS) domain."""
        results: list[SDTMDS] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            ds_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.DS.value
                or "DS" in r.item_code.upper()
                or "DISPOSITION" in r.item_name.upper()
            ]
            ds_recs.sort(key=lambda x: x.observation_datetime)

            if not ds_recs:
                rfstdtc = rfstdtc_map.get(subj) or "2026-01-01"
                results.append(
                    SDTMDS(
                        STUDYID=self.study_id,
                        DOMAIN="DS",
                        USUBJID=usubjid,
                        DSSEQ=1,
                        DSTERM="INFORMED CONSENT OBTAINED",
                        DSDECOD="INFORMED CONSENT OBTAINED",
                        DSCAT="PROTOCOL MILESTONE",
                        DSSCAT="ENROLLMENT",
                        DSSTDTC=rfstdtc,
                        EPOCH="SCREENING",
                    )
                )
            else:
                for seq, r in enumerate(ds_recs, start=1):
                    p = r.raw_payload or {}
                    term = (
                        p.get("disposition_term")
                        or p.get("DSTERM")
                        or r.text_value
                        or r.item_name
                    )
                    decod = p.get("standardized_term") or p.get("DSDECOD") or term
                    dtc = _format_iso_date(
                        p.get("date") or p.get("DSSTDTC") or r.observation_datetime
                    )

                    results.append(
                        SDTMDS(
                            STUDYID=self.study_id,
                            DOMAIN="DS",
                            USUBJID=usubjid,
                            DSSEQ=seq,
                            DSTERM=str(term),
                            DSDECOD=str(decod),
                            DSCAT=p.get("DSCAT") or "DISPOSITION EVENT",
                            DSSCAT=p.get("DSSCAT"),
                            DSSTDTC=dtc,
                            EPOCH=p.get("EPOCH") or "TREATMENT",
                        )
                    )
        return results

    def _transform_tu(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMTU]:
        """Transform into Tumor Identification (TU) domain."""
        results: list[SDTMTU] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            tu_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.TU.value
                or "TUMOR" in r.item_code.upper()
                and "LESION" in r.item_name.upper()
            ]
            tu_recs.sort(key=lambda x: x.observation_datetime)

            for seq, r in enumerate(tu_recs, start=1):
                p = r.raw_payload or {}
                norm = r.normalized_attributes or {}
                lnkid = p.get("lesion_id") or p.get("TULNKID") or f"L0{seq}"
                cl = (
                    p.get("lesion_type") or p.get("TUORRES") or r.text_value or "TARGET"
                )
                dtc = _format_iso_date(r.observation_datetime)

                results.append(
                    SDTMTU(
                        STUDYID=self.study_id,
                        DOMAIN="TU",
                        USUBJID=usubjid,
                        TUSEQ=seq,
                        TULNKID=str(lnkid),
                        TUTESTCD="TUMIDENT",
                        TUTEST="Tumor Identification",
                        TUORRES=str(cl).upper(),
                        TUSTRESC=str(cl).upper(),
                        TUMETHOD=norm.get("modality") or "CT SCAN",
                        TULOC=p.get("anatomical_site") or p.get("TULOC") or "LUNG",
                        VISITNUM=norm.get("visit_num") or float(seq),
                        VISIT=norm.get("timepoint") or "Screening Scan",
                        TUDTC=dtc,
                        TUEVAL="INDEPENDENT ASSESSOR",
                        TUEVALID=p.get("evaluator_id") or "RAD-001",
                    )
                )
        return results

    def _transform_tr(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMTR]:
        """Transform into Tumor Results / Measurements (TR) domain."""
        results: list[SDTMTR] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            tr_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.TR.value
                or "DIAMETER" in r.item_code.upper()
                or "LESION_MEASUREMENT" in r.item_name.upper()
            ]
            tr_recs.sort(key=lambda x: x.observation_datetime)

            for seq, r in enumerate(tr_recs, start=1):
                p = r.raw_payload or {}
                norm = r.normalized_attributes or {}
                lnkid = p.get("lesion_id") or p.get("TRLNKID") or f"L0{seq}"
                val_num = r.numeric_value or (
                    float(p.get("longest_diameter_mm"))
                    if "longest_diameter_mm" in p
                    else None
                )
                val_char = str(val_num) if val_num is not None else (r.text_value or "")
                dtc = _format_iso_date(r.observation_datetime)

                results.append(
                    SDTMTR(
                        STUDYID=self.study_id,
                        DOMAIN="TR",
                        USUBJID=usubjid,
                        TRSEQ=seq,
                        TRLNKID=str(lnkid),
                        TRTESTCD="LDIAM",
                        TRTEST="Longest Diameter",
                        TRORRES=val_char,
                        TRORRESU="mm",
                        TRSTRESC=val_char,
                        TRSTRESN=val_num,
                        TRSTRESU="mm",
                        TRMETHOD=norm.get("modality") or "CT SCAN",
                        VISITNUM=norm.get("visit_num") or float(seq),
                        VISIT=norm.get("timepoint") or f"Scan {seq}",
                        TRDTC=dtc,
                        TREVAL="INDEPENDENT ASSESSOR",
                        TREVALID=p.get("evaluator_id") or "RAD-001",
                    )
                )
        return results

    def _transform_rs(
        self,
        records_by_subject: dict[str, list[CanonicalClinicalRecord]],
        rfstdtc_map: dict[str, str],
    ) -> list[SDTMRS]:
        """Transform into Disease Response (RS) domain."""
        results: list[SDTMRS] = []
        for subj, recs in sorted(records_by_subject.items()):
            usubjid = (
                f"{self.study_id}-{subj}"
                if not subj.startswith(self.study_id)
                else subj
            )
            rs_recs = [
                r
                for r in recs
                if r.domain == ObservationDomain.RS.value
                or "RECIST" in r.item_code.upper()
                or "RESPONSE" in r.item_name.upper()
            ]
            rs_recs.sort(key=lambda x: x.observation_datetime)

            for seq, r in enumerate(rs_recs, start=1):
                p = r.raw_payload or {}
                norm = r.normalized_attributes or {}
                resp = (
                    p.get("overall_response")
                    or p.get("RSORRES")
                    or r.text_value
                    or "PR"
                )
                dtc = _format_iso_date(r.observation_datetime)

                results.append(
                    SDTMRS(
                        STUDYID=self.study_id,
                        DOMAIN="RS",
                        USUBJID=usubjid,
                        RSSEQ=seq,
                        RSTESTCD="OVRLRESP",
                        RSTEST="Overall Response",
                        RSORRES=str(resp).upper(),
                        RSSTRESC=str(resp).upper(),
                        RSMETHOD="RECIST 1.1",
                        VISITNUM=norm.get("visit_num") or float(seq),
                        VISIT=norm.get("timepoint") or f"Response Assessment {seq}",
                        RSDTC=dtc,
                        RSEVAL="INDEPENDENT ASSESSOR",
                        RSEVALID=p.get("adjudicator_id") or "RAD-001",
                        RSEVALSTAT=None,
                    )
                )
        return results
