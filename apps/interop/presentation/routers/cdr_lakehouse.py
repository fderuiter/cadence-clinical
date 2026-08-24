"""CDISC Lakehouse & Submission Pipeline Router Aggregator.

Re-exports routers and dependencies for CDR Lakehouse and CDISC Study Submission Pipeline.

Requirements: PRD-CDR-001, PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from apps.interop.presentation.routers.cdr_pipeline import (
    _extract_principal_info,
    _get_cdr_service,
    execute_adam_pipeline,
    execute_continuous_pipeline,
    execute_sdtm_pipeline,
    export_dataset_json,
    export_define_xml,
    get_metadata_catalog,
    get_pipeline_execution_result,
    validate_dataset_json_endpoint,
    validate_define_xml_endpoint,
)
from apps.interop.presentation.routers.cdr_pipeline import (
    router as cdr_pipeline_router,
)
from apps.interop.presentation.routers.lakehouse import (
    router as lakehouse_router,
)

router = cdr_pipeline_router

__all__ = [
    "_extract_principal_info",
    "_get_cdr_service",
    "cdr_pipeline_router",
    "execute_adam_pipeline",
    "execute_continuous_pipeline",
    "execute_sdtm_pipeline",
    "export_dataset_json",
    "export_define_xml",
    "get_metadata_catalog",
    "get_pipeline_execution_result",
    "lakehouse_router",
    "router",
    "validate_dataset_json_endpoint",
    "validate_define_xml_endpoint",
]
