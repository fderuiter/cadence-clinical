"""CDISC-Native Smart Component Blocks Catalog.

Provides standard library of pre-configured clinical domain panels (Vital Signs, Adverse Events,
Lab Panels) with embedded CDISC CDASH/SDTM metadata and constraints.
"""

from __future__ import annotations

from typing import Any

from packages.ddf.tripartite import (
    CdiscMetadata,
    ClinicalDataType,
    ConstraintDefinition,
    DataSchema,
    FieldWidgetConfig,
    GridSection,
    LogicSchema,
    TripartiteFormDefinition,
    UiSchema,
    VariableDefinition,
    ViewportMode,
    WidgetType,
)


class SmartBlocksCatalogService:
    """Provides CDISC-native smart component blocks with embedded metadata."""

    @staticmethod
    def get_catalog() -> list[dict[str, Any]]:
        """Return library of standardized clinical smart blocks."""
        return [
            {
                "block_id": "BLOCK-VITAL-SIGNS",
                "name": "Standard Vital Signs Panel",
                "domain": "VS",
                "version": "2.0.0",
                "description": "Pre-configured panel with SYSBP, DIABP, HR, TEMP, RR, and MAP calculation.",
                "variables": [
                    {
                        "id": "VS_SYSBP",
                        "name": "Systolic Blood Pressure",
                        "data_type": "integer",
                        "cdisc_oid": "IT.VS.SYSBP",
                        "ucum_unit": "mmHg",
                        "min": 40,
                        "max": 250,
                    },
                    {
                        "id": "VS_DIABP",
                        "name": "Diastolic Blood Pressure",
                        "data_type": "integer",
                        "cdisc_oid": "IT.VS.DIABP",
                        "ucum_unit": "mmHg",
                        "min": 30,
                        "max": 150,
                    },
                    {
                        "id": "VS_PULSE",
                        "name": "Pulse / Heart Rate",
                        "data_type": "integer",
                        "cdisc_oid": "IT.VS.PULSE",
                        "ucum_unit": "beats/min",
                        "min": 30,
                        "max": 220,
                    },
                    {
                        "id": "VS_TEMP",
                        "name": "Body Temperature",
                        "data_type": "float",
                        "cdisc_oid": "IT.VS.TEMP",
                        "ucum_unit": "Cel",
                        "min": 32.0,
                        "max": 43.0,
                    },
                ],
            },
            {
                "block_id": "BLOCK-ADVERSE-EVENT",
                "name": "CDASH Adverse Event Log",
                "domain": "AE",
                "version": "2.1.0",
                "description": "MedDRA-enabled AE log grid with seriousness criteria and CTCAE toxicity grades.",
                "variables": [
                    {
                        "id": "AE_TERM",
                        "name": "Adverse Event Verbatim Term",
                        "data_type": "string",
                        "cdisc_oid": "IT.AE.AETERM",
                    },
                    {
                        "id": "AE_START_DATE",
                        "name": "AE Start Date",
                        "data_type": "date",
                        "cdisc_oid": "IT.AE.AESTDAT",
                    },
                    {
                        "id": "AE_SEVERITY",
                        "name": "Toxicity Grade / Severity",
                        "data_type": "codelist",
                        "cdisc_oid": "IT.AE.AESEV",
                    },
                    {
                        "id": "AE_SERIOUS",
                        "name": "Serious Adverse Event (SAE)?",
                        "data_type": "boolean",
                        "cdisc_oid": "IT.AE.AESER",
                    },
                ],
            },
        ]

    @classmethod
    def instantiate_smart_block(
        cls, block_id: str, form_id: str
    ) -> TripartiteFormDefinition:
        """Instantiate a pre-configured smart block into a TripartiteFormDefinition."""
        catalog = {b["block_id"]: b for b in cls.get_catalog()}
        if block_id not in catalog:
            raise ValueError(f"Smart block '{block_id}' not found in Global Library.")

        block = catalog[block_id]
        variables: list[VariableDefinition] = []
        widgets: list[FieldWidgetConfig] = []

        for var_data in block["variables"]:
            var_def = VariableDefinition(
                id=var_data["id"],
                name=var_data["name"],
                data_type=ClinicalDataType(var_data["data_type"]),
                cdisc=CdiscMetadata(
                    domain=block["domain"],
                    variable_name=var_data["id"].split("_")[-1],
                    oid=var_data["cdisc_oid"],
                    ucum_unit=var_data.get("ucum_unit"),
                ),
                constraints=ConstraintDefinition(
                    required=True,
                    min_value=var_data.get("min"),
                    max_value=var_data.get("max"),
                ),
            )
            variables.append(var_def)
            widgets.append(
                FieldWidgetConfig(
                    variable_id=var_data["id"],
                    widget=WidgetType.NUMBER_INPUT
                    if var_data["data_type"] in ["integer", "float"]
                    else WidgetType.TEXT_INPUT,
                    grid_column_span=6,
                )
            )

        data_schema = DataSchema(
            form_id=form_id,
            name=block["name"],
            cdisc_form_oid=f"FORM.{block['domain']}",
            variables=variables,
        )

        ui_schema = UiSchema(
            form_id=form_id,
            default_viewport=ViewportMode.DESKTOP_EDC,
            sections=[
                GridSection(
                    id=f"SEC-{block['domain']}",
                    title=block["name"],
                    columns=12,
                    widgets=widgets,
                )
            ],
        )

        logic_schema = LogicSchema(
            form_id=form_id,
            edit_checks=[],
            derived_rules=[],
            cross_visit_dependencies=[],
        )

        return TripartiteFormDefinition(
            form_id=form_id,
            protocol_id="GLOBAL_LIBRARY",
            version_tag=block["version"],
            data_schema=data_schema,
            ui_schema=ui_schema,
            logic_schema=logic_schema,
        )
