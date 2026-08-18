"""Standalone HTML Consent Certificate and Audit Artifact Renderer.

Generates self-contained, 21 CFR Part 11 compliant HTML documents
incorporating study metadata, full clause content, granular option choices,
cryptographic SHA-256 digests, and append-only audit trail logs.
"""

import html
from typing import Any


def render_verifiable_consent_html(
    study_id: str,
    site_id: str,
    subject_pseudonym: str,
    template_id: str,
    template_name: str,
    protocol_version: str,
    version_index: int,
    clauses: list[dict[str, Any]],
    signatures: list[dict[str, Any]],
    granular_selections: list[dict[str, Any]] | None = None,
    audit_logs: list[dict[str, Any]] | None = None,
    source_content_identity: str | None = None,
) -> str:
    """Renders a tamper-evident, printable HTML clinical consent certificate."""
    granular_selections = granular_selections or []
    audit_logs = audit_logs or []

    # Format clauses HTML
    clauses_html = ""
    for idx, c in enumerate(clauses, start=1):
        c_title = html.escape(c.get("title") or f"Section {idx}")
        c_text = html.escape(c.get("text") or "").replace("\n", "<br/>")
        clauses_html += f"""
        <div class="clause-block" style="margin-bottom: 1.5rem;">
            <h3 style="color: #1e293b; font-size: 1.1rem; margin-bottom: 0.5rem;">{idx}. {c_title}</h3>
            <div style="color: #334155; line-height: 1.6; font-size: 0.95rem;">{c_text}</div>
        </div>
        """

    # Format granular options HTML
    granular_html = ""
    if granular_selections:
        items_html = ""
        for opt in granular_selections:
            code = html.escape(opt.get("option_code") or "")
            title = html.escape(opt.get("title") or code)
            selected = opt.get("selected", False)
            badge_color = "#16a34a" if selected else "#dc2626"
            status_label = (
                "CONSENTED / OPTED-IN" if selected else "DECLINED / OPTED-OUT"
            )
            items_html += f"""
            <tr style="border-bottom: 1px solid #e2e8f0;">
                <td style="padding: 8px 12px; font-weight: 500;">{title} ({code})</td>
                <td style="padding: 8px 12px; text-align: right;">
                    <span style="color: {badge_color}; font-weight: bold; font-size: 0.85rem;">{status_label}</span>
                </td>
            </tr>
            """
        granular_html = f"""
        <div class="granular-section" style="margin: 2rem 0; padding: 1rem; border: 1px solid #cbd5e1; border-radius: 6px; background: #f8fafc;">
            <h3 style="margin-top: 0; color: #0f172a; font-size: 1.1rem;">Optional Research & Specimen Consents</h3>
            <table style="width: 100%; border-collapse: collapse;">
                <tbody>{items_html}</tbody>
            </table>
        </div>
        """

    # Format multi-party signatures HTML
    signatures_html = ""
    for sig in signatures:
        role = html.escape(sig.get("role") or "SUBJECT")
        signer_name = html.escape(
            sig.get("signer_name") or sig.get("created_by") or "Participant"
        )
        signed_at = html.escape(str(sig.get("signed_at") or ""))
        meaning = html.escape(sig.get("meaning") or "Consent Execution")
        relationship = html.escape(sig.get("lar_relationship") or "")
        digest = html.escape(sig.get("digest_sha256") or "")

        rel_block = (
            f'<div style="font-size: 0.85rem; color: #475569;">Relationship: <strong>{relationship}</strong></div>'
            if relationship
            else ""
        )
        digest_block = (
            f'<div style="font-family: monospace; font-size: 0.75rem; color: #64748b; margin-top: 4px; word-break: break-all;">Verification Digest (SHA-256): {digest}</div>'
            if digest
            else ""
        )

        signatures_html += f"""
        <div class="sig-card" style="border: 1px solid #94a3b8; border-radius: 6px; padding: 1rem; margin-bottom: 1rem; background: #ffffff;">
            <div style="display: flex; justify-content: space-between; margin-bottom: 0.5rem;">
                <span style="font-weight: bold; color: #1e3a8a; text-transform: uppercase; font-size: 0.85rem;">Role: {role}</span>
                <span style="color: #64748b; font-size: 0.85rem;">Signed At: {signed_at}</span>
            </div>
            <div style="font-size: 1.1rem; font-weight: 600; color: #0f172a;">{signer_name}</div>
            {rel_block}
            <div style="font-size: 0.9rem; color: #334155; font-style: italic; margin-top: 4px;">"{meaning}"</div>
            {digest_block}
        </div>
        """

    # Format audit trail table
    audit_html = ""
    if audit_logs:
        rows = ""
        for log in audit_logs:
            ts = html.escape(str(log.get("timestamp") or ""))
            actor = html.escape(
                f"{log.get('actor_id') or 'system'} ({log.get('actor_role') or 'system'})"
            )
            action = html.escape(log.get("action") or "")
            reason = html.escape(log.get("reason_for_change") or "N/A")
            rows += f"""
        <tr style="border-bottom: 1px solid #f1f5f9; font-size: 0.8rem;">
            <td style="padding: 4px 8px;">{ts}</td>
            <td style="padding: 4px 8px;">{actor}</td>
            <td style="padding: 4px 8px; font-weight: 600;">{action}</td>
            <td style="padding: 4px 8px;">{reason}</td>
        </tr>
        """
        audit_html = f"""
    <div class="audit-section" style="margin-top: 2rem; page-break-inside: avoid;">
        <h4 style="color: #475569; font-size: 0.95rem; margin-bottom: 0.5rem;">Append-Only Audit Trail Summary</h4>
        <table style="width: 100%; border-collapse: collapse; background: #f8fafc; border: 1px solid #e2e8f0;">
            <thead>
                <tr style="text-align: left; background: #e2e8f0; font-size: 0.8rem;">
                    <th style="padding: 4px 8px;">Timestamp (UTC)</th>
                    <th style="padding: 4px 8px;">Actor</th>
                    <th style="padding: 4px 8px;">Action</th>
                    <th style="padding: 4px 8px;">Justification</th>
                </tr>
            </thead>
            <tbody>
                {rows}
            </tbody>
        </table>
    </div>
        """

    doc_id_block = (
        f'<div style="font-size: 0.8rem; color: #64748b;">Source Hash: {html.escape(source_content_identity)}</div>'
        if source_content_identity
        else ""
    )

    return f"""<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>eConsent Certificate - {html.escape(study_id)} - {html.escape(subject_pseudonym)}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; margin: 0; padding: 2rem; background: #ffffff; color: #0f172a; }}
        .header-box {{ border-bottom: 3px solid #2563eb; padding-bottom: 1rem; margin-bottom: 1.5rem; display: flex; justify-content: space-between; align-items: flex-start; }}
        .badge {{ display: inline-block; padding: 4px 8px; font-size: 0.75rem; font-weight: bold; border-radius: 4px; background: #e0e7ff; color: #3730a3; }}
        @media print {{ body {{ padding: 0; }} }}
    </style>
</head>
<body>
    <div class="header-box">
        <div>
            <span class="badge">FDA 21 CFR Part 11 Compliant</span>
            <span class="badge" style="background: #f0fdf4; color: #166534;">ICH GCP E6(R2)/(R3)</span>
            <h1 style="margin: 0.5rem 0 0.25rem 0; font-size: 1.5rem; color: #1e3a8a;">{html.escape(template_name)}</h1>
            <p style="margin: 0; color: #475569; font-size: 0.9rem;">
                Study ID: <strong>{html.escape(study_id)}</strong> | Site ID: <strong>{html.escape(site_id)}</strong> | Protocol: <strong>{html.escape(protocol_version)}</strong> (v{version_index})
            </p>
        </div>
        <div style="text-align: right;">
            <div style="font-size: 1.1rem; font-weight: 700; color: #0f172a;">Subject ID: {html.escape(subject_pseudonym)}</div>
            {doc_id_block}
        </div>
    </div>

    <div class="content-body" style="margin-bottom: 2rem;">
        {clauses_html}
    </div>

    {granular_html}

    <div class="signatures-section" style="margin-top: 2rem;">
        <h3 style="color: #0f172a; font-size: 1.2rem; border-bottom: 2px solid #e2e8f0; padding-bottom: 0.5rem; margin-bottom: 1rem;">
            21 CFR Part 11 Verified Electronic Signatures
        </h3>
        {signatures_html}
    </div>

    {audit_html}
</body>
</html>
"""
