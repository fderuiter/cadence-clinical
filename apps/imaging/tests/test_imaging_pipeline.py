"""Unit and integration tests for Medical Imaging ingestion pipeline.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

import io
import zipfile

import numpy as np
import pydicom
import pytest
from pydicom.dataset import FileDataset, FileMetaDataset
from sqlalchemy import select

from apps.imaging.adapters.models import (
    DeidentificationCertificateModel,
    ImagingInstanceEnvelopeModel,
    ImagingSeriesEnvelopeModel,
)
from apps.imaging.services.pipeline import ImagingIngestionPipeline
from packages.deid.dicom import DicomDeidentifier


def make_test_dicom(
    study_uid: str = "1.2.840.113619.2.55.3.100",
    series_uid: str = "1.2.840.113619.2.55.3.100.1",
    sop_uid: str = "1.2.840.113619.2.55.3.100.1.1",
    modality: str = "CT",
    patient_name: str = "DOE^JOHN",
    patient_id: str = "MRN-001122",
    burned_in: str = "NO",
) -> bytes:
    """Creates synthetic DICOM bytes for pipeline testing."""
    file_meta = FileMetaDataset()
    file_meta.MediaStorageSOPClassUID = "1.2.840.10008.5.1.4.1.1.2"
    file_meta.MediaStorageSOPInstanceUID = sop_uid
    file_meta.TransferSyntaxUID = pydicom.uid.ExplicitVRLittleEndian

    bio = io.BytesIO()
    ds = FileDataset(bio, {}, file_meta=file_meta, preamble=b"\0" * 128)
    ds.PatientName = patient_name
    ds.PatientID = patient_id
    ds.PatientBirthDate = "19800520"
    ds.StudyInstanceUID = study_uid
    ds.SeriesInstanceUID = series_uid
    ds.SOPInstanceUID = sop_uid
    ds.SOPClassUID = "1.2.840.10008.5.1.4.1.1.2"
    ds.Modality = modality
    ds.BurnedInAnnotation = burned_in
    ds.SeriesNumber = 1
    ds.InstanceNumber = 1
    ds.StudyDate = "20260301"
    ds.SeriesDescription = "Chest Contrast CT"
    ds.StudyDescription = "Oncology Staging Scan"
    ds.Rows = 64
    ds.Columns = 64
    ds.BitsAllocated = 16
    ds.BitsStored = 16
    ds.HighBit = 15
    ds.PixelRepresentation = 0
    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.PixelSpacing = [0.8, 0.8]
    ds.SliceThickness = 1.25
    ds.SliceLocation = 10.0

    arr = np.full((64, 64), 300, dtype=np.uint16)
    ds.PixelData = arr.tobytes()
    ds.save_as(bio, enforce_file_format=True)
    return bio.getvalue()


@pytest.mark.asyncio
async def test_single_dicom_ingestion_pipeline(db_session, test_env):
    """Validate full end-to-end ingestion of a single DICOM slice.

    @req:PRD-IMG-001
    @req:PRD-IMG-002
    @req:PRD-SYS-164
    """
    raw_dcm = make_test_dicom()
    storage_provider = test_env["storage"]
    deid = DicomDeidentifier(signing_secret=b"test-imaging-audit-secret-key-999")
    pipeline = ImagingIngestionPipeline(
        storage_port=storage_provider, deidentifier=deid
    )

    study_model, cert = await pipeline.ingest_dicom_bytes(
        dicom_bytes=raw_dcm,
        study_id="TRIAL-PHASE3-001",
        site_id="SITE-101",
        subject_id="SUBJ-789",
        user_id="crc.tester",
        reason_for_change="Baseline protocol CT scan upload",
        session=db_session,
        tenant_id="tenant_alpha",
    )
    await db_session.commit()

    assert study_model.id is not None
    assert study_model.study_id == "TRIAL-PHASE3-001"
    assert study_model.site_id == "SITE-101"
    assert study_model.subject_id == "SUBJ-789"
    assert study_model.instance_count == 1
    assert study_model.series_count == 1
    assert "CT" in study_model.modalities

    # Verify certificate signature
    assert deid.verify_certificate(cert) is True

    # Verify object storage persistence
    instances_stmt = select(ImagingInstanceEnvelopeModel)
    instances = (await db_session.execute(instances_stmt)).scalars().all()
    assert len(instances) == 1
    inst = instances[0]

    assert await storage_provider.exists(inst.object_key) is True
    stored_bytes, sha = await storage_provider.get_object(inst.object_key)
    assert sha == cert.target_sha256

    # Verify stored file is de-identified
    stored_ds = pydicom.dcmread(io.BytesIO(stored_bytes), force=True)
    assert stored_ds.PatientID == "SUBJ-789"
    assert stored_ds.PatientName == "SUBJ^SUBJ-789"
    assert not hasattr(stored_ds, "PatientBirthDate")


@pytest.mark.asyncio
async def test_zip_archive_multi_slice_ingestion(db_session, test_env):
    """Validate ingestion of a multi-slice DICOM series packaged as a ZIP archive.

    @req:PRD-IMG-001
    @req:PRD-IMG-002
    @req:PRD-SYS-164
    """
    zip_bio = io.BytesIO()
    with zipfile.ZipFile(zip_bio, "w") as zf:
        for idx in range(1, 4):
            slice_bytes = make_test_dicom(
                sop_uid=f"1.2.840.113619.2.55.3.100.1.{idx}",
            )
            zf.writestr(f"slice_{idx}.dcm", slice_bytes)

    archive_bytes = zip_bio.getvalue()
    storage_provider = test_env["storage"]
    deid = DicomDeidentifier(signing_secret=b"test-imaging-audit-secret-key-999")
    pipeline = ImagingIngestionPipeline(
        storage_port=storage_provider, deidentifier=deid
    )

    study_model, certs = await pipeline.ingest_archive_or_file(
        payload_bytes=archive_bytes,
        filename="ct_series.zip",
        study_id="TRIAL-PHASE3-001",
        site_id="SITE-101",
        subject_id="SUBJ-789",
        user_id="crc.tester",
        reason_for_change="Multi-slice series upload",
        session=db_session,
    )
    await db_session.commit()

    assert len(certs) == 3
    assert study_model.instance_count == 3

    # Check relational envelopes
    instances = (
        (await db_session.execute(select(ImagingInstanceEnvelopeModel))).scalars().all()
    )
    assert len(instances) == 3

    series_list = (
        (await db_session.execute(select(ImagingSeriesEnvelopeModel))).scalars().all()
    )
    assert len(series_list) == 1
    assert series_list[0].instance_count == 3

    # Verify all certificates in DB
    db_certs = (
        (await db_session.execute(select(DeidentificationCertificateModel)))
        .scalars()
        .all()
    )
    assert len(db_certs) == 3
    for c in db_certs:
        assert c.subject_id == "SUBJ-789"
        assert c.site_id == "SITE-101"
