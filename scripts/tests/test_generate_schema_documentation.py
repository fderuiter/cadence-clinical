"""Unit tests for the automated schema boundaries and documentation generator.

Verifies model extraction, GxP and immutability mapping, and file output.
"""

import pytest

from scripts.generate_schema_documentation import (
    extract_all_service_schemas,
    get_designer_schema,
    parse_sqlalchemy_schema,
)


def test_parse_sqlalchemy_schema_execution():
    """Verify SQLAlchemy schema extraction for execution models."""
    try:
        from apps.execution.database.models import Base as ExecutionBase
    except ImportError:
        pytest.skip("Execution service transcribed to .NET C#")

    tables = parse_sqlalchemy_schema(ExecutionBase, "execution")
    assert len(tables) > 0

    # Locate ClinicalSubject table and check attributes
    subject_table = next(
        (t for t in tables if t["class_name"] == "ClinicalSubject"), None
    )
    assert subject_table is not None
    assert subject_table["table_name"] == "clinical_subjects"
    assert subject_table["service"] == "execution"
    assert subject_table["gxp"] is True
    assert subject_table["immutable"] is False


def test_parse_sqlalchemy_schema_etmf():
    """Verify SQLAlchemy schema extraction for eTMF models."""
    from apps.etmf.infrastructure.models import Base as EtmfBase

    tables = parse_sqlalchemy_schema(EtmfBase, "etmf")
    assert len(tables) > 0

    # Locate TMFDocument
    doc_table = next((t for t in tables if t["class_name"] == "TMFDocument"), None)
    assert doc_table is not None
    assert doc_table["table_name"] == "tmf_documents"
    assert doc_table["service"] == "etmf"

    # DocumentQCTransition is write-protected/immutable
    qc_table = next(
        (t for t in tables if t["class_name"] == "DocumentQCTransition"), None
    )
    assert qc_table is not None
    assert qc_table["immutable"] is True


def test_parse_sqlalchemy_schema_fileshare():
    """Verify SQLAlchemy schema extraction for fileshare models."""
    from apps.fileshare.infrastructure.models import Base as FileshareBase

    tables = parse_sqlalchemy_schema(FileshareBase, "fileshare")
    assert len(tables) > 0


def test_parse_sqlalchemy_schema_imaging():
    """Verify SQLAlchemy schema extraction for imaging models."""
    from apps.imaging.infrastructure.models import Base as ImagingBase

    tables = parse_sqlalchemy_schema(ImagingBase, "imaging")
    assert len(tables) > 0


def test_parse_sqlalchemy_schema_payments():
    """Verify SQLAlchemy schema extraction for payments models."""
    from apps.payments.infrastructure.models import Base as PaymentsBase

    tables = parse_sqlalchemy_schema(PaymentsBase, "payments")
    assert len(tables) > 0


def test_parse_sqlalchemy_schema_knowledge():
    """Verify SQLAlchemy schema extraction for knowledge models."""
    import apps.knowledge.infrastructure.training_models  # noqa: F401
    from apps.knowledge.infrastructure.models import Base as KnowledgeBase

    tables = parse_sqlalchemy_schema(KnowledgeBase, "knowledge")
    assert len(tables) > 0


def test_extract_all_service_schemas_includes_new_services():
    """Verify extract_all_service_schemas includes fileshare, imaging, payments, and knowledge."""
    schemas = extract_all_service_schemas()
    for service_id in ["fileshare", "imaging", "payments", "knowledge"]:
        assert service_id in schemas
        assert len(schemas[service_id]["tables"]) > 0


def test_get_designer_schema():
    """Verify static graph schema loading for Designer/MDR."""
    nodes = get_designer_schema()
    if not nodes:
        pytest.skip("Designer service transcribed to .NET C#")

    study_node = next((n for n in nodes if n["class_name"] == "Study"), None)
    assert study_node is not None
    assert study_node["service"] == "designer"
    assert study_node["gxp"] is True


def test_generate_schema_documentation_main(tmp_path, monkeypatch):
    """Verify full end-to-end HTML visualizer generation."""
    # Temporarily redirect output files to tmp_path
    import scripts.generate_schema_documentation

    output_html_file = tmp_path / "docs" / "schema_visualizer.html"
    output_index_file = tmp_path / "docs" / "schema" / "index.html"

    # Patch ROOT_DIR to tmp_path so files write to isolated temporary directories
    monkeypatch.setattr(scripts.generate_schema_documentation, "ROOT_DIR", tmp_path)

    scripts.generate_schema_documentation.main()

    # Assert HTML and CDISC JSON files are written and have valid content
    output_cdisc_file = tmp_path / "docs" / "CDISC" / "cdisc_usdm_compliance.json"
    assert output_html_file.exists()
    assert output_index_file.exists()
    assert output_cdisc_file.exists()

    content = output_html_file.read_text(encoding="utf-8")
    assert "<!DOCTYPE html>" in content
    assert "Interactive Boundary Explorer" in content
    assert "FDA 21 CFR Part 11 & EU Annex 11 Compliance" in content
    assert "schemaData" in content

    import json

    cdisc_data = json.loads(output_cdisc_file.read_text(encoding="utf-8"))
    assert cdisc_data["usdmVersion"] == "3.0"
    assert len(cdisc_data["biomedicalConcepts"]) > 0
