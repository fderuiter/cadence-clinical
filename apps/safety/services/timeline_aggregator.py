"""Chronological Event Aggregator compiling de-identified clinical event streams from apps/execution.

Requirements: PRD-SYS-052
"""

import logging
from typing import Any

from apps.safety.domain.narrative_models import (
    ClinicalTimelineEvent,
    SubjectSafetyTimeline,
    TimelineEventType,
)

logger = logging.getLogger("timeline-aggregator")


def build_timeline_from_sdtm_records(
    study_id: str,
    subject_id: str,
    sdtm_bundle: dict[str, list[dict[str, Any]]],
    target_sae_key: str | None = None,
) -> SubjectSafetyTimeline:
    """Compiles normalized SDTM domain records into a unified chronological event stream.

    Args:
        study_id: Clinical study identifier.
        subject_id: Subject pseudonym or identifier.
        sdtm_bundle: Dictionary of SDTM records keyed by domain (DM, MH, CM, AE, LB, VS, EX).
        target_sae_key: Target index SAE key to identify the focal event.

    Returns:
        SubjectSafetyTimeline containing ordered and de-identified ClinicalTimelineEvents.
    """
    events: list[ClinicalTimelineEvent] = []

    # 1. Demographics (DM)
    dm_records = [
        r
        for r in sdtm_bundle.get("DM", [])
        if r.get("USUBJID") == subject_id or r.get("SUBJID") == subject_id
    ]
    for idx, dm in enumerate(dm_records, start=1):
        age = dm.get("AGE")
        ageu = dm.get("AGEU", "YEARS")
        sex = dm.get("SEX", "Unknown")
        race = dm.get("RACE", "Not reported")
        arm = dm.get("ARM") or dm.get("ACTARM", "Investigational Arm")
        rfstdtc = dm.get("RFSTDTC") or dm.get("RFICDTC")

        desc = (
            f"Subject {subject_id} is a {age}-year-old ({ageu}) {sex} ({race}) "
            f"enrolled in study {study_id} on treatment arm '{arm}'."
        )
        events.append(
            ClinicalTimelineEvent(
                event_id=f"EVT-DM-{idx:02d}",
                event_type=TimelineEventType.DEMOGRAPHICS,
                event_date=rfstdtc,
                title="Demographics & Baseline Enrollment",
                description=desc,
                domain="DM",
                sequence=idx,
                source_record_id=str(dm.get("SUBJID", subject_id)),
                details=dm,
            )
        )

    # 2. Medical History (MH)
    mh_records = [
        r
        for r in sdtm_bundle.get("MH", [])
        if r.get("USUBJID") == subject_id or r.get("SUBJID") == subject_id
    ]
    for idx, mh in enumerate(mh_records, start=1):
        term = mh.get("MHTERM") or mh.get("MHDECOD", "Medical condition")
        bodsys = mh.get("MHBODSYS", "")
        stdtc = mh.get("MHSTDTC")
        endtc = mh.get("MHENDTC")

        desc = f"Medical History: {term}"
        if bodsys:
            desc += f" ({bodsys})"
        if stdtc:
            desc += f", onset {stdtc}"
        if endtc:
            desc += f", resolved {endtc}"
        else:
            desc += " (ongoing)"

        events.append(
            ClinicalTimelineEvent(
                event_id=f"EVT-MH-{idx:02d}",
                event_type=TimelineEventType.MEDICAL_HISTORY,
                event_date=stdtc,
                title=f"Medical History: {term}",
                description=desc,
                domain="MH",
                sequence=idx,
                source_record_id=str(mh.get("MHSEQ", idx)),
                details=mh,
            )
        )

    # 3. Concomitant Medications (CM)
    cm_records = [
        r
        for r in sdtm_bundle.get("CM", [])
        if r.get("USUBJID") == subject_id or r.get("SUBJID") == subject_id
    ]
    for idx, cm in enumerate(cm_records, start=1):
        drug = cm.get("CMTRT") or cm.get("CMDECOD", "Concomitant Medication")
        dose = cm.get("CMDOSE", "")
        dosu = cm.get("CMDOSU", "")
        route = cm.get("CMROUTE", "")
        indic = cm.get("CMINDC", "")
        stdtc = cm.get("CMSTDTC")
        endtc = cm.get("CMENDTC")

        desc = f"Concomitant Medication: {drug}"
        if dose:
            desc += f" {dose} {dosu}".strip()
        if route:
            desc += f" via {route}"
        if indic:
            desc += f" for indication '{indic}'"
        if stdtc:
            desc += f" started {stdtc}"
        if endtc:
            desc += f", ended {endtc}"

        events.append(
            ClinicalTimelineEvent(
                event_id=f"EVT-CM-{idx:02d}",
                event_type=TimelineEventType.CONCOMITANT_MEDICATION,
                event_date=stdtc,
                title=f"ConMed: {drug}",
                description=desc,
                domain="CM",
                sequence=idx,
                source_record_id=str(cm.get("CMSEQ", idx)),
                details=cm,
            )
        )

    # 4. Study Drug Administration (EX)
    ex_records = [
        r
        for r in sdtm_bundle.get("EX", [])
        if r.get("USUBJID") == subject_id or r.get("SUBJID") == subject_id
    ]
    for idx, ex in enumerate(ex_records, start=1):
        trt = ex.get("EXTRT", "Investigational Product")
        dose = ex.get("EXDOSE", "")
        dosu = ex.get("EXDOSU", "mg")
        stdtc = ex.get("EXSTDTC")
        endtc = ex.get("EXENDTC")

        desc = f"Study Drug Administered: {trt} {dose} {dosu}".strip()
        if stdtc:
            desc += f" on {stdtc}"

        events.append(
            ClinicalTimelineEvent(
                event_id=f"EVT-EX-{idx:02d}",
                event_type=TimelineEventType.DRUG_ADMINISTRATION,
                event_date=stdtc,
                title=f"Study Drug: {trt}",
                description=desc,
                domain="EX",
                sequence=idx,
                source_record_id=str(ex.get("EXSEQ", idx)),
                details=ex,
            )
        )

    # 5. Adverse Events (AE)
    ae_records = [
        r
        for r in sdtm_bundle.get("AE", [])
        if r.get("USUBJID") == subject_id or r.get("SUBJID") == subject_id
    ]
    for idx, ae in enumerate(ae_records, start=1):
        term = ae.get("AETERM") or ae.get("AEDECOD", "Adverse Event")
        stdtc = ae.get("AESTDTC")
        endtc = ae.get("AEENDTC")
        sev = ae.get("AESEV", "Unknown severity")
        ser = ae.get("AESER", "N")
        rel = ae.get("AEREL", "Unknown relatedness")
        out = ae.get("AEOUT", "Unknown outcome")
        seq = ae.get("AESEQ", idx)
        actn = ae.get("AEACN", "")

        is_sae = str(ser).strip().upper() in {"Y", "YES", "TRUE", "1"}
        sae_tag = " [SERIOUS ADVERSE EVENT]" if is_sae else ""

        desc = (
            f"Adverse Event{sae_tag}: '{term}', Severity: {sev}, Causality: {rel}, "
            f"Outcome: {out}."
        )
        if stdtc:
            desc += f" Onset Date: {stdtc}."
        if endtc:
            desc += f" Resolution Date: {endtc}."
        if actn:
            desc += f" Action taken with study drug: {actn}."

        events.append(
            ClinicalTimelineEvent(
                event_id=f"EVT-AE-{idx:02d}",
                event_type=TimelineEventType.ADVERSE_EVENT,
                event_date=stdtc,
                title=f"AE: {term} ({sev})",
                description=desc,
                domain="AE",
                sequence=seq,
                source_record_id=str(seq),
                details=ae,
            )
        )

        # Check for hospitalization or dechallenge/rechallenge events inside AE record
        if is_sae and ae.get("AESHOSP") == "Y":
            events.append(
                ClinicalTimelineEvent(
                    event_id=f"EVT-HOSP-{idx:02d}",
                    event_type=TimelineEventType.HOSPITALIZATION,
                    event_date=stdtc,
                    title=f"Hospitalization due to {term}",
                    description=f"Subject required acute hospitalization secondary to {term} on {stdtc}.",
                    domain="AE",
                    sequence=seq,
                    source_record_id=str(seq),
                    details={"related_ae_term": term, "onset": stdtc},
                )
            )

        if actn and ("WITHDRAWN" in actn.upper() or "REDUCED" in actn.upper()):
            events.append(
                ClinicalTimelineEvent(
                    event_id=f"EVT-DECHAL-{idx:02d}",
                    event_type=TimelineEventType.DECHALLENGE_RECHALLENGE,
                    event_date=stdtc,
                    title=f"Dechallenge Action: {term}",
                    description=f"Study drug action taken ({actn}) following onset of {term}. Outcome: {out}.",
                    domain="AE",
                    sequence=seq,
                    source_record_id=str(seq),
                    details={"action": actn, "outcome": out},
                )
            )

    # 6. Diagnostic Labs (LB)
    lb_records = [
        r
        for r in sdtm_bundle.get("LB", [])
        if r.get("USUBJID") == subject_id or r.get("SUBJID") == subject_id
    ]
    for idx, lb in enumerate(lb_records, start=1):
        test = lb.get("LBTEST") or lb.get("LBTESTCD", "Lab Test")
        res = lb.get("LBORRES") or lb.get("LBSTRESN", "")
        unit = lb.get("LBORRESU") or lb.get("LBSTRESU", "")
        dtc = lb.get("LBDTC")
        high = lb.get("LBSTNRHI", "")
        low = lb.get("LBSTNRLO", "")
        flag = lb.get("LBNRIND", "")

        desc = f"Diagnostic Lab: {test} = {res} {unit}".strip()
        if low and high:
            desc += f" (Ref Range: {low} - {high} {unit})"
        if flag:
            desc += f" [Flag: {flag}]"
        if dtc:
            desc += f" on {dtc}"

        events.append(
            ClinicalTimelineEvent(
                event_id=f"EVT-LB-{idx:02d}",
                event_type=TimelineEventType.DIAGNOSTIC_LAB,
                event_date=dtc,
                title=f"Lab: {test} ({res} {unit})",
                description=desc,
                domain="LB",
                sequence=idx,
                source_record_id=str(lb.get("LBSEQ", idx)),
                details=lb,
            )
        )

    # 7. Sort events chronologically (fallback to stable ordering for events without date)
    def event_sort_key(ev: ClinicalTimelineEvent) -> tuple[str, int]:
        d = ev.event_date or "9999-99-99"
        # Precedence order when dates match:
        type_prio = {
            TimelineEventType.DEMOGRAPHICS: 1,
            TimelineEventType.MEDICAL_HISTORY: 2,
            TimelineEventType.DRUG_ADMINISTRATION: 3,
            TimelineEventType.CONCOMITANT_MEDICATION: 4,
            TimelineEventType.ADVERSE_EVENT: 5,
            TimelineEventType.HOSPITALIZATION: 6,
            TimelineEventType.DECHALLENGE_RECHALLENGE: 7,
            TimelineEventType.DIAGNOSTIC_LAB: 8,
        }
        return (d, type_prio.get(ev.event_type, 9))

    events.sort(key=event_sort_key)

    return SubjectSafetyTimeline(
        study_id=study_id,
        subject_id=subject_id,
        sae_event_key=target_sae_key,
        events=events,
    )
