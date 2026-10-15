"""CDISC Dataset-JSON v1.1 & Columnar SDTM Extraction Service.

Extracts clinical observations collected via Tripartite Schemas and transforms
them into standardized CDISC Dataset-JSON and SDTM tabular datasets (DM, AE, VS, LB).
"""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any

from packages.ddf.tripartite import TripartiteFormDefinition

DOMAIN_LABELS: dict[str, str] = {
    "DM": "Demographics",
    "AE": "Adverse Events",
    "CM": "Concomitant Medications",
    "VS": "Vital Signs",
    "LB": "Laboratory Findings",
    "EX": "Exposure",
    "DS": "Disposition",
    "TU": "Tumor Identification",
    "TR": "Tumor Results",
    "RS": "Disease Response",
}


class DatasetJsonEtlService:
    """High-performance columnar ETL service converting eCRF records to CDISC Dataset-JSON."""

    @staticmethod
    def extract_sdtm_domain(
        form_def: TripartiteFormDefinition,
        raw_observations: list[dict[str, Any]],
        domain: str = "VS",
    ) -> dict[str, Any]:
        """Transform raw observation records into CDISC SDTM domain Dataset-JSON v1.1."""
        now_iso = datetime.now(UTC).isoformat()
        protocol_id = form_def.protocol_id or "STUDY"
        item_group_oid = f"IG.{domain}"
        domain_label = DOMAIN_LABELS.get(domain, f"{domain} Domain")

        # Standard variable metadata items for SDTM domain
        items = [
            {
                "OID": f"IT.{domain}.STUDYID",
                "name": "STUDYID",
                "label": "Study Identifier",
                "type": "string",
                "dataType": "string",
            },
            {
                "OID": f"IT.{domain}.USUBJID",
                "name": "USUBJID",
                "label": "Unique Subject Identifier",
                "type": "string",
                "dataType": "string",
            },
            {
                "OID": f"IT.{domain}.DOMAIN",
                "name": "DOMAIN",
                "label": "Domain Abbreviation",
                "type": "string",
                "dataType": "string",
            },
            {
                "OID": f"IT.{domain}.{domain}TESTCD",
                "name": f"{domain}TESTCD",
                "label": f"{domain} Short Name",
                "type": "string",
                "dataType": "string",
            },
            {
                "OID": f"IT.{domain}.{domain}ORRES",
                "name": f"{domain}ORRES",
                "label": "Original Result",
                "type": "string",
                "dataType": "string",
            },
            {
                "OID": f"IT.{domain}.{domain}ORRESU",
                "name": f"{domain}ORRESU",
                "label": "Original Units",
                "type": "string",
                "dataType": "string",
            },
        ]

        if not raw_observations:
            item_data: list[list[Any]] = []
        else:
            var_map_dict = {
                var.id: (var.cdisc.variable_name, var.cdisc.ucum_unit or "")
                for var in form_def.data_schema.variables
                if var.cdisc.domain == domain
            }

            sample = raw_observations[:100]
            present_var_ids = [
                vid for vid in var_map_dict if any(vid in r for r in sample)
            ]

            item_data = []
            usubjid_cache: dict[str, str] = {}

            for var_id in present_var_ids:
                sdtm_var, unit = var_map_dict[var_id]
                rows = [
                    [
                        sid := (r["study_id"] if "study_id" in r else protocol_id),
                        (
                            r["usubjid"]
                            if "usubjid" in r
                            else (
                                usubjid_cache.get(
                                    sub := (
                                        r["subject_id"]
                                        if "subject_id" in r
                                        else "UNKNOWN"
                                    )
                                )
                                or usubjid_cache.setdefault(sub, f"{sid}-{sub}")
                            )
                        ),
                        domain,
                        sdtm_var,
                        str(r[var_id]),
                        unit,
                    ]
                    for r in raw_observations
                    if var_id in r
                ]
                item_data.extend(rows)

        return {
            "creationDateTime": now_iso,
            "datasetJSONCreationDateTime": now_iso,
            "datasetJSONVersion": "1.1.0",
            "fileOID": f"FILE.{protocol_id}.{domain}.DATASET-JSON",
            "originator": "Cadence Clinical CDR Engine",
            "sourceSystem": "Cadence Clinical",
            "sourceSystemVersion": "2.1.0",
            "clinicalData": {
                "studyOID": protocol_id,
                "metaDataVersionOID": "MDV.CDISC.001",
                "metaDataRef": "define.xml",
                "itemGroupData": {
                    item_group_oid: {
                        "itemGroupOID": item_group_oid,
                        "name": domain,
                        "label": domain_label,
                        "records": len(item_data),
                        "items": items,
                        "itemData": item_data,
                    }
                },
            },
        }

