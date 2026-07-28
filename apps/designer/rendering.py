import io
import os
import re
from datetime import datetime, timezone
from typing import Any, Dict

from docx import Document
from docxtpl import DocxTemplate
from jinja2 import Environment, FileSystemLoader, select_autoescape
from weasyprint import HTML

from protocol_render import RenderedProtocolDocument, SoAMatrixView

# Initialize Jinja2 environment
TEMPLATES_DIR = os.path.join(os.path.dirname(__file__), "templates")
os.makedirs(TEMPLATES_DIR, exist_ok=True)

jinja_env = Environment(
    loader=FileSystemLoader(TEMPLATES_DIR),
    autoescape=select_autoescape(["html", "xml", "j2"]),
)


class RendererResult:
    """
    Abstractions containing the rendered document bytes, suggested safe filename,
    and the exact MIME/media type.
    """
    def __init__(self, content: bytes, filename: str, media_type: str):
        self.content = content
        self.filename = filename
        self.media_type = media_type


def sanitize_filename(name: str) -> str:
    """
    Sanitizes string inputs to create secure, deterministic filenames.
    """
    # Keep only alphanumeric characters, underscores, dashes, and periods.
    safe = re.sub(r"[^a-zA-Z0-9_\-\.]", "_", name)
    # Collapse multiple underscores
    safe = re.sub(r"_+", "_", safe)
    return safe.strip("_")


def get_safe_filename(study_id: str, version_index: int, extension: str) -> str:
    """
    Generates a safe, deterministic filename based on the study ID, version index, and extension.
    """
    safe_study_id = sanitize_filename(study_id)
    return f"protocol_{safe_study_id}_v{version_index}.{extension.strip('.')}"


def ensure_docx_template_exists() -> str:
    """
    Programmatically creates the base version-controlled .docx template containing
    docxtpl placeholders if it does not already exist.
    """
    template_path = os.path.join(TEMPLATES_DIR, "protocol_template.docx")
    if os.path.exists(template_path):
        return template_path

    doc = Document()

    # Title Page
    doc.add_heading("CLINICAL STUDY PROTOCOL", level=0)
    p_title = doc.add_paragraph()
    p_title.add_run("{{ synopsis.protocol_title }}").bold = True
    p_title.alignment = 1  # Center

    doc.add_paragraph("Sponsor: {{ synopsis.sponsor_name }}")
    doc.add_paragraph("Protocol Number: {{ synopsis.protocol_number }}")
    doc.add_paragraph("Study Phase: {{ synopsis.phase }}")
    doc.add_paragraph("Version: {{ metadata.version_index }}")
    doc.add_paragraph("Author/Creator: {{ metadata.creator }}")

    doc.add_page_break()

    # Synopsis Section
    doc.add_heading("1. PROTOCOL SYNOPSIS", level=1)
    doc.add_paragraph("Protocol Title: {{ synopsis.protocol_title }}")
    doc.add_paragraph("Sponsor: {{ synopsis.sponsor_name }}")
    doc.add_paragraph("Protocol Number: {{ synopsis.protocol_number }}")
    doc.add_paragraph("Phase: {{ synopsis.phase }}")
    doc.add_paragraph("Study Design Type: {{ synopsis.study_design_type }}")

    doc.add_paragraph("Objectives:")
    doc.add_paragraph("{% for obj in synopsis.objectives %}")
    p_obj = doc.add_paragraph(style="List Bullet")
    p_obj.add_run("{{ obj }}")
    doc.add_paragraph("{% endfor %}")

    doc.add_paragraph("Target Population: {{ synopsis.population }}")
    doc.add_paragraph("Sample Size: {{ synopsis.sample_size }}")
    doc.add_paragraph("Duration: {{ synopsis.duration }}")

    doc.add_paragraph("Interventions:")
    doc.add_paragraph("{% for inter in synopsis.interventions %}")
    p_int = doc.add_paragraph(style="List Bullet")
    p_int.add_run("{{ inter }}")
    doc.add_paragraph("{% endfor %}")

    doc.add_page_break()

    # Narrative Sections
    doc.add_heading("2. STUDY NARRATIVE & RATIONALE", level=1)
    doc.add_paragraph("{% for sec in narrative_sections %}")
    doc.add_heading("{{ sec.section_number }} {{ sec.title }}", level=2)
    doc.add_paragraph("{% for item in sec.items %}")
    doc.add_paragraph("{{ item.text }}")
    doc.add_paragraph("{% endfor %}")

    # Subsections level 1
    doc.add_paragraph("{% for sub in sec.subsections %}")
    doc.add_heading("{{ sub.section_number }} {{ sub.title }}", level=3)
    doc.add_paragraph("{% for sub_item in sub.items %}")
    doc.add_paragraph("{{ sub_item.text }}")
    doc.add_paragraph("{% endfor %}")

    # Subsections level 2
    doc.add_paragraph("{% for sub2 in sub.subsections %}")
    doc.add_heading("{{ sub2.section_number }} {{ sub2.title }}", level=4)
    doc.add_paragraph("{% for sub2_item in sub2.items %}")
    doc.add_paragraph("{{ sub2_item.text }}")
    doc.add_paragraph("{% endfor %}")
    doc.add_paragraph("{% endfor %}")  # end sub2 loop

    doc.add_paragraph("{% endfor %}")  # end sub loop
    doc.add_paragraph("{% endfor %}")  # end sec loop

    doc.add_page_break()

    # Schedule of Activities (SoA)
    doc.add_heading("3. SCHEDULE OF ACTIVITIES (SoA)", level=1)
    doc.add_paragraph("{{ soa_subdoc }}")

    doc.save(template_path)
    return template_path


def build_soa_subdoc(subdoc: Any, soa_matrix: SoAMatrixView) -> None:
    """
    Builds the SoA table programmatically in the SubDoc.
    """
    if not soa_matrix or not soa_matrix.encounters:
        subdoc.add_paragraph("No Schedule of Activities (SoA) defined for this study.")
        return

    # Total columns = 1 (Activity name) + number of encounters
    num_cols = 1 + len(soa_matrix.encounters)
    # Total rows = 2 (headers: Epoch and Encounter) + number of activities
    num_rows = 2 + len(soa_matrix.rows)

    table = subdoc.add_table(rows=num_rows, cols=num_cols)
    table.style = "Table Grid"

    # Header Row 1: Activity / Epochs
    hdr_cells = table.rows[0].cells
    hdr_cells[0].text = "Activity / Procedure"

    col_idx = 1
    # We write the Epoch name for each column first
    for enc in soa_matrix.encounters:
        epoch_name = ""
        for ep in soa_matrix.epochs:
            if ep.epoch_id == enc.epoch_id:
                epoch_name = ep.epoch_name
                break
        hdr_cells[col_idx].text = epoch_name
        col_idx += 1

    # Merge adjacent identical Epoch name cells in Header Row 1
    col_idx = 1
    while col_idx < num_cols - 1:
        cell_curr = hdr_cells[col_idx]
        cell_next = hdr_cells[col_idx + 1]
        if cell_curr.text and cell_curr.text == cell_next.text:
            cell_curr.merge(cell_next)
        col_idx += 1

    # Header Row 2: Encounters
    hdr2_cells = table.rows[1].cells
    hdr2_cells[0].text = "Activity / Procedure"
    col_idx = 1
    for enc in soa_matrix.encounters:
        hdr2_cells[col_idx].text = enc.encounter_name
        col_idx += 1

    # Merge vertically the first column for Header Row 1 and Header Row 2
    hdr_cells[0].merge(hdr2_cells[0])

    # Fill in Activity Rows
    row_idx = 2
    for row_view in soa_matrix.rows:
        row_cells = table.rows[row_idx].cells
        row_cells[0].text = row_view.activity_name

        col_idx = 1
        for cell_view in row_view.cells:
            if cell_view.is_applicable:
                text = "X"
                if cell_view.details:
                    text += f"\n({cell_view.details})"
                row_cells[col_idx].text = text
            else:
                row_cells[col_idx].text = "-"
            col_idx += 1
        row_idx += 1


def render_protocol_to_pdf(doc: RenderedProtocolDocument) -> RendererResult:
    """
    Renders the RenderedProtocolDocument to a PDF byte stream using WeasyPrint.
    """
    template = jinja_env.get_template("protocol_template.html")
    # Render HTML template with model context
    html_content = template.render(
        metadata=doc.metadata,
        synopsis=doc.synopsis,
        narrative_sections=doc.narrative_sections,
        soa_matrix=doc.soa_matrix,
    )
    # Generate PDF bytes via WeasyPrint
    pdf_bytes = HTML(string=html_content).write_pdf()
    filename = get_safe_filename(doc.synopsis.study_id, doc.metadata.version_index, "pdf")
    return RendererResult(
        content=pdf_bytes,
        filename=filename,
        media_type="application/pdf",
    )


def render_protocol_to_docx(doc: RenderedProtocolDocument) -> RendererResult:
    """
    Renders the RenderedProtocolDocument to a DOCX byte stream using docxtpl.
    """
    template_path = ensure_docx_template_exists()
    tpl = DocxTemplate(template_path)

    # Build SubDoc for SoA
    subdoc = tpl.new_subdoc()
    build_soa_subdoc(subdoc, doc.soa_matrix)

    # Convert model to dictionary structure
    context = {
        "metadata": {
            "creator": doc.metadata.creator,
            "timestamp": doc.metadata.timestamp.strftime("%Y-%m-%d %H:%M:%S UTC"),
            "change_reason": doc.metadata.change_reason or "",
            "version_index": doc.metadata.version_index,
        },
        "synopsis": {
            "study_id": doc.synopsis.study_id,
            "protocol_title": doc.synopsis.protocol_title,
            "protocol_number": doc.synopsis.protocol_number or "",
            "sponsor_name": doc.synopsis.sponsor_name or "",
            "phase": doc.synopsis.phase or "",
            "objectives": doc.synopsis.objectives,
            "study_design_type": doc.synopsis.study_design_type or "",
            "population": doc.synopsis.population or "",
            "sample_size": doc.synopsis.sample_size or "",
            "duration": doc.synopsis.duration or "",
            "interventions": doc.synopsis.interventions,
        },
        "narrative_sections": doc.narrative_sections,
        "soa_subdoc": subdoc,
    }

    tpl.render(context)
    bio = io.BytesIO()
    tpl.save(bio)
    docx_bytes = bio.getvalue()

    filename = get_safe_filename(doc.synopsis.study_id, doc.metadata.version_index, "docx")
    return RendererResult(
        content=docx_bytes,
        filename=filename,
        media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    )
