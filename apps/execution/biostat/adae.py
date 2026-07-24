"""ADAE derivation module for SDTM AE and ADSL datasets."""

from datetime import date, timedelta
from typing import Any, Dict, List, Optional

from apps.execution.biostat.dates import impute_partial_date, to_sas_date
from apps.execution.biostat.terminology import normalize_severity


def from_sas_date(sas_date: Optional[int]) -> Optional[str]:
    """Converts a SAS numeric date (days since 1960-01-01) back to 'YYYY-MM-DD' ISO format.

    Args:
        sas_date: Numeric SAS date (integer), representing days since Epoch (1960-01-01).

    Returns:
        Optional[str]: Date as 'YYYY-MM-DD' string, or None if sas_date is None.
    """
    if sas_date is None:
        return None
    epoch = date(1960, 1, 1)
    dt = epoch + timedelta(days=sas_date)
    return dt.isoformat()


def derive_adae(
    adsl_records: List[Dict[str, Any]],
    ae_records: List[Dict[str, Any]],
) -> List[Dict[str, Any]]:
    """Derives the Occurrence-Structured Adverse Events Analysis Dataset (ADAE).

    Each AE record is matched and joined to its ADSL subject based on USUBJID.
    The output includes all standard ADSL baseline and treatment variables, copied AE
    variables, and newly derived analysis variables (ASTDT, AENDT, ASTDY, AENDY, TRTEMFL, AESEVN).

    Args:
        adsl_records: List of Subject-Level Analysis Dataset (ADSL) records.
        ae_records: List of SDTM Adverse Events (AE) records.

    Returns:
        List[Dict[str, Any]]: Derived ADAE analysis records.
    """
    # Create an index of ADSL records by USUBJID for fast lookup
    adsl_by_usubjid = {
        rec.get("USUBJID"): rec for rec in adsl_records if rec.get("USUBJID")
    }

    adae_records = []

    for ae_rec in ae_records:
        usubjid = ae_rec.get("USUBJID")
        if not usubjid:
            continue

        adsl_rec = adsl_by_usubjid.get(usubjid)
        if not adsl_rec:
            # Skip records that do not map to an ADSL subject
            continue

        # Start with a copy of all variables from ADSL
        adae_rec = dict(adsl_rec)

        # Copy AE standard variables
        ae_keys = [
            "AESEQ",
            "AETERM",
            "AEDECOD",
            "AEBODSYS",
            "AELOC",
            "AELDTC",
            "AESTDTC",
            "AEENDTC",
            "AESEV",
            "AESER",
            "AEREL",
            "AEOUT",
        ]
        for key in ae_keys:
            if key in ae_rec:
                adae_rec[key] = ae_rec[key]

        # Extract and convert ADSL reference dates
        trtsdt = adsl_rec.get("TRTSDT")
        trtedt = adsl_rec.get("TRTEDT")
        eosdt = adsl_rec.get("EOSDT")

        trt_start_str = from_sas_date(trtsdt)
        eos_date_str = from_sas_date(eosdt)

        # 1. ASTDT and AENDT Date Imputation
        aestdtc = ae_rec.get("AESTDTC")
        aeendtc = ae_rec.get("AEENDTC")

        astdt_str = impute_partial_date(
            aestdtc,
            direction="START",
            treatment_start_date=trt_start_str,
        )
        aendt_str = impute_partial_date(
            aeendtc,
            direction="END",
            treatment_start_date=trt_start_str,
            end_of_study_date=eos_date_str,
        )

        astdt = to_sas_date(astdt_str) if astdt_str else None
        aendt = to_sas_date(aendt_str) if aendt_str else None

        # 2. ASTDY and AENDY Day Calculations (Relative Day Formula)
        astdy = None
        if astdt is not None and trtsdt is not None:
            if astdt >= trtsdt:
                astdy = astdt - trtsdt + 1
            else:
                astdy = astdt - trtsdt

        aendy = None
        if aendt is not None and trtsdt is not None:
            if aendt >= trtsdt:
                aendy = aendt - trtsdt + 1
            else:
                aendy = aendt - trtsdt

        # 3. TRTEMFL (Treatment Emergent Adverse Event Flag)
        # Set to "Y" if ASTDT >= TRTSDT and ASTDT <= ADSL.TRTEDT + 30. Otherwise "N".
        # Missing or invalid source dates are not silently misclassified as treatment-emergent.
        trtemfl = "N"
        if astdt is not None and trtsdt is not None:
            if astdt >= trtsdt:
                if trtedt is not None:
                    if astdt <= trtedt + 30:
                        trtemfl = "Y"
                else:
                    # Missing TRTEDT implies ongoing treatment; any event on/after TRTSDT is treatment-emergent.
                    trtemfl = "Y"

        # 4. AESEVN (Severity Numeric Grade Mapping)
        aesev = ae_rec.get("AESEV")
        aesevn = None
        if aesev:
            try:
                norm_sev = normalize_severity(aesev)
                if norm_sev == "MILD":
                    aesevn = 1
                elif norm_sev == "MODERATE":
                    aesevn = 2
                elif norm_sev == "SEVERE":
                    aesevn = 3
            except ValueError:
                pass

        # Populate ADAE specific analysis variables
        adae_rec["ASTDT"] = astdt
        adae_rec["AENDT"] = aendt
        adae_rec["ASTDY"] = astdy
        adae_rec["AENDY"] = aendy
        adae_rec["TRTEMFL"] = trtemfl
        adae_rec["AESEVN"] = aesevn

        adae_records.append(adae_rec)

    return adae_records
