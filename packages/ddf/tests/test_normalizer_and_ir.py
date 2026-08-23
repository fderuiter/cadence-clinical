"""Unit tests for USDM Normalizer Stage and DDF Semantic IR Builder."""

import pytest

from packages.ddf.stages.ir_builder import DDFSemanticIRBuilderStage
from packages.ddf.stages.normalizer import USDMNormalizerStage


def test_normalizer_missing_protocol_raises():
    normalizer = USDMNormalizerStage()
    with pytest.raises(ValueError, match="'protocol' missing"):
        normalizer.normalize("study-123", {"name": "Test Study"})


def test_normalizer_synthesizes_fallbacks():
    normalizer = USDMNormalizerStage()
    raw = {
        "protocol": {"items": [{"id": "sys_bp", "name": "Systolic BP", "type": "int"}]}
    }
    normalized, warnings = normalizer.normalize("study-123", raw)
    assert normalized["name"] == "Default Study Name"
    assert len(warnings) >= 1
    assert any(w.field == "study_id" for w in warnings)


def test_ir_builder_processes_items_and_rules():
    raw_payload = {
        "name": "Hypertension Study",
        "protocol": {
            "items": [
                {
                    "id": "sys_bp",
                    "name": "Systolic Blood Pressure",
                    "type": "integer",
                    "cols": 2,
                    "rules": [
                        {
                            "type": "constraint",
                            "condition": {
                                "type": "comparison",
                                "operator": ">=",
                                "operands": [
                                    {
                                        "type": "field_ref",
                                        "field_ref": {"field_id": "sys_bp"},
                                    },
                                    {"type": "constant", "value": 60},
                                ],
                            },
                            "query_message": "Value must be at least 60",
                        }
                    ],
                }
            ]
        },
    }
    builder = DDFSemanticIRBuilderStage()
    ir = builder.build_ir("study-999", raw_payload)
    assert ir.study_id == "study-999"
    assert ir.name == "Hypertension Study"
    assert len(ir.items) == 1

    item = ir.items[0]
    assert item.id == "sys_bp"
    assert item.appearance == "w2"
    assert item.constraint == "(/sys_bp >= 60)"
    assert item.constraint_message == "Value must be at least 60"
    assert "en" in item.localizations
    assert item.localizations["en"]["label"] == "Systolic Blood Pressure"
