import re
import sys
import time
from pathlib import Path

# Absolute paths
REPO_ROOT = Path(__file__).resolve().parent.parent
TS_FILE = REPO_ROOT / "packages" / "usdm-schemas" / "src" / "index.ts"
PY_FILE = REPO_ROOT / "apps" / "designer" / "domain" / "cdisc" / "usdm_models.py"

DOCSTRINGS = {
    "Code": "USDM Code / Concept representation.",
    "SyntaxTemplate": "Syntax template definition for rules and eligibility criteria.",
    "EligibilityCriterion": "Eligibility criterion (Inclusion or Exclusion).",
    "Activity": "Study activity or procedure definition.",
    "Encounter": "Study encounter / visit definition.",
    "StudyArm": "Study arm definition.",
    "StudyEpoch": "Study epoch definition.",
    "StudyDesign": "Study design containing arms, epochs, encounters, activities, and criteria.",
    "USDMStudy": "Root USDM protocol study specification container.",
}


def camel_to_snake(name: str) -> str:
    """Convert camelCase to snake_case."""
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def get_python_field_info(field_name: str, zod_type: str) -> str:
    """Translate TS field name and Zod type into Python field string."""
    # 1. Convert field_name to snake_case
    py_field_name = camel_to_snake(field_name)
    has_alias = py_field_name != field_name

    # 2. Extract base type and modifiers
    is_list = "z.array(" in zod_type
    is_dict = "z.record(" in zod_type
    is_nullable = ".nullable()" in zod_type or ".optional()" in zod_type

    # Check default value
    default_val = None
    default_match = re.search(r"\.default\((.*?)\)", zod_type)
    if default_match:
        default_val = default_match.group(1).strip()

    # Extract base type name
    if is_list:
        inner_match = re.search(r"z\.array\((.*?)\)", zod_type)
        inner_zod = inner_match.group(1).strip() if inner_match else "z.any()"
        if "z.string" in inner_zod:
            inner_type = "str"
        elif "z.record" in inner_zod:
            inner_type = "dict[str, Any]"
        elif "Schema" in inner_zod:
            inner_type = inner_zod.replace("Schema", "")
        else:
            inner_type = "Any"
        py_type = f"list[{inner_type}]"
    elif is_dict:
        py_type = "dict[str, Any]"
    elif "z.string" in zod_type:
        py_type = "str"
    elif "z.number().int" in zod_type:
        py_type = "int"
    elif "z.number" in zod_type:
        py_type = "float"
    elif "z.boolean" in zod_type:
        py_type = "bool"
    elif "z.any" in zod_type:
        py_type = "Any"
    elif "Schema" in zod_type:
        schema_match = re.search(r"(\w+)Schema", zod_type)
        base_schema = schema_match.group(1) if schema_match else "Any"
        py_type = base_schema
    else:
        py_type = "Any"

    if is_nullable:
        py_type = f"{py_type} | None"

    # Construct default value expression
    field_args = []

    if is_list:
        field_args.append("default_factory=list")
    elif default_val is not None:
        if default_val == "[]":
            field_args.append("default_factory=list")
        else:
            field_args.append(f"default={default_val}")
    elif is_nullable:
        field_args.append("default=None")

    if has_alias:
        field_args.append(f'alias="{field_name}"')

    # Specific description for criterionType
    if py_field_name == "criterion_type":
        field_args.append('description="Inclusion or Exclusion"')

    if field_args:
        if len(field_args) == 1 and field_args[0] == "default=None":
            default_expr = " = None"
        else:
            default_expr = f" = Field({', '.join(field_args)})"
    else:
        default_expr = ""

    return f"    {py_field_name}: {py_type}{default_expr}"


def compile_schemas():
    """Parse TS Zod schemas and compile them to Python BaseModel definitions."""
    if not TS_FILE.exists():
        print(f"Error: {TS_FILE} does not exist.", file=sys.stderr)
        sys.exit(1)

    print(f"Reading TypeScript schemas from {TS_FILE}...")
    with open(TS_FILE, encoding="utf-8") as f:
        content = f.read()

    # Match: export const <Model>Schema = z.object({ ... });
    pattern = re.compile(r"export const (\w+)Schema\s*=\s*z\.object\(\{([\s\S]*?)\}\);")
    matches = pattern.findall(content)

    if not matches:
        print("Warning: No Schema matches found in the TypeScript file.")

    classes_code = []

    for model_name, fields_block in matches:
        docstring = DOCSTRINGS.get(model_name, f"USDM {model_name} schema.")
        field_lines = []

        lines = fields_block.strip().split("\n")
        for line in lines:
            line = line.strip()
            if not line:
                continue
            if ":" not in line:
                continue
            field_name, zod_type = line.split(":", 1)
            field_name = field_name.strip()
            zod_type = zod_type.strip()
            if zod_type.endswith(","):
                zod_type = zod_type[:-1].strip()

            field_code = get_python_field_info(field_name, zod_type)
            field_lines.append(field_code)

        fields_str = "\n".join(field_lines)

        class_template = f"""class {model_name}(BaseModel):
    \"\"\"{docstring}\"\"\"

    model_config = ConfigDict(
        populate_by_name=True, extra="ignore", frozen=True, validate_assignment=True
    )

{fields_str}"""
        classes_code.append(class_template)

    classes_section = "\n\n\n".join(classes_code)

    file_template = f"""# This file is auto-generated from packages/usdm-schemas/src/index.ts.
# DO NOT EDIT DIRECTLY.
\"\"\"CDISC USDM v2.0 and v3.0 Pydantic v2 data models.

Provides strictly-typed objects representing the Unified Study Data Model (USDM)
protocol graph structure, including study designs, encounters, activities, and
eligibility criteria.

Requirements: PRD-SYS-001
\"\"\"

from typing import Any

from pydantic import BaseModel, ConfigDict, Field


{classes_section}
"""

    PY_FILE.parent.mkdir(parents=True, exist_ok=True)
    with open(PY_FILE, "w", encoding="utf-8") as f:
        f.write(file_template)

    print(f"Successfully generated Python classes at {PY_FILE}")

    # Run formatting via subprocess
    try:
        import subprocess

        # Format
        subprocess.run(["uv", "run", "ruff", "format", str(PY_FILE)], check=True)
        # Fix imports/lint issues
        subprocess.run(
            ["uv", "run", "ruff", "check", str(PY_FILE), "--fix"], check=False
        )
        print("Formatted and lint-checked with ruff.")
    except Exception as e:
        print(f"Warning: Failed to format/check with ruff: {e}")


def main():
    if "--watch" in sys.argv:
        print(f"Watching {TS_FILE} for changes...")
        last_mtime = TS_FILE.stat().st_mtime if TS_FILE.exists() else 0
        while True:
            try:
                mtime = TS_FILE.stat().st_mtime if TS_FILE.exists() else 0
                if mtime != last_mtime:
                    print("Change detected, recompiling...")
                    compile_schemas()
                    last_mtime = mtime
                time.sleep(0.5)
            except KeyboardInterrupt:
                print("\nWatch stopped.")
                break
            except Exception as e:
                print(f"Error in watch loop: {e}")
                time.sleep(1)
    else:
        compile_schemas()


if __name__ == "__main__":
    main()
