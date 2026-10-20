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
/// An actor's authority to exercise one capability, or a role's capabilities, within a scope (spec §9.3). A grant with
/// a <see cref="ParentGrantId"/> is a delegation, bounded by its parent. Its validity is bitemporal
/// (<see cref="GrantPeriod"/>): a revocation closes the recorded period of the current row and records the shortened
/// validity, so the grant can be evaluated as of any valid and known instant and a later grant or revocation never
/// changes what was known when an earlier decision was made (K-AUTH-3).
/// </summary>
public class Grant : AggregateRoot<GrantId>
{
    private readonly List<GrantPeriod> _periods = [];

    protected Grant() { }

    private Grant(GrantId id, TenantId tenantId, Actor grantee, string? capability, RoleId? roleId, AuthorityScope scope, bool withGrantOption, GrantId? parentGrantId, string issuedBy)
        : base(id)
    {
        TenantId = tenantId;
        GranteeKind = grantee.Kind;
        GranteeId = grantee.Id;
        Capability = capability;
        RoleId = roleId;
        ScopeLevel = scope.Level;
        ScopeTargetId = scope.TargetId;
        ScopeCountry = scope.Country;
        WithGrantOption = withGrantOption;
        ParentGrantId = parentGrantId;
        IssuedBy = issuedBy;
    }

    public TenantId TenantId { get; private set; }

    public ActorKind GranteeKind { get; private set; }

    public string GranteeId { get; private set; } = string.Empty;

    /// <summary>The capability granted, or null when the grant is of a role.</summary>
    public string? Capability { get; private set; }

    /// <summary>The role granted, or null when the grant is of one capability.</summary>
    public RoleId? RoleId { get; private set; }

    public ScopeLevel ScopeLevel { get; private set; }

    public Guid? ScopeTargetId { get; private set; }

    public string? ScopeCountry { get; private set; }

    public bool WithGrantOption { get; private set; }

    /// <summary>The grant this delegation was issued under, or null for a grant issued directly.</summary>
    public GrantId? ParentGrantId { get; private set; }

    /// <summary>The actor that issued or delegated the grant (<see cref="Actor.ToString"/>).</summary>
    public string IssuedBy { get; private set; } = string.Empty;

    public Actor Grantee => new(GranteeKind, GranteeId);

    public AuthorityScope Scope => new(ScopeLevel, ScopeTargetId, ScopeCountry);

    /// <summary>The bitemporal validity history, oldest first.</summary>
    public IReadOnlyList<GrantPeriod> Periods => _periods;

    /// <summary>The validity as currently known, or null once revoked entirely.</summary>
    public TimeRange? CurrentValidity => _periods.SingleOrDefault(p => p.Period.Recorded.IsOpen)?.Period.Valid;

    /// <summary>
    /// Issues a grant, directly or (with <paramref name="parentGrantId"/>) as a delegation, valid from
    /// <paramref name="validFrom"/> and known from <paramref name="recordedAt"/>. The handler checks the parent bounds.
    /// </summary>
    public static CommandResult<Grant> Issue(
        GrantId id,
        TenantId tenantId,
        Actor? grantee,
        string? capability,
        RoleId? roleId,
        AuthorityScope? scope,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo,
        bool withGrantOption,
        GrantId? parentGrantId,
        Actor issuedBy,
        DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(issuedBy);
        if (grantee is null || grantee.RequestedBy is not null || (capability is null) == (roleId is null))
        {
            return AuthorityErrors.GrantTargetInvalid;
        }

        if (capability is not null && !Authorization.Capability.IsWellFormed(capability))
        {
            return AuthorityErrors.CapabilityInvalid;
        }

        if (scope?.Problem() is { } problem)
        {
            return AuthorityErrors.ScopeInvalid.WithMessage(problem);
        }

        if (scope is null)
        {
            return AuthorityErrors.ScopeInvalid;
        }

        var from = KernelTimestamps.ToStoredPrecision(validFrom);
        var to = validTo is { } end ? KernelTimestamps.ToStoredPrecision(end) : (DateTimeOffset?)null;
        if (to <= from)
        {
            return AuthorityErrors.ValidityInvalid;
        }

        var grant = new Grant(id, tenantId, grantee, capability, roleId, scope, withGrantOption, parentGrantId, issuedBy.ToString());
        grant._periods.Add(new GrantPeriod(grant, new TemporalPeriod(new TimeRange(from, to), TimeRange.From(KernelTimestamps.ToStoredPrecision(recordedAt)))));
        return CommandResult<Grant>.Success(grant);
    }

    /// <summary>
    /// Ends the grant's validity at <paramref name="effectiveAt"/>, as known from <paramref name="recordedAt"/>. Refused
    /// when the grant is already revoked or already ends by then.
    /// </summary>
    public KernelError? Revoke(DateTimeOffset effectiveAt, DateTimeOffset recordedAt)
    {
        var current = _periods.SingleOrDefault(p => p.Period.Recorded.IsOpen);
        var at = KernelTimestamps.ToStoredPrecision(effectiveAt);
        if (current is null || (current.Period.Valid.End is { } end && at >= end))
        {
            return AuthorityErrors.RevocationInvalid;
        }

        var known = KernelTimestamps.ToStoredPrecision(recordedAt);
        current.Supersede(known);
        if (at > current.Period.Valid.Start)
        {
            _periods.Add(new GrantPeriod(this, new TemporalPeriod(new TimeRange(current.Period.Valid.Start, at), TimeRange.From(known))));
        }

        return null;
    }

    /// <summary>True when the grant was valid at <paramref name="at"/> as known at <paramref name="knownAt"/>.</summary>
    public bool IsInEffect(DateTimeOffset at, DateTimeOffset knownAt) => _periods.Any(p => p.Period.IsEffectiveAsOf(at, knownAt));

    /// <summary>The grant as a command result.</summary>
    public GrantState State()
    {
        var validity = CurrentValidity;
        return new GrantState(Id, Grantee, Capability, RoleId, Scope, ParentGrantId, WithGrantOption, validity?.Start, validity?.End, validity is null);
    }
}

/// <summary>
/// One bitemporal fact about a grant: it was valid during <see cref="TemporalPeriod.Valid"/>, as Cadence knew it during
/// <see cref="TemporalPeriod.Recorded"/>. Rows change only to close <c>recorded_period</c> (K-TIME-1).
/// </summary>
public class GrantPeriod : IBitemporalEntity
{
    protected GrantPeriod() { }

    internal GrantPeriod(Grant grant, TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = grant.TenantId;
        GrantId = grant.Id;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public GrantId GrantId { get; private set; }

    public TemporalPeriod Period { get; private set; }

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}
