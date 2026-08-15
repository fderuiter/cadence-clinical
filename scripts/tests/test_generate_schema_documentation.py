"""Unit tests for the automated schema boundaries and documentation generator.

Verifies model extraction, GxP and immutability mapping, and file output.
"""

from scripts.generate_schema_documentation import (
    get_designer_schema,
    parse_sqlalchemy_schema,
)


def test_parse_sqlalchemy_schema_execution():
    """Verify SQLAlchemy schema extraction for execution models."""
    from apps.execution.database.models import Base as ExecutionBase

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

    # Check some column details
    subject_cols = {c["name"]: c for c in subject_table["columns"]}
    assert "subject_id" in subject_cols
    assert "study_id" in subject_cols
    assert "encrypted_demographics" in subject_cols
    assert subject_cols["subject_id"]["primary_key"] is False


def test_parse_sqlalchemy_schema_etmf():
    """Verify SQLAlchemy schema extraction for eTMF models."""
    from apps.etmf.adapters.models import Base as EtmfBase

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


def test_get_designer_schema():
    """Verify static graph schema loading for Designer/MDR."""
    nodes = get_designer_schema()
    assert len(nodes) > 0

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

    # Assert both files are written and have valid HTML layout
    assert output_html_file.exists()
    assert output_index_file.exists()

    content = output_html_file.read_text(encoding="utf-8")
    assert "<!DOCTYPE html>" in content
    assert "Interactive Boundary Explorer" in content
    assert "FDA 21 CFR Part 11 & EU Annex 11 Compliance" in content
    assert "schemaData" in content
