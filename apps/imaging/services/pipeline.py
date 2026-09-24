"""Ingestion pipeline orchestrating DICOM de-identification, S3 persistence, and envelope recording.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

import io
import zipfile
from datetime import UTC, datetime
from typing import Any

import pydicom
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.imaging.adapters.models import (
    DeidentificationCertificateModel,
    ImagingAuditLogModel,
    ImagingInstanceEnvelopeModel,
    ImagingSeriesEnvelopeModel,
    ImagingStudyEnvelopeModel,
)
from packages.deid.dicom import (
    DicomDeidCertificate,
    DicomDeidentifier,
    DicomDeidResult,
    PixelMaskBoundingBox,
)
from packages.storage import StoragePort


class ImagingIngestionPipeline:
    """Enterprise pipeline for in-flight DICOM PHI de-identification and S3 envelope storage."""

    def __init__(
        self,
        storage_port: StoragePort[Any],
        deidentifier: DicomDeidentifier | None = None,
    ) -> None:
        self.storage_port = storage_port
        self.deidentifier = deidentifier or DicomDeidentifier()

    async def ingest_dicom_bytes(
        self,
        dicom_bytes: bytes,
        study_id: str,
        site_id: str,
        subject_id: str,
        user_id: str,
        reason_for_change: str,
        session: AsyncSession,
        tenant_id: str = "tenant_default",
        date_shift_days: int = 0,
        apply_pixel_mask: bool = True,
        custom_pixel_masks: list[PixelMaskBoundingBox] | None = None,
    ) -> tuple[ImagingStudyEnvelopeModel, DicomDeidCertificate]:
        """Processes a single DICOM binary in-flight, storing encrypted blob and metadata envelope."""
        # 1. In-Flight Header Scrubbing & Pixel Masking
        deid_result: DicomDeidResult = self.deidentifier.deidentify(
            dicom_bytes=dicom_bytes,
            subject_id=subject_id,
            site_id=site_id,
            study_id=study_id,
            date_shift_days=date_shift_days,
            apply_pixel_mask=apply_pixel_mask,
            custom_pixel_masks=custom_pixel_masks,
            operator_identity=user_id,
        )

        # 2. Persist to Tenant-Isolated Object Storage via StoragePort
        object_key = (
            f"imaging/{tenant_id}/{study_id}/{site_id}/{subject_id}/"
            f"{deid_result.anonymized_study_uid}/{deid_result.anonymized_series_uid}/"
            f"{deid_result.anonymized_sop_uid}.dcm"
        )

        metadata_tags = {
            "study_id": study_id,
            "site_id": site_id,
            "subject_id": subject_id,
            "tenant_id": tenant_id,
            "sha256": deid_result.certificate.target_sha256,
            "modality": deid_result.modality,
        }

        await self.storage_port.put_object(
            key=object_key,
            data=deid_result.deidentified_bytes,
            content_type="application/dicom",
            metadata=metadata_tags,
            expected_sha256=deid_result.certificate.target_sha256,
        )

        # 3. Parse auxiliary DICOM metadata for series & study envelopes
        ds = pydicom.dcmread(io.BytesIO(deid_result.deidentified_bytes), force=True)
        study_date = str(getattr(ds, "StudyDate", "")) or None
        series_num = (
            int(ds.SeriesNumber)
            if hasattr(ds, "SeriesNumber") and str(ds.SeriesNumber).isdigit()
            else None
        )
        instance_num = (
            int(ds.InstanceNumber)
            if hasattr(ds, "InstanceNumber") and str(ds.InstanceNumber).isdigit()
            else None
        )
        body_part = str(getattr(ds, "BodyPartExamined", "")) or None
        series_desc = str(getattr(ds, "SeriesDescription", "")) or None
        study_desc = str(getattr(ds, "StudyDescription", "")) or None
        protocol_name = str(getattr(ds, "ProtocolName", "")) or None
        frame_of_ref = str(getattr(ds, "FrameOfReferenceUID", "")) or None

        # 4. Upsert Relational Study Envelope
        study_stmt = select(ImagingStudyEnvelopeModel).where(
            ImagingStudyEnvelopeModel.study_instance_uid
            == deid_result.anonymized_study_uid
        )
        study_exec = await session.execute(study_stmt)
        study_model = study_exec.scalar_one_or_none()

        if study_model is None:
            study_model = ImagingStudyEnvelopeModel(
                study_instance_uid=deid_result.anonymized_study_uid,
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                modalities=[deid_result.modality],
                study_date=study_date,
                study_description=study_desc,
                series_count=1,
                instance_count=1,
                storage_prefix=f"imaging/{tenant_id}/{study_id}/{site_id}/{subject_id}/{deid_result.anonymized_study_uid}",
                status="STORED",
                adjudication_status="UNASSIGNED",
                tenant_id=tenant_id,
                created_by=user_id,
                reason_for_change=reason_for_change,
            )
            session.add(study_model)
            await session.flush()
        else:
            if deid_result.modality not in study_model.modalities:
                study_model.modalities = [*study_model.modalities, deid_result.modality]
            study_model.instance_count += 1
            study_model.updated_at = datetime.now(UTC)

        # 5. Upsert Relational Series Envelope
        series_stmt = select(ImagingSeriesEnvelopeModel).where(
            ImagingSeriesEnvelopeModel.series_instance_uid
            == deid_result.anonymized_series_uid,
            ImagingSeriesEnvelopeModel.study_envelope_id == study_model.id,
        )
        series_exec = await session.execute(series_stmt)
        series_model = series_exec.scalar_one_or_none()

        if series_model is None:
            series_model = ImagingSeriesEnvelopeModel(
                study_envelope_id=study_model.id,
                series_instance_uid=deid_result.anonymized_series_uid,
                series_number=series_num,
                modality=deid_result.modality,
                body_part_examined=body_part,
                series_description=series_desc,
                instance_count=1,
                protocol_name=protocol_name,
                frame_of_reference_uid=frame_of_ref,
            )
            session.add(series_model)
            await session.flush()
            # Update study series count
            study_series_count_stmt = select(ImagingSeriesEnvelopeModel).where(
                ImagingSeriesEnvelopeModel.study_envelope_id == study_model.id
            )
            count_exec = await session.execute(study_series_count_stmt)
            study_model.series_count = len(count_exec.scalars().all())
        else:
            series_model.instance_count += 1

        # 6. Record Signed De-Identification Certificate
        cert_data = deid_result.certificate
        cert_model = DeidentificationCertificateModel(
            id=cert_data.certificate_id,
            study_envelope_id=study_model.id,
            source_sha256=cert_data.source_sha256,
            target_sha256=cert_data.target_sha256,
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            original_study_instance_uid_hash=cert_data.original_study_instance_uid_hash,
            original_sop_instance_uid_hash=cert_data.original_sop_instance_uid_hash,
            anonymized_study_instance_uid=cert_data.anonymized_study_instance_uid,
            anonymized_sop_instance_uid=cert_data.anonymized_sop_instance_uid,
            scrubbed_tags_count=cert_data.scrubbed_tags_count,
            scrubbed_tags=cert_data.scrubbed_tags,
            pixel_mask_applied=cert_data.pixel_mask_applied,
            pixel_masks=[b.model_dump() for b in cert_data.pixel_masks],
            operator_identity=cert_data.operator_identity,
            timestamp=cert_data.timestamp,
            signature=cert_data.signature or "",
        )
        session.add(cert_model)

        # 7. Record Instance Envelope
        instance_model = ImagingInstanceEnvelopeModel(
            series_envelope_id=series_model.id,
            sop_instance_uid=deid_result.anonymized_sop_uid,
            sop_class_uid=str(getattr(ds, "SOPClassUID", "1.2.840.10008.5.1.4.1.1.2")),
            instance_number=instance_num,
            rows=deid_result.rows,
            columns=deid_result.columns,
            bits_allocated=deid_result.bits_allocated,
            pixel_spacing=deid_result.pixel_spacing,
            slice_thickness=deid_result.slice_thickness,
            slice_location=deid_result.slice_location,
            window_center=deid_result.window_center,
            window_width=deid_result.window_width,
            object_key=object_key,
            checksum_sha256=deid_result.certificate.target_sha256,
            size_bytes=len(deid_result.deidentified_bytes),
            burned_in_phi_masked=cert_data.pixel_mask_applied,
            certificate_id=cert_model.id,
        )
        session.add(instance_model)

        # 8. Record 21 CFR Part 11 Audit Log
        audit_log = ImagingAuditLogModel(
            study_envelope_id=study_model.id,
            action="DICOM_INGESTION_DEIDENTIFIED",
            actor_id=user_id,
            reason_for_change=reason_for_change,
            details={
                "sop_instance_uid": deid_result.anonymized_sop_uid,
                "certificate_id": cert_model.id,
                "object_key": object_key,
                "sha256": deid_result.certificate.target_sha256,
            },
        )
        session.add(audit_log)

        await session.flush()
        return study_model, cert_data

    async def ingest_archive_or_file(
        self,
        payload_bytes: bytes,
        filename: str,
        study_id: str,
        site_id: str,
        subject_id: str,
        user_id: str,
        reason_for_change: str,
        session: AsyncSession,
        tenant_id: str = "tenant_default",
        date_shift_days: int = 0,
        apply_pixel_mask: bool = True,
        custom_pixel_masks: list[PixelMaskBoundingBox] | None = None,
    ) -> tuple[ImagingStudyEnvelopeModel, list[DicomDeidCertificate]]:
        """Processes either a single DICOM file or a ZIP archive containing multiple DICOM slices."""
        certificates: list[DicomDeidCertificate] = []
        study_model: ImagingStudyEnvelopeModel | None = None

        if filename.lower().endswith(".zip") or (payload_bytes[:4] == b"PK\x03\x04"):
            # Process ZIP archive of DICOM slices
            with zipfile.ZipFile(io.BytesIO(payload_bytes), "r") as zip_ref:
                for zip_info in zip_ref.infolist():
                    # Security check against path traversal / zip slip
                    if (
                        zip_info.is_dir()
                        or zip_info.filename.startswith("/")
                        or ".." in zip_info.filename
                    ):
                        continue

                    # Read slice bytes
                    slice_bytes = zip_ref.read(zip_info.filename)
                    if len(slice_bytes) < 132:
                        continue

                    try:
                        study_model, cert = await self.ingest_dicom_bytes(
                            dicom_bytes=slice_bytes,
                            study_id=study_id,
                            site_id=site_id,
                            subject_id=subject_id,
                            user_id=user_id,
                            reason_for_change=reason_for_change,
                            session=session,
                            tenant_id=tenant_id,
                            date_shift_days=date_shift_days,
                            apply_pixel_mask=apply_pixel_mask,
                            custom_pixel_masks=custom_pixel_masks,
                        )
                        certificates.append(cert)
                    except Exception:
                        # Non-DICOM file in ZIP archive: ignore safely
                        continue
        else:
            # Process Single DICOM file
            study_model, cert = await self.ingest_dicom_bytes(
                dicom_bytes=payload_bytes,
                study_id=study_id,
                site_id=site_id,
                subject_id=subject_id,
                user_id=user_id,
                reason_for_change=reason_for_change,
                session=session,
                tenant_id=tenant_id,
                date_shift_days=date_shift_days,
                apply_pixel_mask=apply_pixel_mask,
                custom_pixel_masks=custom_pixel_masks,
            )
            certificates.append(cert)

        if study_model is None:
            raise ValueError(
                "No valid DICOM datasets could be extracted or processed from the provided payload."
            )

        return study_model, certificates
