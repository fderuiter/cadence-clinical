"""Comprehensive unit and integration test suite for the Validated Containerized Statistical Computing Environment (SCE).

Requirements: PRD-CDR-004, PRD-CDR-005, PRD-CDR-006, ADR-2199, 21 CFR Part 11
"""

from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.interop.application.lakehouse_service import LakehouseService
from apps.interop.application.statistical_computing_service import (
    StatisticalComputingService,
    UnblindedAccessDeniedError,
)
from apps.interop.domain.lakehouse_models import (
    DataStreamType,
    EDCEcrfIngestItem,
    LockMilestoneType,
    MultiStreamIngestPayload,
)
from apps.interop.domain.lakehouse_normalizer import normalize_edc_ecrf
from apps.interop.domain.sce_lock_token import (
    issue_database_lock_token,
    verify_database_lock_token,
)
from apps.interop.domain.sce_lockfile_verifier import (
    validate_package_lockfile,
)
from apps.interop.domain.sce_models import (
    ExecutionLanguage,
    ExecutionSandboxConfig,
    ExecutionStatus,
    LockValidationStatus,
    PackageLockType,
)
from apps.interop.domain.sce_sandbox_orchestrator import (
    run_in_statistical_sandbox,
)
from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.lakehouse_repository import LakehouseRepository
from apps.interop.infrastructure.models import Base
from apps.interop.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup clean in-memory database for SCE tests."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "sponsor_statistician",
    user_id: str = "user_stat_01",
    change_reason: str = "GxP CDR SCE testing",
    unblinded_access: bool = False,
) -> dict[str, str]:
    """Helper to generate valid gateway V2 signed headers."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        unblinded_access=unblinded_access,
    )


# =========================================================================
# Unit Tests: Package Lockfile Parsing, Cryptographic Hashing & Verification
# =========================================================================


class TestLockfileIntegrityVerification:
    """Test package lockfile parsing, deterministic SHA-256 digests, and tamper detection.

    @req:PRD-CDR-005
    @req:ADR-2199
    """

    def test_validate_uv_lock_parsing_and_digest(self) -> None:
        """Verify uv.lock (TOML) parsing, package extraction, and deterministic digest generation.

        @req:PRD-CDR-005
        @req:ADR-2199
        """
        uv_content = """version = 1
requires-python = ">=3.14"

[[package]]
name = "pandas"
version = "2.2.0"
wheels = [{ hash = "sha256:7f8a9b0c1d2e3f4a5b6c" }]

[[package]]
name = "numpy"
version = "1.26.4"
wheels = [{ hash = "sha256:1a2b3c4d5e6f7a8b9c0d" }]

[[package]]
name = "scipy"
version = "1.12.0"
wheels = [{ hash = "sha256:bb88cc99dd00ee11ff22" }]
"""
        res = validate_package_lockfile(
            lock_type=PackageLockType.UV_LOCK,
            content=uv_content,
            strict_qualification=True,
        )

        assert res.status == LockValidationStatus.VALID
        assert res.package_count == 3
        assert res.is_qualified is True
        assert len(res.lock_digest) == 64
        assert res.packages[0].name == "pandas"
        assert res.packages[0].version == "2.2.0"
        assert res.packages[0].checksum == "sha256:7f8a9b0c1d2e3f4a5b6c"

    def test_validate_renv_lock_parsing_and_digest(self) -> None:
        """Verify renv.lock (JSON) parsing and qualification checks.

        @req:PRD-CDR-005
        @req:ADR-2199
        """
        renv_content = """{
  "R": { "Version": "4.4.2" },
  "Packages": {
    "dplyr": {
      "Package": "dplyr",
      "Version": "1.1.4",
      "Source": "CRAN",
      "Hash": "9a77b8f9e1e2d3c4"
    },
    "rtables": {
      "Package": "rtables",
      "Version": "0.6.6",
      "Source": "CRAN",
      "Hash": "c4d3e2f1a8b7c6d5"
    },
    "survival": {
      "Package": "survival",
      "Version": "3.7-0",
      "Source": "CRAN",
      "Hash": "1122334455667788"
    }
  }
}"""
        res = validate_package_lockfile(
            lock_type=PackageLockType.RENV_LOCK,
            content=renv_content,
            strict_qualification=True,
        )

        assert res.status == LockValidationStatus.VALID
        assert res.package_count == 3
        assert res.is_qualified is True
        assert len(res.lock_digest) == 64

    def test_detect_tampered_lockfile(self) -> None:
        """Verify tampered lockfile fails validation against pre-registered expected digest.

        @req:PRD-CDR-005
        @req:ADR-2199
        """
        content = """version = 1\n[[package]]\nname = "pandas"\nversion = "2.2.0"\n"""
        val_original = validate_package_lockfile(PackageLockType.UV_LOCK, content)
        original_digest = val_original.lock_digest

        # Tampered content with modified version
        content_tampered = (
            """version = 1\n[[package]]\nname = "pandas"\nversion = "2.2.1"\n"""
        )
        val_tampered = validate_package_lockfile(
            lock_type=PackageLockType.UV_LOCK,
            content=content_tampered,
            expected_digest=original_digest,
        )

        assert val_tampered.status == LockValidationStatus.TAMPERED
        assert "mismatch" in val_tampered.details.lower()

    def test_detect_unqualified_package(self) -> None:
        """Verify strict qualification flags unapproved third-party packages.

        @req:PRD-CDR-005
        @req:ADR-2199
        """
        content = """version = 1\n[[package]]\nname = "unapproved_hack_pkg"\nversion = "0.0.1"\n"""
        res = validate_package_lockfile(
            lock_type=PackageLockType.UV_LOCK,
            content=content,
            strict_qualification=True,
        )

        assert res.status == LockValidationStatus.UNQUALIFIED_PACKAGE
        assert res.is_qualified is False
        assert "unapproved_hack_pkg" in res.unqualified_packages

    def test_detect_invalid_syntax(self) -> None:
        """Verify invalid TOML/JSON syntax produces INVALID_SYNTAX status.

        @req:PRD-CDR-005
        @req:ADR-2199
        """
        res = validate_package_lockfile(
            PackageLockType.UV_LOCK, "INVALID { [[[ TOML ::::"
        )
        assert res.status == LockValidationStatus.INVALID_SYNTAX
        assert res.package_count == 0


# =========================================================================
# Unit Tests: Database Lock Authorization Tokens & Unblinded Analysis Gating
# =========================================================================


class TestDatabaseLockAuthorization:
    """Test formal database lock authorization tokens and double-blind gating.

    @req:PRD-CDR-006
    @req:ADR-2199
    """

    def test_issue_and_verify_database_lock_token(self) -> None:
        """Verify cryptographic issuance and validation of database lock authorization token.

        @req:PRD-CDR-006
        @req:ADR-2199
        """
        token = issue_database_lock_token(
            study_id="STUDY-101",
            lock_id="LOCK-STUDY-101-FINAL",
            milestone="DATABASE_LOCK",
            issued_to="lead_unblinded_stat",
            reason="Formally authorized for interim efficacy review by IDMC",
            expires_in_hours=48,
        )

        assert token.token_id.startswith("DBLOCK-AUTH-")
        assert token.study_id == "STUDY-101"
        assert token.is_revoked is False
        assert len(token.signature_hash) == 64

        # Validate with permitted unblinded role
        is_valid, msg = verify_database_lock_token(
            token=token,
            requesting_study_id="STUDY-101",
            requesting_user_role="unblinded_statistician",
        )
        assert is_valid is True
        assert "successfully validated" in msg.lower()

    def test_reject_unauthorized_role(self) -> None:
        """Verify standard blinded roles are rejected when attempting to use unblinded token.

        @req:PRD-CDR-006
        @req:ADR-2199
        """
        token = issue_database_lock_token(
            study_id="STUDY-101",
            lock_id="LOCK-101",
            milestone="DATABASE_LOCK",
            issued_to="stat_user",
            reason="IDMC review",
        )

        is_valid, msg = verify_database_lock_token(
            token=token,
            requesting_study_id="STUDY-101",
            requesting_user_role="site_crc",
        )
        assert is_valid is False
        assert "not authorized" in msg.lower()

    def test_reject_expired_or_revoked_token(self) -> None:
        """Verify expired or revoked authorization tokens are rejected.

        @req:PRD-CDR-006
        @req:ADR-2199
        """
        token = issue_database_lock_token(
            study_id="STUDY-101",
            lock_id="LOCK-101",
            milestone="DATABASE_LOCK",
            issued_to="stat_user",
            reason="Testing expiry",
        )

        # 1. Manually expire token
        token.expires_at = datetime.now(UTC) - timedelta(minutes=5)
        is_valid, msg = verify_database_lock_token(
            token=token,
            requesting_study_id="STUDY-101",
            requesting_user_role="unblinded_statistician",
        )
        assert is_valid is False
        assert "expired" in msg.lower()

        # 2. Revoked token
        token.expires_at = datetime.now(UTC) + timedelta(hours=2)
        token.is_revoked = True
        is_valid2, msg2 = verify_database_lock_token(
            token=token,
            requesting_study_id="STUDY-101",
            requesting_user_role="unblinded_statistician",
        )
        assert is_valid2 is False
        assert "revoked" in msg2.lower()


# =========================================================================
# Unit Tests: Sandbox Execution Orchestrator (R & Python)
# =========================================================================


class TestSandboxExecutionOrchestrator:
    """Test isolated containerized execution of validated Python and R scripts.

    @req:PRD-CDR-004
    @req:PRD-CDR-005
    @req:PRD-CDR-006
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_python_statistical_sandbox_execution(self) -> None:
        """Verify Python execution in sandbox with data injection, artifact generation, and manifest signing.

        @req:PRD-CDR-004
        @req:PRD-CDR-005
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        rec1 = normalize_edc_ecrf(
            "STUDY-TEST-01",
            EDCEcrfIngestItem(
                subject_id="SUBJ-001",
                visit_id="V1",
                form_oid="F_VS",
                field_name="SYSBP",
                value=124.0,
                unit="mmHg",
                recorded_at=now,
                domain="VS",
            ),
        )
        rec2 = normalize_edc_ecrf(
            "STUDY-TEST-01",
            EDCEcrfIngestItem(
                subject_id="SUBJ-002",
                visit_id="V1",
                form_oid="F_VS",
                field_name="SYSBP",
                value=136.0,
                unit="mmHg",
                recorded_at=now,
                domain="VS",
            ),
        )

        python_script = """
import csv

# Read projected tabular data
with open("data_records.csv", mode="r") as infile:
    reader = csv.DictReader(infile)
    rows = list(reader)

values = [float(r["numeric_value"]) for r in rows if r["numeric_value"]]
mean_val = sum(values) / len(values) if values else 0.0

# Write output statistical table
with open("t_summary_stats.csv", mode="w", newline="") as outfile:
    writer = csv.writer(outfile)
    writer.writerow(["METRIC", "N", "MEAN"])
    writer.writerow(["SYSBP", len(values), mean_val])

# Write listing
with open("listing_subjects.txt", mode="w") as outlist:
    outlist.write(f"Evaluated {len(rows)} observations. Mean SYSBP: {mean_val}\\n")

print(f"Computed mean SYSBP: {mean_val} across {len(values)} subjects.")
"""

        manifest = await run_in_statistical_sandbox(
            run_id="RUN-TEST-PY-01",
            study_id="STUDY-TEST-01",
            program_id="PROG-PY-01",
            program_name="t_summary_vitals.py",
            program_version="1.0.0",
            language=ExecutionLanguage.PYTHON,
            source_code=python_script,
            lockfile_content='version = 1\n[[package]]\nname = "pandas"\nversion = "2.2.0"\n',
            lock_type=PackageLockType.UV_LOCK,
            lockfile_digest="a" * 64,
            snapshot_id="SNAP-TEST-01",
            snapshot_version_tag="v1.0",
            snapshot_digest="b" * 64,
            snapshot_records=[rec1, rec2],
            is_unblinded_run=False,
            database_lock_token_id=None,
            executed_by="lead_stat_01",
            executed_by_role="sponsor_statistician",
            config=ExecutionSandboxConfig(timeout_seconds=30),
        )

        assert manifest.run_id == "RUN-TEST-PY-01"
        assert manifest.status == ExecutionStatus.COMPLETED
        assert manifest.exit_code == 0
        assert "Computed mean SYSBP: 130.0" in manifest.stdout_capture
        assert len(manifest.output_artifacts) == 2

        # Check artifact details
        table_art = next(
            a for a in manifest.output_artifacts if a.name == "t_summary_stats.csv"
        )
        assert table_art.size_bytes > 0
        assert len(table_art.sha256_digest) == 64
        assert "SYSBP,2,130.0" in (table_art.content_preview or "")

        # Check Electronic Signature Block
        assert manifest.signature_block.signer_id == "lead_stat_01"
        assert len(manifest.signature_block.hmac_signature) == 64
        assert len(manifest.manifest_sha256_seal) == 64

    @pytest.mark.asyncio
    async def test_r_statistical_sandbox_execution(self) -> None:
        """Verify R statistical analysis execution with artifacts and manifest seal.

        @req:PRD-CDR-004
        @req:PRD-CDR-005
        @req:ADR-2199
        """
        now = datetime.now(UTC)
        rec1 = normalize_edc_ecrf(
            "STUDY-TEST-02",
            EDCEcrfIngestItem(
                subject_id="SUBJ-001",
                visit_id="V1",
                form_oid="F_VS",
                field_name="SYSBP",
                value=120.0,
                recorded_at=now,
                domain="VS",
            ),
        )

        r_script = """
# R Statistical Script for Summary Statistics
data <- read.csv("data_records.csv")
cat(sprintf("Loaded %d records\\n", nrow(data)))
"""

        manifest = await run_in_statistical_sandbox(
            run_id="RUN-TEST-R-01",
            study_id="STUDY-TEST-02",
            program_id="PROG-R-01",
            program_name="t_14_1_demog.R",
            program_version="1.0.0",
            language=ExecutionLanguage.R,
            source_code=r_script,
            lockfile_content='{\n  "R": { "Version": "4.4.2" }\n}\n',
            lock_type=PackageLockType.RENV_LOCK,
            lockfile_digest="c" * 64,
            snapshot_id="SNAP-TEST-02",
            snapshot_version_tag="v1.0",
            snapshot_digest="d" * 64,
            snapshot_records=[rec1],
            is_unblinded_run=False,
            database_lock_token_id=None,
            executed_by="r_statistician",
            executed_by_role="sponsor_statistician",
        )

        assert manifest.status == ExecutionStatus.COMPLETED
        assert manifest.language == ExecutionLanguage.R
        assert len(manifest.output_artifacts) >= 1
        assert len(manifest.manifest_sha256_seal) == 64

    @pytest.mark.asyncio
    async def test_sandbox_timeout_enforcement(self) -> None:
        """Verify bounded timeout kills long-running or hanging scripts and sets TIMEOUT status.

        @req:PRD-CDR-004
        @req:ADR-2199
        """
        infinite_script = """
import time
time.sleep(10)
"""
        manifest = await run_in_statistical_sandbox(
            run_id="RUN-TIMEOUT-01",
            study_id="STUDY-TEST-01",
            program_id="PROG-HANG",
            program_name="hang_script.py",
            program_version="1.0.0",
            language=ExecutionLanguage.PYTHON,
            source_code=infinite_script,
            lockfile_content=None,
            lock_type=PackageLockType.UV_LOCK,
            lockfile_digest="e" * 64,
            snapshot_id="SNAP-01",
            snapshot_version_tag="v1.0",
            snapshot_digest="f" * 64,
            snapshot_records=[],
            is_unblinded_run=False,
            database_lock_token_id=None,
            executed_by="stat_user",
            executed_by_role="sponsor_statistician",
            config=ExecutionSandboxConfig(timeout_seconds=2),
        )

        assert manifest.status == ExecutionStatus.TIMEOUT
        assert "timeout" in manifest.stderr_capture.lower()


# =========================================================================
# Integration Tests: End-to-End Service Layer & Database Lifecycle
# =========================================================================


class TestStatisticalComputingServiceIntegration:
    """Test full database and service layer lifecycle for the Statistical Computing Environment.

    @req:PRD-CDR-004
    @req:PRD-CDR-005
    @req:PRD-CDR-006
    @req:ADR-2199
    """

    @pytest.mark.asyncio
    async def test_full_sce_lifecycle_with_unblinded_gating(self) -> None:
        """Verify complete SCE workflow: data ingestion -> snapshot -> program authoring -> unblinded execution -> verification.

        @req:PRD-CDR-004
        @req:PRD-CDR-005
        @req:PRD-CDR-006
        @req:ADR-2199
        """
        async with db_manager.session_maker() as session:
            repo = LakehouseRepository(session)
            lakehouse_svc = LakehouseService(repo)
            sce_svc = StatisticalComputingService(repo)

            now = datetime.now(UTC)

            # 1. Ingest Clinical Data (including unblinded treatment arm)
            ingest_payload = MultiStreamIngestPayload(
                study_id="STUDY-SCE-99",
                stream_type=DataStreamType.EDC_ECRF,
                edc_items=[
                    EDCEcrfIngestItem(
                        subject_id="SUBJ-101",
                        visit_id="V1",
                        form_oid="F_VS",
                        field_name="SYSBP",
                        value=120.0,
                        unit="mmHg",
                        recorded_at=now,
                        domain="VS",
                    ),
                    EDCEcrfIngestItem(
                        subject_id="SUBJ-101",
                        visit_id="V1",
                        form_oid="F_RAND",
                        field_name="TRT01P",
                        value="ACTIVE_DRUG_200MG",
                        recorded_at=now,
                        is_unblinded=True,
                    ),
                ],
                reason_for_ingest="Baseline EDC data for SCE integration test",
            )
            await lakehouse_svc.ingest_stream_payload(
                payload=ingest_payload, user_id="cra_01", user_role="cra"
            )

            # 2. Seal Snapshot Pool
            snapshot = await lakehouse_svc.create_version_pinned_snapshot(
                study_id="STUDY-SCE-99",
                version_tag="v1.0.0-INTERIM-DSMB",
                milestone=LockMilestoneType.INTERIM_ANALYSIS_1,
                lock_id="LOCK-SCE-99-INT1",
                reason_for_snapshot="DSMB Interim Lock",
                filter_criteria=None,
                user_id="lead_stat",
                user_role="sponsor_statistician",
            )

            # 3. Create Validated Statistical Program
            script_code = """
import csv

with open("data_records.csv") as f:
    rows = list(csv.DictReader(f))

treatment_vals = [r["text_value"] for r in rows if r["is_unblinded"] == "True"]

with open("t_unblinded_arms.csv", "w", newline="") as out:
    writer = csv.writer(out)
    writer.writerow(["RECORD_TYPE", "CAPTURED_VALUE"])
    for val in treatment_vals:
        writer.writerow(["TREATMENT_ARM", val])

print(f"Processed treatment arms: {treatment_vals}")
"""
            program = await sce_svc.create_program(
                study_id="STUDY-SCE-99",
                name="t_treatment_allocation.py",
                title="Analysis of Treatment Arm Allocation",
                description="Interim unblinded efficacy evaluation",
                language=ExecutionLanguage.PYTHON,
                version="1.0.0",
                source_code=script_code,
                lockfile_content='version = 1\n[[package]]\nname = "pandas"\nversion = "2.2.0"\n',
                lock_type=PackageLockType.UV_LOCK,
                target_domain="ADSL",
                reason_for_change="Initial authoring for DSMB review",
                user_id="lead_stat",
                user_role="sponsor_statistician",
            )
            assert program.program_id.startswith("PROG-")

            # 4. Attempt Unblinded Execution WITHOUT Lock Token (Must Fail)
            with pytest.raises(UnblindedAccessDeniedError):
                await sce_svc.execute_statistical_program(
                    study_id="STUDY-SCE-99",
                    program_id=program.program_id,
                    snapshot_id=snapshot.snapshot_id,
                    is_unblinded=True,
                    database_lock_token_id=None,  # Missing token
                    user_id="unblinded_stat_01",
                    user_role="unblinded_statistician",
                )

            # 5. Issue Database Lock Authorization Token
            lock_token = await sce_svc.issue_database_lock_token(
                study_id="STUDY-SCE-99",
                lock_id="LOCK-SCE-99-INT1",
                milestone="INTERIM_ANALYSIS_1",
                issued_to="unblinded_stat_01",
                reason="Authorized by IDMC Charter for unblinded analysis",
                expires_in_hours=24,
                user_id="sponsor_admin_01",
                user_role="sponsor_admin",
            )
            assert lock_token.token_id.startswith("DBLOCK-AUTH-")

            # 6. Execute Unblinded Statistical Analysis WITH Valid Token (Must Succeed)
            run_manifest = await sce_svc.execute_statistical_program(
                study_id="STUDY-SCE-99",
                program_id=program.program_id,
                snapshot_id=snapshot.snapshot_id,
                is_unblinded=True,
                database_lock_token_id=lock_token.token_id,
                user_id="unblinded_stat_01",
                user_role="unblinded_statistician",
                reason_for_execution="Executing unblinded DSMB efficacy run",
            )

            assert run_manifest.status == ExecutionStatus.COMPLETED
            assert run_manifest.is_unblinded_run is True
            assert run_manifest.database_lock_token_id == lock_token.token_id
            assert "ACTIVE_DRUG_200MG" in run_manifest.stdout_capture

            # 7. Cryptographically Verify Manifest Seal
            verification = await sce_svc.verify_run_manifest(
                run_id=run_manifest.run_id,
                user_id="auditor_01",
                user_role="auditor",
            )
            assert verification["is_valid"] is True
            assert verification["seal_valid"] is True
            assert verification["signature_valid"] is True
            assert verification["stored_seal"] == run_manifest.manifest_sha256_seal

            # 8. Check 21 CFR Part 11 Audit Trail
            logs = await repo.get_audit_trail(snapshot.snapshot_id)
            assert any(
                entry["action"]
                in (
                    "EXECUTION_COMPLETE",
                    "UNBLINDED_ANALYSIS_ACCESS",
                    "MANIFEST_VERIFY",
                )
                for entry in logs
            )


# =========================================================================
# Integration Tests: FastAPI REST Endpoints via TestClient
# =========================================================================


class TestStatisticalComputingRESTAPI:
    """Test full HTTP API surface for the Statistical Computing Environment.

    @req:PRD-CDR-004
    @req:PRD-CDR-005
    @req:PRD-CDR-006
    @req:ADR-2199
    """

    def test_sce_rest_api_workflow(self) -> None:
        """Verify REST API program registration, token issuance, execution, verification, and artifact retrieval.

        @req:PRD-CDR-004
        @req:PRD-CDR-005
        @req:PRD-CDR-006
        @req:ADR-2199
        """
        client = TestClient(app)
        headers_stat = get_auth_headers(
            roles="sponsor_statistician", user_id="stat_lead"
        )
        headers_unblinded = get_auth_headers(
            roles="unblinded_statistician",
            user_id="unblinded_user",
            unblinded_access=True,
        )

        now_iso = datetime.now(UTC).isoformat()

        # 1. Ingest Data & Create Snapshot
        client.post(
            "/api/v1/interop/lakehouse/ingest",
            json={
                "study_id": "STUDY-REST-SCE",
                "stream_type": "EDC_ECRF",
                "edc_items": [
                    {
                        "subject_id": "SUBJ-901",
                        "visit_id": "V1",
                        "form_oid": "F_VS",
                        "field_name": "PULSE",
                        "value": 75.0,
                        "unit": "bpm",
                        "recorded_at": now_iso,
                        "domain": "VS",
                    },
                    {
                        "subject_id": "SUBJ-901",
                        "visit_id": "V1",
                        "form_oid": "F_RAND",
                        "field_name": "ARMCD",
                        "value": "ACTIVE_ARM",
                        "recorded_at": now_iso,
                        "is_unblinded": True,
                    },
                ],
                "reason_for_ingest": "REST API Ingest",
            },
            headers=headers_stat,
        )

        snap_resp = client.post(
            "/api/v1/interop/lakehouse/snapshots",
            json={
                "study_id": "STUDY-REST-SCE",
                "version_tag": "v1.0.0-REST-LOCK",
                "milestone": "DATABASE_LOCK",
                "lock_id": "LOCK-REST-01",
                "reason_for_snapshot": "Locking for REST API SCE tests",
            },
            headers=headers_stat,
        )
        assert snap_resp.status_code == 201
        snapshot_id = snap_resp.json()["snapshot"]["snapshot_id"]

        # 2. POST /api/v1/interop/sce/validate-lockfile
        lock_resp = client.post(
            "/api/v1/interop/sce/validate-lockfile",
            json={
                "lock_type": "UV_LOCK",
                "content": 'version = 1\n[[package]]\nname = "pandas"\nversion = "2.2.0"\n',
                "strict_qualification": True,
            },
            headers=headers_stat,
        )
        assert lock_resp.status_code == 200
        assert lock_resp.json()["result"]["status"] == "VALID"

        # 3. POST /api/v1/interop/sce/programs
        prog_resp = client.post(
            "/api/v1/interop/sce/programs",
            json={
                "study_id": "STUDY-REST-SCE",
                "name": "t_vitals_summary.py",
                "title": "Vital Signs Descriptive Summary",
                "description": "Computes baseline vital signs summary",
                "language": "PYTHON",
                "version": "1.0.0",
                "source_code": "import csv\nwith open('data_records.csv') as f: print('Processed:', len(list(csv.reader(f))))\nwith open('t_out.csv', 'w') as o: o.write('COL1,COL2\\n1,2\\n')",
                "target_domain": "VS",
                "reason_for_change": "Initial REST authoring",
            },
            headers=headers_stat,
        )
        assert prog_resp.status_code == 201
        program_id = prog_resp.json()["program"]["program_id"]

        # 4. GET /api/v1/interop/sce/programs
        list_prog_resp = client.get(
            "/api/v1/interop/sce/programs",
            params={"study_id": "STUDY-REST-SCE"},
            headers=headers_stat,
        )
        assert list_prog_resp.status_code == 200
        assert list_prog_resp.json()["total"] >= 1

        # 5. POST /api/v1/interop/sce/lock-tokens
        token_resp = client.post(
            "/api/v1/interop/sce/lock-tokens",
            json={
                "study_id": "STUDY-REST-SCE",
                "lock_id": "LOCK-REST-01",
                "milestone": "DATABASE_LOCK",
                "issued_to": "unblinded_user",
                "reason": "Authorized for interim DSMB efficacy evaluation",
                "expires_in_hours": 48,
            },
            headers=headers_unblinded,
        )
        assert token_resp.status_code == 201
        token_id = token_resp.json()["token"]["token_id"]

        # 6. POST /api/v1/interop/sce/execute (Blinded Run)
        exec_blind_resp = client.post(
            "/api/v1/interop/sce/execute",
            json={
                "study_id": "STUDY-REST-SCE",
                "program_id": program_id,
                "snapshot_id": snapshot_id,
                "is_unblinded": False,
                "reason_for_execution": "Blinded baseline run",
            },
            headers=headers_stat,
        )
        assert exec_blind_resp.status_code == 200
        run_manifest = exec_blind_resp.json()["manifest"]
        run_id = run_manifest["run_id"]
        assert run_manifest["status"] == "COMPLETED"

        # 7. POST /api/v1/interop/sce/execute (Unblinded Run with Token)
        exec_unblind_resp = client.post(
            "/api/v1/interop/sce/execute",
            json={
                "study_id": "STUDY-REST-SCE",
                "program_id": program_id,
                "snapshot_id": snapshot_id,
                "is_unblinded": True,
                "database_lock_token_id": token_id,
                "reason_for_execution": "Unblinded interim run",
            },
            headers=headers_unblinded,
        )
        if exec_unblind_resp.status_code != 200:
            print(
                "EXEC_UNBLIND ERROR:",
                exec_unblind_resp.status_code,
                exec_unblind_resp.json(),
            )
        assert exec_unblind_resp.status_code == 200
        assert exec_unblind_resp.json()["manifest"]["is_unblinded_run"] is True

        # 8. GET /api/v1/interop/sce/runs
        runs_resp = client.get(
            "/api/v1/interop/sce/runs",
            params={"study_id": "STUDY-REST-SCE"},
            headers=headers_stat,
        )
        assert runs_resp.status_code == 200
        assert runs_resp.json()["total"] == 2

        # 9. GET /api/v1/interop/sce/runs/{run_id}
        run_detail_resp = client.get(
            f"/api/v1/interop/sce/runs/{run_id}",
            headers=headers_stat,
        )
        assert run_detail_resp.status_code == 200
        assert run_detail_resp.json()["run"]["run_id"] == run_id

        # 10. POST /api/v1/interop/sce/runs/{run_id}/verify
        verify_resp = client.post(
            f"/api/v1/interop/sce/runs/{run_id}/verify",
            headers=headers_stat,
        )
        assert verify_resp.status_code == 200
        assert verify_resp.json()["result"]["is_valid"] is True

        # 11. GET /api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}
        artifacts = run_manifest["output_artifacts"]
        if artifacts:
            art_id = artifacts[0]["artifact_id"]
            art_resp = client.get(
                f"/api/v1/interop/sce/runs/{run_id}/artifacts/{art_id}",
                headers=headers_stat,
            )
            assert art_resp.status_code == 200
            assert art_resp.json()["artifact_id"] == art_id

        # 12. POST /api/v1/interop/sce/lock-tokens/{token_id}/revoke
        revoke_resp = client.post(
            f"/api/v1/interop/sce/lock-tokens/{token_id}/revoke",
            params={"reason": "Study milestone finished"},
            headers=headers_unblinded,
        )
        assert revoke_resp.status_code == 200
        assert revoke_resp.json()["revoked"] is True
