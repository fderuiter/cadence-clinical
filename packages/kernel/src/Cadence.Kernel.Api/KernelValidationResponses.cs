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
/// <param name="Authority">Rule authority (<c>Study</c>, <c>Sponsor</c>, <c>CDISC</c>, <c>FDA</c>, etc.).</param>
/// <param name="CitationDocument">Public document cited by the rule (K-RULE-2).</param>
/// <param name="CitationSection">Section or element cited within the public document.</param>
/// <param name="CitationUri">Optional HTTP(S) URI for the cited public document.</param>
public sealed record KernelRuleSummaryResponse(
    string Id,
    int Version,
    string Stage,
    string? Dataset,
    string Severity,
    string Explanation,
    string? RemediationGuidance,
    string? Condition = null,
    string? Authority = null,
    string? CitationDocument = null,
    string? CitationSection = null,
    string? CitationUri = null);

/// <summary>Summary of a <see cref="LibraryItemKind.RulePack"/> library item available to or pinned by a study.</summary>
/// <param name="Ref">Library item <c>NodeRef</c> (<c>library_item:&lt;uuid&gt;</c>).</param>
/// <param name="Key">Stable rule-pack key across versions.</param>
/// <param name="Version">Rule-pack version number.</param>
/// <param name="Title">Display title of the rule pack.</param>
/// <param name="Description">Optional description from the rule-pack JSON payload.</param>
/// <param name="Status">Governance status (<c>Draft</c>, <c>Approved</c>, <c>Deprecated</c>).</param>
/// <param name="Pinned">True when this exact rule-pack version is pinned in the study's declared standards.</param>
/// <param name="ContentHash">Canonical SHA-256 content hash attested on approval.</param>
/// <param name="RuleCount">Total number of rules in the pack.</param>
/// <param name="Rules">Summaries of the rules defined in the pack.</param>
/// <param name="Authority">Rule-pack authority (<c>Study</c>, <c>Sponsor</c>, <c>CDISC</c>, etc.).</param>
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
    string Authority = "Study")
{
    internal static KernelRulePackResponse From(LibraryItemView item, bool pinned)
    {
        var (authority, description, rules) = ReadRulesSummary(item.Content);
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
            authority);
    }

    private static (string Authority, string? Description, IReadOnlyList<KernelRuleSummaryResponse> Rules) ReadRulesSummary(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            return ("Study", null, []);
        }

        var packAuthority = content.TryGetProperty("authority", out var authProp) && authProp.ValueKind == JsonValueKind.String
            ? authProp.GetString() ?? "Study"
            : "Study";
        var description = content.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String
            ? descProp.GetString()
            : null;
        if (!content.TryGetProperty("rules", out var rulesProp) || rulesProp.ValueKind != JsonValueKind.Array)
        {
            return (packAuthority, description, []);
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
            var ruleAuthority = entry.TryGetProperty("authority", out var rAuthProp) && rAuthProp.ValueKind == JsonValueKind.String
                ? rAuthProp.GetString() ?? packAuthority
                : packAuthority;
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

            string? citationDoc = null;
            string? citationSec = null;
            string? citationUri = null;
            if (entry.TryGetProperty("citation", out var citProp) && citProp.ValueKind == JsonValueKind.Object)
            {
                citationDoc = citProp.TryGetProperty("document", out var docProp) && docProp.ValueKind == JsonValueKind.String ? docProp.GetString() : null;
                citationSec = citProp.TryGetProperty("section", out var secProp) && secProp.ValueKind == JsonValueKind.String ? secProp.GetString() : null;
                citationUri = citProp.TryGetProperty("uri", out var uriProp) && uriProp.ValueKind == JsonValueKind.String ? uriProp.GetString() : null;
            }

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

            rules.Add(new KernelRuleSummaryResponse(
                id,
                version,
                stage,
                dataset,
                severity,
                explanation,
                guidance,
                condition,
                ruleAuthority,
                citationDoc,
                citationSec,
                citationUri));
        }

        return (packAuthority, description, rules);
    }
}

/// <summary>Response for <c>GET /api/v1/kernel/studies/{studyRef}/rule-packs</c>.</summary>
/// <param name="StudyRef">The study <c>NodeRef</c>.</param>
/// <param name="ProfileRef">The study's <c>StudyStandardsProfile</c> <c>NodeRef</c>, or null if none declared yet.</param>
/// <param name="PinnedRulePackRefs">Currently pinned rule-pack <c>NodeRef</c>s on the study.</param>
/// <param name="RulePacks">Available rule-pack library items in the tenant.</param>
public sealed record KernelStudyRulePacksResponse(
    string StudyRef,
    string? ProfileRef,
    IReadOnlyList<string> PinnedRulePackRefs,
    IReadOnlyList<KernelRulePackResponse> RulePacks);

/// <summary>Request body for <c>POST /api/v1/kernel/studies/{studyRef}/rule-packs</c>.</summary>
/// <param name="Key">Stable rule-pack key across versions (<c>DraftLibraryItem.Key</c>).</param>
/// <param name="Title">Display title of the rule-pack version.</param>
/// <param name="Content">RulePack JSON object validated against <c>RulePackJson</c>.</param>
/// <param name="LibraryItemId">Optional caller-supplied library item UUID.</param>
/// <param name="StandardVersionRefs">Optional <c>standard_version:&lt;uuid&gt;</c> references linked to the rule pack.</param>
public sealed record KernelDraftRulePackRequest(
    string Key,
    string Title,
    JsonElement Content,
    Guid? LibraryItemId = null,
    IReadOnlyList<string>? StandardVersionRefs = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/library-items/{itemRef}/approve</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Approved</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelApproveLibraryItemRequest(string? Meaning = null, Guid? AttestationId = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/library-items/{itemRef}/deprecate</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Responsibility</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelDeprecateLibraryItemRequest(string? Meaning = null, Guid? AttestationId = null);

/// <summary>Request body for <c>PUT /api/v1/kernel/studies/{studyRef}/standards</c>.</summary>
/// <param name="RulePackRefs">Approved rule-pack <c>NodeRef</c>s to pin on the study (replaces pinned rule packs while preserving other declared standards/pins).</param>
public sealed record KernelDeclareStudyRulePacksRequest(IReadOnlyList<string> RulePackRefs);

/// <summary>Optional request body for <c>POST /api/v1/kernel/studies/{studyRef}/validation/runs</c>.</summary>
/// <param name="AsOf">Optional effective date for rule evaluation; defaults to today in UTC.</param>
public sealed record KernelRunStudyValidationRequest(DateOnly? AsOf = null);

/// <summary>Response for <c>POST /api/v1/kernel/studies/{studyRef}/validation/runs</c>.</summary>
/// <param name="StudyRef">Evaluated study <c>NodeRef</c>.</param>
/// <param name="RunRef">Recorded <c>validation_run:&lt;uuid&gt;</c> <c>NodeRef</c>, or null when no pinned rule packs or datasets applied.</param>
/// <param name="Detected">Total findings detected in the run.</param>
/// <param name="New">Findings detected for the first time on the study.</param>
/// <param name="Resolved">Previously open findings resolved by this run.</param>
/// <param name="Findings">Current collection-stage findings across the study after the run.</param>
public sealed record KernelStudyValidationRunResponse(
    string StudyRef,
    string? RunRef,
    int Detected,
    int New,
    int Resolved,
    IReadOnlyList<KernelValidationFindingResponse> Findings);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/classify</c>.</summary>
/// <param name="Classification">Finding classification (<c>DataIssue</c>, <c>MappingIssue</c>, <c>MetadataIssue</c>, <c>RuleIssue</c>, <c>NotAnIssue</c>).</param>
public sealed record KernelClassifyFindingRequest(string Classification);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/accept</c>.</summary>
/// <param name="Rationale">Acceptance rationale attested with <c>Responsibility</c> meaning.</param>
/// <param name="LastDetectedRunRef">Optional <c>validation_run:&lt;uuid&gt;</c> <c>NodeRef</c> the signer bound into <c>FindingDecisionContent.Hash</c>.</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelAcceptFindingRequest(
    string Rationale,
    string? LastDetectedRunRef = null,
    Guid? AttestationId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/explain</c>.</summary>
/// <param name="Explanation">Reviewer-guide explanation attested with <c>Approved</c> meaning.</param>
/// <param name="LastDetectedRunRef">Optional <c>validation_run:&lt;uuid&gt;</c> <c>NodeRef</c> the signer bound into <c>FindingDecisionContent.Hash</c>.</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelExplainFindingRequest(
    string Explanation,
    string? LastDetectedRunRef = null,
    Guid? AttestationId = null);

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

/// <summary>Recorded decision metadata on a validation finding.</summary>
/// <param name="Status">Decision status (<c>Investigating</c>, <c>Accepted</c>, <c>Explained</c>, <c>Open</c>).</param>
/// <param name="DecidedBy">Actor identifier of the reviewer.</param>
/// <param name="DecidedAt">UTC timestamp when the decision was recorded.</param>
/// <param name="Text">Rationale or explanation text for Accepted or Explained decisions.</param>
/// <param name="DecidedOnRunRef">Validation run <c>NodeRef</c> the decision was attested against, when concluded.</param>
/// <param name="AttestationRef">Attestation <c>NodeRef</c> backing an Accepted or Explained decision, when concluded.</param>
public sealed record KernelFindingDecisionResponse(
    string Status,
    string DecidedBy,
    DateTimeOffset DecidedAt,
    string? Text,
    string? DecidedOnRunRef,
    string? AttestationRef)
{
    internal static KernelFindingDecisionResponse From(FindingDecision decision) =>
        new(
            decision.Status.ToString(),
            decision.DecidedBy,
            decision.DecidedAt,
            decision.Text,
            decision.DecidedOnRun is { } run ? NodeRef.For(run).ToString() : null,
            decision.AttestationId is { } att ? NodeRef.For(att).ToString() : null);
}

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
    KernelFindingDecisionResponse? Decision = null,
    string? DecisionText = null,
    string? DecidedBy = null)
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
            view.Decision is { } d ? KernelFindingDecisionResponse.From(d) : null,
            view.Decision?.Text,
            view.Decision?.DecidedBy);
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
