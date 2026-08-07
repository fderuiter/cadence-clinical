"""
Re-export SignatureManifestation and SigningReason for packages.security compatibility.
"""

from signature import (
    SignatureManifestation,
    SigningReason,
    verify_signature_manifestation,
)

__all__ = [
    "SignatureManifestation",
    "SigningReason",
    "verify_signature_manifestation",
]
