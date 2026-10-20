using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Runs;

namespace Cadence.Modules.Intelligence.Domain;

/// <summary>
/// A stored evaluation run (ADR-2634 §4.4, #5574): the run record a qualification rests on, kept in the module's schema
/// as canonical JSON with its kernel content hash. The server admits a record only when it evaluated a shipped
/// configuration version against exactly its pinned set, criteria and provider, and its metrics and verdict follow
/// from its case results (<see cref="Problems"/>), so a qualification never rests on a caller's word that a run passed.
/// Written only by <c>RecordEvaluationRun</c>, never changed.
/// </summary>
public sealed class EvaluationRun : AggregateRoot<Guid>
{
    private EvaluationRun()
    {
    }

    private EvaluationRun(TenantId tenantId, EvaluationRunRecord record)
        : base(IdFor(tenantId, record.RunId))
    {
        TenantId = tenantId;
        RunId = record.RunId;
        TaskKey = record.Configuration.Key;
        TaskVersion = record.Configuration.Version;
        ConfigurationHash = record.Configuration.ContentHash;
        EvaluationSetId = record.Set.SetId;
        EvaluationSetVersion = record.Set.Version;
        EvaluationSetHash = record.Set.ContentHash;
        Provider = record.Provider.Name;
        Model = record.Provider.ModelId;
        Accepted = record.Verdict.Accepted;
        StartedAt = AgentRun.ToStoredPrecision(record.StartedAt);
        CompletedAt = AgentRun.ToStoredPrecision(record.CompletedAt);
        RecordJson = record.ToJson();
        RecordHash = Cadence.Kernel.Trust.ContentHash.Compute(RecordJson);
        SummaryJson = JsonSerializer.Serialize(new EvaluationRunSummary(record.Metrics, record.Verdict), EvaluationJson.Options);
    }

    /// <summary>The tenant the run was recorded in.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The run's id in the harness.</summary>
    public Guid RunId { get; private set; }

    /// <summary>The task key evaluated.</summary>
    public string TaskKey { get; private set; } = string.Empty;

    /// <summary>The configuration version evaluated.</summary>
    public int TaskVersion { get; private set; }

    /// <summary>The content hash of the configuration evaluated.</summary>
    public string ConfigurationHash { get; private set; } = string.Empty;

    /// <summary>The evaluation set's id.</summary>
    public string EvaluationSetId { get; private set; } = string.Empty;

    /// <summary>The evaluation set's version.</summary>
    public int EvaluationSetVersion { get; private set; }

    /// <summary>The evaluation set's content hash.</summary>
    public string EvaluationSetHash { get; private set; } = string.Empty;

    /// <summary>The provider the run used.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>The model the run used.</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>True when the run met the configuration's acceptance criteria.</summary>
    public bool Accepted { get; private set; }

    /// <summary>When the run started, from the harness's clock.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When the run ended, from the harness's clock.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>The run record as canonical JSON, exactly as hashed.</summary>
    public string RecordJson { get; private set; } = "{}";

    /// <summary>The kernel content hash of <see cref="RecordJson"/>, which a qualification binds to.</summary>
    public string RecordHash { get; private set; } = string.Empty;

    /// <summary>The run's metrics and verdict, as JSON.</summary>
    public string SummaryJson { get; private set; } = "{}";

    /// <summary>The stored run's id: one per tenant and run, derived so it can be found without a lookup.</summary>
    public static Guid IdFor(TenantId tenantId, Guid runId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"intelligence.EvaluationRun:{tenantId.Value:D}:{runId:D}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Stores <paramref name="record"/>, which must have no <see cref="Problems"/> against its configuration.</summary>
    public static EvaluationRun Record(TenantId tenantId, EvaluationRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new EvaluationRun(tenantId, record);
    }

    /// <summary>The evidence a qualification records from this run.</summary>
    public EvaluationEvidence Evidence() => new(RunId.ToString("D"), RecordHash, EvaluationSetHash, SummaryJson);

    /// <summary>
    /// Every reason <paramref name="record"/> cannot be evidence for <paramref name="configuration"/>: it evaluated
    /// another version or content, another evaluation set, other acceptance criteria or another provider, it has no or
    /// repeated cases, a case is marked passed although a check failed, its times run backwards, judge scores lack the rubric, judge and calibration they count under, or
    /// its metrics or verdict are not what the harness computes from its case results. Empty when it is sound.
    /// </summary>
    public static IReadOnlyList<string> Problems(TaskConfiguration configuration, EvaluationRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(record);
        var problems = new List<string>();
        if (record.RunId == Guid.Empty)
        {
            problems.Add("The run has no id.");
        }

        if (record.Configuration != configuration.Ref)
        {
            problems.Add($"The run evaluated {Describe(record.Configuration)}, not {Describe(configuration.Ref)}.");
        }

        if (record.Set != configuration.EvaluationSet)
        {
            problems.Add(
                $"The run used set {record.Set.SetId} v{record.Set.Version} ({record.Set.ContentHash}), but the configuration pins "
                + (configuration.EvaluationSet is { } pinned ? $"{pinned.SetId} v{pinned.Version} ({pinned.ContentHash})." : "no set."));
        }

        if (record.Criteria != configuration.AcceptanceCriteria)
        {
            problems.Add("The run applied acceptance criteria other than the configuration's.");
        }

        if (!string.Equals(record.Provider.Name, configuration.Provider, StringComparison.Ordinal))
        {
            problems.Add($"The run used provider '{record.Provider.Name}', but the configuration runs on '{configuration.Provider}'.");
        }

        if (record.CompletedAt < record.StartedAt)
        {
            problems.Add("The run ended before it started.");
        }

        if (record.Cases.Count == 0)
        {
            problems.Add("The run has no case results.");
        }

        foreach (var repeated in record.Cases.GroupBy(c => c.CaseId, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"Case '{repeated.Key}' appears {repeated.Count()} times.");
        }

        foreach (var c in record.Cases.Where(c => c.Passed && (c.Error is not null || c.Checks.Any(k => !k.Passed))))
        {
            problems.Add($"Case '{c.CaseId}' is marked passed but its run failed or a check failed.");
        }

        if (record.Cases.Any(c => c.JudgeScores is { Count: > 0 }) && (record.Rubric is null || record.Judge is null || string.IsNullOrEmpty(record.CalibrationHash)))
        {
            problems.Add("Judge scores count only with the rubric, the judge configuration and the calibration they were calibrated under.");
        }

        if (problems.Count == 0 && configuration.AcceptanceCriteria is { } criteria)
        {
            var metrics = EvaluationHarness.Measure(record.Cases);
            if (Json(metrics) != Json(record.Metrics))
            {
                problems.Add("The run's metrics are not the ones its case results give.");
            }

            if (Json(EvaluationHarness.Decide(criteria, record.Cases, metrics)) != Json(record.Verdict))
            {
                problems.Add("The run's verdict is not the one its case results and the configuration's criteria give.");
            }
        }

        return problems;
    }

    private static string Describe(ConfigurationRef r) => $"{r.Key} v{r.Version} ({r.ContentHash})";

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, EvaluationJson.Options);
}
