using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Whether a protocol version is in force in one scope of its study (spec §7.1): the whole study, the study's sites in
/// one country, or one study site. The status history is bitemporal, like an enrollment's: a change closes the recorded
/// period of the current <see cref="EffectivityStatusPeriod"/> and records what was true before and after, so the
/// effectivity can be read as of any valid and known instant. Each row names the artifact versions that evidenced it.
/// Expected failures return a <see cref="KernelError"/>; nothing here throws for them.
/// </summary>
public class Effectivity : AggregateRoot<EffectivityId>
{
    /// <summary>The most artifact versions one status change may name as evidence.</summary>
    public const int MaxEvidence = 16;

    private readonly List<EffectivityStatusPeriod> _periods = [];

    protected Effectivity() { }

    private Effectivity(EffectivityId id, TenantId tenantId, StudyId studyId, ProtocolVersionId protocolVersionId, EffectivityScope scope)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        ProtocolVersionId = protocolVersionId;
        ScopeLevel = scope.Level;
        ScopeCountry = scope.Country;
        ScopeStudySiteId = scope.StudySiteId;
    }

    public TenantId TenantId { get; private set; }

    /// <summary>The study of <see cref="ProtocolVersionId"/>, repeated so a study's effectivities are read together.</summary>
    public StudyId StudyId { get; private set; }

    public ProtocolVersionId ProtocolVersionId { get; private set; }

    public EffectivityLevel ScopeLevel { get; private set; }

    public string? ScopeCountry { get; private set; }

    public StudySiteId? ScopeStudySiteId { get; private set; }

    /// <summary>The scope this effectivity applies to.</summary>
    public EffectivityScope Scope => new(ScopeLevel, ScopeCountry, ScopeStudySiteId);

    /// <summary>The status now: the status of the current open row in <see cref="Periods"/>.</summary>
    public EffectivityStatus Status { get; private set; }

    /// <summary>When the current status began, in valid time.</summary>
    public DateTimeOffset StatusSince { get; private set; }

    /// <summary>Every bitemporal status row of this effectivity, including superseded ones.</summary>
    public IReadOnlyList<EffectivityStatusPeriod> Periods => _periods;

    /// <summary>The evidence of the current status.</summary>
    public IReadOnlyList<ArtifactVersionId> CurrentEvidence() => [.. CurrentPeriod().Evidence.Select(ArtifactVersionId.FromGuid)];

    /// <summary>
    /// Declares <paramref name="protocolVersionId"/> <see cref="EffectivityStatus.Pending"/> in <paramref name="scope"/>
    /// from <paramref name="declaredAt"/>, recorded at <paramref name="recordedAt"/>. The handler has checked that a
    /// study-site scope belongs to <paramref name="studyId"/>.
    /// </summary>
    public static CommandResult<Effectivity> Declare(
        EffectivityId id,
        TenantId tenantId,
        StudyId studyId,
        ProtocolVersionId protocolVersionId,
        EffectivityScope scope,
        DateTimeOffset declaredAt,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Problem() is { } problem)
        {
            return EffectivityErrors.ScopeInvalid.WithMessage(problem);
        }

        declaredAt = KernelTimestamps.ToStoredPrecision(declaredAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (declaredAt > recordedAt)
        {
            return EffectivityErrors.TransitionTimeInvalid;
        }

        var effectivity = new Effectivity(id, tenantId, studyId, protocolVersionId, scope);
        effectivity.Begin(EffectivityStatus.Pending, [], declaredAt, recordedAt);
        return CommandResult<Effectivity>.Success(effectivity);
    }

    /// <summary>
    /// Why the effectivity cannot move to <paramref name="to"/> at <paramref name="effectiveAt"/> when recorded at
    /// <paramref name="recordedAt"/>, or null when it can. Evidence is checked by the handler, which reads the artifacts.
    /// </summary>
    public KernelError? CheckTransition(EffectivityStatus to, DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        if (!Enum.IsDefined(to) || !EffectivityTransitions.IsAllowed(Status, to))
        {
            return EffectivityErrors.InvalidTransition.WithMessage($"An effectivity that is {Status} cannot move to {to}.");
        }

        effectiveAt = KernelTimestamps.ToStoredPrecision(effectiveAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (effectiveAt <= StatusSince || effectiveAt > recordedAt)
        {
            return EffectivityErrors.TransitionTimeInvalid;
        }

        // Two changes recorded at the same instant would leave an empty recorded period; the second one retries.
        return recordedAt <= CurrentPeriod().Period.Recorded.Start ? KernelErrors.ConcurrencyConflict : null;
    }

    /// <summary>Moves the effectivity to <paramref name="to"/> on <paramref name="evidence"/>, which the handler has verified.</summary>
    public KernelError? ChangeStatus(
        EffectivityStatus to, IReadOnlyCollection<ArtifactVersionId> evidence, DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (CheckTransition(to, effectiveAt, recordedAt) is { } invalid)
        {
            return invalid;
        }

        if (evidence.Count > MaxEvidence || evidence.Distinct().Count() != evidence.Count)
        {
            return EffectivityErrors.EvidenceInvalid.WithMessage($"Name at most {MaxEvidence} distinct artifact versions.");
        }

        if (to == EffectivityStatus.Effective && evidence.Count == 0)
        {
            return EffectivityErrors.EvidenceRequired;
        }

        var current = CurrentPeriod();
        effectiveAt = KernelTimestamps.ToStoredPrecision(effectiveAt);
        recordedAt = KernelTimestamps.ToStoredPrecision(recordedAt);
        current.Supersede(recordedAt);
        _periods.Add(new EffectivityStatusPeriod(
            this, current.Status, current.Evidence, new TemporalPeriod(new TimeRange(current.Period.Valid.Start, effectiveAt), TimeRange.From(recordedAt))));
        Begin(to, [.. evidence.Select(e => e.Value)], effectiveAt, recordedAt);
        return null;
    }

    /// <summary>The status at <paramref name="validAt"/> as known at <paramref name="knownAt"/>, or null before it was declared.</summary>
    public EffectivityStatus? StatusAsOf(DateTimeOffset validAt, DateTimeOffset knownAt) =>
        _periods.SingleOrDefault(p => p.Period.IsEffectiveAsOf(validAt, knownAt))?.Status;

    private EffectivityStatusPeriod CurrentPeriod() =>
        _periods.SingleOrDefault(p => p.IsCurrent)
        ?? throw new InvalidOperationException($"Effectivity {Id} has no current status row; load it with its periods.");

    private void Begin(EffectivityStatus status, Guid[] evidence, DateTimeOffset validFrom, DateTimeOffset recordedAt)
    {
        _periods.Add(new EffectivityStatusPeriod(this, status, evidence, new TemporalPeriod(TimeRange.From(validFrom), TimeRange.From(recordedAt))));
        Status = status;
        StatusSince = validFrom;
    }
}

/// <summary>
/// One bitemporal fact about an effectivity: it had <see cref="Status"/> on <see cref="Evidence"/> during
/// <see cref="TemporalPeriod.Valid"/>, as Cadence knew it during <see cref="TemporalPeriod.Recorded"/> (spec §4.1). Rows
/// are never changed except to close <c>recorded_period</c> (K-TIME-1), and two rows of one effectivity never overlap in
/// both times (K-TIME-2).
/// </summary>
public class EffectivityStatusPeriod : IBitemporalEntity
{
    protected EffectivityStatusPeriod() { }

    internal EffectivityStatusPeriod(Effectivity effectivity, EffectivityStatus status, Guid[] evidence, TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = effectivity.TenantId;
        EffectivityId = effectivity.Id;
        Status = status;
        Evidence = evidence;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public EffectivityId EffectivityId { get; private set; }

    public EffectivityStatus Status { get; private set; }

    /// <summary>The artifact versions evidencing this status; empty for a status that needs none.</summary>
    public Guid[] Evidence { get; private set; } = [];

    public TemporalPeriod Period { get; private set; }

    /// <summary>True while this row is both current knowledge and open-ended in valid time: the status now.</summary>
    internal bool IsCurrent => Period.Recorded.IsOpen && Period.Valid.IsOpen;

    /// <summary>Closes this row's recorded period at <paramref name="supersededAt"/>: the only change K-TIME-1 allows.</summary>
    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}

/// <summary>
/// Resolves which protocol versions are in force at a study site (spec §7.1): for each version, the most specific of its
/// effectivities that applies to the site decides, study site before country before study.
/// </summary>
public static class EffectivityResolution
{
    /// <summary>
    /// The effectivities among <paramref name="effectivities"/> that decide, per protocol version, at
    /// <paramref name="studySite"/> in <paramref name="country"/> at <paramref name="validAt"/> as known at
    /// <paramref name="knownAt"/>, and are <see cref="EffectivityStatus.Effective"/>. An effectivity not yet declared at
    /// that instant does not decide.
    /// </summary>
    public static IReadOnlyList<Effectivity> EffectiveAt(
        IEnumerable<Effectivity> effectivities, StudySiteId studySite, string country, DateTimeOffset validAt, DateTimeOffset knownAt)
    {
        ArgumentNullException.ThrowIfNull(effectivities);
        return effectivities
            .Where(e => e.Scope.Covers(studySite, country) && e.StatusAsOf(validAt, knownAt) is not null)
            .GroupBy(e => e.ProtocolVersionId)
            .Select(g => g.MaxBy(e => (int)e.ScopeLevel)!)
            .Where(e => e.StatusAsOf(validAt, knownAt) == EffectivityStatus.Effective)
            .ToList();
    }
}
