import base64

from cryptography import x509
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa


class TamperDetectedError(Exception):
    """Exception raised when post-signature document tampering is detected.

    Requirements: PRD-SYS-001
    """

    def __init__(
        self,
        message: str,
        is_valid: bool = False,
        status: str = "TAMPERED_INVALID_HASH",
    ):
        super().__init__(message)
        self.is_valid = is_valid
        self.status = status


class VerificationResult:
    """Represents the outcome of a cryptographic signature verification."""

    def __init__(self, is_valid: bool, status: str, failure_reason: str = ""):
        self.is_valid = is_valid
        self.status = status
        self.failure_reason = failure_reason

    def __bool__(self) -> bool:
        return self.is_valid


class ESignatureVerifier:
    """ESignatureVerifier verifies 21 CFR Part 11 signatures and detects tampering."""

    def __init__(self, revoked_certs=None):
        """Initialize the verifier with a set of revoked certificate identifiers.

        Args:
            revoked_certs (set/list, optional): PEM strings, serial numbers, or SHA-256 fingerprints
                                                of revoked certificates.
        """
        self.revoked_certs = set(revoked_certs) if revoked_certs else set()

    def _get_normalized_revoked_certs(
        self,
    ) -> tuple[set[int], set[str], set[str], set[str]]:
        """Normalize all revoked_certs identifiers into strongly-typed representations.

        Returns:
            tuple: (serials_int, serials_str, fingerprints, pems)
        """
        serials_int: set[int] = set()
        serials_str: set[str] = set()
        fingerprints: set[str] = set()
        pems: set[str] = set()

        if not self.revoked_certs:
            return serials_int, serials_str, fingerprints, pems

        for item in self.revoked_certs:
            if item is None:
                continue

            if isinstance(item, int):
                serials_int.add(item)
                serials_str.add(str(item))
                serials_str.add(hex(item)[2:].lower())
                serials_str.add(hex(item).lower())
            elif isinstance(item, str):
                clean_item = item.strip()
                if not clean_item:
                    continue

                # Check if it is a PEM certificate block
                if "-----BEGIN CERTIFICATE-----" in clean_item:
                    pems.add(clean_item)
                    try:
                        parsed_cert = x509.load_pem_x509_certificate(
                            clean_item.encode("utf-8")
                        )
                        sn = parsed_cert.serial_number
                        serials_int.add(sn)
                        serials_str.add(str(sn))
                        serials_str.add(hex(sn)[2:].lower())
                        serials_str.add(hex(sn).lower())
                        fingerprints.add(
                            parsed_cert.fingerprint(hashes.SHA256()).hex().lower()
                        )
                    except Exception:
                        pass
                    continue

                # Check if it is a SHA-256 fingerprint (64 hex characters)
                fp_candidate = (
                    clean_item.replace(":", "")
                    .replace("-", "")
                    .replace(" ", "")
                    .lower()
                )
                if len(fp_candidate) == 64 and all(
                    c in "0123456789abcdef" for c in fp_candidate
                ):
                    fingerprints.add(fp_candidate)

                # Check if it is an integer in decimal format
                if clean_item.isdigit():
                    sn = int(clean_item)
                    serials_int.add(sn)
                    serials_str.add(clean_item)
                    serials_str.add(str(sn))
                    serials_str.add(hex(sn)[2:].lower())
                    serials_str.add(hex(sn).lower())
                # Check if it is a hex format starting with 0x / 0X
                elif clean_item.lower().startswith("0x"):
                    try:
                        sn = int(clean_item, 16)
                        serials_int.add(sn)
                        serials_str.add(clean_item.lower())
                        serials_str.add(str(sn))
                        serials_str.add(hex(sn)[2:].lower())
                    except ValueError:
                        serials_str.add(clean_item.lower())
                else:
                    # Generic hex string or raw string identifier
                    try:
                        sn = int(clean_item, 16)
                        serials_int.add(sn)
                        serials_str.add(str(sn))
                        serials_str.add(hex(sn)[2:].lower())
                        serials_str.add(hex(sn).lower())
                    except ValueError:
                        pass
                    serials_str.add(clean_item.lower())

        return serials_int, serials_str, fingerprints, pems

    def verify_signature(self, signed_data: bytes) -> VerificationResult:
        """Verify the embedded signature of a signed document payload.

        Args:
            signed_data (bytes): The full signed document payload.

        Returns:
            VerificationResult: The result of the verification check.
        """
        try:
            # Check for duplicate/injected cert/signature blocks
            if (
                signed_data.count(b"-----BEGIN CERTIFICATE-----") > 1
                or signed_data.count(b"-----BEGIN SIGNATURE-----") > 1
                or signed_data.count(b"<X509Certificate>") > 1
                or signed_data.count(b"<SignatureValue>") > 1
                or signed_data.count(b"<Signature>") > 1
            ):
                return VerificationResult(
                    is_valid=False,
                    status="DUPLICATE_BLOCKS_REJECTED",
                    failure_reason="Duplicate or injected certificate/signature blocks detected.",
                )

            # Locate certificate and signature PEM blocks
            cert_start = signed_data.find(b"-----BEGIN CERTIFICATE-----")
            sig_start = signed_data.find(b"-----BEGIN SIGNATURE-----")
            sig_end = signed_data.find(b"-----END SIGNATURE-----")

            if cert_start == -1 or sig_start == -1 or sig_end == -1:
                # Fallback check if markers are missing
                if b"mock" in signed_data.lower():
                    return VerificationResult(
                        is_valid=False,
                        status="MOCK_SIGNATURE_DETECTED",
                        failure_reason="Mock signature detected and blocked.",
                    )
                return VerificationResult(
                    is_valid=False,
                    status="TAMPERED_INVALID_HASH",
                    failure_reason="TAMPER DETECTED: Missing certificate or signature markers.",
                )

            # Extract components
            original_data = signed_data[:cert_start].rstrip()
            cert_pem = signed_data[cert_start:sig_start].strip()
            sig_b64 = (
                signed_data[sig_start + len(b"-----BEGIN SIGNATURE-----") : sig_end]
                .strip()
                .decode("utf-8", errors="ignore")
            )

            # Robust mock detection
            is_mock_cert = False
            cert_pem_str = cert_pem.decode("utf-8", errors="ignore")
            if "mock" in cert_pem_str.lower():
                try:
                    parsed_cert = x509.load_pem_x509_certificate(cert_pem)
                    subject_str = parsed_cert.subject.rfc4514_string().lower()
                    issuer_str = parsed_cert.issuer.rfc4514_string().lower()
                    if "mock" in subject_str or "mock" in issuer_str:
                        is_mock_cert = True
                except Exception:
                    is_mock_cert = True

            is_mock_sig = False
            if "mock" in sig_b64.lower():
                try:
                    sig_bytes = base64.b64decode(sig_b64)
                    if sig_bytes is None or len(sig_bytes) < 64:
                        is_mock_sig = True
                    else:
                        if b"MOCK" in sig_bytes or b"mock" in sig_bytes:
                            is_mock_sig = True
                except Exception:
                    is_mock_sig = True

            if is_mock_cert or is_mock_sig:
                return VerificationResult(
                    is_valid=False,
                    status="MOCK_SIGNATURE_DETECTED",
                    failure_reason="Mock signature detected and blocked.",
                )

            # Load certificate
            try:
                cert = x509.load_pem_x509_certificate(cert_pem)
            except Exception as e:
                return VerificationResult(
                    is_valid=False,
                    status="TAMPERED_INVALID_HASH",
                    failure_reason=f"TAMPER DETECTED: Invalid certificate PEM: {str(e)}",
                )

            # Load public key and decode signature
            public_key = cert.public_key()
            try:
                signature = base64.b64decode(sig_b64)
            except Exception as e:
                return VerificationResult(
                    is_valid=False,
                    status="TAMPERED_INVALID_HASH",
                    failure_reason=f"TAMPER DETECTED: Failed to decode signature Base64: {str(e)}",
                )

            # Cryptographically verify the signature of the combined data (original data + cert_pem)
            try:
                if isinstance(public_key, rsa.RSAPublicKey):
                    try:
                        public_key.verify(
                            signature,
                            original_data + cert_pem,
                            padding.PSS(
                                mgf=padding.MGF1(hashes.SHA256()),
                                salt_length=padding.PSS.MAX_LENGTH,
                            ),
                            hashes.SHA256(),
                        )
                    except InvalidSignature:
                        # Check if it was signed with PKCS#1 v1.5 deterministic padding
                        try:
                            public_key.verify(
                                signature,
                                original_data + cert_pem,
                                padding.PKCS1v15(),
                                hashes.SHA256(),
                            )
                            # Succeeded with PKCS1v15, meaning it's a legacy padding signature!
                            import logging

                            logging.getLogger("esignature-verifier").error(
                                "COMPLIANCE ALERT: Legacy PKCS#1 v1.5 signature padding detected. This signature is insecure and has been rejected."
                            )
                            return VerificationResult(
                                is_valid=False,
                                status="LEGACY_PADDING_REJECTED",
                                failure_reason="LEGACY PADDING DETECTED: Document signatures using legacy PKCS#1 v1.5 padding fail verification to satisfy 21 CFR Part 11 strict compliance.",
                            )
                        except Exception:
                            # It failed PKCS1v15 too, so it's just a regular invalid signature
                            raise InvalidSignature()
                else:
                    public_key.verify(
                        signature,
                        original_data + cert_pem,
                        hashes.SHA256(),
                    )
            except InvalidSignature:
                return VerificationResult(
                    is_valid=False,
                    status="TAMPERED_INVALID_HASH",
                    failure_reason="TAMPER DETECTED: Cryptographic signature mismatch (data or certificate modified post-signature).",
                )

            cert_pem_str = cert_pem.decode("utf-8", errors="ignore")

            # Trust store validation
            from packages.security.cert_store import get_active_cert_store

            cert_store = get_active_cert_store()

            # Check self-signed using the public trust store validation service
            is_self_signed = cert.issuer == cert.subject
            if is_self_signed:
                if not cert_store.verify_trust(cert_pem_str):
                    return VerificationResult(
                        is_valid=False,
                        status="UNTRUSTED_SELF_SIGNED",
                        failure_reason="Self-signed certificate is not approved in trust store",
                    )

            # Check active status (expiration and revocation)
            is_valid_status, status_msg = cert_store.verify_certificate_status(
                cert_pem_str
            )
            if not is_valid_status:
                return VerificationResult(
                    is_valid=False,
                    status=status_msg
                    if "REVOKED" in status_msg
                    else "CERTIFICATE_INVALID",
                    failure_reason=f"Certificate validation failed: {status_msg}",
                )

            # Check for revocation in constructor-provided revoked_certs
            cert_serial = cert.serial_number
            cert_serial_dec = str(cert_serial)
            cert_serial_hex = hex(cert_serial)[2:].lower()
            cert_serial_hex_0x = hex(cert_serial).lower()
            cert_fingerprint = cert.fingerprint(hashes.SHA256()).hex().lower()
            cert_pem_normalized = cert_pem_str.strip()

            serials_int, serials_str, fingerprints, pems = (
                self._get_normalized_revoked_certs()
            )

            is_revoked = (
                cert_serial in serials_int
                or cert_serial_dec in serials_str
                or cert_serial_hex in serials_str
                or cert_serial_hex_0x in serials_str
                or cert_fingerprint in fingerprints
                or cert_pem_normalized in pems
            )

            if is_revoked:
                return VerificationResult(
                    is_valid=False,
                    status="CERTIFICATE_REVOKED",
                    failure_reason="Certificate has been revoked.",
                )

            return VerificationResult(is_valid=True, status="VALID")

        except Exception as e:
            return VerificationResult(
                is_valid=False,
                status="TAMPERED_INVALID_HASH",
                failure_reason=f"TAMPER DETECTED: Verification system failure: {str(e)}",
            )

    def verify_pdf(self, pdf_bytes: bytes) -> VerificationResult:
        """Verify the signature and integrity of a PDF document.

        Args:
            pdf_bytes (bytes): The full signed PDF bytes.

        Returns:
            VerificationResult: The verification result if valid.

        Raises:
            TamperDetectedError: If tampering/byte alteration is detected.
        """
        res = self.verify_signature(pdf_bytes)
        if not res.is_valid and res.status == "TAMPERED_INVALID_HASH":
            raise TamperDetectedError(
                f"TAMPER DETECTED: {res.failure_reason}",
                is_valid=False,
                status="TAMPERED_INVALID_HASH",
            )
        return res
