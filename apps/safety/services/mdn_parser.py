"""Automated MDN (Message Disposition Notification) Parser & Verifier.

Parses RFC 3798 / RFC 4130 synchronous and asynchronous MDN receipts, performs cryptographic
signature verification, and validates Message Integrity Check (MIC) hashes.

Requirements: PRD-PVG-002, ADR-2199
"""

import os
import re

from cryptography import x509
from cryptography.hazmat.primitives.asymmetric import rsa

from apps.safety.domain.as2_models import MDNDispositionType, MDNParseResult
from apps.safety.services.crypto_smime import (
    sign_payload_smime,
    verify_signature_smime,
)


class MDNParser:
    """Enterprise parser and validator for AS2 Message Disposition Notifications (MDN)."""

    @classmethod
    def parse_raw_mdn(
        cls,
        mdn_raw: str | bytes,
        expected_message_id: str | None = None,
        expected_mic: str | None = None,
        authority_cert: str | bytes | x509.Certificate | None = None,
    ) -> MDNParseResult:
        """Parse raw MDN content, perform signature check and MIC comparison.

        Args:
            mdn_raw: Raw MIME or text MDN content (bytes or string).
            expected_message_id: Optional expected outbound Message-ID to verify against.
            expected_mic: Optional expected MIC string to compare.
            authority_cert: Optional authority public certificate to verify signed MDNs.

        Returns:
            MDNParseResult with parsed fields and validation status.
        """
        raw_bytes = mdn_raw.encode("utf-8") if isinstance(mdn_raw, str) else mdn_raw
        raw_str = raw_bytes.decode("utf-8", errors="replace")

        is_signed = False
        sig_valid: bool | None = None
        report_content_str = raw_str

        # 1. Check if MDN is a signed MIME structure (multipart/signed)
        if "multipart/signed" in raw_str.lower() or "smime.p7s" in raw_str:
            is_signed = True
            is_ok, extracted_bytes, err = verify_signature_smime(
                raw_bytes, trusted_cert=authority_cert
            )
            sig_valid = is_ok
            if is_ok and extracted_bytes:
                report_content_str = extracted_bytes.decode("utf-8", errors="replace")
            elif not is_ok:
                # If signature verification failed but we still want to parse what we can
                pass

        # 2. Extract fields using regex & MIME parser
        headers_dict: dict[str, str] = {}
        human_readable_text: str | None = None
        orig_msg_id: str | None = None
        reporting_ua: str | None = None
        disposition_mode: str | None = None
        disp_type = MDNDispositionType.FAILED
        raw_disp: str | None = None
        received_mic: str | None = None
        error_details: str | None = None

        # Parse headers directly from content
        for line in report_content_str.splitlines():
            line_str = line.strip()
            if ":" in line_str:
                k, v = line_str.split(":", 1)
                k_clean = k.strip().title()
                headers_dict[k_clean] = v.strip()

        # Extract Original-Message-ID
        msg_id_match = re.search(
            r"Original-Message-ID:\s*([^\r\n;]+)", report_content_str, re.IGNORECASE
        )
        if msg_id_match:
            orig_msg_id = msg_id_match.group(1).strip()
            # Strip angle brackets if present
            if orig_msg_id.startswith("<") and orig_msg_id.endswith(">"):
                orig_msg_id = orig_msg_id[1:-1]

        # Extract Reporting-UA
        ua_match = re.search(
            r"Reporting-UA:\s*([^\r\n;]+)", report_content_str, re.IGNORECASE
        )
        if ua_match:
            reporting_ua = ua_match.group(1).strip()

        # Extract Received-Content-MIC
        mic_match = re.search(
            r"Received-Content-MIC:\s*([^\r\n]+)", report_content_str, re.IGNORECASE
        )
        if mic_match:
            received_mic = mic_match.group(1).strip()

        # Extract Disposition
        disp_match = re.search(
            r"Disposition:\s*([^\r\n]+)", report_content_str, re.IGNORECASE
        )
        if disp_match:
            raw_disp = disp_match.group(1).strip()
            raw_lower = raw_disp.lower()

            if "automatic-action" in raw_lower:
                disposition_mode = "automatic-action"
            elif "manual-action" in raw_lower:
                disposition_mode = "manual-action"

            if "processed" in raw_lower:
                if "error" in raw_lower:
                    disp_type = MDNDispositionType.PROCESSED_ERROR
                elif "warning" in raw_lower:
                    disp_type = MDNDispositionType.PROCESSED_WARNING
                else:
                    disp_type = MDNDispositionType.PROCESSED
            elif "failed" in raw_lower:
                disp_type = MDNDispositionType.FAILED
            elif "rejected" in raw_lower:
                disp_type = MDNDispositionType.REJECTED
            else:
                disp_type = MDNDispositionType.FAILED

        # Extract Error / Failure description
        err_match = re.search(
            r"(?:Error|Failure):\s*([^\r\n]+)", report_content_str, re.IGNORECASE
        )
        if err_match:
            error_details = err_match.group(1).strip()

        # Extract human readable text (first text/plain section or beginning of body)
        text_match = re.search(
            r"(?:Your message has been|The AS2 message|Message transmission|Receipt:)(.+?)(?:\r?\n\r?\n|Content-Type:|Final-Recipient:|$)",
            report_content_str,
            re.DOTALL | re.IGNORECASE,
        )
        if text_match:
            human_readable_text = text_match.group(0).strip()
        else:
            # Fallback to lines before the disposition notification headers
            lines_before = []
            for line in report_content_str.splitlines():
                if "disposition:" in line.lower() or "final-recipient:" in line.lower():
                    break
                if (
                    line.strip()
                    and not line.strip().startswith("--")
                    and not line.strip().startswith("Content-")
                ):
                    lines_before.append(line.strip())
            if lines_before:
                human_readable_text = " ".join(lines_before[:3])

        # 3. Perform MIC Comparison
        mic_matches: bool | None = None
        if received_mic and expected_mic:
            mic_matches = cls.verify_mic_match(received_mic, expected_mic)

        # 4. Determine overall validity
        is_valid = bool(
            raw_disp
            and (orig_msg_id is not None or "processed" in (raw_disp or "").lower())
        )
        if expected_message_id and orig_msg_id:
            exp_clean = expected_message_id.strip("<>")
            got_clean = orig_msg_id.strip("<>")
            if exp_clean != got_clean:
                is_valid = False
                error_details = f"Original-Message-ID mismatch: expected '{exp_clean}', got '{got_clean}'"

        if is_signed and sig_valid is False:
            is_valid = False
            error_details = (
                error_details
                or "MDN cryptographic digital signature verification failed"
            )

        if mic_matches is False:
            is_valid = False
            error_details = (
                error_details
                or "Received-Content-MIC does not match transmitted payload digest"
            )

        return MDNParseResult(
            is_valid_mdn=is_valid,
            original_message_id=orig_msg_id,
            reporting_ua=reporting_ua,
            disposition_mode=disposition_mode,
            disposition_type=disp_type,
            disposition_raw=raw_disp,
            received_content_mic=received_mic,
            mic_matches=mic_matches,
            is_signed=is_signed,
            signature_valid=sig_valid,
            human_readable_text=human_readable_text,
            error_details=error_details,
            headers=headers_dict,
        )

    @classmethod
    def verify_mic_match(cls, received_mic: str, expected_mic: str) -> bool:
        """Verify whether received MIC matches expected outbound calculated MIC.

        Handles variations in algorithm tagging and whitespace (e.g. 'hash, sha-256' vs 'hash, sha256').

        Args:
            received_mic: MIC string returned in MDN header.
            expected_mic: Stored MIC string calculated at transmission time.

        Returns:
            bool: True if digests match identically, False otherwise.
        """
        if not received_mic or not expected_mic:
            return False

        def extract_digest(mic_str: str) -> str:
            clean = mic_str.strip()
            if "," in clean:
                digest_part = clean.split(",", 1)[0].strip()
            else:
                digest_part = clean.split()[0].strip()
            return digest_part

        digest_rec = extract_digest(received_mic)
        digest_exp = extract_digest(expected_mic)
        return digest_rec == digest_exp

    @classmethod
    def generate_mock_mdn(
        cls,
        original_message_id: str,
        received_mic: str,
        sender_as2_id: str = "CADENCE-SPONSOR",
        receiver_as2_id: str = "FDA-ESG",
        disposition_type: MDNDispositionType = MDNDispositionType.PROCESSED,
        signing_key_pem: str | bytes | rsa.RSAPrivateKey | None = None,
        signing_cert_pem: str | bytes | x509.Certificate | None = None,
        human_text: str = "The AS2 message has been received successfully by the Gateway.",
        error_message: str | None = None,
    ) -> tuple[bytes, dict[str, str]]:
        """Generate a compliant RFC 3798 / RFC 4130 MDN response for testing.

        Args:
            original_message_id: Outbound transmission Message-ID.
            received_mic: Message Integrity Check hash.
            sender_as2_id: Originator AS2 identifier.
            receiver_as2_id: Gateway receiver AS2 identifier.
            disposition_type: Disposition outcome enum.
            signing_key_pem: Optional private key to sign the MDN.
            signing_cert_pem: Optional X.509 certificate for signature.
            human_text: Human readable text section.
            error_message: Optional failure details if failed.

        Returns:
            Tuple of (mdn_bytes, headers_dict).
        """
        boundary = f"----=_Part_MDN_{os.urandom(8).hex()}"
        clean_msg_id = (
            original_message_id
            if original_message_id.startswith("<")
            else f"<{original_message_id}>"
        )

        disp_status_str = "processed"
        if disposition_type == MDNDispositionType.FAILED:
            disp_status_str = "failed"
        elif disposition_type == MDNDispositionType.REJECTED:
            disp_status_str = "rejected"
        elif disposition_type == MDNDispositionType.PROCESSED_ERROR:
            disp_status_str = "processed/error"
        elif disposition_type == MDNDispositionType.PROCESSED_WARNING:
            disp_status_str = "processed/warning"

        report_lines = [
            f"--{boundary}",
            "Content-Type: text/plain; charset=us-ascii",
            "Content-Transfer-Encoding: 7bit",
            "",
            human_text,
            f"--{boundary}",
            "Content-Type: message/disposition-notification",
            "Content-Transfer-Encoding: 7bit",
            "",
            f"Reporting-UA: Cadence AS2 Server / {receiver_as2_id}",
            f"Original-Recipient: rfc822; {receiver_as2_id}",
            f"Final-Recipient: rfc822; {receiver_as2_id}",
            f"Original-Message-ID: {clean_msg_id}",
            f"Disposition: automatic-action/MDN-sent-automatically; {disp_status_str}",
            f"Received-Content-MIC: {received_mic}",
        ]
        if error_message:
            report_lines.append(f"Error: {error_message}")

        report_lines.extend([f"--{boundary}--", ""])
        body_str = "\r\n".join(report_lines)
        body_bytes = body_str.encode("utf-8")

        headers = {
            "Content-Type": f'multipart/report; report-type=disposition-notification; boundary="{boundary}"',
            "AS2-Version": "1.2",
            "AS2-From": receiver_as2_id,
            "AS2-To": sender_as2_id,
            "Message-ID": f"<{os.urandom(8).hex()}@cadence-as2>",
        }

        # If signed MDN requested
        if signing_key_pem is not None and signing_cert_pem is not None:
            signed_bytes, signed_headers = sign_payload_smime(
                payload=body_bytes,
                private_key=signing_key_pem,
                certificate=signing_cert_pem,
                content_type=headers["Content-Type"],
            )
            signed_headers["AS2-Version"] = "1.2"
            signed_headers["AS2-From"] = receiver_as2_id
            signed_headers["AS2-To"] = sender_as2_id
            signed_headers["Message-ID"] = f"<{os.urandom(8).hex()}@cadence-as2>"
            return signed_bytes, signed_headers

        return body_bytes, headers
