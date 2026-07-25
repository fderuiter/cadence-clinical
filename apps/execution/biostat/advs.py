"""ADVS derivation module for SDTM VS and ADSL datasets."""

from typing import Any, Dict, List

from apps.execution.biostat.dates import to_sas_date


def derive_advs(
    adsl_records: List[Dict[str, Any]],
    vs_records: List[Dict[str, Any]],
) -> List[Dict[str, Any]]:
    """Derives the Basic Data Structure Vital Signs Analysis Dataset (ADVS).

    Each VS record is matched and joined to its ADSL subject based on USUBJID.
    The output includes ADSL baseline/treatment variables, copied VS variables,
    and derived BDS variables (PARAMCD, PARAM, AVAL, AVALC, ADY, AVISIT, AVISITN, ABLFL, BASE, CHG, PCHG).

    Args:
        adsl_records: List of Subject-Level Analysis Dataset (ADSL) records.
        vs_records: List of SDTM Vital Signs (VS) records.

    Returns:
        List[Dict[str, Any]]: Derived ADVS analysis records.
    """
    # Create index of ADSL records by USUBJID
    adsl_by_usubjid = {
        rec.get("USUBJID"): rec for rec in adsl_records if rec.get("USUBJID")
    }

    # Step 1: Pre-process records, join ADSL variables, and calculate basic ADVS variables
    advs_raw: List[Dict[str, Any]] = []

    for vs_rec in vs_records:
        usubjid = vs_rec.get("USUBJID")
        if not usubjid:
            continue

        adsl_rec = adsl_by_usubjid.get(usubjid)
        if not adsl_rec:
            # Skip if no matching ADSL subject
            continue

        # Start with a copy of all variables from ADSL
        advs_rec = dict(adsl_rec)

        # Copy over source SDTM VS variables
        vs_keys = [
            "VSSEQ",
            "VSTESTCD",
            "VSTEST",
            "VSORRES",
            "VSORRESU",
            "VSSTRESC",
            "VSSTRESN",
            "VSSTRESU",
            "VSPOS",
            "VSDTC",
            "VSBLFL",
        ]
        for key in vs_keys:
            if key in vs_rec:
                advs_rec[key] = vs_rec[key]

        # 1. PARAMCD & PARAM
        paramcd = vs_rec.get("VSTESTCD") or ""
        advs_rec["PARAMCD"] = paramcd

        vstest = vs_rec.get("VSTEST") or paramcd or ""
        vsstresu = vs_rec.get("VSSTRESU") or ""
        if vstest and vsstresu:
            param = f"{vstest} ({vsstresu})"
        else:
            param = vstest
        advs_rec["PARAM"] = param

        # 2. AVAL & AVALC
        # Numeric values should not be coerced into misleading values if they are unavailable.
        aval = vs_rec.get("VSSTRESN")
        if aval is not None and not isinstance(aval, (int, float)):
            try:
                aval = float(aval)
            except (ValueError, TypeError):
                aval = None
        advs_rec["AVAL"] = aval

        avalc = vs_rec.get("VSSTRESC")
        if avalc is None:
            avalc = vs_rec.get("VSORRES")
            if avalc is not None:
                avalc = str(avalc)
        advs_rec["AVALC"] = str(avalc) if avalc is not None else None

        # 3. ADY (Analysis Relative Day)
        vsdtc = vs_rec.get("VSDTC")
        trtsdt = adsl_rec.get("TRTSDT")
        ady = None
        if vsdtc and trtsdt is not None:
            vs_sas_dt = to_sas_date(vsdtc)
            if vs_sas_dt is not None:
                if vs_sas_dt >= trtsdt:
                    ady = vs_sas_dt - trtsdt + 1
                else:
                    ady = vs_sas_dt - trtsdt
        advs_rec["ADY"] = ady

        # 4. AVISIT & AVISITN
        avisit = vs_rec.get("VISIT")
        if avisit is None:
            for k, v in vs_rec.items():
                if k.upper() in {"VISIT", "VISIT_NAME", "VISITNAME"}:
                    avisit = v
                    break
        advs_rec["AVISIT"] = avisit

        avisitn = vs_rec.get("VISITNUM")
        if avisitn is None:
            for k, v in vs_rec.items():
                if k.upper() in {
                    "VISITNUM",
                    "VISIT_NUM",
                    "VISIT_NUMBER",
                    "VISITNUMBER",
                }:
                    avisitn = v
                    break
        if avisitn is not None:
            try:
                avisitn = float(avisitn)
            except (ValueError, TypeError):
                pass
        advs_rec["AVISITN"] = avisitn

        # Default placeholder columns for baseline/change variables
        advs_rec["ABLFL"] = None
        advs_rec["BASE"] = None
        advs_rec["CHG"] = None
        advs_rec["PCHG"] = None

        advs_raw.append(advs_rec)

    # Step 2: Determine baseline and compute change metrics grouped by subject and parameter
    # Group records by (USUBJID, PARAMCD)
    grouped: Dict[tuple, List[Dict[str, Any]]] = {}
    for rec in advs_raw:
        sub_param_key = (rec["USUBJID"], rec["PARAMCD"])
        grouped.setdefault(sub_param_key, []).append(rec)

    for (usubjid, paramcd), recs in grouped.items():
        # First, find baseline candidates: records with AVAL is not None.
        # Among these, we look for those with VSBLFL == "Y" or "y" or similar.
        baseline_candidates = []
        for r in recs:
            if r.get("AVAL") is not None:
                if str(r.get("VSBLFL") or "").strip().upper() == "Y":
                    baseline_candidates.append(r)

        # If no records have VSBLFL == "Y", fall back to records with ADY <= 0 (pre-dose/baseline day)
        if not baseline_candidates:
            for r in recs:
                if r.get("AVAL") is not None:
                    ady_val = r.get("ADY")
                    if ady_val is not None and ady_val <= 0:
                        baseline_candidates.append(r)

        baseline_rec = None
        if baseline_candidates:
            # Sort deterministically: VSDTC ascending, then VSSEQ ascending.
            # The last element in the sorted list is selected as the baseline record.
            baseline_candidates.sort(
                key=lambda x: (
                    x.get("VSDTC") or "",
                    x.get("VSSEQ") or 0,
                )
            )
            baseline_rec = baseline_candidates[-1]
            baseline_rec["ABLFL"] = "Y"

        base_val = baseline_rec["AVAL"] if baseline_rec else None

        # Populate BASE, CHG, and PCHG
        for r in recs:
            # BASE is populated for all records of the same subject/parameter if baseline exists
            r["BASE"] = base_val

            # CHG and PCHG are only computed for eligible post-baseline records.
            # Post-baseline is defined as:
            # - AVISITN > 1 (post-baseline visits), or
            # - ADY > 0, or
            # - Not the baseline record (when visit or day numbering is not available)
            is_post_baseline = False
            r_avisitn = r.get("AVISITN")
            r_ady = r.get("ADY")
            if r_avisitn is not None and r_avisitn > 1:
                is_post_baseline = True
            elif r_ady is not None and r_ady > 0:
                is_post_baseline = True
            elif r_avisitn is None and r_ady is None and r is not baseline_rec:
                is_post_baseline = True

            if is_post_baseline:
                r_aval = r.get("AVAL")
                if r_aval is not None and base_val is not None:
                    # Compute CHG
                    chg_val = r_aval - base_val
                    r["CHG"] = chg_val

                    # Compute PCHG (handling division-by-zero explicitly)
                    if base_val != 0:
                        r["PCHG"] = (chg_val / base_val) * 100.0
                    else:
                        r["PCHG"] = None
                else:
                    r["CHG"] = None
                    r["PCHG"] = None
            else:
                r["CHG"] = None
                r["PCHG"] = None

    return advs_raw
