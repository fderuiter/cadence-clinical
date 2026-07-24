from typing import Any, Dict, List, Optional

from apps.execution.biostat.dates import impute_partial_date, to_sas_date
from apps.execution.biostat.extractors import get_demographics, get_value


def derive_adsl(
    subjects: List[Any],
    observations: Optional[List[Any]] = None,
) -> List[Dict[str, Any]]:
    """Derives the Subject-Level Analysis Dataset (ADSL).

    ADSL is unique as it contains exactly one record per subject, consolidating
    foundational trial milestones, demographics, and stratification parameters.

    Args:
        subjects: List of clinical subject records (objects or dicts).
        observations: Optional list of clinical observation records (objects or dicts).

    Returns:
        List[Dict[str, Any]]: List of ADSL records, one per USUBJID.
    """
    obs_list = observations or []

    # Pre-group observations by subject_id for fast lookup
    obs_by_subject: Dict[str, List[Any]] = {}
    for obs in obs_list:
        sub_id = get_value(obs, "subject_id")
        if sub_id:
            obs_by_subject.setdefault(sub_id, []).append(obs)

    adsl_records = []

    for subj in subjects:
        sub_id = get_value(subj, "subject_id")
        if not sub_id:
            continue

        study_id = get_value(subj, "study_id") or ""
        demographics = get_demographics(subj)

        # 1. SITEID
        site_id = (
            get_value(subj, "site_id")
            or demographics.get("site_id")
            or demographics.get("siteID")
            or "001"
        )

        # 2. USUBJID
        usubjid = (
            get_value(subj, "usubjid")
            or get_value(subj, "USUBJID")
            or demographics.get("usubjid")
            or demographics.get("USUBJID")
        )
        if not usubjid:
            usubjid = f"{study_id}-{site_id}-{sub_id}"

        sub_obs = obs_by_subject.get(sub_id, [])

        # 3. ARM
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

        # 4. ACTARM / TRT01P / TRT01A
        actarm = (
            get_value(subj, "actarm")
            or get_value(subj, "ACTARM")
            or demographics.get("actarm")
            or demographics.get("ACTARM")
        )
        if not actarm:
            # Look in EX observations for EXTRT
            ex_obs = [o for o in sub_obs if str(get_value(o, "domain")).upper() == "EX"]
            for o in ex_obs:
                if str(get_value(o, "test_code")).upper() in {"EXTRT", "ACTARM"}:
                    val = get_value(o, "value_string") or get_value(o, "value")
                    if val:
                        actarm = str(val).strip()
                        break
        if not actarm:
            actarm = arm

        trt01p = arm
        trt01a = actarm

        # 5. TRTSDT / TRTEDT (Treatment Start / End Date)
        ex_start_dates = []
        ex_end_dates = []

        ex_obs = [o for o in sub_obs if str(get_value(o, "domain")).upper() == "EX"]
        for o in ex_obs:
            tcode = str(get_value(o, "test_code")).upper()
            val_str = get_value(o, "value_string")
            obs_dt = get_value(o, "observation_date")

            if tcode in {"EXSTDTC", "RFSTDTC"}:
                if val_str:
                    ex_start_dates.append(str(val_str).strip())
                elif obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        ex_start_dates.append(obs_dt.isoformat())
                    else:
                        ex_start_dates.append(str(obs_dt))
            elif tcode in {"EXENDTC", "RFENDTC"}:
                if val_str:
                    ex_end_dates.append(str(val_str).strip())
                elif obs_dt:
                    if hasattr(obs_dt, "isoformat"):
                        ex_end_dates.append(obs_dt.isoformat())
                    else:
                        ex_end_dates.append(str(obs_dt))

        # Check properties/demographics fallback
        rfstdtc_prop = (
            get_value(subj, "rfstdtc")
            or get_value(subj, "RFSTDTC")
            or demographics.get("rfstdtc")
            or demographics.get("RFSTDTC")
        )
        if rfstdtc_prop:
            ex_start_dates.append(str(rfstdtc_prop).strip())

        rfendtc_prop = (
            get_value(subj, "rfendtc")
            or get_value(subj, "RFENDTC")
            or demographics.get("rfendtc")
            or demographics.get("RFENDTC")
        )
        if rfendtc_prop:
            ex_end_dates.append(str(rfendtc_prop).strip())

        # Impute start dates
        imputed_starts = []
        for d_str in ex_start_dates:
            imp = impute_partial_date(d_str, direction="START")
            if imp:
                imputed_starts.append(imp)

        earliest_start_str = min(imputed_starts) if imputed_starts else None
        trtsdt = to_sas_date(earliest_start_str) if earliest_start_str else None

        # Impute end dates (using earliest start date and potentially end of study date)
        # End of Study Date discovery first (so we can pass to end date imputation)
        eos_date_str = None
        eos_dt_prop = (
            get_value(subj, "eosdt")
            or get_value(subj, "EOSDT")
            or get_value(subj, "end_of_study_date")
            or demographics.get("eosdt")
            or demographics.get("EOSDT")
            or demographics.get("end_of_study_date")
        )
        if eos_dt_prop:
            eos_date_str = str(eos_dt_prop).strip()
        else:
            ds_obs = [o for o in sub_obs if str(get_value(o, "domain")).upper() == "DS"]
            groups = {}
            for o in ds_obs:
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

            # Look for DSCAT == 'DISPOSITION EVENT' and DSSCAT == 'STUDY COMPLETION/WITHDRAWAL'
            for g_key, g_obs in groups.items():
                is_eos_event = False
                cat_val = None
                scat_val = None
                date_val = None

                for o in g_obs:
                    tcode = str(get_value(o, "test_code")).upper()
                    val = get_value(o, "value_string") or get_value(o, "value")
                    if not val:
                        continue
                    if tcode == "DSCAT":
                        cat_val = str(val).strip().upper()
                    elif tcode == "DSSCAT":
                        scat_val = str(val).strip().upper()
                    elif tcode in {"DSSTDTC", "DSDTC"}:
                        date_val = str(val).strip()

                if (
                    cat_val == "DISPOSITION EVENT"
                    and scat_val == "STUDY COMPLETION/WITHDRAWAL"
                ):
                    is_eos_event = True

                if is_eos_event:
                    if date_val:
                        eos_date_str = date_val
                    else:
                        for o in g_obs:
                            obs_dt = get_value(o, "observation_date")
                            if obs_dt:
                                if hasattr(obs_dt, "isoformat"):
                                    eos_date_str = obs_dt.isoformat()
                                else:
                                    eos_date_str = str(obs_dt)
                                break
                    break

            # Fallback check for DSDECOD
            if not eos_date_str:
                for g_key, g_obs in groups.items():
                    is_eos_event = False
                    decod_val = None
                    date_val = None
                    for o in g_obs:
                        tcode = str(get_value(o, "test_code")).upper()
                        val = get_value(o, "value_string") or get_value(o, "value")
                        if not val:
                            continue
                        if tcode == "DSDECOD":
                            decod_val = str(val).strip().upper()
                        elif tcode in {"DSSTDTC", "DSDTC"}:
                            date_val = str(val).strip()
                    if decod_val in {
                        "COMPLETED",
                        "STUDY COMPLETION",
                        "WITHDRAWAL",
                        "STUDY TERMINATED",
                    }:
                        is_eos_event = True
                    if is_eos_event:
                        if date_val:
                            eos_date_str = date_val
                        else:
                            for o in g_obs:
                                obs_dt = get_value(o, "observation_date")
                                if obs_dt:
                                    if hasattr(obs_dt, "isoformat"):
                                        eos_date_str = obs_dt.isoformat()
                                    else:
                                        eos_date_str = str(obs_dt)
                                    break
                        break

        imputed_eos_str = (
            impute_partial_date(eos_date_str, direction="END") if eos_date_str else None
        )
        eosdt = to_sas_date(imputed_eos_str) if imputed_eos_str else None

        imputed_ends = []
        for d_str in ex_end_dates:
            imp = impute_partial_date(
                d_str,
                direction="END",
                treatment_start_date=earliest_start_str,
                end_of_study_date=imputed_eos_str,
            )
            if imp:
                imputed_ends.append(imp)

        latest_end_str = max(imputed_ends) if imputed_ends else None
        trtedt = to_sas_date(latest_end_str) if latest_end_str else None

        # 6. RANDT (Randomization Date)
        rand_date_str = None
        rand_dt_prop = (
            get_value(subj, "randt")
            or get_value(subj, "RANDT")
            or get_value(subj, "randomization_date")
            or demographics.get("randt")
            or demographics.get("RANDT")
            or demographics.get("randomization_date")
        )
        if rand_dt_prop:
            rand_date_str = str(rand_dt_prop).strip()
        else:
            ds_obs = [o for o in sub_obs if str(get_value(o, "domain")).upper() == "DS"]
            for o in ds_obs:
                tcode = str(get_value(o, "test_code")).upper()
                val = get_value(o, "value_string") or get_value(o, "value")
                if (
                    tcode == "DSDECOD"
                    and val
                    and str(val).strip().upper() == "RANDOMIZED"
                ):
                    page_id = get_value(o, "page_id")
                    obs_dt = get_value(o, "observation_date")

                    # Match by page_id first
                    for o2 in ds_obs:
                        tcode2 = str(get_value(o2, "test_code")).upper()
                        if tcode2 in {"DSSTDTC", "DSDTC"}:
                            if page_id and get_value(o2, "page_id") == page_id:
                                rand_date_str = get_value(
                                    o2, "value_string"
                                ) or get_value(o2, "value")
                                break

                    # Match by observation_date
                    if not rand_date_str:
                        for o2 in ds_obs:
                            tcode2 = str(get_value(o2, "test_code")).upper()
                            if (
                                tcode2 in {"DSSTDTC", "DSDTC"}
                                and get_value(o2, "observation_date") == obs_dt
                            ):
                                rand_date_str = get_value(
                                    o2, "value_string"
                                ) or get_value(o2, "value")
                                break

                    # Match by record itself
                    if not rand_date_str:
                        if obs_dt:
                            if hasattr(obs_dt, "isoformat"):
                                rand_date_str = obs_dt.isoformat()
                            else:
                                rand_date_str = str(obs_dt)
                    break

        imputed_rand_str = (
            impute_partial_date(rand_date_str, direction="START")
            if rand_date_str
            else None
        )
        randt = to_sas_date(imputed_rand_str) if imputed_rand_str else None

        # 7. DTHDT (Death Date)
        dthdtc = (
            get_value(subj, "dthdtc")
            or get_value(subj, "DTHDTC")
            or get_value(subj, "death_date")
            or demographics.get("dthdtc")
            or demographics.get("DTHDTC")
            or demographics.get("death_date")
        )
        if not dthdtc:
            for o in sub_obs:
                if str(get_value(o, "test_code")).upper() == "DTHDTC":
                    val = get_value(o, "value_string") or get_value(o, "value")
                    if val:
                        dthdtc = str(val).strip()
                        break

        imputed_dth_str = (
            impute_partial_date(dthdtc, direction="START") if dthdtc else None
        )
        dthdt = to_sas_date(imputed_dth_str) if imputed_dth_str else None

        # 8. Population Flags
        saffl = "Y" if trtsdt is not None else "N"
        ittfl = "Y" if randt is not None else "N"

        record = {
            "STUDYID": study_id,
            "USUBJID": usubjid,
            "SUBJID": sub_id,
            "SITEID": site_id,
            "ARM": arm,
            "ACTARM": actarm,
            "TRT01P": trt01p,
            "TRT01A": trt01a,
            "TRTSDT": trtsdt,
            "TRTEDT": trtedt,
            "RANDT": randt,
            "DTHDT": dthdt,
            "EOSDT": eosdt,
            "SAFFL": saffl,
            "ITTFL": ittfl,
        }
        adsl_records.append(record)

    return adsl_records
