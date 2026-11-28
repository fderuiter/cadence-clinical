using System;
using System.Text.Json.Serialization;

namespace Cadence.Contracts;

/// <summary>
/// Wire contract for an asymmetrically signed study database lock certificate minted by the Clinical Kernel
/// and verified offline by Statistical Computing Environment (SCE) sandboxes.
/// </summary>
/// <param name="CertificateId">Stable identifier of the lock certificate.</param>
/// <param name="TenantCode">Canonical tenant code or UUID string owning the study.</param>
/// <param name="StudyRef">Canonical study UUID or NodeRef string in the Clinical Kernel.</param>
/// <param name="StudyCode">Canonical short study code (for standalone services keyed by study code).</param>
/// <param name="LockId">Kernel lock assertion UUID string covered by this certificate.</param>
/// <param name="Milestone">Closeout milestone profile (<c>InterimCut</c> or <c>FinalDatabaseLock</c>).</param>
/// <param name="AttestationId">Electronic signature attestation UUID string that authorized the lock.</param>
/// <param name="SignedBy">Verified actor identifier who applied the lock.</param>
/// <param name="IssuedAt">UTC timestamp when the lock assertion and certificate were recorded.</param>
/// <param name="KeyId">Identifier of the kernel verification public key.</param>
/// <param name="Algorithm">Asymmetric signature algorithm (<c>ECDSA-P256-SHA256</c> or <c>RSA-PSS-SHA256</c>).</param>
/// <param name="PayloadSha256">Lowercase hex SHA-256 digest of the canonical signed payload bytes.</param>
/// <param name="FingerprintSha256">Lowercase hex SHA-256 fingerprint over payload and signature.</param>
/// <param name="SignatureBase64">Base64-encoded asymmetric signature produced by the kernel private key.</param>
public sealed record DatabaseLockCertificateContract(
    [property: JsonPropertyName("certificate_id")] string CertificateId,
    [property: JsonPropertyName("tenant_code")] string TenantCode,
    [property: JsonPropertyName("study_ref")] string StudyRef,
    [property: JsonPropertyName("study_code")] string StudyCode,
    [property: JsonPropertyName("lock_id")] string LockId,
    [property: JsonPropertyName("milestone")] string Milestone,
    [property: JsonPropertyName("attestation_id")] string AttestationId,
    [property: JsonPropertyName("signed_by")] string SignedBy,
    [property: JsonPropertyName("issued_at")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("key_id")] string KeyId,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("payload_sha256")] string PayloadSha256,
    [property: JsonPropertyName("fingerprint_sha256")] string FingerprintSha256,
    [property: JsonPropertyName("signature_base64")] string SignatureBase64);

/// <summary>
/// Published atomically via outbox when a <c>Frozen</c> or <c>Locked</c> scope lock is applied in the Clinical Kernel,
/// allowing standalone microservices (<c>apps/econsent</c>, <c>apps/interop</c>) to replicate lock state locally.
/// </summary>
public sealed record KernelScopeLockApplied
{
    /// <summary>Gets the canonical tenant code or UUID string.</summary>
    public required string TenantCode { get; init; }

    /// <summary>Gets the kernel lock assertion UUID string.</summary>
    public required string LockId { get; init; }

    /// <summary>Gets the kernel study UUID or NodeRef string.</summary>
    public required string StudyRef { get; init; }

    /// <summary>Gets the human-readable study code.</summary>
    public required string StudyCode { get; init; }

    /// <summary>Gets the optional study-site UUID or code when the lock scopes a site or below.</summary>
    public string? SiteCode { get; init; }

    /// <summary>Gets the optional pseudonymous subject code when the lock scopes an enrollment or below.</summary>
    public string? SubjectCode { get; init; }

    /// <summary>Gets the locked target NodeRef kind (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c>, or <c>observation</c>).</summary>
    public required string ScopeKind { get; init; }

    /// <summary>Gets the unversioned locked target NodeRef string.</summary>
    public required string ScopeRef { get; init; }

    /// <summary>Gets the lock kind (<c>Frozen</c> or <c>Locked</c>).</summary>
    public required string LockKind { get; init; }

    /// <summary>Gets the milestone profile (<c>InterimCut</c> or <c>FinalDatabaseLock</c>).</summary>
    public required string Milestone { get; init; }

    /// <summary>Gets the actor identifier who applied the lock.</summary>
    public required string AppliedBy { get; init; }

    /// <summary>Gets the UTC timestamp when the lock was applied.</summary>
    public required DateTimeOffset AppliedAt { get; init; }

    /// <summary>Gets the asymmetric database lock certificate when a study-wide <c>Locked</c> assertion was applied; otherwise null.</summary>
    public DatabaseLockCertificateContract? Certificate { get; init; }
}

/// <summary>
/// Published atomically via outbox when a scope lock is released in the Clinical Kernel, revoking replicated locks
/// and any associated <see cref="DatabaseLockCertificateContract"/> in downstream services.
/// </summary>
public sealed record KernelScopeLockReleased
{
    /// <summary>Gets the canonical tenant code or UUID string.</summary>
    public required string TenantCode { get; init; }

    /// <summary>Gets the release assertion UUID string.</summary>
    public required string ReleaseAssertionId { get; init; }

    /// <summary>Gets the released lock assertion UUID string.</summary>
    public required string LockId { get; init; }

    /// <summary>Gets the kernel study UUID or NodeRef string.</summary>
    public required string StudyRef { get; init; }

    /// <summary>Gets the human-readable study code.</summary>
    public required string StudyCode { get; init; }

    /// <summary>Gets the optional study-site code when the released lock scoped a site or below.</summary>
    public string? SiteCode { get; init; }

    /// <summary>Gets the optional pseudonymous subject code when the released lock scoped an enrollment or below.</summary>
    public string? SubjectCode { get; init; }

    /// <summary>Gets the released target NodeRef kind.</summary>
    public required string ScopeKind { get; init; }

    /// <summary>Gets the unversioned released target NodeRef string.</summary>
    public required string ScopeRef { get; init; }

    /// <summary>Gets the released lock kind (<c>Frozen</c> or <c>Locked</c>).</summary>
    public required string LockKind { get; init; }

    /// <summary>Gets the actor identifier who released the lock.</summary>
    public required string ReleasedBy { get; init; }

    /// <summary>Gets the UTC timestamp when the lock was released.</summary>
    public required DateTimeOffset ReleasedAt { get; init; }

    /// <summary>Gets the reason recorded for releasing the lock.</summary>
    public required string ReleaseReason { get; init; }

    /// <summary>Gets the identifier of the revoked <see cref="DatabaseLockCertificateContract"/> when releasing a study hard lock; otherwise null.</summary>
    public string? RevokedCertificateId { get; init; }
}
