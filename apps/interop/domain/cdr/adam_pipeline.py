"""Automated CDISC ADaM Pipeline & Derivation Engine.

Executes automated biostatistical derivation rules generating CDISC ADaM v1.3 / v2.1 datasets:
- ADSL (Subject-Level Analysis Dataset)
- ADAE (Adverse Events Analysis Dataset)
- ADTTE (Time-to-Event Analysis Dataset: OS, PFS, Toxicity, Discontinuation)
- BDS (Basic Data Structure: ADVS, ADLB with Baseline, CHG, PCHG, and Relative Days)

Requirements: PRD-CDR-002, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from datetime import date, datetime
from typing import Any

from apps.interop.domain.cdr.models import (
    SDTMAE,
    SDTMDM,
    SDTMDS,
    SDTMEX,
    SDTMLB,
    SDTMRS,
    SDTMTR,
    SDTMVS,
    ADaMADAE,
    ADaMADSL,
    ADaMADTTE,
    ADaMBDS,
)


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


def _format_date(d: date | None) -> str | None:
    """Format date to ISO 8601 string."""
    return d.strftime("%Y-%m-%d") if d is not None else None


def _calculate_relative_day(target_dt: str | None, ref_dt: str | None) -> int | None:
    """Calculate ADaM relative study day."""
    t_d = _parse_date(target_dt)
    r_d = _parse_date(ref_dt)
    if t_d is None or r_d is None:
        return None
    diff = (t_d - r_d).days
    return diff + 1 if diff >= 0 else diff


class ADaMPipelineEngine:
    """Automated derivation engine generating ADaM analysis datasets from standard SDTM domains."""

    def __init__(self, study_id: str = "STUDY-001") -> None:
        self.study_id = study_id

    def build_adam_datasets(
        self, sdtm_domains: dict[str, list[Any]]
    ) -> dict[str, list[Any]]:
        """Derive complete suite of ADaM datasets from SDTM domains."""
        dm_list: list[SDTMDM] = sdtm_domains.get("DM", [])
        ae_list: list[SDTMAE] = sdtm_domains.get("AE", [])
        ex_list: list[SDTMEX] = sdtm_domains.get("EX", [])
        ds_list: list[SDTMDS] = sdtm_domains.get("DS", [])
        vs_list: list[SDTMVS] = sdtm_domains.get("VS", [])
        lb_list: list[SDTMLB] = sdtm_domains.get("LB", [])
        rs_list: list[SDTMRS] = sdtm_domains.get("RS", [])
        tr_list: list[SDTMTR] = sdtm_domains.get("TR", [])

        # 1. Derive ADSL (Subject-Level Analysis Dataset)
        adsl_records = self._derive_adsl(dm_list, ex_list, ds_list, vs_list)
        adsl_map = {r.USUBJID: r for r in adsl_records}

        # 2. Derive ADAE (Adverse Events Analysis Dataset)
        adae_records = self._derive_adae(ae_list, adsl_map)

        # 3. Derive ADTTE (Time-to-Event Analysis Dataset)
        adtte_records = self._derive_adtte(
            adsl_records, ae_list, rs_list, tr_list, ds_list
        )

        # 4. Derive BDS (Basic Data Structure for VS & LB)
        bds_records = self._derive_bds(vs_list, lb_list, adsl_map)

        return {
            "ADSL": adsl_records,
            "ADAE": adae_records,
            "ADTTE": adtte_records,
            "BDS": bds_records,
        }

    def _derive_adsl(
        self,
        dm_list: list[SDTMDM],
        ex_list: list[SDTMEX],
        ds_list: list[SDTMDS],
        vs_list: list[SDTMVS],
    ) -> list[ADaMADSL]:
        """Derive Subject-Level Analysis Dataset (ADSL)."""
        # Map exposure start/end by subject
        ex_by_subj: dict[str, list[SDTMEX]] = {}
        for ex in ex_list:
            ex_by_subj.setdefault(ex.USUBJID, []).append(ex)

        # Map disposition by subject
        ds_by_subj: dict[str, list[SDTMDS]] = {}
        for ds in ds_list:
            ds_by_subj.setdefault(ds.USUBJID, []).append(ds)

        # Map baseline height / weight by subject
        height_by_subj: dict[str, float] = {}
        weight_by_subj: dict[str, float] = {}
        for vs in vs_list:
            if vs.VSBLFL == "Y" or vs.VSDY is None or vs.VSDY <= 1:
                if vs.VSTESTCD == "HEIGHT" and vs.VSSTRESN is not None:
                    height_by_subj[vs.USUBJID] = vs.VSSTRESN
                elif vs.VSTESTCD == "WEIGHT" and vs.VSSTRESN is not None:
                    weight_by_subj[vs.USUBJID] = vs.VSSTRESN

        results: list[ADaMADSL] = []

        for dm in dm_list:
            usubjid = dm.USUBJID
            subjid = dm.SUBJID
            siteid = f"SITE-{subjid.split('-')[0]}" if "-" in subjid else "SITE-01"

            # Treatment timing
            subj_ex = ex_by_subj.get(usubjid, [])
            valid_ex_dates = [
                ex.EXSTDTC for ex in subj_ex if ex.EXSTDTC and (ex.EXDOSE or 0) > 0
            ]
            valid_ex_ends = [
                ex.EXENDTC for ex in subj_ex if ex.EXENDTC and (ex.EXDOSE or 0) > 0
            ]

            trtsdt = min(valid_ex_dates) if valid_ex_dates else dm.RFSTDTC
            trtedt = max(valid_ex_ends) if valid_ex_ends else trtsdt
            randt = dm.RFSTDTC or trtsdt

            # Population flags
            saffl = (
                "Y"
                if (subj_ex and any((ex.EXDOSE or 0) > 0 for ex in subj_ex))
                else ("Y" if trtsdt else "N")
            )
            ittfl = "Y"  # All randomized / enrolled subjects in DM
            fasfl = "Y" if (saffl == "Y" and ittfl == "Y") else "N"

            # Disposition status
            subj_ds = ds_by_subj.get(usubjid, [])
            eosdt = None
            dthdt = None
            dthfl = "N"
            complfl = "N"

            for ds in subj_ds:
                decod_upper = (ds.DSDECOD or "").upper()
                if "DEATH" in decod_upper or "FATAL" in decod_upper:
                    dthfl = "Y"
                    dthdt = ds.DSSTDTC or eosdt
                if "COMPLETED" in decod_upper:
                    complfl = "Y"
                if ds.DSSTDTC:
                    eosdt = ds.DSSTDTC

            if not eosdt:
                eosdt = trtedt or trtsdt or "2026-06-30"

            # Baseline physical metrics
            h_bl = height_by_subj.get(usubjid, 175.0)
            w_bl = weight_by_subj.get(usubjid, 70.0)
            bmi_bl = None
            if h_bl and w_bl and h_bl > 0:
                h_m = h_bl / 100.0 if h_bl > 3.0 else h_bl
                bmi_bl = round(w_bl / (h_m * h_m), 2)

            arm = dm.ARM or "Experimental Arm 100mg"
            armcd = dm.ARMCD or "TRT-100"

            results.append(
                ADaMADSL(
                    STUDYID=self.study_id,
                    USUBJID=usubjid,
                    SUBJID=subjid,
                    SITEID=siteid,
                    AGE=dm.AGE,
                    AGEU=dm.AGEU or "YEARS",
                    SEX=dm.SEX,
                    RACE=dm.RACE,
                    ETHNIC=dm.ETHNIC,
                    ARM=arm,
                    ARMCD=armcd,
                    ACTARM=dm.ACTARM or arm,
                    ACTARMCD=dm.ACTARMCD or armcd,
                    TRT01P=arm,
                    TRT01A=dm.ACTARM or arm,
                    TRTSDT=trtsdt,
                    TRTEDT=trtedt,
                    RANDT=randt,
                    EOSDT=eosdt,
                    DTHDT=dthdt,
                    DTHFL=dthfl,
                    SAFFL=saffl,
                    ITTFL=ittfl,
                    FASFL=fasfl,
                    COMPLFL=complfl,
                    HEIGHTBL=h_bl,
                    WEIGHTBL=w_bl,
                    BMIBL=bmi_bl,
                )
            )

        return results

    def _derive_adae(
        self, ae_list: list[SDTMAE], adsl_map: dict[str, ADaMADSL]
    ) -> list[ADaMADAE]:
        """Derive Adverse Events Analysis Dataset (ADAE)."""
        results: list[ADaMADAE] = []

        for ae in ae_list:
            adsl = adsl_map.get(ae.USUBJID)
            siteid = adsl.SITEID if adsl else "SITE-01"
            arm = adsl.ARM if adsl else "Active Treatment"
            actarm = adsl.ACTARM if adsl else "Active Treatment"
            trt01a = adsl.TRT01A if adsl else "Active Treatment"
            saffl = adsl.SAFFL if adsl else "Y"
            trtsdt = adsl.TRTSDT if adsl else None

            astdt = ae.AESTDTC
            aendt = ae.AEENDTC or astdt

            astdy = _calculate_relative_day(astdt, trtsdt) if trtsdt else None
            aendy = _calculate_relative_day(aendt, trtsdt) if trtsdt else None

            # Duration
            adurn = None
            if astdt and aendt:
                s_d = _parse_date(astdt)
                e_d = _parse_date(aendt)
                if s_d and e_d:
                    adurn = max(1, (e_d - s_d).days + 1)

            # Treatment emergent flag (TRTEMFL)
            # True if event starts on or after treatment start date
            is_teae = False
            is_pre = False
            if astdt and trtsdt:
                s_d = _parse_date(astdt)
                trt_d = _parse_date(trtsdt)
                if s_d and trt_d:
                    if s_d >= trt_d:
                        is_teae = True
                    else:
                        is_pre = True
            else:
                is_teae = True

            # Severity numeric
            sev_num = 1
            if ae.AESEV == "MODERATE":
                sev_num = 2
            elif ae.AESEV == "SEVERE":
                sev_num = 3

            results.append(
                ADaMADAE(
                    STUDYID=self.study_id,
                    USUBJID=ae.USUBJID,
                    SUBJID=ae.USUBJID.replace(f"{self.study_id}-", ""),
                    SITEID=siteid,
                    ARM=arm,
                    ACTARM=actarm,
                    TRT01A=trt01a,
                    SAFFL=saffl,
                    AESEQ=ae.AESEQ,
                    AETERM=ae.AETERM,
                    AEDECOD=ae.AEDECOD or ae.AETERM,
                    AEBODSYS=ae.AEBODSYS or "General disorders",
                    AESEV=ae.AESEV or "MILD",
                    AESEVN=sev_num,
                    AESER=ae.AESER,
                    AEREL=ae.AEREL or "NOT RELATED",
                    AEOUT=ae.AEOUT or "RECOVERED/RESOLVED",
                    AESTDTC=ae.AESTDTC,
                    AEENDTC=ae.AEENDTC,
                    ASTDT=astdt,
                    AENDT=aendt,
                    ASTDY=astdy,
                    AENDY=aendy,
                    ADURN=adurn,
                    ADURU="DAYS",
                    TRTEMFL="Y" if is_teae else "N",
                    PREFL="Y" if is_pre else "N",
                )
            )

        return results

    def _derive_adtte(
        self,
        adsl_records: list[ADaMADSL],
        ae_list: list[SDTMAE],
        rs_list: list[SDTMRS],
        tr_list: list[SDTMTR],
        ds_list: list[SDTMDS],
    ) -> list[ADaMADTTE]:
        """Derive Time-to-Event Analysis Dataset (ADTTE) for OS, PFS, Toxicity, and Discontinuation."""
        results: list[ADaMADTTE] = []

        # Map AE earliest onset by subject
        ae_earliest_by_subj: dict[str, str] = {}
        for ae in ae_list:
            if ae.AESTDTC:
                cur = ae_earliest_by_subj.get(ae.USUBJID)
                if not cur or cur > ae.AESTDTC:
                    ae_earliest_by_subj[ae.USUBJID] = ae.AESTDTC

        # Map Disease Progression (PD) or response by subject
        pd_by_subj: dict[str, str] = {}
        for rs in rs_list:
            if rs.RSORRES == "PD" and rs.RSDTC:
                cur = pd_by_subj.get(rs.USUBJID)
                if not cur or cur > rs.RSDTC:
                    pd_by_subj[rs.USUBJID] = rs.RSDTC

        for adsl in adsl_records:
            usubjid = adsl.USUBJID
            subjid = adsl.SUBJID
            start_dt_str = adsl.TRTSDT or adsl.RANDT or "2026-01-01"
            start_d = _parse_date(start_dt_str) or date(2026, 1, 1)

            # Endpoint 1: Overall Survival (TTDEATH)
            if adsl.DTHFL == "Y" and adsl.DTHDT:
                death_d = _parse_date(adsl.DTHDT) or start_d
                aval_os = max(1.0, float((death_d - start_d).days + 1))
                results.append(
                    ADaMADTTE(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=subjid,
                        SITEID=adsl.SITEID,
                        ARM=adsl.ARM,
                        ACTARM=adsl.ACTARM,
                        PARAMCD="TTDEATH",
                        PARAM="Time to Death / Overall Survival",
                        STARTDT=start_dt_str,
                        ADT=adsl.DTHDT,
                        AVAL=aval_os,
                        AVALU="DAYS",
                        CNSR=0,
                        EVNTDESC="Death",
                        SAFFL=adsl.SAFFL,
                        ITTFL=adsl.ITTFL,
                    )
                )
            else:
                eos_dt_str = adsl.EOSDT or adsl.TRTEDT or start_dt_str
                eos_d = _parse_date(eos_dt_str) or start_d
                aval_os = max(1.0, float((eos_d - start_d).days + 1))
                results.append(
                    ADaMADTTE(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=subjid,
                        SITEID=adsl.SITEID,
                        ARM=adsl.ARM,
                        ACTARM=adsl.ACTARM,
                        PARAMCD="TTDEATH",
                        PARAM="Time to Death / Overall Survival",
                        STARTDT=start_dt_str,
                        ADT=eos_dt_str,
                        AVAL=aval_os,
                        AVALU="DAYS",
                        CNSR=1,
                        EVNTDESC="Censored at End of Study / Last Known Alive",
                        SAFFL=adsl.SAFFL,
                        ITTFL=adsl.ITTFL,
                    )
                )

            # Endpoint 2: Progression Free Survival (TTPROG)
            pd_dt_str = pd_by_subj.get(usubjid)
            if pd_dt_str:
                pd_d = _parse_date(pd_dt_str) or start_d
                aval_pfs = max(1.0, float((pd_d - start_d).days + 1))
                results.append(
                    ADaMADTTE(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=subjid,
                        SITEID=adsl.SITEID,
                        ARM=adsl.ARM,
                        ACTARM=adsl.ACTARM,
                        PARAMCD="TTPROG",
                        PARAM="Progression Free Survival (PFS)",
                        STARTDT=start_dt_str,
                        ADT=pd_dt_str,
                        AVAL=aval_pfs,
                        AVALU="DAYS",
                        CNSR=0,
                        EVNTDESC="Progressive Disease",
                        SAFFL=adsl.SAFFL,
                        ITTFL=adsl.ITTFL,
                    )
                )
            elif adsl.DTHFL == "Y" and adsl.DTHDT:
                death_d = _parse_date(adsl.DTHDT) or start_d
                aval_pfs = max(1.0, float((death_d - start_d).days + 1))
                results.append(
                    ADaMADTTE(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=subjid,
                        SITEID=adsl.SITEID,
                        ARM=adsl.ARM,
                        ACTARM=adsl.ACTARM,
                        PARAMCD="TTPROG",
                        PARAM="Progression Free Survival (PFS)",
                        STARTDT=start_dt_str,
                        ADT=adsl.DTHDT,
                        AVAL=aval_pfs,
                        AVALU="DAYS",
                        CNSR=0,
                        EVNTDESC="Death Prior to Progression",
                        SAFFL=adsl.SAFFL,
                        ITTFL=adsl.ITTFL,
                    )
                )
            else:
                eos_dt_str = adsl.EOSDT or start_dt_str
                eos_d = _parse_date(eos_dt_str) or start_d
                aval_pfs = max(1.0, float((eos_d - start_d).days + 1))
                results.append(
                    ADaMADTTE(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=subjid,
                        SITEID=adsl.SITEID,
                        ARM=adsl.ARM,
                        ACTARM=adsl.ACTARM,
                        PARAMCD="TTPROG",
                        PARAM="Progression Free Survival (PFS)",
                        STARTDT=start_dt_str,
                        ADT=eos_dt_str,
                        AVAL=aval_pfs,
                        AVALU="DAYS",
                        CNSR=1,
                        EVNTDESC="Censored without Progression",
                        SAFFL=adsl.SAFFL,
                        ITTFL=adsl.ITTFL,
                    )
                )

            # Endpoint 3: Time to First Adverse Event (TTTOX)
            ae_first_dt_str = ae_earliest_by_subj.get(usubjid)
            if ae_first_dt_str:
                ae_d = _parse_date(ae_first_dt_str) or start_d
                aval_tox = max(1.0, float((ae_d - start_d).days + 1))
                results.append(
                    ADaMADTTE(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=subjid,
                        SITEID=adsl.SITEID,
                        ARM=adsl.ARM,
                        ACTARM=adsl.ACTARM,
                        PARAMCD="TTTOX",
                        PARAM="Time to First Adverse Event",
                        STARTDT=start_dt_str,
                        ADT=ae_first_dt_str,
                        AVAL=aval_tox,
                        AVALU="DAYS",
                        CNSR=0,
                        EVNTDESC="Adverse Event Onset",
                        SAFFL=adsl.SAFFL,
                        ITTFL=adsl.ITTFL,
                    )
                )
            else:
                eos_dt_str = adsl.EOSDT or start_dt_str
                eos_d = _parse_date(eos_dt_str) or start_d
                aval_tox = max(1.0, float((eos_d - start_d).days + 1))
                results.append(
                    ADaMADTTE(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=subjid,
                        SITEID=adsl.SITEID,
                        ARM=adsl.ARM,
                        ACTARM=adsl.ACTARM,
                        PARAMCD="TTTOX",
                        PARAM="Time to First Adverse Event",
                        STARTDT=start_dt_str,
                        ADT=eos_dt_str,
                        AVAL=aval_tox,
                        AVALU="DAYS",
                        CNSR=1,
                        EVNTDESC="Censored with No Events",
                        SAFFL=adsl.SAFFL,
                        ITTFL=adsl.ITTFL,
                    )
                )

            # Endpoint 4: Time to Study / Treatment Discontinuation (TTDISC)
            is_disc = adsl.COMPLFL != "Y"
            eos_dt_str = adsl.EOSDT or start_dt_str
            eos_d = _parse_date(eos_dt_str) or start_d
            aval_disc = max(1.0, float((eos_d - start_d).days + 1))
            results.append(
                ADaMADTTE(
                    STUDYID=self.study_id,
                    USUBJID=usubjid,
                    SUBJID=subjid,
                    SITEID=adsl.SITEID,
                    ARM=adsl.ARM,
                    ACTARM=adsl.ACTARM,
                    PARAMCD="TTDISC",
                    PARAM="Time to Treatment Discontinuation",
                    STARTDT=start_dt_str,
                    ADT=eos_dt_str,
                    AVAL=aval_disc,
                    AVALU="DAYS",
                    CNSR=0 if is_disc else 1,
                    EVNTDESC="Treatment Discontinued Early"
                    if is_disc
                    else "Study Completed Successfully",
                    SAFFL=adsl.SAFFL,
                    ITTFL=adsl.ITTFL,
                )
            )

        return results

    def _derive_bds(
        self,
        vs_list: list[SDTMVS],
        lb_list: list[SDTMLB],
        adsl_map: dict[str, ADaMADSL],
    ) -> list[ADaMBDS]:
        """Derive Basic Data Structure (BDS) for parameterized continuous observations (VS & LB)."""
        results: list[ADaMBDS] = []

        # 1. Process Vital Signs into BDS
        # Group by (USUBJID, VSTESTCD)
        vs_groups: dict[tuple[str, str], list[SDTMVS]] = {}
        for vs in vs_list:
            vs_groups.setdefault((vs.USUBJID, vs.VSTESTCD), []).append(vs)

        for (usubjid, testcd), group in vs_groups.items():
            adsl = adsl_map.get(usubjid)
            siteid = adsl.SITEID if adsl else "SITE-01"
            arm = adsl.ARM if adsl else "Active Treatment"
            actarm = adsl.ACTARM if adsl else "Active Treatment"
            trtsdt = adsl.TRTSDT if adsl else None

            # Find baseline record
            base_val: float | None = None
            for item in group:
                if item.VSBLFL == "Y" and item.VSSTRESN is not None:
                    base_val = item.VSSTRESN
                    break
            if base_val is None:
                for item in group:
                    if (
                        item.VSDY is None or item.VSDY <= 1
                    ) and item.VSSTRESN is not None:
                        base_val = item.VSSTRESN
                        break

            for item in group:
                aval = item.VSSTRESN
                chg = None
                pchg = None
                if aval is not None and base_val is not None:
                    chg = round(aval - base_val, 2)
                    if base_val != 0:
                        pchg = round(((aval - base_val) / base_val) * 100.0, 2)

                results.append(
                    ADaMBDS(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=usubjid.replace(f"{self.study_id}-", ""),
                        SITEID=siteid,
                        ARM=arm,
                        ACTARM=actarm,
                        TRTSDT=trtsdt,
                        PARAMCD=item.VSTESTCD,
                        PARAM=item.VSTEST,
                        PARAMN=1,
                        AVAL=aval,
                        AVALC=str(aval) if aval is not None else item.VSSTRESC,
                        BASE=base_val,
                        CHG=chg,
                        PCHG=pchg,
                        ADTC=item.VSDTC,
                        ADY=item.VSDY,
                        AVISIT=item.VISIT,
                        AVISITN=item.VISITNUM,
                        ABLFL=item.VSBLFL,
                        ANRIND=None,
                        BNRIND=None,
                    )
                )

        # 2. Process Laboratory Findings into BDS
        lb_groups: dict[tuple[str, str], list[SDTMLB]] = {}
        for lb in lb_list:
            lb_groups.setdefault((lb.USUBJID, lb.LBTESTCD), []).append(lb)

        for (usubjid, testcd), group in lb_groups.items():
            adsl = adsl_map.get(usubjid)
            siteid = adsl.SITEID if adsl else "SITE-01"
            arm = adsl.ARM if adsl else "Active Treatment"
            actarm = adsl.ACTARM if adsl else "Active Treatment"
            trtsdt = adsl.TRTSDT if adsl else None

            base_val = None
            base_nrind = None
            for item in group:
                if item.LBBLFL == "Y" and item.LBSTRESN is not None:
                    base_val = item.LBSTRESN
                    base_nrind = item.LBNRIND
                    break
            if base_val is None:
                for item in group:
                    if (
                        item.LBDY is None or item.LBDY <= 1
                    ) and item.LBSTRESN is not None:
                        base_val = item.LBSTRESN
                        base_nrind = item.LBNRIND
                        break

            for item in group:
                aval = item.LBSTRESN
                chg = None
                pchg = None
                if aval is not None and base_val is not None:
                    chg = round(aval - base_val, 2)
                    if base_val != 0:
                        pchg = round(((aval - base_val) / base_val) * 100.0, 2)

                results.append(
                    ADaMBDS(
                        STUDYID=self.study_id,
                        USUBJID=usubjid,
                        SUBJID=usubjid.replace(f"{self.study_id}-", ""),
                        SITEID=siteid,
                        ARM=arm,
                        ACTARM=actarm,
                        TRTSDT=trtsdt,
                        PARAMCD=item.LBTESTCD,
                        PARAM=item.LBTEST,
                        PARAMN=2,
                        AVAL=aval,
                        AVALC=str(aval) if aval is not None else item.LBSTRESC,
                        BASE=base_val,
                        CHG=chg,
                        PCHG=pchg,
                        ADTC=item.LBDTC,
                        ADY=item.LBDY,
                        AVISIT=item.VISIT,
                        AVISITN=item.VISITNUM,
                        ABLFL=item.LBBLFL,
                        ANRIND=item.LBNRIND,
                        BNRIND=base_nrind,
                    )
                )

        return results
