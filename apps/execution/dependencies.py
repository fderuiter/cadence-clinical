from fastapi import HTTPException, Request


def verify_change_justification(request: Request) -> None:
    """Enforce presence of change justification header (version 1 or 2)."""
    version = request.headers.get("X-Signature-Version")
    change_reason = request.headers.get("X-Change-Reason")
    if version not in ("1", "v1", "2", "v2") or not change_reason:
        raise HTTPException(
            status_code=403,
            detail="API rejects any state modifications that do not contain a verified, gateway-signed change justification header.",
        )
