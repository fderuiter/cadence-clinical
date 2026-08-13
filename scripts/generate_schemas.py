import os
import sys
import types
from typing import Any, Union, get_args, get_origin

from pydantic import BaseModel

# Ensure we can import apps
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from apps.designer.domain.cdisc.usdm_models import (
    Activity,
    Code,
    EligibilityCriterion,
    Encounter,
    StudyArm,
    StudyDesign,
    StudyEpoch,
    SyntaxTemplate,
    USDMStudy,
)

MODELS = [
    Code,
    SyntaxTemplate,
    EligibilityCriterion,
    Activity,
    Encounter,
    StudyArm,
    StudyEpoch,
    StudyDesign,
    USDMStudy,
]


def python_type_to_zod(py_type: Any, metadata: list[Any] = None) -> str:
    origin = get_origin(py_type)
    args = get_args(py_type)

    if origin is Union or origin is types.UnionType:
        # Check if None is in the Union (Optional)
        non_none_args = [arg for arg in args if arg is not type(None)]
        if len(non_none_args) == 1:
            zod_base = python_type_to_zod(non_none_args[0], metadata)
            return f"{zod_base}.nullable().optional()"
        union_schemas = [python_type_to_zod(arg, metadata) for arg in non_none_args]
        zod_base = f"z.union([{', '.join(union_schemas)}])"
        if type(None) in args:
            return f"{zod_base}.nullable().optional()"
        return zod_base

    if origin is list or py_type is list:
        if args:
            inner_type = args[0]
            if inner_type is Any:
                zod_base = "z.array(z.unknown())"
            else:
                zod_base = f"z.array({python_type_to_zod(inner_type)})"
        else:
            zod_base = "z.array(z.unknown())"

        # Apply list-level constraints if metadata is provided
        if metadata:
            for m in metadata:
                if hasattr(m, 'min_length') and m.min_length is not None:
                    zod_base += f".min({m.min_length})"
                if hasattr(m, 'max_length') and m.max_length is not None:
                    zod_base += f".max({m.max_length})"
        return zod_base

    if origin is dict or py_type is dict:
        return "z.record(z.string(), z.unknown())"

    if py_type is str:
        zod_base = "z.string()"
        if metadata:
            for m in metadata:
                if hasattr(m, 'min_length') and m.min_length is not None:
                    zod_base += f".min({m.min_length})"
                if hasattr(m, 'max_length') and m.max_length is not None:
                    zod_base += f".max({m.max_length})"
                if hasattr(m, 'pattern') and m.pattern is not None:
                    pattern_escaped = m.pattern.replace("\\", "\\\\").replace('"', '\\"')
                    zod_base += f'.regex(new RegExp("{pattern_escaped}"))'
        return zod_base

    if py_type is int:
        zod_base = "z.number().int()"
        if metadata:
            for m in metadata:
                if hasattr(m, 'gt') and m.gt is not None:
                    zod_base += f".gt({m.gt})"
                if hasattr(m, 'ge') and m.ge is not None:
                    zod_base += f".gte({m.ge})"
                if hasattr(m, 'lt') and m.lt is not None:
                    zod_base += f".lt({m.lt})"
                if hasattr(m, 'le') and m.le is not None:
                    zod_base += f".lte({m.le})"
                if hasattr(m, 'multiple_of') and m.multiple_of is not None:
                    zod_base += f".multipleOf({m.multiple_of})"
        return zod_base

    if py_type is float:
        zod_base = "z.number()"
        if metadata:
            for m in metadata:
                if hasattr(m, 'gt') and m.gt is not None:
                    zod_base += f".gt({m.gt})"
                if hasattr(m, 'ge') and m.ge is not None:
                    zod_base += f".gte({m.ge})"
                if hasattr(m, 'lt') and m.lt is not None:
                    zod_base += f".lt({m.lt})"
                if hasattr(m, 'le') and m.le is not None:
                    zod_base += f".lte({m.le})"
                if hasattr(m, 'multiple_of') and m.multiple_of is not None:
                    zod_base += f".multipleOf({m.multiple_of})"
        return zod_base

    if py_type is bool:
        return "z.boolean()"

    if py_type is Any:
        return "z.unknown()"

    if isinstance(py_type, type) and issubclass(py_type, BaseModel):
        return f"z.lazy(() => {py_type.__name__}Schema)"

    return "z.unknown()"


def main():
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

    for model in MODELS:
        name = model.__name__
        lines.append(f"export const {name}Schema = z.object({{")
        for field_name, field_info in model.model_fields.items():
            # Use alias if defined (for camelCase alignment with serialized API JSON payloads)
            target_name = field_info.alias if field_info.alias else field_name
            zod_type = python_type_to_zod(field_info.annotation, field_info.metadata)

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
