using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Validation;

/// <summary>
/// Pre-commit warning reported when previewing form values against a study's pinned collection rule packs (K-RULE-3).
/// </summary>
/// <param name="PackKey">The rule pack key that raised the violation.</param>
/// <param name="RuleId">The violated rule identifier.</param>
/// <param name="RuleVersion">The violated rule version.</param>
/// <param name="Severity">The rule's validation severity.</param>
/// <param name="Dataset">The collection specification identifier evaluated.</param>
/// <param name="ItemId">The primary item identifier on the previewed form.</param>
/// <param name="Message">The rule explanation shown inline to the user.</param>
/// <param name="RemediationGuidance">Optional guidance explaining how to resolve the discrepancy.</param>
public sealed record CollectionPreviewWarning(
    string PackKey,
    string RuleId,
    int RuleVersion,
    ValidationSeverity Severity,
    string Dataset,
    string ItemId,
    string Message,
    string? RemediationGuidance = null);

/// <summary>
/// In-memory violation reported when previewing a draft or approved rule pack against a study's collection records.
/// </summary>
/// <param name="FormId">The evaluated collection instance identifier.</param>
/// <param name="PackKey">The rule pack key.</param>
/// <param name="RuleId">The violated rule identifier.</param>
/// <param name="RuleVersion">The violated rule version.</param>
/// <param name="Severity">The rule's validation severity.</param>
/// <param name="Dataset">The collection specification identifier evaluated.</param>
/// <param name="ItemId">The primary item identifier on the form, when resolved.</param>
/// <param name="Message">The rule explanation.</param>
/// <param name="RemediationGuidance">Optional remediation guidance.</param>
public sealed record CollectionRulePackPreviewViolation(
    CollectionInstanceId FormId,
    string PackKey,
    string RuleId,
    int RuleVersion,
    ValidationSeverity Severity,
    string Dataset,
    string? ItemId,
    string Message,
    string? RemediationGuidance = null);

/// <summary>
/// Summary and detected violations from previewing a rule pack in memory over a study's collection records.
/// </summary>
/// <param name="FormsEvaluated">Number of collection instances evaluated in memory.</param>
/// <param name="RulesEvaluated">Number of rules in the previewed rule pack.</param>
/// <param name="Violations">Violations detected across the study's collection instances.</param>
public sealed record CollectionRulePackPreviewResult(
    int FormsEvaluated,
    int RulesEvaluated,
    IReadOnlyList<CollectionRulePackPreviewViolation> Violations);

/// <summary>
/// Result of evaluating a study's collection records against its pinned rule packs and recording the validation run.
/// </summary>
/// <param name="RunOutcome">Recorded run outcome, or null when the study pins no released rule packs or covers no datasets.</param>
/// <param name="Findings">Current derived collection-stage findings across the study after the run.</param>
public sealed record CollectionValidationResult(
    ValidationRunOutcome? RunOutcome,
    IReadOnlyList<ValidationFindingView> Findings);

/// <summary>
/// Evaluates collection-stage edit checks over eCRF form instances, supporting both pure in-memory pre-commit form
/// preview and post-commit study validation runs (spec §10.2, K-RULE-3, K-FIND-1).
/// </summary>
public interface ICollectionValidationService
{
    /// <summary>
    /// Evaluates the study's pinned rule packs in memory over <paramref name="formId"/> overlaid with
    /// <paramref name="draftValues"/> (plus sibling forms on the enrollment) without mutating kernel state.
    /// </summary>
    Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
        TenantId tenant,
        CollectionInstanceId formId,
        IReadOnlyDictionary<string, ObservationValue?> draftValues,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a specific <see cref="LibraryItemKind.RulePack"/> library item (draft or approved) in memory against
    /// all current collection instances of <paramref name="studyId"/> without recording a validation run.
    /// </summary>
    Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
        TenantId tenant,
        StudyId studyId,
        LibraryItemId rulePackId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates the study's pinned rule packs at <see cref="ValidationStage.Collection"/> across the study's form
    /// instances and records a deterministic <see cref="RecordValidationRun"/> when rule packs are pinned.
    /// </summary>
    Task<CommandResult<CollectionValidationResult>> ValidateStudyCollectionsAsync(
        TenantId tenant,
        StudyId studyId,
        DateOnly asOf,
        DateTimeOffset evaluatedAt,
        string idempotencyKey,
        string reasonForChange,
        CommandMetadata? metadata = null,
        CancellationToken cancellationToken = default);
}
