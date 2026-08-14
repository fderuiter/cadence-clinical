"""Standalone Empirical Adversarial Challenge Runner for Phase 1 Deliverables.

Executes all adversarial challenge test scenarios directly and outputs a structured empirical report.
"""

import asyncio
import io
import json
import math
import os
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from datetime import UTC, datetime

# Ensure project root is in sys.path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "../..")))

from apps.execution.biostat.csv_export import generate_deidentified_csv
from apps.execution.biostat.odm_xml import generate_odm_xml
from apps.execution.biostat.serializer import serialize_dataset_json
from apps.execution.biostat.xpt import double_to_ibm, generate_sas_xpt, ibm_to_double
from apps.execution.coding.impact import analyze_upversioning_impact
from apps.execution.coding.matcher import find_fuzzy_matches
from apps.execution.coding.service import MedicalCodingService
from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalCodingAssignment,
    ClinicalObservation,
    ClinicalSubject,
    DataLock,
    FormSubmission,
    MedDRATerm,
)
from apps.execution.lab_ranges import (
    convert_lab_unit,
    evaluate_lab_value,
)
from apps.execution.services.lab_ingestion_service import (
    parse_csv_payload,
    parse_fhir_payload,
    parse_hl7_v2_payload,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security.sig_token_verifier import (
    token_consumption_cache,
    verify_and_consume_sig_token,
)


async def reset_db():
    TrialLockManager.reset()
    token_consumption_cache.clear()
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, "sqlite")
    current_user_id.set("adversarial_challenger_user")
    current_change_reason.set("Phase 1 Adversarial Empirical Challenge")


async def run_all_challenges():
    results = []
    print("=" * 80)
    print("CADENCE CLINICAL PLATFORM — PHASE 1 EMPIRICAL ADVERSARIAL CHALLENGE HARNESS")
    print("=" * 80)

    # --------------------------------------------------------------------------
    # 1. DATA LOCK INTERCEPTION & 6-TIER INHERITANCE
    # --------------------------------------------------------------------------
    print("\n[DOMAIN 1: DATA LOCK INTERCEPTION & HIERARCHICAL GATING]")
    await reset_db()

    # Challenge 1.1: 6-Tier Lock Inheritance
    t0 = time.perf_counter()
    study_id, site_id, subject_id, visit_id, form_id, field_name = (
        "STUDY-001",
        "SITE-001",
        "SUBJ-001",
        "VISIT-01",
        "FORM-01",
        "SYSBP",
    )

    async with db_manager.get_session_maker()() as session, session.begin():
        subj = ClinicalSubject(
            id=subject_id,
            subject_id=subject_id,
            study_id=study_id,
            site_id=site_id,
            status="ENROLLED",
        )
        form = FormSubmission(
            id=form_id,
            subject_id=subject_id,
            study_id=study_id,
            site_id=site_id,
            visit_id=visit_id,
            form_id=form_id,
            status="DRAFT",
        )
        session.add_all([subj, form])

    # 1.1 Study Lock Rejection
    TrialLockManager.lock_trial(reason="Study locked")
    study_locked_blocked = False
    try:
        async with db_manager.get_session_maker()() as session:
            obs = ClinicalObservation(
                id="obs_1",
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                visit_id=visit_id,
                form_id=form_id,
                test_code=field_name,
                numeric_value=120.0,
            )
            session.add(obs)
            await session.commit()
    except PermissionError:
        study_locked_blocked = True
    TrialLockManager.unlock_trial()

    # 1.2 Site Lock Rejection
    TrialLockManager.lock_site(site_id)
    site_locked_blocked = False
    try:
        async with db_manager.get_session_maker()() as session:
            obs = ClinicalObservation(
                id="obs_2",
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                visit_id=visit_id,
                form_id=form_id,
                test_code=field_name,
                numeric_value=120.0,
            )
            session.add(obs)
            await session.commit()
    except PermissionError:
        site_locked_blocked = True
    TrialLockManager.unlock_site(site_id)

    # 1.3 Subject Lock Rejection
    TrialLockManager.lock_subject(subject_id)
    subj_locked_blocked = False
    try:
        async with db_manager.get_session_maker()() as session:
            obs = ClinicalObservation(
                id="obs_3",
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                visit_id=visit_id,
                form_id=form_id,
                test_code=field_name,
                numeric_value=120.0,
            )
            session.add(obs)
            await session.commit()
    except PermissionError:
        subj_locked_blocked = True
    TrialLockManager.unlock_subject(subject_id)

    # 1.4 Visit Lock Rejection
    TrialLockManager.lock_visit(visit_id)
    visit_locked_blocked = False
    try:
        async with db_manager.get_session_maker()() as session:
            obs = ClinicalObservation(
                id="obs_4",
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                visit_id=visit_id,
                form_id=form_id,
                test_code=field_name,
                numeric_value=120.0,
            )
            session.add(obs)
            await session.commit()
    except PermissionError:
        visit_locked_blocked = True
    TrialLockManager.unlock_visit(visit_id)

    # 1.5 Form Lock Rejection
    TrialLockManager.lock_form(form_id)
    form_locked_blocked = False
    try:
        async with db_manager.get_session_maker()() as session:
            obs = ClinicalObservation(
                id="obs_5",
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                visit_id=visit_id,
                form_id=form_id,
                test_code=field_name,
                numeric_value=120.0,
            )
            session.add(obs)
            await session.commit()
    except PermissionError:
        form_locked_blocked = True
    TrialLockManager.unlock_form(form_id)

    # 1.6 Field Lock Rejection
    TrialLockManager.lock_field(field_name, form_id)
    field_locked_blocked = False
    try:
        async with db_manager.get_session_maker()() as session:
            obs = ClinicalObservation(
                id="obs_6",
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                visit_id=visit_id,
                form_id=form_id,
                test_code=field_name,
                numeric_value=120.0,
            )
            session.add(obs)
            await session.commit()
    except PermissionError:
        field_locked_blocked = True
    TrialLockManager.unlock_field(field_name, form_id)

    # Post-unlock write success
    post_unlock_ok = False
    async with db_manager.get_session_maker()() as session, session.begin():
        obs_ok = ClinicalObservation(
            id="obs_ok",
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_id=visit_id,
            form_id=form_id,
            test_code=field_name,
            numeric_value=120.0,
        )
        session.add(obs_ok)
        post_unlock_ok = True

    tier_success = all([
        study_locked_blocked,
        site_locked_blocked,
        subj_locked_blocked,
        visit_locked_blocked,
        form_locked_blocked,
        field_locked_blocked,
        post_unlock_ok,
    ])
    dt = (time.perf_counter() - t0) * 1000
    results.append((
        "1.1",
        "6-Tier Hierarchical Lock Mutation Interception",
        "PASS" if tier_success else "FAIL",
        f"All 6 tiers rejected blocked mutations & recovered after unlock ({dt:.1f}ms)",
    ))
    print(f"  [1.1] 6-Tier Lock Inheritance: {'PASS' if tier_success else 'FAIL'} ({dt:.1f}ms)")

    # --------------------------------------------------------------------------
    # 2. LAB INGESTION RESILIENCE
    # --------------------------------------------------------------------------
    print("\n[DOMAIN 2: LAB BATCH INGESTION RESILIENCE]")
    t0 = time.perf_counter()

    # 2.1 Delimited CSV parsing resilience
    csv_dirty = """PatientID;TestCode;Result;Unit;Date\nSUBJ_01;K;4.2;mmol/L;2026-08-01\n;ALT;45;U/L;2026-08-01\nSUBJ_02;GLUC;5.6;mmol/L;invalid-date"""
    recs_csv, errs_csv = parse_csv_payload(csv_dirty)
    csv_ok = len(recs_csv) == 2 and len(errs_csv) == 1 and recs_csv[0].subject_id == "SUBJ_01"

    # 2.2 HL7 v2.x corruption resilience
    hl7_corrupt = "PID|1||SUBJ_HL7_01\nOBX|1|NM|K||4.2|mmol/L"
    recs_hl7, errs_hl7 = parse_hl7_v2_payload(hl7_corrupt)
    hl7_ok = len(errs_hl7) >= 1 and "MSH" in errs_hl7[0]["error"]

    valid_hl7 = "MSH|^~\\&|LAB|CLINIC|CADENCE|SPONSOR|20260810120000||ORU^R01|MSG001|P|2.5\nPID|1||SUBJ_HL7_02||DOE^J\nOBR|1||LAB123|CHEM7\nOBX|1|NM|K^Potassium||4.8|mmol/L|3.5-5.0|N|||F"
    recs_hl7_v, errs_hl7_v = parse_hl7_v2_payload(valid_hl7)
    hl7_ok = hl7_ok and len(recs_hl7_v) == 1 and recs_hl7_v[0].value == 4.8

    # 2.3 FHIR Observation JSON resilience
    fhir_invalid = {"resourceType": "Condition", "code": {"text": "Hypertension"}}
    recs_fhir, errs_fhir = parse_fhir_payload(fhir_invalid)
    fhir_ok = len(recs_fhir) == 0 and len(errs_fhir) >= 1

    # 2.4 Range evaluation & Critical SAE panic alerts
    s_norm, f_norm, c_norm = evaluate_lab_value(4.2, 3.5, 5.0, 2.5, 6.5)
    s_warn, f_warn, c_warn = evaluate_lab_value(5.6, 3.5, 5.0, 2.5, 6.5)
    s_crit, f_crit, c_crit = evaluate_lab_value(7.2, 3.5, 5.0, 2.5, 6.5)
    eval_ok = (
        s_norm == "NORMAL" and not c_norm
        and s_warn == "OUT_OF_RANGE_WARNING" and not c_warn
        and s_crit == "POTENTIAL_SAE_CRITICAL" and c_crit
    )

    # 2.5 UCUM unit conversions
    v_hgb, u_hgb = convert_lab_unit(14.0, "g/dL", "HGB")
    v_gluc, u_gluc = convert_lab_unit(90.0, "mg/dL", "GLUC")
    ucum_ok = (v_hgb == 140.0 and u_hgb == "g/L") and (math.isclose(v_gluc, 4.9959, rel_tol=1e-3))

    lab_all_ok = all([csv_ok, hl7_ok, fhir_ok, eval_ok, ucum_ok])
    dt = (time.perf_counter() - t0) * 1000
    results.append((
        "2.1",
        "Lab Ingestion (CSV, HL7, FHIR, Ranges, SAE, UCUM)",
        "PASS" if lab_all_ok else "FAIL",
        f"All adapters, range evaluations, critical SAE alerts & UCUM conversions validated ({dt:.1f}ms)",
    ))
    print(f"  [2.1] Lab Ingestion Resilience: {'PASS' if lab_all_ok else 'FAIL'} ({dt:.1f}ms)")

    # --------------------------------------------------------------------------
    # 3. MEDICAL CODING WORKBENCH & UP-VERSIONING IMPACT
    # --------------------------------------------------------------------------
    print("\n[DOMAIN 3: MEDICAL CODING WORKBENCH]")
    t0 = time.perf_counter()
    await reset_db()

    async with db_manager.get_session_maker()() as session, session.begin():
        t1 = MedDRATerm(dictionary_version="26.0", code="10020772", term="Hypertension", level="PT", soc_code="10047065", soc_name="Vascular disorders")
        t2 = MedDRATerm(dictionary_version="26.0", code="10019211", term="Headache", level="PT", soc_code="10029205", soc_name="Nervous system disorders")
        t_v25_1 = MedDRATerm(dictionary_version="25.0", code="10050", term="Cardiac Flutter", level="PT", soc_code="20001", soc_name="Cardiac disorders")
        t_v26_1 = MedDRATerm(dictionary_version="26.0", code="10050", term="Cardiac Flutter", level="PT", soc_code="20002", soc_name="Vascular disorders") # Shifted SOC
        session.add_all([t1, t2, t_v25_1, t_v26_1])

        # Add active assignment
        assign = ClinicalCodingAssignment(
            id="assign_100",
            observation_id="obs_cardiac",
            verbatim_term="Cardiac Flutter",
            dictionary_type="MEDDRA",
            dictionary_version="25.0",
            coded_code="10050",
            coded_term="Cardiac Flutter",
            status="APPROVED",
        )
        session.add(assign)

    async with db_manager.get_session_maker()() as session:
        # Fuzzy match
        m_exact = await find_fuzzy_matches(session, "MEDDRA", "26.0", "Hypertension")
        m_typo = await find_fuzzy_matches(session, "MEDDRA", "26.0", "hypertenssion")
        m_gibberish = await find_fuzzy_matches(session, "MEDDRA", "26.0", "???")
        fuzzy_ok = len(m_exact) > 0 and len(m_typo) > 0 and len(m_gibberish) == 0

    # Query Escalation
    async with db_manager.get_session_maker()() as session, session.begin():
        obs_code = ClinicalObservation(id="obs_esc_1", study_id="S1", test_code="AETERM", string_value="Uncodable gibberish")
        session.add(obs_code)
        q = await MedicalCodingService.raise_coding_query(
            session=session,
            observation_id="obs_esc_1",
            query_text="Unclear verbatim",
            user_id="coder_01",
            reason="Unresolvable term",
        )
        query_ok = q.id is not None and q.status == "OPEN"

    # Upversioning Impact
    async with db_manager.get_session_maker()() as session, session.begin():
        impact = await analyze_upversioning_impact(session, "MEDDRA", "25.0", "26.0")
        impact_ok = impact["affected_count"] >= 1

    coding_all_ok = all([fuzzy_ok, query_ok, impact_ok])
    dt = (time.perf_counter() - t0) * 1000
    results.append((
        "3.1",
        "Medical Coding (Fuzzy Matching, Query Escalation, Up-versioning)",
        "PASS" if coding_all_ok else "FAIL",
        f"Fuzzy matches, discrepancy queries & up-versioning impact analysis validated ({dt:.1f}ms)",
    ))
    print(f"  [3.1] Medical Coding Workbench: {'PASS' if coding_all_ok else 'FAIL'} ({dt:.1f}ms)")

    # --------------------------------------------------------------------------
    # 4. BIOSTAT EXPORTS: SAS XPT, IBM 360 FLOAT, ODM-XML & DATASET-JSON
    # --------------------------------------------------------------------------
    print("\n[DOMAIN 4: BIOSTAT EXPORTS & PRIVACY SCRUBBING]")
    t0 = time.perf_counter()

    # 4.1 IBM 360 Float Precision
    ibm_zero = ibm_to_double(double_to_ibm(0.0)) == 0.0
    ibm_none = ibm_to_double(double_to_ibm(None)) is None
    ibm_pi = math.isclose(ibm_to_double(double_to_ibm(3.141592653589793)), 3.141592653589793, rel_tol=1e-12)
    ibm_small = math.isclose(ibm_to_double(double_to_ibm(0.000012345)), 0.000012345, rel_tol=1e-12)
    ibm_ok = all([ibm_zero, ibm_none, ibm_pi, ibm_small])

    # 4.2 SAS XPT Binary Generation & Card Alignment
    xpt_bytes = generate_sas_xpt("VS", ["USUBJID", "SYSBP"], [{"USUBJID": "S1", "SYSBP": 120.0}])
    xpt_ok = len(xpt_bytes) % 80 == 0 and xpt_bytes.startswith(b"HEADER RECORD*******LIBRARY HEADER RECORD")

    # 4.3 CDISC ODM-XML v1.3.2 with <AuditRecord>
    odm_xml = generate_odm_xml(
        "STUDY-01",
        "Study 01",
        [{"study_id": "STUDY-01", "subject_id": "SUBJ-01", "form_id": "F1", "item_group_id": "IG1", "item_id": "SYSBP", "value": "120", "user_id": "crc_01", "timestamp": "2026-08-01T12:00:00Z", "reason_for_change": "Initial measurement"}],
    )
    root = ET.fromstring(odm_xml)
    audit_recs = root.findall(".//{http://www.cdisc.org/ns/odm/v1.3}AuditRecord")
    odm_ok = len(audit_recs) >= 1 and "ODM" in root.tag

    # 4.4 Dataset-JSON 1.0.0 Compliance
    djson = serialize_dataset_json(
        "STUDY-01",
        "DM",
        "Demographics",
        [{"OID": "IT.DM.USUBJID", "name": "USUBJID", "label": "Subject ID", "type": "string"}],
        [["SUBJ-01"]],
    )
    djson_obj = json.loads(djson)
    djson_ok = djson_obj.get("datasetJsonVersion") == "1.0.0" and ("clinicalData" in djson_obj or "datasetData" in djson_obj)

    # 4.5 De-identified CSV
    csv_deid = generate_deidentified_csv(
        [{"USUBJID": "SUBJ-01", "PATIENT_NAME": "John Doe", "SSN": "123-45-6789", "SYSBP": 120}],
        study_salt="secret_salt",
    )
    deid_ok = "John Doe" not in csv_deid and "123-45-6789" not in csv_deid and "120" in csv_deid

    biostat_all_ok = all([ibm_ok, xpt_ok, odm_ok, djson_ok, deid_ok])
    dt = (time.perf_counter() - t0) * 1000
    results.append((
        "4.1",
        "Biostat Exports (SAS XPT, IBM 360 Float, ODM-XML, Dataset-JSON, De-ID CSV)",
        "PASS" if biostat_all_ok else "FAIL",
        f"IBM 360 float precision, 80-byte card alignment, ODM AuditRecords & Dataset-JSON 1.0.0 validated ({dt:.1f}ms)",
    ))
    print(f"  [4.1] Biostat Exports: {'PASS' if biostat_all_ok else 'FAIL'} ({dt:.1f}ms)")

    print("\n" + "=" * 80)
    print("SUMMARY OF EMPIRICAL ADVERSARIAL CHALLENGE RESULTS:")
    print("=" * 80)
    for code, title, status, details in results:
        print(f"[{code}] {title:65s} | {status} | {details}")
    print("=" * 80)
    return results


if __name__ == "__main__":
    asyncio.run(run_all_challenges())
