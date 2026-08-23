"""Domain models and enums for Secure AS2 B2B Transmission Gateway & E2B(R3) MDN Parser.

Requirements: PRD-PVG-002, ADR-2199
"""

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class AS2Authority(StrEnum):
    """Supported Global Regulatory Health Authorities and AS2 Endpoints."""

    FDA_ESG = "FDA_ESG"  # US FDA Electronic Submissions Gateway
    EMA_GATEWAY = "EMA_GATEWAY"  # European Medicines Agency EudraVigilance Gateway
    PMDA = "PMDA"  # Japan Pharmaceuticals and Medical Devices Agency
    MHRA = "MHRA"  # UK Medicines and Healthcare products Regulatory Agency
    HEALTH_CANADA = "HEALTH_CANADA"  # Health Canada Gateway


class TransmissionStatus(StrEnum):
    """Lifecycle status of an outbound AS2 B2B Transmission."""

    PENDING = "PENDING"
    TRANSMITTED = "TRANSMITTED"
    ACK_A_RECEIVED = "ACK_A_RECEIVED"  # Perimeter transport / MDN confirmed
    ACK_B_RECEIVED = "ACK_B_RECEIVED"  # Syntactic / Schema validation passed
    ACK_C_RECEIVED = "ACK_C_RECEIVED"  # Authority business acceptance / DB ingested
    FAILED_TRANSMISSION = "FAILED_TRANSMISSION"
    FAILED_MDN = "FAILED_MDN"
    FAILED_ACK_B = "FAILED_ACK_B"
    FAILED_ACK_C = "FAILED_ACK_C"
    REJECTED = "REJECTED"


class MDNDispositionType(StrEnum):
    """Standard AS2 RFC 3798 / RFC 4130 MDN Disposition types."""

    PROCESSED = "PROCESSED"
    PROCESSED_WARNING = "PROCESSED_WARNING"
    PROCESSED_ERROR = "PROCESSED_ERROR"
    FAILED = "FAILED"
    REJECTED = "REJECTED"


class AckType(StrEnum):
    """Multi-tier regulatory acknowledgement types for pharmacovigilance gateways."""

    ACK_A = "ACK_A"  # Transport / Gateway receipt (MDN)
    ACK_B = "ACK_B"  # Syntactic / Technical schema validation
    ACK_C = "ACK_C"  # Authority / Business acceptance & FAERS/EudraVigilance ingestion


class AckStatus(StrEnum):
    """Regulatory acknowledgement evaluation outcomes."""

    ACCEPTED = "ACCEPTED"
    ACCEPTED_WITH_WARNINGS = "ACCEPTED_WITH_WARNINGS"
    REJECTED = "REJECTED"
    SYNTAX_ERROR = "SYNTAX_ERROR"
    PARSING_ERROR = "PARSING_ERROR"
    PENDING = "PENDING"


class EncryptionAlgorithm(StrEnum):
    """S/MIME Encryption Algorithms."""

    AES_256_GCM = "AES-256-GCM"
    AES_256_CBC = "AES-256-CBC"
    NONE = "NONE"


class SignatureAlgorithm(StrEnum):
    """Digital Signature Digest Algorithms."""

    SHA_256_RSA = "SHA-256-RSA"
    SHA_256_ECDSA = "SHA-256-ECDSA"
    NONE = "NONE"


class AS2PartnerConfig(BaseModel):
    """Configuration profile for a B2B Regulatory Health Authority Gateway Partner."""

    model_config = ConfigDict(frozen=True)

    as2_id: str = Field(
        ..., description="AS2 partner identifier e.g. FDA-ESG or EMA-GATEWAY"
    )
    url: str = Field(..., description="AS2 gateway endpoint HTTP(S) URL")
    encryption_certificate_pem: str | None = Field(
        default=None,
        description="Partner X.509 certificate for S/MIME payload encryption",
    )
    signing_certificate_pem: str | None = Field(
        default=None, description="Partner X.509 certificate for signature verification"
    )
    request_signed_mdn: bool = Field(
        default=True,
        description="Whether to request a cryptographically signed MDN receipt",
    )
    request_async_mdn: bool = Field(
        default=False,
        description="Whether MDN should be sent asynchronously to a callback URL",
    )
    async_mdn_url: str | None = Field(
        default=None, description="Callback URL for asynchronous MDN responses"
    )
    mic_algorithm: str = Field(
        default="sha-256", description="Message Integrity Check digest algorithm"
    )
    encryption_algorithm: EncryptionAlgorithm = Field(
        default=EncryptionAlgorithm.AES_256_GCM,
        description="S/MIME symmetric encryption cipher",
    )
    signature_algorithm: SignatureAlgorithm = Field(
        default=SignatureAlgorithm.SHA_256_RSA,
        description="Asymmetric digital signature algorithm",
    )


class AS2OutboundTransmissionInput(BaseModel):
    """Input parameters for dispatching an outbound ICH E2B(R3) AS2 transmission."""

    case_id: str | None = Field(
        default=None, description="Safety Case / ICSR database ID"
    )
    study_id: str | None = Field(default=None, description="Clinical Study Identifier")
    icsr_xml_payload: str = Field(
        ..., description="ICH E2B(R3) XML payload conforming to HL7 ICSR standard"
    )
    target_authority: AS2Authority = Field(
        ..., description="Target Regulatory Health Authority"
    )
    sender_as2_id: str = Field(
        default="CADENCE-SPONSOR", description="Sender AS2 Identifier"
    )
    receiver_as2_id: str | None = Field(
        default=None,
        description="Receiver AS2 Identifier (defaults based on target_authority)",
    )
    environment: str = Field(
        default="PRODUCTION",
        description="Deployment environment (PRODUCTION / TEST / STAGING)",
    )
    custom_headers: dict[str, str] = Field(
        default_factory=dict, description="Additional custom HTTP/AS2 headers"
    )


class MDNParseResult(BaseModel):
    """Structured result of parsing and validating an RFC 3798 / RFC 4130 MDN."""

    is_valid_mdn: bool = Field(
        ..., description="Whether the payload is a valid MDN report"
    )
    original_message_id: str | None = Field(
        default=None, description="Message-ID of the original outbound transmission"
    )
    reporting_ua: str | None = Field(
        default=None, description="Reporting User Agent / Gateway software identifier"
    )
    disposition_mode: str | None = Field(
        default=None, description="Disposition action mode (e.g. automatic-action)"
    )
    disposition_type: MDNDispositionType = Field(
        default=MDNDispositionType.FAILED, description="Disposition status outcome"
    )
    disposition_raw: str | None = Field(
        default=None, description="Full raw Disposition header value"
    )
    received_content_mic: str | None = Field(
        default=None, description="MIC value returned by the receiver"
    )
    mic_matches: bool | None = Field(
        default=None,
        description="Whether the returned MIC matches the outbound calculated MIC",
    )
    is_signed: bool = Field(
        default=False,
        description="Whether the MDN contains a cryptographic digital signature",
    )
    signature_valid: bool | None = Field(
        default=None,
        description="Whether the MDN digital signature is cryptographically valid",
    )
    human_readable_text: str | None = Field(
        default=None, description="Human-readable explanation message from MDN"
    )
    error_details: str | None = Field(
        default=None,
        description="Error message if MDN indicates failure or parsing error",
    )
    headers: dict[str, str] = Field(
        default_factory=dict, description="Parsed MIME/AS2 headers"
    )


class RegulatoryAckParseResult(BaseModel):
    """Structured result of parsing regulatory ACK A, ACK B, or ACK C messages."""

    ack_type: AckType = Field(
        ..., description="Acknowledgement type (ACK_A, ACK_B, ACK_C)"
    )
    ack_status: AckStatus = Field(..., description="Acknowledgement validation status")
    is_accepted: bool = Field(
        ..., description="Whether the submission was accepted at this tier"
    )
    authority_receipt_id: str | None = Field(
        default=None,
        description="Authority-assigned receipt or tracking ticket identifier",
    )
    worldwide_unique_case_id: str | None = Field(
        default=None,
        description="Worldwide unique case ID referenced in acknowledgement",
    )
    message_id: str | None = Field(
        default=None, description="Original transmission Message-ID referenced in ACK"
    )
    authority_status_code: str | None = Field(
        default=None,
        description="Raw regulatory ACK code (e.g. AA, AE, AR, 01, 02, 03)",
    )
    errors: list[str] = Field(
        default_factory=list,
        description="List of syntactic or business validation error messages",
    )
    warnings: list[str] = Field(
        default_factory=list, description="List of validation warning messages"
    )
    details: dict[str, Any] = Field(
        default_factory=dict,
        description="Parsed metadata and attributes from the ACK XML",
    )
    received_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Timestamp when acknowledgement was received and parsed",
    )


class AS2TransmissionResult(BaseModel):
    """Summary result returned after dispatching an outbound AS2 transmission."""

    transmission_id: str = Field(
        ..., description="Internal tracking ID for this transmission"
    )
    message_id: str = Field(..., description="AS2 Message-ID header value")
    status: TransmissionStatus = Field(..., description="Current transmission status")
    payload_digest_sha256: str = Field(
        ..., description="SHA-256 hex digest of original XML"
    )
    mic: str = Field(..., description="Computed Message Integrity Check string")
    http_status_code: int | None = Field(
        default=None, description="HTTP response status code from gateway"
    )
    mdn_parsed: MDNParseResult | None = Field(
        default=None, description="Parsed synchronous MDN receipt (if returned)"
    )
    error_message: str | None = Field(
        default=None, description="Error message if transmission failed"
    )
