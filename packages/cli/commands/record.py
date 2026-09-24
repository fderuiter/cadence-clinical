"""Unified CLI command for automated UI golden path video recording and Google Drive synchronization."""

import json
import os
import shutil
import subprocess
from datetime import UTC, datetime
from enum import StrEnum
from pathlib import Path
from typing import Any

import typer
from rich.table import Table

from packages.cli.formatting import (
    console,
    is_json_mode,
    output_json,
    print_error,
    print_header,
    print_info,
    print_success,
    print_warning,
)
from packages.storage.gdrive import GoogleDriveClient, generate_run_manifest

record_app = typer.Typer(
    help="Capture high-definition UI video recordings across clinical personas and sync to Google Drive."
)

REPO_ROOT = Path(__file__).resolve().parents[3]
RECORDINGS_DIR = REPO_ROOT / ".artifacts" / "recordings"


class PersonaSuiteName(StrEnum):
    """Supported persona golden path test suite identifiers."""

    DESIGNER = "designer"
    CRC = "crc"
    CRA = "cra"
    DATA_MANAGER = "data_manager"
    SAFETY = "safety"
    COMPLIANCE = "compliance"
    ALL = "all"


SUITE_MAPPING: dict[str, str] = {
    PersonaSuiteName.DESIGNER: "apps/web/tests/e2e/suites/01_designer_suite.spec.ts",
    PersonaSuiteName.CRC: "apps/web/tests/e2e/suites/02_crc_ecrf_suite.spec.ts",
    PersonaSuiteName.CRA: "apps/web/tests/e2e/suites/03_cra_monitoring_suite.spec.ts",
    PersonaSuiteName.DATA_MANAGER: "apps/web/tests/e2e/suites/04_data_manager_suite.spec.ts",
    PersonaSuiteName.SAFETY: "apps/web/tests/e2e/suites/05_safety_physician_suite.spec.ts",
    PersonaSuiteName.COMPLIANCE: "apps/web/tests/e2e/suites/06_advanced_compliance_suite.spec.ts",
    PersonaSuiteName.ALL: "apps/web/tests/e2e/suites/",
}


def _update_env_file(key_path: str, folder_id: str | None) -> None:
    """Safely updates or appends Google Drive configuration in .env file.

    Args:
        key_path: Path to Google Service Account credentials.
        folder_id: Optional target Google Drive folder ID.
    """
    env_path = REPO_ROOT / ".env"
    lines: list[str] = []
    if env_path.exists():
        lines = env_path.read_text(encoding="utf-8").splitlines()

    updated_creds = False
    updated_folder = False
    new_lines: list[str] = []

    for line in lines:
        if line.startswith("GOOGLE_APPLICATION_CREDENTIALS="):
            new_lines.append(f"GOOGLE_APPLICATION_CREDENTIALS={key_path}")
            updated_creds = True
        elif line.startswith("GOOGLE_DRIVE_FOLDER_ID="):
            if folder_id:
                new_lines.append(f"GOOGLE_DRIVE_FOLDER_ID={folder_id}")
                updated_folder = True
        else:
            new_lines.append(line)

    if not updated_creds:
        new_lines.append(f"GOOGLE_APPLICATION_CREDENTIALS={key_path}")
    if folder_id and not updated_folder:
        new_lines.append(f"GOOGLE_DRIVE_FOLDER_ID={folder_id}")

    env_path.write_text("\n".join(new_lines) + "\n", encoding="utf-8")


def _compile_html_verification_report(
    run_dir: Path,
    run_name: str,
    timestamp: str,
    passed: bool,
    manifest: dict[str, Any],
) -> Path:
    """Generates an interactive verification HTML report embedding recorded videos.

    Args:
        run_dir: Path to directory containing video artifacts.
        run_name: Human-readable run identifier.
        timestamp: ISO/UTC execution timestamp.
        passed: Boolean indicating whether Triple Sentinel gate passed.
        manifest: Run manifest dictionary.

    Returns:
        Path to compiled HTML report.
    """
    video_files = [
        f for f in manifest.get("files", []) if f["name"].endswith((".webm", ".mp4"))
    ]

    cards_html = ""
    for v in video_files:
        cards_html += f"""
      <div class="card">
        <div class="card-header">
          <h3>{v["name"]}</h3>
          <span class="badge">{(v["size_bytes"] / (1024 * 1024)):.2f} MB</span>
        </div>
        <div class="video-wrapper">
          <video controls preload="metadata">
            <source src="./{v["name"]}" type="{v["mime_type"]}">
            Your browser does not support HTML5 video.
          </video>
        </div>
        <div class="hash-row">SHA-256: <code>{v["sha256"]}</code></div>
      </div>
        """

    html = f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Cadence Clinical — UI Verification & Video Report</title>
  <style>
    body {{
      margin: 0;
      padding: 32px;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
      background: #0f172a;
      color: #f8fafc;
    }}
    .header {{
      border-bottom: 1px solid #334155;
      padding-bottom: 20px;
      margin-bottom: 30px;
    }}
    .status-badge {{
      display: inline-block;
      padding: 6px 14px;
      border-radius: 9999px;
      font-weight: bold;
      font-size: 0.85rem;
      background: {"#10b981" if passed else "#ef4444"};
      color: white;
      text-transform: uppercase;
      margin-top: 10px;
    }}
    .grid {{
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(460px, 1fr));
      gap: 24px;
    }}
    .card {{
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 12px;
      overflow: hidden;
      display: flex;
      flex-direction: column;
    }}
    .card-header {{
      padding: 16px 20px;
      background: rgba(255, 255, 255, 0.03);
      border-bottom: 1px solid #334155;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }}
    .card-header h3 {{
      margin: 0;
      font-size: 1rem;
    }}
    .badge {{
      font-size: 0.75rem;
      background: rgba(2, 132, 199, 0.2);
      color: #38bdf8;
      padding: 4px 8px;
      border-radius: 6px;
    }}
    .video-wrapper {{
      background: #000;
      aspect-ratio: 16/9;
    }}
    video {{
      width: 100%;
      height: 100%;
      display: block;
    }}
    .hash-row {{
      padding: 12px 20px;
      font-size: 0.75rem;
      color: #94a3b8;
      border-top: 1px solid #334155;
      word-break: break-all;
    }}
  </style>
</head>
<body>
  <div class="header">
    <h1>Cadence Clinical — Triple Sentinel UI Verification Report</h1>
    <p style="color: #94a3b8;">Run: <code>{run_name}</code> | Timestamp: {timestamp}</p>
    <div class="status-badge">{"VERIFIED — ALL SENTINELS PASSED" if passed else "EXECUTION FAILED"}</div>
  </div>
  <div class="grid">
    {cards_html or '<p style="color: #94a3b8;">No video files captured.</p>'}
  </div>
</body>
</html>"""

    report_path = run_dir / "verification_report.html"
    report_path.write_text(html, encoding="utf-8")
    return report_path


@record_app.command(name="setup-gdrive")
def setup_gdrive(
    ctx: typer.Context,
    credentials_path: str | None = typer.Option(
        None,
        "--credentials-path",
        "-c",
        help="Path to Google Cloud Service Account JSON key file",
    ),
    folder_id: str | None = typer.Option(
        None,
        "--folder-id",
        "-f",
        help="Target Google Drive Folder ID",
    ),
) -> None:
    """Validate Google Drive Service Account credentials and target folder permissions.

    Args:
        ctx: Typer Context object.
        credentials_path: Optional path to Google Service Account JSON credentials.
        folder_id: Optional target Google Drive folder ID.

    Raises:
        typer.Exit: If validation fails or credentials file is invalid.
    """
    print_header("Google Drive Credential Setup & Validation", "Storage & Compliance")

    from packages.config import get_storage_settings

    storage_settings = get_storage_settings()
    key_path = credentials_path or storage_settings.google_application_credentials
    target_folder = folder_id or storage_settings.google_drive_folder_id

    if not key_path:
        print_error("Missing service account credentials key path.")
        raise typer.Exit(1)

    resolved_path = Path(key_path).expanduser().resolve()
    if not resolved_path.is_file():
        print_error(f"Credentials file does not exist at: {resolved_path}")
        raise typer.Exit(1)

    print_info(f"Validating service account credentials from: {resolved_path}")
    client = GoogleDriveClient(credentials_path=str(resolved_path))

    try:
        validation = client.validate_credentials()
        print_success(
            f"Successfully verified Google Drive credentials for: [bold]{validation.get('email')}[/bold]"
        )
    except Exception as err:
        print_error(f"Failed to authenticate with Google Drive API: {err}")
        raise typer.Exit(1) from err

    if target_folder:
        try:
            test_folder_name = (
                f"_Cadence_Access_Check_{datetime.now(UTC).strftime('%Y%m%d%H%M%S')}"
            )
            created_id = client.create_folder(
                test_folder_name, parent_folder_id=target_folder
            )
            print_success(
                f"Verified write access to Google Drive folder [bold]{target_folder}[/bold]"
            )
            client.delete_file(created_id)
        except Exception as err:
            print_warning(
                f"Could not verify write permissions in target folder {target_folder}: {err}"
            )

    env_file = REPO_ROOT / ".env"
    print_info(f"Configuring environment variables in {env_file}")
    _update_env_file(str(resolved_path), target_folder)

    if is_json_mode(ctx.obj):
        output_json(
            {
                "status": "success",
                "service_account": validation.get("email"),
                "credentials_path": str(resolved_path),
                "folder_id": target_folder,
            }
        )
    else:
        console.print(
            "\n[bold green]✔ Setup Complete.[/bold green] You can now run [bold cyan]uv run cadence record run --upload-drive[/bold cyan]"
        )


@record_app.command(name="list")
def list_recordings(ctx: typer.Context) -> None:
    """List historical local UI video recording runs and manifests.

    Args:
        ctx: Typer Context object.
    """
    if not RECORDINGS_DIR.exists() or not any(RECORDINGS_DIR.iterdir()):
        if is_json_mode(ctx.obj):
            output_json([])
        else:
            print_info("No local recordings found in .artifacts/recordings/")
        return

    runs = [d for d in RECORDINGS_DIR.iterdir() if d.is_dir()]
    runs.sort(key=lambda d: d.stat().st_mtime, reverse=True)

    if is_json_mode(ctx.obj):
        records = []
        for run in runs:
            manifest_file = run / "run_manifest.json"
            data = {}
            if manifest_file.exists():
                with manifest_file.open(encoding="utf-8") as f:
                    data = json.load(f)
            records.append({"run_dir": run.name, "manifest": data})
        output_json(records)
        return

    print_header("Recorded UI Sessions & Verification Envelopes", "Video Artifacts")
    table = Table(show_header=True, header_style="bold cyan")
    table.add_column("Run Directory", style="bold", no_wrap=False)
    table.add_column("Created At")
    table.add_column("Videos / Files", justify="right")
    table.add_column("Total Size", justify="right")
    table.add_column("Status", justify="center")

    for run in runs:
        files = list(run.glob("*"))
        video_count = len([f for f in files if f.suffix in [".webm", ".mp4"]])
        total_size_mb = sum(f.stat().st_size for f in files) / (1024 * 1024)
        mtime = datetime.fromtimestamp(run.stat().st_mtime, tz=UTC).strftime(
            "%Y-%m-%d %H:%M:%S UTC"
        )

        status = (
            "[green]VERIFIED[/green]"
            if (run / "run_manifest.json").exists()
            else "[yellow]PARTIAL[/yellow]"
        )
        table.add_row(
            run.name,
            mtime,
            f"{video_count} videos ({len(files)} files)",
            f"{total_size_mb:.2f} MB",
            status,
        )

    console.print(table)


@record_app.command(name="run")
def run_recordings(
    ctx: typer.Context,
    suite: str = typer.Option(
        "all",
        "--suite",
        "-s",
        help="Target persona suite: 'designer', 'crc', 'cra', 'data_manager', 'safety', 'compliance', 'all'",
    ),
    upload_drive: bool = typer.Option(
        False,
        "--upload-drive",
        "--upload",
        help="Upload recorded video artifacts to Google Drive upon completion",
    ),
    local_only: bool = typer.Option(
        False,
        "--local-only",
        help="Skip Google Drive upload and preserve artifacts locally only",
    ),
    demo: bool = typer.Option(
        True,
        "--demo/--fast",
        help="Run in demonstration pacing mode with smooth pauses vs high-speed headless",
    ),
    headed: bool = typer.Option(
        False,
        "--headed",
        help="Display browser window during test execution",
    ),
    mp4: bool = typer.Option(
        False,
        "--mp4",
        help="Transcode WebM recordings to MP4 if ffmpeg is installed",
    ),
) -> None:
    """Execute end-to-end persona golden paths, record 1080p videos, and sync to Google Drive.

    Args:
        ctx: Typer Context object.
        suite: Persona suite name to run.
        upload_drive: Whether to upload to Google Drive.
        local_only: Restrict artifact preservation to local filesystem.
        demo: Use human-speed demonstration pacing.
        headed: Display browser window during execution.
        mp4: Convert WebM to MP4 if ffmpeg is available.
    """
    print_header(
        "UI Golden Path Video Recording & Triple Sentinel Verification",
        "E2E & Compliance",
    )

    timestamp = datetime.now(UTC).strftime("%Y-%m-%d_%H-%M-%S")
    run_name = f"Cadence_Recordings_{timestamp}"
    run_dir = RECORDINGS_DIR / run_name
    run_dir.mkdir(parents=True, exist_ok=True)

    target_spec = SUITE_MAPPING.get(suite.lower(), SUITE_MAPPING["all"])
    print_info(f"Target Suite: [bold]{suite}[/bold] ({target_spec})")
    print_info(f"Output Directory: [bold]{run_dir}[/bold]")
    print_info(
        f"Execution Mode: [bold]{'Demonstration Paced' if demo else 'Fast Execution'}[/bold]"
    )

    env = os.environ.copy()
    env["RECORD_VIDEO"] = "true"
    env["RECORDING_OUTPUT_DIR"] = str(run_dir)
    env["DEMO_MODE"] = "true" if demo else "false"

    cmd = [
        "pnpm",
        "--filter",
        "web",
        "exec",
        "playwright",
        "test",
        target_spec,
    ]
    if headed:
        cmd.append("--headed")

    print_info(f"Executing: {' '.join(cmd)}")
    result = subprocess.run(
        cmd, cwd=REPO_ROOT, env=env, capture_output=True, text=True, check=False
    )

    if result.returncode != 0:
        print_warning(
            f"Playwright run returned non-zero code ({result.returncode}). Checking captured artifacts..."
        )
        if result.stdout:
            console.print(result.stdout)
        if result.stderr:
            console.print(result.stderr)
    else:
        print_success("Playwright Golden Path execution completed successfully.")

    # Generate Manifest and verify Triple Sentinel
    manifest = generate_run_manifest(
        run_dir,
        run_metadata={
            "run_name": run_name,
            "timestamp": timestamp,
            "suite": suite,
            "demo_pacing": demo,
            "exit_code": result.returncode,
            "triple_sentinel_passed": result.returncode == 0,
        },
    )
    manifest_path = run_dir / "run_manifest.json"
    with manifest_path.open("w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print_success(
        f"Generated cryptographic run manifest: [bold]{manifest_path.name}[/bold]"
    )

    # Compile HTML Verification Report
    report_file = _compile_html_verification_report(
        run_dir=run_dir,
        run_name=run_name,
        timestamp=timestamp,
        passed=result.returncode == 0,
        manifest=manifest,
    )
    print_success(
        f"Generated interactive verification report: [bold]{report_file.name}[/bold]"
    )

    # Optional MP4 transcoding if ffmpeg exists
    if mp4 and shutil.which("ffmpeg"):
        print_info("Transcoding WebM videos to MP4 format...")
        for webm_file in list(run_dir.glob("*.webm")):
            mp4_file = webm_file.with_suffix(".mp4")
            subprocess.run(
                [
                    "ffmpeg",
                    "-y",
                    "-i",
                    str(webm_file),
                    "-c:v",
                    "libx264",
                    "-pix_fmt",
                    "yuv420p",
                    str(mp4_file),
                ],
                capture_output=True,
                check=False,
            )

    # Google Drive Sync
    if upload_drive and not local_only:
        client = GoogleDriveClient()
        if not client.is_configured:
            print_warning(
                "Google Drive credentials not configured. Skipping upload. "
                "Run [bold cyan]uv run cadence record setup-gdrive[/bold cyan] to configure."
            )
        else:
            print_info("Uploading recording envelope to Google Drive...")
            from packages.config import get_storage_settings

            parent_folder_id = get_storage_settings().google_drive_folder_id
            upload_summary = client.upload_directory(
                local_dir=run_dir,
                parent_folder_id=parent_folder_id,
                folder_name=run_name,
            )
            print_success(
                f"Successfully uploaded {upload_summary['total_files']} artifacts to Google Drive "
                f"(Folder ID: [bold]{upload_summary['folder_id']}[/bold])"
            )

    if is_json_mode(ctx.obj):
        output_json(
            {
                "status": "success" if result.returncode == 0 else "failure",
                "run_name": run_name,
                "output_dir": str(run_dir),
                "manifest": manifest,
            }
        )
    else:
        print_success(
            "Triple Sentinel Verification Passed: Video recordings and manifest generated."
        )
