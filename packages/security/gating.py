"""
Shared gating module for 21 CFR Part 11 signature step-up authentication.
"""

SIGNATURE_GATED_PATTERNS = [
    "approve",
    "sign-off",
    "unblind",
    "randomize",
    "queries/sync",
    "close",
    "sign",
    "capture-consent",
]


def is_path_signature_gated(path_lower: str) -> bool:
    """
    Checks if a lowercase HTTP request path is gated under electronic signature requirements.

    Preserves existing substring-match semantics for historical patterns like 'approve' or 'sign-off'.
    For the newly introduced 'sign' pattern, we use exact path segment matching or exact ending checks
    to prevent false positives such as 'design', 'assignments', 'designers', or 'signature-verification'.
    We also exclude 'econsent' paths from gateway-level step-up gating as eConsent handles its own subject-level
    consent checks.
    """
    if "capture-consent" in path_lower:
        return True
    if "econsent" in path_lower:
        return False

    for pattern in SIGNATURE_GATED_PATTERNS:
        if pattern == "sign":
            # Match exactly segment "sign" or if path ends with "/sign"
            segments = path_lower.split("/")
            if "sign" in segments:
                return True
        elif pattern in path_lower:
            return True
    return False
