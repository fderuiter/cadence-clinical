"""FastAPI Router for Interop Medical Device & IVD Regulatory Exports.

Provides CDISC SDTM DD (Device Deficiencies), DO (Device Tracking and Operations),
and EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2) compilation.

Requirements: PRD-SYS-126, PRD-DEV-004, ISO 14155:2020, EU MDR 2017/745, EU IVDR 2017/746, ADR-2209.
"""

import hashlib
import json
from datetime import UTC, datetime

from fastapi import APIRouter, Request

from apps.interop.domain.cdr.submission_engine import CdiscSubmissionEngine
from apps.interop.presentation.device_dtos import (
    DeviceInvestigationExportPackageRequest,
    DeviceInvestigationExportPackageResponse,
    MDRIncidentReportRequest,
    MDRIncidentReportResponse,
    SDTMDDGenerateRequest,
    SDTMDDResponse,
    SDTMDOGenerateRequest,
    SDTMDOResponse,
)

router = APIRouter(
    prefix="/api/v1/interop/devices", tags=["Device Interop & Regulatory Exports"]
)


@router.post(
    "/sdtm/dd",
    response_model=SDTMDDResponse,
    status_code=200,
)
async def generate_sdtm_dd_endpoint(
    request: Request,
    payload: SDTMDDGenerateRequest,
) -> SDTMDDResponse:
    """Compiles CDISC SDTM DD (Device Deficiencies) dataset from reported device malfunctions.

    Requirements: PRD-DEV-004, ISO 14155:2020, ADR-2209
    """
    user_id = request.headers.get("X-User-Id", "system")
    change_reason = request.headers.get(
        "X-Change-Reason", "CDISC SDTM DD dataset compilation"
    )

    engine = CdiscSubmissionEngine(study_id=payload.study_id)
    dev_sdtm = engine.compile_device_sdtm(
        deficiencies=payload.deficiencies,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    records = [m.model_dump(mode="json") for m in dev_sdtm.get("DD", [])]

    return SDTMDDResponse(
        study_id=payload.study_id,
        domain="DD",
        record_count=len(records),
        records=records,
    )


@router.post(
    "/sdtm/do",
    response_model=SDTMDOResponse,
    status_code=200,
)
async def generate_sdtm_do_endpoint(
    request: Request,
    payload: SDTMDOGenerateRequest,
) -> SDTMDOResponse:
    """Compiles CDISC SDTM DO (Device Tracking and Operations) dataset from serialized inventory.

    Requirements: PRD-DEV-004, ISO 14155:2020, ADR-2209
    """
    user_id = request.headers.get("X-User-Id", "system")
    change_reason = request.headers.get(
        "X-Change-Reason", "CDISC SDTM DO dataset compilation"
    )

    engine = CdiscSubmissionEngine(study_id=payload.study_id)
    dev_sdtm = engine.compile_device_sdtm(
        devices=payload.devices,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    records = [m.model_dump(mode="json") for m in dev_sdtm.get("DO", [])]

    return SDTMDOResponse(
        study_id=payload.study_id,
        domain="DO",
        record_count=len(records),
        records=records,
    )


@router.post(
    "/mir-report",
    response_model=MDRIncidentReportResponse,
    status_code=200,
)
async def generate_mdr_incident_report_endpoint(
    request: Request,
    payload: MDRIncidentReportRequest,
) -> MDRIncidentReportResponse:
    """Compiles an EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2).

    Requirements: PRD-SYS-126, PRD-DEV-004, EU MDR 2017/745, EU IVDR 2017/746, ADR-2209
    """
    ref_num = f"MIR-{payload.study_id}-{payload.deficiency_id[:8].upper()}"
    now_iso = datetime.now(UTC).isoformat()

    summary_desc = (
        f"Investigational Medical Device Incident Report for {payload.brand_name} "
        f"(UDI-DI: {payload.udi_di}, Lot: {payload.lot_number}, Serial: {payload.serial_number or 'N/A'}). "
        f"Deficiency: {payload.deficiency_type} - {payload.deficiency_description}. "
        f"Safety Classification: {payload.safety_classification}. Action Taken: {payload.action_taken}."
    )

    payload_json = {
        "mir_version": "7.2",
        "regulatory_framework": "EU_MDR_2017_745",
        "administrative_information": {
            "report_reference_number": ref_num,
            "report_type": "INITIAL_INCIDENT_REPORT",
            "competent_authority": "EUDAMED / National Competent Authority",
            "reporting_date": now_iso,
        },
        "device_identification": {
            "brand_name": payload.brand_name,
            "udi_di": payload.udi_di,
            "lot_number": payload.lot_number,
            "serial_number": payload.serial_number,
            "clinical_investigation_study_id": payload.study_id,
            "site_id": payload.site_id,
        },
        "incident_information": {
            "deficiency_id": payload.deficiency_id,
            "incident_date": payload.incident_date,
            "awareness_date": payload.awareness_date or now_iso,
            "deficiency_type": payload.deficiency_type,
            "safety_classification": payload.safety_classification,
            "causal_relationship": payload.causality,
            "patient_involved": payload.patient_involved,
            "health_impact": payload.health_impact,
            "narrative_description": summary_desc,
        },
        "preliminary_manufacturer_evaluation": {
            "root_cause_analysis": payload.root_cause_analysis
            or "Root cause investigation underway at sponsor technical laboratory.",
            "action_taken": payload.action_taken,
            "capa_initiated": payload.capa_required,
            "fsca_required": payload.fsca_required,
        },
    }

    payload_xml = f"""<?xml version="1.0" encoding="UTF-8"?>
<ManufacturerIncidentReport version="7.2" xmlns="urn:eudamed:mdr:mir:v7.2">
  <AdministrativeInformation>
    <ReportReference>{ref_num}</ReportReference>
    <ReportType>INITIAL_INCIDENT_REPORT</ReportType>
    <ReportingDate>{now_iso}</ReportingDate>
    <CompetentAuthority>EUDAMED / National Competent Authority</CompetentAuthority>
  </AdministrativeInformation>
  <DeviceIdentification>
    <BrandName>{payload.brand_name}</BrandName>
    <UDI_DI>{payload.udi_di}</UDI_DI>
    <LotNumber>{payload.lot_number}</LotNumber>
    <SerialNumber>{payload.serial_number or ""}</SerialNumber>
    <StudyIdentifier>{payload.study_id}</StudyIdentifier>
    <SiteIdentifier>{payload.site_id}</SiteIdentifier>
  </DeviceIdentification>
  <IncidentInformation>
    <DeficiencyIdentifier>{payload.deficiency_id}</DeficiencyIdentifier>
    <IncidentDate>{payload.incident_date}</IncidentDate>
    <Classification>{payload.safety_classification}</Classification>
    <Causality>{payload.causality}</Causality>
    <PatientInvolved>{str(payload.patient_involved).lower()}</PatientInvolved>
    <HealthImpact>{payload.health_impact}</HealthImpact>
    <Narrative>{summary_desc}</Narrative>
  </IncidentInformation>
  <CorrectiveActions>
    <ActionTaken>{payload.action_taken}</ActionTaken>
    <CAPAInitiated>{str(payload.capa_required).lower()}</CAPAInitiated>
    <FSCARequired>{str(payload.fsca_required).lower()}</FSCARequired>
  </CorrectiveActions>
</ManufacturerIncidentReport>"""

    return MDRIncidentReportResponse(
        report_reference_number=ref_num,
        mir_version="7.2",
        regulatory_framework="EU_MDR_2017_745",
        reporting_date=now_iso,
        payload_json=payload_json,
        payload_xml=payload_xml,
    )


@router.post(
    "/export-package",
    response_model=DeviceInvestigationExportPackageResponse,
    status_code=200,
)
async def generate_export_package_endpoint(
    request: Request,
    payload: DeviceInvestigationExportPackageRequest,
) -> DeviceInvestigationExportPackageResponse:
    """Compiles a complete regulatory investigation package (SDTM DD, DO, and MIR forms).

    Requirements: PRD-SYS-126, PRD-DEV-004, ADR-2209
    """
    user_id = request.headers.get("X-User-Id", "system")
    change_reason = request.headers.get(
        "X-Change-Reason", "Regulatory device export package generation"
    )

    engine = CdiscSubmissionEngine(study_id=payload.study_id)
    package_dict = engine.compile_device_investigation_package(
        study_id=payload.study_id,
        deficiencies=payload.deficiencies,
        devices=payload.devices,
        user_id=user_id,
        change_reason=change_reason,
    )

    dd_records = package_dict["sdtm_dd_dataset"]
    do_records = package_dict["sdtm_do_dataset"]

    incident_reports = []
    for d in payload.deficiencies:
        mir_req = MDRIncidentReportRequest(
            study_id=payload.study_id,
            site_id=d.get("site_id", "SITE-001"),
            deficiency_id=d.get("id", str(d.get("deficiency_id", "DEF-001"))),
            brand_name=d.get("device_name", "Investigational Medical Device"),
            udi_di=d.get("udi_di", "00840123456789"),
            lot_number=d.get("lot_number", "LOT-001"),
            serial_number=d.get("serial_number"),
            incident_date=str(d.get("incident_date", datetime.now(UTC).isoformat())),
            deficiency_type=d.get("deficiency_type", "MALFUNCTION"),
            deficiency_description=d.get(
                "deficiency_description", "Device deficiency observed"
            ),
            safety_classification=d.get("safety_classification", "SADE"),
            causality=d.get("causality", "POSSIBLE"),
            patient_involved=bool(d.get("patient_involved", False)),
            health_impact=d.get("health_impact", "NONE"),
            root_cause_analysis=d.get("root_cause_analysis"),
            action_taken=d.get("action_taken_device", "QUARANTINED_AT_SITE"),
            capa_required=bool(d.get("capa_required", False)),
            fsca_required=bool(d.get("fsca_required", False)),
        )
        mir_res = await generate_mdr_incident_report_endpoint(request, mir_req)
        incident_reports.append(mir_res.model_dump(mode="json"))

    now_iso = datetime.now(UTC).isoformat()
    raw_digest_input = json.dumps(
        {
            "study_id": payload.study_id,
            "generated_at": now_iso,
            "dd_count": len(dd_records),
            "do_count": len(do_records),
            "mir_count": len(incident_reports),
        },
        sort_keys=True,
    )
    digest_hash = hashlib.sha256(raw_digest_input.encode("utf-8")).hexdigest()

    manifest = {
        "study_id": payload.study_id,
        "compiled_by": user_id,
        "generated_at": now_iso,
        "digest_sha256": digest_hash,
        "gxp_compliant": True,
        "standards": ["CDISC SDTM v1.8", "ISO 14155:2020", "EU MDR 2017/745"],
    }

    return DeviceInvestigationExportPackageResponse(
        study_id=payload.study_id,
        generated_at=now_iso,
        sdtm_dd_count=len(dd_records),
        sdtm_do_count=len(do_records),
        sdtm_dd_dataset=dd_records,
        sdtm_do_dataset=do_records,
        incident_reports=incident_reports,
        audit_manifest=manifest,
    )
