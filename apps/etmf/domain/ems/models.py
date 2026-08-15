"""Pydantic domain models for DIA TMF Exchange Mechanism Standard (EMS).

Implements the standard schema for transferring eTMF content and metadata across
clinical trial repositories, sponsors, CROs, and regulatory auditors.
"""

import xml.etree.ElementTree as ET
from typing import Any

from defusedxml import minidom
from pydantic import BaseModel, ConfigDict, Field


class TmfEmsSignatureRecord(BaseModel):
    """Represents an electronic signature entry within an EMS package."""

    model_config = ConfigDict(frozen=True)

    signer_id: str = Field(..., description="Identity or username of the signer")
    signer_name: str | None = Field(None, description="Full display name of the signer")
    signing_reason: str = Field(..., description="Part 11 purpose of signature")
    timestamp: str = Field(..., description="ISO 8601 UTC signing timestamp")
    signature_digest: str | None = Field(
        None, description="Cryptographic signature digest or hash"
    )
    certificate_fingerprint: str | None = Field(
        None, description="X.509 certificate SHA-256 fingerprint"
    )


class TmfEmsVersion(BaseModel):
    """Represents a specific version of a document in an EMS package."""

    model_config = ConfigDict(frozen=True)

    version_index: int = Field(..., description="1-indexed document version number")
    status: str = Field(..., description="QC lifecycle status of this version")
    created_at: str = Field(..., description="ISO 8601 UTC creation timestamp")
    created_by: str = Field(..., description="User or system who authored this version")
    filename: str = Field(..., description="Original filename of this version")
    relative_path: str = Field(
        ..., description="Relative file path inside the ZIP archive"
    )
    mime_type: str = Field(..., description="MIME content type")
    sha256_checksum: str = Field(..., description="SHA-256 content digest")
    reason_for_change: str | None = Field(
        None, description="21 CFR Part 11 rationale for this version"
    )
    signatures: list[TmfEmsSignatureRecord] = Field(
        default_factory=list, description="Signatures attached to this version"
    )
    is_redacted: bool = Field(
        default=False, description="Whether this version is a redacted derivative"
    )
    redaction_source_id: str | None = Field(
        None, description="Source document ID if this version is redacted"
    )


class TmfEmsDocument(BaseModel):
    """Represents a TMF document entity in the EMS taxonomy."""

    model_config = ConfigDict(frozen=True)

    document_id: str = Field(..., description="Unique document UUID")
    study_id: str = Field(..., description="Clinical study protocol identifier")
    site_id: str | None = Field(
        None, description="Clinical site ID if site-level artifact"
    )
    zone_code: int = Field(..., description="DIA TMF Zone integer code (1-11)")
    zone_name: str = Field(..., description="DIA TMF Zone title")
    section_code: str = Field(..., description="DIA TMF Section code (e.g. 01.01)")
    section_name: str = Field(..., description="DIA TMF Section title")
    artifact_code: str = Field(..., description="DIA TMF Artifact code (e.g. 01.01.01)")
    artifact_name: str = Field(..., description="Canonical DIA TMF Artifact title")
    taxonomy_version: str = Field(
        ..., description="Taxonomy catalog version (e.g. v3.2.0)"
    )
    latest_status: str = Field(
        ..., description="Lifecycle status of latest document version"
    )
    issue_date: str | None = Field(None, description="Document issue date (YYYY-MM-DD)")
    expiration_date: str | None = Field(
        None, description="Document expiration date (ISO 8601)"
    )
    document_owner_id: str | None = Field(
        None, description="Owner or accountable role ID"
    )
    versions: list[TmfEmsVersion] = Field(
        default_factory=list, description="Historical versions of the document"
    )
    metadata: dict[str, Any] = Field(
        default_factory=dict, description="Custom or extended metadata attributes"
    )


class TmfEmsAuditRecord(BaseModel):
    """Represents an immutable audit log entry in the EMS package."""

    model_config = ConfigDict(frozen=True)

    id: str = Field(..., description="Audit log entry UUID")
    timestamp: str = Field(..., description="ISO 8601 UTC event timestamp")
    user_id: str = Field(..., description="User ID or masked identifier")
    user_role: str = Field(..., description="User role(s) at time of action")
    action: str = Field(..., description="Controlled action verb")
    document_id: str | None = Field(
        None, description="Associated document ID if applicable"
    )
    details: str = Field(..., description="Event details description")
    cryptographic_seal: str | None = Field(
        None, description="Merkle root or ledger seal hash"
    )


class TmfEmsPackage(BaseModel):
    """Top-level DIA TMF Exchange Mechanism Standard (EMS) exchange package."""

    model_config = ConfigDict(frozen=True)

    ems_version: str = Field(
        default="1.0", description="TMF Exchange Mechanism Standard version"
    )
    package_id: str = Field(..., description="Unique exchange package identifier")
    study_id: str = Field(..., description="Clinical study protocol identifier")
    study_title: str | None = Field(None, description="Clinical study formal title")
    source_system: str = Field(
        default="Cadence Clinical eTMF",
        description="Source electronic system producing the package",
    )
    export_timestamp: str = Field(..., description="ISO 8601 UTC package creation time")
    exported_by: str = Field(..., description="User or service initiating the export")
    exported_by_role: str = Field(
        ..., description="Role of the actor initiating the export"
    )
    document_count: int = Field(..., description="Total number of documents")
    version_count: int = Field(
        ..., description="Total number of document versions/files"
    )
    documents: list[TmfEmsDocument] = Field(
        default_factory=list, description="List of TMF documents"
    )
    audit_trail: list[TmfEmsAuditRecord] = Field(
        default_factory=list, description="Associated immutable audit trail"
    )

    def to_xml_string(self) -> str:
        """Serializes the EMS package to a standardized DIA TMF EMS XML document."""
        root = ET.Element(
            "TmfExchangePackage",
            {
                "version": self.ems_version,
                "packageId": self.package_id,
                "studyId": self.study_id,
                "exportTimestamp": self.export_timestamp,
                "sourceSystem": self.source_system,
            },
        )

        header = ET.SubElement(root, "Header")
        if self.study_title:
            ET.SubElement(header, "StudyTitle").text = self.study_title
        ET.SubElement(header, "ExportedBy").text = self.exported_by
        ET.SubElement(header, "ExportedByRole").text = self.exported_by_role
        ET.SubElement(header, "DocumentCount").text = str(self.document_count)
        ET.SubElement(header, "VersionCount").text = str(self.version_count)

        docs_elem = ET.SubElement(root, "Documents")
        for doc in self.documents:
            doc_elem = ET.SubElement(
                docs_elem,
                "Document",
                {
                    "id": doc.document_id,
                    "artifactCode": doc.artifact_code,
                    "zone": str(doc.zone_code),
                    "section": doc.section_code,
                    "taxonomyVersion": doc.taxonomy_version,
                },
            )
            ET.SubElement(doc_elem, "ArtifactName").text = doc.artifact_name
            ET.SubElement(doc_elem, "ZoneName").text = doc.zone_name
            ET.SubElement(doc_elem, "SectionName").text = doc.section_name
            if doc.site_id:
                ET.SubElement(doc_elem, "SiteId").text = doc.site_id
            if doc.issue_date:
                ET.SubElement(doc_elem, "IssueDate").text = doc.issue_date
            if doc.expiration_date:
                ET.SubElement(doc_elem, "ExpirationDate").text = doc.expiration_date
            if doc.document_owner_id:
                ET.SubElement(doc_elem, "DocumentOwner").text = doc.document_owner_id
            ET.SubElement(doc_elem, "LatestStatus").text = doc.latest_status

            versions_elem = ET.SubElement(doc_elem, "Versions")
            for ver in doc.versions:
                ver_elem = ET.SubElement(
                    versions_elem,
                    "Version",
                    {
                        "index": str(ver.version_index),
                        "status": ver.status,
                    },
                )
                ET.SubElement(ver_elem, "Filename").text = ver.filename
                ET.SubElement(ver_elem, "RelativePath").text = ver.relative_path
                ET.SubElement(ver_elem, "MimeType").text = ver.mime_type
                ET.SubElement(ver_elem, "Sha256Checksum").text = ver.sha256_checksum
                ET.SubElement(ver_elem, "CreatedAt").text = ver.created_at
                ET.SubElement(ver_elem, "CreatedBy").text = ver.created_by
                if ver.reason_for_change:
                    ET.SubElement(
                        ver_elem, "ReasonForChange"
                    ).text = ver.reason_for_change
                if ver.is_redacted:
                    ET.SubElement(
                        ver_elem,
                        "Redaction",
                        {"sourceId": ver.redaction_source_id or ""},
                    )

                if ver.signatures:
                    sigs_elem = ET.SubElement(ver_elem, "Signatures")
                    for sig in ver.signatures:
                        sig_elem = ET.SubElement(
                            sigs_elem,
                            "Signature",
                            {
                                "signerId": sig.signer_id,
                                "timestamp": sig.timestamp,
                            },
                        )
                        ET.SubElement(sig_elem, "Reason").text = sig.signing_reason
                        if sig.signer_name:
                            ET.SubElement(sig_elem, "SignerName").text = sig.signer_name
                        if sig.signature_digest:
                            ET.SubElement(
                                sig_elem, "SignatureDigest"
                            ).text = sig.signature_digest
                        if sig.certificate_fingerprint:
                            ET.SubElement(
                                sig_elem, "CertificateFingerprint"
                            ).text = sig.certificate_fingerprint

        audit_elem = ET.SubElement(root, "AuditTrail")
        for log in self.audit_trail:
            log_elem = ET.SubElement(
                audit_elem,
                "AuditEntry",
                {
                    "id": log.id,
                    "timestamp": log.timestamp,
                    "action": log.action,
                },
            )
            ET.SubElement(log_elem, "UserId").text = log.user_id
            ET.SubElement(log_elem, "UserRole").text = log.user_role
            if log.document_id:
                ET.SubElement(log_elem, "DocumentId").text = log.document_id
            ET.SubElement(log_elem, "Details").text = log.details
            if log.cryptographic_seal:
                ET.SubElement(
                    log_elem, "CryptographicSeal"
                ).text = log.cryptographic_seal

        rough_string = ET.tostring(root, encoding="utf-8")
        reparsed = minidom.parseString(rough_string)
        return reparsed.toprettyxml(indent="  ", encoding="utf-8").decode("utf-8")
