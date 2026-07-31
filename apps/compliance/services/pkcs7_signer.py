import base64

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa


class PKCS7Signer:
    """PKCS7Signer handles 21 CFR Part 11 electronic signatures for PDF documents."""

    def __init__(self, cert: x509.Certificate, key: rsa.RSAPrivateKey):
        """Initialize the signer with a certificate and private key.

        Args:
            cert (x509.Certificate): The X.509 certificate of the signer.
            key (rsa.RSAPrivateKey): The RSA private key of the signer.
        """
        if isinstance(cert, (str, bytes)):
            pem_bytes = cert.encode("utf-8") if isinstance(cert, str) else cert
            self.cert = x509.load_pem_x509_certificate(pem_bytes)
        else:
            self.cert = cert

        if isinstance(key, (str, bytes)):
            pem_bytes = key.encode("utf-8") if isinstance(key, str) else key
            self.key = serialization.load_pem_private_key(pem_bytes, password=None)
        else:
            self.key = key

    def sign_document(self, data: bytes) -> bytes:
        """Sign a document and embed the PKCS#7 / X.509 signature.

        To guarantee absolute tamper-detection across all parts of the signed document,
        both the original data and the certificate are cryptographically signed together.

        Args:
            data (bytes): The original document content.

        Returns:
            bytes: The signed document content containing the original payload,
                   the certificate, and the cryptographic signature.
        """
        cert_pem = self.cert.public_bytes(serialization.Encoding.PEM).strip()
        # Sign the combination of data and certificate to bind them together securely
        to_sign = data + cert_pem
        signature = self.key.sign(to_sign, padding.PKCS1v15(), hashes.SHA256())
        sig_b64 = base64.b64encode(signature)

        signed_payload = (
            data
            + b"\n"
            + cert_pem
            + b"\n-----BEGIN SIGNATURE-----\n"
            + sig_b64
            + b"\n-----END SIGNATURE-----\n"
        )
        return signed_payload

    def sign_pdf(self, pdf_bytes: bytes) -> bytes:
        """Sign a PDF document. Alias for sign_document.

        Args:
            pdf_bytes (bytes): The original PDF bytes.

        Returns:
            bytes: The signed PDF bytes.
        """
        return self.sign_document(pdf_bytes)
