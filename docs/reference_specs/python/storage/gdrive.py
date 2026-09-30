"""Google Drive v3 client adapter and artifact synchronization engine.

Provides headless authentication via Google Cloud Service Accounts, automated
remote folder management, resumable file uploads, and cryptographic SHA-256
manifest generation for GxP UI video recordings and verification reports.
"""

import hashlib
import logging
import mimetypes
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from google.oauth2 import service_account
from googleapiclient.discovery import Resource, build
from googleapiclient.http import MediaFileUpload
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)

SCOPES = ["https://www.googleapis.com/auth/drive.file"]


def get_file_mime_type(file_path: Path | str) -> str:
    """Resolve standard MIME type with explicit video overrides for WebM/MP4.

    Args:
        file_path: Target path or filename.

    Returns:
        MIME type string (e.g. 'video/webm', 'text/html').
    """
    path = Path(file_path)
    name = path.name.lower()
    if name.endswith(".webm"):
        return "video/webm"
    if name.endswith(".mp4"):
        return "video/mp4"
    if name.endswith(".html"):
        return "text/html"
    if name.endswith(".json"):
        return "application/json"

    guessed, _ = mimetypes.guess_type(path)
    return guessed or "application/octet-stream"


def compute_sha256(file_path: Path | str) -> str:
    """Compute the SHA-256 cryptographic hash of a local file.

    Args:
        file_path: Path to the target file.

    Returns:
        Hexadecimal SHA-256 string.
    """
    hasher = hashlib.sha256()
    path = Path(file_path)
    with path.open("rb") as f:
        while chunk := f.read(65536):
            hasher.update(chunk)
    return hasher.hexdigest()


class FileManifestRecord(BaseModel):
    """Metadata and integrity record for a single artifact file."""

    name: str
    relative_path: str
    size_bytes: int
    sha256: str
    mime_type: str
    modified_at: str


class RunManifest(BaseModel):
    """Run manifest cataloging all artifacts within an execution session."""

    manifest_version: str = "1.0"
    created_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    run_metadata: dict[str, Any] = Field(default_factory=dict)
    files: list[FileManifestRecord] = Field(default_factory=list)


def generate_run_manifest(
    directory_path: Path | str,
    run_metadata: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Generate a structured run manifest cataloging all files in a recording directory.

    Args:
        directory_path: Path to the local directory containing recordings.
        run_metadata: Optional dictionary of run-level parameters and sentinel metrics.

    Returns:
        Dictionary containing manifest version, timestamps, run metadata, and file records.
    """
    root_dir = Path(directory_path)
    file_records: list[FileManifestRecord] = []

    for file_path in sorted(root_dir.glob("**/*")):
        if file_path.is_file() and not file_path.name.endswith(".tmp"):
            stat = file_path.stat()
            file_records.append(
                FileManifestRecord(
                    name=file_path.name,
                    relative_path=str(file_path.relative_to(root_dir)),
                    size_bytes=stat.st_size,
                    sha256=compute_sha256(file_path),
                    mime_type=get_file_mime_type(file_path),
                    modified_at=datetime.fromtimestamp(
                        stat.st_mtime, tz=UTC
                    ).isoformat(),
                )
            )

    manifest = RunManifest(
        run_metadata=run_metadata or {},
        files=file_records,
    )
    return manifest.model_dump()


class GoogleDriveClient:
    """Google Drive v3 API client for automated artifact synchronization."""

    def __init__(
        self,
        credentials_path: str | None = None,
        service_account_info: dict[str, Any] | None = None,
    ) -> None:
        """Initialize GoogleDriveClient with service account credentials.

        Args:
            credentials_path: Optional path to Google Service Account JSON file.
            service_account_info: Optional dictionary containing service account key payload.
        """
        from packages.config import get_storage_settings

        settings = get_storage_settings()
        self._credentials_path = (
            credentials_path
            or settings.google_application_credentials
            or settings.google_drive_service_account_path
        )
        self._service_account_info = service_account_info
        self._service: Resource | None = None

    @property
    def is_configured(self) -> bool:
        """Check whether service account credentials are provided and accessible."""
        if self._service_account_info:
            return True
        return bool(self._credentials_path and Path(self._credentials_path).is_file())

    def get_service(self) -> Resource:
        """Build or return an existing Google Drive v3 API service resource.

        Returns:
            Google API client Resource.

        Raises:
            RuntimeError: If credentials are not configured.
        """
        if self._service is not None:
            return self._service

        if not self.is_configured:
            raise RuntimeError(
                "Google Drive credentials are not configured. Set GOOGLE_APPLICATION_CREDENTIALS "
                "or pass a valid service account JSON file."
            )

        if self._service_account_info:
            creds = service_account.Credentials.from_service_account_info(
                self._service_account_info, scopes=SCOPES
            )
        else:
            creds = service_account.Credentials.from_service_account_file(
                self._credentials_path, scopes=SCOPES
            )

        self._service = build("drive", "v3", credentials=creds, cache_discovery=False)
        return self._service

    def validate_credentials(self) -> dict[str, Any]:
        """Validate connection to Google Drive API and return account identity metadata.

        Returns:
            Dictionary containing email, display name, and quota stats.

        Raises:
            Exception: If API call fails.
        """
        service = self.get_service()
        about = service.about().get(fields="user, storageQuota").execute()
        user_info = about.get("user", {})
        quota = about.get("storageQuota", {})
        return {
            "valid": True,
            "email": user_info.get("emailAddress", "unknown"),
            "displayName": user_info.get("displayName", "Service Account"),
            "storage_usage_bytes": int(quota.get("usage", 0)),
            "storage_limit_bytes": int(quota.get("limit", 0))
            if "limit" in quota
            else None,
        }

    def create_folder(
        self, folder_name: str, parent_folder_id: str | None = None
    ) -> str:
        """Create a new folder in Google Drive.

        Args:
            folder_name: Name of the folder to create.
            parent_folder_id: Optional parent Google Drive folder ID.

        Returns:
            The created Google Drive folder ID.
        """
        service = self.get_service()
        metadata: dict[str, Any] = {
            "name": folder_name,
            "mimeType": "application/vnd.google-apps.folder",
        }
        if parent_folder_id:
            metadata["parents"] = [parent_folder_id]

        folder = service.files().create(body=metadata, fields="id, name").execute()
        logger.info(
            "Created Google Drive folder '%s' (ID: %s)",
            folder_name,
            folder.get("id"),
        )
        return folder.get("id")

    def delete_file(self, file_id: str) -> None:
        """Delete a file or folder in Google Drive.

        Args:
            file_id: Target file or folder ID to delete.
        """
        service = self.get_service()
        service.files().delete(fileId=file_id).execute()
        logger.info("Deleted Google Drive item (ID: %s)", file_id)

    def upload_file(
        self,
        file_path: Path | str,
        folder_id: str,
        file_name: str | None = None,
        mime_type: str | None = None,
    ) -> dict[str, Any]:
        """Upload a single file to a designated Google Drive folder.

        Args:
            file_path: Local path to the file.
            folder_id: Target Google Drive folder ID.
            file_name: Optional override filename.
            mime_type: Optional override MIME type.

        Returns:
            Google Drive file metadata dictionary (id, name, webViewLink, size, etc.).
        """
        path = Path(file_path)
        if not path.is_file():
            raise FileNotFoundError(f"File not found: {path}")

        service = self.get_service()
        target_name = file_name or path.name
        resolved_mime = mime_type or get_file_mime_type(target_name)

        media = MediaFileUpload(str(path), mimetype=resolved_mime, resumable=True)
        file_metadata: dict[str, Any] = {
            "name": target_name,
            "parents": [folder_id],
        }

        created = (
            service.files()
            .create(
                body=file_metadata,
                media_body=media,
                fields="id, name, webViewLink, size, md5Checksum, createdTime",
            )
            .execute()
        )
        logger.info(
            "Uploaded '%s' to Google Drive (ID: %s)",
            target_name,
            created.get("id"),
        )
        return created

    def upload_directory(
        self,
        local_dir: Path | str,
        parent_folder_id: str | None = None,
        folder_name: str | None = None,
    ) -> dict[str, Any]:
        """Upload an entire directory structure to a new folder in Google Drive.

        Args:
            local_dir: Local path to directory containing artifacts.
            parent_folder_id: Parent Google Drive folder ID.
            folder_name: Name of the remote folder to create. Defaults to directory name.

        Returns:
            Dictionary containing remote folder ID, web link, and uploaded file records.
        """
        dir_path = Path(local_dir)
        target_folder_name = folder_name or dir_path.name
        created_folder_id = self.create_folder(
            target_folder_name, parent_folder_id=parent_folder_id
        )

        uploaded_files: list[dict[str, Any]] = []
        for item in sorted(dir_path.glob("*")):
            if item.is_file():
                upload_res = self.upload_file(item, folder_id=created_folder_id)
                uploaded_files.append(upload_res)

        return {
            "folder_id": created_folder_id,
            "folder_name": target_folder_name,
            "files": uploaded_files,
            "total_files": len(uploaded_files),
        }
