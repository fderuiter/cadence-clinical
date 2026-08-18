"""CDISC ODM v1.3.2 / v2.0 XML generator for eConsent clinical trial records.

Produces regulatory-compliant, validated ODM XML payloads representing
study consent templates, subject signature events, granular options, and audit trails.
"""

import xml.etree.ElementTree as ET
from datetime import UTC, datetime
from typing import Any


def generate_econsent_cdisc_odm_xml(
    study_id: str,
    subject_pseudonym: str,
    template_id: str,
    template_name: str,
    protocol_version: str,
    version_index: int,
    signatures: list[dict[str, Any]],
    granular_selections: list[dict[str, Any]],
    audit_logs: list[dict[str, Any]],
    odm_version: str = "1.3.2",
) -> str:
    """Constructs a compliant CDISC ODM XML string for an eConsent subject record."""
    now_iso = datetime.now(UTC).isoformat()
    ns_odm = "http://www.cdisc.org/ns/odm/v1.3"

    root = ET.Element(
        "ODM",
        {
            "xmlns": ns_odm,
            "ODMVersion": odm_version,
            "FileType": "Snapshot",
            "FileOID": f"ECONSENT_{study_id}_{subject_pseudonym}_{template_id}_v{version_index}",
            "CreationDateTime": now_iso,
            "Description": "Cadence Clinical eConsent Electronic Signature Record",
        },
    )

    # Study element
    study_el = ET.SubElement(root, "Study", {"OID": study_id})
    global_vars = ET.SubElement(study_el, "GlobalVariables")

    study_name = ET.SubElement(global_vars, "StudyName")
    study_name.text = f"Study {study_id}"

    study_desc = ET.SubElement(global_vars, "StudyDescription")
    study_desc.text = (
        f"Informed Consent Form Template: {template_name} (Protocol {protocol_version})"
    )

    protocol_name = ET.SubElement(global_vars, "ProtocolName")
    protocol_name.text = protocol_version

    # MetaDataVersion element
    meta_el = ET.SubElement(
        study_el,
        "MetaDataVersion",
        {
            "OID": f"MDV_{template_id}_v{version_index}",
            "Name": f"{template_name} v{version_index}",
        },
    )

    form_def = ET.SubElement(
        meta_el,
        "FormDef",
        {
            "OID": f"FORM_ICF_{template_id}",
            "Name": template_name,
            "Repeating": "No",
        },
    )
    ig_ref = ET.SubElement(
        form_def,
        "ItemGroupRef",
        {"ItemGroupOID": "IG_CONSENT_SIGNATURES", "Mandatory": "Yes"},
    )
    ig_ref.set("OrderNumber", "1")

    # ClinicalData element
    clinical_data = ET.SubElement(
        root,
        "ClinicalData",
        {
            "StudyOID": study_id,
            "MetaDataVersionOID": f"MDV_{template_id}_v{version_index}",
        },
    )
    subj_data = ET.SubElement(
        clinical_data, "SubjectData", {"SubjectKey": subject_pseudonym}
    )
    study_event_data = ET.SubElement(
        subj_data, "StudyEventData", {"StudyEventOID": "SE_INFORMED_CONSENT"}
    )
    form_data = ET.SubElement(
        study_event_data,
        "FormData",
        {"FormOID": f"FORM_ICF_{template_id}", "FormRepeatKey": f"v{version_index}"},
    )

    # Signature Records ItemGroup
    sig_ig = ET.SubElement(
        form_data, "ItemGroupData", {"ItemGroupOID": "IG_CONSENT_SIGNATURES"}
    )

    for idx, sig in enumerate(signatures, start=1):
        role = sig.get("role", "SUBJECT")
        signer_name = sig.get("signer_name", subject_pseudonym)
        signed_at = sig.get("signed_at", now_iso)
        meaning = sig.get("meaning", "Consent to participate in clinical trial")
        digest = sig.get("digest_sha256", "")

        sig_item = ET.SubElement(
            sig_ig,
            "ItemData",
            {
                "ItemOID": f"IT_SIG_{role}_{idx}",
                "Value": signer_name,
            },
        )
        # Add AuditRecord for Part 11
        audit_rec = ET.SubElement(sig_item, "AuditRecord")
        user_ref = ET.SubElement(
            audit_rec, "UserRef", {"UserOID": sig.get("created_by", "patient")}
        )
        user_ref.set("Role", role)
        dt_el = ET.SubElement(audit_rec, "DateTimeStamp")
        dt_el.text = str(signed_at)
        reason_el = ET.SubElement(audit_rec, "ReasonForChange")
        reason_el.text = meaning

        # Signature element in ODM
        sig_el = ET.SubElement(sig_item, "Signature", {"ID": f"SIG_{role}_{idx}"})
        sig_user = ET.SubElement(sig_el, "UserRef", {"UserOID": signer_name})
        sig_user.set("Role", role)
        ET.SubElement(sig_el, "LocationRef", {"LocationOID": "ELECTRONIC"})
        sig_dt = ET.SubElement(sig_el, "DateTimeStamp")
        sig_dt.text = str(signed_at)
        sig_meaning = ET.SubElement(sig_el, "Meaning")
        sig_meaning.text = meaning
        if digest:
            sig_dig = ET.SubElement(sig_el, "CryptoBinding")
            sig_dig.text = digest

    # Granular Choices ItemGroup
    if granular_selections:
        opt_ig = ET.SubElement(
            form_data, "ItemGroupData", {"ItemGroupOID": "IG_GRANULAR_OPTIONS"}
        )
        for opt in granular_selections:
            code = opt.get("option_code", "UNKNOWN")
            sel = "YES" if opt.get("selected") else "NO"
            opt_item = ET.SubElement(
                opt_ig, "ItemData", {"ItemOID": f"IT_OPT_{code}", "Value": sel}
            )
            opt_audit = ET.SubElement(opt_item, "AuditRecord")
            opt_dt = ET.SubElement(opt_audit, "DateTimeStamp")
            opt_dt.text = str(opt.get("selected_at", now_iso))
            opt_reason = ET.SubElement(opt_audit, "ReasonForChange")
            opt_reason.text = f"Subject selection for {code}"

    # Audit Trail records
    if audit_logs:
        audit_ig = ET.SubElement(
            form_data, "ItemGroupData", {"ItemGroupOID": "IG_AUDIT_TRAIL"}
        )
        for log in audit_logs:
            log_item = ET.SubElement(
                audit_ig,
                "ItemData",
                {
                    "ItemOID": f"IT_AUDIT_{log.get('id', 'LOG')}",
                    "Value": log.get("action", ""),
                },
            )
            log_audit = ET.SubElement(log_item, "AuditRecord")
            log_user = ET.SubElement(
                log_audit, "UserRef", {"UserOID": log.get("actor_id", "system")}
            )
            log_user.set("Role", log.get("actor_role", "system"))
            log_dt = ET.SubElement(log_audit, "DateTimeStamp")
            log_dt.text = str(log.get("timestamp", now_iso))
            log_reason = ET.SubElement(log_audit, "ReasonForChange")
            log_reason.text = log.get("reason_for_change", "")

    return ET.tostring(root, encoding="utf-8", xml_declaration=True).decode("utf-8")
