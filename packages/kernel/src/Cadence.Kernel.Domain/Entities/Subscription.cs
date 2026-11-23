using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Commercial subscription aggregate for a tenant (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Owns its bitemporal <see cref="SubscriptionPeriod"/> lifecycle history and bitemporal
/// <see cref="ProductEntitlement"/> revisions so tier switches, expirations, and scoped activations commit in one
/// atomic transaction without overwriting <c>CreatedBy</c>, <c>ReasonForChange</c>, <c>TrialEndsAt</c>, or <c>ExpiredAt</c>.
/// Never grants clinical data access or bypasses kernel authority, blinding, or consent.
/// </summary>
public class Subscription : AggregateRoot<SubscriptionId>
{
    private const long OneMicrosecondTicks = 10;

    private readonly List<SubscriptionPeriod> _periods = [];
    private readonly List<ProductEntitlement> _entitlements = [];

    protected Subscription() { }

    public Subscription(
        SubscriptionId id,
        TenantId tenantId,
        CommercialTier tier,
        SubscriptionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? trialEndsAt,
        string createdBy,
        string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;

        var start = KernelTimestamps.ToStoredPrecision(startedAt);
        var normalizedTrialEnds = trialEndsAt.HasValue ? KernelTimestamps.ToStoredPrecision(trialEndsAt.Value) : (DateTimeOffset?)null;
        var initialPeriod = new TemporalPeriod(TimeRange.From(start), TimeRange.From(start));

        _periods.Add(new SubscriptionPeriod(
            Guid.CreateVersion7(start),
            tenantId,
            id,
            tier,
            status,
            start,
            normalizedTrialEnds,
            expiredAt: status == SubscriptionStatus.Expired ? start : null,
            activatedAt: status == SubscriptionStatus.Active ? start : null,
            scoping: null,
            initialPeriod,
            createdBy,
            reasonForChange));

        foreach (var family in CommercialCatalog.FamiliesForTier(tier))
        {
            _entitlements.Add(new ProductEntitlement(
                ProductEntitlementId.New(start),
                tenantId,
                id,
                family,
                isEnabled: true,
                grantedAt: start,
                initialPeriod,
                createdBy,
                reasonForChange));
        }
    }

    public TenantId TenantId { get; private set; }

    /// <summary>Every bitemporal lifecycle revision of this subscription, including superseded ones.</summary>
    public IReadOnlyList<SubscriptionPeriod> Periods => _periods;

    /// <summary>Every bitemporal product-family entitlement revision under this subscription.</summary>
    public IReadOnlyList<ProductEntitlement> Entitlements => _entitlements;

    /// <summary>Currently open commercial period in recorded time.</summary>
    public SubscriptionPeriod CurrentPeriod =>
        _periods
            .Where(p => p.IsCurrent)
            .OrderByDescending(p => p.Period.Valid.Start)
            .FirstOrDefault()
        ?? throw new InvalidOperationException($"Subscription {Id} has no current period row; load it with its periods.");

    public CommercialTier Tier => CurrentPeriod.Tier;

    public SubscriptionStatus Status => CurrentPeriod.Status;

    public DateTimeOffset StartedAt => CurrentPeriod.StartedAt;

    public DateTimeOffset? TrialEndsAt => CurrentPeriod.TrialEndsAt;

    public DateTimeOffset? ExpiredAt => CurrentPeriod.ExpiredAt;

    public DateTimeOffset? ActivatedAt => CurrentPeriod.ActivatedAt;

    public bool IsExpiredAt(DateTimeOffset asOf) => CurrentPeriod.IsExpiredAt(asOf);

    public SubscriptionStatus EffectiveStatusAt(DateTimeOffset asOf) => CurrentPeriod.EffectiveStatusAt(asOf);

    public SubscriptionScoping? GetScoping() => CurrentPeriod.GetScoping();

    /// <summary>Currently enabled product families in recorded time.</summary>
    public IReadOnlyList<ProductFamily> GetEnabledProductFamilies() =>
        _entitlements
            .Where(e => e.IsCurrent && e.IsEnabled && e.Period.Valid.End is null)
            .Select(e => e.Family)
            .Distinct()
            .OrderBy(f => (int)f)
            .ToArray();

    /// <summary>Appends a bitemporal <see cref="SubscriptionStatus.Expired"/> revision without mutating prior rows.</summary>
    public void Expire(DateTimeOffset expiredAt, string actorId, string reasonForChange)
    {
        var current = CurrentPeriod;
        if (current.Status == SubscriptionStatus.Expired)
        {
            return;
        }

        var (effectiveAt, recordedAt) = NextInstants(current.Period, expiredAt);
        current.Supersede(recordedAt);
        if (effectiveAt > current.Period.Valid.Start)
        {
            _periods.Add(current.SliceBefore(effectiveAt, recordedAt));
        }

        _periods.Add(new SubscriptionPeriod(
            Guid.CreateVersion7(recordedAt),
            TenantId,
            Id,
            current.Tier,
            SubscriptionStatus.Expired,
            current.StartedAt,
            current.TrialEndsAt,
            expiredAt: current.ExpiredAt ?? KernelTimestamps.ToStoredPrecision(expiredAt),
            activatedAt: current.ActivatedAt,
            scoping: current.GetScoping(),
            new TemporalPeriod(TimeRange.From(effectiveAt), TimeRange.From(recordedAt)),
            actorId,
            reasonForChange));
    }

    /// <summary>
    /// Switches the subscription's commercial tier and synchronizes its <see cref="ProductEntitlement"/> revisions in one
    /// aggregate transition while preserving <see cref="TrialEndsAt"/>, <see cref="StartedAt"/>, and prior audit rows.
    /// </summary>
    public SubscriptionEntitlementDiff SwitchTier(
        CommercialTier tier,
        DateTimeOffset changedAt,
        string actorId,
        string reasonForChange)
    {
        var current = CurrentPeriod;
        var (effectiveAt, recordedAt) = NextInstants(current.Period, changedAt);

        if (current.Tier != tier)
        {
            current.Supersede(recordedAt);
            if (effectiveAt > current.Period.Valid.Start)
            {
                _periods.Add(current.SliceBefore(effectiveAt, recordedAt));
            }

            _periods.Add(new SubscriptionPeriod(
                Guid.CreateVersion7(recordedAt),
                TenantId,
                Id,
                tier,
                current.Status,
                current.StartedAt,
                current.TrialEndsAt,
                current.ExpiredAt,
                current.ActivatedAt,
                current.GetScoping(),
                new TemporalPeriod(TimeRange.From(effectiveAt), TimeRange.From(recordedAt)),
                actorId,
                reasonForChange));
        }

        return SyncProductFamilies(CommercialCatalog.FamiliesForTier(tier), changedAt, actorId, reasonForChange);
    }

    /// <summary>
    /// Activates the subscription with validated <paramref name="scoping"/> and synchronizes its
    /// <see cref="ProductEntitlement"/> revisions in one atomic aggregate transition.
    /// </summary>
    public SubscriptionEntitlementDiff Activate(
        CommercialTier tier,
        SubscriptionScoping scoping,
        DateTimeOffset activatedAt,
        string actorId,
        string reasonForChange)
    {
        ArgumentNullException.ThrowIfNull(scoping);
        var normalized = SubscriptionScopingValidator.Normalize(scoping);
        var current = CurrentPeriod;
        var (effectiveAt, recordedAt) = NextInstants(current.Period, activatedAt);
        var storedActivatedAt = KernelTimestamps.ToStoredPrecision(activatedAt);

        current.Supersede(recordedAt);
        if (effectiveAt > current.Period.Valid.Start)
        {
            _periods.Add(current.SliceBefore(effectiveAt, recordedAt));
        }

        _periods.Add(new SubscriptionPeriod(
            Guid.CreateVersion7(recordedAt),
            TenantId,
            Id,
            tier,
            SubscriptionStatus.Active,
            current.StartedAt,
            trialEndsAt: null,
            expiredAt: null,
            activatedAt: storedActivatedAt,
            scoping: normalized,
            new TemporalPeriod(TimeRange.From(effectiveAt), TimeRange.From(recordedAt)),
            actorId,
            reasonForChange));

        return SyncProductFamilies(CommercialCatalog.FamiliesForTier(tier), activatedAt, actorId, reasonForChange);
    }

    /// <summary>Grants or re-enables one <see cref="ProductFamily"/> bitemporally under this subscription.</summary>
    public (ProductEntitlementId Id, bool Granted) GrantFamily(
        ProductEntitlementId requestedId,
        ProductFamily family,
        DateTimeOffset grantedAt,
        string actorId,
        string reasonForChange)
    {
        var current = CurrentEntitlement(family);
        if (current is { IsEnabled: true })
        {
            return (current.Id, Granted: false);
        }

        var storedAt = KernelTimestamps.ToStoredPrecision(grantedAt);
        if (current is null)
        {
            var id = requestedId.IsEmpty ? ProductEntitlementId.New(storedAt) : requestedId;
            var created = new ProductEntitlement(
                id,
                TenantId,
                Id,
                family,
                isEnabled: true,
                grantedAt: storedAt,
                new TemporalPeriod(TimeRange.From(storedAt), TimeRange.From(storedAt)),
                actorId,
                reasonForChange);
            _entitlements.Add(created);
            return (created.Id, Granted: true);
        }

        var (effectiveAt, recordedAt) = NextInstants(current.Period, storedAt);
        current.Supersede(recordedAt);
        if (effectiveAt > current.Period.Valid.Start)
        {
            _entitlements.Add(SliceEntitlementBefore(current, effectiveAt, recordedAt));
        }

        var nextId = requestedId.IsEmpty || _entitlements.Any(e => e.Id == requestedId)
            ? ProductEntitlementId.New(recordedAt)
            : requestedId;
        var enabled = new ProductEntitlement(
            nextId,
            TenantId,
            Id,
            family,
            isEnabled: true,
            grantedAt: storedAt,
            new TemporalPeriod(TimeRange.From(effectiveAt), TimeRange.From(recordedAt)),
            actorId,
            reasonForChange);
        _entitlements.Add(enabled);
        return (enabled.Id, Granted: true);
    }

    /// <summary>Disables one <see cref="ProductFamily"/> bitemporally under this subscription.</summary>
    public ProductEntitlementId? RevokeFamily(
        ProductFamily family,
        DateTimeOffset revokedAt,
        string actorId,
        string reasonForChange)
    {
        var current = CurrentEntitlement(family);
        if (current is null || !current.IsEnabled)
        {
            return null;
        }

        var storedAt = KernelTimestamps.ToStoredPrecision(revokedAt);
        var (effectiveAt, recordedAt) = NextInstants(current.Period, storedAt);
        current.Supersede(recordedAt);
        if (effectiveAt > current.Period.Valid.Start)
        {
            _entitlements.Add(SliceEntitlementBefore(current, effectiveAt, recordedAt));
        }

        var disabled = new ProductEntitlement(
            ProductEntitlementId.New(recordedAt),
            TenantId,
            Id,
            family,
            isEnabled: false,
            grantedAt: current.GrantedAt,
            new TemporalPeriod(TimeRange.From(effectiveAt), TimeRange.From(recordedAt)),
            actorId,
            reasonForChange);
        _entitlements.Add(disabled);
        return disabled.Id;
    }

    private SubscriptionEntitlementDiff SyncProductFamilies(
        IReadOnlyList<ProductFamily> targetFamilies,
        DateTimeOffset changedAt,
        string actorId,
        string reasonForChange)
    {
        var granted = new List<(ProductEntitlementId Id, ProductFamily Family)>();
        var revoked = new List<(ProductEntitlementId Id, ProductFamily Family)>();

        foreach (var family in targetFamilies)
        {
            var (id, wasGranted) = GrantFamily(default, family, changedAt, actorId, reasonForChange);
            if (wasGranted)
            {
                granted.Add((id, family));
            }
        }

        var openFamilies = _entitlements
            .Where(e => e.IsCurrent && e.IsEnabled && e.Period.Valid.End is null)
            .Select(e => e.Family)
            .Distinct()
            .ToArray();
        foreach (var enabledFamily in openFamilies)
        {
            if (targetFamilies.Contains(enabledFamily))
            {
                continue;
            }

            if (RevokeFamily(enabledFamily, changedAt, actorId, reasonForChange) is { } revokedId)
            {
                revoked.Add((revokedId, enabledFamily));
            }
        }

        return new SubscriptionEntitlementDiff(granted, revoked);
    }

    private ProductEntitlement? CurrentEntitlement(ProductFamily family) =>
        _entitlements
            .Where(e => e.Family == family && e.IsCurrent && e.Period.Valid.End is null)
            .OrderByDescending(e => e.Period.Valid.Start)
            .FirstOrDefault();

    private static ProductEntitlement SliceEntitlementBefore(
        ProductEntitlement prior,
        DateTimeOffset effectiveAt,
        DateTimeOffset recordedAt) =>
        new(
            ProductEntitlementId.New(recordedAt),
            prior.TenantId,
            prior.SubscriptionId,
            prior.Family,
            prior.IsEnabled,
            prior.GrantedAt,
            new TemporalPeriod(new TimeRange(prior.Period.Valid.Start, effectiveAt), TimeRange.From(recordedAt)),
            prior.CreatedBy,
            prior.ReasonForChange);

    private static (DateTimeOffset EffectiveAt, DateTimeOffset RecordedAt) NextInstants(
        TemporalPeriod currentPeriod,
        DateTimeOffset requestedAt)
    {
        var storedRequested = KernelTimestamps.ToStoredPrecision(requestedAt);
        var minRecorded = currentPeriod.Recorded.Start.AddTicks(OneMicrosecondTicks);
        var recordedAt = storedRequested > minRecorded ? storedRequested : minRecorded;
        var effectiveAt = storedRequested > currentPeriod.Valid.Start ? storedRequested : currentPeriod.Valid.Start;
        return (effectiveAt, recordedAt);
    }
}

/// <summary>Product-family entitlement transitions produced during an atomic <see cref="Subscription"/> mutation.</summary>
public sealed record SubscriptionEntitlementDiff(
    IReadOnlyList<(ProductEntitlementId Id, ProductFamily Family)> Granted,
    IReadOnlyList<(ProductEntitlementId Id, ProductFamily Family)> Revoked);

/// <summary>
/// One bitemporal lifecycle fact about a <see cref="Subscription"/>: its commercial tier, status, trial/expiry timestamps,
/// scoping caps, and immutable actor/reason attribution during <see cref="TemporalPeriod.Valid"/> as known during
/// <see cref="TemporalPeriod.Recorded"/>. Guarded by K-TIME-1 (<c>KT001</c>) and K-TIME-2 (<c>subscription_id</c>).
/// </summary>
public class SubscriptionPeriod : IBitemporalEntity
{
    protected SubscriptionPeriod() { }

    internal SubscriptionPeriod(
        Guid id,
        TenantId tenantId,
        SubscriptionId subscriptionId,
        CommercialTier tier,
        SubscriptionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? trialEndsAt,
        DateTimeOffset? expiredAt,
        DateTimeOffset? activatedAt,
        SubscriptionScoping? scoping,
        TemporalPeriod period,
        string createdBy,
        string reasonForChange)
    {
        Id = id;
        TenantId = tenantId;
        SubscriptionId = subscriptionId;
        Tier = tier;
        Status = status;
        StartedAt = KernelTimestamps.ToStoredPrecision(startedAt);
        TrialEndsAt = trialEndsAt.HasValue ? KernelTimestamps.ToStoredPrecision(trialEndsAt.Value) : null;
        ExpiredAt = expiredAt.HasValue ? KernelTimestamps.ToStoredPrecision(expiredAt.Value) : null;
        ActivatedAt = activatedAt.HasValue ? KernelTimestamps.ToStoredPrecision(activatedAt.Value) : null;
        if (scoping is not null)
        {
            var normalized = SubscriptionScopingValidator.Normalize(scoping);
            StudyPhase = normalized.StudyPhase;
            StudyDurationMonths = normalized.StudyDurationMonths;
            CountriesCsv = string.Join(',', normalized.Countries);
            SiteCount = normalized.SiteCount;
            ParticipantCount = normalized.ParticipantCount;
            ActiveStudyCount = normalized.ActiveStudyCount;
            BillingConfirmation = normalized.BillingConfirmation;
        }

        Period = period;
        CreatedAt = period.Recorded.Start.UtcDateTime;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
        VersionIndex = 1;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public SubscriptionId SubscriptionId { get; private set; }

    public CommercialTier Tier { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? TrialEndsAt { get; private set; }

    public DateTimeOffset? ExpiredAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public string? StudyPhase { get; private set; }

    public int? StudyDurationMonths { get; private set; }

    public string? CountriesCsv { get; private set; }

    public int? SiteCount { get; private set; }

    public int? ParticipantCount { get; private set; }

    public int? ActiveStudyCount { get; private set; }

    public string? BillingConfirmation { get; private set; }

    public TemporalPeriod Period { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public string ReasonForChange { get; private set; } = string.Empty;

    public int VersionIndex { get; private set; } = 1;

    /// <summary>True when this row has not been superseded in recorded time.</summary>
    public bool IsCurrent => Period.Recorded.IsOpen;

    public bool IsExpiredAt(DateTimeOffset asOf)
    {
        var instant = KernelTimestamps.ToStoredPrecision(asOf);
        return Status == SubscriptionStatus.Expired
            || (Status == SubscriptionStatus.Trialing && TrialEndsAt is { } endsAt && endsAt <= instant);
    }

    public SubscriptionStatus EffectiveStatusAt(DateTimeOffset asOf) =>
        IsExpiredAt(asOf) ? SubscriptionStatus.Expired : Status;

    public SubscriptionScoping? GetScoping()
    {
        if (string.IsNullOrWhiteSpace(StudyPhase)
            || StudyDurationMonths is not { } duration
            || SiteCount is not { } sites
            || ParticipantCount is not { } participants
            || ActiveStudyCount is not { } studies
            || string.IsNullOrWhiteSpace(BillingConfirmation))
        {
            return null;
        }

        var countries = string.IsNullOrWhiteSpace(CountriesCsv)
            ? []
            : CountriesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new SubscriptionScoping(
            StudyPhase,
            duration,
            countries,
            sites,
            participants,
            studies,
            BillingConfirmation);
    }

    internal SubscriptionPeriod SliceBefore(DateTimeOffset effectiveAt, DateTimeOffset recordedAt) =>
        new(
            Guid.CreateVersion7(recordedAt),
            TenantId,
            SubscriptionId,
            Tier,
            Status,
            StartedAt,
            TrialEndsAt,
            ExpiredAt,
            ActivatedAt,
            GetScoping(),
            new TemporalPeriod(new TimeRange(Period.Valid.Start, effectiveAt), TimeRange.From(recordedAt)),
            CreatedBy,
            ReasonForChange);

    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}
