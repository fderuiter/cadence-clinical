"""21 CFR Part 11 audit manifest generator for signed clinical subject casebooks.

Requirements: PRD-SYS-001
"""

import hashlib
import json

import packages  # noqa: F401


class AuditManifestGenerator:
    """Generator compiling 21 CFR Part 11 compliant cryptographic audit manifests for signed casebooks.

    Requirements: PRD-SYS-001
    """

    def generate_casebook_manifest(
        self,
        study_id: str,
        subject_id: str,
        signature_id: str,
        signer_name: str,
        signer_user_id: str,
        signing_reason: str,
        form_digests: dict[str, str],
        timestamp_utc: str,
    ) -> dict[str, str]:
        """Compile Part 11 audit manifest containing form digests, master root hash, and printed signature details.

        Args:
            study_id: Protocol study ID.
            subject_id: Clinical trial subject ID.
            signature_id: Cryptographic eSignature ID.
            signer_name: Printed name of investigator/signer.
            signer_user_id: User ID of investigator/signer.
            signing_reason: Legally binding signature purpose.
            form_digests: Dictionary mapping form IDs to SHA-256 digests.
            timestamp_utc: UTC ISO execution timestamp.

        Returns:
            Structured 21 CFR Part 11 audit manifest dictionary.
        """
        # Calculate master root digest over sorted form digests
        sorted_pairs = sorted(form_digests.items())
        combined_str = "|".join([f"{fid}:{dig}" for fid, dig in sorted_pairs])
        master_digest = hashlib.sha256(combined_str.encode("utf-8")).hexdigest()

        printable_summary = (
            f"21 CFR Part 11 ELECTRONIC SIGNATURE MANIFEST\n"
            f"Study ID: {study_id} | Subject ID: {subject_id}\n"
            f"Signed By: {signer_name} ({signer_user_id})\n"
            f"Reason: {signing_reason}\n"
            f"Timestamp UTC: {timestamp_utc}\n"
            f"Signature ID: {signature_id}\n"
            f"Master Root Digest: {master_digest}\n"
            f"Signed Forms Count: {len(form_digests)}\n"
        )

        return {
            "manifest_version": "1.0",
            "study_id": study_id,
            "subject_id": subject_id,
            "signature_id": signature_id,
            "signer_name": signer_name,
            "signer_user_id": signer_user_id,
            "signing_reason": signing_reason,
            "timestamp_utc": timestamp_utc,
            "master_root_digest": master_digest,
            "form_digests": json.dumps(form_digests, sort_keys=True),
            "printable_summary": printable_summary,
        }
