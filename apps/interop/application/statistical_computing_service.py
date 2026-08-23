"""Application service orchestrating the Validated Containerized Statistical Computing Environment (SCE).

Provides biostatisticians with an isolated, 21 CFR Part 11 compliant analytical workspace:
- Manages version-controlled R and Python statistical programs.
- Verifies package lockfile integrity (uv.lock, renv.lock) for 100% reproducible statistical runs.
- Gates unblinded statistical analysis behind formal cryptographic database lock authorization tokens.
- Orchestrates containerized/sandboxed execution with bounded memory, timeouts, and network isolation.
- Generates and verifies cryptographically signed execution run manifests and Part 11 electronic signatures.

Requirements: PRD-CDR-004, PRD-CDR-005, PRD-CDR-006, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

import hashlib
import hmac
import os
import uuid
from datetime import UTC, datetime
from typing import Any

from apps.interop.domain.ports import LakehouseRepositoryPort
from apps.interop.domain.sce_lock_token import (
    issue_database_lock_token as domain_issue_lock_token,
)
from apps.interop.domain.sce_lock_token import (
    verify_database_lock_token,
)
from apps.interop.domain.sce_lockfile_verifier import (
    validate_package_lockfile,
)
from apps.interop.domain.sce_models import (
    DatabaseLockAuthToken,
    ExecutionLanguage,
    ExecutionRunManifest,
    ExecutionSandboxConfig,
    LockValidationStatus,
    PackageLockType,
    PackageLockValidationResult,
    SCEAction,
    StatisticalProgram,
)
from apps.interop.domain.sce_sandbox_orchestrator import (
    compute_manifest_sha256_seal,
    run_in_statistical_sandbox,
)


class SCEError(Exception):
    """Base exception for Statistical Computing Environment errors."""

    pass


class ProgramNotFoundError(SCEError):
    """Raised when a requested statistical program does not exist."""

    pass


class LockfileIntegrityError(SCEError):
    """Raised when package lockfile validation fails or tampered dependencies are detected."""

    pass


class UnblindedAccessDeniedError(SCEError):
    """Raised when unblinded statistical analysis is attempted without a valid database lock token."""

    pass


class ExecutionRunNotFoundError(SCEError):
    """Raised when an execution run record is not found."""

    pass


def _get_signing_secret() -> bytes:
    """Retrieve cryptographic signing secret."""
    secret = (
        os.getenv("SIGNING_SECRET")
        or os.getenv("GATEWAY_SECRET")
        or "cadence-default-sce-signing-secret"
    )
    return secret.encode("utf-8")


class StatisticalComputingService:
    """Application service for the validated Statistical Computing Environment."""

    def __init__(self, repository: LakehouseRepositoryPort) -> None:
        self.repository = repository

    async def validate_lockfile(
        self,
        lock_type: PackageLockType | str,
        content: str,
        expected_digest: str | None = None,
        strict_qualification: bool = False,
    ) -> PackageLockValidationResult:
        """Parses and validates package lockfile integrity and GxP qualification."""
        return validate_package_lockfile(
            lock_type=lock_type,
            content=content,
            expected_digest=expected_digest,
            strict_qualification=strict_qualification,
        )

    async def create_program(
        self,
        study_id: str,
        name: str,
        title: str,
        description: str,
        language: ExecutionLanguage | str,
        version: str,
        source_code: str,
        lockfile_content: str | None,
        lock_type: PackageLockType | str = PackageLockType.UV_LOCK,
        target_domain: str | None = None,
        reason_for_change: str = "Initial statistical program authoring",
        user_id: str = "statistician",
        user_role: str = "sponsor_statistician",
    ) -> StatisticalProgram:
        """Creates and persists a version-controlled statistical program with validated package lockfile."""
        lang_enum = ExecutionLanguage(str(language).upper())
        lock_enum = PackageLockType(str(lock_type).upper())

        # Validate lockfile if provided, or supply minimal validated lockfile
        if lockfile_content:
            val_res = validate_package_lockfile(lock_enum, lockfile_content)
            if val_res.status in (
                LockValidationStatus.INVALID_SYNTAX,
                LockValidationStatus.TAMPERED,
            ):
                raise LockfileIntegrityError(
                    f"Package lockfile validation failed: {val_res.details}"
                )
            lock_digest = val_res.lock_digest
        else:
            lockfile_content = (
                '# Default minimal lock\nversion = 1\n[[package]]\nname = "pandas"\nversion = "2.2.0"\n'
                if lang_enum == ExecutionLanguage.PYTHON
                else '{\n  "R": { "Version": "4.4.2" },\n  "Packages": {}\n}\n'
            )
            lock_digest = hashlib.sha256(lockfile_content.encode("utf-8")).hexdigest()

        code_hash = hashlib.sha256(source_code.encode("utf-8")).hexdigest()
        program_id = f"PROG-{uuid.uuid4().hex[:10].upper()}"

        program = StatisticalProgram(
            program_id=program_id,
            study_id=study_id,
            name=name,
            title=title,
            description=description,
            language=lang_enum,
            version=version,
            source_code=source_code,
            code_hash=code_hash,
            lockfile_content=lockfile_content,
            lock_type=lock_enum,
            lock_digest=lock_digest,
            target_domain=target_domain,
            created_by=user_id,
            created_at=datetime.now(UTC),
            reason_for_change=reason_for_change,
        )

        saved = await self.repository.save_program(program)

        # 21 CFR Part 11 Audit Trail
        seal_payload = f"{program_id}:{code_hash}:{lock_digest}:{user_id}:{datetime.now(UTC).isoformat()}"
        seal = hashlib.sha256(seal_payload.encode("utf-8")).hexdigest()

        await self.repository.save_audit_log(
            audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
            timestamp=datetime.now(UTC),
            user_id=user_id,
            user_role=user_role,
            action=SCEAction.PROGRAM_CREATE.value,
            study_id=study_id,
            snapshot_id=None,
            details={
                "program_id": program_id,
                "name": name,
                "language": lang_enum.value,
                "version": version,
                "code_hash": code_hash,
                "lock_digest": lock_digest,
                "target_domain": target_domain,
            },
            reason_for_change=reason_for_change,
            sha256_seal=seal,
        )

        return saved

    async def get_program(self, program_id: str) -> StatisticalProgram:
        """Retrieve statistical program by ID."""
        program = await self.repository.get_program_by_id(program_id)
        if not program:
            raise ProgramNotFoundError(f"Statistical program '{program_id}' not found.")
        return program

    async def list_programs(self, study_id: str) -> list[StatisticalProgram]:
        """List all statistical programs for a study."""
        return await self.repository.list_programs(study_id)

    async def issue_database_lock_token(
        self,
        study_id: str,
        lock_id: str,
        milestone: str,
        issued_to: str,
        reason: str,
        expires_in_hours: int = 24,
        authorized_roles: list[str] | None = None,
        user_id: str = "sponsor_admin",
        user_role: str = "sponsor_admin",
    ) -> DatabaseLockAuthToken:
        """Issues a cryptographic database lock authorization token for unblinded analysis."""
        token = domain_issue_lock_token(
            study_id=study_id,
            lock_id=lock_id,
            milestone=milestone,
            issued_to=issued_to,
            reason=reason,
            expires_in_hours=expires_in_hours,
            authorized_roles=authorized_roles,
        )

        saved = await self.repository.save_lock_token(token)

        # Audit log
        seal = hashlib.sha256(
            f"{token.token_id}:{token.signature_hash}:{user_id}".encode()
        ).hexdigest()

        await self.repository.save_audit_log(
            audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
            timestamp=datetime.now(UTC),
            user_id=user_id,
            user_role=user_role,
            action=SCEAction.LOCK_TOKEN_ISSUE.value,
            study_id=study_id,
            snapshot_id=None,
            details={
                "token_id": token.token_id,
                "lock_id": lock_id,
                "milestone": milestone,
                "issued_to": issued_to,
                "expires_at": token.expires_at.isoformat(),
                "authorized_roles": token.authorized_roles,
            },
            reason_for_change=reason,
            sha256_seal=seal,
        )

        return saved

    async def revoke_database_lock_token(
        self,
        token_id: str,
        reason: str,
        user_id: str,
        user_role: str,
    ) -> bool:
        """Revokes an active database lock authorization token."""
        token = await self.repository.get_lock_token_by_id(token_id)
        if not token:
            return False

        success = await self.repository.revoke_lock_token(token_id)
        if success:
            seal = hashlib.sha256(f"REVOKE:{token_id}:{user_id}".encode()).hexdigest()
            await self.repository.save_audit_log(
                audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
                timestamp=datetime.now(UTC),
                user_id=user_id,
                user_role=user_role,
                action=SCEAction.LOCK_TOKEN_REVOKE.value,
                study_id=token.study_id,
                snapshot_id=None,
                details={"token_id": token_id, "revoked_by": user_id},
                reason_for_change=reason,
                sha256_seal=seal,
            )
        return success

    async def list_database_lock_tokens(
        self, study_id: str
    ) -> list[DatabaseLockAuthToken]:
        """List active authorization tokens for a study."""
        return await self.repository.list_lock_tokens(study_id)

    async def execute_statistical_program(
        self,
        study_id: str,
        program_id: str,
        snapshot_id: str,
        is_unblinded: bool = False,
        database_lock_token_id: str | None = None,
        sandbox_config: ExecutionSandboxConfig | None = None,
        user_id: str = "statistician",
        user_role: str = "sponsor_statistician",
        reason_for_execution: str = "Execution of statistical analysis program",
    ) -> ExecutionRunManifest:
        """Orchestrates an isolated, sandboxed execution run with GxP reproducibility and signed manifests."""
        # 1. Fetch Program
        program = await self.get_program(program_id)
        if program.study_id != study_id:
            raise ProgramNotFoundError(
                f"Program '{program_id}' belongs to study '{program.study_id}', not '{study_id}'."
            )

        # 2. Fetch Snapshot & Records
        records, snapshot = await self.repository.get_snapshot_records(snapshot_id)
        if not snapshot:
            raise ProgramNotFoundError(
                f"Clinical data snapshot '{snapshot_id}' not found."
            )

        # 3. Gate Unblinded Analysis
        if is_unblinded:
            if not database_lock_token_id:
                # Log unauthorized unblinded access attempt
                seal = hashlib.sha256(
                    f"DENIED:{study_id}:{program_id}:{user_id}".encode()
                ).hexdigest()
                await self.repository.save_audit_log(
                    audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
                    timestamp=datetime.now(UTC),
                    user_id=user_id,
                    user_role=user_role,
                    action=SCEAction.UNBLINDED_ACCESS_DENIED.value,
                    study_id=study_id,
                    snapshot_id=snapshot_id,
                    details={
                        "program_id": program_id,
                        "reason": "Unblinded analysis requested without database_lock_token_id",
                    },
                    reason_for_change=reason_for_execution,
                    sha256_seal=seal,
                )
                raise UnblindedAccessDeniedError(
                    "Unblinded statistical analysis requires a valid formal database lock authorization token."
                )

            token = await self.repository.get_lock_token_by_id(database_lock_token_id)
            if not token:
                raise UnblindedAccessDeniedError(
                    f"Database lock token '{database_lock_token_id}' does not exist."
                )

            is_valid, reject_msg = verify_database_lock_token(
                token=token,
                requesting_study_id=study_id,
                requesting_user_role=user_role,
            )
            if not is_valid:
                seal = hashlib.sha256(
                    f"DENIED:{database_lock_token_id}:{user_id}".encode()
                ).hexdigest()
                await self.repository.save_audit_log(
                    audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
                    timestamp=datetime.now(UTC),
                    user_id=user_id,
                    user_role=user_role,
                    action=SCEAction.UNBLINDED_ACCESS_DENIED.value,
                    study_id=study_id,
                    snapshot_id=snapshot_id,
                    details={
                        "token_id": database_lock_token_id,
                        "rejection_reason": reject_msg,
                    },
                    reason_for_change=reason_for_execution,
                    sha256_seal=seal,
                )
                raise UnblindedAccessDeniedError(reject_msg)

            # Log authorized unblinded access
            seal_unblinded = hashlib.sha256(
                f"UNBLINDED_ACCESS:{database_lock_token_id}:{user_id}".encode()
            ).hexdigest()
            await self.repository.save_audit_log(
                audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
                timestamp=datetime.now(UTC),
                user_id=user_id,
                user_role=user_role,
                action=SCEAction.UNBLINDED_ANALYSIS_ACCESS.value,
                study_id=study_id,
                snapshot_id=snapshot_id,
                details={
                    "token_id": database_lock_token_id,
                    "lock_id": token.lock_id,
                    "milestone": token.milestone,
                    "program_id": program_id,
                },
                reason_for_change=reason_for_execution,
                sha256_seal=seal_unblinded,
            )

        # 4. Generate unique Run ID and dispatch sandbox execution
        run_id = f"RUN-{uuid.uuid4().hex[:12].upper()}"

        manifest = await run_in_statistical_sandbox(
            run_id=run_id,
            study_id=study_id,
            program_id=program.program_id,
            program_name=program.name,
            program_version=program.version,
            language=program.language,
            source_code=program.source_code,
            lockfile_content=program.lockfile_content,
            lock_type=program.lock_type,
            lockfile_digest=program.lock_digest,
            snapshot_id=snapshot.snapshot_id,
            snapshot_version_tag=snapshot.version_tag,
            snapshot_digest=snapshot.sha256_digest,
            snapshot_records=records,
            is_unblinded_run=is_unblinded,
            database_lock_token_id=database_lock_token_id,
            executed_by=user_id,
            executed_by_role=user_role,
            config=sandbox_config,
        )

        # 5. Persist Run and Manifest
        saved_manifest = await self.repository.save_execution_run(manifest)

        # 6. Record 21 CFR Part 11 Audit Trail
        await self.repository.save_audit_log(
            audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
            timestamp=datetime.now(UTC),
            user_id=user_id,
            user_role=user_role,
            action=SCEAction.EXECUTION_COMPLETE.value,
            study_id=study_id,
            snapshot_id=snapshot_id,
            details={
                "run_id": run_id,
                "program_id": program_id,
                "language": program.language.value,
                "status": manifest.status.value,
                "duration_ms": manifest.duration_ms,
                "exit_code": manifest.exit_code,
                "artifact_count": len(manifest.output_artifacts),
                "artifacts_digest": manifest.output_artifacts_digest,
                "manifest_sha256_seal": manifest.manifest_sha256_seal,
                "is_unblinded": is_unblinded,
            },
            reason_for_change=reason_for_execution,
            sha256_seal=manifest.manifest_sha256_seal,
        )

        return saved_manifest

    async def get_execution_run(self, run_id: str) -> ExecutionRunManifest:
        """Retrieve an execution run manifest by run ID."""
        run = await self.repository.get_execution_run_by_id(run_id)
        if not run:
            raise ExecutionRunNotFoundError(f"Execution run '{run_id}' not found.")
        return run

    async def list_execution_runs(
        self,
        study_id: str,
        program_id: str | None = None,
        snapshot_id: str | None = None,
    ) -> list[ExecutionRunManifest]:
        """List execution runs for a study filtered by program or snapshot."""
        return await self.repository.list_execution_runs(
            study_id=study_id, program_id=program_id, snapshot_id=snapshot_id
        )

    async def verify_run_manifest(
        self,
        run_id: str,
        user_id: str = "auditor",
        user_role: str = "auditor",
    ) -> dict[str, Any]:
        """Cryptographically verifies an execution run manifest seal and electronic signature block."""
        manifest = await self.get_execution_run(run_id)

        # 1. Recompute Root SHA-256 Seal
        computed_seal = compute_manifest_sha256_seal(
            run_id=manifest.run_id,
            study_id=manifest.study_id,
            program_id=manifest.program_id,
            snapshot_id=manifest.snapshot_id,
            script_code_digest=manifest.script_code_digest,
            snapshot_digest=manifest.snapshot_digest,
            lockfile_digest=manifest.lockfile_digest,
            container_image_digest=manifest.container_image_digest,
            output_artifacts_digest=manifest.output_artifacts_digest,
            executed_by=manifest.executed_by,
            status=manifest.status.value,
        )

        seal_valid = hmac.compare_digest(computed_seal, manifest.manifest_sha256_seal)

        # 2. Verify Electronic Signature HMAC
        secret = _get_signing_secret()
        sig_block = manifest.signature_block
        sig_payload = f"{manifest.manifest_id}:{sig_block.signer_id}:{sig_block.signer_role}:{sig_block.signed_at.isoformat()}:{manifest.manifest_sha256_seal}"
        expected_hmac = hmac.new(
            secret, sig_payload.encode("utf-8"), hashlib.sha256
        ).hexdigest()
        signature_valid = hmac.compare_digest(sig_block.hmac_signature, expected_hmac)

        overall_valid = seal_valid and signature_valid

        # Audit verification
        seal_log = hashlib.sha256(
            f"VERIFY:{run_id}:{overall_valid}:{user_id}".encode()
        ).hexdigest()

        await self.repository.save_audit_log(
            audit_id=f"AUD-{uuid.uuid4().hex[:12].upper()}",
            timestamp=datetime.now(UTC),
            user_id=user_id,
            user_role=user_role,
            action=SCEAction.MANIFEST_VERIFY.value,
            study_id=manifest.study_id,
            snapshot_id=manifest.snapshot_id,
            details={
                "run_id": run_id,
                "is_valid": overall_valid,
                "seal_valid": seal_valid,
                "signature_valid": signature_valid,
                "stored_seal": manifest.manifest_sha256_seal,
                "computed_seal": computed_seal,
            },
            reason_for_change="21 CFR Part 11 cryptographic manifest verification",
            sha256_seal=seal_log,
        )

        return {
            "run_id": run_id,
            "manifest_id": manifest.manifest_id,
            "is_valid": overall_valid,
            "seal_valid": seal_valid,
            "signature_valid": signature_valid,
            "stored_seal": manifest.manifest_sha256_seal,
            "computed_seal": computed_seal,
            "signer_id": sig_block.signer_id,
            "signer_role": sig_block.signer_role,
            "signed_at": sig_block.signed_at.isoformat(),
            "details": (
                "Manifest seal and electronic signature successfully verified bit-for-bit."
                if overall_valid
                else "Cryptographic verification failed: manifest or signature has been modified."
            ),
        }
