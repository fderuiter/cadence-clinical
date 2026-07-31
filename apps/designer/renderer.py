"""
Clinical study protocol document rendering pipeline (PDF + DOCX).
Converts the structured presentation-centric content assembly model into
publication-quality documents using Jinja2 HTML/CSS templates (for WeasyPrint)
and docxtpl Word templates.
"""

import asyncio
import os
from concurrent.futures import ThreadPoolExecutor

from jinja2 import Environment, FileSystemLoader, select_autoescape
from protocol_render import RenderedProtocolDocument

from apps.designer.rendering import (
    RendererResult,
    TemplateRenderingError,
    build_soa_subdoc,
    get_safe_filename,
    load_docx_template,
)

# Setup Jinja2 Environment exactly as translator.py does
TEMPLATE_DIR = os.path.join(os.path.dirname(__file__), "templates")
env = Environment(
    loader=FileSystemLoader(TEMPLATE_DIR),
    autoescape=select_autoescape(default_for_string=True, default=True),
)

# Module-level thread pool executor to offload CPU-bound rendering work
_thread_pool = ThreadPoolExecutor()


def render_protocol_to_html(
    doc: RenderedProtocolDocument, output: str = "combined"
) -> str:
    """
    Renders the RenderedProtocolDocument to an HTML string using the local Jinja2 Environment.
    """
    template = env.get_template("protocol_template.html")
    return template.render(
        metadata=doc.metadata,
        synopsis=doc.synopsis,
        narrative_sections=doc.narrative_sections,
        soa_matrix=doc.soa_matrix,
        output=output,
    )


def render_protocol_to_pdf(
    doc: RenderedProtocolDocument, output: str = "combined"
) -> RendererResult:
    """
    Renders the RenderedProtocolDocument to a PDF byte stream using WeasyPrint.
    Falls back to a structural minimal PDF stream if WeasyPrint is unavailable.
    """
    filename = get_safe_filename(
        doc.synopsis.study_id, doc.metadata.version_index, "pdf"
    )
    try:
        from weasyprint import HTML

        html_content = render_protocol_to_html(doc, output)
        # Generate PDF bytes via WeasyPrint
        pdf_bytes = HTML(string=html_content).write_pdf()
    except (ImportError, OSError) as err:
        import logging

        logging.warning(
            f"[WeasyPrint Fallback] Native PDF renderer unavailable ({err})."
        )
        # Fallback minimal structural PDF for headless/lightweight environments
        pdf_bytes = (
            b"%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj "
            b"2 0 obj<</Type/Pages/Count 1/Kids[3 0 R]>>endobj "
            b"3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Parent 2 0 R/Resources<<>>>>endobj\n"
            b"xref\n0 4\n0000000000 65535 f\n0000000009 00000 n\n0000000052 00000 n\n"  # deid: ignore
            b"0000000101 00000 n\ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n178\n%%EOF\n"  # deid: ignore
        )
    except Exception as err:
        raise TemplateRenderingError(f"WeasyPrint PDF rendering failed: {err}")

    return RendererResult(
        content=pdf_bytes,
        filename=filename,
        media_type="application/pdf",
    )


def render_protocol_to_docx(
    doc: RenderedProtocolDocument, output: str = "combined"
) -> RendererResult:
    """
    Renders the RenderedProtocolDocument to a DOCX byte stream using docxtpl.
    """
    import io

    try:
        tpl = load_docx_template()

        # Build SubDoc for SoA
        subdoc = tpl.new_subdoc()
        if output == "combined" or output == "soa":
            build_soa_subdoc(subdoc, doc.soa_matrix)
        else:
            subdoc.add_paragraph("Schedule of Activities (SoA) omitted from this view.")

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
            "output": output,
        }

        tpl.render(context)
        bio = io.BytesIO()
        tpl.save(bio)
        docx_bytes = bio.getvalue()
    except Exception as err:
        raise TemplateRenderingError(f"Docxtpl Word rendering failed: {err}")

    filename = get_safe_filename(
        doc.synopsis.study_id, doc.metadata.version_index, "docx"
    )
    return RendererResult(
        content=docx_bytes,
        filename=filename,
        media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    )


async def render_protocol_to_pdf_async(
    doc: RenderedProtocolDocument, output: str = "combined"
) -> RendererResult:
    """
    Asynchronously renders the RenderedProtocolDocument to a PDF byte stream
    using a thread pool executor to offload CPU-bound rendering work.
    """
    loop = asyncio.get_running_loop()
    return await loop.run_in_executor(_thread_pool, render_protocol_to_pdf, doc, output)


async def render_protocol_to_docx_async(
    doc: RenderedProtocolDocument, output: str = "combined"
) -> RendererResult:
    """
    Asynchronously renders the RenderedProtocolDocument to a DOCX byte stream
    using a thread pool executor to offload CPU-bound rendering work.
    """
    loop = asyncio.get_running_loop()
    return await loop.run_in_executor(
        _thread_pool, render_protocol_to_docx, doc, output
    )
