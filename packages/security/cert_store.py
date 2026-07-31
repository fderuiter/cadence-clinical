"""X.509 certificate store and Certificate Revocation List (CRL) verification service.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime

from cryptography import x509


class CertificateStoreService:
    """Service managing X.509 user public key certificates and revocation status checks.

    Requirements: PRD-SYS-001
    """

    def __init__(self) -> None:
        """Initialize in-memory certificate registry and CRL revocation set."""
        self._cert_registry: dict[str, dict] = {}
        self._revocation_list: dict[str, str] = {}

    def register_certificate(self, user_id: str, cert_pem: str) -> dict:
        """Register user X.509 digital certificate credential in certificate store.

        Args:
            user_id: Target user ID.
            cert_pem: PEM encoded X.509 certificate string.

        Returns:
            Certificate metadata record dictionary.
        """
        cert = x509.load_pem_x509_certificate(cert_pem.encode("utf-8"))
        serial_hex = hex(cert.serial_number)[2:]
        now_iso = datetime.now(UTC).isoformat()

        record = {
            "user_id": user_id,
            "serial_number": serial_hex,
            "subject": cert.subject.rfc4514_string(),
            "issuer": cert.issuer.rfc4514_string(),
            "not_before": cert.not_valid_before_utc.isoformat(),
            "not_after": cert.not_valid_after_utc.isoformat(),
            "registered_at": now_iso,
            "pem": cert_pem,
        }

        self._cert_registry[serial_hex] = record
        return record

    def revoke_certificate(self, cert_serial: str, reason: str) -> None:
        """Add certificate serial number to Certificate Revocation List (CRL).

        Args:
            cert_serial: Certificate serial number in hex or decimal string format.
            reason: Mandatory GxP revocation reason justification.
        """
        clean_serial = cert_serial.lower().replace("0x", "")
        self._revocation_list[clean_serial] = reason

    def verify_certificate_status(self, cert_pem: str) -> tuple[bool, str]:
        """Validate certificate expiration and CRL revocation status.

        Args:
            cert_pem: PEM encoded X.509 certificate string.

        Returns:
            Tuple (is_valid, status_code_string).
        """
        try:
            cert = x509.load_pem_x509_certificate(cert_pem.encode("utf-8"))
            serial_hex = hex(cert.serial_number)[2:].lower()
            now_utc = datetime.now(UTC)

            # Check revocation
            if serial_hex in self._revocation_list:
                return False, f"REVOKED: {self._revocation_list[serial_hex]}"

            # Check expiration
            if now_utc < cert.not_valid_before_utc:
                return False, "NOT_YET_VALID"

            if now_utc > cert.not_valid_after_utc:
                return False, "EXPIRED"

            return True, "VALID"
        except Exception as exc:
            return False, f"INVALID_FORMAT: {str(exc)}"
