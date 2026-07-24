"""
EDC-to-SDTM Mapper Module.

Provides stateless, rule-based mapping functions from EDC records
(ClinicalSubject, ClinicalVisit, and ClinicalObservation) to shared
strongly-typed SDTM models (DM, VS, LB, AE, CM) in packages/core-models/sdtm.
All computations are pure and run without database I/O.
"""

import re
from datetime import date, datetime
from typing import Any, Dict, List, Optional

# Import shared SDTM models and terminology normalizers
from sdtm.models import AE, CM, DM, LB, VS
from sdtm.terminology import (
    normalize_race,
    normalize_seriousness,
    normalize_severity,
    normalize_sex,
)


def _get_val(obj: Any, key: str, default: Any = None) -> Any:
    """
    Retrieves a value from a dictionary or an object attribute.
    Ensures safe attribute/key retrieval across all formats.
    """
    if obj is None:
        return default
    if isinstance(obj, dict):
        return obj.get(key, default)
    return getattr(obj, key, default)


def to_dtc(val: Any) -> Optional[str]:
    """
    Converts standard dates, datetimes, or validated strings into CDISC DTC format.
    Allows partial dates and full ISO 8601 timestamps, normalizing slashes.
    """
    if val is None:
        return None
    if isinstance(val, (datetime, date)):
        iso = val.isoformat()
        if iso.endswith("+00:00"):
            iso = iso[:-6] + "Z"
        return iso
    if isinstance(val, str):
        val_clean = val.strip()
        if not val_clean:
            return None
        # Normalize slashes commonly entered in EDC to hyphens
        val_clean = val_clean.replace("/", "-")
        return val_clean
    return str(val)


def get_demographics(subject: Any) -> Dict[str, Any]:
    """
    Retrieves and decrypts the demographics dictionary from a ClinicalSubject.
    Uses the secure helper from apps.execution.demographics if encrypted.
    """
    demographics = _get_val(subject, "demographics")
    if not demographics:
        enc = _get_val(subject, "encrypted_demographics")
        if enc:
            try:
                from apps.execution.demographics import decrypt_demographics

                demographics = decrypt_demographics(enc)
            except Exception:
                pass
    if isinstance(demographics, dict):
        return demographics
    elif hasattr(demographics, "model_dump"):
        return demographics.model_dump()
    elif hasattr(demographics, "dict"):
        return demographics.dict()
    return {}


def compute_age(rfstdtc: Optional[str], brthdtc: Optional[str]) -> Optional[int]:
    """
    Derives AGE from RFSTDTC and BRTHDTC when source precision supports it.
    Uses month and day comparison for full precision, falling back to year subtraction.
    """
    if not rfstdtc or not brthdtc:
        return None

    # Match YYYY-MM-DD at the start of strings
    match_rf = re.match(r"^(\d{4})-(\d{2})-(\d{2})", rfstdtc.strip())
    match_br = re.match(r"^(\d{4})-(\d{2})-(\d{2})", brthdtc.strip())

    if match_rf and match_br:
        try:
            rf_y, rf_m, rf_d = map(int, match_rf.groups())
            br_y, br_m, br_d = map(int, match_br.groups())

            rf_date = date(rf_y, rf_m, rf_d)
            br_date = date(br_y, br_m, br_d)

            # exact completed years
            age = (
                rf_date.year
                - br_date.year
                - ((rf_date.month, rf_date.day) < (br_date.month, br_date.day))
            )
            if age >= 0:
                return age
        except Exception:
            pass

    # Fallback to simple year difference if partial precision is used
    match_rf_y = re.match(r"^(\d{4})", rfstdtc.strip())
    match_br_y = re.match(r"^(\d{4})", brthdtc.strip())
    if match_rf_y and match_br_y:
        try:
            rf_y = int(match_rf_y.group(1))
            br_y = int(match_br_y.group(1))
            age = rf_y - br_y
            if age >= 0:
                return age
        except Exception:
            pass

    return None


def _get_obs_date(obs: Any, visits_by_id: Dict[str, Any]) -> Optional[str]:
    """
    Gets DTC format date for observation, falling back to visit date if missing.
    """
    obs_dt = (
        _get_val(obs, "observation_date")
        or _get_val(obs, "vsdtc")
        or _get_val(obs, "lbdtc")
        or _get_val(obs, "aestdtc")
        or _get_val(obs, "cmstdtc")
    )
    if not obs_dt:
        v_id = _get_val(obs, "visit_id")
        if v_id and v_id in visits_by_id:
            visit = visits_by_id[v_id]
            obs_dt = _get_val(visit, "visit_date") or _get_val(visit, "date")
    return to_dtc(obs_dt)


def parse_float(val: Any) -> Optional[float]:
    """
    Safely parses any numeric or string representation into a float.
    """
    if val is None:
        return None
    if isinstance(val, (int, float)):
        return float(val)
    try:
        return float(str(val).strip())
    except ValueError:
        return None


def normalize_aerel(val: Optional[str]) -> Optional[str]:
    """
    Normalizes treatment relationship string to match AERelationship enum.
    """
    if val is None:
        return None
    cleaned = str(val).strip().upper().replace("_", " ")
    if cleaned in {"RELATED", "Y", "YES"}:
        return "RELATED"
    if cleaned in {"NOT RELATED", "N", "NO"}:
        return "NOT RELATED"
    if cleaned in {"POSSIBLY RELATED", "POSSIBLY", "POSSIBLE"}:
        return "POSSIBLY RELATED"
    return cleaned


def normalize_aeout(val: Optional[str]) -> Optional[str]:
    """
    Normalizes outcome string to match AEOutcome enum.
    """
    if val is None:
        return None
    cleaned = str(val).strip().upper().replace("_", " ").replace("/", " ")
    if "RECOVERED" in cleaned and "SEQUELAE" in cleaned:
        return "RECOVERED/RESOLVED WITH SEQUELAE"
    if "RECOVERED" in cleaned or "RESOLVED" in cleaned:
        return "RECOVERED/RESOLVED"
    if "RECOVERING" in cleaned or "RESOLVING" in cleaned:
        return "RECOVERING/RESOLVING"
    if "NOT RECOVERED" in cleaned or "NOT RESOLVED" in cleaned:
        return "NOT RECOVERED/NOT RESOLVED"
    if "FATAL" in cleaned:
        return "FATAL"
    if "UNKNOWN" in cleaned:
        return "UNKNOWN"
    return cleaned


def map_dm(
    subjects: List[Any],
    visits: List[Any],
    observations: List[Any],
    created_by: str = "system",
    reason_for_change: str = "Automated EDC-to-SDTM DM mapping",
    **kwargs,
) -> List[DM]:
    """
    Stateless rule-based mapping from EDC sources to SDTM DM (Demographics) records.
    """
    # Build a dictionary of visits for easy reference date resolution
    visits_by_id = {}
    for v in visits:
        v_id = _get_val(v, "id") or _get_val(v, "visit_id")
        if v_id:
            visits_by_id[v_id] = v

    # Group observations by subject_id
    obs_by_subject: Dict[str, List[Any]] = {}
    for o in observations:
        sub_id = _get_val(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    dm_records: List[DM] = []

    for s in subjects:
        sub_id = _get_val(s, "subject_id")
        if not sub_id:
            continue

        study_id = _get_val(s, "study_id") or ""
        demographics = get_demographics(s)

        site_id = (
            _get_val(s, "site_id")
            or demographics.get("site_id")
            or demographics.get("siteID")
            or "001"
        )
        usubjid = (
            _get_val(s, "usubjid")
            or _get_val(s, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        sub_obs = obs_by_subject.get(sub_id, [])

        # 4. RFSTDTC (First exposure date)
        rfstdtc = None
        ex_obs = [
            o
            for o in sub_obs
            if str(_get_val(o, "domain")).upper() == "EX"
            or str(_get_val(o, "test_code")).upper() in {"EXSTDTC", "RFSTDTC"}
        ]
        if ex_obs:
            dates = []
            for o in ex_obs:
                val_str = _get_val(o, "value_string")
                obs_dt = _get_obs_date(o, visits_by_id)
                if val_str:
                    dates.append(str(val_str).strip())
                elif obs_dt:
                    dates.append(obs_dt)
            if dates:
                rfstdtc = min(dates)

        if not rfstdtc:
            rfstdtc = (
                _get_val(s, "rfstdtc")
                or _get_val(s, "RFSTDTC")
                or demographics.get("rfstdtc")
                or demographics.get("RFSTDTC")
            )

        # 5. RFENDTC (Last disposition / exposure date)
        rfendtc = None
        ds_obs = [
            o
            for o in sub_obs
            if str(_get_val(o, "domain")).upper() == "DS"
            or str(_get_val(o, "test_code")).upper() in {"DSSTDTC", "RFENDTC"}
        ]
        if ds_obs:
            dates = []
            for o in ds_obs:
                val_str = _get_val(o, "value_string")
                obs_dt = _get_obs_date(o, visits_by_id)
                if val_str:
                    dates.append(str(val_str).strip())
                elif obs_dt:
                    dates.append(obs_dt)
            if dates:
                rfendtc = max(dates)

        if not rfendtc:
            rfendtc = (
                _get_val(s, "rfendtc")
                or _get_val(s, "RFENDTC")
                or demographics.get("rfendtc")
                or demographics.get("RFENDTC")
            )

        # 6. BRTHDTC
        brthdtc = (
            demographics.get("birthdate")
            or demographics.get("birth_date")
            or demographics.get("date_of_birth")
            or demographics.get("dob")
            or demographics.get("BRTHDTC")
        )
        if not brthdtc:
            brth_obs = [
                o for o in sub_obs if str(_get_val(o, "test_code")).upper() == "BRTHDTC"
            ]
            if brth_obs:
                brthdtc = _get_val(brth_obs[0], "value_string") or _get_obs_date(
                    brth_obs[0], visits_by_id
                )

        # Formats to proper DTC string
        rfstdtc_str = to_dtc(rfstdtc)
        rfendtc_str = to_dtc(rfendtc)
        brthdtc_str = to_dtc(brthdtc)

        # 7. AGE & AGEU
        age = compute_age(rfstdtc_str, brthdtc_str)
        ageu = "YEARS"

        # 8. SEX
        raw_sex = (
            demographics.get("gender")
            or demographics.get("sex")
            or demographics.get("SEX")
        )
        if not raw_sex:
            sex_obs = [
                o for o in sub_obs if str(_get_val(o, "test_code")).upper() == "SEX"
            ]
            if sex_obs:
                raw_sex = _get_val(sex_obs[0], "value_string")
        if not raw_sex:
            raw_sex = "U"
        sex_val = normalize_sex(raw_sex)

        # 9. RACE
        raw_race = demographics.get("race") or demographics.get("RACE")
        if not raw_race:
            race_obs = [
                o for o in sub_obs if str(_get_val(o, "test_code")).upper() == "RACE"
            ]
            if race_obs:
                raw_race = _get_val(race_obs[0], "value_string") or _get_val(
                    race_obs[0], "value"
                )
                # Handle lists stored inside observation values
                if isinstance(raw_race, list):
                    pass
                elif raw_race and str(raw_race).startswith("["):
                    import json as py_json

                    try:
                        raw_race = py_json.loads(str(raw_race))
                    except Exception:
                        pass
        if not raw_race:
            raw_race = "OTHER"
        race_val = normalize_race(raw_race)

        # 10. ARM
        arm = demographics.get("arm") or demographics.get("ARM")
        if not arm:
            arm_obs = [
                o
                for o in sub_obs
                if str(_get_val(o, "test_code")).upper() in {"ARM", "ACTARM"}
            ]
            if arm_obs:
                arm = _get_val(arm_obs[0], "value_string")
        if not arm:
            arm = "SCREEN FAILURE"

        # Construct and validate DM record
        record = DM(
            STUDYID=study_id,
            DOMAIN="DM",
            USUBJID=usubjid,
            SUBJID=sub_id,
            RFSTDTC=rfstdtc_str,
            RFENDTC=rfendtc_str,
            BRTHDTC=brthdtc_str,
            AGE=age,
            AGEU=ageu,
            SEX=sex_val,
            RACE=race_val,
            ARM=arm,
            created_by=created_by,
            reason_for_change=reason_for_change,
            **kwargs,
        )
        dm_records.append(record)

    return dm_records


def map_vs(
    subjects: List[Any],
    visits: List[Any],
    observations: List[Any],
    created_by: str = "system",
    reason_for_change: str = "Automated EDC-to-SDTM VS mapping",
    **kwargs,
) -> List[VS]:
    """
    Stateless rule-based mapping from EDC sources to SDTM VS (Vital Signs) records.
    """
    visits_by_id = {}
    for v in visits:
        v_id = _get_val(v, "id") or _get_val(v, "visit_id")
        if v_id:
            visits_by_id[v_id] = v

    # Group subjects for quick lookup
    subjects_by_id = {}
    for s in subjects:
        sub_id = _get_val(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    vs_records: List[VS] = []

    # Map observations for VS domain
    vs_obs = [o for o in observations if str(_get_val(o, "domain")).upper() == "VS"]

    # Process per subject to ensure deterministic monotonic sequencing per subject
    obs_by_subject: Dict[str, List[Any]] = {}
    for o in vs_obs:
        sub_id = _get_val(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        if not subj:
            continue

        study_id = _get_val(subj, "study_id") or ""
        demographics = get_demographics(subj)
        site_id = _get_val(subj, "site_id") or demographics.get("site_id") or "001"
        usubjid = (
            _get_val(subj, "usubjid")
            or _get_val(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Sort observations deterministically by: timing variable VSDTC, then test_code, then test_name
        # to ensure deterministic monotonic sequence numbers.
        sorted_obs_with_dates = []
        for o in sub_obs:
            dtc = _get_obs_date(o, visits_by_id)
            sorted_obs_with_dates.append((dtc or "", o))

        # Stable sort on (dtc_str, test_code, id)
        sorted_obs_with_dates.sort(
            key=lambda item: (
                item[0],
                str(_get_val(item[1], "test_code") or "").upper(),
                str(_get_val(item[1], "id") or id(item[1])),
            )
        )

        for seq, (dtc, o) in enumerate(sorted_obs_with_dates, start=1):
            val_num = _get_val(o, "value")
            val_str = _get_val(o, "value_string")

            # VSORRES can be the original float or int
            vsorres = val_num
            if vsorres is None and val_str is not None:
                vsorres = parse_float(val_str)

            # Standardized results use the persisted normalized data rather than recalculated conversions.
            vsstresn = _get_val(o, "normalized_value")
            vsstresu = _get_val(o, "normalized_unit")

            # Standardized character format
            if vsstresn is not None:
                vsstresc = str(vsstresn)
            else:
                vsstresc = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else None)
                )

            vspos = _get_val(o, "position") or _get_val(o, "vspos")
            vsblfl = _get_val(o, "baseline_flag") or _get_val(o, "vsblfl")

            record = VS(
                STUDYID=study_id,
                DOMAIN="VS",
                USUBJID=usubjid,
                VSSEQ=seq,
                VSTESTCD=_get_val(o, "test_code"),
                VSTEST=_get_val(o, "test_name"),
                VSORRES=vsorres,
                VSORRESU=_get_val(o, "unit"),
                VSSTRESC=vsstresc,
                VSSTRESN=vsstresn,
                VSSTRESU=vsstresu,
                VSPOS=vspos,
                VSDTC=dtc,
                VSBLFL=vsblfl,
                created_by=created_by,
                reason_for_change=reason_for_change,
                **kwargs,
            )
            vs_records.append(record)

    return vs_records


def map_lb(
    subjects: List[Any],
    visits: List[Any],
    observations: List[Any],
    created_by: str = "system",
    reason_for_change: str = "Automated EDC-to-SDTM LB mapping",
    **kwargs,
) -> List[LB]:
    """
    Stateless rule-based mapping from EDC sources to SDTM LB (Laboratory Findings) records.
    """
    visits_by_id = {}
    for v in visits:
        v_id = _get_val(v, "id") or _get_val(v, "visit_id")
        if v_id:
            visits_by_id[v_id] = v

    # Group subjects for quick lookup
    subjects_by_id = {}
    for s in subjects:
        sub_id = _get_val(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    lb_records: List[LB] = []

    # Map observations for LB domain
    lb_obs = [o for o in observations if str(_get_val(o, "domain")).upper() == "LB"]

    # Process per subject
    obs_by_subject: Dict[str, List[Any]] = {}
    for o in lb_obs:
        sub_id = _get_val(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        if not subj:
            continue

        study_id = _get_val(subj, "study_id") or ""
        demographics = get_demographics(subj)
        site_id = _get_val(subj, "site_id") or demographics.get("site_id") or "001"
        usubjid = (
            _get_val(subj, "usubjid")
            or _get_val(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Sort observations deterministically by: timing variable LBDTC, then test_code, then test_name
        sorted_obs_with_dates = []
        for o in sub_obs:
            dtc = _get_obs_date(o, visits_by_id)
            sorted_obs_with_dates.append((dtc or "", o))

        sorted_obs_with_dates.sort(
            key=lambda item: (
                item[0],
                str(_get_val(item[1], "test_code") or "").upper(),
                str(_get_val(item[1], "id") or id(item[1])),
            )
        )

        for seq, (dtc, o) in enumerate(sorted_obs_with_dates, start=1):
            val_str = _get_val(o, "value_string")
            val_num = _get_val(o, "value")

            # LBORRES is character format
            lborres = (
                val_str
                if val_str is not None
                else (str(val_num) if val_num is not None else None)
            )

            # Sourced standardized results directly from normalized_value/normalized_unit
            lbstresn = _get_val(o, "normalized_value")
            lbstresu = _get_val(o, "normalized_unit")

            if lbstresn is not None:
                lbstresc = str(lbstresn)
            else:
                lbstresc = lborres

            lbnrind = (
                _get_val(o, "lab_indicator")
                or _get_val(o, "lbnrind")
                or _get_val(o, "indicator")
            )
            lbloinc = _get_val(o, "lbloinc") or _get_val(o, "loinc")

            record = LB(
                STUDYID=study_id,
                DOMAIN="LB",
                USUBJID=usubjid,
                LBSEQ=seq,
                LBTESTCD=_get_val(o, "test_code"),
                LBTEST=_get_val(o, "test_name"),
                LBORRES=lborres,
                LBORRESU=_get_val(o, "unit"),
                LBSTRESC=lbstresc,
                LBSTRESN=lbstresn,
                LBSTRESU=lbstresu,
                LBNRIND=lbnrind,
                LBDTC=dtc,
                LBLOINC=lbloinc,
                created_by=created_by,
                reason_for_change=reason_for_change,
                **kwargs,
            )
            lb_records.append(record)

    return lb_records


def map_ae(
    subjects: List[Any],
    visits: List[Any],
    observations: List[Any],
    created_by: str = "system",
    reason_for_change: str = "Automated EDC-to-SDTM AE mapping",
    **kwargs,
) -> List[AE]:
    """
    Stateless rule-based mapping from EDC sources to SDTM AE (Adverse Events) records.
    Supports both grouped CDASH structures (multi-record observations with matching page_id)
    and flat observations.
    """
    visits_by_id = {}
    for v in visits:
        v_id = _get_val(v, "id") or _get_val(v, "visit_id")
        if v_id:
            visits_by_id[v_id] = v

    # Group subjects
    subjects_by_id = {}
    for s in subjects:
        sub_id = _get_val(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    ae_records: List[AE] = []

    # Map observations for AE domain
    ae_obs = [o for o in observations if str(_get_val(o, "domain")).upper() == "AE"]

    # Process per subject
    obs_by_subject: Dict[str, List[Any]] = {}
    for o in ae_obs:
        sub_id = _get_val(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        if not subj:
            continue

        study_id = _get_val(subj, "study_id") or ""
        demographics = get_demographics(subj)
        site_id = _get_val(subj, "site_id") or demographics.get("site_id") or "001"
        usubjid = (
            _get_val(subj, "usubjid")
            or _get_val(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Group observations by event (page_id or observation_date)
        groups: Dict[str, List[Any]] = {}
        for o in sub_obs:
            page_id = _get_val(o, "page_id")
            if page_id is not None and str(page_id).strip() != "":
                group_key = f"page_{page_id}"
            else:
                dtc = _get_obs_date(o, visits_by_id)
                if dtc:
                    group_key = f"date_{dtc}"
                else:
                    group_key = f"uniq_{id(o)}"
            groups.setdefault(group_key, []).append(o)

        subject_events = []

        for group_key, group_obs in groups.items():
            # Check if any observation has direct AE attributes (flat mapping fallback)
            is_flat = False
            for o in group_obs:
                if (
                    _get_val(o, "aeterm") is not None
                    or _get_val(o, "AETERM") is not None
                    or _get_val(o, "aestdtc") is not None
                ):
                    is_flat = True
                    break

            if is_flat:
                # Process each observation as a separate flat AE
                for o in group_obs:
                    dtc = _get_obs_date(o, visits_by_id)
                    aeterm = (
                        _get_val(o, "aeterm")
                        or _get_val(o, "AETERM")
                        or _get_val(o, "value_string")
                        or _get_val(o, "test_name")
                    )
                    aeloc = _get_val(o, "aeloc") or _get_val(o, "AELOC")
                    aeldtc = _get_val(o, "aeldtc") or _get_val(o, "AELDTC")
                    aestdtc = _get_val(o, "aestdtc") or _get_val(o, "AESTDTC") or dtc
                    aeendtc = _get_val(o, "aeendtc") or _get_val(o, "AEENDTC")
                    aesev = _get_val(o, "aesev") or _get_val(o, "AESEV")
                    aeser = _get_val(o, "aeser") or _get_val(o, "AESER") or "N"
                    aerel = _get_val(o, "aerel") or _get_val(o, "AEREL")
                    aeout = _get_val(o, "aeout") or _get_val(o, "AEOUT")

                    subject_events.append(
                        {
                            "AETERM": aeterm,
                            "AELOC": aeloc,
                            "AELDTC": to_dtc(aeldtc),
                            "AESTDTC": to_dtc(aestdtc),
                            "AEENDTC": to_dtc(aeendtc),
                            "AESEV": normalize_severity(aesev) if aesev else None,
                            "AESER": normalize_seriousness(aeser),
                            "AEREL": normalize_aerel(aerel),
                            "AEOUT": normalize_aeout(aeout),
                            "id": _get_val(o, "id") or id(o),
                        }
                    )
            else:
                # standard CDASH grouped structure
                event_data: Dict[str, Any] = {
                    "AETERM": None,
                    "AELOC": None,
                    "AELDTC": None,
                    "AESTDTC": None,
                    "AEENDTC": None,
                    "AESEV": None,
                    "AESER": "N",
                    "AEREL": None,
                    "AEOUT": None,
                    "id": group_key,
                }

                # Find earliest observation date as fallback for AESTDTC
                dates = []
                for o in group_obs:
                    dtc = _get_obs_date(o, visits_by_id)
                    if dtc:
                        dates.append(dtc)

                for o in group_obs:
                    tcode = str(_get_val(o, "test_code") or "").upper()
                    val_str = _get_val(o, "value_string")
                    val_num = _get_val(o, "value")
                    val = (
                        val_str
                        if val_str is not None
                        else (str(val_num) if val_num is not None else "")
                    )

                    if tcode in {"AETERM", "AEVERB", "TERM"}:
                        event_data["AETERM"] = val
                    elif tcode in {"AELOC", "LOC"}:
                        event_data["AELOC"] = val
                    elif tcode == "AELDTC":
                        event_data["AELDTC"] = to_dtc(val)
                    elif tcode in {"AESTDTC", "STDTC"}:
                        event_data["AESTDTC"] = to_dtc(val)
                    elif tcode in {"AEENDTC", "ENDTC"}:
                        event_data["AEENDTC"] = to_dtc(val)
                    elif tcode in {"AESEV", "SEV"}:
                        event_data["AESEV"] = normalize_severity(val) if val else None
                    elif tcode in {"AESER", "SER"}:
                        event_data["AESER"] = normalize_seriousness(val)
                    elif tcode in {"AEREL", "REL"}:
                        event_data["AEREL"] = normalize_aerel(val)
                    elif tcode in {"AEOUT", "OUT"}:
                        event_data["AEOUT"] = normalize_aeout(val)

                # Robust Fallbacks for mandatory values
                if not event_data["AETERM"]:
                    # Fallback to test_name of first observation if AETERM is missing
                    fallback_term = ""
                    for o in group_obs:
                        fallback_term = (
                            _get_val(o, "test_name") or _get_val(o, "test_code") or ""
                        )
                        if fallback_term:
                            break
                    event_data["AETERM"] = fallback_term

                if not event_data["AESTDTC"]:
                    event_data["AESTDTC"] = min(dates) if dates else None

                subject_events.append(event_data)

        # Sort per-subject deterministically by timing variable AESTDTC, then AETERM
        subject_events.sort(
            key=lambda item: (
                item.get("AESTDTC") or "",
                str(item.get("AETERM") or "").upper(),
                str(item.get("id") or ""),
            )
        )

        for seq, item in enumerate(subject_events, start=1):
            record = AE(
                STUDYID=study_id,
                DOMAIN="AE",
                USUBJID=usubjid,
                AESEQ=seq,
                AETERM=item["AETERM"],
                AELOC=item["AELOC"],
                AELDTC=item["AELDTC"],
                AESTDTC=item["AESTDTC"],
                AEENDTC=item["AEENDTC"],
                AESEV=item["AESEV"],
                AESER=item["AESER"],
                AEREL=item["AEREL"],
                AEOUT=item["AEOUT"],
                created_by=created_by,
                reason_for_change=reason_for_change,
                **kwargs,
            )
            ae_records.append(record)

    return ae_records


def map_cm(
    subjects: List[Any],
    visits: List[Any],
    observations: List[Any],
    created_by: str = "system",
    reason_for_change: str = "Automated EDC-to-SDTM CM mapping",
    **kwargs,
) -> List[CM]:
    """
    Stateless rule-based mapping from EDC sources to SDTM CM (Concomitant Medications) records.
    Supports both grouped CDASH structures (multi-record observations with matching page_id)
    and flat observations.
    """
    visits_by_id = {}
    for v in visits:
        v_id = _get_val(v, "id") or _get_val(v, "visit_id")
        if v_id:
            visits_by_id[v_id] = v

    # Group subjects
    subjects_by_id = {}
    for s in subjects:
        sub_id = _get_val(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    cm_records: List[CM] = []

    # Map observations for CM domain
    cm_obs = [o for o in observations if str(_get_val(o, "domain")).upper() == "CM"]

    # Process per subject
    obs_by_subject: Dict[str, List[Any]] = {}
    for o in cm_obs:
        sub_id = _get_val(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        if not subj:
            continue

        study_id = _get_val(subj, "study_id") or ""
        demographics = get_demographics(subj)
        site_id = _get_val(subj, "site_id") or demographics.get("site_id") or "001"
        usubjid = (
            _get_val(subj, "usubjid")
            or _get_val(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Group observations by event (page_id or observation_date)
        groups: Dict[str, List[Any]] = {}
        for o in sub_obs:
            page_id = _get_val(o, "page_id")
            if page_id is not None and str(page_id).strip() != "":
                group_key = f"page_{page_id}"
            else:
                dtc = _get_obs_date(o, visits_by_id)
                if dtc:
                    group_key = f"date_{dtc}"
                else:
                    group_key = f"uniq_{id(o)}"
            groups.setdefault(group_key, []).append(o)

        subject_events = []

        for group_key, group_obs in groups.items():
            # Check if any observation has direct CM attributes (flat mapping fallback)
            is_flat = False
            for o in group_obs:
                if (
                    _get_val(o, "cmtrt") is not None
                    or _get_val(o, "CMTRT") is not None
                    or _get_val(o, "cmstdtc") is not None
                ):
                    is_flat = True
                    break

            if is_flat:
                for o in group_obs:
                    dtc = _get_obs_date(o, visits_by_id)
                    cmtrt = (
                        _get_val(o, "cmtrt")
                        or _get_val(o, "CMTRT")
                        or _get_val(o, "value_string")
                        or _get_val(o, "test_name")
                    )
                    cmdecod = _get_val(o, "cmdecod") or _get_val(o, "CMDECOD")
                    cmclas = _get_val(o, "cmclas") or _get_val(o, "CMCLAS")
                    cmdose = (
                        _get_val(o, "cmdose")
                        or _get_val(o, "CMDOSE")
                        or _get_val(o, "value")
                    )
                    cmdoseu = (
                        _get_val(o, "cmdoseu")
                        or _get_val(o, "CMDOSEU")
                        or _get_val(o, "unit")
                    )
                    cmdosfrq = _get_val(o, "cmdosfrq") or _get_val(o, "CMDOSFRQ")
                    cmroute = _get_val(o, "cmroute") or _get_val(o, "CMROUTE")
                    cmstdtc = _get_val(o, "cmstdtc") or _get_val(o, "CMSTDTC") or dtc
                    cmendtc = _get_val(o, "cmendtc") or _get_val(o, "CMENDTC")

                    subject_events.append(
                        {
                            "CMTRT": cmtrt,
                            "CMDECOD": cmdecod,
                            "CMCLAS": cmclas,
                            "CMDOSE": parse_float(cmdose)
                            if cmdose is not None
                            else None,
                            "CMDOSEU": cmdoseu,
                            "CMDOSFRQ": cmdosfrq,
                            "CMROUTE": cmroute,
                            "CMSTDTC": to_dtc(cmstdtc),
                            "CMENDTC": to_dtc(cmendtc),
                            "id": _get_val(o, "id") or id(o),
                        }
                    )
            else:
                # Grouped CDASH
                event_data: Dict[str, Any] = {
                    "CMTRT": None,
                    "CMDECOD": None,
                    "CMCLAS": None,
                    "CMDOSE": None,
                    "CMDOSEU": None,
                    "CMDOSFRQ": None,
                    "CMROUTE": None,
                    "CMSTDTC": None,
                    "CMENDTC": None,
                    "id": group_key,
                }

                dates = []
                for o in group_obs:
                    dtc = _get_obs_date(o, visits_by_id)
                    if dtc:
                        dates.append(dtc)

                for o in group_obs:
                    tcode = str(_get_val(o, "test_code") or "").upper()
                    val_str = _get_val(o, "value_string")
                    val_num = _get_val(o, "value")
                    val = (
                        val_str
                        if val_str is not None
                        else (str(val_num) if val_num is not None else "")
                    )

                    if tcode in {"CMTRT", "MEDNAME"}:
                        event_data["CMTRT"] = val
                    elif tcode in {"CMDECOD", "DECOD"}:
                        event_data["CMDECOD"] = val
                    elif tcode in {"CMCLAS", "CLASS"}:
                        event_data["CMCLAS"] = val
                    elif tcode == "CMDOSE":
                        event_data["CMDOSE"] = parse_float(val) if val else None
                    elif tcode in {"CMDOSEU", "CMUNIT", "UNIT"}:
                        event_data["CMDOSEU"] = val
                    elif tcode in {"CMDOSFRQ", "CMFREQ", "FREQ"}:
                        event_data["CMDOSFRQ"] = val
                    elif tcode in {"CMROUTE", "ROUTE"}:
                        event_data["CMROUTE"] = val
                    elif tcode in {"CMSTDTC", "STDTC"}:
                        event_data["CMSTDTC"] = to_dtc(val)
                    elif tcode in {"CMENDTC", "ENDTC"}:
                        event_data["CMENDTC"] = to_dtc(val)

                # Fallback for mandatory fields
                if not event_data["CMTRT"]:
                    fallback_term = ""
                    for o in group_obs:
                        fallback_term = (
                            _get_val(o, "test_name") or _get_val(o, "test_code") or ""
                        )
                        if fallback_term:
                            break
                    event_data["CMTRT"] = fallback_term

                if not event_data["CMSTDTC"]:
                    event_data["CMSTDTC"] = min(dates) if dates else None

                subject_events.append(event_data)

        # Sort per-subject deterministically by timing variable CMSTDTC, then CMTRT
        subject_events.sort(
            key=lambda item: (
                item.get("CMSTDTC") or "",
                str(item.get("CMTRT") or "").upper(),
                str(item.get("id") or ""),
            )
        )

        for seq, item in enumerate(subject_events, start=1):
            record = CM(
                STUDYID=study_id,
                DOMAIN="CM",
                USUBJID=usubjid,
                CMSEQ=seq,
                CMTRT=item["CMTRT"],
                CMDECOD=item["CMDECOD"],
                CMCLAS=item["CMCLAS"],
                CMDOSE=item["CMDOSE"],
                CMDOSEU=item["CMDOSEU"],
                CMDOSFRQ=item["CMDOSFRQ"],
                CMROUTE=item["CMROUTE"],
                CMSTDTC=item["CMSTDTC"],
                CMENDTC=item["CMENDTC"],
                created_by=created_by,
                reason_for_change=reason_for_change,
                **kwargs,
            )
            cm_records.append(record)

    return cm_records


def map_to_sdtm(
    domain: str,
    subjects: List[Any],
    visits: List[Any],
    observations: List[Any],
    created_by: str = "system",
    reason_for_change: Optional[str] = None,
    **kwargs,
) -> List[Any]:
    """
    Central dispatcher orchestrating standard SDTM mappings by domain string.
    """
    domain_upper = str(domain).strip().upper()
    if reason_for_change is None:
        reason_for_change = f"Automated EDC-to-SDTM {domain_upper} mapping"

    if domain_upper == "DM":
        return map_dm(
            subjects, visits, observations, created_by, reason_for_change, **kwargs
        )
    elif domain_upper == "VS":
        return map_vs(
            subjects, visits, observations, created_by, reason_for_change, **kwargs
        )
    elif domain_upper == "LB":
        return map_lb(
            subjects, visits, observations, created_by, reason_for_change, **kwargs
        )
    elif domain_upper == "AE":
        return map_ae(
            subjects, visits, observations, created_by, reason_for_change, **kwargs
        )
    elif domain_upper == "CM":
        return map_cm(
            subjects, visits, observations, created_by, reason_for_change, **kwargs
        )
    else:
        raise ValueError(f"SDTM mapping domain '{domain}' is not supported.")
