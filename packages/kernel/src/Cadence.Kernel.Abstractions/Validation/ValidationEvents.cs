using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Validation;

/// <summary>
/// A validation run was recorded (spec §5.2, Quality family), with what it changed (K-FIND-1): how many findings it
/// detected, how many of those are new, and how many it resolved. The findings themselves are read from
/// <see cref="IValidationFindings"/>.
/// </summary>
[KernelEvent("ValidationRunCompleted", 1)]
public sealed record ValidationRunCompleted(
    TenantId TenantId,
    ValidationRunId RunId,
    StudyId StudyId,
    ValidationStage Stage,
    int RulePackCount,
    int DatasetCount,
    int Detected,
    int New,
    int Resolved);

/// <summary>Someone started investigating a finding.</summary>
[KernelEvent("FindingInvestigationStarted", 1)]
public sealed record FindingInvestigationStarted(TenantId TenantId, ValidationFindingId FindingId);

/// <summary>A finding was classified.</summary>
[KernelEvent("FindingClassified", 1)]
public sealed record FindingClassified(TenantId TenantId, ValidationFindingId FindingId, FindingClassification Classification);

/// <summary>A finding was accepted as it is, with an attestation (K-FIND-2).</summary>
[KernelEvent("FindingAccepted", 1)]
public sealed record FindingAccepted(TenantId TenantId, ValidationFindingId FindingId, ValidationRunId DetectedBy, AttestationId AttestationId);

/// <summary>A finding was explained, with an attestation (K-FIND-2).</summary>
[KernelEvent("FindingExplained", 1)]
public sealed record FindingExplained(TenantId TenantId, ValidationFindingId FindingId, ValidationRunId DetectedBy, AttestationId AttestationId);

/// <summary>An investigation, acceptance or explanation was withdrawn; the finding is open again.</summary>
[KernelEvent("FindingReopened", 1)]
public sealed record FindingReopened(TenantId TenantId, ValidationFindingId FindingId, FindingStatus PreviousStatus);
