import os
import sys
import types
from typing import Any, Union, get_args, get_origin

from pydantic import BaseModel

# Ensure we can import apps
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from apps.designer.domain.cdisc.usdm_models import (
    Activity,
    BiomedicalConcept,
    BiomedicalConceptProperty,
    Code,
    EligibilityCriterion,
    Encounter,
    StudyArm,
    StudyDesign,
    StudyEpoch,
    StudyVersion,
    SyntaxTemplate,
    USDMStudy,
)

MODELS = [
    Code,
    SyntaxTemplate,
    EligibilityCriterion,
    BiomedicalConceptProperty,
    BiomedicalConcept,
    Activity,
    Encounter,
    StudyArm,
    StudyEpoch,
    StudyDesign,
    StudyVersion,
    USDMStudy,
]


def python_type_to_zod(py_type: Any) -> str:
    origin = get_origin(py_type)
    args = get_args(py_type)

    if origin is Union or origin is types.UnionType:
        # Check if None is in the Union (Optional)
        non_none_args = [arg for arg in args if arg is not type(None)]
        if len(non_none_args) == 1:
            zod_base = python_type_to_zod(non_none_args[0])
            return f"{zod_base}.nullable().optional()"
        union_schemas = [python_type_to_zod(arg) for arg in non_none_args]
        zod_base = f"z.union([{', '.join(union_schemas)}])"
        if type(None) in args:
            return f"{zod_base}.nullable().optional()"
        return zod_base

    if origin is list or py_type is list:
        if args:
            inner_type = args[0]
            if inner_type is Any:
                return "z.array(z.any())"
            return f"z.array({python_type_to_zod(inner_type)})"
        return "z.array(z.any())"

    if origin is dict or py_type is dict:
        return "z.record(z.any())"

    if py_type is str:
        return "z.string()"
    if py_type is int:
        return "z.number().int()"
    if py_type is float:
        return "z.number()"
    if py_type is bool:
        return "z.boolean()"
    if py_type is Any:
        return "z.any()"

    if isinstance(py_type, type) and issubclass(py_type, BaseModel):
        return f"{py_type.__name__}Schema"

    return "z.any()"


def main():
    # Halt on production-level environments
    for env_var in ["NODE_ENV", "APP_ENV", "ENVIRONMENT", "STAGE"]:
        val = os.getenv(env_var, "").lower()
        if "prod" in val or val == "production":
            sys.stderr.write(
                f"Error: Schema generation is blocked in production environment ({env_var}={os.getenv(env_var)}).\n"
            )
            sys.exit(1)

    # Filter out sensitive internal/audit tables
    allowed_models = []
    for model in MODELS:
        name = model.__name__.lower()
        if (
            "audit" in name
            or "seal" in name
            or "credential" in name
            or "secret" in name
            or "private" in name
        ):
            continue
        allowed_models.append(model)

    output_dir = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "../packages/usdm-schemas/src")
    )
    os.makedirs(output_dir, exist_ok=True)
    output_file = os.path.join(output_dir, "index.ts")

    lines = []
    lines.append(
        "// This file is auto-generated from Python USDM models. DO NOT EDIT DIRECTLY."
    )
    lines.append('import { z } from "zod";')
    lines.append("")

    for model in allowed_models:
        name = model.__name__
        lines.append(f"export const {name}Schema = z.object({{")
        for field_name, field_info in model.model_fields.items():
            # Use alias if defined (for camelCase alignment with serialized API JSON payloads)
            target_name = field_info.alias if field_info.alias else field_name
            zod_type = python_type_to_zod(field_info.annotation)

            # Check if there is a default or default_factory
            has_default = (
                field_info.default is not None and field_info.default is not ...
            ) or field_info.default_factory is not None

            # Special default matching for lists or empty structures if needed
            if zod_type.startswith("z.array"):
                lines.append(f"  {target_name}: {zod_type}.default([]),")
            elif has_default:
                default_val = field_info.default
                if isinstance(default_val, str):
                    lines.append(
                        f'  {target_name}: {zod_type}.default("{default_val}"),'
                    )
                elif isinstance(default_val, bool):
                    lines.append(
                        f"  {target_name}: {zod_type}.default({str(default_val).lower()}),"
                    )
                elif isinstance(default_val, (int, float)):
                    lines.append(f"  {target_name}: {zod_type}.default({default_val}),")
                else:
                    lines.append(f"  {target_name}: {zod_type},")
            else:
                lines.append(f"  {target_name}: {zod_type},")

        lines.append("});")
        lines.append(f"export type {name} = z.infer<typeof {name}Schema>;")
        lines.append("")

    with open(output_file, "w") as f:
        f.write("\n".join(lines))

    print(f"Successfully generated Zod schemas and TypeScript types at: {output_file}")


if __name__ == "__main__":
    main()
