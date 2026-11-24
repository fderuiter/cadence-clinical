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
    IReadOnlyList<KernelRuleSummaryResponse> Rules)
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
            rules);
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

/// <summary>Optional request body for <c>POST /api/v1/kernel/library-items/{itemRef}/approve</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Approved</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelApproveLibraryItemRequest(string? Meaning = null, Guid? AttestationId = null);

/// <summary>Request body for <c>PUT /api/v1/kernel/studies/{studyRef}/standards</c>.</summary>
/// <param name="RulePackRefs">Approved rule-pack <c>NodeRef</c>s to pin on the study (replaces pinned rule packs while preserving other declared standards/pins).</param>
public sealed record KernelDeclareStudyRulePacksRequest(IReadOnlyList<string> RulePackRefs);

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
/// <param name="RulePackRef">Previewed rule-pack <c>NodeRef</c> (<c>library_item:&lt;uuid&gt;</c>).</param>
/// <param name="RulePackLabel">Human-readable rule-pack title and version.</param>
/// <param name="SiteRef">Study site <c>NodeRef</c> (<c>study_site:&lt;uuid&gt;</c>), when visible.</param>
/// <param name="SiteLabel">Human-readable study site label, or <c>masked</c> when restricted.</param>
/// <param name="ParticipantRef">Enrollment <c>NodeRef</c> (<c>enrollment:&lt;uuid&gt;</c>), when visible.</param>
/// <param name="ParticipantId">Participant <c>NodeRef</c> (<c>participant:&lt;uuid&gt;</c>), when available.</param>
/// <param name="ParticipantLabel">Human-facing participant subject code, or <c>masked</c> when restricted.</param>
/// <param name="PlannedActivityId">Scheduled visit (<c>PlannedActivity</c>) identifier on the form, when scheduled.</param>
/// <param name="VisitLabel">Human-readable scheduled visit title.</param>
/// <param name="FormRef">Form instance <c>NodeRef</c> (<c>collection_instance:&lt;uuid&gt;</c>).</param>
/// <param name="FormLabel">Human-readable eCRF form title.</param>
/// <param name="CollectionSpecId">Governing <c>CollectionSpec</c> identifier on the form.</param>
/// <param name="ItemLabel">Human-readable item prompt or concept label.</param>
/// <param name="ObservationRef">Primary observation <c>NodeRef</c> (<c>observation:&lt;uuid&gt;</c>), when recorded and visible.</param>
/// <param name="ObservationLabel">Human-readable observation summary, or <c>masked</c> when blinded/restricted.</param>
/// <param name="QueryRef">Linked discrepancy query <c>NodeRef</c> (<c>form_query:&lt;uuid&gt;</c>), when present and visible.</param>
/// <param name="QueryId">Linked discrepancy query UUID, when present and visible.</param>
/// <param name="QueryLabel">Human-readable discrepancy query label, or <c>masked</c> when restricted.</param>
public sealed record KernelRulePackPreviewViolationResponse(
    Guid FormId,
    string PackKey,
    string RuleId,
    int RuleVersion,
    string Severity,
    string Dataset,
    string? ItemId,
    string Message,
    string? RemediationGuidance,
    string? RulePackRef = null,
    string? RulePackLabel = null,
    string? SiteRef = null,
    string? SiteLabel = null,
    string? ParticipantRef = null,
    string? ParticipantId = null,
    string? ParticipantLabel = null,
    string? PlannedActivityId = null,
    string? VisitLabel = null,
    string? FormRef = null,
    string? FormLabel = null,
    string? CollectionSpecId = null,
    string? ItemLabel = null,
    string? ObservationRef = null,
    string? ObservationLabel = null,
    string? QueryRef = null,
    Guid? QueryId = null,
    string? QueryLabel = null)
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
            violation.RemediationGuidance,
            FormRef: violation.FormId.IsEmpty ? null : NodeRef.For(violation.FormId).ToString(),
            CollectionSpecId: violation.Dataset);
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

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/classify</c>.</summary>
/// <param name="Classification">Finding classification (<c>DataIssue</c>, <c>MappingIssue</c>, <c>MetadataIssue</c>, <c>RuleIssue</c>, <c>NotAnIssue</c>).</param>
public sealed record KernelClassifyFindingRequest(string Classification);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/accept</c>.</summary>
/// <param name="Rationale">Clinical or operational rationale for accepting the finding as it is.</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelAcceptFindingRequest(string Rationale, Guid? AttestationId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/validation/findings/{findingRef}/explain</c>.</summary>
/// <param name="Explanation">Reviewer-guide explanation for the finding.</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelExplainFindingRequest(string Explanation, Guid? AttestationId = null);

/// <summary>Summary of a validation finding returned by <c>GET /api/v1/kernel/studies/{studyRef}/validation/findings</c>.</summary>
/// <param name="Ref">Finding <c>NodeRef</c> (<c>validation_finding:&lt;uuid&gt;</c>).</param>
/// <param name="StudyRef">Owning study <c>NodeRef</c> (<c>study:&lt;uuid&gt;</c>).</param>
/// <param name="Stage">Validation lifecycle stage.</param>
/// <param name="PackKey">Rule pack key.</param>
/// <param name="RulePackRef">Rule-pack <c>NodeRef</c> (<c>library_item:&lt;uuid&gt;</c>).</param>
/// <param name="RuleId">Rule identifier within the pack.</param>
/// <param name="RuleVersion">Rule version number.</param>
/// <param name="Severity">Finding severity (<c>Error</c>, <c>Warning</c>, <c>Notice</c>).</param>
/// <param name="Status">Derived finding status (<c>Open</c>, <c>Investigating</c>, <c>Resolved</c>, <c>Accepted</c>, <c>Explained</c>).</param>
/// <param name="Blocks">Whether this finding currently blocks its lifecycle stage gate.</param>
/// <param name="TargetKind">Strongly-typed finding target kind (<see cref="FindingTargetKind"/>).</param>
/// <param name="Dataset">Target dataset or collection specification identifier.</param>
/// <param name="RecordKey">Target record key or kernel <c>NodeRef</c>.</param>
/// <param name="Variable">Optional narrowed variable or item identifier.</param>
/// <param name="Message">Discrepancy message reported by the rule.</param>
/// <param name="Expected">Optional expected value or condition text.</param>
/// <param name="Observed">Observed variable values evaluated by the rule.</param>
/// <param name="FirstDetectedRunRef">First detecting validation run <c>NodeRef</c>.</param>
/// <param name="LastDetectedRunRef">Latest detecting validation run <c>NodeRef</c>.</param>
/// <param name="ResolvedByRunRef">Resolving validation run <c>NodeRef</c>, when resolved.</param>
/// <param name="Classification">Assigned finding classification, when classified.</param>
/// <param name="Decision">Decision in force on the finding, when present.</param>
/// <param name="RulePackLabel">Human-readable rule-pack title and version.</param>
/// <param name="TargetNodeRef">Resolved target kernel <c>NodeRef</c> (<c>collection_instance:&lt;uuid&gt;</c>, <c>observation:&lt;uuid&gt;</c>, or design element).</param>
/// <param name="TargetLabel">Human-readable target label free of raw UUIDs.</param>
/// <param name="SiteRef">Study site <c>NodeRef</c> (<c>study_site:&lt;uuid&gt;</c>), when resolved and visible.</param>
/// <param name="SiteLabel">Human-readable study site label, or <c>masked</c> when restricted.</param>
/// <param name="ParticipantRef">Enrollment <c>NodeRef</c> (<c>enrollment:&lt;uuid&gt;</c>), when resolved and visible.</param>
/// <param name="ParticipantId">Participant <c>NodeRef</c> (<c>participant:&lt;uuid&gt;</c>), when available.</param>
/// <param name="ParticipantLabel">Human-facing participant subject code, or <c>masked</c> when restricted.</param>
/// <param name="PlannedActivityId">Scheduled visit (<c>PlannedActivity</c>) identifier on the form, when scheduled.</param>
/// <param name="VisitLabel">Human-readable scheduled visit title.</param>
/// <param name="FormRef">Form instance <c>NodeRef</c> (<c>collection_instance:&lt;uuid&gt;</c>), when resolved and visible.</param>
/// <param name="FormId">Form instance UUID string, when resolved and visible.</param>
/// <param name="FormLabel">Human-readable eCRF form title.</param>
/// <param name="CollectionSpecId">Governing <c>CollectionSpec</c> identifier on the form.</param>
/// <param name="ItemId">Resolved item identifier on the form.</param>
/// <param name="ItemLabel">Human-readable item prompt or concept label.</param>
/// <param name="ObservationRef">Primary observation <c>NodeRef</c> (<c>observation:&lt;uuid&gt;</c>), when resolved and visible.</param>
/// <param name="ObservationLabel">Human-readable observation summary, or <c>masked</c> when blinded/restricted.</param>
/// <param name="QueryRef">Linked discrepancy query <c>NodeRef</c> (<c>form_query:&lt;uuid&gt;</c>), when present and visible.</param>
/// <param name="QueryId">Linked discrepancy query UUID, when present and visible.</param>
/// <param name="QueryLabel">Human-readable discrepancy query label, or <c>masked</c> when restricted.</param>
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
    FindingTargetKind TargetKind,
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
    string? RulePackLabel = null,
    string? TargetNodeRef = null,
    string? TargetLabel = null,
    string? SiteRef = null,
    string? SiteLabel = null,
    string? ParticipantRef = null,
    string? ParticipantId = null,
    string? ParticipantLabel = null,
    string? PlannedActivityId = null,
    string? VisitLabel = null,
    string? FormRef = null,
    string? FormId = null,
    string? FormLabel = null,
    string? CollectionSpecId = null,
    string? ItemId = null,
    string? ItemLabel = null,
    string? ObservationRef = null,
    string? ObservationLabel = null,
    string? QueryRef = null,
    Guid? QueryId = null,
    string? QueryLabel = null)
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
            view.Target.Kind,
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
            TargetNodeRef: view.Target.Node?.ToString(),
            TargetLabel: view.TargetLabel);
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
