using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Api;

/// <summary>Summary of one rule within a <see cref="LibraryItemKind.RulePack"/> library item.</summary>
/// <param name="Id">Rule identifier within the pack.</param>
/// <param name="Version">Rule version number.</param>
/// <param name="Stage">Validation lifecycle stage (<c>Collection</c>, <c>Design</c>, <c>Standardization</c>, etc.).</param>
/// <param name="Dataset">Target dataset or collection specification identifier, when scoped.</param>
/// <param name="Severity">Rule violation severity (<c>Error</c>, <c>Warning</c>, <c>Notice</c>).</param>
/// <param name="Explanation">Human-readable discrepancy message.</param>
/// <param name="RemediationGuidance">Optional guidance for resolving a violation.</param>
/// <param name="Condition">Canonical JSON representation of the rule violation condition.</param>
public sealed record KernelRuleSummaryResponse(
    string Id,
    int Version,
    string Stage,
    string? Dataset,
    string Severity,
    string Explanation,
    string? RemediationGuidance,
    string? Condition = null);

/// <summary>Summary of a library item (such as a <see cref="LibraryItemKind.RulePack"/>) available to or pinned by a study.</summary>
/// <param name="Ref">Library item <c>NodeRef</c> (<c>library_item:&lt;uuid&gt;</c>).</param>
/// <param name="Key">Stable library item key across versions.</param>
/// <param name="Version">Library item version number.</param>
/// <param name="Title">Display title of the library item.</param>
/// <param name="Description">Optional description from the JSON payload.</param>
/// <param name="Status">Governance status (<c>Draft</c>, <c>Approved</c>, <c>Deprecated</c>).</param>
/// <param name="Pinned">True when this exact version is pinned in the study's declared standards.</param>
/// <param name="ContentHash">Canonical SHA-256 content hash attested on approval.</param>
/// <param name="RuleCount">Total number of rules when the item is a <c>RulePack</c>.</param>
/// <param name="Rules">Summaries of rules when the item is a <c>RulePack</c>.</param>
/// <param name="Kind">Library item kind (<c>CollectionSpecification</c>, <c>MappingSpecification</c>, <c>RulePack</c>, <c>CodelistSubset</c>, <c>IndicatorSet</c>).</param>
/// <param name="StandardVersionRefs">Referenced <c>standard_version:&lt;uuid&gt;</c> NodeRefs.</param>
public sealed record KernelRulePackResponse(
    string Ref,
    string Key,
    int Version,
    string Title,
    string? Description,
    string Status,
    bool Pinned,
    string ContentHash,
    int RuleCount,
    IReadOnlyList<KernelRuleSummaryResponse> Rules,
    string Kind = "RulePack",
    IReadOnlyList<string>? StandardVersionRefs = null)
{
    internal static KernelRulePackResponse From(LibraryItemView item, bool pinned)
    {
        var (description, rules) = ReadRulesSummary(item.Content);
        return new(
            NodeRef.For(item.Id).ToString(),
            item.Key,
            item.Version,
            item.Title,
            description,
            item.Status.ToString(),
            pinned,
            item.ContentHash,
            rules.Count,
            rules,
            item.Kind.ToString(),
            [.. item.StandardVersions.Select(id => NodeRef.For(id).ToString())]);
    }

    private static (string? Description, IReadOnlyList<KernelRuleSummaryResponse> Rules) ReadRulesSummary(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            return (null, []);
        }

        var description = content.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String
            ? descProp.GetString()
            : null;
        if (!content.TryGetProperty("rules", out var rulesProp) || rulesProp.ValueKind != JsonValueKind.Array)
        {
            return (description, []);
        }

        var rules = new List<KernelRuleSummaryResponse>();
        foreach (var entry in rulesProp.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = entry.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? string.Empty : string.Empty;
            var version = entry.TryGetProperty("version", out var verProp) && verProp.TryGetInt32(out var verInt) ? verInt : 1;
            var severity = entry.TryGetProperty("severity", out var sevProp) && sevProp.ValueKind == JsonValueKind.String ? sevProp.GetString() ?? "Warning" : "Warning";
            var explanation = entry.TryGetProperty("explanation", out var expProp) && expProp.ValueKind == JsonValueKind.String ? expProp.GetString() ?? string.Empty : string.Empty;
            string? guidance = null;
            if (entry.TryGetProperty("remediation", out var remProp) && remProp.ValueKind == JsonValueKind.String)
            {
                guidance = remProp.GetString();
            }
            else if (entry.TryGetProperty("remediationGuidance", out var guidProp) && guidProp.ValueKind == JsonValueKind.String)
            {
                guidance = guidProp.GetString();
            }

            var condition = entry.TryGetProperty("condition", out var condProp) ? condProp.GetRawText() : null;

            string stage = "Collection";
            string? dataset = null;
            if (entry.TryGetProperty("applicability", out var appProp) && appProp.ValueKind == JsonValueKind.Object)
            {
                if (appProp.TryGetProperty("stage", out var stageProp) && stageProp.ValueKind == JsonValueKind.String)
                {
                    stage = stageProp.GetString() ?? "Collection";
                }

                if (appProp.TryGetProperty("dataset", out var dsProp) && dsProp.ValueKind == JsonValueKind.String)
                {
                    dataset = dsProp.GetString();
                }
            }

            rules.Add(new KernelRuleSummaryResponse(id, version, stage, dataset, severity, explanation, guidance, condition));
        }

        return (description, rules);
    }
}

/// <summary>Summary of any Metadata Registry library item across all <see cref="LibraryItemKind"/> kinds.</summary>
public sealed record KernelLibraryItemResponse(
    string Ref,
    string Kind,
    string Key,
    int Version,
    string Title,
    string Status,
    bool Pinned,
    string ContentHash,
    IReadOnlyList<string> StandardVersionRefs,
    string? ApprovalAttestationRef,
    string? DeprecationAttestationRef,
    JsonElement Content)
{
    internal static KernelLibraryItemResponse From(LibraryItemView item, bool pinned) => new(
        NodeRef.For(item.Id).ToString(),
        item.Kind.ToString(),
        item.Key,
        item.Version,
        item.Title,
        item.Status.ToString(),
        pinned,
        item.ContentHash,
        [.. item.StandardVersions.Select(id => NodeRef.For(id).ToString())],
        item.ApprovalAttestation is { } appId ? NodeRef.For(appId).ToString() : null,
        item.DeprecationAttestation is { } depId ? NodeRef.For(depId).ToString() : null,
        item.Content);
}

/// <summary>Request body for <c>POST /api/v1/kernel/library-items</c>.</summary>
/// <param name="Kind">Library item kind (<c>CollectionSpecification</c>, <c>MappingSpecification</c>, <c>RulePack</c>, <c>CodelistSubset</c>, <c>IndicatorSet</c>).</param>
/// <param name="Key">Stable item key within its kind.</param>
/// <param name="Title">Display title.</param>
/// <param name="Content">Kind-specific JSON object payload.</param>
/// <param name="StandardVersionRefs">Optional referenced <c>standard_version:&lt;uuid&gt;</c> NodeRefs.</param>
/// <param name="SeedStandardFile">Optional bundled standard seed file name (e.g. <c>sdtmig-3-4.json</c>) to resolve/register when <paramref name="StandardVersionRefs"/> is omitted.</param>
/// <param name="LibraryItemId">Optional caller-supplied library item identifier.</param>
public sealed record KernelDraftLibraryItemRequest(
    string Kind,
    string Key,
    string Title,
    JsonElement Content,
    IReadOnlyList<string>? StandardVersionRefs = null,
    string? SeedStandardFile = null,
    Guid? LibraryItemId = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/library-items/{itemRef}/approve</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Approved</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelApproveLibraryItemRequest(string? Meaning = null, Guid? AttestationId = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/library-items/{itemRef}/deprecate</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Responsibility</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelDeprecateLibraryItemRequest(string? Meaning = null, Guid? AttestationId = null);

/// <summary>Variable summary within a registered or bundled standard dataset.</summary>
public sealed record KernelStandardVariableSummaryResponse(
    string Name,
    string Label,
    string DataType,
    string? Role,
    string Core);

/// <summary>Dataset summary within a registered or bundled standard version.</summary>
public sealed record KernelStandardDatasetSummaryResponse(
    string Name,
    string Label,
    string? Class,
    IReadOnlyList<KernelStandardVariableSummaryResponse> Variables)
{
    internal static KernelStandardDatasetSummaryResponse From(DatasetDefinitionData dataset) => new(
        dataset.Name,
        dataset.Label,
        dataset.Class,
        [.. dataset.Variables.Select(v => new KernelStandardVariableSummaryResponse(
            v.Name,
            v.Label,
            v.DataType.ToString(),
            v.Role,
            v.Core.ToString()))]);
}

/// <summary>Controlled terminology version reference on wire.</summary>
public sealed record KernelCodeSystemVersionRefDto(string CodeSystem, string Version)
{
    internal CodeSystemVersionRef ToDomain() => new(CodeSystem ?? string.Empty, Version ?? string.Empty);

    internal static KernelCodeSystemVersionRefDto From(CodeSystemVersionRef reference) =>
        new(reference.CodeSystem, reference.Version);
}

/// <summary>Registered standard version summary.</summary>
public sealed record KernelStandardVersionResponse(
    string Ref,
    string StandardCode,
    string Version,
    string Name,
    string Publisher,
    string? Source,
    DateOnly? EffectiveDate,
    string ContentHash,
    string Status,
    string? ApprovalAttestationRef,
    string? DeprecationAttestationRef,
    IReadOnlyList<KernelCodeSystemVersionRefDto> ControlledTerminology,
    IReadOnlyList<KernelStandardDatasetSummaryResponse> Datasets)
{
    internal static KernelStandardVersionResponse From(StandardVersionSummary summary, StandardPackage? package) => new(
        NodeRef.For(summary.Id).ToString(),
        summary.StandardCode,
        summary.Version,
        summary.Name,
        summary.Publisher,
        summary.Source,
        summary.EffectiveDate,
        summary.ContentHash,
        summary.Status.ToString(),
        summary.ApprovalAttestation is { } appId ? NodeRef.For(appId).ToString() : null,
        summary.DeprecationAttestation is { } depId ? NodeRef.For(depId).ToString() : null,
        [.. summary.ControlledTerminology.Select(KernelCodeSystemVersionRefDto.From)],
        package is null ? [] : [.. package.Datasets.Select(KernelStandardDatasetSummaryResponse.From)]);
}

/// <summary>Bundled public CDISC standard seed available to register in a tenant.</summary>
public sealed record KernelBundledStandardSeedResponse(
    string SeedFile,
    string StandardCode,
    string Version,
    string Name,
    string Publisher,
    IReadOnlyList<KernelStandardDatasetSummaryResponse> Datasets);

/// <summary>Catalog of tenant-registered standard versions and bundled platform standard seeds.</summary>
public sealed record KernelStandardsCatalogResponse(
    IReadOnlyList<KernelStandardVersionResponse> Standards,
    IReadOnlyList<KernelBundledStandardSeedResponse> BundledSeeds);

/// <summary>Request body for <c>POST /api/v1/kernel/standards</c>.</summary>
/// <param name="SeedFile">Optional bundled seed file name (e.g. <c>sdtmig-3-4.json</c>, <c>adamig-1-3.json</c>).</param>
/// <param name="Package">Explicit <see cref="StandardPackage"/> when not loading a bundled seed.</param>
/// <param name="ExpectedContentHash">Optional expected content hash verified on registration.</param>
/// <param name="StandardVersionId">Optional caller-supplied standard version identifier.</param>
public sealed record KernelRegisterStandardVersionRequest(
    string? SeedFile = null,
    StandardPackage? Package = null,
    string? ExpectedContentHash = null,
    Guid? StandardVersionId = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/standards/{standardRef}/approve</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Approved</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelApproveStandardVersionRequest(string? Meaning = null, Guid? AttestationId = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/standards/{standardRef}/deprecate</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Responsibility</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelDeprecateStandardVersionRequest(string? Meaning = null, Guid? AttestationId = null);

/// <summary>Response for <c>GET /api/v1/kernel/studies/{studyRef}/standards</c>.</summary>
public sealed record KernelStudyStandardsProfileResponse(
    string StudyRef,
    string? ProfileRef,
    long Revision,
    IReadOnlyList<KernelStandardVersionResponse> StandardVersions,
    IReadOnlyList<KernelCodeSystemVersionRefDto> ControlledTerminology,
    IReadOnlyList<KernelLibraryItemResponse> LibraryPins);

/// <summary>Response for <c>GET /api/v1/kernel/studies/{studyRef}/rule-packs</c> and <c>PUT /api/v1/kernel/studies/{studyRef}/standards</c>.</summary>
/// <param name="StudyRef">The study <c>NodeRef</c>.</param>
/// <param name="ProfileRef">The study's <c>StudyStandardsProfile</c> <c>NodeRef</c>, or null if none declared yet.</param>
/// <param name="PinnedRulePackRefs">Currently pinned rule-pack <c>NodeRef</c>s on the study.</param>
/// <param name="RulePacks">Available rule-pack library items in the tenant.</param>
/// <param name="StandardVersionRefs">Declared standard version <c>NodeRef</c>s on the study.</param>
/// <param name="LibraryPinRefs">All pinned library item <c>NodeRef</c>s on the study across all kinds.</param>
public sealed record KernelStudyRulePacksResponse(
    string StudyRef,
    string? ProfileRef,
    IReadOnlyList<string> PinnedRulePackRefs,
    IReadOnlyList<KernelRulePackResponse> RulePacks,
    IReadOnlyList<string>? StandardVersionRefs = null,
    IReadOnlyList<string>? LibraryPinRefs = null);

/// <summary>Request body for <c>PUT /api/v1/kernel/studies/{studyRef}/standards</c>.</summary>
/// <param name="RulePackRefs">Optional approved rule-pack <c>NodeRef</c>s to pin on the study (replaces pinned rule packs while preserving other kinds when <paramref name="LibraryPinRefs"/> is null).</param>
/// <param name="MappingSpecRefs">Optional approved mapping-specification <c>NodeRef</c>s to pin on the study (replaces pinned mapping specs while preserving other kinds when <paramref name="LibraryPinRefs"/> is null).</param>
/// <param name="LibraryPinRefs">Optional complete list of approved library-item <c>NodeRef</c>s across all kinds to pin on the study.</param>
/// <param name="StandardVersionRefs">Optional approved standard-version <c>NodeRef</c>s to declare on the study.</param>
/// <param name="ControlledTerminology">Optional controlled-terminology version bindings to declare on the study.</param>
public sealed record KernelDeclareStudyRulePacksRequest(
    IReadOnlyList<string>? RulePackRefs = null,
    IReadOnlyList<string>? MappingSpecRefs = null,
    IReadOnlyList<string>? LibraryPinRefs = null,
    IReadOnlyList<string>? StandardVersionRefs = null,
    IReadOnlyList<KernelCodeSystemVersionRefDto>? ControlledTerminology = null);

/// <summary>One in-memory violation returned when previewing a rule pack against a study's collection instances.</summary>
/// <param name="FormId">The evaluated form instance identifier.</param>
/// <param name="PackKey">The rule pack key.</param>
/// <param name="RuleId">The violated rule identifier.</param>
/// <param name="RuleVersion">The violated rule version.</param>
/// <param name="Severity">Violation severity (<c>Error</c>, <c>Warning</c>, <c>Notice</c>).</param>
/// <param name="Dataset">Collection specification identifier.</param>
/// <param name="ItemId">Primary item identifier on the form, when resolved.</param>
/// <param name="Message">Discrepancy message.</param>
/// <param name="RemediationGuidance">Optional remediation guidance.</param>
public sealed record KernelRulePackPreviewViolationResponse(
    Guid FormId,
    string PackKey,
    string RuleId,
    int RuleVersion,
    string Severity,
    string Dataset,
    string? ItemId,
    string Message,
    string? RemediationGuidance)
{
    internal static KernelRulePackPreviewViolationResponse From(CollectionRulePackPreviewViolation violation) =>
        new(
            violation.FormId.Value,
            violation.PackKey,
            violation.RuleId,
            violation.RuleVersion,
            violation.Severity.ToString(),
            violation.Dataset,
            violation.ItemId,
            violation.Message,
            violation.RemediationGuidance);
}

/// <summary>Response for <c>POST /api/v1/kernel/studies/{studyRef}/rule-packs/{itemRef}/preview</c>.</summary>
/// <param name="ItemRef">Previewed library item <c>NodeRef</c>.</param>
/// <param name="FormsEvaluated">Number of collection instances evaluated in memory.</param>
/// <param name="RulesEvaluated">Number of rules in the previewed rule pack.</param>
/// <param name="ViolationCount">Number of violations found in memory across current study forms.</param>
/// <param name="Violations">Detected violations.</param>
public sealed record KernelRulePackPreviewResponse(
    string ItemRef,
    int FormsEvaluated,
    int RulesEvaluated,
    int ViolationCount,
    IReadOnlyList<KernelRulePackPreviewViolationResponse> Violations);

/// <summary>Observed variable value on a validation finding.</summary>
/// <param name="Variable">Variable or item identifier.</param>
/// <param name="Value">Formatted observed value, or null when missing.</param>
public sealed record KernelFindingObservedValueResponse(string Variable, string? Value);

/// <summary>Summary of a validation finding returned by <c>GET /api/v1/kernel/studies/{studyRef}/validation/findings</c>.</summary>
public sealed record KernelValidationFindingResponse(
    string Ref,
    string StudyRef,
    string Stage,
    string PackKey,
    string RulePackRef,
    string RuleId,
    int RuleVersion,
    string Severity,
    string Status,
    bool Blocks,
    string TargetKind,
    string Dataset,
    string RecordKey,
    string? Variable,
    string Message,
    string? Expected,
    IReadOnlyList<KernelFindingObservedValueResponse> Observed,
    string FirstDetectedRunRef,
    string LastDetectedRunRef,
    string? ResolvedByRunRef,
    string? Classification,
    string? DecisionText = null,
    string? DecidedBy = null,
    DateTimeOffset? DecidedAt = null,
    string? DecisionAttestationRef = null)
{
    internal static KernelValidationFindingResponse From(ValidationFindingView view) =>
        new(
            NodeRef.For(view.Id).ToString(),
            NodeRef.For(view.Study).ToString(),
            view.Stage.ToString(),
            view.PackKey,
            NodeRef.For(view.RulePack).ToString(),
            view.RuleId,
            view.RuleVersion,
            view.Severity.ToString(),
            view.Status.ToString(),
            view.Blocks,
            view.Target.Kind.ToString(),
            view.Target.Dataset,
            view.Target.Key,
            view.Target.Variable,
            view.Message,
            view.Expected,
            [.. view.Observed.Select(o => new KernelFindingObservedValueResponse(o.Variable, o.Value))],
            NodeRef.For(view.FirstDetectedRun).ToString(),
            NodeRef.For(view.LastDetectedRun).ToString(),
            view.ResolvedBy is { } r ? NodeRef.For(r).ToString() : null,
            view.Classification?.ToString(),
            view.Decision?.Text,
            view.Decision?.DecidedBy,
            view.Decision?.DecidedAt,
            view.Decision?.AttestationId is { } attId ? NodeRef.For(attId).ToString() : null);
}

/// <summary>Page of validation findings for a study.</summary>
/// <param name="Total">Total matching findings.</param>
/// <param name="Findings">Returned findings.</param>
public sealed record KernelValidationFindingPageResponse(
    int Total,
    IReadOnlyList<KernelValidationFindingResponse> Findings);

/// <summary>Detailed validation finding with its structured <see cref="Explanation"/>.</summary>
/// <param name="Finding">Finding state and metadata.</param>
/// <param name="Explanation">Structured provenance explanation.</param>
public sealed record KernelValidationFindingDetailResponse(
    KernelValidationFindingResponse Finding,
    Explanation Explanation);

/// <summary>Observed variable input when explicitly recording a validation run.</summary>
public sealed record KernelFindingObservedValueInput(string Variable, string? Value);

/// <summary>Detection input when explicitly recording a validation run via <c>POST /api/v1/kernel/studies/{studyRef}/validation/runs</c>.</summary>
public sealed record KernelValidationDetectionInput(
    string PackKey,
    string RuleId,
    int RuleVersion,
    string Severity,
    string TargetKind,
    string Dataset,
    string RecordKey,
    string? Variable,
    string Message,
    string? Expected = null,
    IReadOnlyList<KernelFindingObservedValueInput>? Observed = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/studies/{studyRef}/validation/runs</c>.</summary>
/// <remarks>
/// When <paramref name="RulePackRefs"/> and <paramref name="Detections"/> are omitted, evaluates the study's pinned
/// collection rule packs on demand over current collection instances via <see cref="ICollectionValidationService"/>.
/// When <paramref name="RulePackRefs"/> or <paramref name="Detections"/> are supplied, dispatches
/// <see cref="RecordValidationRun"/> directly.
/// </remarks>
public sealed record KernelRunStudyValidationRequest(
    string? Stage = null,
    IReadOnlyList<string>? RulePackRefs = null,
    DateOnly? AsOf = null,
    string? Standard = null,
    string? StandardVersion = null,
    string? Agency = null,
    IReadOnlyList<string>? InputRefs = null,
    IReadOnlyList<string>? Datasets = null,
    IReadOnlyList<KernelValidationDetectionInput>? Detections = null,
    Guid? RunId = null);

/// <summary>Response returned after running or recording a study validation run.</summary>
public sealed record KernelValidationRunResponse(
    string? RunRef,
    int Detected,
    int New,
    int Resolved,
    IReadOnlyList<KernelValidationFindingResponse> Findings);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/classify</c>.</summary>
/// <param name="Classification">Finding classification (<c>TrueIssue</c>, <c>DataException</c>, <c>RuleDefect</c>, <c>MappingGap</c>, <c>StandardLimitation</c>).</param>
public sealed record KernelClassifyFindingRequest(string Classification);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/accept</c>.</summary>
/// <param name="Rationale">Justification text (1..4000 characters) signed under <c>Responsibility</c>.</param>
/// <param name="Meaning">Attestation meaning (<c>Responsibility</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelAcceptFindingRequest(
    string Rationale,
    string? Meaning = null,
    Guid? AttestationId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/explain</c>.</summary>
/// <param name="Explanation">Reviewer-guide explanation text (1..4000 characters) signed under <c>Approved</c>.</param>
/// <param name="Meaning">Attestation meaning (<c>Approved</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelExplainFindingRequest(
    string Explanation,
    string? Meaning = null,
    Guid? AttestationId = null);
