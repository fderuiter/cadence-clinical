"""DICOM In-Flight De-Identification and Pixel PHI Masking Engine.

Implements HIPAA Safe Harbor (45 CFR 164.514(b)(2)), DICOM PS 3.15 Annex E
Confidentiality Profile, and GxP 21 CFR Part 11 cryptographic certificate manifests.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

import contextlib
import hashlib
import io
import os
import uuid
from datetime import UTC, datetime, timedelta

import pydicom
from pydantic import BaseModel, Field
from pydicom.dataset import Dataset, FileDataset
from pydicom.sequence import Sequence

from packages.security.signing import (
    generate_canonical_signature,
    verify_canonical_signature,
)

# DICOM UID Root for Cadence Clinical Research (ISO / OID namespace)
CADENCE_DICOM_UID_ROOT = "1.2.826.0.1.3680043.10.99."

# Comprehensive HIPAA Safe Harbor / DICOM PS 3.15 Annex E Header Tags to Strip or Blank
TAGS_TO_REMOVE = [
    # Patient Demographics & Identifiers
    "PatientBirthDate",
    "PatientBirthTime",
    "PatientSex",
    "PatientAge",
    "PatientWeight",
    "PatientSize",
    "PatientAddress",
    "PatientMotherBirthName",
    "PatientTelephoneNumbers",
    "EthnicGroup",
    "OtherPatientIDs",
    "OtherPatientIDsSequence",
    "OtherPatientNames",
    "IssuerOfPatientID",
    "MedicalRecordLocator",
    "MilitaryRank",
    "BranchOfService",
    "PatientComments",
    "SpecialNeeds",
    "PatientState",
    "Occupation",
    "AdditionalPatientHistory",
    "PatientReligiousPreference",
    # Institution & Hardware Identifiers
    "InstitutionAddress",
    "InstitutionalDepartmentName",
    "StationName",
    "DeviceSerialNumber",
    "InstitutionalDepartmentTypeCodeSequence",
    # Physicians, Staff & Operators
    "ReferringPhysicianName",
    "ReferringPhysicianAddress",
    "ReferringPhysicianTelephoneNumbers",
    "ReferringPhysicianIdentificationSequence",
    "PerformingPhysicianName",
    "PerformingPhysicianIdentificationSequence",
    "PhysicianOfRecord",
    "PhysicianOfRecordIdentificationSequence",
    "ConsultingPhysicianName",
    "ConsultingPhysicianIdentificationSequence",
    "NameOfPhysiciansReadingStudy",
    "PhysiciansReadingStudyIdentificationSequence",
    "OperatorsName",
    "OperatorIdentificationSequence",
    "RequestingPhysician",
    "OrderCallbackPhoneNumber",
    # Order & Episode Information
    "AccessionNumber",
    "IssuerOfAccessionNumberSequence",
    "RequestedProcedureID",
    "ScheduledProcedureStepID",
    "ScheduledProcedureStepDescription",
    "RequestedProcedureDescription",
    "StudyID",
    "FillerOrderNumberImagingServiceRequest",
    "PlacerOrderNumberImagingServiceRequest",
    # Descriptions that frequently harbor patient names
    "StudyDescription",
    "SeriesDescription",
]

DATE_TAGS = [
    "StudyDate",
    "SeriesDate",
    "AcquisitionDate",
    "ContentDate",
    "OverlayDate",
    "CurveDate",
]

TIME_TAGS = [
    "StudyTime",
    "SeriesTime",
    "AcquisitionTime",
    "ContentTime",
    "OverlayTime",
    "CurveTime",
]


class PixelMaskBoundingBox(BaseModel):
    """Bounding box coordinates for burned-in pixel PHI masking."""

    x_min: int = Field(..., description="Top-left X coordinate (0-indexed column)")
    y_min: int = Field(..., description="Top-left Y coordinate (0-indexed row)")
    x_max: int = Field(..., description="Bottom-right X coordinate (column)")
    y_max: int = Field(..., description="Bottom-right Y coordinate (row)")


class DicomDeidCertificate(BaseModel):
    """Immutable, tamper-evident cryptographic de-identification certificate."""

    certificate_id: str = Field(
        default_factory=lambda: str(uuid.uuid4()),
        description="Unique UUID for this de-identification event",
    )
    source_sha256: str = Field(..., description="SHA-256 digest of source DICOM binary")
    target_sha256: str = Field(
        ..., description="SHA-256 digest of de-identified target DICOM binary"
    )
    study_id: str = Field(..., description="Clinical Trial Protocol ID")
    site_id: str = Field(..., description="Clinical Site ID / Code")
    subject_id: str = Field(..., description="Clinical Trial Subject UUID")
    original_study_instance_uid_hash: str = Field(
        ..., description="One-way hash of original StudyInstanceUID"
    )
    original_sop_instance_uid_hash: str = Field(
        ..., description="One-way hash of original SOPInstanceUID"
    )
    anonymized_study_instance_uid: str = Field(
        ..., description="Generated anonymized StudyInstanceUID"
    )
    anonymized_sop_instance_uid: str = Field(
        ..., description="Generated anonymized SOPInstanceUID"
    )
    scrubbed_tags_count: int = Field(
        ..., description="Total count of scrubbed or replaced DICOM header tags"
    )
    scrubbed_tags: list[str] = Field(
        default_factory=list,
        description="List of scrubbed or pseudonymized tag keywords",
    )
    pixel_mask_applied: bool = Field(
        default=False,
        description="Whether burned-in pixel PHI masking was executed",
    )
    pixel_masks: list[PixelMaskBoundingBox] = Field(
        default_factory=list,
        description="List of bounding boxes masked during pixel de-identification",
    )
    operator_identity: str = Field(
        default="cadence.imaging.pipeline",
        description="Identity of the operator or service executing de-identification",
    )
    timestamp: str = Field(
        default_factory=lambda: datetime.now(UTC).isoformat(),
        description="ISO 8601 UTC timestamp of the de-identification operation",
    )
    signature: str | None = Field(
        None, description="HMAC-SHA256 cryptographic signature"
    )


class DicomDeidResult(BaseModel):
    """Result container of the DICOM in-flight de-identification process."""

    deidentified_bytes: bytes = Field(
        ..., description="De-identified output DICOM binary bytes"
    )
    certificate: DicomDeidCertificate = Field(
        ..., description="Signed cryptographic de-identification certificate"
    )
    anonymized_study_uid: str = Field(..., description="Anonymized StudyInstanceUID")
    anonymized_series_uid: str = Field(..., description="Anonymized SeriesInstanceUID")
    anonymized_sop_uid: str = Field(..., description="Anonymized SOPInstanceUID")
    modality: str = Field(..., description="DICOM Modality (e.g. CT, MR, US, DX)")
    rows: int = Field(default=0, description="Image pixel matrix row dimension")
    columns: int = Field(default=0, description="Image pixel matrix column dimension")
    bits_allocated: int = Field(default=16, description="Bit depth of image pixel data")
    pixel_spacing: list[float] = Field(
        default_factory=list, description="Row and column physical pixel spacing in mm"
    )
    slice_thickness: float | None = Field(
        default=None, description="Nominal slice thickness in mm"
    )
    slice_location: float | None = Field(
        default=None, description="Relative slice location in mm"
    )
    window_center: float | None = Field(
        default=None, description="Default window center / level"
    )
    window_width: float | None = Field(default=None, description="Default window width")


class DicomDeidentifier:
    """Enterprise DICOM in-flight de-identification and pixel PHI masking engine.

    Fulfills HIPAA Safe Harbor, DICOM PS 3.15 Annex E, and 21 CFR Part 11 requirements.
    """

    def __init__(self, signing_secret: bytes | None = None) -> None:
        from packages.config import get_common_settings

        self.signing_secret = (
            signing_secret
            or get_common_settings().audit_log_secret_key.encode()
        )

    def _generate_uid(self, original_uid: str, namespace_salt: str) -> str:
        """Derives a deterministic, standards-compliant DICOM UID from an original UID.

        Result is guaranteed <= 64 characters and consists only of digits and dots.
        """
        combined = f"{original_uid}:{namespace_salt}".encode()
        hash_digest = hashlib.sha256(combined).hexdigest()
        num_str = str(int(hash_digest[:32], 16))
        full_uid = f"{CADENCE_DICOM_UID_ROOT}{num_str}"
        return full_uid[:64]

    def _shift_dicom_date(self, date_str: str, days: int) -> str:
        """Shifts a DICOM DA tag (YYYYMMDD) by a specified number of days."""
        if not date_str or len(date_str) != 8:
            return date_str
        try:
            dt = datetime.strptime(date_str, "%Y%m%d")
            shifted = dt + timedelta(days=days)
            return shifted.strftime("%Y%m%d")
        except Exception:
            return date_str

    def mask_burned_in_pixels(
        self,
        dataset: Dataset,
        custom_masks: list[PixelMaskBoundingBox] | None = None,
        auto_detect_headers: bool = True,
    ) -> list[PixelMaskBoundingBox]:
        """Detects and blanks burned-in pixel text annotations in pixel data.

        Operates in-place on dataset pixel arrays and re-encodes PixelData.
        Returns the list of applied bounding boxes.
        """
        applied_masks: list[PixelMaskBoundingBox] = []

        if not hasattr(dataset, "PixelData") or dataset.PixelData is None:
            return applied_masks

        try:
            arr = dataset.pixel_array.copy()
        except Exception:
            return applied_masks

        rows = int(dataset.Rows) if hasattr(dataset, "Rows") else arr.shape[0]
        cols = int(dataset.Columns) if hasattr(dataset, "Columns") else arr.shape[1]
        modality = str(getattr(dataset, "Modality", "")).upper()
        burned_in_header = str(getattr(dataset, "BurnedInAnnotation", "")).upper()

        masks_to_apply: list[PixelMaskBoundingBox] = []

        if auto_detect_headers and (
            modality in ("US", "SC", "OT") or burned_in_header == "YES"
        ):
            top_bar_height = max(1, int(rows * 0.12))
            masks_to_apply.append(
                PixelMaskBoundingBox(x_min=0, y_min=0, x_max=cols, y_max=top_bar_height)
            )

        if custom_masks:
            masks_to_apply.extend(custom_masks)

        if not masks_to_apply:
            return applied_masks

        for box in masks_to_apply:
            x0 = max(0, min(box.x_min, cols))
            x1 = max(0, min(box.x_max, cols))
            y0 = max(0, min(box.y_min, rows))
            y1 = max(0, min(box.y_max, rows))

            if x1 <= x0 or y1 <= y0:
                continue

            if arr.ndim == 2:
                arr[y0:y1, x0:x1] = 0
            elif arr.ndim == 3 and arr.shape[2] in (3, 4):
                arr[y0:y1, x0:x1, :] = 0
            elif arr.ndim == 3:
                arr[:, y0:y1, x0:x1] = 0
            elif arr.ndim == 4:
                arr[:, y0:y1, x0:x1, :] = 0

            applied_masks.append(box)

        dataset.PixelData = arr.tobytes()
        dataset.BurnedInAnnotation = "NO"
        return applied_masks

    def deidentify(
        self,
        dicom_bytes: bytes,
        subject_id: str,
        site_id: str,
        study_id: str,
        date_shift_days: int = 0,
        apply_pixel_mask: bool = True,
        custom_pixel_masks: list[PixelMaskBoundingBox] | None = None,
        operator_identity: str = "cadence.imaging.pipeline",
    ) -> DicomDeidResult:
        """Executes comprehensive in-flight DICOM de-identification on binary bytes."""
        source_sha256 = hashlib.sha256(dicom_bytes).hexdigest()

        bio = io.BytesIO(dicom_bytes)
        dataset: FileDataset = pydicom.dcmread(bio, force=True)

        scrubbed_tags: list[str] = []

        orig_study_uid = str(getattr(dataset, "StudyInstanceUID", str(uuid.uuid4())))
        orig_series_uid = str(getattr(dataset, "SeriesInstanceUID", str(uuid.uuid4())))
        orig_sop_uid = str(getattr(dataset, "SOPInstanceUID", str(uuid.uuid4())))
        orig_for_uid = str(getattr(dataset, "FrameOfReferenceUID", ""))

        orig_study_hash = hashlib.sha256(orig_study_uid.encode("utf-8")).hexdigest()
        orig_sop_hash = hashlib.sha256(orig_sop_uid.encode("utf-8")).hexdigest()

        new_study_uid = self._generate_uid(orig_study_uid, f"study:{study_id}")
        new_series_uid = self._generate_uid(orig_series_uid, f"series:{study_id}")
        new_sop_uid = self._generate_uid(orig_sop_uid, f"sop:{study_id}:{subject_id}")

        dataset.StudyInstanceUID = new_study_uid
        dataset.SeriesInstanceUID = new_series_uid
        dataset.SOPInstanceUID = new_sop_uid

        if orig_for_uid:
            dataset.FrameOfReferenceUID = self._generate_uid(
                orig_for_uid, f"for:{study_id}"
            )

        if hasattr(dataset, "file_meta") and dataset.file_meta is not None:
            dataset.file_meta.MediaStorageSOPInstanceUID = new_sop_uid

        scrubbed_tags.extend(
            ["StudyInstanceUID", "SeriesInstanceUID", "SOPInstanceUID"]
        )

        for tag in TAGS_TO_REMOVE:
            if hasattr(dataset, tag):
                delattr(dataset, tag)
                scrubbed_tags.append(tag)

        dataset.remove_private_tags()
        scrubbed_tags.append("PrivateTagsGroup")

        if date_shift_days != 0:
            for d_tag in DATE_TAGS:
                if hasattr(dataset, d_tag):
                    val = str(getattr(dataset, d_tag, ""))
                    shifted_val = self._shift_dicom_date(val, date_shift_days)
                    setattr(dataset, d_tag, shifted_val)
                    scrubbed_tags.append(d_tag)

        for t_tag in TIME_TAGS:
            if hasattr(dataset, t_tag):
                setattr(dataset, t_tag, "000000.000000")
                scrubbed_tags.append(t_tag)

        dataset.PatientID = subject_id
        dataset.PatientName = f"SUBJ^{subject_id}"
        dataset.InstitutionName = site_id
        dataset.StudyID = study_id
        dataset.ClinicalTrialSponsorName = "Cadence Clinical"
        dataset.ClinicalTrialProtocolID = study_id
        dataset.ClinicalTrialSiteID = site_id
        dataset.ClinicalTrialSubjectID = subject_id
        dataset.PatientIdentityRemoved = "YES"
        dataset.DeidentificationMethod = (
            "Cadence Clinical HIPAA Safe Harbor + DICOM PS 3.15 Annex E"
        )
        scrubbed_tags.extend(
            [
                "PatientID",
                "PatientName",
                "InstitutionName",
                "ClinicalTrialProtocolID",
                "ClinicalTrialSiteID",
                "ClinicalTrialSubjectID",
            ]
        )

        applied_masks: list[PixelMaskBoundingBox] = []
        if apply_pixel_mask:
            applied_masks = self.mask_burned_in_pixels(
                dataset=dataset,
                custom_masks=custom_pixel_masks,
                auto_detect_headers=True,
            )

        modality = str(getattr(dataset, "Modality", "OT")).upper()
        rows = int(getattr(dataset, "Rows", 0))
        cols = int(getattr(dataset, "Columns", 0))
        bits_allocated = int(getattr(dataset, "BitsAllocated", 16))

        pixel_spacing: list[float] = []
        if hasattr(dataset, "PixelSpacing") and dataset.PixelSpacing:
            with contextlib.suppress(Exception):
                pixel_spacing = [float(x) for x in dataset.PixelSpacing]

        slice_thickness: float | None = None
        if hasattr(dataset, "SliceThickness") and dataset.SliceThickness:
            with contextlib.suppress(Exception):
                slice_thickness = float(dataset.SliceThickness)

        slice_location: float | None = None
        if hasattr(dataset, "SliceLocation") and dataset.SliceLocation:
            with contextlib.suppress(Exception):
                slice_location = float(dataset.SliceLocation)

        window_center: float | None = None
        if hasattr(dataset, "WindowCenter") and dataset.WindowCenter:
            with contextlib.suppress(Exception):
                wc = dataset.WindowCenter
                window_center = float(wc[0] if isinstance(wc, (list, Sequence)) else wc)

        window_width: float | None = None
        if hasattr(dataset, "WindowWidth") and dataset.WindowWidth:
            with contextlib.suppress(Exception):
                ww = dataset.WindowWidth
                window_width = float(ww[0] if isinstance(ww, (list, Sequence)) else ww)

        out_bio = io.BytesIO()
        dataset.save_as(out_bio, enforce_file_format=True)
        out_bytes = out_bio.getvalue()
        target_sha256 = hashlib.sha256(out_bytes).hexdigest()

        cert = DicomDeidCertificate(
            source_sha256=source_sha256,
            target_sha256=target_sha256,
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            original_study_instance_uid_hash=orig_study_hash,
            original_sop_instance_uid_hash=orig_sop_hash,
            anonymized_study_instance_uid=new_study_uid,
            anonymized_sop_instance_uid=new_sop_uid,
            scrubbed_tags_count=len(set(scrubbed_tags)),
            scrubbed_tags=sorted(list(set(scrubbed_tags))),
            pixel_mask_applied=len(applied_masks) > 0,
            pixel_masks=applied_masks,
            operator_identity=operator_identity,
        )

        cert_dict = cert.model_dump(exclude={"signature"})
        cert.signature = generate_canonical_signature(cert_dict, self.signing_secret)

        return DicomDeidResult(
            deidentified_bytes=out_bytes,
            certificate=cert,
            anonymized_study_uid=new_study_uid,
            anonymized_series_uid=new_series_uid,
            anonymized_sop_uid=new_sop_uid,
            modality=modality,
            rows=rows,
            columns=cols,
            bits_allocated=bits_allocated,
            pixel_spacing=pixel_spacing,
            slice_thickness=slice_thickness,
            slice_location=slice_location,
            window_center=window_center,
            window_width=window_width,
        )

    def verify_certificate(self, cert: DicomDeidCertificate) -> bool:
        """Verifies the HMAC-SHA256 signature on a de-identification certificate."""
        if not cert.signature:
            return False
        payload = cert.model_dump(exclude={"signature"})
        return verify_canonical_signature(payload, cert.signature, self.signing_secret)
