"""Automated Unit Test Suite for eCRF Layout Synthesis Engine.

Validates comprehensive CDASH domain mapping, Biomedical Concept Value-Level
Metadata (VLM) resolution to UI widgets (text, numeric, select, vas_slider,
body_map_74_zone), responsive column spans (12/8/6/4), declarative edit check
synthesis, and input structure polymorphisms (USDMStudy, dict, list, extraction DTO).

Requirements: PRD-CRF-004, PRD-DDF-001, PRD-SYS-001, PRD-MDR-007
"""

from __future__ import annotations

from apps.designer.domain.cdisc.usdm_models import (
    Activity,
    BiomedicalConcept,
    BiomedicalConceptProperty,
    StudyDesign,
    USDMStudy,
)
from apps.designer.domain.synthesis.crf_synthesizer import (
    CRFSynthesizer,
    resolve_widget_representation,
    synthesize_crf_layout_from_usdm,
)


def test_widget_representation_resolution_all_types() -> None:
    """Validate resolution of UI widgets from data types and configurations.

    @req:PRD-CRF-004
    """
    # 1. VAS Slider
    w_type, span, cfg = resolve_widget_representation("vas_slider", "QS_VAS_PAIN")
    assert w_type == "vas_slider"
    assert span == 8
    assert cfg["min_value"] == 0
    assert cfg["max_value"] == 100
    assert cfg["step"] == 1
    assert "No Pain" in cfg["min_label"]
    assert "Worst" in cfg["max_label"]

    # 2. 74-Zone SNOMED CT Body Map
    w_type, span, cfg = resolve_widget_representation("body_map", "PE_BODY_MAP")
    assert w_type == "body_map_74_zone"
    assert span == 12
    assert cfg["zones_total"] == 74
    assert cfg["snomed_ct_version"] == "2024-09"
    assert cfg["multiselect"] is True

    # 3. Categorical / Select Choice
    w_type, span, cfg = resolve_widget_representation(
        "choice", "DM_SEX", options=["M", "F"]
    )
    assert w_type == "choice"
    assert span == 6

    # 4. Numeric Integer and Decimal
    w_type, span, _ = resolve_widget_representation("integer", "VS_SYSBP", grid_span=6)
    assert w_type == "integer"
    assert span == 6

    w_type, span, _ = resolve_widget_representation("decimal", "LB_HGB", grid_span=4)
    assert w_type == "decimal"
    assert span == 4

    # 5. Boolean
    w_type, span, _ = resolve_widget_representation("boolean", "AE_SERIOUS")
    assert w_type == "boolean"
    assert span == 6

    # 6. Text (Wide Comment vs Standard Field)
    w_type, span, _ = resolve_widget_representation("text", "VS_COMM")
    assert w_type == "text"
    assert span == 12

    w_type, span, _ = resolve_widget_representation("text", "AE_TERM")
    assert w_type == "text"
    assert span == 12


def test_synthesize_from_usdm_study_model() -> None:
    """Validate synthesis from a fully-populated USDMStudy domain model.

    @req:PRD-CRF-004
    @req:PRD-DDF-001
    """
    study = USDMStudy(
        id="study_synthesis_001",
        name="SYNTH-STUDY-001",
        protocol_title="Cardiovascular and Oncology Synthesis Study",
        usdm_version="4.0",
        study_designs=[
            StudyDesign(
                id="sd_01",
                name="Main Design",
                activities=[
                    Activity(
                        id="act_vs",
                        name="Vital Signs Assessment",
                        cdash_domain="VS",
                    ),
                    Activity(
                        id="act_eg",
                        name="12-Lead Electrocardiogram",
                        cdash_domain="EG",
                    ),
                    Activity(
                        id="act_lb",
                        name="Safety Chemistry & Hematology",
                        cdash_domain="LB",
                    ),
                    Activity(
                        id="act_qs",
                        name="Pain and Health Questionnaires",
                        cdash_domain="QS",
                    ),
                    Activity(
                        id="act_pe",
                        name="Physical Examination",
                        cdash_domain="PE",
                    ),
                    Activity(
                        id="act_dm",
                        name="Demographics & Baseline",
                        cdash_domain="DM",
                    ),
                    Activity(
                        id="act_ae",
                        name="Adverse Events Monitoring",
                        cdash_domain="AE",
                    ),
                    Activity(
                        id="act_cm",
                        name="Prior and Concomitant Medications",
                        cdash_domain="CM",
                    ),
                    Activity(
                        id="act_mh",
                        name="Medical History Review",
                        cdash_domain="MH",
                    ),
                    Activity(
                        id="act_ie",
                        name="Inclusion and Exclusion Criteria",
                        cdash_domain="IE",
                    ),
                ],
            )
        ],
    )

    forms = synthesize_crf_layout_from_usdm(study)
    assert len(forms) == 10

    domain_forms = {f.cdash_domain: f for f in forms}

    # Verify VS form
    vs = domain_forms["VS"]
    assert vs.form_id == "FORM_VS"
    assert any(i["field_id"] == "VS_SYSBP" for i in vs.items)
    assert any(i["field_id"] == "VS_DIABP" for i in vs.items)
    assert any("VS_SYSBP > VS_DIABP" in r["condition"] for r in vs.rules)

    # Verify EG form
    eg = domain_forms["EG"]
    assert eg.form_id == "FORM_EG"
    assert any(i["field_id"] == "EG_QTC" for i in eg.items)
    assert any("EG_QTC <= 500" in r["condition"] for r in eg.rules)

    # Verify LB form
    lb = domain_forms["LB"]
    assert lb.form_id == "FORM_LB"
    assert any(i["field_id"] == "LB_HGB" for i in lb.items)
    assert any(i["field_id"] == "LB_CREAT" for i in lb.items)

    # Verify QS form
    qs = domain_forms["QS"]
    vas_item = next(i for i in qs.items if i["field_id"] == "QS_VAS_PAIN")
    assert vas_item["data_type"] == "vas_slider"
    assert vas_item["config"]["min_value"] == 0
    assert vas_item["config"]["max_value"] == 100

    # Verify PE form
    pe = domain_forms["PE"]
    bm_item = next(i for i in pe.items if i["field_id"] == "PE_BODY_MAP")
    assert bm_item["data_type"] == "body_map_74_zone"
    assert bm_item["config"]["zones_total"] == 74

    # Verify DM form
    dm = domain_forms["DM"]
    assert any(i["field_id"] == "DM_AGE" and i["mandatory"] is True for i in dm.items)

    # Verify AE form
    ae = domain_forms["AE"]
    assert any(i["field_id"] == "AE_TERM" for i in ae.items)
    assert any(r["rule_id"] == "CHK_AE_DATES_SANITY" for r in ae.rules)

    # Verify CM form
    cm = domain_forms["CM"]
    assert any(i["field_id"] == "CM_TRT" for i in cm.items)

    # Verify MH form
    mh = domain_forms["MH"]
    assert any(i["field_id"] == "MH_TERM" for i in mh.items)

    # Verify IE form
    ie = domain_forms["IE"]
    assert any(i["field_id"] == "IE_ALL_MET" for i in ie.items)


def test_synthesize_with_biomedical_concept_value_level_metadata() -> None:
    """Validate that Biomedical Concept value-level metadata properly enriches and overrides eCRF layout items.

    @req:PRD-CRF-004
    @req:PRD-MDR-007
    """
    study = USDMStudy(
        id="study_vlm_001",
        name="VLM-STUDY",
        biomedical_concepts=[
            BiomedicalConcept(
                id="bc_custom_vs",
                name="Custom Vital Signs Profile",
                cdash_domain="VS",
                properties=[
                    BiomedicalConceptProperty(
                        id="VS_SYSBP_CUSTOM",
                        name="sysbp_custom",
                        label="Systolic Pressure (Sitting)",
                        cdash_variable="VS.SYSBP",
                        data_type="numeric",
                        mandatory=True,
                        grid_span=6,
                        unit="mmHg",
                        range="60-220",
                    ),
                    BiomedicalConceptProperty(
                        id="VS_DIABP_CUSTOM",
                        name="diabp_custom",
                        label="Diastolic Pressure (Sitting)",
                        cdash_variable="VS.DIABP",
                        data_type="numeric",
                        mandatory=True,
                        grid_span=6,
                        unit="mmHg",
                        range="40-140",
                    ),
                    BiomedicalConceptProperty(
                        id="VS_POS",
                        name="position",
                        label="Subject Measurement Position",
                        cdash_variable="VS.VSPOS",
                        data_type="select",
                        options=["Sitting", "Supine", "Standing"],
                        mandatory=True,
                        grid_span=6,
                    ),
                ],
            )
        ],
        study_designs=[
            StudyDesign(
                id="sd_01",
                name="VLM Design",
                activities=[
                    Activity(
                        id="act_custom_vs",
                        name="Custom Vital Signs Procedure",
                        cdash_domain="VS",
                    )
                ],
            )
        ],
    )

    forms = synthesize_crf_layout_from_usdm(study)
    assert len(forms) == 1
    vs_form = forms[0]
    assert vs_form.cdash_domain == "VS"
    assert len(vs_form.items) == 3

    field_map = {i["field_id"]: i for i in vs_form.items}
    assert "VS_SYSBP_CUSTOM" in field_map
    assert field_map["VS_SYSBP_CUSTOM"]["label"] == "Systolic Pressure (Sitting)"
    assert field_map["VS_SYSBP_CUSTOM"]["grid_span"] == 6
    assert field_map["VS_SYSBP_CUSTOM"]["unit"] == "mmHg"

    assert "VS_POS" in field_map
    assert field_map["VS_POS"]["data_type"] == "choice"
    assert "Sitting" in field_map["VS_POS"]["options"]


def test_synthesize_from_raw_dictionary_and_list() -> None:
    """Validate synthesis from raw dictionaries and lists of activity payloads.

    @req:PRD-DDF-001
    """
    # 1. Dictionary with activities
    raw_dict = {
        "study_title": "Raw Dict Study",
        "activities": [
            {"name": "Vital Signs Monitoring", "cdash_domain": "VS"},
            {"name": "12-Lead Electrocardiogram", "cdash_domain": "EG"},
        ],
    }
    forms_from_dict = synthesize_crf_layout_from_usdm(raw_dict)
    assert len(forms_from_dict) == 2
    domains = {f.cdash_domain for f in forms_from_dict}
    assert "VS" in domains
    assert "EG" in domains

    # 2. List of activity dictionaries
    raw_list = [
        {"name": "Visual Analog Scale", "cdash_domain": "QS"},
        {"name": "Physical Exam Anatomical Lesion Map", "cdash_domain": "PE"},
        {"name": "Novel Biomarker Assessment", "cdash_domain": "NV"},
    ]
    forms_from_list = synthesize_crf_layout_from_usdm(raw_list)
    assert len(forms_from_list) == 3

    # Verify custom unmapped domain fallback
    nv_form = next(f for f in forms_from_list if f.cdash_domain == "NV")
    assert nv_form.form_id == "FORM_NV"
    nv_fields = [i["field_id"] for i in nv_form.items]
    assert "NV_PERF" in nv_fields
    assert "NV_COMM" in nv_fields


def test_crf_synthesizer_class_service() -> None:
    """Validate CRFSynthesizer class service and catalog overrides.

    @req:PRD-CRF-004
    """
    custom_catalog = {
        "CO": {
            "form_name": "Comments Log eCRF",
            "items": [
                {
                    "field_id": "CO_VAL",
                    "label": "Comment Text",
                    "data_type": "text",
                    "cdash_variable": "CO.COVAL",
                    "mandatory": True,
                    "grid_span": 12,
                }
            ],
        }
    }

    synthesizer = CRFSynthesizer(catalog_overrides=custom_catalog)
    assert "CO" in synthesizer.catalog

    activities = [{"name": "General Comments", "cdash_domain": "CO"}]
    forms = synthesizer.synthesize(activities)
    assert len(forms) == 1
    assert forms[0].cdash_domain == "CO"
    assert forms[0].items[0]["field_id"] == "CO_VAL"
