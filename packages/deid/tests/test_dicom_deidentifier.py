"""Unit tests for DICOM in-flight de-identification and pixel PHI masking engine.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

import io

import numpy as np
import pydicom
from pydicom.dataset import FileDataset, FileMetaDataset

from packages.deid.dicom import (
    CADENCE_DICOM_UID_ROOT,
    DicomDeidentifier,
    PixelMaskBoundingBox,
)


def create_mock_dicom_bytes(
    modality: str = "CT",
    rows: int = 100,
    cols: int = 100,
    patient_name: str = "Smith^John^A",
    patient_id: str = "MRN-987654321",
    birth_date: str = "19750815",
    study_date: str = "20260115",
    institution: str = "City Hospital",
    physician: str = "Dr. Jane Doe",
    accession: str = "ACC-2026-001",
    burned_in_annotation: str = "NO",
    is_rgb: bool = False,
) -> bytes:
    """Helper to generate valid synthetic DICOM binary bytes for tests."""
    file_meta = FileMetaDataset()
    file_meta.MediaStorageSOPClassUID = "1.2.840.10008.5.1.4.1.1.2"  # CT Image Storage
    file_meta.MediaStorageSOPInstanceUID = "1.2.345.678.901.1"
    file_meta.TransferSyntaxUID = pydicom.uid.ExplicitVRLittleEndian

    bio = io.BytesIO()
    ds = FileDataset(bio, {}, file_meta=file_meta, preamble=b"\0" * 128)

    # Identifiable Patient Demographics
    ds.PatientName = patient_name
    ds.PatientID = patient_id
    ds.PatientBirthDate = birth_date
    ds.PatientSex = "M"
    ds.PatientAge = "051Y"
    ds.PatientAddress = "123 Main Street, Suite 400"
    ds.EthnicGroup = "NOT_SPECIFIED"

    # Institution & Hardware
    ds.InstitutionName = institution
    ds.InstitutionAddress = "456 Healthcare Blvd"
    ds.StationName = "CT_ROOM_4"
    ds.DeviceSerialNumber = "GE-REV-88492"

    # Physicians & Staff
    ds.ReferringPhysicianName = physician
    ds.PerformingPhysicianName = "Dr. Bob Roberts"
    ds.OperatorsName = "Tech Sarah"

    # Study & Episode
    ds.StudyInstanceUID = "1.2.840.113619.2.55.3.123456789"
    ds.SeriesInstanceUID = "1.2.840.113619.2.55.3.123456789.1"
    ds.SOPInstanceUID = "1.2.345.678.901.1"
    ds.SOPClassUID = "1.2.840.10008.5.1.4.1.1.2"
    ds.AccessionNumber = accession
    ds.StudyID = "STUDY-LOCAL-99"
    ds.StudyDate = study_date
    ds.SeriesDate = study_date
    ds.AcquisitionDate = study_date
    ds.ContentDate = study_date
    ds.StudyTime = "143022.000000"
    ds.Modality = modality
    ds.BurnedInAnnotation = burned_in_annotation
    ds.Rows = rows
    ds.Columns = cols
    ds.BitsAllocated = 16 if not is_rgb else 8
    ds.BitsStored = 16 if not is_rgb else 8
    ds.HighBit = 15 if not is_rgb else 7
    ds.PixelRepresentation = 0
    ds.PixelSpacing = [0.75, 0.75]
    ds.SliceThickness = 2.5
    ds.SliceLocation = -45.0
    ds.WindowCenter = 40
    ds.WindowWidth = 400

    # Private tag to verify private tag stripping
    block = ds.private_block(0x0029, "CADENCE_PRIVATE_BLOCK", create=True)
    block.add_new(0x01, "LO", "SECRET_PRIVATE_PHI_PAYLOAD")

    if is_rgb:
        ds.SamplesPerPixel = 3
        ds.PhotometricInterpretation = "RGB"
        ds.PlanarConfiguration = 0
        arr = np.full((rows, cols, 3), 200, dtype=np.uint8)
    else:
        ds.SamplesPerPixel = 1
        ds.PhotometricInterpretation = "MONOCHROME2"
        arr = np.full((rows, cols), 500, dtype=np.uint16)

    ds.PixelData = arr.tobytes()

    ds.save_as(bio, enforce_file_format=True)
    return bio.getvalue()


def test_dicom_deidentification_header_scrubbing():
    """Validate full HIPAA 18 header tag scrubbing and clinical trial metadata injection.

    @req:PRD-IMG-001
    @req:PRD-IMG-002
    """
    raw_bytes = create_mock_dicom_bytes(
        patient_name="John^Smith",
        patient_id="MRN-12345",
        institution="City General Hospital",
        physician="Dr. Alice Adams",
    )

    deidentifier = DicomDeidentifier(signing_secret=b"test-secret-key-12345")
    result = deidentifier.deidentify(
        dicom_bytes=raw_bytes,
        subject_id="SUBJ-UUID-101",
        site_id="SITE-901",
        study_id="PROTOCOL-ONC-301",
        date_shift_days=10,
        apply_pixel_mask=False,
    )

    assert result.deidentified_bytes is not None
    assert len(result.deidentified_bytes) > 0

    # Parse de-identified dataset
    ds = pydicom.dcmread(io.BytesIO(result.deidentified_bytes), force=True)

    # Injected Trial & Subject metadata
    assert ds.PatientID == "SUBJ-UUID-101"
    assert ds.PatientName == "SUBJ^SUBJ-UUID-101"
    assert ds.InstitutionName == "SITE-901"
    assert ds.StudyID == "PROTOCOL-ONC-301"
    assert ds.ClinicalTrialProtocolID == "PROTOCOL-ONC-301"
    assert ds.ClinicalTrialSiteID == "SITE-901"
    assert ds.ClinicalTrialSubjectID == "SUBJ-UUID-101"
    assert ds.PatientIdentityRemoved == "YES"
    assert "HIPAA Safe Harbor" in ds.DeidentificationMethod

    # Stripped HIPAA identifiers
    assert not hasattr(ds, "PatientBirthDate")
    assert not hasattr(ds, "PatientBirthTime")
    assert not hasattr(ds, "PatientAddress")
    assert not hasattr(ds, "ReferringPhysicianName")
    assert not hasattr(ds, "PerformingPhysicianName")
    assert not hasattr(ds, "OperatorsName")
    assert not hasattr(ds, "DeviceSerialNumber")
    assert not hasattr(ds, "AccessionNumber")
    assert not hasattr(ds, "StationName")

    # Date shifted
    assert ds.StudyDate == "20260125"  # 20260115 + 10 days
    assert ds.StudyTime == "000000.000000"

    # Private tags stripped
    private_elements = [elem for elem in ds if elem.tag.is_private]
    assert len(private_elements) == 0

    # UID Anonymization
    assert ds.StudyInstanceUID.startswith(CADENCE_DICOM_UID_ROOT)
    assert ds.SeriesInstanceUID.startswith(CADENCE_DICOM_UID_ROOT)
    assert ds.SOPInstanceUID.startswith(CADENCE_DICOM_UID_ROOT)
    assert ds.file_meta.MediaStorageSOPInstanceUID == ds.SOPInstanceUID


def test_dicom_deterministic_uid_generation():
    """Validate that identical input UIDs within the same study produce deterministic anonymized UIDs.

    @req:PRD-IMG-002
    """
    raw_bytes = create_mock_dicom_bytes()
    deidentifier = DicomDeidentifier(signing_secret=b"test-secret-key-12345")

    res1 = deidentifier.deidentify(
        dicom_bytes=raw_bytes,
        subject_id="SUBJ-001",
        site_id="SITE-01",
        study_id="STUDY-100",
    )
    res2 = deidentifier.deidentify(
        dicom_bytes=raw_bytes,
        subject_id="SUBJ-001",
        site_id="SITE-01",
        study_id="STUDY-100",
    )

    assert res1.anonymized_study_uid == res2.anonymized_study_uid
    assert res1.anonymized_series_uid == res2.anonymized_series_uid
    assert res1.anonymized_sop_uid == res2.anonymized_sop_uid


def test_burned_in_pixel_masking_ultrasound():
    """Validate automatic detection and black-out of burned-in header text on ultrasound images.

    @req:PRD-IMG-002
    """
    rows, cols = 100, 100
    us_bytes = create_mock_dicom_bytes(
        modality="US",
        rows=rows,
        cols=cols,
        burned_in_annotation="YES",
    )

    deidentifier = DicomDeidentifier(signing_secret=b"test-secret-key-12345")
    result = deidentifier.deidentify(
        dicom_bytes=us_bytes,
        subject_id="SUBJ-999",
        site_id="SITE-01",
        study_id="STUDY-US",
        apply_pixel_mask=True,
    )

    assert result.certificate.pixel_mask_applied is True
    assert len(result.certificate.pixel_masks) > 0

    # Parse and inspect pixel array
    ds = pydicom.dcmread(io.BytesIO(result.deidentified_bytes), force=True)
    arr = ds.pixel_array

    # Top header banner (top 12% rows) should be zeroed
    top_bar_height = int(rows * 0.12)
    assert np.all(arr[:top_bar_height, :] == 0)
    # Remaining lower portion should retain original values
    assert np.all(arr[top_bar_height:, :] == 500)
    assert ds.BurnedInAnnotation == "NO"


def test_burned_in_pixel_masking_custom_coordinates():
    """Validate custom coordinate masking for secondary capture annotations.

    @req:PRD-IMG-002
    """
    sc_bytes = create_mock_dicom_bytes(
        modality="SC",
        rows=200,
        cols=200,
        is_rgb=True,
    )

    custom_masks = [
        PixelMaskBoundingBox(x_min=10, y_min=10, x_max=50, y_max=30),
        PixelMaskBoundingBox(x_min=150, y_min=180, x_max=190, y_max=195),
    ]

    deidentifier = DicomDeidentifier(signing_secret=b"test-secret-key-12345")
    result = deidentifier.deidentify(
        dicom_bytes=sc_bytes,
        subject_id="SUBJ-777",
        site_id="SITE-02",
        study_id="STUDY-SC",
        apply_pixel_mask=True,
        custom_pixel_masks=custom_masks,
    )

    ds = pydicom.dcmread(io.BytesIO(result.deidentified_bytes), force=True)
    arr = ds.pixel_array

    # Check custom mask 1 zeroed
    assert np.all(arr[10:30, 10:50, :] == 0)
    # Check custom mask 2 zeroed
    assert np.all(arr[180:195, 150:190, :] == 0)


def test_cryptographic_certificate_manifest_and_tamper_detection():
    """Validate HMAC-SHA256 signature verification and tamper evidence on certificates.

    @req:PRD-IMG-002
    """
    secret = b"test-cryptographic-signing-key-gxp"
    raw_bytes = create_mock_dicom_bytes()
    deidentifier = DicomDeidentifier(signing_secret=secret)

    result = deidentifier.deidentify(
        dicom_bytes=raw_bytes,
        subject_id="SUBJ-123",
        site_id="SITE-01",
        study_id="STUDY-DEMO",
    )

    cert = result.certificate
    assert cert.signature is not None
    assert cert.source_sha256 != cert.target_sha256
    assert cert.scrubbed_tags_count > 0

    # Verification should succeed with original secret
    assert deidentifier.verify_certificate(cert) is True

    # Tampering with certificate fields must invalidate signature
    tampered_cert = cert.model_copy(deep=True)
    tampered_cert.subject_id = "TAMPERED-SUBJECT-999"
    assert deidentifier.verify_certificate(tampered_cert) is False

    # Verification must fail with wrong key
    wrong_key_deidentifier = DicomDeidentifier(signing_secret=b"wrong-key-xyz")
    assert wrong_key_deidentifier.verify_certificate(cert) is False
