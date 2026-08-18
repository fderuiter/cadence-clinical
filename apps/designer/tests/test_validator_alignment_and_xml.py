"""Comprehensive test suite for XML Mapping, Alignment Report, Review Comments, and Sentinel.

Requirements: PRD-SYS-001, PRD-DDF-001, PRD-MDR-007
"""

from unittest.mock import AsyncMock, patch

import pytest

from apps.designer.application.services.quality_sentinel import (
    ProtocolQualitySentinel,
    make_patient_context,
)
from apps.designer.presentation.routers.comments import (
    MOCK_FORM_COMMENTS,
    CommentCreatePayload,
    get_form_comments,
    post_form_comment,
)
from apps.designer.validator import (
    generate_alignment_report,
)
from apps.designer.xml_mapping import (
    is_valid_xml_name,
    validate_mapping_csv,
)


def test_xml_name_validation():
    """Verify W3C XML name regex compliance."""
    assert is_valid_xml_name("validName") is True
    assert is_valid_xml_name("prefix:validLocalName") is True
    assert is_valid_xml_name("_valid_with_underscore") is True
    assert is_valid_xml_name("valid-with-hyphen") is True
    assert is_valid_xml_name("valid.with.dot") is True

    # Invalid names
    assert is_valid_xml_name("") is False
    assert is_valid_xml_name("123invalidStart") is False
    assert is_valid_xml_name("invalid with spaces") is False
    assert is_valid_xml_name("prefix:too:many:colons") is False
    assert is_valid_xml_name(":invalidPrefix") is False
    assert is_valid_xml_name("invalidSuffix:") is False


def test_validate_mapping_csv():
    """Verify CSV validation for CDISC XML mapping rules."""
    valid_csv = "to_name,to_alias,description\nVS_SYSBP,ItemGroup_VS,Systolic Blood Pressure\nprefix:EG_HR,alias:EG_HR,Heart Rate"
    rows = validate_mapping_csv(valid_csv)
    assert len(rows) == 2
    assert rows[0]["to_name"] == "VS_SYSBP"

    # Missing headers
    with pytest.raises(ValueError, match="Missing mandatory headers"):
        validate_mapping_csv("name,alias\nVS,ItemGroup")

    with pytest.raises(ValueError, match="Missing headers"):
        validate_mapping_csv("")

    # Invalid XML name in to_name
    invalid_csv = "to_name,to_alias\n123_invalid,ValidAlias"
    with pytest.raises(ValueError, match="Invalid XML name in 'to_name' column"):
        validate_mapping_csv(invalid_csv)

    # Invalid XML name in to_alias
    invalid_alias_csv = "to_name,to_alias\nValidName,invalid alias with spaces"
    with pytest.raises(ValueError, match="Invalid XML name in 'to_alias' column"):
        validate_mapping_csv(invalid_alias_csv)


@pytest.mark.asyncio
async def test_form_review_comments_endpoints():
    """Verify form review comments creation and retrieval."""
    MOCK_FORM_COMMENTS.clear()
    current_user = {"sub": "data_manager_1", "roles": ["data_manager"]}

    payload = CommentCreatePayload(
        field_id="VS_SYSBP",
        comment_text="Please verify allowable range for pediatric population.",
    )

    created = await post_form_comment("form_vs_001", payload, current_user)
    assert created.form_id == "form_vs_001"
    assert created.field_id == "VS_SYSBP"
    assert created.author_id == "data_manager_1"
    assert created.status == "Open"
    assert created.is_resolved is False

    comments = await get_form_comments("form_vs_001", current_user)
    assert len(comments) == 1
    assert comments[0].id == created.id
    assert comments[0].text == "Please verify allowable range for pediatric population."


@pytest.mark.asyncio
async def test_generate_alignment_report_with_complete_mapping():
    """Verify USDM-to-ODM alignment reporting engine."""
    study_id = "STUDY-TEST-ALIGN-01"
    mock_study_payload = {
        "id": study_id,
        "name": "Phase 2 Oncology Protocol",
        "versions": [
            {
                "id": "v1.0",
                "versionIdentifier": "1.0",
                "studyDesigns": [
                    {
                        "id": "design_1",
                        "name": "Parallel Design",
                        "activities": [
                            {
                                "id": "act_vs",
                                "name": "Vital Signs",
                                "biomedicalConceptIds": ["bc_sysbp", "bc_diabp"],
                            }
                        ],
                        "epochs": [
                            {
                                "id": "epoch_screen",
                                "name": "Screening Epoch",
                                "scheduledEvents": [
                                    {
                                        "id": "event_v1",
                                        "name": "Screening Visit",
                                        "activityIds": ["act_vs"],
                                    }
                                ],
                            }
                        ],
                    }
                ],
            }
        ],
        "odm": {
            "ODM": {
                "Study": {
                    "MetaDataVersion": {
                        "ItemGroupDef": {
                            "ItemRef": [
                                {"@ItemOID": "bc_sysbp"},
                                {"@ItemOID": "bc_diabp"},
                            ]
                        }
                    }
                }
            }
        },
    }

    mock_resp = AsyncMock()
    mock_resp.status_code = 200
    mock_resp.json = lambda: mock_study_payload
    mock_resp.raise_for_status = lambda: None

    with patch("httpx.AsyncClient.get", return_value=mock_resp):
        report = await generate_alignment_report(study_id)
        assert report.study_id == study_id
        assert len(report.complete_activities) == 1
        assert report.complete_activities[0].activity_def_id == "act_vs"
        assert report.complete_activities[0].status == "complete"
        assert len(report.complete_activities[0].mapped_items) == 2


def test_quality_sentinel_patient_context_and_readability():
    """Verify Quality Sentinel patient evaluation and readability calculations."""
    patient = {
        "id": "PT_01",
        "name": "Alice",
        "age": 30,
        "gender": "F",
        "systolic_bp": 120,
        "diastolic_bp": 80,
    }

    ctx = make_patient_context(patient)
    assert ctx["AGE"] == 30
    assert ctx["eCRF.DM.AGE"] == 30
    assert ctx["eCRF.VS.SYSBP"] == 120

    sentinel = ProtocolQualitySentinel()
    study_payload = {
        "id": "STUDY-SENTINEL-01",
        "name": "Phase 3 Cardiology Trial",
        "narrative": "This clinical study evaluates drug efficacy in hypertensive adult patients.",
        "studyDesigns": [
            {
                "id": "design_1",
                "name": "Treatment Design",
                "objectives": [{"id": "obj_1", "name": "Evaluate SBP reduction"}],
                "activities": [
                    {
                        "id": "act_1",
                        "name": "Venipuncture Blood Draw",
                        "forms": [{"id": "form_lb", "fields": [{"id": "LB_HGB"}]}],
                    }
                ],
                "encounters": [{"id": "enc_1", "name": "Screening"}],
            }
        ],
        "eligibilityCriteria": [
            {"id": "crit_1", "text": "Age >= 18", "criterionType": "Inclusion"}
        ],
    }

    score = sentinel.evaluate_protocol_quality(study_payload)
    assert score.quality_score >= 0.0
    assert score.readability is not None
    assert score.readability.word_count > 0
    assert score.burden_details is not None
    assert score.burden_details.total_burden >= 0.0
