"""GxP 21 CFR Part 11 Training Certificate PDF Generator.

Requirements: PRD-TRN-003, PRD-TRN-004
"""

from __future__ import annotations

import base64
from datetime import datetime
from io import BytesIO
from typing import Any

try:
    from reportlab.lib import colors
    from reportlab.lib.pagesizes import letter
    from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
    from reportlab.platypus import (
        Paragraph,
        SimpleDocTemplate,
        Spacer,
        Table,
        TableStyle,
    )

    HAS_REPORTLAB = True
except ImportError:
    HAS_REPORTLAB = False


def generate_training_certificate_pdf(
    certificate_id: str,
    participant_name: str,
    participant_id: str,
    participant_role: str,
    study_id: str,
    site_id: str,
    protocol_version: str,
    curriculum_title: str,
    quiz_score: float,
    time_spent_seconds: int,
    completed_at: datetime,
    sha256_seal: str,
    signature_manifestation: dict[str, Any],
) -> bytes:
    """Generate a formal 21 CFR Part 11 compliant Training Certificate in PDF format."""
    score_pct = int(quiz_score * 100) if quiz_score <= 1.0 else int(quiz_score)
    completed_iso = completed_at.strftime("%Y-%m-%d %H:%M:%S UTC")

    if HAS_REPORTLAB:
        buffer = BytesIO()
        doc = SimpleDocTemplate(
            buffer,
            pagesize=letter,
            rightMargin=36,
            leftMargin=36,
            topMargin=36,
            bottomMargin=36,
        )
        styles = getSampleStyleSheet()

        title_style = ParagraphStyle(
            "CertTitle",
            parent=styles["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=20,
            leading=24,
            alignment=1,  # Centered
            textColor=colors.HexColor("#0f172a"),
        )
        subtitle_style = ParagraphStyle(
            "CertSubtitle",
            parent=styles["Normal"],
            fontName="Helvetica",
            fontSize=11,
            leading=15,
            alignment=1,
            textColor=colors.HexColor("#475569"),
        )
        body_style = ParagraphStyle(
            "CertBody",
            parent=styles["Normal"],
            fontName="Helvetica",
            fontSize=10,
            leading=14,
            textColor=colors.HexColor("#1e293b"),
        )
        bold_label = ParagraphStyle(
            "BoldLabel",
            parent=styles["Normal"],
            fontName="Helvetica-Bold",
            fontSize=10,
            leading=14,
            textColor=colors.HexColor("#0f172a"),
        )
        legal_style = ParagraphStyle(
            "LegalText",
            parent=styles["Normal"],
            fontName="Helvetica-Oblique",
            fontSize=8,
            leading=11,
            alignment=1,
            textColor=colors.HexColor("#64748b"),
        )

        elements = []

        # Header Banner
        elements.append(Paragraph("CADENCE CLINICAL RESEARCH PLATFORM", subtitle_style))
        elements.append(Spacer(1, 8))
        elements.append(Paragraph("CERTIFICATE OF PROTOCOL QUALIFICATION", title_style))
        elements.append(Spacer(1, 4))
        elements.append(
            Paragraph(
                "Compliant with ICH GCP E6(R2)/(R3) &amp; 21 CFR Part 11 Electronic Records",
                subtitle_style,
            )
        )
        elements.append(Spacer(1, 16))

        # Recipient Statement
        elements.append(
            Paragraph(
                f"This certifies that <b>{participant_name}</b> (ID: <code>{participant_id}</code>), acting in the role of <b>{participant_role}</b>, has successfully completed the required mandatory training curriculum:",
                body_style,
            )
        )
        elements.append(Spacer(1, 10))

        # Curriculum Box Table
        curriculum_data = [
            [
                Paragraph("<b>Curriculum / Module:</b>", bold_label),
                Paragraph(curriculum_title, body_style),
            ],
            [
                Paragraph("<b>Study Identifier:</b>", bold_label),
                Paragraph(f"{study_id} (Site: {site_id})", body_style),
            ],
            [
                Paragraph("<b>Protocol Version:</b>", bold_label),
                Paragraph(f"Version {protocol_version}", body_style),
            ],
            [
                Paragraph("<b>Comprehension Score:</b>", bold_label),
                Paragraph(f"{score_pct}% (Passing Score: &ge; 80%)", body_style),
            ],
            [
                Paragraph("<b>Training Duration:</b>", bold_label),
                Paragraph(
                    f"{time_spent_seconds} seconds recorded time-on-page", body_style
                ),
            ],
            [
                Paragraph("<b>Completion Timestamp:</b>", bold_label),
                Paragraph(completed_iso, body_style),
            ],
        ]

        t = Table(curriculum_data, colWidths=[160, 380])
        t.setStyle(
            TableStyle(
                [
                    ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#f8fafc")),
                    ("BOX", (0, 0), (-1, -1), 1, colors.HexColor("#cbd5e1")),
                    ("INNERGRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#e2e8f0")),
                    ("TOPPADDING", (0, 0), (-1, -1), 6),
                    ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
                    ("LEFTPADDING", (0, 0), (-1, -1), 10),
                    ("RIGHTPADDING", (0, 0), (-1, -1), 10),
                ]
            )
        )
        elements.append(t)
        elements.append(Spacer(1, 16))

        # Electronic Signature Section
        sig_data = [
            [
                Paragraph(
                    "<b>21 CFR Part 11 Digital Signature Manifestation</b>", bold_label
                ),
                Paragraph("", body_style),
            ],
            [
                Paragraph("<b>Signer:</b>", bold_label),
                Paragraph(
                    f"{signature_manifestation.get('signer_name', participant_name)} ({signature_manifestation.get('signer_id', participant_id)})",
                    body_style,
                ),
            ],
            [
                Paragraph("<b>Meaning:</b>", bold_label),
                Paragraph(
                    str(
                        signature_manifestation.get(
                            "meaning",
                            "Training comprehension and compliance certified.",
                        )
                    ),
                    body_style,
                ),
            ],
            [
                Paragraph("<b>Signature Hash:</b>", bold_label),
                Paragraph(
                    f"<code>{signature_manifestation.get('signature_hash', '')[:32]}...</code>",
                    body_style,
                ),
            ],
            [
                Paragraph("<b>SHA-256 Cryptographic Seal:</b>", bold_label),
                Paragraph(f"<code>{sha256_seal}</code>", body_style),
            ],
            [
                Paragraph("<b>Certificate ID:</b>", bold_label),
                Paragraph(f"<code>{certificate_id}</code>", body_style),
            ],
        ]

        sig_table = Table(sig_data, colWidths=[160, 380])
        sig_table.setStyle(
            TableStyle(
                [
                    ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#f1f5f9")),
                    ("BOX", (0, 0), (-1, -1), 1, colors.HexColor("#94a3b8")),
                    ("INNERGRID", (0, 0), (-1, -1), 0.5, colors.HexColor("#cbd5e1")),
                    ("TOPPADDING", (0, 0), (-1, -1), 5),
                    ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
                    ("LEFTPADDING", (0, 0), (-1, -1), 10),
                    ("RIGHTPADDING", (0, 0), (-1, -1), 10),
                ]
            )
        )
        elements.append(sig_table)
        elements.append(Spacer(1, 14))

        # Regulatory Footer
        elements.append(
            Paragraph(
                "This document is an electronically signed GxP record archived automatically under DIA TMF Reference Model Zone 5 (Artifact 05.02.04 Site Staff Training Records) and site eISF binders. Any unauthorized alteration renders this certificate void.",
                legal_style,
            )
        )

        doc.build(elements)
        return buffer.getvalue()

    # Fallback valid PDF byte stream
    header = "%PDF-1.4\n"
    body_text = (
        f"CADENCE CLINICAL RESEARCH PLATFORM - CERTIFICATE OF PROTOCOL QUALIFICATION\n"
        f"Certificate ID: {certificate_id}\n"
        f"Participant: {participant_name} ({participant_id}) - Role: {participant_role}\n"
        f"Study ID: {study_id} | Site ID: {site_id} | Protocol Version: {protocol_version}\n"
        f"Curriculum: {curriculum_title}\n"
        f"Comprehension Quiz Score: {score_pct}%\n"
        f"Recorded Duration: {time_spent_seconds} seconds\n"
        f"Completed: {completed_iso}\n"
        f"Part 11 Signature Hash: {signature_manifestation.get('signature_hash', '')}\n"
        f"Cryptographic SHA-256 Seal: {sha256_seal}\n"
        f"Routed to eTMF Zone 5 (05.02.04) and Site eISF Binder.\n"
    )
    content_stream = f"BT /F1 10 Tf 50 720 Td ({body_text}) Tj ET"
    stream_len = len(content_stream)
    objects = (
        "1 0 obj <</Type /Catalog /Pages 2 0 R /MarkInfo <</Marked true>> /StructTreeRoot 6 0 R>> endobj\n"
        "2 0 obj <</Type /Pages /Kids [3 0 R] /Count 1>> endobj\n"
        "3 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources <</Font <</F1 5 0 R>>>> /StructParents 0>> endobj\n"
        f"4 0 obj <</Length {stream_len}>> stream\n{content_stream}\nendstream\nendobj\n"
        "5 0 obj <</Type /Font /Subtype /Type1 /BaseFont /Helvetica>> endobj\n"
        "6 0 obj <</Type /StructTreeRoot /RoleMap <</Document /Div>> /K 7 0 R>> endobj\n"
        "7 0 obj <</Type /StructElem /S /Document /P 6 0 R /Pg 3 0 R /K [0]>> endobj\n"
    )
    xref = (
        "xref\n0 8\n0000000000 65535 f \n"
        "0000000009 00000 n \n0000000098 00000 n \n0000000155 00000 n \n"
        "0000000302 00000 n \n0000000406 00000 n \n0000000486 00000 n \n"
        "0000000556 00000 n \ntrailer <</Size 8 /Root 1 0 R>>\nstartxref\n636\n%%EOF"
    )
    return f"{header}{objects}{xref}".encode("latin1", errors="replace")


def encode_pdf_to_base64(pdf_bytes: bytes) -> str:
    """Encode binary PDF bytes into a base64 string."""
    return base64.b64encode(pdf_bytes).decode("utf-8")
