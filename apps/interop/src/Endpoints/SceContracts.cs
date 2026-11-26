using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Interop.Endpoints;

public record CreateProgramRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("language")] string Language = "PYTHON",
    [property: JsonPropertyName("version")] string Version = "1.0.0",
    [property: JsonPropertyName("source_code")] string SourceCode = "",
    [property: JsonPropertyName("lockfile_content")] string? LockfileContent = null,
    [property: JsonPropertyName("lock_type")] string LockType = "UV_LOCK",
    [property: JsonPropertyName("target_domain")] string? TargetDomain = null,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange = "Initial program authoring"
);

public record StatisticalProgramDto(
    [property: JsonPropertyName("program_id")] string ProgramId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("source_code")] string SourceCode,
    [property: JsonPropertyName("code_hash")] string CodeHash,
    [property: JsonPropertyName("lockfile_content")] string? LockfileContent,
    [property: JsonPropertyName("lock_type")] string LockType,
    [property: JsonPropertyName("lock_digest")] string LockDigest,
    [property: JsonPropertyName("target_domain")] string? TargetDomain,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange
);

public record ProgramResponseDto(
    [property: JsonPropertyName("program")] StatisticalProgramDto Program
);

public record ProgramListResponseDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("programs")] List<StatisticalProgramDto> Programs
);

public record ValidateLockfileRequestDto(
    [property: JsonPropertyName("lock_type")] string LockType,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("expected_digest")] string? ExpectedDigest = null,
    [property: JsonPropertyName("strict_qualification")] bool StrictQualification = false
);

public record PackageEntrySchemaDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("checksum")] string? Checksum = null,
    [property: JsonPropertyName("repository")] string? Repository = null,
    [property: JsonPropertyName("is_qualified")] bool IsQualified = true
);

public record LockfileValidationResultSchemaDto(
    [property: JsonPropertyName("lock_type")] string LockType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("lock_digest")] string LockDigest,
    [property: JsonPropertyName("package_count")] int PackageCount,
    [property: JsonPropertyName("packages")] List<PackageEntrySchemaDto> Packages,
    [property: JsonPropertyName("is_qualified")] bool IsQualified,
    [property: JsonPropertyName("unqualified_packages")] List<string> UnqualifiedPackages,
    [property: JsonPropertyName("details")] string Details
);

public record ValidateLockfileResponseDto(
    [property: JsonPropertyName("result")] LockfileValidationResultSchemaDto Result
);

public record IssueLockTokenRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("lock_id")] string LockId,
    [property: JsonPropertyName("milestone")] string Milestone,
    [property: JsonPropertyName("issued_to")] string IssuedTo,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("expires_in_hours")] double ExpiresInHours = 24.0
);

public record DatabaseLockAuthTokenDto(
    [property: JsonPropertyName("token_id")] string TokenId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("lock_id")] string LockId,
    [property: JsonPropertyName("milestone")] string Milestone,
    [property: JsonPropertyName("issued_to")] string IssuedTo,
    [property: JsonPropertyName("authorized_roles")] List<string> AuthorizedRoles,
    [property: JsonPropertyName("issued_at")] string IssuedAt,
    [property: JsonPropertyName("expires_at")] string ExpiresAt,
    [property: JsonPropertyName("signature_hash")] string SignatureHash,
    [property: JsonPropertyName("is_revoked")] bool IsRevoked
);

public record LockTokenResponseDto(
    [property: JsonPropertyName("token")] DatabaseLockAuthTokenDto Token
);

public record LockTokenListResponseDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("tokens")] List<DatabaseLockAuthTokenDto> Tokens
);

public record ExecutionSandboxConfigDto(
    [property: JsonPropertyName("max_memory_mb")] int MaxMemoryMb = 2048,
    [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds = 300,
    [property: JsonPropertyName("network_isolated")] bool NetworkIsolated = true,
    [property: JsonPropertyName("filesystem_readonly_root")] bool FilesystemReadonlyRoot = true,
    [property: JsonPropertyName("max_output_bytes")] long MaxOutputBytes = 10485760,
    [property: JsonPropertyName("docker_image_digest")] string DockerImageDigest = "sha256:cadence-sce-r4.4-py3.14-validated-base-2026",
    [property: JsonPropertyName("python_version")] string PythonVersion = "3.14.0",
    [property: JsonPropertyName("r_version")] string RVersion = "4.4.2"
);

public record ExecuteProgramRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("program_id")] string ProgramId,
    [property: JsonPropertyName("snapshot_id")] string SnapshotId,
    [property: JsonPropertyName("is_unblinded")] bool IsUnblinded = false,
    [property: JsonPropertyName("database_lock_token_id")] string? DatabaseLockTokenId = null,
    [property: JsonPropertyName("sandbox_config")] ExecutionSandboxConfigDto? SandboxConfig = null,
    [property: JsonPropertyName("reason_for_execution")] string ReasonForExecution = "Statistical analysis execution run"
);

public record OutputArtifactSchemaDto(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("artifact_type")] string ArtifactType,
    [property: JsonPropertyName("file_path")] string FilePath,
    [property: JsonPropertyName("mime_type")] string MimeType,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256_digest")] string Sha256Digest,
    [property: JsonPropertyName("content_preview")] string? ContentPreview = null
);

public record ElectronicSignatureBlockDto(
    [property: JsonPropertyName("signer_id")] string SignerId,
    [property: JsonPropertyName("signer_role")] string SignerRole,
    [property: JsonPropertyName("signed_at")] string SignedAt,
    [property: JsonPropertyName("signature_meaning")] string SignatureMeaning,
    [property: JsonPropertyName("hmac_signature")] string HmacSignature
);

public record ExecutionRunManifestDto(
    [property: JsonPropertyName("manifest_id")] string ManifestId,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("program_id")] string ProgramId,
    [property: JsonPropertyName("program_name")] string ProgramName,
    [property: JsonPropertyName("program_version")] string ProgramVersion,
    [property: JsonPropertyName("snapshot_id")] string SnapshotId,
    [property: JsonPropertyName("snapshot_version_tag")] string SnapshotVersionTag,
    [property: JsonPropertyName("snapshot_digest")] string SnapshotDigest,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("script_code_digest")] string ScriptCodeDigest,
    [property: JsonPropertyName("lockfile_digest")] string LockfileDigest,
    [property: JsonPropertyName("container_image_digest")] string ContainerImageDigest,
    [property: JsonPropertyName("execution_start")] string ExecutionStart,
    [property: JsonPropertyName("execution_end")] string ExecutionEnd,
    [property: JsonPropertyName("duration_ms")] double DurationMs,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stdout_capture")] string StdoutCapture,
    [property: JsonPropertyName("stderr_capture")] string StderrCapture,
    [property: JsonPropertyName("output_artifacts")] List<OutputArtifactSchemaDto> OutputArtifacts,
    [property: JsonPropertyName("output_artifacts_digest")] string OutputArtifactsDigest,
    [property: JsonPropertyName("is_unblinded_run")] bool IsUnblindedRun,
    [property: JsonPropertyName("database_lock_token_id")] string? DatabaseLockTokenId,
    [property: JsonPropertyName("executed_by")] string ExecutedBy,
    [property: JsonPropertyName("executed_by_role")] string ExecutedByRole,
    [property: JsonPropertyName("signature_block")] ElectronicSignatureBlockDto SignatureBlock,
    [property: JsonPropertyName("manifest_sha256_seal")] string ManifestSha256Seal
);

public record ExecuteProgramResponseDto(
    [property: JsonPropertyName("manifest")] ExecutionRunManifestDto Manifest
);

public record ExecutionRunListResponseDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("runs")] List<ExecutionRunManifestDto> Runs
);

public record ExecutionRunDetailResponseDto(
    [property: JsonPropertyName("run")] ExecutionRunManifestDto Run
);

public record VerifyManifestResponseDto(
    [property: JsonPropertyName("result")] Dictionary<string, object> Result
);
