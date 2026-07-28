import json
from datetime import datetime, timezone


def apply_watermark(content: str, mime_type: str, user_id: str, user_role: str) -> str:
    """
    Applies a secure, attributable watermark to the given document content string.
    This helper is format-agnostic and modifies the content structure based on the MIME type
    to preserve syntactic validity of the document (e.g., XML/HTML comments, JSON keys, CSV rows).

    The original stored document content is NEVER modified in the database.

    Args:
        content (str): The document content string to watermark.
        mime_type (str): The MIME type of the document (e.g. application/json, application/xml, text/plain).
        user_id (str): The ID of the requester.
        user_role (str): The role/roles of the requester.

    Returns:
        str: The watermarked content string.
    """
    now_utc = datetime.now(timezone.utc).isoformat()
    marker = "CONFIDENTIAL — Auditor Copy"
    watermark_msg = f"{marker} | Access by: {user_id} ({user_role}) | UTC Time: {now_utc}"

    mime_lower = mime_type.lower().strip()

    # 1. JSON
    if "json" in mime_lower:
        try:
            parsed = json.loads(content)
            if isinstance(parsed, dict):
                parsed["_watermark"] = {
                    "marker": marker,
                    "accessed_by": user_id,
                    "role": user_role,
                    "timestamp": now_utc,
                }
                return json.dumps(parsed, indent=2)
        except Exception:
            # Fallback to plain text style if JSON is invalid or not a dictionary
            pass

    # 2. XML / HTML
    if "xml" in mime_lower or "html" in mime_lower:
        comment = f"\n<!-- {watermark_msg} -->"
        return content + comment

    # 3. CSV
    if "csv" in mime_lower:
        row = f'\n# {watermark_msg}'
        return content + row

    # 4. Fallback (Plain Text / unknown formats)
    fallback_block = (
        f"\n\n--- WATERMARK ---\n"
        f"CONFIDENTIAL — Auditor Copy\n"
        f"Accessed by: {user_id} ({user_role})\n"
        f"UTC Timestamp: {now_utc}\n"
        f"------------------\n"
    )
    return content + fallback_block
