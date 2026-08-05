#!/usr/bin/env python3
import sys
import re
from pathlib import Path

# Absolute paths starting with /app
APP_ROOT = Path("/app")
DOCS_DIR = APP_ROOT / "docs"
CDISC_DIR = DOCS_DIR / "CDISC"
ECRFS_DIR = CDISC_DIR / "Library" / "Data_Collection" / "eCRFs"
TIG_DIR = ECRFS_DIR / "TIG"

def resolve_mermaid_ref(ref):
    """
    Map common nicknames or abbreviations inside the Mermaid diagrams to their physical files.
    """
    mapping = {
        "PRD.md": "SDLC/01_Product_Requirements_Document_PRD.md",
        "RTM.md": "SDLC/Requirements_Traceability_Matrix.md",
        "TDD": "SDLC/02_Technical_Design_Document_TDD.md",
        "API Specs": "SDLC/03_API_Integration_Specification.md",
        "Audit Specs": "SDLC/05_Security_Compliance_Audit_Spec.md",
        "QA Plan": "SDLC/06_QA_Validation_Plan.md",
        "Dev Env": "LOCAL_DEV_ENVIRONMENT.md"
    }
    if ref in mapping:
        return mapping[ref]
    return ref

def check_documentation_index(docs_dir=DOCS_DIR, app_root=APP_ROOT):
    print("Executing Check 1: Documentation Index Link & Diagram Verification...")
    doc_index_path = docs_dir / "DOCUMENTATION_INDEX.md"
    if not doc_index_path.exists():
        return False, ["DOCUMENTATION_INDEX.md does not exist on disk."]
    
    content = doc_index_path.read_text(encoding="utf-8")
    errors = []
    
    # Extract markdown links: [label](path)
    # Ignore absolute links starting with http/https or anchors starting with #
    markdown_links = re.findall(r'\[([^\]]+)\]\(([^)]+)\)', content)
    for label, path in markdown_links:
        if path.startswith(("http://", "https://", "#")):
            continue
        # Resolve path relative to docs_dir
        target_path = (docs_dir / path).resolve()
        if not target_path.exists():
            errors.append(f"Broken markdown link in DOCUMENTATION_INDEX.md: '{label}' points to non-existent file '{path}' (resolved: {target_path})")
            
    # Parse Mermaid diagram nodes referencing files
    mermaid_blocks = re.findall(r'```mermaid(.*?)```', content, re.DOTALL)
    for block in mermaid_blocks:
        # Find all files with .md extension in the mermaid block
        files = re.findall(r'\b[\w\-\.\/]+\.md\b', block)
        # Also find specific nicknames without .md extension
        nicknames = ["TDD", "API Specs", "Audit Specs", "QA Plan", "Dev Env"]
        for nickname in nicknames:
            if nickname in block:
                files.append(nickname)
                
        for file in files:
            resolved_file = resolve_mermaid_ref(file)
            target_path_docs = (docs_dir / resolved_file).resolve()
            target_path_root = (app_root / resolved_file).resolve()
            if not target_path_docs.exists() and not target_path_root.exists():
                errors.append(f"Mermaid diagram in DOCUMENTATION_INDEX.md refers to non-existent file '{file}'")
                
    if errors:
        return False, errors
    return True, []

def check_clinical_directories_alignment(cdisc_dir=CDISC_DIR, ecrfs_dir=ECRFS_DIR):
    print("Executing Check 2: CDISC Flowchart/Table Clinical Directory Alignment...")
    readme_path = cdisc_dir / "README.md"
    if not readme_path.exists():
        return False, ["CDISC README.md does not exist on disk."]
        
    content = readme_path.read_text(encoding="utf-8")
    errors = []
    
    # Parse directories mapped under Covered eCRF Clinical Domains
    mapped_dirs = set()
    for line in content.splitlines():
        if line.strip().startswith("|") and line.count("|") >= 3:
            parts = [p.strip() for p in line.split("|")]
            # parts[0] is empty, parts[1] is Domain Code, parts[2] is Subdirectory Name
            col_val = parts[2]
            matches = re.findall(r'`([^`]+)`', col_val)
            for m in matches:
                mapped_dirs.add(m.strip())
            
    # Physically existing subdirectories under ecrfs_dir
    if not ecrfs_dir.exists():
        return False, [f"eCRFs directory does not exist: {ecrfs_dir}"]
        
    physical_dirs = {p.name for p in ecrfs_dir.iterdir() if p.is_dir() and p.name != ".git"}
    
    # Check alignment
    for pdir in physical_dirs:
        if pdir not in mapped_dirs and pdir.replace("_", " ") not in mapped_dirs:
            errors.append(f"Clinical domain directory '{pdir}' is left unmapped in the CDISC flowchart/table of README.md.")
            
    if errors:
        return False, errors
    return True, []

def check_format_parity(cdisc_dir=CDISC_DIR, app_root=APP_ROOT):
    print("Executing Check 3: Excel vs JSON Format Parity Verification...")
    errors = []
    # Search cdisc_dir recursively for *.xlsx or *.xls
    for path in cdisc_dir.rglob("*"):
        if path.is_file() and path.suffix in (".xlsx", ".xls"):
            # Ignore temp files starting with ~$
            if path.name.startswith("~$"):
                continue
            # Corresponding JSON path
            json_path = path.with_suffix(".json")
            if not json_path.exists():
                errors.append(f"Spreadsheet file '{path.relative_to(app_root)}' does not have a corresponding JSON format equivalent '{json_path.relative_to(app_root)}'")
                
    if errors:
        return False, errors
    return True, []

def check_clinical_template_filenames(ecrfs_dir=ECRFS_DIR, app_root=APP_ROOT):
    print("Executing Check 4: Clinical Template Filenames convention, extension, and spelling checks...")
    errors = []
    
    for path in ecrfs_dir.rglob("*"):
        if not path.is_file():
            continue
            
        filename = path.name
        
        # Check for spelling typos
        if "aderse" in filename.lower():
            errors.append(f"Clinical template filename '{path.relative_to(app_root)}' contains a spelling typo ('Aderse' instead of 'Adverse')")
            
        # Check for incorrect suffixes or extensions
        suffix_to_ext = {
            "_XML": ".xml",
            "_HTML": ".html",
            "_PDF": ".pdf",
            "_Excel": (".xlsx", ".json")
        }
        for suffix, expected_exts in suffix_to_ext.items():
            if suffix in filename:
                if isinstance(expected_exts, str):
                    if path.suffix.lower() != expected_exts.lower():
                        errors.append(f"Clinical template filename '{path.relative_to(app_root)}' has an incorrect extension. Expected suffix '{suffix}' to end with '{expected_exts}' but got '{path.suffix}'")
                else:
                    if path.suffix.lower() not in expected_exts:
                        errors.append(f"Clinical template filename '{path.relative_to(app_root)}' has an incorrect extension. Expected suffix '{suffix}' to end with one of {expected_exts} but got '{path.suffix}'")
                        
        # Check case convention
        if "_xml." in filename or "_html." in filename or "_pdf." in filename or "_excel." in filename:
            errors.append(f"Clinical template filename '{path.relative_to(app_root)}' does not match the designated uppercase case convention.")
            
    if errors:
        return False, errors
    return True, []

def check_therapeutic_integration_registration(cdisc_dir=CDISC_DIR, tig_dir=TIG_DIR):
    print("Executing Check 5: Therapeutic Integration Subdirectory Registration...")
    readme_path = cdisc_dir / "README.md"
    if not readme_path.exists():
        return False, ["CDISC README.md does not exist on disk."]
        
    content = readme_path.read_text(encoding="utf-8")
    errors = []
    
    if not tig_dir.exists():
        return False, [f"TIG directory does not exist: {tig_dir}"]
        
    tig_subdirs = {p.name for p in tig_dir.iterdir() if p.is_dir() and p.name != ".git"}
    
    for subdir in tig_subdirs:
        if not re.search(r'\b' + re.escape(subdir) + r'\b', content):
            errors.append(f"Therapeutic area subdirectory '{subdir}' is not registered/documented in the clinical guide (docs/CDISC/README.md).")
            
    if errors:
        return False, errors
    return True, []

def main():
    all_errors = []
    
    checks = [
        check_documentation_index,
        check_clinical_directories_alignment,
        check_format_parity,
        check_clinical_template_filenames,
        check_therapeutic_integration_registration
    ]
    
    for check in checks:
        passed, errors = check()
        if not passed:
            all_errors.extend(errors)
            
    if all_errors:
        print("\n=== validation suite failed ===")
        print(f"Detected {len(all_errors)} validation errors:")
        for err in all_errors:
            print(f" - {err}")
        sys.exit(1)
        
    print("\n✔ Decoupled validation suite passed successfully!")
    sys.exit(0)

if __name__ == "__main__":
    main()
