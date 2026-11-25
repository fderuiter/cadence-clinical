using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Validation;

/// <summary>
/// Records one validation run over a study (spec §10.3): which released rule packs were evaluated at which stage, on
/// what inputs, over which datasets, and every problem they detected. Findings are identified by rule and target
/// across runs (K-FIND-1): a problem detected again names the same finding, and a finding in the run's scope (its
/// packs and datasets) that the run no longer detects is resolved by it. Runs are immutable once recorded.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="RunId">The new run's identifier.</param>
/// <param name="StudyId">The study whose data or design was validated.</param>
/// <param name="Stage">The lifecycle stage evaluated (spec §10.2).</param>
/// <param name="RulePacks">The released rule pack versions evaluated, at most one version per pack key.</param>
/// <param name="AsOf">The date the rules' effective periods were evaluated at.</param>
/// <param name="Standard">The standard evaluated against (for example <c>SDTMIG</c>), when the run had one.</param>
/// <param name="StandardVersion">The standard version, when the run had one.</param>
/// <param name="Agency">The agency the data is prepared for, when the run had one.</param>
/// <param name="Inputs">The versioned specifications the run read (standard version, mapping specification, protocol version), each a versioned NodeRef.</param>
/// <param name="Datasets">The datasets evaluated. Findings on other datasets are outside the run and keep their status.</param>
/// <param name="StartedAt">When evaluation started.</param>
/// <param name="CompletedAt">When evaluation completed; not after the command is recorded.</param>
/// <param name="Detections">Every problem detected, at most one per finding.</param>
/// <param name="IdempotencyKey">The command's idempotency key.</param>
/// <param name="ReasonForChange">Why the run is recorded.</param>
[KernelCapability("validation_run.record", VerbClass.Execute, Resource = nameof(StudyId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(ValidationErrors),
    nameof(ValidationErrors.RunInvalid),
    nameof(ValidationErrors.RulePackNotUsable),
    nameof(ValidationErrors.StudyUnknown))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RecordValidationRun(
    TenantId TenantId,
    ValidationRunId RunId,
    StudyId StudyId,
    ValidationStage Stage,
    IReadOnlyList<LibraryItemId> RulePacks,
    DateOnly AsOf,
    string? Standard,
    string? StandardVersion,
    string? Agency,
    IReadOnlyList<NodeRef> Inputs,
    IReadOnlyList<string> Datasets,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ValidationDetection> Detections,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ValidationRunOutcome>
{
    /// <summary>The most detections one run records. A larger evaluation is recorded as several runs over fewer datasets.</summary>
    public const int MaxDetections = 100_000;

    /// <summary>The most datasets one run covers.</summary>
    public const int MaxDatasets = 500;

    /// <summary>The most inputs one run names.</summary>
    public const int MaxInputs = 100;
}

/// <summary>What recording a run changed (K-FIND-1).</summary>
/// <param name="RunId">The recorded run.</param>
/// <param name="Detected">Findings the run detected.</param>
/// <param name="New">Of those, findings no earlier run of the study detected.</param>
/// <param name="Resolved">Findings in the run's scope that were detected before and that this run no longer detects.</param>
public sealed record ValidationRunOutcome(ValidationRunId RunId, int Detected, int New, int Resolved);

/// <summary>Marks an open finding as being investigated.</summary>
[KernelCapability("validation_finding.investigate", VerbClass.Execute, Resource = nameof(FindingId))]
[RaisesKernelErrors(typeof(ValidationErrors), nameof(ValidationErrors.FindingUnknown), nameof(ValidationErrors.InvalidTransition))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record InvestigateFinding(
    TenantId TenantId,
    ValidationFindingId FindingId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ValidationFindingId>;

/// <summary>Records what kind of problem a finding is. Allowed while the finding is detected, whatever its status.</summary>
[KernelCapability("validation_finding.classify", VerbClass.Execute, Resource = nameof(FindingId))]
[RaisesKernelErrors(typeof(ValidationErrors), nameof(ValidationErrors.FindingUnknown), nameof(ValidationErrors.InvalidTransition))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record ClassifyFinding(
    TenantId TenantId,
    ValidationFindingId FindingId,
    FindingClassification Classification,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ValidationFindingId>;

/// <summary>
/// Accepts an open or investigating finding as it is (K-FIND-2). The acting person signs a
/// <c>Responsibility</c> Attestation over <see cref="FindingDecisionContent.Hash"/> of the decision, computed with the
/// run that last detected the finding and <see cref="Rationale"/>.
/// </summary>
[KernelCapability("validation_finding.accept", VerbClass.Approve, Resource = nameof(FindingId))]
[RaisesKernelErrors(
    typeof(ValidationErrors),
    nameof(ValidationErrors.FindingUnknown),
    nameof(ValidationErrors.InvalidTransition),
    nameof(ValidationErrors.DecisionTextInvalid),
    nameof(ValidationErrors.AttestationRejected))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record AcceptFinding(
    TenantId TenantId,
    ValidationFindingId FindingId,
    string Rationale,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ValidationFindingId>;

/// <summary>
/// Explains an open or investigating finding (K-FIND-2); the explanation goes into the reviewer guide. The acting
/// person signs an <c>Approved</c> Attestation over <see cref="FindingDecisionContent.Hash"/> of the decision, computed
/// with the run that last detected the finding and <see cref="Explanation"/>.
/// </summary>
[KernelCapability("validation_finding.explain", VerbClass.Approve, Resource = nameof(FindingId))]
[RaisesKernelErrors(
    typeof(ValidationErrors),
    nameof(ValidationErrors.FindingUnknown),
    nameof(ValidationErrors.InvalidTransition),
    nameof(ValidationErrors.DecisionTextInvalid),
    nameof(ValidationErrors.AttestationRejected))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record ExplainFinding(
    TenantId TenantId,
    ValidationFindingId FindingId,
    string Explanation,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ValidationFindingId>;

/// <summary>Withdraws an investigation, acceptance or explanation, so a detected finding is open again.</summary>
[KernelCapability("validation_finding.reopen", VerbClass.Execute, Resource = nameof(FindingId))]
[RaisesKernelErrors(typeof(ValidationErrors), nameof(ValidationErrors.FindingUnknown), nameof(ValidationErrors.InvalidTransition))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record ReopenFinding(
    TenantId TenantId,
    ValidationFindingId FindingId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ValidationFindingId>;

/// <summary>
/// Deterministically rebuilds a study's materialized <c>validation_finding_projections</c> rows from its immutable
/// validation runs, detections, and finding decisions (ADR-2462 §5).
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="StudyId">The study whose validation finding projection rows are rebuilt.</param>
/// <param name="IdempotencyKey">The command's idempotency key.</param>
/// <param name="ReasonForChange">Why the projection rebuild is requested.</param>
[KernelCapability("validation_finding.rebuild_projection", VerbClass.Execute, Resource = nameof(StudyId), InCatalogue = false)]
[RaisesKernelErrors(typeof(ValidationErrors), nameof(ValidationErrors.StudyUnknown))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RebuildValidationFindingProjections(
    TenantId TenantId,
    StudyId StudyId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<int>;

/// <summary>Errors raised by validation runs and findings (K-ERR-1).</summary>
public static class ValidationErrors
{
    /// <summary>The run is not well formed.</summary>
    public static readonly KernelError RunInvalid =
        new("K.Validation.RunInvalid", 422, "The validation run is not valid.");

    /// <summary>A rule pack of the run is unknown, not a rule pack, or not released.</summary>
    public static readonly KernelError RulePackNotUsable =
        new("K.Validation.RulePackNotUsable", 422, "The rule pack cannot be used for validation.");

    /// <summary>The study does not exist in the tenant.</summary>
    public static readonly KernelError StudyUnknown =
        new("K.Validation.StudyUnknown", 404, "The study does not exist.");

    /// <summary>No run has detected this finding.</summary>
    public static readonly KernelError FindingUnknown =
        new("K.Validation.FindingUnknown", 404, "No validation run has detected this finding.");

    /// <summary>The finding's status does not allow the change.</summary>
    public static readonly KernelError InvalidTransition =
        new("K.Validation.InvalidTransition", 409, "The finding's status does not allow this change.");

    /// <summary>The decision text is missing or too long.</summary>
    public static readonly KernelError DecisionTextInvalid =
        new("K.Validation.DecisionTextInvalid", 422, $"The text must be 1 to {FindingText.MaxLength} characters.");

    /// <summary>The attestation does not match the decision.</summary>
    public static readonly KernelError AttestationRejected =
        new("K.Validation.AttestationRejected", 403, "The attestation does not satisfy the decision.");
}
