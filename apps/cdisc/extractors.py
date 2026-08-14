import contextlib
import re
from datetime import date, datetime
from typing import Any

from apps.cdisc.demographics import decrypt_demographics as decrypt_demographics
from apps.cdisc.models import SUPPRecord
from apps.cdisc.terminology import normalize_race, normalize_sex


def calculate_age(rfstdtc: str | None, brthdtc: str | None) -> int | None:
    """Derive AGE from RFSTDTC and BRTHDTC where source precision supports the calculation.

    Requires full YYYY-MM-DD precision for both dates.
    """
    if not rfstdtc or not brthdtc:
        return None

    # Match YYYY-MM-DD at the start of strings
    match_rf = re.match(r"^(\d{4})-(\d{2})-(\d{2})", rfstdtc.strip())
    match_br = re.match(r"^(\d{4})-(\d{2})-(\d{2})", brthdtc.strip())

    if not match_rf or not match_br:
        return None

    try:
        rf_y, rf_m, rf_d = map(int, match_rf.groups())
        br_y, br_m, br_d = map(int, match_br.groups())

        rf_date = date(rf_y, rf_m, rf_d)
        br_date = date(br_y, br_m, br_d)

        delta_days = (rf_date - br_date).days
        if delta_days < 0:
            return None
        return int(delta_days // 365.25)
    except Exception:
        return None


def get_value(obj: Any, key: str, default: Any = None) -> Any:
    """Helper to retrieve value from dict or object attribute."""
    if isinstance(obj, dict):
        return obj.get(key, default)
    return getattr(obj, key, default)


def get_demographics(subject: Any) -> dict:
    """Extracts demographics dict from subject (handling encryption and structures)."""
    demographics = get_value(subject, "demographics")
    if not demographics:
        enc = get_value(subject, "encrypted_demographics")
        if enc:
            demographics = decrypt_demographics(enc)
    if isinstance(demographics, dict):
        return demographics
    if hasattr(demographics, "model_dump"):
        return demographics.model_dump()
    if hasattr(demographics, "dict"):
        return demographics.dict()
    return {}


def get_subject_rfstdtc(subj: Any, all_observations: list[Any]) -> str | None:
    """Retrieves or derives the RFSTDTC for a subject."""
    sub_id = get_value(subj, "subject_id")
    if not sub_id:
        return None

    # 1. Look in subject properties or demographics
    demographics = get_demographics(subj)
    rfstdtc = (
        get_value(subj, "rfstdtc")
        or get_value(subj, "RFSTDTC")
        or demographics.get("rfstdtc")
        or demographics.get("RFSTDTC")
    )
    if rfstdtc:
        return str(rfstdtc).strip()

    # 2. Look in EX observations
    ex_obs = [
        o
        for o in all_observations
        if get_value(o, "subject_id") == sub_id
        and (
            str(get_value(o, "domain")).upper() == "EX"
            or str(get_value(o, "test_code")).upper() in {"EXSTDTC", "RFSTDTC"}
        )
    ]
    if ex_obs:
        dates = []
        for o in ex_obs:
            val_str = get_value(o, "value_string")
            obs_dt = get_value(o, "observation_date")
            if val_str:
                dates.append(str(val_str).strip())
            elif obs_dt:
                if hasattr(obs_dt, "isoformat"):
                    dates.append(obs_dt.isoformat())
                else:
                    dates.append(str(obs_dt))
        if dates:
            return min(dates)
    return None


def extract_dm(
    subjects: list[Any],
    observations: list[Any] | None = None,
) -> list[dict[str, Any]]:
    """Pure extractor for SDTM Demographics (DM) dataset from in-memory source models.

    Args:
        subjects: List of clinical subject records (objects or dicts).
        observations: Optional list of clinical observation records (objects or dicts).

    Returns:
        List[Dict[str, Any]]: List of mapped DM domain dictionaries.
    """
    obs_list = observations or []

    # Pre-group observations by subject_id for fast lookup
    obs_by_subject: dict[str, list[Any]] = {}
    for obs in obs_list:
        sub_id = get_value(obs, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(obs)

    dm_records = []

    for subj in subjects:
        sub_id = get_value(subj, "subject_id")
        if not sub_id:
            continue

        study_id = get_value(subj, "study_id") or ""
        demographics = get_demographics(subj)

        # 1. STUDYID
        # 2. SUBJID
        # 3. USUBJID
        site_id = (
            get_value(subj, "site_id")
            or demographics.get("site_id")
            or demographics.get("siteID")
            or "001"
        )
        usubjid = (
            get_value(subj, "usubjid")
            or get_value(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        sub_obs = obs_by_subject.get(sub_id, [])

        # 4. RFSTDTC (First treatment exposure)
        rfstdtc = None
        ex_obs = [
            o
            for o in sub_obs
            if str(get_value(o, "domain")).upper() == "EX"
            or str(get_value(o, "test_code")).upper() in {"EXSTDTC", "RFSTDTC"}
        ]
        if ex_obs:
            dates = []
            for o in ex_obs:
                val_str = get_value(o, "value_string")
                obs_dt = get_value(o, "observation_date")
                if val_str:
                    dates.append(str(val_str).strip())
                elif obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        dates.append(obs_dt.isoformat())
                    else:
                        dates.append(str(obs_dt))
            if dates:
                rfstdtc = min(dates)

        if not rfstdtc:
            rfstdtc = (
                get_value(subj, "rfstdtc")
                or get_value(subj, "RFSTDTC")
                or demographics.get("rfstdtc")
                or demographics.get("RFSTDTC")
            )

        # 5. RFENDTC (Last study exposure or completion)
        rfendtc = None
        ds_obs = [
            o
            for o in sub_obs
            if str(get_value(o, "domain")).upper() == "DS"
            or str(get_value(o, "test_code")).upper() in {"DSSTDTC", "RFENDTC"}
        ]
        if ds_obs:
            dates = []
            for o in ds_obs:
                val_str = get_value(o, "value_string")
                obs_dt = get_value(o, "observation_date")
                if val_str:
                    dates.append(str(val_str).strip())
                elif obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        dates.append(obs_dt.isoformat())
                    else:
                        dates.append(str(obs_dt))
            if dates:
                rfendtc = max(dates)

        if not rfendtc:
            rfendtc = (
                get_value(subj, "rfendtc")
                or get_value(subj, "RFENDTC")
                or demographics.get("rfendtc")
                or demographics.get("RFENDTC")
            )

        # 6. BRTHDTC
        brthdtc = (
            demographics.get("birthdate")
            or demographics.get("birth_date")
            or demographics.get("BRTHDTC")
        )
        if not brthdtc:
            brth_obs = [
                o
                for o in sub_obs
                if str(get_value(o, "test_code")).upper() == "BRTHDTC"
            ]
            if brth_obs:
                brthdtc = get_value(brth_obs[0], "value_string")

        # 7. AGE & AGEU
        age = calculate_age(rfstdtc, brthdtc)
        ageu = "YEARS"

        # 8. SEX
        raw_sex = (
            demographics.get("gender")
            or demographics.get("sex")
            or demographics.get("SEX")
        )
        if not raw_sex:
            sex_obs = [
                o for o in sub_obs if str(get_value(o, "test_code")).upper() == "SEX"
            ]
            if sex_obs:
                raw_sex = get_value(sex_obs[0], "value_string")
        if not raw_sex:
            raw_sex = "U"
        sex = normalize_sex(raw_sex)

        # 9. RACE
        raw_race = demographics.get("race") or demographics.get("RACE")
        if not raw_race:
            race_obs = [
                o for o in sub_obs if str(get_value(o, "test_code")).upper() == "RACE"
            ]
            if race_obs:
                raw_race = get_value(race_obs[0], "value_string")
        if not raw_race:
            raw_race = "OTHER"
        race = normalize_race(raw_race)

        # 10. ARM
        arm = demographics.get("arm") or demographics.get("ARM")
        if not arm:
            arm_obs = [
                o
                for o in sub_obs
                if str(get_value(o, "test_code")).upper() in {"ARM", "ACTARM"}
            ]
            if arm_obs:
                arm = get_value(arm_obs[0], "value_string")
        if not arm:
            arm = "SCREEN FAILURE"

        record = {
            "STUDYID": study_id,
            "USUBJID": usubjid,
            "SUBJID": sub_id,
            "RFSTDTC": rfstdtc or "",
            "RFENDTC": rfendtc or "",
            "BRTHDTC": brthdtc or "",
            "AGE": age,
            "AGEU": ageu,
            "SEX": sex,
            "RACE": race,
            "ARM": arm,
        }
        dm_records.append(record)

    return dm_records


def extract_mh(
    subjects: list[Any],
    observations: list[Any],
) -> tuple[list[dict[str, Any]], list[SUPPRecord]]:
    """Pure extractor for SDTM Medical History (MH) dataset and SUPPMH records.

    Args:
        subjects: List of clinical subject records (objects or dicts).
        observations: List of clinical observations of the MH domain (objects or dicts).

    Returns:
        Tuple[List[Dict[str, Any]], List[SUPPRecord]]:
            - List of MH SDTM dictionaries.
            - List of SUPPRecord models for unmapped qualifier fields.
    """
    # 1. Index subjects by subject_id for fast lookup
    subjects_by_id = {}
    for s in subjects:
        sub_id = get_value(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    # 2. Filter observations for MH domain
    mh_obs = [o for o in observations if str(get_value(o, "domain")).upper() == "MH"]

    # 3. Group MH observations by subject_id
    obs_by_subject: dict[str, list[Any]] = {}
    for o in mh_obs:
        sub_id = get_value(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    mh_records: list[dict[str, Any]] = []
    supp_records: list[SUPPRecord] = []

    # 4. For each subject, process and sequence their MH events
    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        study_id = get_value(subj, "study_id") if subj else ""
        demographics = get_demographics(subj) if subj else {}

        site_id = (
            get_value(subj, "site_id")
            or demographics.get("site_id")
            or demographics.get("siteID")
            or "001"
        )
        usubjid = (
            get_value(subj, "usubjid")
            or get_value(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Group observations by page_id (or observation_date if page_id is missing/None)
        # to identify a single medical history record/form submission
        groups: dict[str, list[Any]] = {}
        for o in sub_obs:
            page_id = get_value(o, "page_id")
            if page_id:
                group_key = f"page_{page_id}"
            else:
                obs_dt = get_value(o, "observation_date")
                if obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        group_key = f"date_{obs_dt.isoformat()}"
                    else:
                        group_key = f"date_{str(obs_dt)}"
                else:
                    group_key = f"uniq_{id(o)}"
            groups.setdefault(group_key, []).append(o)

        subject_mh_records = []

        for group_key, group_obs in groups.items():
            record_data = {
                "STUDYID": study_id,
                "USUBJID": usubjid,
                "SUBJID": sub_id,
            }

            # Map the standard fields
            unmapped_observations = []
            for o in group_obs:
                tcode = str(get_value(o, "test_code")).upper()
                val_str = get_value(o, "value_string")
                val_num = get_value(o, "value")
                val = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else "")
                )

                if tcode == "MHTERM":
                    record_data["MHTERM"] = val
                elif tcode == "MHDECOD":
                    record_data["MHDECOD"] = val
                elif tcode == "MHBODSYS":
                    record_data["MHBODSYS"] = val
                elif tcode in {"MHSTDTC", "MHDTC"}:
                    record_data["MHSTDTC"] = val
                elif tcode == "MHENDTC":
                    record_data["MHENDTC"] = val
                elif tcode == "MHENRTP":
                    record_data["MHENRTP"] = val
                else:
                    # Non-standard/unmapped qualifier field
                    unmapped_observations.append(o)

            # Ensure minimal requirements: MHTERM should be present
            if "MHTERM" not in record_data:
                # Fallback to test_name of first observation if MHTERM is missing
                fallback_term = ""
                for o in group_obs:
                    fallback_term = (
                        get_value(o, "test_name") or get_value(o, "test_code") or ""
                    )
                    if fallback_term:
                        break
                record_data["MHTERM"] = fallback_term

            # Save the unmapped observations list with the record data temporarily
            # so we can generate SUPPMH records after sorting and sequencing
            record_data["_unmapped_obs"] = unmapped_observations
            subject_mh_records.append(record_data)

        # 5. Stable per-subject sequencing based on MHSTDTC (onset date field), then MHTERM
        subject_mh_records.sort(
            key=lambda r: (
                r.get("MHSTDTC") or "",
                r.get("MHTERM") or "",
            )
        )

        # 6. Assign MHSEQ and generate SUPPMH records
        for seq, rec in enumerate(subject_mh_records, start=1):
            rec["MHSEQ"] = seq
            unmapped_obs = rec.pop("_unmapped_obs", [])

            # Generate SUPPMH records for unmapped content
            for o in unmapped_obs:
                qname = str(get_value(o, "test_code"))
                qlabel = str(get_value(o, "test_name") or qname)
                val_str = get_value(o, "value_string")
                val_num = get_value(o, "value")
                qval = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else "")
                )

                supp = SUPPRecord(
                    STUDYID=study_id,
                    RDOMAIN="MH",
                    USUBJID=usubjid,
                    IDVAR="MHSEQ",
                    IDVARVAL=str(seq),
                    QNAM=qname,
                    QLABEL=qlabel,
                    QVAL=qval,
                )
                supp_records.append(supp)

            # Clean and finalize standard MH fields in record
            final_record = {
                "STUDYID": rec["STUDYID"],
                "USUBJID": rec["USUBJID"],
                "SUBJID": rec["SUBJID"],
                "MHSEQ": rec["MHSEQ"],
                "MHTERM": rec.get("MHTERM") or "",
                "MHDECOD": rec.get("MHDECOD") or "",
                "MHBODSYS": rec.get("MHBODSYS") or "",
                "MHSTDTC": rec.get("MHSTDTC") or "",
                "MHENDTC": rec.get("MHENDTC") or "",
                "MHENRTP": rec.get("MHENRTP") or "",
            }
            mh_records.append(final_record)

    return mh_records, supp_records


def extract_ae(
    subjects: list[Any],
    observations: list[Any],
) -> tuple[list[dict[str, Any]], list[SUPPRecord]]:
    """Pure extractor for SDTM Adverse Events (AE) dataset and SUPPAE records.

    Args:
        subjects: List of clinical subject records (objects or dicts).
        observations: List of clinical observations of the AE domain (objects or dicts).

    Returns:
        Tuple[List[Dict[str, Any]], List[SUPPRecord]]:
            - List of AE SDTM dictionaries.
            - List of SUPPRecord models for unmapped qualifier fields and AEENGRY.
    """
    from apps.cdisc.terminology import normalize_severity

    # 1. Index subjects by subject_id for fast lookup
    subjects_by_id = {}
    for s in subjects:
        sub_id = get_value(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    # 2. Filter observations for AE domain
    ae_obs = [o for o in observations if str(get_value(o, "domain")).upper() == "AE"]

    # 3. Group AE observations by subject_id
    obs_by_subject: dict[str, list[Any]] = {}
    for o in ae_obs:
        sub_id = get_value(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    ae_records: list[dict[str, Any]] = []
    supp_records: list[SUPPRecord] = []

    # 4. For each subject, process and sequence their AE events
    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        study_id = get_value(subj, "study_id") if subj else ""
        demographics = get_demographics(subj) if subj else {}

        site_id = (
            get_value(subj, "site_id")
            or demographics.get("site_id")
            or demographics.get("siteID")
            or "001"
        )
        usubjid = (
            get_value(subj, "usubjid")
            or get_value(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Group observations by page_id (or observation_date if page_id is missing/None)
        # to identify a single adverse event record/form submission
        groups: dict[str, list[Any]] = {}
        for o in sub_obs:
            page_id = get_value(o, "page_id")
            if page_id:
                group_key = f"page_{page_id}"
            else:
                obs_dt = get_value(o, "observation_date")
                if obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        group_key = f"date_{obs_dt.isoformat()}"
                    else:
                        group_key = f"date_{str(obs_dt)}"
                else:
                    group_key = f"uniq_{id(o)}"
            groups.setdefault(group_key, []).append(o)

        subject_ae_records = []

        for group_key, group_obs in groups.items():
            record_data = {
                "STUDYID": study_id,
                "USUBJID": usubjid,
                "SUBJID": sub_id,
            }

            # Map the standard fields
            unmapped_observations = []
            is_ongoing = False

            for o in group_obs:
                tcode = str(get_value(o, "test_code")).upper()
                val_str = get_value(o, "value_string")
                val_num = get_value(o, "value")
                val = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else "")
                )

                if tcode == "AETERM":
                    record_data["AETERM"] = val
                elif tcode == "AELOC":
                    record_data["AELOC"] = val
                elif tcode == "AELDTC":
                    record_data["AELDTC"] = val
                elif tcode in {"AESTDTC", "AEDTC"}:
                    record_data["AESTDTC"] = val
                elif tcode == "AEENDTC":
                    record_data["AEENDTC"] = val
                elif tcode == "AESEV":
                    # Normalize severity using the controlled terminology mapping
                    record_data["AESEV"] = normalize_severity(val)
                elif tcode == "AESER":
                    # Serious Adverse Event flag: 'Y' or 'N'
                    cleaned_ser = str(val).strip().upper()
                    if cleaned_ser in {"Y", "YES", "TRUE", "1"}:
                        record_data["AESER"] = "Y"
                    else:
                        record_data["AESER"] = "N"
                elif tcode == "AEREL":
                    record_data["AEREL"] = val
                elif tcode == "AEOUT":
                    record_data["AEOUT"] = val
                    if "RESOLVING" in val.upper() or "RECOVERING" in val.upper():
                        is_ongoing = True
                elif tcode in {"AEONGO", "AEENGRY", "ONGOING"}:
                    cleaned_val = str(val).strip().upper()
                    if cleaned_val in {"Y", "YES", "TRUE", "ONGOING", "1"}:
                        is_ongoing = True
                else:
                    unmapped_observations.append(o)

            # Check if AEENDTC indicates ongoing or missing
            end_date = record_data.get("AEENDTC", "")
            if not end_date or is_ongoing:
                is_ongoing = True
                record_data["AEENDTC"] = ""

            record_data["_is_ongoing"] = is_ongoing

            # Ensure minimal requirements: AETERM should be present
            if "AETERM" not in record_data:
                fallback_term = ""
                for o in group_obs:
                    fallback_term = (
                        get_value(o, "test_name") or get_value(o, "test_code") or ""
                    )
                    if fallback_term:
                        break
                record_data["AETERM"] = fallback_term

            record_data["_unmapped_obs"] = unmapped_observations
            subject_ae_records.append(record_data)

        # 5. Stable per-subject sequencing based on AESTDTC (onset date field), then AETERM
        subject_ae_records.sort(
            key=lambda r: (
                r.get("AESTDTC") or "",
                r.get("AETERM") or "",
            )
        )

        # 6. Assign AESEQ and generate SUPPAE records
        for seq, rec in enumerate(subject_ae_records, start=1):
            rec["AESEQ"] = seq
            unmapped_obs = rec.pop("_unmapped_obs", [])
            is_ongoing = rec.pop("_is_ongoing", False)

            # If ongoing, generate SUPPAE record for AEENGRY
            if is_ongoing:
                supp_ong = SUPPRecord(
                    STUDYID=study_id,
                    RDOMAIN="AE",
                    USUBJID=usubjid,
                    IDVAR="AESEQ",
                    IDVARVAL=str(seq),
                    QNAM="AEENGRY",
                    QLABEL="Ongoing Status",
                    QVAL="ONGOING",
                )
                supp_records.append(supp_ong)

            # Generate SUPPAE records for unmapped content
            for o in unmapped_obs:
                qname = str(get_value(o, "test_code"))
                qlabel = str(get_value(o, "test_name") or qname)
                val_str = get_value(o, "value_string")
                val_num = get_value(o, "value")
                qval = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else "")
                )

                supp = SUPPRecord(
                    STUDYID=study_id,
                    RDOMAIN="AE",
                    USUBJID=usubjid,
                    IDVAR="AESEQ",
                    IDVARVAL=str(seq),
                    QNAM=qname,
                    QLABEL=qlabel,
                    QVAL=qval,
                )
                supp_records.append(supp)

            # Clean and finalize standard AE fields in record
            final_record = {
                "STUDYID": rec["STUDYID"],
                "USUBJID": rec["USUBJID"],
                "SUBJID": rec["SUBJID"],
                "AESEQ": rec["AESEQ"],
                "AETERM": rec.get("AETERM") or "",
                "AELOC": rec.get("AELOC") or "",
                "AELDTC": rec.get("AELDTC") or "",
                "AESTDTC": rec.get("AESTDTC") or "",
                "AEENDTC": rec.get("AEENDTC") or "",
                "AESEV": rec.get("AESEV") or "",
                "AESER": rec.get("AESER") or "N",
                "AEREL": rec.get("AEREL") or "",
                "AEOUT": rec.get("AEOUT") or "",
            }
            ae_records.append(final_record)

    return ae_records, supp_records


def extract_vs(
    subjects: list[Any],
    observations: list[Any],
) -> tuple[list[dict[str, Any]], list[SUPPRecord]]:
    """Pure extractor for SDTM Vital Signs (VS) dataset and SUPPVS records.

    Args:
        subjects: List of clinical subject records (objects or dicts).
        observations: List of clinical observations of the VS domain (objects or dicts).

    Returns:
        Tuple[List[Dict[str, Any]], List[SUPPRecord]]:
            - List of VS SDTM dictionaries.
            - List of SUPPRecord models for unmapped qualifier fields.
    """
    # 1. Index subjects by subject_id for fast lookup
    subjects_by_id = {}
    for s in subjects:
        sub_id = get_value(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    # 2. Filter observations for VS domain
    vs_obs = [o for o in observations if str(get_value(o, "domain")).upper() == "VS"]

    # 3. Group VS observations by subject_id
    obs_by_subject: dict[str, list[Any]] = {}
    for o in vs_obs:
        sub_id = get_value(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    vs_records: list[dict[str, Any]] = []
    supp_records: list[SUPPRecord] = []

    # 4. For each subject, process and sequence their VS records
    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        study_id = get_value(subj, "study_id") if subj else ""
        demographics = get_demographics(subj) if subj else {}

        site_id = (
            get_value(subj, "site_id")
            or demographics.get("site_id")
            or demographics.get("siteID")
            or "001"
        )
        usubjid = (
            get_value(subj, "usubjid")
            or get_value(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Retrieve RFSTDTC using the robust helper
        rfstdtc = get_subject_rfstdtc(subj, observations) if subj else None

        # Group observations by page_id (or observation_date + visit_id if page_id is missing/None)
        # to identify a single vital sign assessment form session
        groups: dict[str, list[Any]] = {}
        for o in sub_obs:
            page_id = get_value(o, "page_id")
            if page_id:
                group_key = f"page_{page_id}"
            else:
                obs_dt = get_value(o, "observation_date")
                visit_id = get_value(o, "visit_id") or ""
                dt_str = ""
                if obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        dt_str = obs_dt.isoformat()
                    else:
                        dt_str = str(obs_dt)
                group_key = f"grp_{dt_str}_{visit_id}"
            groups.setdefault(group_key, []).append(o)

        subject_vs_records = []

        for group_key, group_obs in groups.items():
            # Find any qualifiers like VSPOS on this page/group
            vspos = None
            for o in group_obs:
                tcode = str(get_value(o, "test_code")).upper()
                if tcode == "VSPOS":
                    val_str = get_value(o, "value_string")
                    val_num = get_value(o, "value")
                    vspos = (
                        val_str
                        if val_str is not None
                        else (str(val_num) if val_num is not None else None)
                    )

            # For each measurement observation (not VSPOS) on this page
            for o in group_obs:
                tcode = str(get_value(o, "test_code")).upper()
                if tcode == "VSPOS":
                    continue

                val_num = get_value(o, "value")
                val_str = get_value(o, "value_string")

                # VSORRES: preserve verbatim numeric result (as int or float if possible)
                vsorres = val_num if val_num is not None else None
                if vsorres is None and val_str is not None:
                    with contextlib.suppress(ValueError):
                        vsorres = float(val_str)

                vsorresu = get_value(o, "unit")
                vsstresn = get_value(o, "normalized_value")
                vsstresu = get_value(o, "normalized_unit")

                # Fallback to original values if standardized are missing
                if vsstresn is None:
                    vsstresn = vsorres
                if vsstresu is None:
                    vsstresu = vsorresu

                # VSSTRESC: standardized result as character string
                if vsstresn is not None:
                    if isinstance(vsstresn, float) and vsstresn.is_integer():
                        vsstresc = str(int(vsstresn))
                    else:
                        vsstresc = str(vsstresn)
                else:
                    vsstresc = val_str or ""

                obs_dt = get_value(o, "observation_date")
                dtc_str = ""
                if obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        dtc_str = obs_dt.isoformat()
                    else:
                        dtc_str = str(obs_dt)

                pos = vspos or get_value(o, "vspos") or get_value(o, "VSPOS")

                record_data = {
                    "STUDYID": study_id,
                    "USUBJID": usubjid,
                    "SUBJID": sub_id,
                    "VSTESTCD": tcode,
                    "VSTEST": get_value(o, "test_name") or tcode,
                    "VSORRES": vsorres,
                    "VSORRESU": vsorresu or "",
                    "VSSTRESC": vsstresc,
                    "VSSTRESN": vsstresn,
                    "VSSTRESU": vsstresu or "",
                    "VSPOS": pos or "",
                    "VSDTC": dtc_str,
                    "VSBLFL": "",
                    "_unmapped_obs": [],
                }
                subject_vs_records.append(record_data)

        # 5. Stable per-subject sequencing based on VSDTC, then VSTESTCD
        subject_vs_records.sort(
            key=lambda r: (
                r.get("VSDTC") or "",
                r.get("VSTESTCD") or "",
            )
        )

        # 6. Apply baseline vital-sign logic to produce VSBLFL according to the blueprint rule
        by_testcd: dict[str, list[dict[str, Any]]] = {}
        for r in subject_vs_records:
            by_testcd.setdefault(r["VSTESTCD"], []).append(r)

        for testcd, recs in by_testcd.items():
            eligible_recs = []
            for r in recs:
                dtc = r.get("VSDTC") or ""
                if r.get("VSSTRESN") is not None or r.get("VSORRES") is not None:
                    if rfstdtc:
                        if dtc and dtc <= rfstdtc:
                            eligible_recs.append((dtc, r))
                    else:
                        eligible_recs.append((dtc, r))

            if eligible_recs:
                eligible_recs.sort(key=lambda x: x[0])
                baseline_rec = eligible_recs[-1][1]
                baseline_rec["VSBLFL"] = "Y"

        # 7. Assign VSSEQ and generate SUPPVS records
        for seq, rec in enumerate(subject_vs_records, start=1):
            rec["VSSEQ"] = seq
            unmapped_obs = rec.pop("_unmapped_obs", [])

            for o in unmapped_obs:
                qname = str(get_value(o, "test_code"))
                qlabel = str(get_value(o, "test_name") or qname)
                val_str = get_value(o, "value_string")
                val_num = get_value(o, "value")
                qval = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else "")
                )

                supp = SUPPRecord(
                    STUDYID=study_id,
                    RDOMAIN="VS",
                    USUBJID=usubjid,
                    IDVAR="VSSEQ",
                    IDVARVAL=str(seq),
                    QNAM=qname,
                    QLABEL=qlabel,
                    QVAL=qval,
                )
                supp_records.append(supp)

            final_record = {
                "STUDYID": rec["STUDYID"],
                "USUBJID": rec["USUBJID"],
                "SUBJID": rec["SUBJID"],
                "VSSEQ": rec["VSSEQ"],
                "VSTESTCD": rec["VSTESTCD"],
                "VSTEST": rec["VSTEST"],
                "VSORRES": rec["VSORRES"],
                "VSORRESU": rec["VSORRESU"],
                "VSSTRESC": rec["VSSTRESC"],
                "VSSTRESN": rec["VSSTRESN"],
                "VSSTRESU": rec["VSSTRESU"],
                "VSPOS": rec["VSPOS"],
                "VSDTC": rec["VSDTC"],
                "VSBLFL": rec["VSBLFL"] or "",
            }
            vs_records.append(final_record)

    return vs_records, supp_records


def extract_lb(
    subjects: list[Any],
    observations: list[Any],
) -> tuple[list[dict[str, Any]], list[SUPPRecord]]:
    """Pure extractor for SDTM Laboratory Findings (LB) dataset and SUPPLB records.

    Args:
        subjects: List of clinical subject records (objects or dicts).
        observations: List of clinical observations of the LB domain (objects or dicts).

    Returns:
        Tuple[List[Dict[str, Any]], List[SUPPRecord]]:
            - List of LB SDTM dictionaries.
            - List of SUPPRecord models for unmapped qualifier fields.
    """
    # 1. Index subjects by subject_id for fast lookup
    subjects_by_id = {}
    for s in subjects:
        sub_id = get_value(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    # 2. Filter observations for LB domain
    lb_obs = [o for o in observations if str(get_value(o, "domain")).upper() == "LB"]

    # 3. Group LB observations by subject_id
    obs_by_subject: dict[str, list[Any]] = {}
    for o in lb_obs:
        sub_id = get_value(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    lb_records: list[dict[str, Any]] = []
    supp_records: list[SUPPRecord] = []

    # 4. For each subject, process and sequence their LB records
    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        study_id = get_value(subj, "study_id") if subj else ""
        demographics = get_demographics(subj) if subj else {}

        site_id = (
            get_value(subj, "site_id")
            or demographics.get("site_id")
            or demographics.get("siteID")
            or "001"
        )
        usubjid = (
            get_value(subj, "usubjid")
            or get_value(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Group observations by page_id (or observation_date + visit_id if page_id is missing/None)
        # to identify a single lab panel/form session
        groups: dict[str, list[Any]] = {}
        for o in sub_obs:
            page_id = get_value(o, "page_id")
            if page_id:
                group_key = f"page_{page_id}"
            else:
                obs_dt = get_value(o, "observation_date")
                visit_id = get_value(o, "visit_id") or ""
                dt_str = ""
                if obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        dt_str = obs_dt.isoformat()
                    else:
                        dt_str = str(obs_dt)
                group_key = f"grp_{dt_str}_{visit_id}"
            groups.setdefault(group_key, []).append(o)

        subject_lb_records = []

        for group_key, group_obs in groups.items():
            for o in group_obs:
                tcode = str(get_value(o, "test_code")).upper()

                val_num = get_value(o, "value")
                val_str = get_value(o, "value_string")

                # LBORRES: verbatim original result (character)
                lborres = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else "")
                )

                lborresu = get_value(o, "unit")
                lbstresn = get_value(o, "normalized_value")
                lbstresu = get_value(o, "normalized_unit")

                # Fallback to original values if standardized are missing
                if lbstresn is None and val_num is not None:
                    lbstresn = val_num
                if lbstresu is None:
                    lbstresu = lborresu

                # LBSTRESC: standardized result as character string
                if lbstresn is not None:
                    if isinstance(lbstresn, float) and lbstresn.is_integer():
                        lbstresc = str(int(lbstresn))
                    else:
                        lbstresc = str(lbstresn)
                else:
                    lbstresc = lborres

                # LBNRIND: normal range indicator
                lbnrind = (
                    get_value(o, "lab_indicator")
                    or get_value(o, "LBNRIND")
                    or get_value(o, "lbnrind")
                )
                if not lbnrind:
                    out_of_range = get_value(o, "lab_out_of_range") or get_value(
                        o, "is_out_of_range"
                    )
                    if out_of_range is True:
                        lbnrind = "OUT OF RANGE"
                    elif out_of_range is False:
                        lbnrind = "NORMAL"

                obs_dt = get_value(o, "observation_date")
                dtc_str = ""
                if obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        dtc_str = obs_dt.isoformat()
                    else:
                        dtc_str = str(obs_dt)

                lbloinc = get_value(o, "lbloinc") or get_value(o, "LBLOINC") or ""

                record_data = {
                    "STUDYID": study_id,
                    "USUBJID": usubjid,
                    "SUBJID": sub_id,
                    "LBTESTCD": tcode,
                    "LBTEST": get_value(o, "test_name") or tcode,
                    "LBORRES": lborres,
                    "LBORRESU": lborresu or "",
                    "LBSTRESC": lbstresc,
                    "LBSTRESN": lbstresn,
                    "LBSTRESU": lbstresu or "",
                    "LBNRIND": (str(lbnrind).upper() if lbnrind else ""),
                    "LBDTC": dtc_str,
                    "LBLOINC": lbloinc,
                    "_unmapped_obs": [],
                }
                subject_lb_records.append(record_data)

        # 5. Stable per-subject sequencing based on LBDTC, then LBTESTCD
        subject_lb_records.sort(
            key=lambda r: (
                r.get("LBDTC") or "",
                r.get("LBTESTCD") or "",
            )
        )

        # 6. Assign LBSEQ and generate SUPPLB records
        for seq, rec in enumerate(subject_lb_records, start=1):
            rec["LBSEQ"] = seq
            unmapped_obs = rec.pop("_unmapped_obs", [])

            for o in unmapped_obs:
                qname = str(get_value(o, "test_code"))
                qlabel = str(get_value(o, "test_name") or qname)
                val_str = get_value(o, "value_string")
                val_num = get_value(o, "value")
                qval = (
                    val_str
                    if val_str is not None
                    else (str(val_num) if val_num is not None else "")
                )

                supp = SUPPRecord(
                    STUDYID=study_id,
                    RDOMAIN="LB",
                    USUBJID=usubjid,
                    IDVAR="LBSEQ",
                    IDVARVAL=str(seq),
                    QNAM=qname,
                    QLABEL=qlabel,
                    QVAL=qval,
                )
                supp_records.append(supp)

            final_record = {
                "STUDYID": rec["STUDYID"],
                "USUBJID": rec["USUBJID"],
                "SUBJID": rec["SUBJID"],
                "LBSEQ": rec["LBSEQ"],
                "LBTESTCD": rec["LBTESTCD"],
                "LBTEST": rec["LBTEST"],
                "LBORRES": rec["LBORRES"],
                "LBORRESU": rec["LBORRESU"],
                "LBSTRESC": rec["LBSTRESC"],
                "LBSTRESN": rec["LBSTRESN"],
                "LBSTRESU": rec["LBSTRESU"],
                "LBNRIND": rec["LBNRIND"],
                "LBDTC": rec["LBDTC"],
                "LBLOINC": rec["LBLOINC"],
            }
            lb_records.append(final_record)

    return lb_records, supp_records


def to_dtc(val: Any) -> str | None:
    """Converts standard dates, datetimes, or validated strings into CDISC DTC format."""
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
        return val_clean.replace("/", "-")
    return str(val)


def _get_obs_date(obs: Any, visits_by_id: dict[str, Any]) -> str | None:
    """Gets DTC format date for observation, falling back to visit date if missing."""
    obs_dt = (
        get_value(obs, "observation_date")
        or get_value(obs, "vsdtc")
        or get_value(obs, "lbdtc")
        or get_value(obs, "aestdtc")
        or get_value(obs, "cmstdtc")
    )
    if not obs_dt:
        v_id = get_value(obs, "visit_id")
        if v_id and v_id in visits_by_id:
            visit = visits_by_id[v_id]
            obs_dt = get_value(visit, "visit_date") or get_value(visit, "date")
    return to_dtc(obs_dt)


def parse_float(val: Any) -> float | None:
    """Safely parses any numeric or string representation into a float."""
    if val is None:
        return None
    if isinstance(val, (int, float)):
        return float(val)
    try:
        return float(str(val).strip())
    except ValueError:
        return None


def map_cm(
    subjects: list[Any],
    visits: list[Any],
    observations: list[Any],
    created_by: str = "system",
    reason_for_change: str = "Automated EDC-to-SDTM CM mapping",
    **kwargs,
) -> list[dict[str, Any]]:
    """Stateless rule-based mapping from EDC sources to SDTM CM records (returning dictionaries directly)."""
    visits_by_id = {}
    for v in visits:
        v_id = get_value(v, "id") or get_value(v, "visit_id")
        if v_id:
            visits_by_id[v_id] = v

    # Group subjects
    subjects_by_id = {}
    for s in subjects:
        sub_id = get_value(s, "subject_id")
        if sub_id:
            subjects_by_id[sub_id] = s

    cm_records = []

    # Map observations for CM domain
    cm_obs = [o for o in observations if str(get_value(o, "domain")).upper() == "CM"]

    # Process per subject
    obs_by_subject: dict[str, list[Any]] = {}
    for o in cm_obs:
        sub_id = get_value(o, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(o)

    for sub_id, sub_obs in obs_by_subject.items():
        subj = subjects_by_id.get(sub_id)
        if not subj:
            continue

        study_id = get_value(subj, "study_id") or ""
        demographics = get_demographics(subj)
        site_id = get_value(subj, "site_id") or demographics.get("site_id") or "001"
        usubjid = (
            get_value(subj, "usubjid")
            or get_value(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        # Group observations by event (page_id or observation_date)
        groups: dict[str, list[Any]] = {}
        for o in sub_obs:
            page_id = get_value(o, "page_id")
            if page_id is not None and str(page_id).strip() != "":
                group_key = f"page_{page_id}"
            else:
                dtc = _get_obs_date(o, visits_by_id)
                group_key = f"date_{dtc}" if dtc else f"uniq_{id(o)}"
            groups.setdefault(group_key, []).append(o)

        subject_events = []

        for group_key, group_obs in groups.items():
            # Check if any observation has direct CM attributes (flat mapping fallback)
            is_flat = False
            for o in group_obs:
                if (
                    get_value(o, "cmtrt") is not None
                    or get_value(o, "CMTRT") is not None
                    or get_value(o, "cmstdtc") is not None
                ):
                    is_flat = True
                    break

            if is_flat:
                for o in group_obs:
                    dtc = _get_obs_date(o, visits_by_id)
                    cmtrt = (
                        get_value(o, "cmtrt")
                        or get_value(o, "CMTRT")
                        or get_value(o, "value_string")
                        or get_value(o, "test_name")
                    )
                    cmdecod = get_value(o, "cmdecod") or get_value(o, "CMDECOD")
                    cmclas = get_value(o, "cmclas") or get_value(o, "CMCLAS")
                    cmdose = (
                        get_value(o, "cmdose")
                        or get_value(o, "CMDOSE")
                        or get_value(o, "value")
                    )
                    cmdoseu = (
                        get_value(o, "cmdoseu")
                        or get_value(o, "CMDOSEU")
                        or get_value(o, "unit")
                    )
                    cmdosfrq = get_value(o, "cmdosfrq") or get_value(o, "CMDOSFRQ")
                    cmroute = get_value(o, "cmroute") or get_value(o, "CMROUTE")
                    cmstdtc = get_value(o, "cmstdtc") or get_value(o, "CMDOSE") or dtc

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
                            "CMENDTC": to_dtc(
                                get_value(o, "cmendtc") or get_value(o, "CMENDTC")
                            ),
                            "id": get_value(o, "id") or id(o),
                        }
                    )
            else:
                # Grouped CDASH
                event_data: dict[str, Any] = {
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
                    tcode = str(get_value(o, "test_code") or "").upper()
                    val_str = get_value(o, "value_string")
                    val_num = get_value(o, "value")
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
                            get_value(o, "test_name") or get_value(o, "test_code") or ""
                        )
                        if fallback_term:
                            break
                    event_data["CMTRT"] = fallback_term

                if not event_data["CMSTDTC"]:
                    event_data["CMSTDTC"] = min(dates) if dates else None

                subject_events.append(event_data)

        # Sort per-subject deterministically
        subject_events.sort(
            key=lambda item: (
                item.get("CMSTDTC") or "",
                str(item.get("CMTRT") or "").upper(),
                str(item.get("id") or ""),
            )
        )

        for seq, item in enumerate(subject_events, start=1):
            record = {
                "STUDYID": study_id,
                "DOMAIN": "CM",
                "USUBJID": usubjid,
                "CMSEQ": seq,
                "CMTRT": item["CMTRT"],
                "CMDECOD": item["CMDECOD"],
                "CMCLAS": item["CMCLAS"],
                "CMDOSE": item["CMDOSE"],
                "CMDOSEU": item["CMDOSEU"],
                "CMDOSFRQ": item["CMDOSFRQ"],
                "CMROUTE": item["CMROUTE"],
                "CMSTDTC": item["CMSTDTC"],
                "CMENDTC": item["CMENDTC"],
            }
            cm_records.append(record)

    return cm_records
