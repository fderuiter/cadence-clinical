import re
import uuid
from datetime import datetime

import pytest
from protocol_render import (
    ExportMetadata,
    NarrativeItemView,
    NarrativeSectionView,
    RenderedProtocolDocument,
    SoACellView,
    SoAHeaderEncounter,
    SoAHeaderEpoch,
    SoAMatrixView,
    SoARowView,
    SynopsisView,
)
from pydantic import ValidationError
from usdm_model import Study


def test_export_metadata_valid_initial():
    """
    Test that a valid ExportMetadata with version_index = 1 parses successfully
    with or without change_reason.
    """
    meta = ExportMetadata(creator="user123")
    assert meta.creator == "user123"
    assert meta.version_index == 1
    assert meta.change_reason is None
    assert isinstance(meta.timestamp, datetime)

    meta_with_reason = ExportMetadata(
        creator="user123",
        change_reason="Initial export",
        version_index=1,
    )
    assert meta_with_reason.change_reason == "Initial export"


def test_export_metadata_invalid_version():
    """
    Test that version_index < 1 raises a ValidationError.
    """
    with pytest.raises(ValidationError) as exc:
        ExportMetadata(creator="user123", version_index=0)
    assert "version_index must be greater than or equal to 1" in str(exc.value)


def test_export_metadata_missing_change_reason_on_version_bump():
    """
    Test that for version_index > 1, a non-empty change_reason is required.
    """
    # Missing completely
    with pytest.raises(ValidationError) as exc:
        ExportMetadata(creator="user123", version_index=2)
    assert "change_reason is required and must be non-empty" in str(exc.value)

    # Empty string
    with pytest.raises(ValidationError) as exc:
        ExportMetadata(creator="user123", version_index=2, change_reason="  ")
    assert "change_reason is required and must be non-empty" in str(exc.value)


def test_export_metadata_valid_version_bump():
    """
    Test that a valid ExportMetadata with version_index > 1 and non-empty change_reason
    parses successfully.
    """
    meta = ExportMetadata(
        creator="user123",
        version_index=2,
        change_reason="Updated section 3.2",
    )
    assert meta.version_index == 2
    assert meta.change_reason == "Updated section 3.2"


def test_narrative_item_and_section_views():
    """
    Test creation and ordering of NarrativeItemView and NarrativeSectionView.
    """
    item1 = NarrativeItemView(
        id="item-1",
        name="intro_p1",
        text="This is the first paragraph of the introduction.",
        order=1,
    )
    item2 = NarrativeItemView(
        id="item-2",
        name="intro_p2",
        text="This is the second paragraph.",
        order=2,
    )

    subsection = NarrativeSectionView(
        section_id="sec-1.1",
        section_number="1.1",
        title="Background Information",
        items=[item1, item2],
        order=1,
    )

    parent_section = NarrativeSectionView(
        section_id="sec-1",
        section_number="1",
        title="Introduction",
        items=[],
        subsections=[subsection],
        order=1,
    )

    assert parent_section.title == "Introduction"
    assert len(parent_section.subsections) == 1
    assert parent_section.subsections[0].section_number == "1.1"
    assert len(parent_section.subsections[0].items) == 2
    assert (
        parent_section.subsections[0].items[0].text
        == "This is the first paragraph of the introduction."
    )


def test_synopsis_view_parsing():
    """
    Test that a SynopsisView parses key clinical summary fields.
    """
    synopsis = SynopsisView(
        study_id="study-abc",
        protocol_title="A Phase II Trial of Compound X in Patients with Disease Y",
        protocol_number="PROT-X-202",
        sponsor_name="Pharma Corp",
        phase="Phase II",
        objectives=[
            "To evaluate the efficacy of Compound X.",
            "To assess safety and tolerability.",
        ],
        study_design_type="Randomized, Double-Blind, Placebo-Controlled",
        population="Adults with diagnosed Disease Y.",
        sample_size=150,
        duration="12 weeks",
        interventions=["Compound X 50mg daily", "Placebo daily"],
    )

    assert synopsis.study_id == "study-abc"
    assert synopsis.phase == "Phase II"
    assert len(synopsis.objectives) == 2
    assert synopsis.sample_size == 150
    assert synopsis.interventions[0] == "Compound X 50mg daily"


def test_soa_matrix_view():
    """
    Test Epochs, Encounters, Rows, Cells, and complete SoAMatrixView construction.
    """
    epoch_tx = SoAHeaderEpoch(epoch_id="ep-tx", epoch_name="Treatment", sequence=1)
    epoch_fu = SoAHeaderEpoch(epoch_id="ep-fu", epoch_name="Follow-up", sequence=2)

    visit1 = SoAHeaderEncounter(
        encounter_id="v1", encounter_name="Week 1", epoch_id="ep-tx", sequence=1
    )
    visit2 = SoAHeaderEncounter(
        encounter_id="v2", encounter_name="Week 2", epoch_id="ep-tx", sequence=2
    )
    visit_fu = SoAHeaderEncounter(
        encounter_id="v3", encounter_name="End of Study", epoch_id="ep-fu", sequence=3
    )

    cell1 = SoACellView(
        activity_id="act-vitals",
        encounter_id="v1",
        epoch_id="ep-tx",
        is_applicable=True,
    )
    cell2 = SoACellView(
        activity_id="act-vitals",
        encounter_id="v2",
        epoch_id="ep-tx",
        is_applicable=True,
    )
    cell3 = SoACellView(
        activity_id="act-vitals",
        encounter_id="v3",
        epoch_id="ep-fu",
        is_applicable=False,
        details="Not required unless clinically indicated.",
    )

    row = SoARowView(
        activity_id="act-vitals",
        activity_name="Vital Signs",
        cells=[cell1, cell2, cell3],
    )

    matrix = SoAMatrixView(
        epochs=[epoch_tx, epoch_fu],
        encounters=[visit1, visit2, visit_fu],
        rows=[row],
    )

    assert len(matrix.epochs) == 2
    assert len(matrix.encounters) == 3
    assert len(matrix.rows) == 1
    assert matrix.rows[0].activity_name == "Vital Signs"
    assert matrix.rows[0].cells[2].is_applicable is False
    assert (
        matrix.rows[0].cells[2].details == "Not required unless clinically indicated."
    )


def test_rendered_protocol_document_with_usdm_study():
    """
    Test the integrated RenderedProtocolDocument model wrapping metadata,
    synopsis, narrative, and SoA, including the official usdm_model.Study type.
    """
    study_id_uuid = str(uuid.uuid4())
    # Instantiate official USDM Study model
    usdm_study = Study(
        id=study_id_uuid,
        name="Study 2026-X",
        instanceType="Study",
    )

    meta = ExportMetadata(
        creator="auditor1", change_reason="Routine FDA submission", version_index=3
    )
    synopsis = SynopsisView(
        study_id="study-abc",
        protocol_title="Integrated Study Protocol",
        phase="Phase III",
    )
    matrix = SoAMatrixView()

    doc = RenderedProtocolDocument(
        metadata=meta,
        synopsis=synopsis,
        narrative_sections=[],
        soa_matrix=matrix,
        source_study=usdm_study,
    )

    assert doc.metadata.creator == "auditor1"
    assert doc.metadata.version_index == 3
    assert doc.synopsis.protocol_title == "Integrated Study Protocol"
    assert doc.source_study is not None
    assert str(doc.source_study.id) == study_id_uuid
    assert doc.source_study.name == "Study 2026-X"


def get_sample_rendered_document():
    meta = ExportMetadata(
        creator="test_user",
        change_reason="Routine update",
        version_index=2,
    )
    synopsis = SynopsisView(
        study_id="study_test",
        protocol_title="Test Title 123",
        protocol_number="PRT-999",
        sponsor_name="Sponsor Corp",
        phase="Phase III",
        objectives=["Objective A", "Objective B"],
        study_design_type="Parallel",
        population="Healthy adults",
        sample_size=100,
        duration="6 Months",
        interventions=["Intervention 1"],
    )

    # Narrative hierarchy
    item1 = NarrativeItemView(id="n1", name="intro", text="Intro para text", order=1)
    subsec = NarrativeSectionView(
        section_id="s1_1",
        section_number="1.1",
        title="Background Information",
        items=[item1],
        order=1,
    )
    sec = NarrativeSectionView(
        section_id="s1",
        section_number="1.0",
        title="Introduction Section Title",
        items=[],
        subsections=[subsec],
        order=1,
    )

    # SoA
    epoch = SoAHeaderEpoch(epoch_id="ep1", epoch_name="Screening", sequence=1)
    encounter = SoAHeaderEncounter(
        encounter_id="enc1", encounter_name="Visit 1", epoch_id="ep1", sequence=1
    )
    cell = SoACellView(
        activity_id="act1",
        encounter_id="enc1",
        epoch_id="ep1",
        is_applicable=True,
        details="Vitals detail",
    )
    row = SoARowView(
        activity_id="act1", activity_name="Vitals Collection", cells=[cell]
    )
    soa = SoAMatrixView(epochs=[epoch], encounters=[encounter], rows=[row])

    from usdm_model import Study

    usdm_study = Study(id=str(uuid.uuid4()), name="Test Study", instanceType="Study")

    return RenderedProtocolDocument(
        metadata=meta,
        synopsis=synopsis,
        narrative_sections=[sec],
        soa_matrix=soa,
        source_study=usdm_study,
    )


def test_render_protocol_to_html_combined():
    """
    Assert narrative sections and SoA table structure render correctly in combined output.
    """
    from bs4 import BeautifulSoup

    from apps.designer.rendering import render_protocol_to_html

    doc = get_sample_rendered_document()
    html = render_protocol_to_html(doc, "combined")
    soup = BeautifulSoup(html, "html.parser")

    # General assertions
    assert soup.find("title").text == "Test Title 123"

    # Narrative ordering and structure assertions
    headings = [h.text.strip() for h in soup.find_all(["h2", "h3", "h4"])]
    assert "1.0 Introduction Section Title" in headings
    assert "1.1 Background Information" in headings
    # Assert hierarchy order: Introduction appears before Background Information
    intro_idx = headings.index("1.0 Introduction Section Title")
    bg_idx = headings.index("1.1 Background Information")
    assert intro_idx < bg_idx

    # Content assertions
    items = [
        div.text.strip()
        for d in soup.find_all("div", class_="narrative-item")
        for div in [d]
    ]
    assert "Intro para text" in items

    # SoA Table and structures assertions
    table = soup.find("table", class_="soa-table")
    assert table is not None
    headers = [th.text.strip() for th in table.find_all("th")]
    assert "Activity / Procedure" in headers
    assert "Screening" in headers
    assert "Visit 1" in headers

    row_data = [td.text.strip() for td in table.find_all("td")]
    assert any("Vitals Collection" in r for r in row_data)
    # Check cell applicability and detail rendering
    applicable_cells = table.find_all("td", class_="applicable")
    assert len(applicable_cells) == 1
    assert "X" in applicable_cells[0].text
    assert (
        "Vitals detail" in applicable_cells[0].find("span", class_="cell-details").text
    )


def test_render_protocol_to_html_synopsis_only():
    """
    Assert gated output correctly includes synopsis but excludes other sections.
    """
    from bs4 import BeautifulSoup

    from apps.designer.rendering import render_protocol_to_html

    doc = get_sample_rendered_document()
    html = render_protocol_to_html(doc, "synopsis")
    soup = BeautifulSoup(html, "html.parser")

    assert soup.find("h1", string=re.compile("1. PROTOCOL SYNOPSIS")) is not None
    assert soup.find("h1", string=re.compile("2. STUDY NARRATIVE")) is None
    assert soup.find("div", class_="soa-section") is None


def test_render_protocol_to_html_narrative_only():
    """
    Assert gated output correctly includes narrative but excludes synopsis/SoA.
    """
    from bs4 import BeautifulSoup

    from apps.designer.rendering import render_protocol_to_html

    doc = get_sample_rendered_document()
    html = render_protocol_to_html(doc, "narrative")
    soup = BeautifulSoup(html, "html.parser")

    assert soup.find("h1", string=re.compile("1. PROTOCOL SYNOPSIS")) is None
    assert soup.find("h1", string=re.compile("2. STUDY NARRATIVE")) is not None
    assert soup.find("div", class_="soa-section") is None


def test_render_protocol_to_html_soa_only():
    """
    Assert gated output correctly includes SoA but excludes other sections.
    """
    from bs4 import BeautifulSoup

    from apps.designer.rendering import render_protocol_to_html

    doc = get_sample_rendered_document()
    html = render_protocol_to_html(doc, "soa")
    soup = BeautifulSoup(html, "html.parser")

    assert soup.find("h1", string=re.compile("1. PROTOCOL SYNOPSIS")) is None
    assert soup.find("h1", string=re.compile("2. STUDY NARRATIVE")) is None
    assert soup.find("div", class_="soa-section") is not None


def test_export_metadata_dual_fields():
    """
    Verify 21 CFR Part 11 compliant ExportMetadata synchronized/backward-compatible
    fields behave correctly under bidirectional propagation.
    """
    # 1. Initialize with new-style created_by
    meta1 = ExportMetadata(created_by="system_user")
    assert meta1.created_by == "system_user"
    assert meta1.creator == "system_user"
    assert meta1.timestamp == meta1.created_at

    # 2. Initialize with new-style reason_for_change and version_index > 1
    meta2 = ExportMetadata(
        created_by="system_user",
        version_index=2,
        reason_for_change="Corrected Section 1.2",
    )
    assert meta2.reason_for_change == "Corrected Section 1.2"
    assert meta2.change_reason == "Corrected Section 1.2"

    # 3. Validation failure when neither creator nor created_by is provided
    with pytest.raises(ValidationError) as exc:
        ExportMetadata(version_index=1)
    assert "Field 'creator' or 'created_by' is required." in str(exc.value)


def test_narrative_content_usdm_models():
    """
    Verify NarrativeContent and NarrativeContentItem can be constructed
    properly conforming to CDISC USDM specification.
    """
    from protocol_render import NarrativeContent, NarrativeContentItem

    # Create NarrativeContentItem
    item = NarrativeContentItem(
        id="item-abc-123",
        name="objective_bullet_1",
        text="To evaluate the efficacy of study treatment.",
    )
    assert item.id == "item-abc-123"
    assert item.name == "objective_bullet_1"
    assert item.text == "To evaluate the efficacy of study treatment."
    assert item.instanceType == "NarrativeContentItem"

    # Create NarrativeContent
    section = NarrativeContent(
        id="section-xyz-789",
        name="objectives_section",
        sectionNumber="1.1",
        sectionTitle="Study Objectives",
        displaySectionNumber=True,
        displaySectionTitle=True,
        childIds=["item-abc-123"],
    )
    assert section.id == "section-xyz-789"
    assert section.sectionNumber == "1.1"
    assert section.sectionTitle == "Study Objectives"
    assert section.displaySectionNumber is True
    assert section.displaySectionTitle is True
    assert section.childIds == ["item-abc-123"]
    assert section.instanceType == "NarrativeContent"
