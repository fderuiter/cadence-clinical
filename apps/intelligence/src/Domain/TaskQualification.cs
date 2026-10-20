using System;
using System.Security.Cryptography;
using System.Text;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Modules.Intelligence.Evaluation;

namespace Cadence.Modules.Intelligence.Domain;

/// <summary>Where a configuration version stands in a tenant (ADR-2631).</summary>
public enum QualificationStatus
{
    /// <summary>Evaluated, accepted and signed: the runtime may use it.</summary>
    Qualified = 1,

    /// <summary>Taken out of use, for example after a monitoring signal; only a new qualification restores it.</summary>
    Suspended = 2,
}

/// <summary>
/// The evaluation run a qualification rests on (ADR-2631): which run, against which pinned evaluation set, whether it
/// met the configuration's acceptance criteria, and its summary as JSON. The evaluation harness (#5548) produces it.
/// </summary>
/// <param name="RunReference">The evaluation run's id in the harness.</param>
/// <param name="EvaluationSetHash">The content hash of the evaluation set the run used.</param>
/// <param name="Passed">True when the run met every acceptance criterion.</param>
/// <param name="SummaryJson">The run's metrics and per-criterion results, as JSON.</param>
public sealed record EvaluationEvidence(string RunReference, string EvaluationSetHash, bool Passed, string SummaryJson);

/// <summary>
/// A tenant's qualification of one task configuration version (ADR-2631, K-AI-5): the configuration's content hash
/// it covers, the evaluation evidence, the risk class, and the attestation of the person who signed it (21 CFR 11.50).
/// The runtime uses a configuration only while it is <see cref="QualificationStatus.Qualified"/> and its current
/// content hash is the one qualified, so changing any field of the configuration takes it out of use. Written only by
/// the qualification commands, so every change is on the kernel audit chain.
/// </summary>
public sealed class TaskQualification : AggregateRoot<Guid>
{
    private TaskQualification()
    {
    }

    private TaskQualification(TenantId tenantId, ConfigurationRef configuration)
        : base(IdFor(tenantId, configuration.Key, configuration.Version))
    {
        TenantId = tenantId;
        TaskKey = configuration.Key;
        TaskVersion = configuration.Version;
        ConfigurationHash = configuration.ContentHash;
    }

    /// <summary>The tenant that qualified the configuration.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The task key.</summary>
    public string TaskKey { get; private set; } = string.Empty;

    /// <summary>The configuration version.</summary>
    public int TaskVersion { get; private set; }

    /// <summary>The content hash of the configuration that was qualified.</summary>
    public string ConfigurationHash { get; private set; } = string.Empty;

    /// <summary>Whether the version may be used.</summary>
    public QualificationStatus Status { get; private set; }

    /// <summary>The risk class the configuration's credibility assessment gave when it was qualified.</summary>
    public ModelRiskClass RiskClass { get; private set; }

    /// <summary>The evaluation run the qualification rests on.</summary>
    public string EvaluationRunReference { get; private set; } = string.Empty;

    /// <summary>The content hash of the evaluation set the run used.</summary>
    public string EvaluationSetHash { get; private set; } = string.Empty;

    /// <summary>The evaluation run's summary, as JSON.</summary>
    public string EvaluationSummaryJson { get; private set; } = "{}";

    /// <summary>The attestation (electronic signature) of the person who qualified it.</summary>
    public Guid AttestationId { get; private set; }

    /// <summary>The person who qualified it.</summary>
    public string QualifiedBy { get; private set; } = string.Empty;

    /// <summary>When it was qualified, or qualified again.</summary>
    public DateTimeOffset QualifiedAt { get; private set; }

    /// <summary>When it was suspended, while it is.</summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>Why it was suspended, while it is.</summary>
    public string? SuspensionReason { get; private set; }

    /// <summary>The qualification's id: one per tenant, task and version, derived so it can be found without a lookup.</summary>
    public static Guid IdFor(TenantId tenantId, string key, int version)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"intelligence.TaskQualification:{tenantId.Value:D}:{key}:v{version}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>A first qualification of <paramref name="configuration"/>.</summary>
    public static TaskQualification Qualify(
        TenantId tenantId,
        TaskConfiguration configuration,
        EvaluationEvidence evidence,
        Guid attestationId,
        string qualifiedBy,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var qualification = new TaskQualification(tenantId, configuration.Ref);
        qualification.Requalify(configuration, evidence, attestationId, qualifiedBy, at);
        return qualification;
    }

    /// <summary>Qualifies the same configuration again, for example after a suspension, on new evidence and a new signature.</summary>
    /// <exception cref="InvalidOperationException">The configuration is not the version this qualification covers.</exception>
    public void Requalify(TaskConfiguration configuration, EvaluationEvidence evidence, Guid attestationId, string qualifiedBy, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedBy);
        if (configuration.Key != TaskKey || configuration.Version != TaskVersion || configuration.ContentHash != ConfigurationHash)
        {
            throw new InvalidOperationException("A qualification covers one configuration version and its content; a changed configuration is a new version.");
        }

        Status = QualificationStatus.Qualified;
        RiskClass = configuration.Credibility?.RiskClass ?? ModelRiskClass.High;
        EvaluationRunReference = evidence.RunReference;
        EvaluationSetHash = evidence.EvaluationSetHash;
        EvaluationSummaryJson = evidence.SummaryJson;
        AttestationId = attestationId;
        QualifiedBy = qualifiedBy;
        QualifiedAt = AgentRun.ToStoredPrecision(at);
        SuspendedAt = null;
        SuspensionReason = null;
    }

    /// <summary>Takes the version out of use until it is qualified again.</summary>
    public void Suspend(string reason, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = QualificationStatus.Suspended;
        SuspendedAt = AgentRun.ToStoredPrecision(at);
        SuspensionReason = reason;
    }

    /// <summary>
    /// Why <paramref name="configuration"/> may not run now, or null when it may: not qualified in this tenant,
    /// suspended, or changed since it was qualified.
    /// </summary>
    public static string? WhyNotUsable(TaskQualification? qualification, TaskConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return qualification switch
        {
            null => $"Task configuration {configuration.Key} v{configuration.Version} is not qualified in this tenant. Run its evaluation and have it signed before agents use it.",
            { Status: QualificationStatus.Suspended } => $"Task configuration {configuration.Key} v{configuration.Version} is suspended: {qualification.SuspensionReason} Only a new qualification restores it.",
            _ when qualification.ConfigurationHash != configuration.ContentHash =>
                $"Task configuration {configuration.Key} v{configuration.Version} changed after it was qualified (qualified {qualification.ConfigurationHash}, now {configuration.ContentHash}). Publish the change as a new version and qualify it.",
            _ => null,
        };
    }
}
