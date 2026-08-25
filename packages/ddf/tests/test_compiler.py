"""Unit tests for the Deep DDFCompiler pipeline."""

import pytest

from packages.ddf import CompileOptions, DDFCompiler
from packages.ddf.models import AccessibilityReport
from packages.ddf.stages.accessibility import LayoutAccessibilityStage


class MockAccessibilityValidator:
    """Mock accessibility validator for fast unit testing."""

    def __init__(self, should_fail: bool = False) -> None:
        self.should_fail = should_fail

    async def validate(self, openrosa_xml: str) -> AccessibilityReport:
        if self.should_fail:
            return AccessibilityReport(
                status="FAIL",
                form_hash="dummy_hash",
                violations=[
                    {
                        "id": "label-missing",
                        "description": "Form input missing label",
                        "nodes": [],
                    }
                ],
            )
        return AccessibilityReport(
            status="PASS",
            form_hash="dummy_hash",
            passes=[{"id": "valid-layout"}],
        )


def test_ddf_compiler_uninitialized_validator_raises():
    """Verify that initializing DDFCompiler without an accessibility validator raises a ValueError configuration error."""
    with pytest.raises(
        ValueError, match="Accessibility validator port is uninitialized or missing"
    ):
        DDFCompiler()


def test_layout_accessibility_stage_uninitialized_validator_raises():
    """Verify that initializing LayoutAccessibilityStage without a validator raises ValueError."""
    with pytest.raises(
        ValueError, match="Accessibility validator port is uninitialized or missing"
    ):
        LayoutAccessibilityStage(validator=None)


def test_compile_options_default_accessibility_validation_active():
    """Verify that CompileOptions defaults to active accessibility auditing."""
    opts = CompileOptions()
    assert opts.validate_accessibility is True


@pytest.mark.asyncio
async def test_ddf_compiler_end_to_end_success():
    compiler = DDFCompiler(
        accessibility_validator=MockAccessibilityValidator(should_fail=False)
    )
    study_payload = {
        "name": "Phase 2 Oncology Protocol",
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
                                    {"type": "constant", "value": 50},
                                ],
                            },
                            "query_message": "Systolic BP must be >= 50",
                        }
                    ],
                },
                {
                    "id": "dia_bp",
                    "name": "Diastolic Blood Pressure",
                    "type": "integer",
                    "cols": 2,
                },
            ]
        },
    }

    result = await compiler.compile(
        study_id="study-oncology-01",
        payload=study_payload,
        options=CompileOptions(validate_accessibility=True),
    )

    assert result.status == "COMPLETED"
    assert result.study_id == "study-oncology-01"
    assert result.name == "Phase 2 Oncology Protocol"
    assert "ODM.study-oncology-01" in result.odm_xml
    assert "Systolic Blood Pressure" in result.odm_xml
    assert 'data id="study-oncology-01"' in result.openrosa_xml
    assert 'appearance="w2"' in result.openrosa_xml
    assert 'constraint="(/sys_bp &gt;= 50)"' in result.openrosa_xml
    assert result.form_hash is not None
    assert len(result.form_hash) == 64
    assert result.accessibility_report is not None
    assert result.accessibility_report.status == "PASS"


@pytest.mark.asyncio
async def test_ddf_compiler_accessibility_failure_raises():
    compiler = DDFCompiler(
        accessibility_validator=MockAccessibilityValidator(should_fail=True)
    )
    study_payload = {
        "name": "Broken Protocol",
        "protocol": {"items": [{"id": "item_1", "name": "Item 1", "type": "string"}]},
    }

    with pytest.raises(ValueError, match="Layout/Accessibility Validation Failed"):
        await compiler.compile("study-broken", study_payload)
