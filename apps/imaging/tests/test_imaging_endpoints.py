"""Integration tests for Medical Imaging REST API endpoints.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

import io
import json
import zipfile

import numpy as np
import pydicom
import pytest
from httpx import AsyncClient
from pydicom.dataset import FileDataset, FileMetaDataset


def create_sample_dicom(
    study_uid: str = "1.2.840.113619.2.55.3.200",
    series_uid: str = "1.2.840.113619.2.55.3.200.1",
    sop_uid: str = "1.2.840.113619.2.55.3.200.1.1",
    modality: str = "MR",
    patient_name: str = "Patient^Jane",
    patient_id: str = "MRN-554433",
) -> bytes:
    """Creates synthetic DICOM bytes for endpoint testing."""
    file_meta = FileMetaDataset()
    file_meta.MediaStorageSOPClassUID = "1.2.840.10008.5.1.4.1.1.4"  # MR Image Storage
    file_meta.MediaStorageSOPInstanceUID = sop_uid
    file_meta.TransferSyntaxUID = pydicom.uid.ExplicitVRLittleEndian

    bio = io.BytesIO()
    ds = FileDataset(bio, {}, file_meta=file_meta, preamble=b"\0" * 128)
    ds.PatientName = patient_name
    ds.PatientID = patient_id
    ds.PatientBirthDate = "19900214"
    ds.StudyInstanceUID = study_uid
    ds.SeriesInstanceUID = series_uid
    ds.SOPInstanceUID = sop_uid
    ds.SOPClassUID = "1.2.840.10008.5.1.4.1.1.4"
    ds.Modality = modality
    ds.SeriesNumber = 2
    ds.InstanceNumber = 1
    ds.StudyDate = "20260410"
    ds.SeriesDescription = "Brain T1 Axial"
    ds.StudyDescription = "Brain MRI Screening"
    ds.Rows = 128
    ds.Columns = 128
    ds.BitsAllocated = 16
    ds.BitsStored = 16
    ds.HighBit = 15
    ds.PixelRepresentation = 0
    ds.SamplesPerPixel = 1
    ds.PhotometricInterpretation = "MONOCHROME2"
    ds.PixelSpacing = [0.5, 0.5]
    ds.SliceThickness = 3.0

    arr = np.full((128, 128), 400, dtype=np.uint16)
    ds.PixelData = arr.tobytes()
    ds.save_as(bio, enforce_file_format=True)
    return bio.getvalue()


@pytest.mark.asyncio
async def test_health_check(client: AsyncClient):
    """Validate health check endpoint returns status ok.

    @req:PRD-IMG-001
    @req:PRD-SYS-164
    """
    resp = await client.get("/health")
    assert resp.status_code == 200
    data = resp.json()
    assert data["status"] == "ok"
    assert data["service"] == "imaging"


@pytest.mark.asyncio
async def test_upload_single_dicom_endpoint(client: AsyncClient):
    """Validate POST /api/v1/imaging/upload for a single DICOM file.

    @req:PRD-IMG-001
    @req:PRD-IMG-002
    @req:PRD-SYS-164
    """
    dcm_bytes = create_sample_dicom()

    files = {"file": ("brain_scan.dcm", dcm_bytes, "application/dicom")}
    data = {
        "study_id": "TRIAL-NEURO-202",
        "site_id": "SITE-BOSTON-01",
        "subject_id": "SUBJ-NEURO-001",
        "date_shift_days": "5",
        "apply_pixel_mask": "true",
    }

    resp = await client.post("/api/v1/imaging/upload", files=files, data=data)
    assert resp.status_code == 201, resp.text
    res_data = resp.json()

    assert res_data["study_id"] == "TRIAL-NEURO-202"
    assert res_data["site_id"] == "SITE-BOSTON-01"
    assert res_data["subject_id"] == "SUBJ-NEURO-001"
    assert res_data["processed_instances_count"] == 1
    assert "MR" in res_data["modalities"]
    assert len(res_data["certificates"]) == 1

    cert = res_data["certificates"][0]
    assert cert["signature"] is not None
    assert cert["subject_id"] == "SUBJ-NEURO-001"

    # Verify certificate via verification endpoint
    cert_id = cert["certificate_id"]
    verify_resp = await client.get(f"/api/v1/imaging/certificates/{cert_id}/verify")
    assert verify_resp.status_code == 200
    v_data = verify_resp.json()
    assert v_data["is_valid"] is True
    assert v_data["certificate_id"] == cert_id


@pytest.mark.asyncio
async def test_upload_zip_archive_endpoint(client: AsyncClient):
    """Validate POST /api/v1/imaging/upload for a ZIP archive containing multiple slices.

    @req:PRD-IMG-001
    @req:PRD-IMG-002
    @req:PRD-SYS-164
    """
    zip_bio = io.BytesIO()
    with zipfile.ZipFile(zip_bio, "w") as zf:
        for idx in range(1, 4):
            slice_bytes = create_sample_dicom(
                sop_uid=f"1.2.840.113619.2.55.3.200.1.{idx}",
            )
            zf.writestr(f"slice_{idx}.dcm", slice_bytes)

    files = {"file": ("volume.zip", zip_bio.getvalue(), "application/zip")}
    data = {
        "study_id": "TRIAL-NEURO-202",
        "site_id": "SITE-BOSTON-01",
        "subject_id": "SUBJ-NEURO-002",
    }

    resp = await client.post("/api/v1/imaging/upload", files=files, data=data)
    assert resp.status_code == 201
    res_data = resp.json()

    assert res_data["processed_instances_count"] == 3
    assert res_data["processed_series_count"] == 1


@pytest.mark.asyncio
async def test_list_and_get_studies_endpoints(client: AsyncClient):
    """Validate GET /api/v1/imaging/studies and GET /api/v1/imaging/studies/{study_uid}.

    @req:PRD-IMG-001
    @req:PRD-SYS-164
    """
    # First upload a study
    dcm_bytes = create_sample_dicom()
    files = {"file": ("scan.dcm", dcm_bytes, "application/dicom")}
    data = {
        "study_id": "TRIAL-LIST-TEST",
        "site_id": "SITE-202",
        "subject_id": "SUBJ-999",
    }
    upload_resp = await client.post("/api/v1/imaging/upload", files=files, data=data)
    assert upload_resp.status_code == 201
    study_uid = upload_resp.json()["study_instance_uid"]

    # List studies with filter
    list_resp = await client.get(
        "/api/v1/imaging/studies",
        params={"study_id": "TRIAL-LIST-TEST", "subject_id": "SUBJ-999"},
    )
    assert list_resp.status_code == 200
    studies = list_resp.json()
    assert len(studies) >= 1
    assert any(s["study_instance_uid"] == study_uid for s in studies)

    # Get study detail
    detail_resp = await client.get(f"/api/v1/imaging/studies/{study_uid}")
    assert detail_resp.status_code == 200
    detail = detail_resp.json()
    assert detail["study_instance_uid"] == study_uid
    assert len(detail["series"]) == 1
    assert len(detail["series"][0]["instances"]) == 1

    sop_instance_uid = detail["series"][0]["instances"][0]["sop_instance_uid"]

    # Download instance file
    download_resp = await client.get(
        f"/api/v1/imaging/instances/{sop_instance_uid}/file"
    )
    assert download_resp.status_code == 200
    assert download_resp.headers["content-type"] == "application/dicom"

    # Verify downloaded file is de-identified
    down_ds = pydicom.dcmread(io.BytesIO(download_resp.content), force=True)
    assert down_ds.PatientID == "SUBJ-999"

    # Presigned URL endpoint
    url_resp = await client.get(
        f"/api/v1/imaging/instances/{sop_instance_uid}/presigned-url"
    )
    assert url_resp.status_code == 200
    assert "download_url" in url_resp.json()


@pytest.mark.asyncio
async def test_in_flight_deidentify_stream_endpoint(client: AsyncClient):
    """Validate POST /api/v1/imaging/deidentify returns in-memory sanitized stream.

    @req:PRD-IMG-002
    @req:PRD-SYS-164
    """
    raw_dcm = create_sample_dicom(patient_name="SECRET^PATIENT")
    files = {"file": ("in_flight.dcm", raw_dcm, "application/dicom")}
    data = {
        "study_id": "TRIAL-STREAM-01",
        "site_id": "SITE-STREAM",
        "subject_id": "SUBJ-STREAM-88",
    }

    resp = await client.post("/api/v1/imaging/deidentify", files=files, data=data)
    assert resp.status_code == 200
    assert resp.headers["content-type"] == "application/dicom"
    assert "X-Deid-Certificate-Id" in resp.headers

    deid_ds = pydicom.dcmread(io.BytesIO(resp.content), force=True)
    assert deid_ds.PatientID == "SUBJ-STREAM-88"
    assert deid_ds.PatientName == "SUBJ^SUBJ-STREAM-88"


@pytest.mark.asyncio
async def test_upload_with_custom_pixel_mask_json(client: AsyncClient):
    """Validate custom pixel mask coordinate JSON injection.

    @req:PRD-IMG-002
    @req:PRD-SYS-164
    """
    raw_dcm = create_sample_dicom()
    custom_boxes = [{"x_min": 5, "y_min": 5, "x_max": 20, "y_max": 20}]

    files = {"file": ("custom_mask.dcm", raw_dcm, "application/dicom")}
    data = {
        "study_id": "TRIAL-CUSTOM-BOX",
        "site_id": "SITE-01",
        "subject_id": "SUBJ-001",
        "custom_pixel_masks_json": json.dumps(custom_boxes),
    }

    resp = await client.post("/api/v1/imaging/upload", files=files, data=data)
    assert resp.status_code == 201
    cert = resp.json()["certificates"][0]
    assert cert["pixel_mask_applied"] is True


@pytest.mark.asyncio
async def test_not_found_errors(client: AsyncClient):
    """Validate 404 responses for nonexistent studies, instances, and certificates.

    @req:PRD-IMG-001
    @req:PRD-SYS-164
    """
    res1 = await client.get("/api/v1/imaging/studies/nonexistent-study-uid-999")
    assert res1.status_code == 404

    res2 = await client.get("/api/v1/imaging/instances/nonexistent-sop-uid-999/file")
    assert res2.status_code == 404

    res3 = await client.get(
        "/api/v1/imaging/certificates/nonexistent-cert-id-999/verify"
    )
    assert res3.status_code == 404
