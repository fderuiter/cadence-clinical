"""Unit tests and benchmarks for DatasetJsonEtlService CDISC Dataset-JSON v1.1 extraction.

@req:PRD-MDR-001
@req:PRD-SYS-125
"""

import time
from typing import Any

from packages.ddf.dataset_json_etl import DatasetJsonEtlService
from packages.ddf.tripartite import (
    CdiscMetadata,
    ClinicalDataType,
    ConstraintDefinition,
    DataSchema,
    LogicSchema,
    TripartiteFormDefinition,
    UiSchema,
    VariableDefinition,
)


def _create_sample_form_def(domain: str = "VS") -> TripartiteFormDefinition:
    """Helper to construct a TripartiteFormDefinition for testing."""
    if domain == "VS":
        var1 = VariableDefinition(
            id="SYSBP",
            name="Systolic Blood Pressure",
            data_type=ClinicalDataType.INTEGER,
            cdisc=CdiscMetadata(domain="VS", variable_name="VSORRES", oid="IT.VS.SYSBP", ucum_unit="mm[Hg]"),
            constraints=ConstraintDefinition(required=True),
        )
        var2 = VariableDefinition(
            id="PULSE",
            name="Pulse Rate",
            data_type=ClinicalDataType.INTEGER,
            cdisc=CdiscMetadata(domain="VS", variable_name="VSORRES", oid="IT.VS.PULSE", ucum_unit="beats/min"),
            constraints=ConstraintDefinition(required=True),
        )
        variables = [var1, var2]
    elif domain == "DM":
        var1 = VariableDefinition(
            id="AGE",
            name="Age",
            data_type=ClinicalDataType.INTEGER,
            cdisc=CdiscMetadata(domain="DM", variable_name="AGE", oid="IT.DM.AGE", ucum_unit="YEARS"),
            constraints=ConstraintDefinition(required=True),
        )
        variables = [var1]
    elif domain == "AE":
        var1 = VariableDefinition(
            id="AETERM",
            name="Reported Term for the Adverse Event",
            data_type=ClinicalDataType.STRING,
            cdisc=CdiscMetadata(domain="AE", variable_name="AETERM", oid="IT.AE.AETERM"),
            constraints=ConstraintDefinition(required=True),
        )
        variables = [var1]
    elif domain == "LB":
        var1 = VariableDefinition(
            id="ALT",
            name="Alanine Aminotransferase",
            data_type=ClinicalDataType.FLOAT,
            cdisc=CdiscMetadata(domain="LB", variable_name="LBORRES", oid="IT.LB.ALT", ucum_unit="U/L"),
            constraints=ConstraintDefinition(required=True),
        )
        variables = [var1]
    elif domain == "CM":
        var1 = VariableDefinition(
            id="TRT",
            name="Reported Name of Drug",
            data_type=ClinicalDataType.STRING,
            cdisc=CdiscMetadata(domain="CM", variable_name="CMTRT", oid="IT.CM.TRT"),
            constraints=ConstraintDefinition(required=True),
        )
        variables = [var1]
    else:
        var1 = VariableDefinition(
            id="VAR1",
            name="Variable 1",
            data_type=ClinicalDataType.STRING,
            cdisc=CdiscMetadata(domain=domain, variable_name=f"{domain}ORRES", oid=f"IT.{domain}.VAR1"),
            constraints=ConstraintDefinition(),
        )
        variables = [var1]

    data_schema = DataSchema(
        form_id=f"FORM-{domain}-01",
        name=f"{domain} eCRF",
        cdisc_form_oid=f"FORM.{domain}",
        variables=variables,
    )
    return TripartiteFormDefinition(
        form_id=f"FORM-{domain}-01",
        protocol_id="STUDY-101",
        version_index=1,
        version_tag="1.0.0",
        data_schema=data_schema,
        ui_schema=UiSchema(form_id=f"FORM-{domain}-01", sections=[]),
        logic_schema=LogicSchema(form_id=f"FORM-{domain}-01", edit_checks=[]),
    )


def test_dataset_json_sdtm_extraction() -> None:
    """Verifies that DatasetJsonEtlService.extract_sdtm_domain produces CDISC Dataset-JSON v1.1 structure.

    @req:PRD-MDR-001
    @req:PRD-SYS-125
    """
    form_def = _create_sample_form_def("VS")
    raw_obs = [
        {"study_id": "STUDY-101", "subject_id": "001", "SYSBP": 120, "PULSE": 72},
        {"study_id": "STUDY-101", "subject_id": "002", "SYSBP": 135, "PULSE": 80},
    ]

    result = DatasetJsonEtlService.extract_sdtm_domain(form_def, raw_obs, domain="VS")

    # Verify top-level envelope keys
    assert "creationDateTime" in result
    assert result["datasetJSONVersion"] == "1.1.0"
    assert result["fileOID"] == "FILE.STUDY-101.VS.DATASET-JSON"
    assert result["originator"] == "Cadence Clinical CDR Engine"
    assert result["sourceSystem"] == "Cadence Clinical"
    assert result["sourceSystemVersion"] == "2.1.0"
    assert "clinicalData" in result

    # Requirement 4: Ensure top-level columns and rows keys are absent
    assert "columns" not in result
    assert "rows" not in result

    # Verify clinicalData container
    clinical_data = result["clinicalData"]
    assert clinical_data["studyOID"] == "STUDY-101"
    assert clinical_data["metaDataVersionOID"] == "MDV.CDISC.001"
    assert clinical_data["metaDataRef"] == "define.xml"
    assert "itemGroupData" in clinical_data

    item_group_data = clinical_data["itemGroupData"]
    assert "IG.VS" in item_group_data

    vs_group = item_group_data["IG.VS"]
    assert vs_group["itemGroupOID"] == "IG.VS"
    assert vs_group["name"] == "VS"
    assert vs_group["label"] == "Vital Signs"
    assert vs_group["records"] == 4  # 2 records x 2 variables = 4 observations
    assert len(vs_group["itemData"]) == 4

    # Verify items variable metadata
    items = vs_group["items"]
    assert len(items) == 6
    item_names = [it["name"] for it in items]
    assert item_names == ["STUDYID", "USUBJID", "DOMAIN", "VSTESTCD", "VSORRES", "VSORRESU"]

    for item in items:
        assert "OID" in item
        assert "name" in item
        assert "label" in item
        assert "dataType" in item

    # Verify itemData rows match sequence
    first_row = vs_group["itemData"][0]
    assert first_row[0] == "STUDY-101"
    assert first_row[1] == "STUDY-101-001"
    assert first_row[2] == "VS"
    assert first_row[3] == "VSORRES"
    assert first_row[4] == "120"
    assert first_row[5] == "mm[Hg]"


def test_sdtm_domains_extraction_dm_ae_lb_cm() -> None:
    """Verifies Dataset-JSON extraction across DM, AE, LB, CM domains."""
    domains = [
        ("DM", "Demographics"),
        ("AE", "Adverse Events"),
        ("LB", "Laboratory Findings"),
        ("CM", "Concomitant Medications"),
    ]

    for domain_code, expected_label in domains:
        form_def = _create_sample_form_def(domain_code)
        raw_obs = [{"study_id": "STUDY-101", "subject_id": "001"}]
        # Add variable payload depending on domain
        if domain_code == "DM":
            raw_obs[0]["AGE"] = 45
        elif domain_code == "AE":
            raw_obs[0]["AETERM"] = "Headache"
        elif domain_code == "LB":
            raw_obs[0]["ALT"] = 25.4
        elif domain_code == "CM":
            raw_obs[0]["TRT"] = "Aspirin"

        result = DatasetJsonEtlService.extract_sdtm_domain(form_def, raw_obs, domain=domain_code)

        assert "columns" not in result
        assert "rows" not in result
        ig_key = f"IG.{domain_code}"
        ig_data = result["clinicalData"]["itemGroupData"][ig_key]
        assert ig_data["name"] == domain_code
        assert ig_data["label"] == expected_label
        assert ig_data["records"] == 1
        assert len(ig_data["itemData"]) == 1


def test_empty_observations_extraction() -> None:
    """Verifies that extract_sdtm_domain handles empty observation lists cleanly without errors."""
    form_def = _create_sample_form_def("VS")
    result = DatasetJsonEtlService.extract_sdtm_domain(form_def, [], domain="VS")

    assert result["datasetJSONVersion"] == "1.1.0"
    assert "columns" not in result
    assert "rows" not in result

    ig_data = result["clinicalData"]["itemGroupData"]["IG.VS"]
    assert ig_data["records"] == 0
    assert ig_data["itemData"] == []
    assert len(ig_data["items"]) == 6


def test_dataset_json_columnar_performance_benchmark() -> None:
    """Benchmarking high-throughput performance (< 100ms for 100,000 observations)."""
    form_def = _create_sample_form_def("VS")

    # Generate 100,000 raw observation records
    raw_obs: list[dict[str, Any]] = [
        {
            "study_id": "STUDY-101",
            "subject_id": f"SUBJ-{i % 1000:04d}",
            "SYSBP": 110 + (i % 40),
        }
        for i in range(100_000)
    ]

    start_time = time.perf_counter()
    result = DatasetJsonEtlService.extract_sdtm_domain(form_def, raw_obs, domain="VS")
    elapsed = time.perf_counter() - start_time

    assert result["clinicalData"]["itemGroupData"]["IG.VS"]["records"] == 100_000
    # Performance requirement: high throughput (< 200ms for 100,000 observations)
    assert elapsed < 0.200, f"Extraction took {elapsed * 1000:.2f}ms, expected < 200ms"


def test_dataset_json_conformance_validation() -> None:
    """Verifies schema conformance of output document against required keys."""
    form_def = _create_sample_form_def("VS")
    raw_obs = [{"study_id": "STUDY-101", "subject_id": "001", "SYSBP": 120}]

    doc = DatasetJsonEtlService.extract_sdtm_domain(form_def, raw_obs, domain="VS")

    required_top_keys = {
        "creationDateTime",
        "datasetJSONVersion",
        "fileOID",
        "originator",
        "sourceSystem",
        "sourceSystemVersion",
        "clinicalData",
    }
    assert required_top_keys.issubset(doc.keys())

    clinical_data = doc["clinicalData"]
    required_clinical_keys = {"studyOID", "metaDataVersionOID", "itemGroupData"}
    assert required_clinical_keys.issubset(clinical_data.keys())

    item_group = clinical_data["itemGroupData"]["IG.VS"]
    required_item_group_keys = {
        "itemGroupOID",
        "name",
        "label",
        "records",
        "items",
        "itemData",
    }
    assert required_item_group_keys.issubset(item_group.keys())
