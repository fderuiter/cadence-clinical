import uuid

import pytest
import usdm_model
from protocol_render import RenderedProtocolDocument

from apps.designer.content_assembly import (
    assemble_narrative_sections,
    assemble_rendered_protocol_document,
    assemble_soa_matrix,
)


@pytest.fixture
def base_study():
    """
    Returns a valid usdm_model.Study instance with a complete hierarchy
    of synopsis, narrative, and SoA components for unit testing.
    """
    study_id = str(uuid.uuid4())
    version_id = "ver-1"
    design_id = "design-1"

    # Code structures
    phase_code = usdm_model.Code(
        id="code-phase",
        code="P2",
        codeSystem="USDM",
        codeSystemVersion="1.0",
        decode="Phase II",
        instanceType="Code",
    )
    phase_alias = usdm_model.AliasCode(
        id="alias-phase",
        standardCode=phase_code,
        standardCodeAliases=[],
        instanceType="AliasCode",
    )
    org_type_code = usdm_model.Code(
        id="code-org",
        code="SPONSOR",
        codeSystem="USDM",
        codeSystemVersion="1.0",
        decode="Sponsor",
        instanceType="Code",
    )
    study_type_code = usdm_model.Code(
        id="code-st",
        code="INT",
        codeSystem="USDM",
        codeSystemVersion="1.0",
        decode="Interventional",
        instanceType="Code",
    )
    unit_code = usdm_model.Code(
        id="code-unit",
        code="MON",
        codeSystem="USDM",
        codeSystemVersion="1.0",
        decode="Months",
        instanceType="Code",
    )
    unit_alias = usdm_model.AliasCode(
        id="alias-unit",
        standardCode=unit_code,
        standardCodeAliases=[],
        instanceType="AliasCode",
    )

    # Duration and Quantity
    duration_qty = usdm_model.Quantity(
        id="qty-dur", value=12.0, unit=unit_alias, instanceType="Quantity"
    )
    planned_dur = usdm_model.Duration(
        id="dur-plan",
        quantity=duration_qty,
        durationWillVary=False,
        instanceType="Duration",
    )

    # Population
    enrollment_qty = usdm_model.Quantity(
        id="qty-enroll", value=150.0, instanceType="Quantity"
    )
    population = usdm_model.StudyDesignPopulation(
        id="pop-1",
        name="Target Population",
        description="Adult subjects with Disease X.",
        includesHealthySubjects=False,
        plannedEnrollmentNumber=enrollment_qty,
        instanceType="StudyDesignPopulation",
    )

    # Title
    study_title = usdm_model.StudyTitle(
        id="title-1",
        text="A Phase II Study of Compound Y in Disease X",
        type=usdm_model.Code(
            id="code-title-type",
            code="OFFICIAL",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Official",
            instanceType="Code",
        ),
        instanceType="StudyTitle",
    )

    # Study Identifiers
    study_ident = usdm_model.StudyIdentifier(
        id="ident-1",
        text="PROT-Y-202",
        scopeId="Sponsor",
        instanceType="StudyIdentifier",
    )

    # Organizations
    sponsor_org = usdm_model.Organization(
        id="org-sponsor",
        name="PharmaY Corp",
        type=org_type_code,
        identifierScheme="SponsorID",
        identifier="SP-Y-100",
        instanceType="Organization",
    )

    # Objectives
    objective1 = usdm_model.Objective(
        id="obj-1",
        name="Primary Objective",
        text="To evaluate the efficacy of Compound Y.",
        level=usdm_model.Code(
            id="code-lvl-1",
            code="PRIMARY",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Primary",
            instanceType="Code",
        ),
        instanceType="Objective",
    )

    # Interventions
    intervention1 = usdm_model.StudyIntervention(
        id="inter-1",
        name="Compound Y 50mg",
        role=usdm_model.Code(
            id="code-role-1",
            code="ACTIVE",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Active",
            instanceType="Code",
        ),
        type=usdm_model.Code(
            id="code-type-1",
            code="DRUG",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Drug",
            instanceType="Code",
        ),
        instanceType="StudyIntervention",
    )

    # Narrative Content Items (from StudyVersion)
    item1 = usdm_model.NarrativeContentItem(
        id="narr-item-1",
        name="Introduction Paragraph 1",
        text="Introduction text with a reference to the sponsor name: <usdm:ref klass='Organization' id='org-sponsor' attribute='name'></usdm:ref>.",
        instanceType="NarrativeContentItem",
    )
    item2 = usdm_model.NarrativeContentItem(
        id="narr-item-2",
        name="Background Details",
        text="Background detail text.",
        instanceType="NarrativeContentItem",
    )

    # Epochs and Encounters
    epoch1 = usdm_model.StudyEpoch(
        id="epoch-tx",
        name="Treatment Epoch",
        type=usdm_model.Code(
            id="code-ep-1",
            code="TX",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Treatment",
            instanceType="Code",
        ),
        instanceType="StudyEpoch",
    )
    encounter1 = usdm_model.Encounter(
        id="enc-v1",
        name="Week 1 Visit",
        type=usdm_model.Code(
            id="code-enc-1",
            code="V1",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Visit 1",
            instanceType="Code",
        ),
        instanceType="Encounter",
    )
    encounter2 = usdm_model.Encounter(
        id="enc-v2",
        name="Week 4 Visit",
        type=usdm_model.Code(
            id="code-enc-2",
            code="V2",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Visit 2",
            instanceType="Code",
        ),
        instanceType="Encounter",
    )

    # Schedule Timeline & Activity Instances
    activity1 = usdm_model.Activity(
        id="act-vitals", name="Vital Signs Collection", instanceType="Activity"
    )
    activity2 = usdm_model.Activity(
        id="act-blood", name="Blood Draw", instanceType="Activity"
    )

    sched_inst1 = usdm_model.ScheduledActivityInstance(
        id="sched-1",
        name="Vitals week 1",
        encounterId="enc-v1",
        epochId="epoch-tx",
        activityIds=["act-vitals"],
        description="Standard vitals collection.",
        instanceType="ScheduledActivityInstance",
    )
    sched_inst2 = usdm_model.ScheduledActivityInstance(
        id="sched-2",
        name="Vitals and Blood week 4",
        encounterId="enc-v2",
        epochId="epoch-tx",
        activityIds=["act-vitals", "act-blood"],
        description="Vitals and lab blood draw.",
        instanceType="ScheduledActivityInstance",
    )

    timeline = usdm_model.ScheduleTimeline(
        id="timeline-1",
        name="Main Timeline",
        mainTimeline=True,
        entryCondition="Enrollment",
        entryId="entry-1",
        instances=[sched_inst1, sched_inst2],
        plannedDuration=planned_dur,
        instanceType="ScheduleTimeline",
    )

    # Arm and Cells
    arm = usdm_model.StudyArm(
        id="arm-1",
        name="Treatment Arm Y",
        type=usdm_model.Code(
            id="code-arm-type",
            code="TREATMENT",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Treatment Arm",
            instanceType="Code",
        ),
        dataOriginDescription="Sponsor-defined",
        dataOriginType=usdm_model.Code(
            id="code-origin-type",
            code="SPONSOR",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Sponsor",
            instanceType="Code",
        ),
        instanceType="StudyArm",
    )
    cell = usdm_model.StudyCell(
        id="cell-1",
        armId="arm-1",
        epochId="epoch-tx",
        elementIds=["elem-1"],
        instanceType="StudyCell",
    )

    # Study Design
    study_design = usdm_model.InterventionalStudyDesign(
        id=design_id,
        name="Interventional Design",
        studyPhase=phase_alias,
        studyType=study_type_code,
        epochs=[epoch1],
        encounters=[encounter1, encounter2],
        activities=[activity1, activity2],
        arms=[arm],
        studyCells=[cell],
        rationale="Efficacy validation.",
        population=population,
        objectives=[objective1],
        scheduleTimelines=[timeline],
        model=usdm_model.Code(
            id="code-model",
            code="PARALLEL",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Parallel",
            instanceType="Code",
        ),
        instanceType="InterventionalStudyDesign",
    )

    # Study Version
    study_version = usdm_model.StudyVersion(
        id=version_id,
        versionIdentifier="1.0",
        rationale="Initial Version",
        titles=[study_title],
        studyIdentifiers=[study_ident],
        organizations=[sponsor_org],
        studyDesigns=[study_design],
        narrativeContentItems=[item1, item2],
        studyInterventions=[intervention1],
        instanceType="StudyVersion",
    )

    # Document Section Nodes (NarrativeContent)
    sec_intro = usdm_model.NarrativeContent(
        id="sec-intro",
        name="Introduction Section",
        sectionNumber="1.0",
        sectionTitle="Introduction Section Title",
        displaySectionNumber=True,
        displaySectionTitle=True,
        childIds=["narr-item-1", "sec-bg"],
        instanceType="NarrativeContent",
    )
    sec_bg = usdm_model.NarrativeContent(
        id="sec-bg",
        name="Background Subsection",
        sectionNumber="1.1",
        sectionTitle="Background Subsection Title",
        displaySectionNumber=True,
        displaySectionTitle=True,
        childIds=["narr-item-2"],
        instanceType="NarrativeContent",
    )

    doc_version = usdm_model.StudyDefinitionDocumentVersion(
        id="doc-ver-1",
        version="1",
        status=usdm_model.Code(
            id="code-status",
            code="APPROVED",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Approved",
            instanceType="Code",
        ),
        contents=[sec_intro, sec_bg],
        instanceType="StudyDefinitionDocumentVersion",
    )

    def_doc = usdm_model.StudyDefinitionDocument(
        id="doc-1",
        name="Study Protocol Document",
        templateName="Standard TransCelerate Template",
        language=usdm_model.Code(
            id="code-lang",
            code="en",
            codeSystem="ISO",
            codeSystemVersion="639",
            decode="English",
            instanceType="Code",
        ),
        type=usdm_model.Code(
            id="code-doc-type",
            code="PROTOCOL",
            codeSystem="USDM",
            codeSystemVersion="1.0",
            decode="Protocol",
            instanceType="Code",
        ),
        versions=[doc_version],
        instanceType="StudyDefinitionDocument",
    )

    # Parent Study
    return usdm_model.Study(
        id=study_id,
        name="Compound Y Study",
        versions=[study_version],
        documentedBy=[def_doc],
        instanceType="Study",
    )


def test_successful_assembly_and_synopsis(base_study):
    """
    Test that the complete Study object can be assembled into RenderedProtocolDocument,
    and that SynopsisView yields correct expected mappings.
    """
    doc = assemble_rendered_protocol_document(
        study=base_study,
        creator="fderuiter",
        change_reason="Release v1.0",
        version_index=2,
    )

    assert isinstance(doc, RenderedProtocolDocument)
    assert doc.metadata.creator == "fderuiter"
    assert doc.metadata.version_index == 2
    assert doc.metadata.change_reason == "Release v1.0"

    syn = doc.synopsis
    assert syn.study_id == str(base_study.id)
    assert syn.protocol_title == "A Phase II Study of Compound Y in Disease X"
    assert syn.protocol_number == "PROT-Y-202"
    assert syn.sponsor_name == "PharmaY Corp"
    assert syn.phase == "Phase II"
    assert "To evaluate the efficacy of Compound Y." in syn.objectives
    assert syn.study_design_type == "Interventional"
    assert syn.population == "Adult subjects with Disease X."
    assert syn.sample_size == 150
    assert syn.duration == "12.0 Months"
    assert "Compound Y 50mg" in syn.interventions


def test_narrative_assembly_and_ref_resolution(base_study):
    """
    Test that Narrative sections and items are assembled in deterministic hierarchy,
    and that <usdm:ref> resolves accurately to referenced entity attributes.
    """
    sections = assemble_narrative_sections(base_study)

    # 1. Root Section (Introduction)
    assert len(sections) == 1
    root = sections[0]
    assert root.section_id == "sec-intro"
    assert root.section_number == "1.0"
    assert root.title == "Introduction Section Title"
    assert root.order == 1

    # Items in Root
    assert len(root.items) == 1
    item1 = root.items[0]
    assert item1.id == "narr-item-1"
    # Resolved sponsor name inside <usdm:ref>
    assert "sponsor name: PharmaY Corp." in item1.text
    assert item1.order == 1

    # Subsections inside Root (Background)
    assert len(root.subsections) == 1
    sub = root.subsections[0]
    assert sub.section_id == "sec-bg"
    assert sub.section_number == "1.1"
    assert sub.title == "Background Subsection Title"
    assert sub.order == 2

    assert len(sub.items) == 1
    item2 = sub.items[0]
    assert item2.id == "narr-item-2"
    assert item2.text == "Background detail text."
    assert item2.order == 1


def test_narrative_display_rule_missing_section_number(base_study):
    """
    Test that configured displaySectionNumber=True raises an explicit
    ValueError if sectionNumber is missing or blank.
    """
    # Mutate section in-place to violate rule
    base_study.documentedBy[0].versions[0].contents[0].sectionNumber = "  "

    with pytest.raises(ValueError) as exc:
        assemble_narrative_sections(base_study)
    assert (
        "is configured to display its section number, but 'sectionNumber' is missing or empty"
        in str(exc.value)
    )


def test_narrative_display_rule_duplicate_section_numbers(base_study):
    """
    Test that duplicate displayed section numbers fail explicitly.
    """
    # Introduce duplicate displayed section numbers
    base_study.documentedBy[0].versions[0].contents[1].sectionNumber = "1.0"

    with pytest.raises(ValueError) as exc:
        assemble_narrative_sections(base_study)
    assert "Duplicate displayed section number" in str(exc.value)


def test_unresolved_reference_non_existent_id(base_study):
    """
    Test that a reference pointing to a non-existent ID fails with ValueError.
    """
    # Replace valid text with invalid reference
    base_study.versions[0].narrativeContentItems[
        0
    ].text = "Some invalid text with <usdm:ref klass='Organization' id='invalid-id' attribute='name'></usdm:ref>."

    with pytest.raises(ValueError) as exc:
        assemble_narrative_sections(base_study)
    assert "Unresolved USDM reference" in str(exc.value)


def test_unresolved_reference_invalid_attribute(base_study):
    """
    Test that a reference pointing to a non-existent attribute raises ValueError.
    """
    base_study.versions[0].narrativeContentItems[
        0
    ].text = "Invalid attribute: <usdm:ref klass='Organization' id='org-sponsor' attribute='invalid_attr'></usdm:ref>."

    with pytest.raises(ValueError) as exc:
        assemble_narrative_sections(base_study)
    assert "does not have the attribute 'invalid_attr'" in str(exc.value)


def test_soa_matrix_assembly(base_study):
    """
    Test that SoAMatrixView resolves headers, encounters, rows,
    and cell applicability/details correctly.
    """
    matrix = assemble_soa_matrix(base_study)

    assert len(matrix.epochs) == 1
    assert matrix.epochs[0].epoch_id == "epoch-tx"
    assert matrix.epochs[0].epoch_name == "Treatment Epoch"

    assert len(matrix.encounters) == 2
    assert matrix.encounters[0].encounter_id == "enc-v1"
    assert matrix.encounters[0].epoch_id == "epoch-tx"
    assert matrix.encounters[1].encounter_id == "enc-v2"
    assert matrix.encounters[1].epoch_id == "epoch-tx"

    assert len(matrix.rows) == 2
    # Activity 1: Vital Signs (applicable at v1 and v2)
    row_vitals = next(r for r in matrix.rows if r.activity_id == "act-vitals")
    assert row_vitals.activity_name == "Vital Signs Collection"
    assert len(row_vitals.cells) == 2

    cell_v1 = row_vitals.cells[0]
    assert cell_v1.encounter_id == "enc-v1"
    assert cell_v1.is_applicable is True
    assert cell_v1.details == "Standard vitals collection."

    cell_v2 = row_vitals.cells[1]
    assert cell_v2.encounter_id == "enc-v2"
    assert cell_v2.is_applicable is True
    assert cell_v2.details == "Vitals and lab blood draw."

    # Activity 2: Blood Draw (applicable ONLY at v2)
    row_blood = next(r for r in matrix.rows if r.activity_id == "act-blood")
    assert row_blood.activity_name == "Blood Draw"
    assert len(row_blood.cells) == 2

    assert row_blood.cells[0].encounter_id == "enc-v1"
    assert row_blood.cells[0].is_applicable is False

    assert row_blood.cells[1].encounter_id == "enc-v2"
    assert row_blood.cells[1].is_applicable is True
    assert row_blood.cells[1].details == "Vitals and lab blood draw."


def test_render_synopsis_template_html(base_study):
    """Validate Jinja2 HTML synopsis rendering from USDM study.

    Requirements: PRD-SYS-001
    """
    from apps.designer.content_assembly import (
        USDMSynopsisAssembler,
        assemble_rendered_protocol_document,
        render_synopsis_template_html,
    )

    rendered_doc = assemble_rendered_protocol_document(
        base_study, creator="Dr. Test User", change_reason="Initial Baseline Setup"
    )
    html_output = render_synopsis_template_html(rendered_doc)

    assert "<!DOCTYPE html>" in html_output
    assert "Clinical Protocol Synopsis" in html_output
    assert "Dr. Test User" in html_output
    assert "Initial Baseline Setup" in html_output
    assert "Schedule of Activities" in html_output

    assembler = USDMSynopsisAssembler()
    html_output_2 = assembler.assemble_and_render_html(base_study)
    assert "<!DOCTYPE html>" in html_output_2
