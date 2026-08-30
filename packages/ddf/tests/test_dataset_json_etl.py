"""Unit tests for CDISC Dataset-JSON v1.1 Columnar ETL Service."""

from packages.ddf.dataset_json_etl import DatasetJsonEtlService
from packages.ddf.tripartite import (
    CdiscMetadata,
    ClinicalDataType,
    DataSchema,
    LogicSchema,
    TripartiteFormDefinition,
    UiSchema,
    VariableDefinition,
)


def _create_vitals_form() -> TripartiteFormDefinition:
    """Create sample vitals form with CDISC annotations."""
    return TripartiteFormDefinition(
        form_id="FORM-VS",
        protocol_id="STUDY-101",
        data_schema=DataSchema(
            form_id="FORM-VS",
            name="Vital Signs",
            cdisc_form_oid="FORM.VS",
            variables=[
                VariableDefinition(
                    id="VS_SYSBP",
                    name="Systolic BP",
                    data_type=ClinicalDataType.INTEGER,
                    cdisc=CdiscMetadata(
                        domain="VS",
                        variable_name="SYSBP",
                        oid="IT.VS.SYSBP",
                        ucum_unit="mmHg",
                    ),
                ),
                VariableDefinition(
                    id="VS_DIABP",
                    name="Diastolic BP",
                    data_type=ClinicalDataType.INTEGER,
                    cdisc=CdiscMetadata(
                        domain="VS",
                        variable_name="DIABP",
                        oid="IT.VS.DIABP",
                        ucum_unit="mmHg",
                    ),
                ),
            ],
        ),
        ui_schema=UiSchema(form_id="FORM-VS"),
        logic_schema=LogicSchema(form_id="FORM-VS"),
    )


def test_dataset_json_sdtm_extraction():
    """Verify transformation of raw eCRF observations into CDISC Dataset-JSON v1.1.

    @req:PRD-SYS-125
    @req:PRD-MDR-001
    """
    form = _create_vitals_form()
    raw_obs = [
        {
            "study_id": "STUDY-101",
            "subject_id": "SUBJ-001",
            "VS_SYSBP": 120,
            "VS_DIABP": 80,
        },
        {
            "study_id": "STUDY-101",
            "subject_id": "SUBJ-002",
            "VS_SYSBP": 135,
            "VS_DIABP": 88,
        },
    ]

    dataset_json = DatasetJsonEtlService.extract_sdtm_domain(
        form_def=form,
        raw_observations=raw_obs,
        domain="VS",
    )

    assert dataset_json["records"] == 4  # 2 subjects * 2 variables
    assert dataset_json["domain"] == "VS"
    assert len(dataset_json["columns"]) == 6
    assert any(col["name"] == "VSTESTCD" for col in dataset_json["columns"])
    assert dataset_json["rows"][0][0] == "STUDY-101"
    assert dataset_json["rows"][0][1] == "STUDY-101-SUBJ-001"
    assert dataset_json["rows"][0][3] == "SYSBP"
    assert dataset_json["rows"][0][4] == "120"
    assert dataset_json["rows"][0][5] == "mmHg"
