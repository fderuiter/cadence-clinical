"""Unit tests for USDM Normalizer Stage and DDF Semantic IR Builder."""

import pytest

from packages.ddf.stages.ir_builder import DDFSemanticIRBuilderStage
from packages.ddf.stages.normalizer import USDMNormalizerStage


def test_normalizer_missing_protocol_raises():
    """Verify missing protocol raises ValueError.

    @req:PRD-DDF-001
    """
    normalizer = USDMNormalizerStage()
    with pytest.raises(ValueError, match="'protocol' missing"):
        normalizer.normalize("study-123", {"name": "Test Study"})


def test_normalizer_empty_or_non_dict_raises():
    """Verify non-dict or empty payload raises ValueError.

    @req:PRD-DDF-001
    """
    normalizer = USDMNormalizerStage()
    with pytest.raises(ValueError, match="Payload must be a dictionary"):
        normalizer.normalize("study-123", None)


def test_normalizer_duplicate_ids_raises():
    """Verify duplicate physical IDs in payload raise critical validation error.

    @req:PRD-DDF-001
    """
    normalizer = USDMNormalizerStage(strict_validation=True)
    payload = {
        "id": "study-1",
        "name": "Test",
        "protocol": {
            "items": [
                {"id": "item-1", "name": "A"},
                {"id": "item-1", "name": "B"},
            ]
        },
    }
    with pytest.raises(ValueError, match="Duplicate physical ID 'item-1'"):
        normalizer.normalize("study-1", payload)


def test_normalizer_synthesizes_fallbacks():
    """Verify normalization auto-populates fallback values and documentedBy fields.

    @req:PRD-DDF-001
    """
    normalizer = USDMNormalizerStage()
    raw = {
        "protocol": {"items": [{"id": "sys_bp", "name": "Systolic BP", "type": "int"}]},
        "documentedBy": [
            {
                "language": {"decode": "English"},
                "versions": [{"contents": [{}]}],
            }
        ],
    }
    normalized, warnings = normalizer.normalize("study-123", raw)
    assert normalized["name"] == "Default Study Name"
    assert len(warnings) >= 1
    assert any(w.field == "study_id" for w in warnings)
    doc = normalized["documentedBy"][0]
    assert doc["type"] == "Protocol"
    assert doc["language"]["code"] == "english"
    assert doc["versions"][0]["status"] == "Final"
    assert doc["versions"][0]["contents"][0]["displaySectionTitle"] == "Section Title"


def test_ir_builder_processes_items_and_rules():
    """Verify IR builder parses items, constraint rules, and appearances.

    @req:PRD-DDF-002
    """
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


def test_ir_builder_skip_logic_and_narratives():
    """Verify IR builder resolves skip logic rules and narrative content items for hints/descriptions.

    @req:PRD-DDF-002
    """
    raw_payload = {
        "name": "Narrative Study",
        "documentedBy": [
            {
                "language": {"code": "en"},
                "versions": [
                    {
                        "contents": [
                            {
                                "id": "nc_sys_bp",
                                "name": "sys_bp",
                                "sectionTitle": "Systolic Blood Pressure (mmHg)",
                                "contentItemId": "nci_sys_bp_desc",
                                "childIds": ["nc_sys_bp_hint"],
                            },
                            {
                                "id": "nc_sys_bp_hint",
                                "name": "sys_bp_hint",
                                "sectionTitle": "Measure after 5 min rest",
                            },
                        ],
                        "narrativeContentItems": [
                            {
                                "id": "nci_sys_bp_desc",
                                "text": "Patient sitting BP reading",
                            }
                        ],
                    }
                ],
            }
        ],
        "protocol": {
            "items": [
                {
                    "id": "sys_bp",
                    "name": "Systolic BP",
                    "type": "integer",
                    "rules": [
                        {
                            "type": "skip_logic",
                            "action": "hide",
                            "condition": {
                                "type": "comparison",
                                "operator": "=",
                                "operands": [
                                    {
                                        "type": "field_ref",
                                        "field_ref": {"field_id": "na_check"},
                                    },
                                    {"type": "constant", "value": "true"},
                                ],
                            },
                        }
                    ],
                }
            ]
        },
    }
    builder = DDFSemanticIRBuilderStage()
    ir = builder.build_ir("study-100", raw_payload)
    item = ir.items[0]
    assert item.relevant == "not((/na_check = 'true'))"
    assert item.localizations["en"]["label"] == "Systolic Blood Pressure (mmHg)"
    assert item.localizations["en"]["description"] == "Patient sitting BP reading"
    assert item.localizations["en"]["hint"] == "Measure after 5 min rest"
