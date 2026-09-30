using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// A rule of authorization step 3 (spec §9.3, K-AUTH-5): exercising <see cref="Capability"/> within
/// <see cref="Scope"/> needs a current qualification of <see cref="QualificationCode"/>, for
/// <see cref="ProtocolVersionId"/> when it names one. Its validity is bitemporal
/// (<see cref="QualificationRequirementPeriod"/>), so a decision re-evaluated as of an earlier instant applies the
/// requirements known then.
/// </summary>
public class QualificationRequirement : AggregateRoot<QualificationRequirementId>
{
    private readonly List<QualificationRequirementPeriod> _periods = [];

    protected QualificationRequirement() { }

    private QualificationRequirement(
        QualificationRequirementId id, TenantId tenantId, string capability, string qualificationCode, ProtocolVersionId? protocolVersionId,
        AuthorityScope scope, string definedBy)
        : base(id)
    {
        TenantId = tenantId;
        Capability = capability;
        QualificationCode = qualificationCode;
        ProtocolVersionId = protocolVersionId;
        ScopeLevel = scope.Level;
        ScopeTargetId = scope.TargetId;
        ScopeCountry = scope.Country;
        DefinedBy = definedBy;
    }

    public TenantId TenantId { get; private set; }

    /// <summary>The capability token the requirement gates.</summary>
    public string Capability { get; private set; } = string.Empty;

    /// <summary>The qualification code that satisfies the requirement.</summary>
    public string QualificationCode { get; private set; } = string.Empty;

    /// <summary>When set, only a qualification for this protocol version satisfies the requirement.</summary>
    public ProtocolVersionId? ProtocolVersionId { get; private set; }

    public ScopeLevel ScopeLevel { get; private set; }

    public Guid? ScopeTargetId { get; private set; }

    public string? ScopeCountry { get; private set; }

    /// <summary>The actor that defined the requirement (<see cref="Actor.ToString"/>).</summary>
    public string DefinedBy { get; private set; } = string.Empty;

    public AuthorityScope Scope => new(ScopeLevel, ScopeTargetId, ScopeCountry);

    /// <summary>The bitemporal validity history, oldest first.</summary>
    public IReadOnlyList<QualificationRequirementPeriod> Periods => _periods;

    /// <summary>The validity as currently known, or null once retired entirely.</summary>
    public TimeRange? CurrentValidity => _periods.SingleOrDefault(p => p.Period.Recorded.IsOpen)?.Period.Valid;

    /// <summary>Defines a requirement valid from <paramref name="validFrom"/> and known from <paramref name="recordedAt"/>.</summary>
    public static CommandResult<QualificationRequirement> Define(
        QualificationRequirementId id,
        TenantId tenantId,
        string? capability,
        string? qualificationCode,
        ProtocolVersionId? protocolVersionId,
        AuthorityScope? scope,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo,
        Actor definedBy,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(definedBy);
        if (!Authorization.Capability.IsWellFormed(capability))
        {
            return AuthorityErrors.CapabilityInvalid;
        }

        if (!Qualification.IsWellFormedCode(qualificationCode))
        {
            return QualificationErrors.CodeInvalid;
        }

        if (scope?.Problem() is { } problem)
        {
            return AuthorityErrors.ScopeInvalid.WithMessage(problem);
        }

        if (scope is null)
        {
            return AuthorityErrors.ScopeInvalid;
        }

        if (Qualification.ValidityOf(validFrom, validTo) is not { } valid)
        {
            return QualificationErrors.ValidityInvalid;
        }

        var requirement = new QualificationRequirement(id, tenantId, capability!, qualificationCode!, protocolVersionId, scope, definedBy.ToString());
        requirement._periods.Add(new QualificationRequirementPeriod(
            requirement, new TemporalPeriod(valid, TimeRange.From(KernelTimestamps.ToStoredPrecision(recordedAt)))));
        return CommandResult<QualificationRequirement>.Success(requirement);
    }

    /// <summary>Ends the requirement at <paramref name="effectiveAt"/>, as known from <paramref name="recordedAt"/>.</summary>
    public KernelError? Retire(DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
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
            _periods.Add(new QualificationRequirementPeriod(this, new TemporalPeriod(new TimeRange(current.Period.Valid.Start, at), TimeRange.From(known))));
        }

        return null;
    }

    /// <summary>True when the requirement applied at <paramref name="at"/> as known at <paramref name="knownAt"/>.</summary>
    public bool IsInEffect(DateTimeOffset at, DateTimeOffset knownAt) => _periods.Any(p => p.Period.IsEffectiveAsOf(at, knownAt));

    /// <summary>True when <paramref name="qualification"/> is of the code, and protocol version, this requirement names.</summary>
    public bool IsMetBy(Qualification qualification)
    {
        ArgumentNullException.ThrowIfNull(qualification);
        return string.Equals(qualification.Code, QualificationCode, StringComparison.Ordinal)
            && (ProtocolVersionId is null || qualification.ProtocolVersionId == ProtocolVersionId);
    }

    /// <summary>The requirement as a command result.</summary>
    public QualificationRequirementState State()
    {
        var validity = CurrentValidity;
        return new QualificationRequirementState(Id, Capability, QualificationCode, ProtocolVersionId, Scope, validity?.Start, validity?.End, validity is null);
    }
}

/// <summary>
/// One bitemporal fact about a qualification requirement. Rows change only to close <c>recorded_period</c> (K-TIME-1).
/// </summary>
public class QualificationRequirementPeriod : IBitemporalEntity
{
    protected QualificationRequirementPeriod() { }

    internal QualificationRequirementPeriod(QualificationRequirement requirement, TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = requirement.TenantId;
        RequirementId = requirement.Id;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public QualificationRequirementId RequirementId { get; private set; }

    public TemporalPeriod Period { get; private set; }

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}
