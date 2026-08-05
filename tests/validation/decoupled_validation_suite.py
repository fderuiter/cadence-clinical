import pytest
from pathlib import Path
from scripts.validate_decoupled_suite import (
    check_documentation_index,
    check_clinical_directories_alignment,
    check_format_parity,
    check_clinical_template_filenames,
    check_therapeutic_integration_registration,
)

def test_decoupled_validation_suite_positive_state():
    """
    Verification Suite - Asserts that the current repository documentation, 
    clinical structures, file naming, and format parities are completely 
    correct and aligned with CDISC/TMF specs.
    @req:PRD-SYS-001
    """
    passed, errors = check_documentation_index()
    assert passed is True, f"Check 1 failed: {errors}"

    passed, errors = check_clinical_directories_alignment()
    assert passed is True, f"Check 2 failed: {errors}"

    passed, errors = check_format_parity()
    assert passed is True, f"Check 3 failed: {errors}"

    passed, errors = check_clinical_template_filenames()
    assert passed is True, f"Check 4 failed: {errors}"

    passed, errors = check_therapeutic_integration_registration()
    assert passed is True, f"Check 5 failed: {errors}"


def test_check_documentation_index_negative_state(tmp_path):
    """
    Asserts that the documentation index checker detects broken links 
    and incorrect file mapping targets.
    """
    docs_dir = tmp_path / "docs"
    docs_dir.mkdir()
    
    # Write a DOCUMENTATION_INDEX.md with a broken link and a broken Mermaid file reference
    index_content = """
# Documentation Index
- **[Broken Link](non_existent_file.md)**
- **[Valid Link](valid_file.md)**

```mermaid
graph TD
    A --> B["Some node (broken_diagram_ref.md)"]
```
    """
    (docs_dir / "DOCUMENTATION_INDEX.md").write_text(index_content, encoding="utf-8")
    (docs_dir / "valid_file.md").touch()
    
    passed, errors = check_documentation_index(docs_dir=docs_dir, app_root=tmp_path)
    assert passed is False
    assert len(errors) >= 2
    assert any("non_existent_file.md" in err for err in errors)
    assert any("broken_diagram_ref.md" in err for err in errors)


def test_check_clinical_directories_alignment_negative_state(tmp_path):
    """
    Asserts that the clinical directory alignment checker raises a failure 
    if any clinical domain directory remains unmapped in the CDISC guide.
    """
    cdisc_dir = tmp_path / "CDISC"
    cdisc_dir.mkdir()
    ecrfs_dir = cdisc_dir / "eCRFs"
    ecrfs_dir.mkdir()
    
    # Write CDISC guide with only demographics mapped
    readme_content = """
# CDISC Standards
| Domain Code | Subdirectory Name |
|-------------|-------------------|
| **DM** | `Demographics` |
    """
    (cdisc_dir / "README.md").write_text(readme_content, encoding="utf-8")
    
    # Create two clinical directories on disk: Demographics (mapped) and Adverse_Events (unmapped)
    (ecrfs_dir / "Demographics").mkdir()
    (ecrfs_dir / "Adverse_Events").mkdir()
    
    passed, errors = check_clinical_directories_alignment(cdisc_dir=cdisc_dir, ecrfs_dir=ecrfs_dir)
    assert passed is False
    assert any("Adverse_Events" in err for err in errors)
    assert not any("Demographics" in err for err in errors)


def test_check_format_parity_negative_state(tmp_path):
    """
    Asserts that format parity verification fails if any spreadsheet (.xlsx) 
    lacks its twin structural JSON format file.
    """
    cdisc_dir = tmp_path / "CDISC"
    cdisc_dir.mkdir()
    
    # Create matching parity files
    (cdisc_dir / "matching.xlsx").touch()
    (cdisc_dir / "matching.json").touch()
    
    # Create non-matching file (spreadsheet only)
    (cdisc_dir / "missing_twin.xlsx").touch()
    
    passed, errors = check_format_parity(cdisc_dir=cdisc_dir, app_root=tmp_path)
    assert passed is False
    assert any("missing_twin.json" in err for err in errors)


def test_check_clinical_template_filenames_negative_state(tmp_path):
    """
    Asserts that clinical template scanner rejects files with typos, 
    incorrect suffixes, or casing inconsistencies.
    """
    ecrfs_dir = tmp_path / "eCRFs"
    ecrfs_dir.mkdir()
    
    # Create file with a spelling typo in template name
    (ecrfs_dir / "TIG_Aderse_Experiences_PDF.pdf").touch()
    
    # Create file with an incorrect extension suffix mismatch (_XML suffix with .txt extension)
    (ecrfs_dir / "LB_LOCAL_XML.txt").touch()
    
    passed, errors = check_clinical_template_filenames(ecrfs_dir=ecrfs_dir, app_root=tmp_path)
    assert passed is False
    assert any("spelling typo" in err for err in errors)
    assert any("incorrect extension" in err for err in errors)


def test_check_therapeutic_integration_registration_negative_state(tmp_path):
    """
    Asserts that checking therapeutic integration guides fails if newly added 
    therapeutic folders are not registered in the clinical guide.
    """
    cdisc_dir = tmp_path / "CDISC"
    cdisc_dir.mkdir()
    tig_dir = cdisc_dir / "TIG"
    tig_dir.mkdir()
    
    # Write CDISC guide registering only oncology TIG
    readme_content = """
# CDISC Reference
The registered therapeutic integration folders are: oncology
    """
    (cdisc_dir / "README.md").write_text(readme_content, encoding="utf-8")
    
    # Create two folders under TIG: oncology (registered) and cardiovascular (unregistered)
    (tig_dir / "oncology").mkdir()
    (tig_dir / "cardiovascular").mkdir()
    
    passed, errors = check_therapeutic_integration_registration(cdisc_dir=cdisc_dir, tig_dir=tig_dir)
    assert passed is False
    assert any("cardiovascular" in err for err in errors)
    assert not any("oncology" in err for err in errors)
