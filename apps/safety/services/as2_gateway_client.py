"""Enterprise AS2 B2B Communication Gateway Client.

Provides S/MIME encryption (AES-256), SHA-256 digital signatures, automated MDN receipt verification,
and regulatory acknowledgement (ACK A, ACK B, ACK C) processing for ICH E2B(R3) safety reporting
directly to regulatory health authorities (FDA ESG, EMA EudraVigilance, PMDA, MHRA, Health Canada).

Requirements: PRD-PVG-002, ADR-2199
"""

import logging
import os
import uuid
from datetime import UTC, datetime
from email.utils import formatdate

import defusedxml.ElementTree as ET
import httpx
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.adapters.models import (
    AS2Transmission,
    write_as2_audit_log,
)
from apps.safety.domain.as2_models import (
    AckStatus,
    AckType,
    AS2Authority,
    AS2OutboundTransmissionInput,
    AS2PartnerConfig,
    AS2TransmissionResult,
    EncryptionAlgorithm,
    MDNDispositionType,
    MDNParseResult,
    RegulatoryAckParseResult,
    SignatureAlgorithm,
    TransmissionStatus,
)
from apps.safety.services.crypto_smime import (
    calculate_mic,
    calculate_payload_sha256_hex,
    encrypt_payload_smime,
    generate_as2_test_keypair,
    sign_payload_smime,
)
from apps.safety.services.mdn_parser import MDNParser
from apps.safety.services.regulatory_ack_parser import RegulatoryAckParser
from packages.config import get_safety_settings

logger = logging.getLogger("safety-as2-gateway")


def get_default_partner_configs() -> dict[AS2Authority, AS2PartnerConfig]:
    """Get standard regulatory partner configurations for global health authorities."""
    # Generate test partner keys for fallback simulation
    fda_cert, _ = generate_as2_test_keypair(
        common_name="US FDA Electronic Submissions Gateway",
        organization="US Food and Drug Administration",
    )
    ema_cert, _ = generate_as2_test_keypair(
        common_name="EMA EudraVigilance Gateway",
        organization="European Medicines Agency",
    )
    pmda_cert, _ = generate_as2_test_keypair(
        common_name="PMDA Gateway",
        organization="Pharmaceuticals and Medical Devices Agency Japan",
    )
    mhra_cert, _ = generate_as2_test_keypair(
        common_name="MHRA Submissions Gateway",
        organization="Medicines and Healthcare products Regulatory Agency",
    )
    hc_cert, _ = generate_as2_test_keypair(
        common_name="Health Canada Gateway",
        organization="Health Canada",
    )

    settings = get_safety_settings()
    return {
        AS2Authority.FDA_ESG: AS2PartnerConfig(
            as2_id="FDA-ESG",
            url=settings.fda_esg_as2_url,
            encryption_certificate_pem=os.getenv("FDA_ESG_CERT", fda_cert),
            signing_certificate_pem=os.getenv("FDA_ESG_CERT", fda_cert),
            request_signed_mdn=True,
            mic_algorithm="sha-256",
            encryption_algorithm=EncryptionAlgorithm.AES_256_GCM,
            signature_algorithm=SignatureAlgorithm.SHA_256_RSA,
        ),
        AS2Authority.EMA_GATEWAY: AS2PartnerConfig(
            as2_id="EMA-GATEWAY",
            url=settings.ema_as2_url,
            encryption_certificate_pem=os.getenv("EMA_GATEWAY_CERT", ema_cert),
            signing_certificate_pem=os.getenv("EMA_GATEWAY_CERT", ema_cert),
            request_signed_mdn=True,
            mic_algorithm="sha-256",
            encryption_algorithm=EncryptionAlgorithm.AES_256_GCM,
            signature_algorithm=SignatureAlgorithm.SHA_256_RSA,
        ),
        AS2Authority.PMDA: AS2PartnerConfig(
            as2_id="PMDA-GATEWAY",
            url=settings.pmda_as2_url,
            encryption_certificate_pem=os.getenv("PMDA_CERT", pmda_cert),
            signing_certificate_pem=os.getenv("PMDA_CERT", pmda_cert),
            request_signed_mdn=True,
            mic_algorithm="sha-256",
            encryption_algorithm=EncryptionAlgorithm.AES_256_GCM,
            signature_algorithm=SignatureAlgorithm.SHA_256_RSA,
        ),
        AS2Authority.MHRA: AS2PartnerConfig(
            as2_id="MHRA-GATEWAY",
            url=settings.mhra_as2_url,
            encryption_certificate_pem=os.getenv("MHRA_CERT", mhra_cert),
            signing_certificate_pem=os.getenv("MHRA_CERT", mhra_cert),
            request_signed_mdn=True,
            mic_algorithm="sha-256",
            encryption_algorithm=EncryptionAlgorithm.AES_256_GCM,
            signature_algorithm=SignatureAlgorithm.SHA_256_RSA,
        ),
        AS2Authority.HEALTH_CANADA: AS2PartnerConfig(
            as2_id="HC-GATEWAY",
            url=settings.hc_as2_url,
            encryption_certificate_pem=os.getenv("HC_CERT", hc_cert),
            signing_certificate_pem=os.getenv("HC_CERT", hc_cert),
            request_signed_mdn=True,
            mic_algorithm="sha-256",
            encryption_algorithm=EncryptionAlgorithm.AES_256_GCM,
            signature_algorithm=SignatureAlgorithm.SHA_256_RSA,
        ),
    }


class AS2GatewayClient:
    """Enterprise AS2 B2B Gateway Client for secure regulatory transmissions."""

    def __init__(
        self,
        session: AsyncSession,
        sender_as2_id: str = "CADENCE-SPONSOR",
        signing_key_pem: str | None = None,
        signing_cert_pem: str | None = None,
        partner_configs: dict[AS2Authority, AS2PartnerConfig] | None = None,
        http_client: httpx.AsyncClient | None = None,
    ) -> None:
        """Initialize the AS2 Gateway Client.

        Args:
            session: SQLAlchemy AsyncSession for ledger persistence.
            sender_as2_id: AS2 identity for this sponsor organization.
            signing_key_pem: RSA private key for S/MIME signing.
            signing_cert_pem: X.509 certificate for S/MIME signing.
            partner_configs: Dict mapping AS2Authority to partner profiles.
            http_client: Optional bounded async HTTP client.
        """
        self.session = session
        self.sender_as2_id = sender_as2_id

        # Generate default sponsor certificate if not provided
        if not signing_cert_pem or not signing_key_pem:
            c_pem, k_pem = generate_as2_test_keypair(
                common_name="Cadence Clinical Safety Gateway",
                organization="Cadence Clinical Sponsor",
            )
            self.signing_cert_pem = signing_cert_pem or c_pem
            self.signing_key_pem = signing_key_pem or k_pem
        else:
            self.signing_cert_pem = signing_cert_pem
            self.signing_key_pem = signing_key_pem

        self.partner_configs = partner_configs or get_default_partner_configs()
        self.http_client = http_client

    async def send_e2b_payload(
        self,
        input_data: AS2OutboundTransmissionInput,
        user_id: str,
        change_reason: str,
    ) -> AS2TransmissionResult:
        """Sign, encrypt, record in ledger, and transmit an ICH E2B(R3) XML payload via AS2.

        Args:
            input_data: Outbound transmission configuration and ICSR XML.
            user_id: 21 CFR Part 11 authenticated user ID.
            change_reason: Mandatory GxP change justification reason.

        Returns:
            AS2TransmissionResult with transmission status and parsed MDN.
        """
        raw_xml = input_data.icsr_xml_payload.strip()
        authority = input_data.target_authority
        partner_config = self.partner_configs.get(authority)
        if not partner_config:
            raise ValueError(
                f"No AS2 partner configuration available for authority: {authority}"
            )

        receiver_as2_id = input_data.receiver_as2_id or partner_config.as2_id
        sender_as2_id = input_data.sender_as2_id or self.sender_as2_id

        # Extract Worldwide Unique Case ID from XML if present
        worldwide_case_id: str | None = None
        try:
            root = ET.fromstring(raw_xml.encode("utf-8"))
            for elem in root.iter():
                if "worldwide_unique_case_id" in elem.tag.lower() and elem.text:
                    worldwide_case_id = elem.text.strip()
                    break
        except Exception:
            worldwide_case_id = None

        # 1. Message Identifiers & Cryptographic Hashes
        transmission_uuid = uuid.uuid4().hex
        transmission_id = f"TX-{transmission_uuid[:12].upper()}"
        message_id = f"<{datetime.now(UTC).strftime('%Y%m%d%H%M%S')}-{transmission_uuid[:8]}@{sender_as2_id.lower()}.cadence>"

        payload_digest_sha256 = calculate_payload_sha256_hex(raw_xml)
        mic = calculate_mic(raw_xml, algorithm=partner_config.mic_algorithm)
        outbound_payload_size = len(raw_xml.encode("utf-8"))

        # 2. Persist initial AS2 Transmission in Ledger
        transmission = AS2Transmission(
            transmission_id=transmission_id,
            case_id=input_data.case_id,
            study_id=input_data.study_id,
            worldwide_unique_case_id=worldwide_case_id,
            message_id=message_id,
            as2_from=sender_as2_id,
            as2_to=receiver_as2_id,
            target_authority=authority.value,
            environment=input_data.environment,
            status=TransmissionStatus.PENDING.value,
            payload_digest_sha256=payload_digest_sha256,
            mic=mic,
            encryption_algorithm=partner_config.encryption_algorithm.value,
            signature_algorithm=partner_config.signature_algorithm.value,
            outbound_payload_size=outbound_payload_size,
            outbound_xml=raw_xml,
            created_by=user_id,
            reason_for_change=change_reason,
            version_index=1,
        )
        self.session.add(transmission)
        await self.session.flush()

        await write_as2_audit_log(
            session=self.session,
            transmission_id=transmission.id,
            action="AS2_TRANSMISSION_INITIATED",
            from_status=None,
            to_status=TransmissionStatus.PENDING.value,
            details=f"Initiated AS2 transmission {transmission_id} for case {worldwide_case_id or input_data.case_id} to {authority.value}",
            created_by=user_id,
            reason_for_change=change_reason,
        )

        # 3. S/MIME SHA-256 Digital Signature
        try:
            signed_bytes, sign_headers = sign_payload_smime(
                payload=raw_xml,
                private_key=self.signing_key_pem,
                certificate=self.signing_cert_pem,
                content_type="application/xml",
            )
        except Exception as exc:
            transmission.status = TransmissionStatus.FAILED_TRANSMISSION.value
            transmission.error_message = f"S/MIME digital signature failed: {str(exc)}"
            await write_as2_audit_log(
                session=self.session,
                transmission_id=transmission.id,
                action="SIGNING_FAILED",
                from_status=TransmissionStatus.PENDING.value,
                to_status=TransmissionStatus.FAILED_TRANSMISSION.value,
                details=f"Digital signature creation failed: {str(exc)}",
                created_by=user_id,
                reason_for_change=change_reason,
            )
            await self.session.flush()
            return AS2TransmissionResult(
                transmission_id=transmission_id,
                message_id=message_id,
                status=TransmissionStatus.FAILED_TRANSMISSION,
                payload_digest_sha256=payload_digest_sha256,
                mic=mic,
                error_message=f"Digital signing failure: {str(exc)}",
            )

        # 4. S/MIME AES-256 Payload Encryption
        outbound_bytes = signed_bytes
        outbound_headers: dict[str, str] = dict(sign_headers)

        if (
            partner_config.encryption_algorithm != EncryptionAlgorithm.NONE
            and partner_config.encryption_certificate_pem
        ):
            try:
                enc_bytes, enc_headers = encrypt_payload_smime(
                    payload=signed_bytes,
                    recipient_certificate=partner_config.encryption_certificate_pem,
                    algorithm=partner_config.encryption_algorithm.value,
                )
                outbound_bytes = enc_bytes
                outbound_headers.update(enc_headers)
            except Exception as exc:
                transmission.status = TransmissionStatus.FAILED_TRANSMISSION.value
                transmission.error_message = (
                    f"S/MIME AES-256 encryption failed: {str(exc)}"
                )
                await write_as2_audit_log(
                    session=self.session,
                    transmission_id=transmission.id,
                    action="ENCRYPTION_FAILED",
                    from_status=TransmissionStatus.PENDING.value,
                    to_status=TransmissionStatus.FAILED_TRANSMISSION.value,
                    details=f"S/MIME AES-256 encryption failed: {str(exc)}",
                    created_by=user_id,
                    reason_for_change=change_reason,
                )
                await self.session.flush()
                return AS2TransmissionResult(
                    transmission_id=transmission_id,
                    message_id=message_id,
                    status=TransmissionStatus.FAILED_TRANSMISSION,
                    payload_digest_sha256=payload_digest_sha256,
                    mic=mic,
                    error_message=f"S/MIME encryption failure: {str(exc)}",
                )

        # 5. Assemble AS2 Request Headers (RFC 4130)
        as2_headers = {
            "AS2-Version": "1.2",
            "AS2-From": sender_as2_id,
            "AS2-To": receiver_as2_id,
            "Message-ID": message_id,
            "Date": formatdate(timeval=None, localtime=False, usegmt=True),
            "Subject": f"ICH E2B(R3) ICSR Submission - {worldwide_case_id or input_data.case_id or 'Safety Case'}",
            "Disposition-Notification-To": partner_config.async_mdn_url
            or "as2@cadence.clinical",
            "Disposition-Notification-Options": "signed-receipt-protocol=optional, pkcs7-signature; signed-receipt-micalg=optional, sha-256",
            "Content-Type": outbound_headers.get(
                "Content-Type",
                'application/pkcs7-mime; smime-type=enveloped-data; name="smime.p7m"',
            ),
            "User-Agent": "Cadence Clinical AS2 Gateway Client v0.1.0",
        }
        as2_headers.update(input_data.custom_headers)

        transmission.status = TransmissionStatus.TRANSMITTED.value
        await write_as2_audit_log(
            session=self.session,
            transmission_id=transmission.id,
            action="AS2_PAYLOAD_DISPATCHED",
            from_status=TransmissionStatus.PENDING.value,
            to_status=TransmissionStatus.TRANSMITTED.value,
            details=f"Dispatched AS2 S/MIME payload (Size: {len(outbound_bytes)} bytes) to {partner_config.url}",
            created_by=user_id,
            reason_for_change=change_reason,
        )
        await self.session.flush()

        # 6. Outbound HTTP Post with Bounded Timeout
        http_status: int | None = None
        mdn_parsed: MDNParseResult | None = None

        client = self.http_client or httpx.AsyncClient(timeout=30.0)
        try:
            resp = await client.post(
                partner_config.url,
                content=outbound_bytes,
                headers=as2_headers,
            )
            http_status = resp.status_code

            if resp.status_code in (200, 201, 204) and resp.content:
                # 7. Parse and Verify Synchronous MDN
                mdn_parsed = MDNParser.parse_raw_mdn(
                    mdn_raw=resp.content,
                    expected_message_id=message_id,
                    expected_mic=mic,
                    authority_cert=partner_config.signing_certificate_pem,
                )

                if (
                    mdn_parsed.is_valid_mdn
                    and mdn_parsed.mic_matches is True
                    and mdn_parsed.disposition_type
                    in (
                        MDNDispositionType.PROCESSED,
                        MDNDispositionType.PROCESSED_WARNING,
                    )
                ):
                    transmission.status = TransmissionStatus.ACK_A_RECEIVED.value
                    transmission.ack_a_status = AckStatus.ACCEPTED.value
                    transmission.ack_a_received_at = datetime.now(UTC)
                    transmission.mdn_message_id = (
                        mdn_parsed.original_message_id or message_id
                    )
                    transmission.mdn_disposition = mdn_parsed.disposition_raw
                    transmission.mdn_mic = mdn_parsed.received_content_mic
                    transmission.mdn_status = mdn_parsed.disposition_type.value

                    await write_as2_audit_log(
                        session=self.session,
                        transmission_id=transmission.id,
                        action="MDN_VERIFIED_ACK_A",
                        from_status=TransmissionStatus.TRANSMITTED.value,
                        to_status=TransmissionStatus.ACK_A_RECEIVED.value,
                        details=f"Synchronous MDN verified successfully: MIC matched. Disposition: {mdn_parsed.disposition_raw}",
                        created_by=user_id,
                        reason_for_change=change_reason,
                    )
                else:
                    transmission.status = TransmissionStatus.FAILED_MDN.value
                    transmission.ack_a_status = AckStatus.REJECTED.value
                    transmission.error_message = (
                        mdn_parsed.error_details
                        or "MDN verification rejected or MIC mismatch"
                    )
                    transmission.mdn_disposition = mdn_parsed.disposition_raw
                    transmission.mdn_mic = mdn_parsed.received_content_mic
                    transmission.mdn_status = mdn_parsed.disposition_type.value

                    await write_as2_audit_log(
                        session=self.session,
                        transmission_id=transmission.id,
                        action="MDN_VERIFICATION_FAILED",
                        from_status=TransmissionStatus.TRANSMITTED.value,
                        to_status=TransmissionStatus.FAILED_MDN.value,
                        details=f"MDN verification failed: {transmission.error_message}",
                        created_by=user_id,
                        reason_for_change=change_reason,
                    )

            elif resp.status_code >= 400:
                transmission.status = TransmissionStatus.FAILED_TRANSMISSION.value
                transmission.error_message = (
                    f"Gateway returned HTTP {resp.status_code}: {resp.text[:300]}"
                )
                await write_as2_audit_log(
                    session=self.session,
                    transmission_id=transmission.id,
                    action="HTTP_ERROR_RECORDED",
                    from_status=TransmissionStatus.TRANSMITTED.value,
                    to_status=TransmissionStatus.FAILED_TRANSMISSION.value,
                    details=f"Gateway HTTP failure {resp.status_code}: {resp.text[:200]}",
                    created_by=user_id,
                    reason_for_change=change_reason,
                )
        except Exception as exc:
            transmission.status = TransmissionStatus.FAILED_TRANSMISSION.value
            transmission.error_message = f"Network/HTTP transport failure: {str(exc)}"
            await write_as2_audit_log(
                session=self.session,
                transmission_id=transmission.id,
                action="NETWORK_TRANSPORT_FAILURE",
                from_status=TransmissionStatus.TRANSMITTED.value,
                to_status=TransmissionStatus.FAILED_TRANSMISSION.value,
                details=f"AS2 HTTP dispatch error: {str(exc)}",
                created_by=user_id,
                reason_for_change=change_reason,
            )
        finally:
            if self.http_client is None:
                await client.aclose()

        await self.session.flush()

        return AS2TransmissionResult(
            transmission_id=transmission_id,
            message_id=message_id,
            status=TransmissionStatus(transmission.status),
            payload_digest_sha256=payload_digest_sha256,
            mic=mic,
            http_status_code=http_status,
            mdn_parsed=mdn_parsed,
            error_message=transmission.error_message,
        )

    async def process_inbound_mdn(
        self,
        mdn_raw: str | bytes,
        user_id: str,
        change_reason: str,
        authority_cert: str | None = None,
    ) -> MDNParseResult:
        """Process an asynchronous MDN received at callback webhook endpoint.

        Args:
            mdn_raw: Raw MDN body bytes or string.
            user_id: Authenticated receiving system user.
            change_reason: Audit change reason.
            authority_cert: Optional public certificate of sender.

        Returns:
            MDNParseResult with verification outcomes.
        """
        # Parse initial MDN to obtain Original-Message-ID
        initial_parse = MDNParser.parse_raw_mdn(mdn_raw)
        orig_msg_id = initial_parse.original_message_id

        if not orig_msg_id:
            logger.warning("Inbound MDN missing Original-Message-ID header")
            return initial_parse

        clean_id = orig_msg_id.strip("<>")
        query = select(AS2Transmission).where(
            (AS2Transmission.message_id == orig_msg_id)
            | (AS2Transmission.message_id == f"<{clean_id}>")
        )
        result = await self.session.execute(query)
        transmission = result.scalars().first()

        if not transmission:
            logger.warning(
                f"No matching AS2 transmission found for MDN Original-Message-ID: {orig_msg_id}"
            )
            return initial_parse

        # Verify MIC against stored transmission MIC
        mdn_parsed = MDNParser.parse_raw_mdn(
            mdn_raw=mdn_raw,
            expected_message_id=transmission.message_id,
            expected_mic=transmission.mic,
            authority_cert=authority_cert,
        )

        from_status = transmission.status
        if (
            mdn_parsed.is_valid_mdn
            and mdn_parsed.mic_matches is True
            and mdn_parsed.disposition_type
            in (MDNDispositionType.PROCESSED, MDNDispositionType.PROCESSED_WARNING)
        ):
            transmission.status = TransmissionStatus.ACK_A_RECEIVED.value
            transmission.ack_a_status = AckStatus.ACCEPTED.value
            transmission.ack_a_received_at = datetime.now(UTC)
            transmission.mdn_message_id = mdn_parsed.original_message_id or orig_msg_id
            transmission.mdn_disposition = mdn_parsed.disposition_raw
            transmission.mdn_mic = mdn_parsed.received_content_mic
            transmission.mdn_status = mdn_parsed.disposition_type.value

            await write_as2_audit_log(
                session=self.session,
                transmission_id=transmission.id,
                action="ASYNC_MDN_VERIFIED_ACK_A",
                from_status=from_status,
                to_status=TransmissionStatus.ACK_A_RECEIVED.value,
                details=f"Asynchronous MDN verified successfully: MIC matched. Disposition: {mdn_parsed.disposition_raw}",
                created_by=user_id,
                reason_for_change=change_reason,
            )
        else:
            transmission.status = TransmissionStatus.FAILED_MDN.value
            transmission.ack_a_status = AckStatus.REJECTED.value
            transmission.error_message = (
                mdn_parsed.error_details or "Asynchronous MDN rejected or MIC mismatch"
            )
            transmission.mdn_disposition = mdn_parsed.disposition_raw
            transmission.mdn_mic = mdn_parsed.received_content_mic
            transmission.mdn_status = mdn_parsed.disposition_type.value

            await write_as2_audit_log(
                session=self.session,
                transmission_id=transmission.id,
                action="ASYNC_MDN_FAILED",
                from_status=from_status,
                to_status=TransmissionStatus.FAILED_MDN.value,
                details=f"Asynchronous MDN failed: {transmission.error_message}",
                created_by=user_id,
                reason_for_change=change_reason,
            )

        await self.session.flush()
        return mdn_parsed

    async def process_inbound_ack(
        self,
        ack_payload: str,
        ack_type: AckType,
        user_id: str,
        change_reason: str,
        transmission_id: str | None = None,
    ) -> RegulatoryAckParseResult:
        """Process inbound Regulatory ACK B (syntactic) or ACK C (authority acceptance).

        Args:
            ack_payload: XML acknowledgement payload.
            ack_type: AckType.ACK_B or AckType.ACK_C.
            user_id: Authenticated user.
            change_reason: GxP change justification reason.
            transmission_id: Optional explicit transmission ID to link.

        Returns:
            RegulatoryAckParseResult with parsing and ledger transition status.
        """
        ack_result = RegulatoryAckParser.parse_ack_payload(
            payload_content=ack_payload, ack_type=ack_type
        )

        # Locate transmission record
        transmission: AS2Transmission | None = None
        if transmission_id:
            query = select(AS2Transmission).where(
                (AS2Transmission.transmission_id == transmission_id)
                | (AS2Transmission.id == transmission_id)
            )
            res = await self.session.execute(query)
            transmission = res.scalars().first()

        if not transmission and ack_result.message_id:
            clean_id = ack_result.message_id.strip("<>")
            query = select(AS2Transmission).where(
                (AS2Transmission.message_id == ack_result.message_id)
                | (AS2Transmission.message_id == f"<{clean_id}>")
            )
            res = await self.session.execute(query)
            transmission = res.scalars().first()

        if not transmission and ack_result.worldwide_unique_case_id:
            query = (
                select(AS2Transmission)
                .where(
                    AS2Transmission.worldwide_unique_case_id
                    == ack_result.worldwide_unique_case_id
                )
                .order_by(AS2Transmission.created_at.desc())
            )
            res = await self.session.execute(query)
            transmission = res.scalars().first()

        if not transmission:
            logger.warning(
                f"No matching AS2 transmission found for {ack_type.value} (Message-ID: {ack_result.message_id}, Case: {ack_result.worldwide_unique_case_id})"
            )
            return ack_result

        from_status = transmission.status

        if ack_type == AckType.ACK_B:
            transmission.ack_b_status = ack_result.ack_status.value
            transmission.ack_b_received_at = datetime.now(UTC)
            transmission.ack_b_errors = [{"error": err} for err in ack_result.errors]

            if ack_result.is_accepted:
                transmission.status = TransmissionStatus.ACK_B_RECEIVED.value
                action_name = "ACK_B_SYNTACTIC_VALIDATION_PASSED"
                details_msg = f"ACK B technical validation passed: Code {ack_result.authority_status_code or 'AA'}"
            else:
                transmission.status = TransmissionStatus.FAILED_ACK_B.value
                transmission.error_message = (
                    "; ".join(ack_result.errors)
                    or "ACK B Syntactic Validation Rejected"
                )
                action_name = "ACK_B_SYNTACTIC_VALIDATION_FAILED"
                details_msg = (
                    f"ACK B syntactic validation failed: {transmission.error_message}"
                )

            await write_as2_audit_log(
                session=self.session,
                transmission_id=transmission.id,
                action=action_name,
                from_status=from_status,
                to_status=transmission.status,
                details=details_msg,
                created_by=user_id,
                reason_for_change=change_reason,
            )

        elif ack_type == AckType.ACK_C:
            transmission.ack_c_status = ack_result.ack_status.value
            transmission.ack_c_received_at = datetime.now(UTC)
            transmission.ack_c_details = {
                "receipt_id": ack_result.authority_receipt_id,
                "status_code": ack_result.authority_status_code,
                "warnings": ack_result.warnings,
                "details": ack_result.details,
            }

            if ack_result.is_accepted:
                transmission.status = TransmissionStatus.ACK_C_RECEIVED.value
                action_name = "ACK_C_AUTHORITY_ACCEPTED"
                details_msg = f"Regulatory Authority accepted case. Tracking ID: {ack_result.authority_receipt_id or 'N/A'}"
            else:
                transmission.status = TransmissionStatus.FAILED_ACK_C.value
                transmission.error_message = (
                    "; ".join(ack_result.errors) or "ACK C Authority Ingestion Rejected"
                )
                action_name = "ACK_C_AUTHORITY_REJECTED"
                details_msg = f"Authority rejected case: {transmission.error_message}"

            await write_as2_audit_log(
                session=self.session,
                transmission_id=transmission.id,
                action=action_name,
                from_status=from_status,
                to_status=transmission.status,
                details=details_msg,
                created_by=user_id,
                reason_for_change=change_reason,
            )

        await self.session.flush()
        return ack_result
