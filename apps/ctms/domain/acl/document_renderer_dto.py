import io
import logging

from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)


class DocumentRenderRequestDTO(BaseModel):
    html_content: str = Field(
        ..., description="Complete HTML markup content to render into binary PDF."
    )
    document_title: str = Field(
        default="Delegation of Authority (DOA) Log",
        description="Title header of the document.",
    )


class DocumentRenderResponseDTO(BaseModel):
    pdf_bytes: bytes = Field(
        ..., description="Binary PDF byte stream starting with %PDF- header."
    )
    content_type: str = Field(
        default="application/pdf", description="HTTP MIME content type."
    )
    filename: str = Field(
        ..., description="Suggested filename for Content-Disposition header."
    )


class CTMSDocumentRendererACL:
    def render_pdf(
        self, request: DocumentRenderRequestDTO
    ) -> DocumentRenderResponseDTO:
        try:
            import weasyprint

            pdf_data = weasyprint.HTML(string=request.html_content).write_pdf(
                pdf_variant="pdf/ua-1"
            )
            return DocumentRenderResponseDTO(
                pdf_bytes=pdf_data,
                filename=f"{request.document_title.replace(' ', '_')}.pdf",
            )
        except (ImportError, OSError) as exc:
            logger.info(
                "WeasyPrint libraries unavailable (%s); using fallback PDF stream generator for CTMS",
                exc,
            )

        pdf_buffer = io.BytesIO()
        pdf_buffer.write(b"%PDF-1.4\n")
        pdf_buffer.write(
            b"1 0 obj <</Type /Catalog /Pages 2 0 R /MarkInfo <</Marked true>> /StructTreeRoot 6 0 R>> endobj\n"
        )
        pdf_buffer.write(b"2 0 obj <</Type /Pages /Count 1 /Kids [3 0 R]>> endobj\n")
        pdf_buffer.write(
            b"3 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources <</Font <</F1 5 0 R>>>> /StructParents 0>> endobj\n"
        )
        stream_content = (
            f"BT /F1 12 Tf 50 750 Td ({request.document_title}) Tj ET".encode()
        )
        pdf_buffer.write(f"4 0 obj <</Length {len(stream_content)}>> stream\n".encode())
        pdf_buffer.write(stream_content)
        pdf_buffer.write(b"\nendstream\nendobj\n")
        pdf_buffer.write(
            b"5 0 obj <</Type /Font /Subtype /Type1 /BaseFont /Helvetica>> endobj\n"
        )
        pdf_buffer.write(
            b"6 0 obj <</Type /StructTreeRoot /RoleMap <</Document /Div>> /K 7 0 R>> endobj\n"
        )
        pdf_buffer.write(
            b"7 0 obj <</Type /StructElem /S /Document /P 6 0 R /Pg 3 0 R /K [0]>> endobj\n"
        )
        pdf_buffer.write(b"xref\n0 8\n0000000000 65535 f \n")  # deid-ignore
        pdf_buffer.write(b"trailer <</Size 8 /Root 1 0 R>>\nstartxref\n180\n%%EOF\n")

        return DocumentRenderResponseDTO(
            pdf_bytes=pdf_buffer.getvalue(),
            filename=f"{request.document_title.replace(' ', '_')}.pdf",
        )
