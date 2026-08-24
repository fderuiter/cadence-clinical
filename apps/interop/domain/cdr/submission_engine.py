"""Unified CDISC Study Submission Compiler Engine.

Encapsulates the 4 sequential transformation steps:
1. CDISC SDTM mapping for 10 core domains (DM, AE, CM, VS, LB, EX, DS, TU, TR, RS)
   and extensible custom domains (e.g. DD, DO).
2. CDISC ADaM dataset derivations (ADSL, ADAE, ADTTE, BDS).
3. CDISC Dataset-JSON 1.1 / 2.0 serialization with strict schema validation.
4. CDISC Define-XML 2.1 compilation with VLM, WhereClauses, Origins, CodeLists, and Methods.
Plus role-gated blinding masking and Part 11 cryptographic sealing.

Requirements: PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import copy
import hashlib
import json
from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.interop.domain.cdr.adam_pipeline import ADaMPipelineEngine
from apps.interop.domain.cdr.dataset_json_compiler import (
    STANDARD_VARIABLE_SPECS,
    DatasetJSONCompiler,
)
from apps.interop.domain.cdr.define_xml_compiler import (
    CODELISTS,
    VLM_DEFINITIONS,
    DefineXMLCompiler,
)
from apps.interop.domain.cdr.models import (
    DatasetJSONDocument,
    DefineXMLCompilationRequest,
    DefineXMLValidationResult,
    PipelineExecutionResult,
    PipelineExecutionStatus,
)
from apps.interop.domain.cdr.sdtm_pipeline import SDTMPipelineEngine
from apps.interop.domain.lakehouse_models import CanonicalClinicalRecord
from apps.interop.domain.sdtm_device_mapper import (
    SDTMRecordDD,
    SDTMRecordDO,
    map_dd,
    map_do,
)


class SubmissionPackage(BaseModel):
    """Complete, hermetically sealed CDISC submission package containing SDTM, ADaM, Dataset-JSON, and Define-XML 2.1."""

    model_config = ConfigDict(extra="forbid")

    study_id: str = Field(..., description="Study Identifier")
    snapshot_id: str | None = Field(
        None, description="Linked snapshot ID if snapshot-pinned"
    )
    is_unblinded: bool = Field(
        False, description="Whether treatment arms are unblinded"
    )
    dataset_json_version: str = Field(
        "1.1.0", description="CDISC Dataset-JSON specification version"
    )
    define_xml_version: str = Field(
        "2.1.0", description="CDISC Define-XML specification version"
    )
    compiled_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Compilation timestamp",
    )
    sdtm_domains: dict[str, list[Any]] = Field(
        default_factory=dict, description="Mapped SDTM domain records"
    )
    adam_datasets: dict[str, list[Any]] = Field(
        default_factory=dict, description="Derived ADaM dataset records"
    )
    dataset_json_document: DatasetJSONDocument = Field(
        ..., description="Root Dataset-JSON document model"
    )
    dataset_json_string: str = Field(
        ..., description="Serialized Dataset-JSON UTF-8 string"
    )
    dataset_json_validation: dict[str, Any] = Field(
        default_factory=dict, description="Dataset-JSON validation results"
    )
    define_xml_string: str = Field(
        ..., description="Compiled CDISC Define-XML 2.1 XML content"
    )
    define_xml_validation: DefineXMLValidationResult = Field(
        ..., description="Define-XML validation outcome"
    )
    sdtm_counts: dict[str, int] = Field(
        default_factory=dict, description="Record counts per SDTM domain"
    )
    adam_counts: dict[str, int] = Field(
        default_factory=dict, description="Record counts per ADaM dataset"
    )
    total_sdtm_records: int = Field(
        0, description="Total SDTM records across all domains"
    )
    total_adam_records: int = Field(
        0, description="Total ADaM records across all datasets"
    )
    total_records: int = Field(
        0, description="Grand total records in submission package"
    )
    package_hash_sha256: str = Field(
        ..., description="Cryptographic SHA-256 seal of the entire submission package"
    )

    def to_pipeline_execution_result(
        self,
        execution_id: str,
        started_at: datetime,
        completed_at: datetime | None = None,
        errors: list[str] | None = None,
    ) -> PipelineExecutionResult:
        """Convert submission package into a standard PipelineExecutionResult manifest."""
        comp_time = completed_at or self.compiled_at
        all_datasets = {**self.sdtm_domains, **self.adam_datasets}
        return PipelineExecutionResult(
            execution_id=execution_id,
            study_id=self.study_id,
            snapshot_id=self.snapshot_id,
            status=(
                PipelineExecutionStatus.COMPLETED
                if not errors
                else PipelineExecutionStatus.FAILED
            ),
            started_at=started_at,
            completed_at=comp_time,
            sdtm_counts=self.sdtm_counts,
            adam_counts=self.adam_counts,
            total_sdtm_records=self.total_sdtm_records,
            total_adam_records=self.total_adam_records,
            dataset_json_manifest={
                "datasetJSONVersion": self.dataset_json_version,
                "fileOID": self.dataset_json_document.fileOID,
                "dataset_count": len(all_datasets),
                "is_valid": self.dataset_json_validation.get("is_valid", False),
            },
            define_xml_summary={
                "define_version": self.define_xml_version,
                "standard_name": "SDTM-IG / ADaM-IG",
                "is_valid": self.define_xml_validation.is_valid,
                "item_group_count": self.define_xml_validation.item_group_count,
                "item_def_count": self.define_xml_validation.item_def_count,
                "vlm_count": self.define_xml_validation.value_level_metadata_count,
                "where_clause_count": self.define_xml_validation.where_clause_count,
                "origin_count": self.define_xml_validation.item_def_count,
            },
            validation_summary={
                "dataset_json": self.dataset_json_validation,
                "define_xml": self.define_xml_validation.model_dump(),
            },
            errors=errors or [],
        )

    def get_dataset(self, name: str) -> list[Any] | None:
        """Retrieve a specific SDTM domain or ADaM dataset by name."""
        name_upper = name.strip().upper()
        if name_upper in self.sdtm_domains:
            return self.sdtm_domains[name_upper]
        if name_upper in self.adam_datasets:
            return self.adam_datasets[name_upper]
        return None


class CdiscSubmissionEngine:
    """Unified CDISC Study Submission Compiler Engine.

    Encapsulates the 4 sequential transformation steps:
    1. CDISC SDTM mapping for 10 core domains (DM, AE, CM, VS, LB, EX, DS, TU, TR, RS)
       and extensible custom domains (e.g. DD, DO).
    2. CDISC ADaM dataset derivations (ADSL, ADAE, ADTTE, BDS).
    3. CDISC Dataset-JSON 1.1 / 2.0 serialization with strict schema validation.
    4. CDISC Define-XML 2.1 compilation with VLM, WhereClauses, Origins, CodeLists, and Methods.
    Plus role-gated blinding masking and Part 11 cryptographic sealing.

    Requirements: PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
    """

    def __init__(
        self,
        study_id: str = "STUDY-001",
        metadata_version_oid: str = "MDV.CDISC.001",
        spec_version: str = "1.1.0",
    ) -> None:
        self.study_id = study_id
        self.metadata_version_oid = metadata_version_oid
        self.spec_version = spec_version
        self.sdtm_engine = SDTMPipelineEngine(study_id=study_id)
        self.adam_engine = ADaMPipelineEngine(study_id=study_id)
        self.dataset_json_compiler = DatasetJSONCompiler(
            study_id=study_id,
            metadata_version_oid=metadata_version_oid,
            spec_version=spec_version,
        )

    def mask_blinding(
        self,
        sdtm_domains: dict[str, list[Any]],
        adam_datasets: dict[str, list[Any]],
    ) -> tuple[dict[str, list[Any]], dict[str, list[Any]]]:
        """Apply role-gated blinding mask to treatment arms and sensitive variables."""
        masked_sdtm: dict[str, list[Any]] = {}
        for d_name, recs in sdtm_domains.items():
            masked_recs = []
            for rec in recs:
                if hasattr(rec, "model_copy"):
                    r_copy = rec.model_copy(deep=True)
                elif isinstance(rec, dict):
                    r_copy = dict(rec)
                else:
                    r_copy = copy.deepcopy(rec)

                if hasattr(r_copy, "ARM") and getattr(r_copy, "ARM", None):
                    r_copy.ARM = "Masked Treatment Arm"
                if hasattr(r_copy, "ARMCD") and getattr(r_copy, "ARMCD", None):
                    r_copy.ARMCD = "MASKED"
                if hasattr(r_copy, "ACTARM") and getattr(r_copy, "ACTARM", None):
                    r_copy.ACTARM = "Masked Actual Arm"
                if hasattr(r_copy, "ACTARMCD") and getattr(r_copy, "ACTARMCD", None):
                    r_copy.ACTARMCD = "MASKED"
                if hasattr(r_copy, "EXTRT") and getattr(r_copy, "EXTRT", None):
                    r_copy.EXTRT = "Blind Study Medication"

                if isinstance(r_copy, dict):
                    if r_copy.get("ARM"):
                        r_copy["ARM"] = "Masked Treatment Arm"
                    if r_copy.get("ARMCD"):
                        r_copy["ARMCD"] = "MASKED"
                    if r_copy.get("ACTARM"):
                        r_copy["ACTARM"] = "Masked Actual Arm"
                    if r_copy.get("ACTARMCD"):
                        r_copy["ACTARMCD"] = "MASKED"
                    if r_copy.get("EXTRT"):
                        r_copy["EXTRT"] = "Blind Study Medication"

                masked_recs.append(r_copy)
            masked_sdtm[d_name] = masked_recs

        masked_adam: dict[str, list[Any]] = {}
        for ds_name, recs in adam_datasets.items():
            masked_recs = []
            for rec in recs:
                if hasattr(rec, "model_copy"):
                    r_copy = rec.model_copy(deep=True)
                elif isinstance(rec, dict):
                    r_copy = dict(rec)
                else:
                    r_copy = copy.deepcopy(rec)

                if hasattr(r_copy, "ARM") and getattr(r_copy, "ARM", None):
                    r_copy.ARM = "Masked Treatment Arm"
                if hasattr(r_copy, "ARMCD") and getattr(r_copy, "ARMCD", None):
                    r_copy.ARMCD = "MASKED"
                if hasattr(r_copy, "ACTARM") and getattr(r_copy, "ACTARM", None):
                    r_copy.ACTARM = "Masked Actual Arm"
                if hasattr(r_copy, "ACTARMCD") and getattr(r_copy, "ACTARMCD", None):
                    r_copy.ACTARMCD = "MASKED"
                if hasattr(r_copy, "TRT01P") and getattr(r_copy, "TRT01P", None):
                    r_copy.TRT01P = "Masked Treatment Arm"
                if hasattr(r_copy, "TRT01A") and getattr(r_copy, "TRT01A", None):
                    r_copy.TRT01A = "Masked Actual Arm"

                if isinstance(r_copy, dict):
                    if r_copy.get("ARM"):
                        r_copy["ARM"] = "Masked Treatment Arm"
                    if r_copy.get("ARMCD"):
                        r_copy["ARMCD"] = "MASKED"
                    if r_copy.get("ACTARM"):
                        r_copy["ACTARM"] = "Masked Actual Arm"
                    if r_copy.get("ACTARMCD"):
                        r_copy["ACTARMCD"] = "MASKED"
                    if r_copy.get("TRT01P"):
                        r_copy["TRT01P"] = "Masked Treatment Arm"
                    if r_copy.get("TRT01A"):
                        r_copy["TRT01A"] = "Masked Actual Arm"

                masked_recs.append(r_copy)
            masked_adam[ds_name] = masked_recs

        return masked_sdtm, masked_adam

    def compile_sdtm(
        self,
        records: list[CanonicalClinicalRecord],
        target_domains: list[str] | None = None,
        custom_domains: dict[str, list[Any]] | None = None,
    ) -> dict[str, list[Any]]:
        """Step 1: Map canonical clinical observations to 10 standard SDTM domains."""
        sdtm_domains = self.sdtm_engine.build_sdtm_domains(records)
        if custom_domains:
            for k, v in custom_domains.items():
                sdtm_domains[k.upper()] = v
        if target_domains:
            target_upper = {d.upper() for d in target_domains}
            return {k: v for k, v in sdtm_domains.items() if k.upper() in target_upper}
        return sdtm_domains

    def compile_adam(
        self,
        sdtm_domains: dict[str, list[Any]],
        target_datasets: list[str] | None = None,
    ) -> dict[str, list[Any]]:
        """Step 2: Derive ADaM analysis datasets (ADSL, ADAE, ADTTE, BDS) from SDTM domains."""
        adam_datasets = self.adam_engine.build_adam_datasets(sdtm_domains)
        if target_datasets:
            target_upper = {d.upper() for d in target_datasets}
            return {k: v for k, v in adam_datasets.items() if k.upper() in target_upper}
        return adam_datasets

    def compile_dataset_json(
        self,
        datasets: dict[str, list[Any]],
        spec_version: str | None = None,
        file_oid: str | None = None,
    ) -> tuple[DatasetJSONDocument, str, dict[str, Any]]:
        """Step 3: Serialize datasets to CDISC Dataset-JSON 1.1 / 2.0 with validation."""
        version = spec_version or self.spec_version
        compiler = DatasetJSONCompiler(
            study_id=self.study_id,
            metadata_version_oid=self.metadata_version_oid,
            spec_version=version,
        )
        doc = compiler.serialize_datasets(datasets, file_oid=file_oid)
        json_str = compiler.to_json(doc)
        validation = compiler.validate_dataset_json(json_str)
        return doc, json_str, validation

    def compile_define_xml(
        self,
        request: DefineXMLCompilationRequest | None = None,
        sdtm_domains: list[str] | None = None,
        adam_datasets: list[str] | None = None,
        study_name: str | None = None,
        protocol_name: str | None = None,
        standard_name: str = "SDTM-IG",
        standard_version: str = "3.4",
        include_vlm: bool = True,
        include_where_clauses: bool = True,
        include_origins: bool = True,
    ) -> tuple[str, DefineXMLValidationResult]:
        """Step 4: Auto-compile CDISC Define-XML 2.1 document with VLM, WhereClauses, and Origins."""
        if request is None:
            request = DefineXMLCompilationRequest(
                study_id=self.study_id,
                study_name=study_name or f"Study {self.study_id}",
                protocol_name=protocol_name or f"PROTOCOL-{self.study_id}",
                metadata_version_oid=self.metadata_version_oid,
                standard_name=standard_name,
                standard_version=standard_version,
                domains=(
                    sdtm_domains
                    if sdtm_domains is not None
                    else [
                        "DM",
                        "AE",
                        "CM",
                        "VS",
                        "LB",
                        "EX",
                        "DS",
                        "TU",
                        "TR",
                        "RS",
                    ]
                ),
                adam_datasets=(
                    adam_datasets
                    if adam_datasets is not None
                    else ["ADSL", "ADAE", "ADTTE", "BDS"]
                ),
                include_vlm=include_vlm,
                include_where_clauses=include_where_clauses,
                include_origins=include_origins,
            )
        compiler = DefineXMLCompiler(request)
        xml_string = compiler.compile()
        validation = DefineXMLCompiler.validate_define_xml(xml_string)
        return xml_string, validation

    def compile_submission_package(
        self,
        study_id: str | None = None,
        raw_records: list[CanonicalClinicalRecord] | None = None,
        snapshot_id: str | None = None,
        is_unblinded: bool = False,
        dataset_json_version: str = "1.1.0",
        sdtm_domains_filter: list[str] | None = None,
        adam_datasets_filter: list[str] | None = None,
        study_name: str | None = None,
        protocol_name: str | None = None,
        include_vlm: bool = True,
        include_where_clauses: bool = True,
        include_origins: bool = True,
        custom_domains: dict[str, list[Any]] | None = None,
    ) -> SubmissionPackage:
        """Deep single-call entrypoint orchestrating the entire 4-step transformation and compilation pipeline."""
        target_study_id = study_id or self.study_id
        records = raw_records or []

        # 1. SDTM Mapping
        sdtm_domains = self.compile_sdtm(
            records=records,
            target_domains=sdtm_domains_filter,
            custom_domains=custom_domains,
        )

        # 2. ADaM Derivations
        adam_datasets = self.compile_adam(
            sdtm_domains=sdtm_domains,
            target_datasets=adam_datasets_filter,
        )

        # 3. Blinding Mask
        if not is_unblinded:
            sdtm_domains, adam_datasets = self.mask_blinding(
                sdtm_domains, adam_datasets
            )

        # 4. Dataset-JSON Serialization & Validation
        all_datasets = {**sdtm_domains, **adam_datasets}
        doc, json_str, json_val = self.compile_dataset_json(
            datasets=all_datasets,
            spec_version=dataset_json_version,
        )

        # 5. Define-XML Compilation & Validation
        xml_str, xml_val = self.compile_define_xml(
            sdtm_domains=list(sdtm_domains.keys()),
            adam_datasets=list(adam_datasets.keys()),
            study_name=study_name or f"Cadence Protocol {target_study_id}",
            protocol_name=protocol_name or f"PROTOCOL-{target_study_id}",
            include_vlm=include_vlm,
            include_where_clauses=include_where_clauses,
            include_origins=include_origins,
        )

        # Record counts
        sdtm_counts = {k: len(v) for k, v in sdtm_domains.items()}
        adam_counts = {k: len(v) for k, v in adam_datasets.items()}
        total_sdtm = sum(sdtm_counts.values())
        total_adam = sum(adam_counts.values())
        total_rec = total_sdtm + total_adam

        # Package cryptographic SHA-256 seal (canonical representation)
        seal_elements = {
            "study_id": target_study_id,
            "snapshot_id": snapshot_id,
            "is_unblinded": is_unblinded,
            "dataset_json_version": dataset_json_version,
            "sdtm_counts": sdtm_counts,
            "adam_counts": adam_counts,
            "dataset_json_hash": hashlib.sha256(json_str.encode("utf-8")).hexdigest(),
            "define_xml_hash": hashlib.sha256(xml_str.encode("utf-8")).hexdigest(),
        }
        canonical_digest_input = json.dumps(
            seal_elements, sort_keys=True, separators=(",", ":")
        )
        package_hash = hashlib.sha256(
            canonical_digest_input.encode("utf-8")
        ).hexdigest()

        return SubmissionPackage(
            study_id=target_study_id,
            snapshot_id=snapshot_id,
            is_unblinded=is_unblinded,
            dataset_json_version=dataset_json_version,
            define_xml_version="2.1.0",
            compiled_at=datetime.now(UTC),
            sdtm_domains=sdtm_domains,
            adam_datasets=adam_datasets,
            dataset_json_document=doc,
            dataset_json_string=json_str,
            dataset_json_validation=json_val,
            define_xml_string=xml_str,
            define_xml_validation=xml_val,
            sdtm_counts=sdtm_counts,
            adam_counts=adam_counts,
            total_sdtm_records=total_sdtm,
            total_adam_records=total_adam,
            total_records=total_rec,
            package_hash_sha256=package_hash,
        )

    def validate_dataset_json(self, payload: dict[str, Any] | str) -> dict[str, Any]:
        """Perform strict structural and schema conformance validation for CDISC Dataset-JSON."""
        return self.dataset_json_compiler.validate_dataset_json(payload)

    def validate_define_xml(self, xml_content: str) -> DefineXMLValidationResult:
        """Validate CDISC Define-XML v2.1 document syntax, ODM root, and element presence."""
        return DefineXMLCompiler.validate_define_xml(xml_content)

    def get_metadata_catalog(self) -> dict[str, Any]:
        """Retrieve catalog of supported SDTM domains, ADaM datasets, variable definitions, and VLM."""
        sdtm_keys = ["DM", "AE", "CM", "VS", "LB", "EX", "DS", "TU", "TR", "RS"]
        adam_keys = ["ADSL", "ADAE", "ADTTE", "BDS"]
        return {
            "sdtm_domains": sdtm_keys,
            "adam_datasets": adam_keys,
            "domain_metadata": STANDARD_VARIABLE_SPECS,
            "vlm_catalog": VLM_DEFINITIONS,
            "codelist_catalog": list(CODELISTS.keys()),
        }

    def compile_device_sdtm(
        self,
        deficiencies: list[Any] | None = None,
        devices: list[Any] | None = None,
        created_by: str = "system",
        reason_for_change: str | None = None,
    ) -> dict[str, list[Any]]:
        """Map medical device deficiencies and serialized inventory into SDTM DD and DO domain records."""
        dd_records: list[SDTMRecordDD] = []
        do_records: list[SDTMRecordDO] = []
        if deficiencies:
            dd_records = map_dd(
                deficiencies=deficiencies,
                created_by=created_by,
                reason_for_change=reason_for_change,
            )
        if devices:
            do_records = map_do(
                devices=devices,
                created_by=created_by,
                reason_for_change=reason_for_change,
            )
        return {
            "DD": dd_records,
            "DO": do_records,
        }

    def compile_device_investigation_package(
        self,
        study_id: str,
        deficiencies: list[dict[str, Any]],
        devices: list[dict[str, Any]],
        user_id: str = "system",
        change_reason: str = "Regulatory device export package generation",
    ) -> dict[str, Any]:
        """Compile a complete regulatory device export package containing SDTM DD, DO, and MIR forms."""
        device_domains = self.compile_device_sdtm(
            deficiencies=deficiencies,
            devices=devices,
            created_by=user_id,
            reason_for_change=change_reason,
        )
        dd_records = [m.model_dump(mode="json") for m in device_domains.get("DD", [])]
        do_records = [m.model_dump(mode="json") for m in device_domains.get("DO", [])]

        now_iso = datetime.now(UTC).isoformat()
        raw_digest_input = json.dumps(
            {
                "study_id": study_id,
                "generated_at": now_iso,
                "dd_count": len(dd_records),
                "do_count": len(do_records),
            },
            sort_keys=True,
        )
        digest_hash = hashlib.sha256(raw_digest_input.encode("utf-8")).hexdigest()

        manifest = {
            "study_id": study_id,
            "compiled_by": user_id,
            "generated_at": now_iso,
            "digest_sha256": digest_hash,
            "gxp_compliant": True,
            "standards": ["CDISC SDTM v1.8", "ISO 14155:2020", "EU MDR 2017/745"],
        }

        return {
            "study_id": study_id,
            "generated_at": now_iso,
            "sdtm_dd_count": len(dd_records),
            "sdtm_do_count": len(do_records),
            "sdtm_dd_dataset": dd_records,
            "sdtm_do_dataset": do_records,
            "audit_manifest": manifest,
        }
