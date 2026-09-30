using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Evidence about an actor: completed training, protocol training, a credential or a certificate, with the artifact
/// version that proves it (spec §9.3, K-AUTH-5). Its validity is bitemporal (<see cref="QualificationPeriod"/>), like a
/// grant's: a revocation closes the recorded period of the current row and records the shortened validity, so a
/// decision can be re-evaluated as of any valid and known instant, and training recorded late cannot validate an
/// earlier action (K-AUTH-3).
/// </summary>
public partial class Qualification : AggregateRoot<QualificationId>
{
    /// <summary>The longest qualification code.</summary>
    public const int MaxCodeLength = 64;

    private readonly List<QualificationPeriod> _periods = [];

    protected Qualification() { }

    private Qualification(
        QualificationId id, TenantId tenantId, Actor holder, QualificationKind kind, string code, ProtocolVersionId? protocolVersionId,
        ArtifactVersionId evidence, string recordedBy)
        : base(id)
    {
        TenantId = tenantId;
        HolderKind = holder.Kind;
        HolderId = holder.Id;
        Kind = kind;
        Code = code;
        ProtocolVersionId = protocolVersionId;
        EvidenceArtifactVersionId = evidence;
        RecordedBy = recordedBy;
    }

    public TenantId TenantId { get; private set; }

    public ActorKind HolderKind { get; private set; }

    public string HolderId { get; private set; } = string.Empty;

    public QualificationKind Kind { get; private set; }

    /// <summary>What the qualification is, in lower snake_case; requirements name it.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The protocol version trained on, for protocol training only.</summary>
    public ProtocolVersionId? ProtocolVersionId { get; private set; }

    /// <summary>The artifact version that proves the qualification, such as a training record or a licence.</summary>
    public ArtifactVersionId EvidenceArtifactVersionId { get; private set; }

    /// <summary>The actor that recorded the qualification (<see cref="Actor.ToString"/>).</summary>
    public string RecordedBy { get; private set; } = string.Empty;

    public Actor Holder => new(HolderKind, HolderId);

    /// <summary>The bitemporal validity history, oldest first.</summary>
    public IReadOnlyList<QualificationPeriod> Periods => _periods;

    /// <summary>The validity as currently known, or null once revoked entirely.</summary>
    public TimeRange? CurrentValidity => _periods.SingleOrDefault(p => p.Period.Recorded.IsOpen)?.Period.Valid;

    /// <summary>True when <paramref name="code"/> is a well-formed qualification code: lower snake_case, at most 64 characters.</summary>
    public static bool IsWellFormedCode(string? code) => code is { Length: > 0 and <= MaxCodeLength } && CodePattern().IsMatch(code);

    /// <summary>
    /// Records a qualification valid from <paramref name="validFrom"/> and known from <paramref name="recordedAt"/>. The
    /// handler checks that the evidence and protocol version exist.
    /// </summary>
    public static CommandResult<Qualification> Record(
        QualificationId id,
        TenantId tenantId,
        Actor? holder,
        QualificationKind kind,
        string? code,
        ProtocolVersionId? protocolVersionId,
        ArtifactVersionId evidence,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo,
        Actor recordedBy,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(recordedBy);
        if (holder is null || !Enum.IsDefined(kind) || !IsWellFormedCode(code))
        {
            return QualificationErrors.CodeInvalid;
        }

        if ((kind == QualificationKind.ProtocolTraining) != protocolVersionId.HasValue)
        {
            return QualificationErrors.ProtocolVersionMismatch;
        }

        if (ValidityOf(validFrom, validTo) is not { } valid)
        {
            return QualificationErrors.ValidityInvalid;
        }

        var qualification = new Qualification(id, tenantId, holder, kind, code!, protocolVersionId, evidence, recordedBy.ToString());
        qualification._periods.Add(new QualificationPeriod(qualification, new TemporalPeriod(valid, TimeRange.From(KernelTimestamps.ToStoredPrecision(recordedAt)))));
        return CommandResult<Qualification>.Success(qualification);
    }

    /// <summary>
    /// Ends the validity at <paramref name="effectiveAt"/>, as known from <paramref name="recordedAt"/>. Refused when
    /// nothing is in effect by then.
    /// </summary>
    public KernelError? Revoke(DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        var current = _periods.SingleOrDefault(p => p.Period.Recorded.IsOpen);
        var at = KernelTimestamps.ToStoredPrecision(effectiveAt);
        if (current is null || (current.Period.Valid.End is { } end && at >= end))
        {
            return QualificationErrors.RevocationInvalid;
        }

        var known = KernelTimestamps.ToStoredPrecision(recordedAt);
        current.Supersede(known);
        if (at > current.Period.Valid.Start)
        {
            _periods.Add(new QualificationPeriod(this, new TemporalPeriod(new TimeRange(current.Period.Valid.Start, at), TimeRange.From(known))));
        }

        return null;
    }

    /// <summary>True when the qualification was valid at <paramref name="at"/> as known at <paramref name="knownAt"/>.</summary>
    public bool IsInEffect(DateTimeOffset at, DateTimeOffset knownAt) => _periods.Any(p => p.Period.IsEffectiveAsOf(at, knownAt));

    /// <summary>
    /// When the validity known at <paramref name="knownAt"/> ended at or before <paramref name="at"/>, that end; null
    /// when it had not ended or never started. Names an expiry in a denial (K-AUTH-6).
    /// </summary>
    public DateTimeOffset? LapsedBy(DateTimeOffset at, DateTimeOffset knownAt) =>
        _periods.Where(p => p.Period.Recorded.Contains(knownAt) && p.Period.Valid.End is { } end && end <= at)
            .Select(p => p.Period.Valid.End)
            .Max();

    /// <summary>The qualification as a command result.</summary>
    public QualificationState State()
    {
        var validity = CurrentValidity;
        return new QualificationState(Id, Holder, Kind, Code, ProtocolVersionId, EvidenceArtifactVersionId, validity?.Start, validity?.End, validity is null);
    }

    /// <summary>The valid period from <paramref name="from"/> to <paramref name="to"/> at stored precision, or null when it is empty.</summary>
    internal static TimeRange? ValidityOf(DateTimeOffset from, DateTimeOffset? to)
    {
        var start = KernelTimestamps.ToStoredPrecision(from);
        var end = to is { } until ? KernelTimestamps.ToStoredPrecision(until) : (DateTimeOffset?)null;
        return end <= start ? null : new TimeRange(start, end);
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:_[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}

/// <summary>
/// One bitemporal fact about a qualification: it was valid during <see cref="TemporalPeriod.Valid"/>, as Cadence knew it
/// during <see cref="TemporalPeriod.Recorded"/>. Rows change only to close <c>recorded_period</c> (K-TIME-1).
/// </summary>
public class QualificationPeriod : IBitemporalEntity
{
    protected QualificationPeriod() { }

    internal QualificationPeriod(Qualification qualification, TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = qualification.TenantId;
        QualificationId = qualification.Id;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public QualificationId QualificationId { get; private set; }

    public TemporalPeriod Period { get; private set; }

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}
