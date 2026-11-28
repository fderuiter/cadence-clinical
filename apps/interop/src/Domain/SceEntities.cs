using System;
using System.Collections.Generic;

namespace Cadence.Apps.Interop.Domain;

public class StatisticalProgram
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Language { get; set; } = "PYTHON";
    public string Version { get; set; } = "1.0.0";
    public string SourceCode { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public string? LockfileContent { get; set; }
    public string LockType { get; set; } = "UV_LOCK";
    public string LockDigest { get; set; } = string.Empty;
    public string? TargetDomain { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string ReasonForChange { get; set; } = "Initial program authoring";
    public int VersionIndex { get; set; } = 1;
}

public class DatabaseLockAuthToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TokenId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string LockId { get; set; } = string.Empty;
    public string Milestone { get; set; } = "DATABASE_LOCK";
    public string IssuedTo { get; set; } = string.Empty;
    public string AuthorizedRolesJson { get; set; } = "[]";
    public required DateTimeOffset IssuedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public string SignatureHash { get; set; } = string.Empty;
    public bool IsRevoked { get; set; } = false;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
    public string? CertificateId { get; set; }
    public string? CertificateFingerprint { get; set; }
    public string? LockCertificateJson { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string ReasonForChange { get; set; } = "Unblinded database lock authorization token";
    public int VersionIndex { get; set; } = 1;
}

public class ReplicatedStudyLock
{
    public Cadence.Kernel.TenantId TenantId { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LockId { get; set; } = string.Empty;
    public string StudyRef { get; set; } = string.Empty;
    public string StudyCode { get; set; } = string.Empty;
    public string ScopeKind { get; set; } = "study";
    public string LockKind { get; set; } = "Locked";
    public string Milestone { get; set; } = "FinalDatabaseLock";
    public bool IsActive { get; set; } = true;
    public string? CertificateId { get; set; }
    public string? CertificateFingerprint { get; set; }
    public string? CertificateJson { get; set; }
    public string AppliedBy { get; set; } = string.Empty;
    public required DateTimeOffset AppliedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string ReasonForChange { get; set; } = "Kernel study lock replication";
    public int VersionIndex { get; set; } = 1;
}

public class ExecutionRunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RunId { get; set; } = string.Empty;
    public string? ManifestId { get; set; }
    public string StudyId { get; set; } = string.Empty;
    public string ProgramId { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramVersion { get; set; } = "1.0.0";
    public string SnapshotId { get; set; } = string.Empty;
    public string SnapshotVersionTag { get; set; } = "v1.0.0";
    public string SnapshotDigest { get; set; } = string.Empty;
    public string Language { get; set; } = "PYTHON";
    public string ScriptCodeDigest { get; set; } = string.Empty;
    public string LockfileDigest { get; set; } = string.Empty;
    public string ContainerImageDigest { get; set; } = "sha256:cadence-sce-r4.4-py3.14-validated-base-2026";
    public required DateTimeOffset ExecutionStart { get; set; }
    public required DateTimeOffset ExecutionEnd { get; set; }
    public double DurationMs { get; set; }
    public int ExitCode { get; set; } = 0;
    public string Status { get; set; } = "COMPLETED";
    public string StdoutCapture { get; set; } = string.Empty;
    public string StderrCapture { get; set; } = string.Empty;
    public string OutputArtifactsJson { get; set; } = "[]";
    public string OutputArtifactsDigest { get; set; } = string.Empty;
    public bool IsUnblindedRun { get; set; } = false;
    public string? DatabaseLockTokenId { get; set; }
    public string ExecutedBy { get; set; } = string.Empty;
    public string ExecutedByRole { get; set; } = "BIOSTATISTICIAN";
    public string SignatureBlockJson { get; set; } = "{}";
    public string ManifestSha256Seal { get; set; } = string.Empty;
    public required DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public string ReasonForChange { get; set; } = "Statistical analysis execution run";
    public int VersionIndex { get; set; } = 1;
}

public class BiostatExportAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required DateTimeOffset Timestamp { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string? ProgramId { get; set; }
    public string? SnapshotId { get; set; }
    public bool IsUnblinded { get; set; }
    public string? LockTokenId { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public string Sha256Hash { get; set; } = string.Empty;
    public required DateTimeOffset CreatedAt { get; set; }
}
