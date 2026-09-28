"""CDISC Dataset-JSON v1.1 & Columnar SDTM Extraction Service.

Extracts clinical observations collected via Tripartite Schemas and transforms
them into standardized CDISC Dataset-JSON and SDTM tabular datasets (DM, AE, VS, LB).
"""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any

from packages.ddf.tripartite import TripartiteFormDefinition


class DatasetJsonEtlService:
    """High-performance columnar ETL service converting eCRF records to CDISC Dataset-JSON."""

    @staticmethod
    def extract_sdtm_domain(
        form_def: TripartiteFormDefinition,
        raw_observations: list[dict[str, Any]],
        domain: str = "VS",
    ) -> dict[str, Any]:
        """Transform raw observation records into CDISC SDTM domain Dataset-JSON."""
        if not raw_observations:
            return {
                "datasetJSONCreationDateTime": datetime.now(UTC).isoformat(),
                "records": 0,
                "domain": domain,
                "columns": [],
                "rows": [],
            }

        # Build column mapping from CDISC metadata
        var_to_sdtm = {}
        for var in form_def.data_schema.variables:
            if var.cdisc.domain == domain:
                var_to_sdtm[var.id] = var.cdisc.variable_name

        # Standard SDTM columns
        sdtm_columns = [
            {
                "itemGroupOID": f"IG.{domain}",
                "name": "STUDYID",
                "label": "Study Identifier",
                "dataType": "string",
            },
            {
                "itemGroupOID": f"IG.{domain}",
                "name": "USUBJID",
                "label": "Unique Subject Identifier",
                "dataType": "string",
            },
            {
                "itemGroupOID": f"IG.{domain}",
                "name": "DOMAIN",
                "label": "Domain Abbreviation",
                "dataType": "string",
            },
            {
                "itemGroupOID": f"IG.{domain}",
                "name": f"{domain}TESTCD",
                "label": f"{domain} Short Name",
                "dataType": "string",
            },
            {
                "itemGroupOID": f"IG.{domain}",
                "name": f"{domain}ORRES",
                "label": "Original Result",
                "dataType": "string",
            },
            {
                "itemGroupOID": f"IG.{domain}",
                "name": f"{domain}ORRESU",
                "label": "Original Units",
                "dataType": "string",
            },
        ]

        rows: list[list[Any]] = []
        for record in raw_observations:
            study_id = record.get("study_id", form_def.protocol_id)
            subject_id = record.get("subject_id", "UNKNOWN")
            usubjid = f"{study_id}-{subject_id}"

            for var_id, sdtm_var in var_to_sdtm.items():
                if var_id in record:
                    val = record[var_id]
                    # Find unit from variable definition
                    unit = next(
                        (
                            v.cdisc.ucum_unit
                            for v in form_def.data_schema.variables
                            if v.id == var_id
                        ),
                        None,
                    )
                    row = [
                        study_id,
                        usubjid,
                        domain,
                        sdtm_var,
                        str(val),
                        unit or "",
                    ]
                    rows.append(row)

        return {
            "datasetJSONCreationDateTime": datetime.now(UTC).isoformat(),
            "records": len(rows),
            "domain": domain,
            "columns": sdtm_columns,
            "rows": rows,
        }
