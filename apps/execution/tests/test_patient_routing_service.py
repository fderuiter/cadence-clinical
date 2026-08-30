"""Unit tests for In-Flight Patient Migration Runtime Routing Service."""

from datetime import UTC, datetime, timedelta

from apps.execution.domain.services.patient_routing_service import PatientRoutingService
from packages.ddf.tripartite import (
    CdiscMetadata,
    ClinicalDataType,
    ConstraintDefinition,
    DataSchema,
    LogicSchema,
    MigrationDirective,
    MigrationMode,
    PatientMigrationMatrix,
    TripartiteFormDefinition,
    UiSchema,
    VariableDefinition,
)


def _create_forms_and_matrix() -> tuple[
    TripartiteFormDefinition, TripartiteFormDefinition, PatientMigrationMatrix
]:
    """Create base form, amended form, and migration matrix fixtures."""
    effective_date = datetime(2026, 8, 15, tzinfo=UTC)

    base_form = TripartiteFormDefinition(
        form_id="FORM-VS",
        protocol_id="PROT-101",
        version_index=1,
        version_tag="1.0.0",
        data_schema=DataSchema(
            form_id="FORM-VS",
            name="Vital Signs",
            cdisc_form_oid="FORM.VS",
            variables=[
                VariableDefinition(
                    id="VS_SYSBP",
                    name="Systolic BP",
                    data_type=ClinicalDataType.INTEGER,
                    cdisc=CdiscMetadata(oid="IT.VS.SYSBP"),
                ),
            ],
        ),
        ui_schema=UiSchema(form_id="FORM-VS"),
        logic_schema=LogicSchema(form_id="FORM-VS"),
    )

    amended_form = TripartiteFormDefinition(
        form_id="FORM-VS",
        protocol_id="PROT-101",
        version_index=2,
        version_tag="2.0.0",
        data_schema=DataSchema(
            form_id="FORM-VS",
            name="Vital Signs",
            cdisc_form_oid="FORM.VS",
            variables=[
                VariableDefinition(
                    id="VS_SYSBP",
                    name="Systolic BP",
                    data_type=ClinicalDataType.INTEGER,
                    cdisc=CdiscMetadata(oid="IT.VS.SYSBP"),
                ),
                VariableDefinition(
                    id="VS_WEIGHT",
                    name="Body Weight",
                    data_type=ClinicalDataType.FLOAT,
                    cdisc=CdiscMetadata(oid="IT.VS.WEIGHT", ucum_unit="kg"),
                    constraints=ConstraintDefinition(min_value=20.0, max_value=300.0),
                ),
            ],
        ),
        ui_schema=UiSchema(form_id="FORM-VS"),
        logic_schema=LogicSchema(form_id="FORM-VS"),
    )

    matrix = PatientMigrationMatrix(
        protocol_id="PROT-101",
        from_version_tag="1.0.0",
        to_version_tag="2.0.0",
        effective_date=effective_date,
        directives=[
            MigrationDirective(
                variable_id="VS_WEIGHT",
                action="ADD",
                migration_mode=MigrationMode.RETROSPECTIVE,
                backfill_query_message="Please record body weight for protocol amendment 2.0.",
            )
        ],
    )

    return base_form, amended_form, matrix


def test_post_amendment_patient_routing():
    """Verify newly enrolled subjects receive full amended schema without backfill queries.

    @req:PRD-CRF-012
    @req:PRD-SUB-001
    """
    base_form, amended_form, matrix = _create_forms_and_matrix()
    enrollment_date = matrix.effective_date + timedelta(days=5)

    result = PatientRoutingService.resolve_subject_form_schema(
        subject_id="SUBJ-NEW-001",
        subject_enrollment_date=enrollment_date,
        base_form=base_form,
        amended_form=amended_form,
        migration_matrix=matrix,
    )

    assert result["routing_mode"] == "POST_AMENDMENT_FULL"
    assert result["projected_version_tag"] == "2.0.0"
    assert len(result["form_definition"].data_schema.variables) == 2
    assert len(result["backfill_queries_required"]) == 0


def test_pre_amendment_patient_retrospective_projection():
    """Verify pre-amendment active subjects receive retrospective fields and backfill queries.

    @req:PRD-CRF-012
    @req:PRD-SYS-001
    """
    base_form, amended_form, matrix = _create_forms_and_matrix()
    enrollment_date = matrix.effective_date - timedelta(days=30)

    result = PatientRoutingService.resolve_subject_form_schema(
        subject_id="SUBJ-LEGACY-001",
        subject_enrollment_date=enrollment_date,
        base_form=base_form,
        amended_form=amended_form,
        migration_matrix=matrix,
    )

    assert result["routing_mode"] == "PRE_AMENDMENT_DIRECTIVE_PROJECTION"
    assert "1.0.0-proj-2.0.0" in result["projected_version_tag"]
    assert len(result["form_definition"].data_schema.variables) == 2
    assert len(result["backfill_queries_required"]) == 1
    assert result["backfill_queries_required"][0]["variable_id"] == "VS_WEIGHT"
