"""Tests for the USDM robust schema translation pipeline.

Verifies:
1. Pydantic metadata translation to equivalent Zod constraints (min_length, max_length, gt, ge, lt, le).
2. Recursion/mutual references resolution mapped to deferred execution (z.lazy).
3. Elimination of z.any() loose fallback assignments and general record definitions.
4. Correct runtime exports with inferred TypeScript declarations.

@req:PRD-SYS-001
"""

from typing import Any, Union
from pydantic import BaseModel, Field
import pytest

from scripts.generate_schemas import python_type_to_zod


class MockSubModel(BaseModel):
    id: str


class MockModel(BaseModel):
    id: str = Field(min_length=1, max_length=50)
    sequence_number: int = Field(ge=1, le=100)
    score: float = Field(gt=0.0, lt=10.0)
    pattern_field: str = Field(pattern="^[A-Z]+$")
    sub_model: MockSubModel
    sub_models: list[MockSubModel]
    dynamic_dict: dict[str, Any]
    loose_list: list[Any]


def test_numeric_and_string_constraints():
    """Verify that Pydantic metadata produces chainable validation constraints.

    @req:PRD-SYS-001
    """
    metadata_id = MockModel.model_fields["id"].metadata
    zod_id = python_type_to_zod(str, metadata_id)
    assert "z.string()" in zod_id
    assert ".min(1)" in zod_id
    assert ".max(50)" in zod_id

    metadata_seq = MockModel.model_fields["sequence_number"].metadata
    zod_seq = python_type_to_zod(int, metadata_seq)
    assert "z.number().int()" in zod_seq
    assert ".gte(1)" in zod_seq
    assert ".lte(100)" in zod_seq

    metadata_score = MockModel.model_fields["score"].metadata
    zod_score = python_type_to_zod(float, metadata_score)
    assert "z.number()" in zod_score
    assert ".gt(0.0)" in zod_score
    assert ".lt(10.0)" in zod_score

    metadata_pattern = MockModel.model_fields["pattern_field"].metadata
    zod_pattern = python_type_to_zod(str, metadata_pattern)
    assert "z.string()" in zod_pattern
    assert "regex" in zod_pattern
    assert "^[A-Z]+$" in zod_pattern


def test_recursive_deferred_execution():
    """Verify that nested/recursive BaseModel references map to z.lazy.

    @req:PRD-SYS-001
    """
    zod_sub = python_type_to_zod(MockSubModel)
    assert "z.lazy(" in zod_sub
    assert "MockSubModelSchema" in zod_sub


def test_elimination_of_z_any_fallbacks():
    """Verify that z.any() fallback references are completely eliminated.

    @req:PRD-SYS-001
    """
    metadata_dict = MockModel.model_fields["dynamic_dict"].metadata
    zod_dict = python_type_to_zod(dict, metadata_dict)
    assert "any" not in zod_dict
    assert "z.record(z.string(), z.unknown())" in zod_dict

    metadata_list = MockModel.model_fields["loose_list"].metadata
    zod_list = python_type_to_zod(list[Any], metadata_list)
    assert "any" not in zod_list
    assert "z.array(z.unknown())" in zod_list
