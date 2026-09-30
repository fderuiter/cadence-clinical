using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Domain.Validation;

/// <summary>A released rule pack version a run evaluated, as the handler resolved it from the Metadata Registry.</summary>
/// <param name="LibraryItemId">The pack's library item version.</param>
/// <param name="Key">The pack's key.</param>
/// <param name="Version">The library item version number.</param>
/// <param name="ContentHash">The pack content's hash.</param>
/// <param name="VersionIndex">The library item's version index when the run read it, for lineage.</param>
public sealed record RunRulePack(LibraryItemId LibraryItemId, string Key, int Version, string ContentHash, long VersionIndex);

/// <summary>
/// One validation run over a study (spec §10.3): the released rule pack versions evaluated at one stage, the inputs
/// and datasets evaluated, and every problem detected. A run is fixed once recorded; findings and their statuses are
/// derived from the runs of a study (<see cref="FindingLedger"/>), so a later run never rewrites an earlier one.
/// </summary>
[DerivedObject]
public sealed class ValidationRun : AggregateRoot<ValidationRunId>
{
    /// <summary>The longest standard, standard version or agency name.</summary>
    public const int MaxNameLength = 64;

    private readonly List<ValidationRunPack> _packs = [];
    private readonly List<ValidationRunDataset> _datasets = [];
    private readonly List<ValidationRunInput> _inputs = [];
    private readonly List<ValidationDetectionRecord> _detections = [];

    private ValidationRun() { }

    private ValidationRun(
        ValidationRunId id,
        TenantId tenantId,
        RecordValidationRun command,
        IReadOnlyList<RunRulePack> packs)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = command.StudyId;
        Stage = command.Stage;
        AsOf = command.AsOf;
        Standard = command.Standard;
        StandardVersion = command.StandardVersion;
        Agency = command.Agency;
        StartedAt = command.StartedAt.ToUniversalTime();
        CompletedAt = command.CompletedAt.ToUniversalTime();
        DetectionCount = command.Detections.Count;

        _packs.AddRange(packs.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new ValidationRunPack(tenantId, id, p)));
        _datasets.AddRange(command.Datasets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(d => new ValidationRunDataset(tenantId, id, d)));
        _inputs.AddRange(command.Inputs.Distinct().OrderBy(i => i.ToString(), StringComparer.Ordinal).Select(i => new ValidationRunInput(tenantId, id, i)));
        var packsByKey = packs.ToDictionary(p => p.Key, StringComparer.Ordinal);
        _detections.AddRange(command.Detections.Select(d => new ValidationDetectionRecord(tenantId, id, command.StudyId, command.Stage, packsByKey[d.PackKey], d)));
    }

    public TenantId TenantId { get; private set; }

    public StudyId StudyId { get; private set; }

    public ValidationStage Stage { get; private set; }

    /// <summary>The date the rules' effective periods were evaluated at.</summary>
    public DateOnly AsOf { get; private set; }

    public string? Standard { get; private set; }

    public string? StandardVersion { get; private set; }

    public string? Agency { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public int DetectionCount { get; private set; }

    public IReadOnlyList<ValidationRunPack> Packs => _packs;

    public IReadOnlyList<ValidationRunDataset> Datasets => _datasets;

    public IReadOnlyList<ValidationRunInput> Inputs => _inputs;

    public IReadOnlyList<ValidationDetectionRecord> Detections => _detections;

    /// <summary>
    /// Records a run. <paramref name="packs"/> are the command's rule packs, resolved and checked by the caller;
    /// <paramref name="now"/> is the recording time, which <see cref="RecordValidationRun.CompletedAt"/> may not follow.
    /// </summary>
    public static (ValidationRun? Run, KernelError? Error) Record(
        TenantId tenantId, RecordValidationRun command, IReadOnlyList<RunRulePack> packs, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(packs);

        var problems = Validate(command, packs, now);
        return problems.Count > 0
            ? (null, ValidationErrors.RunInvalid.WithMessage(string.Join(" ", problems.Take(20))))
            : (new ValidationRun(command.RunId, tenantId, command, packs), null);
    }

    private static List<string> Validate(RecordValidationRun command, IReadOnlyList<RunRulePack> packs, DateTimeOffset now)
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(command.Stage))
        {
            problems.Add($"Unknown stage {(int)command.Stage}.");
        }

        if (command.RulePacks is null || command.RulePacks.Count == 0)
        {
            problems.Add("A run evaluates at least one rule pack.");
        }

        if (packs.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != packs.Count)
        {
            problems.Add("A run evaluates at most one version of each rule pack.");
        }

        CheckName(command.Standard, "standard", problems);
        CheckName(command.StandardVersion, "standard version", problems);
        CheckName(command.Agency, "agency", problems);
        if (command.StandardVersion is not null && command.Standard is null)
        {
            problems.Add("A standard version needs its standard.");
        }

        if (command.Inputs is null || command.Inputs.Count > RecordValidationRun.MaxInputs)
        {
            problems.Add($"A run names 0 to {RecordValidationRun.MaxInputs} inputs.");
        }
        else if (command.Inputs.FirstOrDefault(i => i is null || i.Version is null || !NodeKinds.IsRegistered(i.Kind)) is { } unversioned)
        {
            problems.Add($"Input {unversioned?.ToString() ?? "null"} must be a versioned reference to a kernel object.");
        }

        var datasets = new HashSet<string>(StringComparer.Ordinal);
        if (command.Datasets is null || command.Datasets.Count == 0 || command.Datasets.Count > RecordValidationRun.MaxDatasets)
        {
            problems.Add($"A run covers 1 to {RecordValidationRun.MaxDatasets} datasets.");
        }
        else
        {
            foreach (var dataset in command.Datasets)
            {
                if (dataset is null || dataset.Length == 0 || dataset.Length > FindingTarget.MaxNameLength || dataset.Trim().Length != dataset.Length)
                {
                    problems.Add($"Dataset names are 1 to {FindingTarget.MaxNameLength} characters with no surrounding spaces.");
                    break;
                }

                datasets.Add(dataset);
            }
        }

        if (command.CompletedAt < command.StartedAt)
        {
            problems.Add("A run cannot complete before it starts.");
        }

        if (command.CompletedAt > now)
        {
            problems.Add("A run cannot complete after it is recorded.");
        }

        if (command.Detections is null || command.Detections.Count > RecordValidationRun.MaxDetections)
        {
            problems.Add($"A run records 0 to {RecordValidationRun.MaxDetections} detections; record a larger evaluation as several runs over fewer datasets.");
            return problems;
        }

        var packKeys = packs.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var findings = new HashSet<ValidationFindingId>();
        for (var i = 0; i < command.Detections.Count && problems.Count < 20; i++)
        {
            CheckDetection(command, i, packKeys, datasets, findings, problems);
        }

        return problems;
    }

    private static void CheckDetection(
        RecordValidationRun command, int index, HashSet<string> packKeys, HashSet<string> datasets, HashSet<ValidationFindingId> findings, List<string> problems)
    {
        var detection = command.Detections[index];
        var at = $"Detection {index + 1}:";
        if (detection is null)
        {
            problems.Add($"{at} missing.");
            return;
        }

        if (detection.PackKey is null || !packKeys.Contains(detection.PackKey))
        {
            problems.Add($"{at} rule pack \"{detection.PackKey}\" is not one the run evaluated.");
        }

        if (string.IsNullOrWhiteSpace(detection.RuleId) || detection.RuleId.Length > ValidationDetection.MaxRuleIdLength)
        {
            problems.Add($"{at} a rule id of 1 to {ValidationDetection.MaxRuleIdLength} characters is required.");
        }

        if (detection.RuleVersion < 1)
        {
            problems.Add($"{at} rule versions start at 1.");
        }

        if (!Enum.IsDefined(detection.Severity))
        {
            problems.Add($"{at} unknown severity {(int)detection.Severity}.");
        }

        if (detection.Target is null)
        {
            problems.Add($"{at} a target is required.");
            return;
        }

        problems.AddRange(detection.Target.Problems().Select(p => $"{at} {p}"));
        if (detection.Target.Dataset is not null && !datasets.Contains(detection.Target.Dataset))
        {
            problems.Add($"{at} dataset \"{detection.Target.Dataset}\" is not one the run covers.");
        }

        if (string.IsNullOrWhiteSpace(detection.Message) || detection.Message.Length > ValidationDetection.MaxTextLength
            || detection.Expected is { Length: > ValidationDetection.MaxTextLength })
        {
            problems.Add($"{at} a message of 1 to {ValidationDetection.MaxTextLength} characters is required, and an expectation may not be longer.");
        }

        if (detection.Observed is null || detection.Observed.Any(o => o is null || string.IsNullOrEmpty(o.Variable) || o.Variable.Length > FindingTarget.MaxNameLength
            || o.Value is { Length: > ValidationDetection.MaxTextLength }))
        {
            problems.Add($"{at} observed values need a variable name of 1 to {FindingTarget.MaxNameLength} characters and a value of at most {ValidationDetection.MaxTextLength}.");
        }

        if (problems.Count == 0 && detection.PackKey is not null && detection.RuleId is not null
            && !findings.Add(ValidationFindingId.For(TenantId.Empty, Identity(command, detection))))
        {
            problems.Add($"{at} the run already detected rule {detection.RuleId} of {detection.PackKey} on this target.");
        }
    }

    /// <summary>The identity of the finding <paramref name="detection"/> belongs to.</summary>
    public static FindingIdentity Identity(RecordValidationRun command, ValidationDetection detection)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(detection);
        return new FindingIdentity(command.StudyId, command.Stage, detection.PackKey, detection.RuleId, detection.Target);
    }

    private static void CheckName(string? value, string name, List<string> problems)
    {
        if (value is not null && (value.Length == 0 || value.Length > MaxNameLength || value.Trim().Length != value.Length))
        {
            problems.Add($"The {name} must be 1 to {MaxNameLength} characters with no surrounding spaces.");
        }
    }
}

/// <summary>A rule pack version a run evaluated (<c>kernel.validation_run_packs</c>).</summary>
public sealed class ValidationRunPack
{
    private ValidationRunPack() { }

    internal ValidationRunPack(TenantId tenantId, ValidationRunId runId, RunRulePack pack)
    {
        TenantId = tenantId;
        RunId = runId;
        PackKey = pack.Key;
        LibraryItemId = pack.LibraryItemId;
        PackVersion = pack.Version;
        ContentHash = pack.ContentHash;
    }

    public TenantId TenantId { get; private set; }

    public ValidationRunId RunId { get; private set; }

    public string PackKey { get; private set; } = string.Empty;

    public LibraryItemId LibraryItemId { get; private set; }

    public int PackVersion { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;
}

/// <summary>A dataset a run covered (<c>kernel.validation_run_datasets</c>).</summary>
public sealed class ValidationRunDataset
{
    private ValidationRunDataset() { }

    internal ValidationRunDataset(TenantId tenantId, ValidationRunId runId, string dataset)
    {
        TenantId = tenantId;
        RunId = runId;
        Dataset = dataset;
    }

    public TenantId TenantId { get; private set; }

    public ValidationRunId RunId { get; private set; }

    public string Dataset { get; private set; } = string.Empty;
}

/// <summary>A versioned specification a run read (<c>kernel.validation_run_inputs</c>).</summary>
public sealed class ValidationRunInput
{
    private ValidationRunInput() { }

    internal ValidationRunInput(TenantId tenantId, ValidationRunId runId, NodeRef input)
    {
        TenantId = tenantId;
        RunId = runId;
        Input = input.ToString();
    }

    public TenantId TenantId { get; private set; }

    public ValidationRunId RunId { get; private set; }

    /// <summary>The input's versioned NodeRef text.</summary>
    public string Input { get; private set; } = string.Empty;
}

/// <summary>One problem a run detected (<c>kernel.validation_detections</c>), keyed by the finding it belongs to.</summary>
public sealed class ValidationDetectionRecord
{
    private ValidationDetectionRecord() { }

    internal ValidationDetectionRecord(
        TenantId tenantId, ValidationRunId runId, StudyId studyId, ValidationStage stage, RunRulePack pack, ValidationDetection detection)
    {
        TenantId = tenantId;
        RunId = runId;
        StudyId = studyId;
        Stage = stage;
        FindingId = ValidationFindingId.For(tenantId, new FindingIdentity(studyId, stage, detection.PackKey, detection.RuleId, detection.Target));
        PackKey = pack.Key;
        PackItemId = pack.LibraryItemId;
        RuleId = detection.RuleId;
        RuleVersion = detection.RuleVersion;
        Severity = detection.Severity;
        TargetKind = detection.Target.Kind;
        Dataset = detection.Target.Dataset;
        TargetKey = detection.Target.Key;
        Variable = detection.Target.Variable;
        Message = detection.Message;
        Expected = detection.Expected;
        Observed = JsonSerializer.Serialize(detection.Observed.Select(o => new[] { o.Variable, o.Value }));
    }

    public TenantId TenantId { get; private set; }

    public ValidationRunId RunId { get; private set; }

    public ValidationFindingId FindingId { get; private set; }

    public StudyId StudyId { get; private set; }

    public ValidationStage Stage { get; private set; }

    public string PackKey { get; private set; } = string.Empty;

    /// <summary>The rule pack version (library item) that held the rule.</summary>
    public LibraryItemId PackItemId { get; private set; }

    public string RuleId { get; private set; } = string.Empty;

    public int RuleVersion { get; private set; }

    public ValidationSeverity Severity { get; private set; }

    public FindingTargetKind TargetKind { get; private set; }

    public string Dataset { get; private set; } = string.Empty;

    public string TargetKey { get; private set; } = string.Empty;

    public string? Variable { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public string? Expected { get; private set; }

    /// <summary>The observed values as a JSON array of <c>[variable, value]</c> pairs, in the rule's order.</summary>
    public string Observed { get; private set; } = "[]";

    /// <summary>The target as a value.</summary>
    public FindingTarget Target => new(TargetKind, Dataset, TargetKey, Variable);

    /// <summary>The observed values as values.</summary>
    public IReadOnlyList<ObservedVariable> ObservedValues =>
        [.. (JsonSerializer.Deserialize<string?[][]>(Observed) ?? []).Select(pair => new ObservedVariable(pair[0]!, pair[1]))];
}
